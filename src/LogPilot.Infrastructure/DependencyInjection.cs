using LogPilot.Application.Analytics;
using LogPilot.Application.Chat;
using LogPilot.Application.Incidents;
using LogPilot.Application.Knowledge;
using LogPilot.Application.Logs;
using LogPilot.Application.Notifications;
using LogPilot.Application.Purchases;
using LogPilot.Application.Quotes;
using LogPilot.Application.Scenarios;
using LogPilot.Application.Users;
using LogPilot.Infrastructure.Mongo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace LogPilot.Infrastructure;

public static class DependencyInjection
{
    /// AI araçlarının salt-okunur MongoDB bağlantısı için DI anahtarı.
    public const string AiReaderKey = "ai-reader";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<MongoOptions>()
            .Bind(config.GetSection(MongoOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString), "Mongo:ConnectionString is required.")
            .Validate(o => o.LogRetentionHours is >= 1 and <= 24 * 30, "Mongo:LogRetentionHours must be between 1 and 720.")
            .ValidateOnStart();

        // MongoClient thread-safe ve pahalı: uygulama boyunca tek örnek.
        services.AddSingleton<IMongoClient>(sp =>
        {
            var o = sp.GetRequiredService<IOptions<MongoOptions>>().Value;
            var settings = MongoClientSettings.FromConnectionString(o.ConnectionString);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);   // Mongo kapalıysa hızlı hata
            return new MongoClient(settings);
        });

        services.AddSingleton<MongoContext>();

        // ---- AI okuyucu: ayrı (salt-okunur) MongoDB hesabı. Okuma arayüzleri (I*Reader) BU bağlama bağlanır. ----
        services.AddKeyedSingleton<IMongoClient>(AiReaderKey, (sp, _) =>
        {
            var o = sp.GetRequiredService<IOptions<MongoOptions>>().Value;
            if (string.IsNullOrWhiteSpace(o.ReadOnlyConnectionString)) return sp.GetRequiredService<IMongoClient>();
            var settings = MongoClientSettings.FromConnectionString(o.ReadOnlyConnectionString);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
            return new MongoClient(settings);
        });
        services.AddKeyedSingleton<MongoContext>(AiReaderKey, (sp, _) => new MongoContext(
            sp.GetRequiredKeyedService<IMongoClient>(AiReaderKey), sp.GetRequiredService<IOptions<MongoOptions>>().Value));
        static MongoContext Reader(IServiceProvider sp) => sp.GetRequiredKeyedService<MongoContext>(AiReaderKey);

        services.AddSingleton<ILogReader>(sp => new MongoLogRepository(Reader(sp)));
        services.AddSingleton<IQuoteLogReader>(sp => new MongoQuoteLogRepository(Reader(sp)));
        services.AddSingleton<ISmsLogReader>(sp => new MongoSmsLogRepository(Reader(sp)));
        services.AddSingleton<IPurchaseLogReader>(sp => new MongoPurchaseLogRepository(Reader(sp)));
        services.AddSingleton<IUserReader>(sp => new MongoUserRepository(Reader(sp)));
        services.AddSingleton<IIncidentReader>(sp => new MongoIncidentRepository(Reader(sp)));
        services.AddSingleton<IKnowledgeReader>(sp => new MongoKnowledgeRepository(Reader(sp)));
        // Analitik yalnızca okuma (aggregate) yapar: dashboard ve AI aynı salt-okunur hesabı kullanır.
        services.AddSingleton<ILogAnalyticsRepository>(sp => new MongoLogAnalyticsRepository(Reader(sp)));

        services.AddSingleton<ILogRepository, MongoLogRepository>();
        services.AddSingleton<IQuoteLogRepository, MongoQuoteLogRepository>();
        services.AddSingleton<ISmsLogRepository, MongoSmsLogRepository>();
        services.AddSingleton<IPurchaseLogRepository, MongoPurchaseLogRepository>();
        services.AddSingleton<IScenarioRepository, MongoScenarioRepository>();
        services.AddSingleton<IScenarioExecutionRepository, MongoScenarioExecutionRepository>();
        services.AddSingleton<IUserRepository, MongoUserRepository>();
        services.AddSingleton<IIncidentRepository, MongoIncidentRepository>();
        services.AddSingleton<IChatRepository, MongoChatRepository>();
        services.AddSingleton<IKnowledgeRepository, MongoKnowledgeRepository>();
        services.AddHostedService<MongoIndexInitializer>();
        return services;
    }

    /// Yalnızca API'de: açılışta AI okuyucu hesabının yetkilerini doğrular.
    public static IServiceCollection AddMongoAccessVerification(this IServiceCollection services)
        => services.AddHostedService<MongoAccessVerifier>();
}
