using System.Text.RegularExpressions;
using LogPilot.Application.Common;
using LogPilot.Application.Purchases;
using LogPilot.Domain.Logs;
using LogPilot.Domain.Purchases;
using MongoDB.Bson;
using MongoDB.Driver;

namespace LogPilot.Infrastructure.Mongo;

internal sealed class MongoPurchaseLogRepository(MongoContext context) : IPurchaseLogRepository
{
    private const int MaxWindowDocs = 20_000;

    public async Task<PagedResult<ProposalStepLog>> SearchProposalStepsAsync(ProposalStepSearch r, CancellationToken ct)
    {
        var f = Builders<ProposalStepLog>.Filter;
        var filter = f.Gte(x => x.Timestamp, r.From) & f.Lt(x => x.Timestamp, r.To);
        if (r.IdentityNos is { Count: > 0 }) filter &= f.In(x => x.UserIdentityNo, r.IdentityNos);
        if (r.ProposalNo is not null) filter &= f.Eq(x => x.ProposalNo, r.ProposalNo);
        if (r.Step is not null) filter &= f.Eq(x => x.Step, r.Step);
        if (r.OnlyErrors) filter &= f.Eq(x => x.Status, ProposalStepStatus.Error);
        // policyNo ayrı alan değil, response JSON'unun içinde: metin içi arama (değer regex'e kaçışlanır).
        if (r.ResponseContains is not null)
            filter &= f.Regex(x => x.ResponseJson, new BsonRegularExpression(Regex.Escape(r.ResponseContains)));
        var sort = r.Ascending ? Builders<ProposalStepLog>.Sort.Ascending(x => x.Timestamp) : Builders<ProposalStepLog>.Sort.Descending(x => x.Timestamp);
        return await Page(context.ProposalSteps, filter, sort, r.Limit, ct);
    }

    public async Task<PagedResult<WinsureTransaction>> SearchWinsureAsync(WinsureSearch r, CancellationToken ct)
    {
        var f = Builders<WinsureTransaction>.Filter;
        var filter = f.Gte(x => x.Timestamp, r.From) & f.Lt(x => x.Timestamp, r.To);
        if (r.PolicyNos is { Count: > 0 }) filter &= f.In(x => x.PolicyNo, r.PolicyNos);
        if (r.Operation is { } op) filter &= f.Eq(x => x.Operation, op);
        if (r.OnlyFailed) filter &= f.Eq(x => x.Status, WinsureStatus.Failed);
        var sort = r.Ascending ? Builders<WinsureTransaction>.Sort.Ascending(x => x.Timestamp) : Builders<WinsureTransaction>.Sort.Descending(x => x.Timestamp);
        return await Page(context.WinsureTransactions, filter, sort, r.Limit, ct);
    }

    public async Task<PagedResult<PaymentTransaction>> SearchPaymentsAsync(PaymentSearch r, CancellationToken ct)
    {
        var f = Builders<PaymentTransaction>.Filter;
        var filter = f.Gte(x => x.Timestamp, r.From) & f.Lt(x => x.Timestamp, r.To);
        if (r.PolicyNos is { Count: > 0 }) filter &= f.In(x => x.PolicyNo, r.PolicyNos);
        if (r.ClientReferenceCodes is { Count: > 0 }) filter &= f.In(x => x.ClientReferenceCode, r.ClientReferenceCodes);
        if (r.Stage is { } stage) filter &= f.Eq(x => x.Stage, stage);
        if (r.OnlyFailed) filter &= f.Eq(x => x.Status, PaymentStatus.Failed);
        var sort = r.Ascending ? Builders<PaymentTransaction>.Sort.Ascending(x => x.Timestamp) : Builders<PaymentTransaction>.Sort.Descending(x => x.Timestamp);
        return await Page(context.PaymentTransactions, filter, sort, r.Limit, ct);
    }

    public Task<ProposalStepLog?> GetProposalStepByIdAsync(string id, CancellationToken ct) => ById(context.ProposalSteps, id, ct);
    public Task<WinsureTransaction?> GetWinsureByIdAsync(string id, CancellationToken ct) => ById(context.WinsureTransactions, id, ct);
    public Task<PaymentTransaction?> GetPaymentByIdAsync(string id, CancellationToken ct) => ById(context.PaymentTransactions, id, ct);

    public async Task<PurchaseWindow> GetWindowAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        using var timeout = context.CreateTimeout(ct);
        var steps = context.ProposalSteps.Find(x => x.Timestamp >= from && x.Timestamp < to).Limit(MaxWindowDocs).ToListAsync(timeout.Token);
        var winsure = context.WinsureTransactions.Find(x => x.Timestamp >= from && x.Timestamp < to).Limit(MaxWindowDocs).ToListAsync(timeout.Token);
        var payments = context.PaymentTransactions.Find(x => x.Timestamp >= from && x.Timestamp < to).Limit(MaxWindowDocs).ToListAsync(timeout.Token);
        await Task.WhenAll(steps, winsure, payments);
        return new PurchaseWindow(await steps, await winsure, await payments);
    }

    public async Task<long> GetEstimatedCountAsync(CancellationToken ct)
    {
        using var timeout = context.CreateTimeout(ct);
        return await context.ProposalSteps.EstimatedDocumentCountAsync(cancellationToken: timeout.Token);
    }

    public async Task AddManyAsync(IReadOnlyCollection<ProposalStepLog> steps, IReadOnlyCollection<WinsureTransaction> winsure,
        IReadOnlyCollection<PaymentTransaction> payments, CancellationToken ct)
    {
        using var timeout = context.CreateTimeout(ct);
        if (steps.Count > 0) await context.ProposalSteps.InsertManyAsync(steps, cancellationToken: timeout.Token);
        if (winsure.Count > 0) await context.WinsureTransactions.InsertManyAsync(winsure, cancellationToken: timeout.Token);
        if (payments.Count > 0) await context.PaymentTransactions.InsertManyAsync(payments, cancellationToken: timeout.Token);
    }

    private async Task<PagedResult<T>> Page<T>(IMongoCollection<T> collection, FilterDefinition<T> filter, SortDefinition<T> sort, int limit, CancellationToken ct)
    {
        using var timeout = context.CreateTimeout(ct);
        var items = collection.Find(filter).Sort(sort).Limit(limit).ToListAsync(timeout.Token);
        var count = collection.CountDocumentsAsync(filter, cancellationToken: timeout.Token);
        await Task.WhenAll(items, count);
        return new PagedResult<T>(await items, await count, 1, limit);
    }

    private async Task<T?> ById<T>(IMongoCollection<T> collection, string id, CancellationToken ct) where T : class
    {
        if (!ObjectId.TryParse(id, out var oid)) return null;
        using var timeout = context.CreateTimeout(ct);
        return await collection.Find(new BsonDocument("_id", oid)).FirstOrDefaultAsync(timeout.Token);
    }
}
