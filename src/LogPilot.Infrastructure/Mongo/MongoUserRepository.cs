using LogPilot.Application.Users;
using LogPilot.Domain.Users;
using MongoDB.Driver;

namespace LogPilot.Infrastructure.Mongo;

internal sealed class MongoUserRepository(MongoContext context) : IUserRepository
{
    public async Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct)
    {
        using var timeout = context.CreateTimeout(ct);
        return await context.Users.Find(FilterDefinition<User>.Empty).ToListAsync(timeout.Token);
    }

    public async Task UpsertManyAsync(IReadOnlyCollection<User> users, CancellationToken ct)
    {
        if (users.Count == 0) return;

        using var timeout = context.CreateTimeout(ct);
        var models = users.Select(u => new ReplaceOneModel<User>(
            Builders<User>.Filter.Eq(x => x.UserCode, u.UserCode), u) { IsUpsert = true });

        await context.Users.BulkWriteAsync(models, cancellationToken: timeout.Token);
    }

    public async Task<IReadOnlyList<User>> FindAsync(
        IReadOnlyCollection<string>? userCodes, IReadOnlyCollection<string>? customerNos,
        IReadOnlyCollection<string>? identityNos, CancellationToken ct)
    {
        var f = Builders<User>.Filter;
        var any = new List<FilterDefinition<User>>();
        if (userCodes is { Count: > 0 }) any.Add(f.In(x => x.UserCode, userCodes));
        if (customerNos is { Count: > 0 }) any.Add(f.In(x => x.CustomerNo, customerNos));
        if (identityNos is { Count: > 0 }) any.Add(f.In(x => x.IdentityNo, identityNos));
        if (any.Count == 0) return [];

        using var timeout = context.CreateTimeout(ct);
        return await context.Users.Find(f.Or(any)).Limit(20).ToListAsync(timeout.Token);
    }
}
