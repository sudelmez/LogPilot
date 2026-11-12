using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LogPilot.AI.Providers.Ollama;

/// Bilgisayarda çalışan yerel model (Ollama, https://ollama.com). Ücretsiz, anahtar gerektirmez, veri dışarı çıkmaz.
/// Ollama'nın /api/chat ucu araç çağırmayı destekler; model de araç destekli olmalı (ör. qwen3, llama3.1).
/// Orkestratör, doğrulama, güven hesabı ve maskeleme diğer sağlayıcılarla aynıdır.
public sealed partial class OllamaLlmProvider : ILlmProvider, IDisposable
{
    private readonly HttpClient http;
    private readonly OllamaOptions options;
    private readonly ILogger<OllamaLlmProvider> logger;
    private int callCounter;

    public OllamaLlmProvider(IOptions<AiOptions> aiOptions, ILogger<OllamaLlmProvider> logger)
        : this(aiOptions, logger, new HttpClientHandler()) { }

    /// Testler için: sahte bir HTTP işleyicisi verilebilir.
    public OllamaLlmProvider(IOptions<AiOptions> aiOptions, ILogger<OllamaLlmProvider> logger, HttpMessageHandler handler)
    {
        options = aiOptions.Value.Ollama;
        this.logger = logger;
        http = new HttpClient(handler)
        {
            BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds),
        };
    }

    public string Name => $"ollama:{options.Model}";

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        var body = BuildRequest(request, options);
        var watch = Stopwatch.StartNew();

        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync("api/chat", body, ct);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Ollama did not answer within {Seconds}s.", options.TimeoutSeconds);
            throw new LlmUnavailableException(
                $"The local model did not answer within {options.TimeoutSeconds} seconds. Try a smaller model or a shorter time range.", ex);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Could not reach Ollama at {BaseUrl}.", options.BaseUrl);
            throw new LlmUnavailableException(
                $"The local AI (Ollama) could not be reached at {options.BaseUrl}. Is Ollama running? Start it with: ollama serve", ex);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("Ollama returned {Status}: {Body}", (int)response.StatusCode, Truncate(text, 500));
                if (response.StatusCode == HttpStatusCode.NotFound && text.Contains("not found", StringComparison.OrdinalIgnoreCase))
                    throw new LlmUnavailableException(
                        $"The model '{options.Model}' is not installed in Ollama. Install it with: ollama pull {options.Model}");
                throw new LlmUnavailableException($"The local AI (Ollama) returned an error ({(int)response.StatusCode}).");
            }

            var json = JsonNode.Parse(text) ?? throw new LlmUnavailableException("The local AI returned an empty response.");
            var result = ParseResponse(json, () => $"call_{Interlocked.Increment(ref callCounter)}");

            logger.LogInformation(
                "Ollama {Purpose}: {Ms} ms, prompt={Prompt} tokens, output={Output} tokens, toolCalls={ToolCalls}",
                request.Purpose, watch.ElapsedMilliseconds,
                json["prompt_eval_count"]?.GetValue<long>(), json["eval_count"]?.GetValue<long>(), result.ToolCalls.Count);

            if (json["prompt_eval_count"]?.GetValue<long>() is { } prompt && prompt >= options.ContextTokens)
                logger.LogWarning("The prompt filled the whole context window ({Context} tokens); older content was cut. " +
                                  "Raise Ai:Ollama:ContextTokens or narrow the question.", options.ContextTokens);

            return result;
        }
    }

    /// /api/chat istek gövdesi (ağ yok; testlerde doğrulanır).
    public static JsonObject BuildRequest(LlmRequest request, OllamaOptions options)
    {
        var messages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = request.SystemPrompt } };
        foreach (var m in request.Messages)
            if (ToMessage(m) is { } node) messages.Add(node);

        var body = new JsonObject
        {
            ["model"] = options.Model,
            ["messages"] = messages,
            ["stream"] = false,
            ["think"] = options.Think,
            ["keep_alive"] = options.KeepAlive,
            ["options"] = new JsonObject
            {
                ["num_ctx"] = options.ContextTokens,
                ["temperature"] = options.Temperature,
            },
        };

        if (request.Tools.Count > 0)
        {
            body["tools"] = new JsonArray(request.Tools.Select(t => (JsonNode)new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["parameters"] = JsonNode.Parse(t.ParametersJsonSchema),
                },
            }).ToArray());
        }
        else if (request.Purpose == LlmPurpose.Chat)
        {
            // Araç yokken beklenen tek şey JSON cevap: küçük modellerin biçimi bozmasını önler.
            body["format"] = "json";
        }

        return body;
    }

    private static JsonObject? ToMessage(LlmMessage m) => m.Role switch
    {
        LlmRole.User => new JsonObject { ["role"] = "user", ["content"] = m.Content },
        LlmRole.Tool => new JsonObject { ["role"] = "tool", ["content"] = m.Content, ["tool_name"] = m.ToolName },
        LlmRole.Assistant => Assistant(m),
        _ => null,   // sistem talimatı en başta ayrıca gider
    };

    private static JsonObject Assistant(LlmMessage m)
    {
        var node = new JsonObject { ["role"] = "assistant", ["content"] = m.Content };
        if (m.ToolCalls is { Count: > 0 } calls)
        {
            node["tool_calls"] = new JsonArray(calls.Select(c => (JsonNode)new JsonObject
            {
                ["function"] = new JsonObject
                {
                    ["name"] = c.Name,
                    ["arguments"] = string.IsNullOrWhiteSpace(c.ArgumentsJson) ? new JsonObject() : JsonNode.Parse(c.ArgumentsJson),
                },
            }).ToArray());
        }
        return node;
    }

    /// Ollama cevabı → LogPilot modeli. Araç çağrılarının kimliği yoksa üretilir (orkestratör eşleştirmede kullanır).
    public static LlmResponse ParseResponse(JsonNode json, Func<string> newId)
    {
        var message = json["message"];
        var content = message?["content"]?.GetValue<string>();
        if (content is not null) content = ThinkBlock().Replace(content, "").Trim();

        var calls = new List<LlmToolCall>();
        if (message?["tool_calls"] is JsonArray toolCalls)
        {
            foreach (var call in toolCalls)
            {
                var function = call?["function"];
                var name = function?["name"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(name)) continue;

                var arguments = function!["arguments"] switch
                {
                    null => "{}",
                    JsonValue v when v.TryGetValue<string>(out var s) => string.IsNullOrWhiteSpace(s) ? "{}" : s,
                    var other => other.ToJsonString(),
                };
                var id = call?["id"]?.GetValue<string>() is { Length: > 0 } given ? given : newId();
                calls.Add(new LlmToolCall(id, name, arguments));
            }
        }

        return new LlmResponse(string.IsNullOrEmpty(content) ? null : content, calls);
    }

    [GeneratedRegex(@"<think>.*?</think>", RegexOptions.Singleline)]
    private static partial Regex ThinkBlock();

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    public void Dispose() => http.Dispose();
}
