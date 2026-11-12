using System.Text.Json;
using Anthropic.Models.Beta.Messages;

namespace LogPilot.AI.Providers.Claude;

/// LogPilot'un sağlayıcıdan bağımsız mesaj modelini Claude Messages API tiplerine çevirir (ve geri).
/// Saf fonksiyonlar: ağ yok, test edilebilir.
public static class ClaudeMessageMapper
{
    /// Araç tanımları. Son araca önbellek işareti konur: araç listesi her istekte aynı olduğu için
    /// (render sırası: tools → system → messages) en kararlı önek budur.
    public static List<BetaToolUnion> Tools(IReadOnlyList<LlmToolDefinition> definitions)
    {
        var tools = new List<BetaToolUnion>(definitions.Count);
        for (var i = 0; i < definitions.Count; i++)
        {
            var d = definitions[i];
            using var schema = JsonDocument.Parse(d.ParametersJsonSchema);
            var root = schema.RootElement;

            var properties = new Dictionary<string, JsonElement>();
            if (root.TryGetProperty("properties", out var props))
                foreach (var p in props.EnumerateObject())
                    properties[p.Name] = p.Value.Clone();

            var required = root.TryGetProperty("required", out var req)
                ? req.EnumerateArray().Select(r => r.GetString()!).ToList()
                : [];

            tools.Add(new BetaTool
            {
                Name = d.Name,
                Description = d.Description,
                InputSchema = new()
                {
                    Properties = properties,
                    Required = required,
                },
                CacheControl = i == definitions.Count - 1 ? new BetaCacheControlEphemeral() : null,
            });
        }
        return tools;
    }

    /// Mesaj geçmişi. Ardışık araç sonuçları TEK bir user mesajında toplanır (API kuralı:
    /// her tool_use'a karşılık gelen tool_result'lar bir sonraki user turunda birlikte verilir).
    public static List<BetaMessageParam> Messages(IReadOnlyList<LlmMessage> messages)
    {
        var result = new List<BetaMessageParam>();
        List<BetaContentBlockParam>? pendingToolResults = null;

        void FlushToolResults()
        {
            if (pendingToolResults is null) return;
            result.Add(new BetaUserMessageParam { Content = pendingToolResults });
            pendingToolResults = null;
        }

        foreach (var m in messages)
        {
            switch (m.Role)
            {
                case LlmRole.Tool:
                    pendingToolResults ??= [];
                    pendingToolResults.Add(new BetaToolResultBlockParam
                    {
                        ToolUseID = m.ToolCallId ?? throw new InvalidOperationException("Tool message without ToolCallId."),
                        Content = m.Content,
                        IsError = m.Content.StartsWith("Tool error:", StringComparison.Ordinal),
                    });
                    break;

                case LlmRole.Assistant:
                    FlushToolResults();
                    result.Add(Assistant(m));
                    break;

                case LlmRole.User:
                    FlushToolResults();
                    result.Add(new BetaUserMessageParam { Content = m.Content });
                    break;

                case LlmRole.System:
                    // Sistem talimatı ayrı alanda gider; mesaj listesinde sistem rolü kullanılmaz.
                    break;
            }
        }

        FlushToolResults();
        return result;
    }

    /// Asistan turu. Claude'un kendi cevabından gelen bloklar (düşünce blokları dahil) varsa aynen geri verilir:
    /// araç döngüsünde düşünce bloklarının değiştirilmeden iletilmesi gerekir.
    private static BetaAssistantMessageParam Assistant(LlmMessage m)
    {
        if (m.ProviderState is ClaudeTurnState state)
            return new BetaAssistantMessageParam { Content = state.AssistantBlocks.ToList() };

        if (m.ToolCalls is { Count: > 0 } calls)
        {
            var blocks = new List<BetaContentBlockParam>();
            if (!string.IsNullOrWhiteSpace(m.Content))
                blocks.Add(new BetaTextBlockParam { Text = m.Content });
            foreach (var call in calls)
                blocks.Add(new BetaToolUseBlockParam { ID = call.Id, Name = call.Name, Input = ParseArguments(call.ArgumentsJson) });
            return new BetaAssistantMessageParam { Content = blocks };
        }

        return new BetaAssistantMessageParam { Content = string.IsNullOrEmpty(m.Content) ? "(no content)" : m.Content };
    }

    /// Claude'un cevabını LogPilot modeline çevirir. Asistan blokları bir sonraki tur için ProviderState'te saklanır.
    public static LlmResponse ToResponse(BetaMessage message)
    {
        var text = new List<string>();
        var calls = new List<LlmToolCall>();
        var echo = new List<BetaContentBlockParam>();

        foreach (var block in message.Content)
        {
            if (block.TryPickText(out var t))
            {
                text.Add(t.Text);
                echo.Add(new BetaTextBlockParam { Text = t.Text });
            }
            else if (block.TryPickThinking(out var thinking))
            {
                echo.Add(new BetaThinkingBlockParam { Thinking = thinking.Thinking, Signature = thinking.Signature });
            }
            else if (block.TryPickRedactedThinking(out var redacted))
            {
                echo.Add(new BetaRedactedThinkingBlockParam { Data = redacted.Data });
            }
            else if (block.TryPickToolUse(out var toolUse))
            {
                calls.Add(new LlmToolCall(toolUse.ID, toolUse.Name, JsonSerializer.Serialize(toolUse.Input)));
                echo.Add(new BetaToolUseBlockParam { ID = toolUse.ID, Name = toolUse.Name, Input = toolUse.Input });
            }
        }

        var joined = text.Count == 0 ? null : string.Join("\n", text);
        return new LlmResponse(joined, calls, new ClaudeTurnState(echo));
    }

    public static IReadOnlyDictionary<string, JsonElement> ParseArguments(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, JsonElement>();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.ValueKind == JsonValueKind.Object
            ? doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone())
            : new Dictionary<string, JsonElement>();
    }
}

/// Bir Claude asistan turunun, bir sonraki istekte aynen geri gönderilecek blokları.
public sealed record ClaudeTurnState(IReadOnlyList<BetaContentBlockParam> AssistantBlocks);
