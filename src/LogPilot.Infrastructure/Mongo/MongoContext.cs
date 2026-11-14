using LogPilot.Domain.Chat;
using LogPilot.Domain.Incidents;
using LogPilot.Domain.Knowledge;
using LogPilot.Domain.Logs;
using LogPilot.Domain.Notifications;
using LogPilot.Domain.Purchases;
using LogPilot.Domain.Quotes;
using LogPilot.Domain.Scenarios;
using LogPilot.Domain.Users;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace LogPilot.Infrastructure.Mongo;

internal sealed class MongoContext
{
    private readonly TimeSpan _queryTimeout;

    public MongoContext(IMongoClient client, IOptions<MongoOptions> options) : this(client, options.Value) { }

    /// AI okuyucu bağlamı gibi ayrı bir istemciyle (farklı MongoDB hesabı) bağlam kurmak için.
    internal MongoContext(IMongoClient client, MongoOptions options)
    {
        MongoMappings.Register();

        var db = client.GetDatabase(options.DatabaseName);
        Logs = db.GetCollection<LogEntry>("logs");
        QuoteLogs = db.GetCollection<QuoteLogEntry>("quote_logs");
        SmsLogs = db.GetCollection<SmsLogEntry>("sms_logs");
        ProposalSteps = db.GetCollection<ProposalStepLog>("proposal_step_logs");
        WinsureTransactions = db.GetCollection<WinsureTransaction>("winsure_transactions");
        PaymentTransactions = db.GetCollection<PaymentTransaction>("payment_transactions");
        Incidents = db.GetCollection<Incident>("incidents");
        ChatSessions = db.GetCollection<ChatSession>("chat_sessions");
        ChatMessages = db.GetCollection<ChatMessage>("chat_messages");
        Scenarios = db.GetCollection<Scenario>("scenarios");
        Executions = db.GetCollection<ScenarioExecution>("scenario_executions");
        Users = db.GetCollection<User>("users");
        Knowledge = db.GetCollection<KnowledgeCase>("knowledge_cases");
        _queryTimeout = TimeSpan.FromSeconds(options.QueryTimeoutSeconds);
    }

    public IMongoCollection<LogEntry> Logs { get; }
    /// Sigorta teklif motoru logları: uygulama loglarından ayrı sistem, ayrı tablo.
    public IMongoCollection<QuoteLogEntry> QuoteLogs { get; }
    /// SMS ağ geçidi logları: sadece telefon numarasını tanır.
    public IMongoCollection<SmsLogEntry> SmsLogs { get; }
    /// Kurumsal web satın alma: adımlar, poliçe çekirdek sistemi (Winsure) ve ödeme. Dağıtık; anahtarlar farklı.
    public IMongoCollection<ProposalStepLog> ProposalSteps { get; }
    public IMongoCollection<WinsureTransaction> WinsureTransactions { get; }
    public IMongoCollection<PaymentTransaction> PaymentTransactions { get; }
    public IMongoCollection<Incident> Incidents { get; }
    public IMongoCollection<ChatSession> ChatSessions { get; }
    public IMongoCollection<ChatMessage> ChatMessages { get; }
    public IMongoCollection<Scenario> Scenarios { get; }
    public IMongoCollection<ScenarioExecution> Executions { get; }
    public IMongoCollection<User> Users { get; }
    public IMongoCollection<KnowledgeCase> Knowledge { get; }

    /// Sürümden bağımsız sorgu zaman aşımı: istemci iptali + QueryTimeoutSeconds birleşir.
    public CancellationTokenSource CreateTimeout(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_queryTimeout);
        return cts;
    }
}
