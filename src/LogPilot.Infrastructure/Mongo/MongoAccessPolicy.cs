using MongoDB.Bson;
using MongoDB.Driver;

namespace LogPilot.Infrastructure.Mongo;

/// Veritabanı ROLLERİ. Yetki kodda değil, MongoDB'nin kendisinde tanımlanır; uygulama kodu hatalı olsa bile aşılamaz.
///   logpilotApp      : uygulamanın kendisi (generator log yazar, API sohbet/incident/vaka hafızası yazar) → readWrite
///   logpilotAiReader : AI araçlarının kullandığı hesap → SADECE 'find', SADECE analiz için gereken koleksiyonlarda.
///                      Silme, güncelleme, ekleme, koleksiyon/indeks düşürme yetkisi YOKTUR. Sohbet kayıtları,
///                      senaryolar ve çalıştırma kayıtları bu hesaba hiç görünmez.
/// Aynı liste docker/mongo-init/01-logpilot-roles.js içinde de vardır (test ikisinin eşit olduğunu denetler).
public static class MongoAccessPolicy
{
    public const string AiReaderRole = "logpilotAiReader";

    public static readonly IReadOnlyList<string> AiReadableCollections =
        ["logs", "quote_logs", "sms_logs", "users", "incidents", "knowledge_cases"];

    /// Bir hesabın yetkilerinde bulunmaması gereken (veri değiştiren) eylemler.
    public static readonly IReadOnlySet<string> WriteActions = new HashSet<string>(StringComparer.Ordinal)
    {
        "insert", "update", "remove", "dropCollection", "dropDatabase", "createCollection", "createIndex", "dropIndex",
        "collMod", "renameCollectionSameDB", "convertToCapped", "emptycapped", "bypassDocumentValidation", "compact",
        "createUser", "dropUser", "createRole", "dropRole", "grantRole", "revokeRole", "grantRolesToUser", "dropAllUsersFromDatabase"
    };

    /// AI okuyucu rolünü ve kullanıcısını oluşturur/günceller (idempotent). Kurulum betiği ve testler kullanır.
    /// Bu metodu çalıştıran bağlantı userAdmin yetkili olmalıdır; uygulamanın kendisi bunu çağırmaz.
    public static async Task EnsureAiReaderAsync(IMongoDatabase db, string user, string password, CancellationToken ct = default)
    {
        var privileges = new BsonArray(AiReadableCollections.Select(c => new BsonDocument
        {
            { "resource", new BsonDocument { { "db", db.DatabaseNamespace.DatabaseName }, { "collection", c } } },
            { "actions", new BsonArray { "find" } }
        }));

        var roles = await db.RunCommandAsync<BsonDocument>(new BsonDocument("rolesInfo", AiReaderRole), cancellationToken: ct);
        var roleCommand = roles["roles"].AsBsonArray.Count == 0 ? "createRole" : "updateRole";
        var role = new BsonDocument { { roleCommand, AiReaderRole }, { "privileges", privileges } };
        if (roleCommand == "createRole") role.Add("roles", new BsonArray());
        await db.RunCommandAsync<BsonDocument>(role, cancellationToken: ct);

        var users = await db.RunCommandAsync<BsonDocument>(new BsonDocument("usersInfo", user), cancellationToken: ct);
        var userCommand = users["users"].AsBsonArray.Count == 0 ? "createUser" : "updateUser";
        await db.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            { userCommand, user }, { "pwd", password }, { "roles", new BsonArray { AiReaderRole } }
        }, cancellationToken: ct);
    }

    /// Bağlantının gerçek yetkilerini MongoDB'ye sorar (veri değiştirmez).
    public static async Task<AccessReport> InspectAsync(IMongoClient client, CancellationToken ct = default)
    {
        var status = await client.GetDatabase("admin").RunCommandAsync<BsonDocument>(
            new BsonDocument { { "connectionStatus", 1 }, { "showPrivileges", true } }, cancellationToken: ct);
        var auth = status["authInfo"].AsBsonDocument;
        var users = auth["authenticatedUsers"].AsBsonArray.Select(u => u["user"].AsString).ToList();
        var actions = auth.TryGetValue("authenticatedUserPrivileges", out var p)
            ? p.AsBsonArray.SelectMany(x => x["actions"].AsBsonArray.Select(a => a.AsString)).Distinct().ToList()
            : [];
        return new AccessReport(users, actions, actions.Where(WriteActions.Contains).ToList());
    }
}

/// Users boşsa: bağlantı kimliksiz (MongoDB'de auth kapalı) — veritabanı seviyesinde rol uygulanmıyor demektir.
public sealed record AccessReport(IReadOnlyList<string> Users, IReadOnlyList<string> Actions, IReadOnlyList<string> WriteActions)
{
    public bool IsAuthenticated => Users.Count > 0;
    public bool IsReadOnly => IsAuthenticated && WriteActions.Count == 0;
}
