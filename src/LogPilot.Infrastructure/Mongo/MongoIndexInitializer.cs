using LogPilot.Domain.Chat;
using LogPilot.Domain.Incidents;
using LogPilot.Domain.Knowledge;
using LogPilot.Domain.Logs;
using LogPilot.Domain.Notifications;
using LogPilot.Domain.Purchases;
using LogPilot.Domain.Quotes;
using LogPilot.Domain.Users;
using LogPilot.Domain.Scenarios;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace LogPilot.Infrastructure.Mongo;

// Hem Api hem LogGenerator host'unda çalışır; CreateMany idempotent olduğu için sakıncası yok.
internal sealed class MongoIndexInitializer(MongoContext context, IOptions<MongoOptions> options)
    : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        var retention = TimeSpan.FromHours(options.Value.LogRetentionHours);

        // Saklama süresi değiştiyse mevcut TTL indeksini yerinde güncelle; yoksa CreateMany "IndexOptionsConflict" verir.
        await EnsureTtlAsync(context.Logs, "ix_timestamp_ttl", retention, ct);
        await EnsureTtlAsync(context.Executions, "ix_requestedat_ttl", retention, ct);
        await EnsureTtlAsync(context.QuoteLogs, "ix_timestamp_ttl", retention, ct);
        await EnsureTtlAsync(context.SmsLogs, "ix_timestamp_ttl", retention, ct);
        await EnsureTtlAsync(context.ProposalSteps, "ix_timestamp_ttl", retention, ct);
        await EnsureTtlAsync(context.WinsureTransactions, "ix_timestamp_ttl", retention, ct);
        await EnsureTtlAsync(context.PaymentTransactions, "ix_timestamp_ttl", retention, ct);

        var logKeys = Builders<LogEntry>.IndexKeys;
        await context.Logs.Indexes.CreateManyAsync(
        [
            // TTL indeksi aynı zamanda timestamp filtresi/sıralaması için kullanılır.
            new CreateIndexModel<LogEntry>(logKeys.Ascending(x => x.Timestamp),
                new CreateIndexOptions { Name = "ix_timestamp_ttl", ExpireAfter = retention }),
            new CreateIndexModel<LogEntry>(logKeys.Ascending(x => x.UserCode).Descending(x => x.Timestamp),
                new CreateIndexOptions { Name = "ix_user_timestamp" }),
            new CreateIndexModel<LogEntry>(logKeys.Ascending(x => x.Service).Descending(x => x.Timestamp),
                new CreateIndexOptions { Name = "ix_service_timestamp" }),
            // Çoğu log hata değil: sadece errorCode'u olanları indeksle (partial index).
            new CreateIndexModel<LogEntry>(logKeys.Ascending(x => x.ErrorCode).Descending(x => x.Timestamp),
                new CreateIndexOptions<LogEntry>
                {
                    Name = "ix_errorcode_timestamp",
                    PartialFilterExpression = new BsonDocumentFilterDefinition<LogEntry>(
                        new BsonDocument("errorCode", new BsonDocument("$exists", true)))
                }),
            new CreateIndexModel<LogEntry>(logKeys.Ascending(x => x.CorrelationId),
                new CreateIndexOptions { Name = "ix_correlation" }),
            new CreateIndexModel<LogEntry>(logKeys.Ascending(x => x.TransactionId),
                new CreateIndexOptions { Name = "ix_transaction" })
        ], ct);

        // quote_logs: her kimlik anahtarı (quoteId, customerNo, identityNo, userCode, correlationId) tablolar arası
        // eşleştirmede kullanılır; hepsi zamanla birlikte indekslenir.
        var quoteKeys = Builders<QuoteLogEntry>.IndexKeys;
        await context.QuoteLogs.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<QuoteLogEntry>(quoteKeys.Ascending(x => x.Timestamp),
                new CreateIndexOptions { Name = "ix_timestamp_ttl", ExpireAfter = retention }),
            new CreateIndexModel<QuoteLogEntry>(quoteKeys.Ascending(x => x.QuoteId).Ascending(x => x.Timestamp),
                new CreateIndexOptions { Name = "ix_quote_timestamp" }),
            new CreateIndexModel<QuoteLogEntry>(quoteKeys.Ascending(x => x.CustomerNo).Descending(x => x.Timestamp),
                new CreateIndexOptions { Name = "ix_customer_timestamp" }),
            new CreateIndexModel<QuoteLogEntry>(quoteKeys.Ascending(x => x.IdentityNo).Descending(x => x.Timestamp),
                new CreateIndexOptions { Name = "ix_identity_timestamp" }),
            new CreateIndexModel<QuoteLogEntry>(quoteKeys.Ascending(x => x.UserCode).Descending(x => x.Timestamp),
                new CreateIndexOptions { Name = "ix_user_timestamp" }),
            new CreateIndexModel<QuoteLogEntry>(quoteKeys.Ascending(x => x.CorrelationId),
                new CreateIndexOptions { Name = "ix_correlation" }),
            new CreateIndexModel<QuoteLogEntry>(quoteKeys.Ascending(x => x.PolicyNo),
                new CreateIndexOptions<QuoteLogEntry>
                {
                    Name = "ix_policyno",
                    PartialFilterExpression = new BsonDocumentFilterDefinition<QuoteLogEntry>(
                        new BsonDocument("policyNo", new BsonDocument("$exists", true)))
                }),
            new CreateIndexModel<QuoteLogEntry>(quoteKeys.Ascending(x => x.InsurerCode).Descending(x => x.Timestamp),
                new CreateIndexOptions<QuoteLogEntry>
                {
                    Name = "ix_insurer_timestamp",
                    PartialFilterExpression = new BsonDocumentFilterDefinition<QuoteLogEntry>(
                        new BsonDocument("insurerCode", new BsonDocument("$exists", true)))
                }),
            new CreateIndexModel<QuoteLogEntry>(quoteKeys.Ascending(x => x.ErrorCode).Descending(x => x.Timestamp),
                new CreateIndexOptions<QuoteLogEntry>
                {
                    Name = "ix_errorcode_timestamp",
                    PartialFilterExpression = new BsonDocumentFilterDefinition<QuoteLogEntry>(
                        new BsonDocument("errorCode", new BsonDocument("$exists", true)))
                })
        ], ct);

        // sms_logs: zincirin son halkası telefonla aranır (telefon + zaman); mesaj no ile bir mesajın tüm durumları.
        var smsKeys = Builders<SmsLogEntry>.IndexKeys;
        await context.SmsLogs.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<SmsLogEntry>(smsKeys.Ascending(x => x.Timestamp),
                new CreateIndexOptions { Name = "ix_timestamp_ttl", ExpireAfter = retention }),
            new CreateIndexModel<SmsLogEntry>(smsKeys.Ascending(x => x.Phone).Descending(x => x.Timestamp),
                new CreateIndexOptions { Name = "ix_phone_timestamp" }),
            new CreateIndexModel<SmsLogEntry>(smsKeys.Ascending(x => x.MessageId),
                new CreateIndexOptions { Name = "ix_messageid" }),
            new CreateIndexModel<SmsLogEntry>(smsKeys.Ascending(x => x.TemplateCode).Ascending(x => x.Status).Descending(x => x.Timestamp),
                new CreateIndexOptions { Name = "ix_template_status_timestamp" })
        ], ct);

        // Satın alma: her tablonun kendi anahtarı indekslenir (kimlik no, teklif no, poliçe no, clientReferenceCode).
        var stepKeys = Builders<ProposalStepLog>.IndexKeys;
        await context.ProposalSteps.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<ProposalStepLog>(stepKeys.Ascending(x => x.Timestamp),
                new CreateIndexOptions { Name = "ix_timestamp_ttl", ExpireAfter = retention }),
            new CreateIndexModel<ProposalStepLog>(stepKeys.Ascending(x => x.UserIdentityNo).Descending(x => x.Timestamp),
                new CreateIndexOptions { Name = "ix_identity_timestamp" }),
            new CreateIndexModel<ProposalStepLog>(stepKeys.Ascending(x => x.ProposalNo).Ascending(x => x.Timestamp),
                new CreateIndexOptions { Name = "ix_proposal_timestamp" })
        ], ct);

        var winsureKeys = Builders<WinsureTransaction>.IndexKeys;
        await context.WinsureTransactions.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<WinsureTransaction>(winsureKeys.Ascending(x => x.Timestamp),
                new CreateIndexOptions { Name = "ix_timestamp_ttl", ExpireAfter = retention }),
            new CreateIndexModel<WinsureTransaction>(winsureKeys.Ascending(x => x.PolicyNo).Ascending(x => x.Timestamp),
                new CreateIndexOptions { Name = "ix_policy_timestamp" }),
            new CreateIndexModel<WinsureTransaction>(winsureKeys.Ascending(x => x.Status).Descending(x => x.Timestamp),
                new CreateIndexOptions { Name = "ix_status_timestamp" })
        ], ct);

        var paymentKeys = Builders<PaymentTransaction>.IndexKeys;
        await context.PaymentTransactions.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<PaymentTransaction>(paymentKeys.Ascending(x => x.Timestamp),
                new CreateIndexOptions { Name = "ix_timestamp_ttl", ExpireAfter = retention }),
            new CreateIndexModel<PaymentTransaction>(paymentKeys.Ascending(x => x.PolicyNo),
                new CreateIndexOptions<PaymentTransaction>
                {
                    Name = "ix_policyno",
                    PartialFilterExpression = new BsonDocumentFilterDefinition<PaymentTransaction>(new BsonDocument("policyNo", new BsonDocument("$exists", true)))
                }),
            new CreateIndexModel<PaymentTransaction>(paymentKeys.Ascending(x => x.ClientReferenceCode).Ascending(x => x.Timestamp),
                new CreateIndexOptions { Name = "ix_clientref_timestamp" })
        ], ct);

        var userKeys = Builders<User>.IndexKeys;
        await context.Users.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<User>(userKeys.Ascending(x => x.CustomerNo),
                new CreateIndexOptions<User>
                {
                    Name = "ix_customerno",
                    PartialFilterExpression = new BsonDocumentFilterDefinition<User>(new BsonDocument("customerNo", new BsonDocument("$exists", true)))
                }),
            new CreateIndexModel<User>(userKeys.Ascending(x => x.Phone),
                new CreateIndexOptions<User>
                {
                    Name = "ix_phone",
                    PartialFilterExpression = new BsonDocumentFilterDefinition<User>(new BsonDocument("phone", new BsonDocument("$exists", true)))
                }),
            new CreateIndexModel<User>(userKeys.Ascending(x => x.IdentityNo),
                new CreateIndexOptions<User>
                {
                    Name = "ix_identityno",
                    PartialFilterExpression = new BsonDocumentFilterDefinition<User>(new BsonDocument("identityNo", new BsonDocument("$exists", true)))
                })
        ], ct);

        var execKeys = Builders<ScenarioExecution>.IndexKeys;
        await context.Executions.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<ScenarioExecution>(execKeys.Ascending(x => x.RequestedAt),
                new CreateIndexOptions { Name = "ix_requestedat_ttl", ExpireAfter = retention }),
            new CreateIndexModel<ScenarioExecution>(execKeys.Ascending(x => x.Status).Ascending(x => x.RequestedAt),
                new CreateIndexOptions { Name = "ix_status_requestedat" }),
            new CreateIndexModel<ScenarioExecution>(execKeys.Ascending(x => x.ScenarioId).Descending(x => x.RequestedAt),
                new CreateIndexOptions { Name = "ix_scenario_requestedat" })
        ], ct);

        var incidentKeys = Builders<Incident>.IndexKeys;
        await context.Incidents.Indexes.CreateManyAsync(
        [
            // Liste: "açık incident'lar, en son görülen önce"
            new CreateIndexModel<Incident>(incidentKeys.Ascending(x => x.Status).Descending(x => x.LastSeenAt),
                new CreateIndexOptions { Name = "ix_status_lastseen" }),
            // Tespit: "bu hata kodu için açık incident var mı?"
            new CreateIndexModel<Incident>(incidentKeys.Ascending(x => x.ErrorCode).Descending(x => x.LastSeenAt),
                new CreateIndexOptions { Name = "ix_errorcode_lastseen" })
        ], ct);

        await context.ChatSessions.Indexes.CreateOneAsync(
            new CreateIndexModel<ChatSession>(Builders<ChatSession>.IndexKeys.Descending(x => x.UpdatedAt),
                new CreateIndexOptions { Name = "ix_updatedat" }), cancellationToken: ct);

        await context.ChatMessages.Indexes.CreateOneAsync(
            new CreateIndexModel<ChatMessage>(Builders<ChatMessage>.IndexKeys.Ascending(x => x.SessionId).Ascending(x => x.CreatedAt),
                new CreateIndexOptions { Name = "ix_session_createdat" }), cancellationToken: ct);

        // Vaka hafızası: hata koduyla ilgili vaka arama (çok anahtarlı dizi indeksi), liste sıralaması,
        // aynı kaynaktan (mesaj/incident) tekrar onayda güncelleme.
        var knowledgeKeys = Builders<KnowledgeCase>.IndexKeys;
        await context.Knowledge.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<KnowledgeCase>(knowledgeKeys.Ascending(x => x.ErrorCodes).Descending(x => x.UpdatedAt),
                new CreateIndexOptions { Name = "ix_errorcodes_updatedat" }),
            new CreateIndexModel<KnowledgeCase>(knowledgeKeys.Descending(x => x.UpdatedAt),
                new CreateIndexOptions { Name = "ix_updatedat" }),
            new CreateIndexModel<KnowledgeCase>(knowledgeKeys.Ascending(x => x.Source).Ascending(x => x.SourceId),
                new CreateIndexOptions { Name = "ix_source" })
        ], ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private static async Task EnsureTtlAsync<T>(IMongoCollection<T> collection, string indexName, TimeSpan retention, CancellationToken ct)
    {
        var wanted = (long)retention.TotalSeconds;
        using var cursor = await collection.Indexes.ListAsync(ct);
        var existing = (await cursor.ToListAsync(ct)).FirstOrDefault(i => i["name"].AsString == indexName);
        if (existing is null || !existing.Contains("expireAfterSeconds")) return;   // yoksa CreateMany doğru süreyle kurar
        if (existing["expireAfterSeconds"].ToInt64() == wanted) return;

        await collection.Database.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            { "collMod", collection.CollectionNamespace.CollectionName },
            { "index", new BsonDocument { { "name", indexName }, { "expireAfterSeconds", wanted } } }
        }, cancellationToken: ct);
    }
}
