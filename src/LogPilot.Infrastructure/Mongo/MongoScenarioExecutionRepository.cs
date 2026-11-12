using LogPilot.Application.Common;
using LogPilot.Application.Scenarios;
using LogPilot.Domain.Scenarios;
using MongoDB.Bson;
using MongoDB.Driver;

namespace LogPilot.Infrastructure.Mongo;

internal sealed class MongoScenarioExecutionRepository(MongoContext context) : IScenarioExecutionRepository
{
    private static readonly FilterDefinitionBuilder<ScenarioExecution> F = Builders<ScenarioExecution>.Filter;
    private static readonly UpdateDefinitionBuilder<ScenarioExecution> U = Builders<ScenarioExecution>.Update;

    public async Task<ScenarioExecution> CreateAsync(
        string scenarioId, ExecutionTrigger trigger, DateTime requestedAt, CancellationToken ct)
    {
        var execution = new ScenarioExecution
        {
            Id = ObjectId.GenerateNewId().ToString(),   // id'yi biz üretiriz: Location header'ı için garanti
            ScenarioId = scenarioId,
            TriggeredBy = trigger,
            Status = ExecutionStatus.Pending,
            RequestedAt = requestedAt
        };

        using var timeout = context.CreateTimeout(ct);
        await context.Executions.InsertOneAsync(execution, cancellationToken: timeout.Token);
        return execution;
    }

    public async Task<ScenarioExecution?> GetByIdAsync(string id, CancellationToken ct)
    {
        if (!ObjectId.TryParse(id, out _)) return null;

        using var timeout = context.CreateTimeout(ct);
        return await context.Executions.Find(x => x.Id == id).FirstOrDefaultAsync(timeout.Token);
    }

    public async Task<PagedResult<ScenarioExecution>> SearchAsync(
        string? scenarioId, int page, int pageSize, CancellationToken ct)
    {
        using var timeout = context.CreateTimeout(ct);
        var token = timeout.Token;

        var filter = scenarioId is null ? FilterDefinition<ScenarioExecution>.Empty : F.Eq(x => x.ScenarioId, scenarioId);

        var itemsTask = context.Executions.Find(filter)
            .SortByDescending(x => x.RequestedAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(token);

        var countTask = scenarioId is null
            ? context.Executions.EstimatedDocumentCountAsync(cancellationToken: token)
            : context.Executions.CountDocumentsAsync(filter, cancellationToken: token);

        await Task.WhenAll(itemsTask, countTask);

        return new PagedResult<ScenarioExecution>(await itemsTask, await countTask, page, pageSize);
    }

    public async Task<bool> HasActiveAsync(string scenarioId, CancellationToken ct)
    {
        using var timeout = context.CreateTimeout(ct);
        var filter = F.Eq(x => x.ScenarioId, scenarioId)
                     & F.In(x => x.Status, new[] { ExecutionStatus.Pending, ExecutionStatus.Running });

        return await context.Executions.Find(filter).Limit(1).AnyAsync(timeout.Token);
    }

    public async Task<ScenarioExecution?> ClaimNextPendingAsync(DateTime startedAt, CancellationToken ct)
    {
        using var timeout = context.CreateTimeout(ct);

        var update = U.Set(x => x.Status, ExecutionStatus.Running).Set(x => x.StartedAt, startedAt);
        var options = new FindOneAndUpdateOptions<ScenarioExecution>
        {
            Sort = Builders<ScenarioExecution>.Sort.Ascending(x => x.RequestedAt),
            ReturnDocument = ReturnDocument.After
        };

        // Tek atomik işlem: iki worker aynı kaydı alamaz.
        return await context.Executions.FindOneAndUpdateAsync(
            F.Eq(x => x.Status, ExecutionStatus.Pending), update, options, timeout.Token);
    }

    public async Task CompleteAsync(
        string id, DateTime finishedAt, int logCount, int seed, IReadOnlyList<string> correlationIds, CancellationToken ct)
    {
        using var timeout = context.CreateTimeout(ct);
        var update = U.Set(x => x.Status, ExecutionStatus.Completed)
            .Set(x => x.FinishedAt, finishedAt)
            .Set(x => x.GeneratedLogCount, logCount)
            .Set(x => x.Seed, seed)
            .Set(x => x.CorrelationIds, correlationIds.ToList());

        await context.Executions.UpdateOneAsync(x => x.Id == id, update, cancellationToken: timeout.Token);
    }

    public async Task FailAsync(string id, DateTime finishedAt, string error, CancellationToken ct)
    {
        using var timeout = context.CreateTimeout(ct);
        var update = U.Set(x => x.Status, ExecutionStatus.Failed)
            .Set(x => x.FinishedAt, finishedAt)
            .Set(x => x.Error, error);

        await context.Executions.UpdateOneAsync(x => x.Id == id, update, cancellationToken: timeout.Token);
    }

    public async Task<long> FailStaleRunningAsync(DateTime now, CancellationToken ct)
    {
        using var timeout = context.CreateTimeout(ct);
        var update = U.Set(x => x.Status, ExecutionStatus.Failed)
            .Set(x => x.FinishedAt, now)
            .Set(x => x.Error, "Interrupted: the generator was restarted while this execution was running.");

        var result = await context.Executions.UpdateManyAsync(
            F.Eq(x => x.Status, ExecutionStatus.Running), update, cancellationToken: timeout.Token);
        return result.ModifiedCount;
    }
}
