namespace LogPilot.AI;

public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// "Mock" (varsayılan, anahtar gerekmez), "Ollama" (yerel model, ücretsiz) veya "Claude" (ücretli API).
    /// Yeni sağlayıcı: ILlmProvider uygula, DependencyInjection'a ekle.
    public string Provider { get; set; } = "Mock";

    public ClaudeOptions Claude { get; set; } = new();

    public OllamaOptions Ollama { get; set; } = new();
}

public sealed class OllamaOptions
{
    /// Yerelde: http://localhost:11434. Docker'daki API için: http://host.docker.internal:11434 (Ollama Mac'te çalışır).
    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// Araç çağırmayı destekleyen bir model olmalı. Önce indir: ollama pull &lt;model&gt;
    public string Model { get; set; } = "qwen3:8b";

    /// Bağlam penceresi. Araç sonuçları (en fazla 50 log) sığmalı; büyüdükçe RAM kullanımı artar.
    public int ContextTokens { get; set; } = 16384;

    /// Tek model çağrısı için. Yerel model yavaş olabilir.
    public int TimeoutSeconds { get; set; } = 300;

    /// Düşünen modellerde (ör. qwen3) uzun iç düşünmeyi aç/kapat. Kapalı = çok daha hızlı.
    public bool Think { get; set; }

    /// Model son kullanımdan sonra ne kadar bellekte kalsın (ör. "10m"; "0" = hemen boşalt).
    public string KeepAlive { get; set; } = "10m";

    /// Düşük = daha tutarlı, kurallara daha sadık cevaplar.
    public double Temperature { get; set; } = 0.2;

    public bool IsValid() =>
        Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https")
        && !string.IsNullOrWhiteSpace(Model)
        && ContextTokens is >= 2048 and <= 131072
        && TimeoutSeconds is >= 10 and <= 1800
        && Temperature is >= 0 and <= 2;
}

public sealed class ClaudeOptions
{
    /// Boşsa ANTHROPIC_API_KEY ortam değişkeni kullanılır. Anahtarı asla appsettings.json'a yazma:
    /// .env (Docker / start-local.sh) veya ortam değişkeni kullan.
    public string? ApiKey { get; set; }

    public string Model { get; set; } = "claude-opus-5-5";

    /// Sohbet (araç kullanan analiz) için düşünme derinliği: low | medium | high | xhigh | max.
    public string ChatEffort { get; set; } = "medium";

    /// Incident özeti (kısa, araçsız) için.
    public string SummaryEffort { get; set; } = "low";

    public int MaxTokens { get; set; } = 16000;

    /// Tek bir model çağrısının zaman aşımı (SDK kendi yeniden denemelerini bunun içinde yapar).
    public int TimeoutSeconds { get; set; } = 90;

    /// Model bir isteği güvenlik sınıflandırıcısıyla reddederse Anthropic aynı isteği uygun bir modelle yeniden dener.
    public bool UseRefusalFallback { get; set; } = true;

    public static readonly string[] Efforts = ["low", "medium", "high", "xhigh", "max"];

    public string? ResolveApiKey() =>
        string.IsNullOrWhiteSpace(ApiKey) ? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") : ApiKey;

    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(ResolveApiKey())
        && !string.IsNullOrWhiteSpace(Model)
        && Efforts.Contains(ChatEffort) && Efforts.Contains(SummaryEffort)
        && MaxTokens is >= 1024 and <= 64000
        && TimeoutSeconds is >= 10 and <= 600;
}
