using LogPilot.Application.Ai;
using LogPilot.Application.Analytics;
using LogPilot.Application.Incidents;
using LogPilot.Application.Knowledge;
using LogPilot.Application.Logs;
using LogPilot.Application.Scenarios;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LogPilot.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);   // testlerde sahte saat verilebilsin
        services.AddSingleton<SensitiveDataMasker>();
        services.AddScoped<LogService>();
        services.AddScoped<AnalyticsService>();
        services.AddScoped<ScenarioService>();
        services.AddScoped<KnowledgeService>();
        services.AddScoped<IncidentService>();
        services.AddScoped<IncidentDetectionService>();
        services.AddScoped<ILogAnalysisTools, LogAnalysisTools>();
        return services;
    }
}
