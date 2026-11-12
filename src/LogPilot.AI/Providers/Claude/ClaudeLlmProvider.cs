using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LogPilot.AI.Providers.Claude;

/// Anthropic Claude (Messages API, araç kullanımı). Orkestratör, doğrulama, güven hesabı ve maskeleme
/// sağlayıcıdan bağımsızdır: Claude'un cevabı Mock'unkiyle aynı doğrulama hattından geçer.
///
/// - Düşünme: Opus 5.5'te her zaman açık (uyarlamalı); derinliği "effort" belirler.
/// - Önbellek: araç tanımları ve konuşma öneki önbelleğe alınır (aynı sohbet turundaki araç döngüsü ucuzlar).
/// - Red (refusal): sunucu tarafı yedek model (fallback) varsayılan açık.
public sealed class ClaudeLlmProvider : ILlmProvider, IDisposable
{
    private readonly AnthropicClient client;
    private readonly ClaudeOptions options;
    private readonly ILogger<ClaudeLlmProvider> logger;

    public ClaudeLlmProvider(IOptions<AiOptions> aiOptions, ILogger<ClaudeLlmProvider> logger)
    {
        options = aiOptions.Value.Claude;
        this.logger = logger;
        client = new AnthropicClient
        {
            ApiKey = options.ResolveApiKey(),
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds),
        };
    }

    public string Name => $"claude:{options.Model}";

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        var parameters = BuildParameters(request, options);

        BetaMessage message;
        try
        {
            message = await client.Beta.Messages.Create(parameters, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (AnthropicUnauthorizedException ex)
        {
            logger.LogError(ex, "Claude rejected the API key.");
            throw new LlmUnavailableException("The AI service rejected the API key. Check ANTHROPIC_API_KEY.", ex);
        }
        catch (AnthropicRateLimitException ex)
        {
            logger.LogWarning(ex, "Claude rate limit reached.");
            throw new LlmUnavailableException("The AI service is busy (rate limit). Please try again in a moment.", ex);
        }
        catch (AnthropicBadRequestException ex)
        {
            // Bizim istek şeklimizde hata: kullanıcıya ayrıntı gösterme, logla.
            logger.LogError(ex, "Claude rejected the request as invalid.");
            throw new LlmUnavailableException("The AI service rejected the request. See the API logs for details.", ex);
        }
        catch (AnthropicApiException ex)
        {
            logger.LogError(ex, "Claude API error.");
            throw new LlmUnavailableException("The AI service returned an error. Please try again.", ex);
        }
        catch (AnthropicIOException ex)
        {
            logger.LogError(ex, "Could not reach the Claude API.");
            throw new LlmUnavailableException("The AI service could not be reached. Check the network connection.", ex);
        }

        var usage = message.Usage;
        logger.LogInformation(
            "Claude {Purpose}: stop={StopReason}, input={Input}, output={Output}, cacheRead={CacheRead}, cacheWrite={CacheWrite}",
            request.Purpose, message.StopReason, usage.InputTokens, usage.OutputTokens,
            usage.CacheReadInputTokens, usage.CacheCreationInputTokens);

        if (IsStopReason(message, "refusal"))
        {
            logger.LogWarning("Claude declined the request ({Purpose}).", request.Purpose);
            return LlmResponse.FromText(request.Purpose == LlmPurpose.Chat ? RefusalAnswer : "");
        }

        if (IsStopReason(message, "max_tokens"))
            logger.LogWarning("Claude hit max_tokens ({MaxTokens}); the answer may be cut off.", options.MaxTokens);

        return ClaudeMessageMapper.ToResponse(message);
    }

    /// İstek gövdesi (ağ yok; testlerde JSON biçimi doğrulanır).
    public static MessageCreateParams BuildParameters(LlmRequest request, ClaudeOptions options)
    {
        var isChat = request.Purpose == LlmPurpose.Chat;
        var effort = isChat ? options.ChatEffort : options.SummaryEffort;

        return new MessageCreateParams
        {
            Model = options.Model,
            MaxTokens = isChat ? options.MaxTokens : 1024,
            System = new List<BetaTextBlockParam> { new() { Text = request.SystemPrompt } },
            Messages = ClaudeMessageMapper.Messages(request.Messages),
            Tools = request.Tools.Count > 0 ? ClaudeMessageMapper.Tools(request.Tools) : null,
            OutputConfig = new BetaOutputConfig { Effort = ParseEffort(effort) },
            // Otomatik önbellek: konuşmanın son bloğuna kadar olan önek. Araç döngüsünde her yeni çağrı
            // bir öncekinin önekini yeniden kullanır.
            CacheControl = new BetaCacheControlEphemeral(),
            Betas = options.UseRefusalFallback ? [AnthropicBeta.ServerSideFallback2026_07_01] : null,
            Fallbacks = options.UseRefusalFallback ? (BetaFallbacksParam)new Default() : null,   // "default": reddin kategorisine göre uygun model
        };
    }

    private static Effort ParseEffort(string value) => value switch
    {
        "low" => Effort.Low,
        "medium" => Effort.Medium,
        "high" => Effort.High,
        "xhigh" => Effort.Xhigh,
        "max" => Effort.Max,
        _ => Effort.Medium,
    };

    private static bool IsStopReason(BetaMessage message, string reason) =>
        string.Equals(message.StopReason?.ToString(), reason, StringComparison.OrdinalIgnoreCase)
        || JsonSerializer.Serialize(message.StopReason).Trim('"') == reason;

    private const string RefusalAnswer = """
        {"summary":"The AI model declined to answer this request.","probableCause":null,"probableCauseCode":null,
         "evidenceLogIds":[],"affectedServices":[],"suggestedActions":["Rephrase the question to focus on the log data."],
         "dataLimitations":["The language model refused this request."],"clarifyingQuestion":null}
        """;

    public void Dispose() => client.Dispose();
}
