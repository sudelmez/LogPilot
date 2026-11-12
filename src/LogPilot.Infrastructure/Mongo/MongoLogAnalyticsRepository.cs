using LogPilot.Application.Analytics;
using LogPilot.Domain.Analytics;
using LogPilot.Domain.Logs;
using MongoDB.Bson;
using MongoDB.Driver;

namespace LogPilot.Infrastructure.Mongo;

/// Tüm analitikler MongoDB aggregation ile sunucuda hesaplanır; uygulamaya sadece özet döner.
/// Her pipeline `timestamp` aralığıyla başlar, böylece ix_timestamp_ttl indeksi aralığı daraltır.
internal sealed class MongoLogAnalyticsRepository(MongoContext context) : ILogAnalyticsRepository
{
    private static readonly string[] ErrorLevelNames = [nameof(LogSeverity.Error), nameof(LogSeverity.Critical)];

    public async Task<LogCounts> GetCountsAsync(TimeRange range, CancellationToken ct)
    {
        var docs = await RunAsync(
        [
            MatchRange(range),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", BsonNull.Value },
                { "total", new BsonDocument("$sum", 1) },
                { "errors", CountIf(IsErrorLevel()) },
                { "warnings", CountIf(new BsonDocument("$eq", new BsonArray { "$level", nameof(LogSeverity.Warning) })) },
                { "completed", CountIf(new BsonDocument("$in", new BsonArray { "$outcome", new BsonArray { "Success", "Failure" } })) },
                { "failed", CountIf(new BsonDocument("$eq", new BsonArray { "$outcome", "Failure" })) }
            })
        ], ct);

        var d = docs.FirstOrDefault();
        return d is null
            ? new LogCounts(0, 0, 0, 0, 0)
            : new LogCounts(L(d, "total"), L(d, "errors"), L(d, "warnings"), L(d, "completed"), L(d, "failed"));
    }

    public async Task<IReadOnlyList<TrendPoint>> GetTrendAsync(TimeRange range, int bucketMinutes, CancellationToken ct)
    {
        // Kova başlangıcı aritmetikle hesaplanır (epoch ms - ms % kovaMs): C# tarafındaki Floor ile birebir aynı hizalama.
        var bucketMs = bucketMinutes * 60_000L;
        var tsMs = new BsonDocument("$toLong", "$timestamp");

        var docs = await RunAsync(
        [
            MatchRange(range),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", new BsonDocument("$subtract", new BsonArray
                    {
                        tsMs,
                        new BsonDocument("$mod", new BsonArray { tsMs, bucketMs })
                    }) },
                { "total", new BsonDocument("$sum", 1) },
                { "errors", CountIf(IsErrorLevel()) },
                { "warnings", CountIf(new BsonDocument("$eq", new BsonArray { "$level", nameof(LogSeverity.Warning) })) }
            }),
            new BsonDocument("$sort", new BsonDocument("_id", 1))
        ], ct);

        return docs
            .Select(d => new TrendPoint(
                DateTimeOffset.FromUnixTimeMilliseconds(d["_id"].ToInt64()).UtcDateTime,
                L(d, "total"), L(d, "errors"), L(d, "warnings")))
            .ToList();
    }

    public async Task<IReadOnlyList<ServiceStats>> GetServiceStatsAsync(TimeRange range, CancellationToken ct)
    {
        var docs = await RunAsync(
        [
            MatchRange(range),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$service" },
                { "total", new BsonDocument("$sum", 1) },
                { "errors", CountIf(IsErrorLevel()) },
                { "warnings", CountIf(new BsonDocument("$eq", new BsonArray { "$level", nameof(LogSeverity.Warning) })) },
                { "avgDuration", new BsonDocument("$avg", "$durationMs") },
                { "maxDuration", new BsonDocument("$max", "$durationMs") },
                { "lastErrorAt", new BsonDocument("$max", new BsonDocument("$cond", new BsonArray { IsErrorLevel(), "$timestamp", BsonNull.Value })) }
            }),
            new BsonDocument("$sort", new BsonDocument { { "_id", 1 } })
        ], ct);

        return docs
            .Select(d => new ServiceStats(
                d["_id"].AsString, L(d, "total"), L(d, "errors"), L(d, "warnings"),
                D(d, "avgDuration"), d.TryGetValue("maxDuration", out var max) && max.IsNumeric ? max.ToInt64() : null,
                T(d, "lastErrorAt")))
            .ToList();
    }

    public async Task<IReadOnlyList<ErrorCodeCount>> GetTopErrorCodesAsync(TimeRange range, int limit, CancellationToken ct)
    {
        var match = MatchRange(range);
        match["$match"].AsBsonDocument["errorCode"] = new BsonDocument("$exists", true);   // ix_errorcode_timestamp (partial) ile uyumlu

        var docs = await RunAsync(
        [
            match,
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$errorCode" },
                { "count", new BsonDocument("$sum", 1) },
                { "users", new BsonDocument("$addToSet", "$userCode") },
                { "lastSeen", new BsonDocument("$max", "$timestamp") }
            }),
            new BsonDocument("$project", new BsonDocument
            {
                { "count", 1 }, { "lastSeen", 1 },
                { "distinctUsers", new BsonDocument("$size", "$users") }
            }),
            new BsonDocument("$sort", new BsonDocument { { "count", -1 }, { "_id", 1 } }),
            new BsonDocument("$limit", limit)
        ], ct);

        return docs
            .Select(d => new ErrorCodeCount(d["_id"].AsString, L(d, "count"), L(d, "distinctUsers"), T(d, "lastSeen") ?? range.To))
            .ToList();
    }

    public async Task<IReadOnlyList<ErrorGroup>> GetErrorGroupsAsync(TimeRange range, string? errorCode, CancellationToken ct)
    {
        var match = MatchRange(range);
        var filter = match["$match"].AsBsonDocument;
        filter["level"] = new BsonDocument("$in", new BsonArray(ErrorLevelNames));
        filter["errorCode"] = errorCode is null ? new BsonDocument("$exists", true) : new BsonString(errorCode);

        var docs = await RunAsync(
        [
            match,
            new BsonDocument("$sort", new BsonDocument("timestamp", -1)),   // $push sırası = en yeni önce
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$errorCode" },
                { "count", new BsonDocument("$sum", 1) },
                { "users", new BsonDocument("$addToSet", "$userCode") },
                { "services", new BsonDocument("$addToSet", "$service") },
                { "firstSeen", new BsonDocument("$min", "$timestamp") },
                { "lastSeen", new BsonDocument("$max", "$timestamp") },
                { "ids", new BsonDocument("$push", "$_id") }
            }),
            new BsonDocument("$project", new BsonDocument
            {
                { "count", 1 }, { "users", 1 }, { "services", 1 }, { "firstSeen", 1 }, { "lastSeen", 1 },
                { "ids", new BsonDocument("$slice", new BsonArray { "$ids", 50 }) }
            }),
            new BsonDocument("$sort", new BsonDocument { { "count", -1 }, { "_id", 1 } })
        ], ct);

        return docs
            .Select(d => new ErrorGroup(
                d["_id"].AsString,
                L(d, "count"),
                Strings(d, "users"),
                Strings(d, "services"),
                T(d, "firstSeen") ?? range.From,
                T(d, "lastSeen") ?? range.To,
                d["ids"].AsBsonArray.Where(x => x.IsObjectId).Select(x => x.AsObjectId.ToString()).ToList()))
            .ToList();
    }

    // ---- yardımcılar ----

    private async Task<List<BsonDocument>> RunAsync(IEnumerable<BsonDocument> stages, CancellationToken ct)
    {
        using var timeout = context.CreateTimeout(ct);
        var pipeline = PipelineDefinition<LogEntry, BsonDocument>.Create(stages);
        using var cursor = await context.Logs.AggregateAsync(pipeline, cancellationToken: timeout.Token);
        return await cursor.ToListAsync(timeout.Token);
    }

    private static BsonDocument MatchRange(TimeRange r)
        => new("$match", new BsonDocument("timestamp", new BsonDocument { { "$gte", r.From }, { "$lt", r.To } }));

    private static BsonDocument IsErrorLevel()
        => new("$in", new BsonArray { "$level", new BsonArray(ErrorLevelNames) });

    private static BsonDocument CountIf(BsonValue condition)
        => new("$sum", new BsonDocument("$cond", new BsonArray { condition, 1, 0 }));

    private static long L(BsonDocument d, string name)
        => d.TryGetValue(name, out var v) && v.IsNumeric ? v.ToInt64() : 0;

    private static double? D(BsonDocument d, string name)
        => d.TryGetValue(name, out var v) && v.IsNumeric ? v.ToDouble() : null;

    private static DateTime? T(BsonDocument d, string name)
        => d.TryGetValue(name, out var v) && v.IsValidDateTime ? v.ToUniversalTime() : null;

    private static List<string> Strings(BsonDocument d, string name)
        => d.TryGetValue(name, out var v) && v.IsBsonArray
            ? v.AsBsonArray.Where(x => x.IsString).Select(x => x.AsString).OrderBy(x => x, StringComparer.Ordinal).ToList()
            : [];
}
