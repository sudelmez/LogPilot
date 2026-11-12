using LogPilot.AI.Chat;
using LogPilot.AI.Incidents;
using LogPilot.AI.Orchestration;
using LogPilot.AI.Providers;
using LogPilot.AI.Providers.Claude;
using LogPilot.AI.Providers.Ollama;
using LogPilot.AI.Security;
using LogPilot.AI.Tools;
using LogPilot.Application.Incidents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LogPilot.AI;

public static class DependencyInjection
{
    public static IServiceCollection AddAi(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<AiOptions>()
            .Bind(config.GetSection(AiOptions.SectionName))
            .Validate(o => IsKnownProvider(o.Provider),
                "Ai:Provider must be 'Mock', 'Ollama' or 'Claude'.")
            .Validate(o => !Is(o.Provider, "ollama") || o.Ollama.IsValid(),
                "Ai:Provider is 'Ollama' but the Ollama settings are invalid. Check Ai:Ollama:BaseUrl (http://...), Model, " +
                "ContextTokens (2048-131072), TimeoutSeconds (10-1800) and Temperature (0-2).")
            .Validate(o => !IsClaude(o.Provider) || o.Claude.IsValid(),
                "Ai:Provider is 'Claude' but the Claude settings are invalid. Set ANTHROPIC_API_KEY (e.g. in .env), " +
                "and check Ai:Claude:Model, ChatEffort/SummaryEffort (low|medium|high|xhigh|max), MaxTokens (1024-64000) and TimeoutSeconds (10-600).")
            .ValidateOnStart();   // yanlış ayarla açılmak yerine açılışta net hata

        services.AddSingleton<PromptInjectionGuard>();

        services.AddSingleton<ILlmProvider>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AiOptions>>();
            var provider = options.Value.Provider;
            if (IsClaude(provider)) return ActivatorUtilities.CreateInstance<ClaudeLlmProvider>(sp);
            if (Is(provider, "ollama")) return new OllamaLlmProvider(options, sp.GetRequiredService<ILogger<OllamaLlmProvider>>());
            return ActivatorUtilities.CreateInstance<MockLlmProvider>(sp);
        });

        services.AddScoped<ToolRegistry>();
        services.AddScoped<ChatOrchestrator>();
        services.AddScoped<ChatService>();
        services.AddScoped<IIncidentSummarizer, LlmIncidentSummarizer>();
        return services;
    }

    private static bool Is(string? provider, string name) => string.Equals(provider?.Trim(), name, StringComparison.OrdinalIgnoreCase);

    private static bool IsClaude(string? provider) => Is(provider, "claude");

    private static bool IsKnownProvider(string? provider) =>
        IsClaude(provider) || Is(provider, "mock") || Is(provider, "ollama");
}
