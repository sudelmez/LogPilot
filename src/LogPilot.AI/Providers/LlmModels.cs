namespace LogPilot.AI.Providers;

public enum LlmRole { System, User, Assistant, Tool }

public enum LlmPurpose { Chat, IncidentSummary }

public sealed record LlmToolCall(string Id, string Name, string ArgumentsJson);

/// ProviderState: sağlayıcının kendi turu için sakladığı opak veri (ör. Claude'un düşünce blokları).
/// Orkestratör içeriğine bakmaz; asistan mesajıyla birlikte aynı sağlayıcıya geri verir.
public sealed record LlmMessage(
    LlmRole Role,
    string Content,
    string? ToolCallId = null,
    string? ToolName = null,
    IReadOnlyList<LlmToolCall>? ToolCalls = null,
    object? ProviderState = null);

public sealed record LlmToolDefinition(string Name, string Description, string ParametersJsonSchema);

public sealed record LlmRequest(
    string SystemPrompt,
    IReadOnlyList<LlmMessage> Messages,
    IReadOnlyList<LlmToolDefinition> Tools,
    LlmPurpose Purpose = LlmPurpose.Chat);

/// Ya araç çağrıları (ToolCalls dolu) ya da nihai metin (Text). Chat için nihai metin tek bir JSON nesnesidir.
public sealed record LlmResponse(string? Text, IReadOnlyList<LlmToolCall> ToolCalls, object? ProviderState = null)
{
    public static LlmResponse FromText(string text) => new(text, []);
}

/// Claude, OpenAI veya yerel bir model eklemek için tek yapılması gereken: bu arayüzü uygulamak ve AddAi içinde kaydetmek.
public interface ILlmProvider
{
    string Name { get; }
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct);
}

/// Model servisine ulaşılamadı veya istek reddedildi (anahtar hatalı, kota, ağ, servis hatası).
/// API bunu 503 olarak döner; ayrıntı loglanır, kullanıcıya sade bir mesaj gider.
public sealed class LlmUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// Incident özeti için LLM'e verilen YAPILANDIRILMIŞ olgular (serbest metin yok).
public sealed record IncidentFacts(
    string ErrorCode, string Severity, long LogCount,
    IReadOnlyList<string> UserCodes, IReadOnlyList<string> Services,
    DateTime FirstSeen, DateTime LastSeen);
