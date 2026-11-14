using LogPilot.Domain.Chat;
using LogPilot.Domain.Incidents;
using LogPilot.Domain.Knowledge;
using LogPilot.Domain.Logs;
using LogPilot.Domain.Notifications;
using LogPilot.Domain.Purchases;
using LogPilot.Domain.Quotes;
using LogPilot.Domain.Scenarios;
using LogPilot.Domain.Users;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.IdGenerators;
using MongoDB.Bson.Serialization.Serializers;

namespace LogPilot.Infrastructure.Mongo;

internal static class MongoMappings
{
    private static int _registered;

    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1) return;

        ConventionRegistry.Register("LogPilotConventions", new ConventionPack
        {
            new CamelCaseElementNameConvention(),
            new EnumRepresentationConvention(BsonType.String),
            new IgnoreIfNullConvention(true),      // null alanlar saklanmaz: daha az disk, partial index'e uygun
            new IgnoreExtraElementsConvention(true),
            new UtcDateTimeConvention()            // DateTime her zaman UTC okunur (yoksa Local'e çevrilir)
        }, type => type.Namespace?.StartsWith("LogPilot.Domain", StringComparison.Ordinal) == true);

        BsonClassMap.TryRegisterClassMap<LogEntry>(map =>
        {
            map.AutoMap();
            map.MapIdMember(x => x.Id).SetIdGenerator(StringObjectIdGenerator.Instance).SetSerializer(new StringSerializer(BsonType.ObjectId));
        });

        BsonClassMap.TryRegisterClassMap<QuoteLogEntry>(map =>
        {
            map.AutoMap();
            map.MapIdMember(x => x.Id).SetIdGenerator(StringObjectIdGenerator.Instance).SetSerializer(new StringSerializer(BsonType.ObjectId));
            map.MapMember(x => x.PremiumAmount).SetSerializer(new NullableSerializer<decimal>(new DecimalSerializer(BsonType.Decimal128)));
        });

        BsonClassMap.TryRegisterClassMap<SmsLogEntry>(map =>
        {
            map.AutoMap();
            map.MapIdMember(x => x.Id).SetIdGenerator(StringObjectIdGenerator.Instance).SetSerializer(new StringSerializer(BsonType.ObjectId));
        });

        BsonClassMap.TryRegisterClassMap<ProposalStepLog>(map =>
        {
            map.AutoMap();
            map.MapIdMember(x => x.Id).SetIdGenerator(StringObjectIdGenerator.Instance).SetSerializer(new StringSerializer(BsonType.ObjectId));
        });

        BsonClassMap.TryRegisterClassMap<WinsureTransaction>(map =>
        {
            map.AutoMap();
            map.MapIdMember(x => x.Id).SetIdGenerator(StringObjectIdGenerator.Instance).SetSerializer(new StringSerializer(BsonType.ObjectId));
        });

        BsonClassMap.TryRegisterClassMap<PaymentTransaction>(map =>
        {
            map.AutoMap();
            map.MapIdMember(x => x.Id).SetIdGenerator(StringObjectIdGenerator.Instance).SetSerializer(new StringSerializer(BsonType.ObjectId));
            map.MapMember(x => x.Amount).SetSerializer(new NullableSerializer<decimal>(new DecimalSerializer(BsonType.Decimal128)));
        });

        BsonClassMap.TryRegisterClassMap<ScenarioExecution>(map =>
        {
            map.AutoMap();
            map.MapIdMember(x => x.Id).SetIdGenerator(StringObjectIdGenerator.Instance).SetSerializer(new StringSerializer(BsonType.ObjectId));
        });

        BsonClassMap.TryRegisterClassMap<ChatSession>(map =>
        {
            map.AutoMap();
            map.MapIdMember(x => x.Id).SetIdGenerator(StringObjectIdGenerator.Instance).SetSerializer(new StringSerializer(BsonType.ObjectId));
        });

        BsonClassMap.TryRegisterClassMap<ChatMessage>(map =>
        {
            map.AutoMap();
            map.MapIdMember(x => x.Id).SetIdGenerator(StringObjectIdGenerator.Instance).SetSerializer(new StringSerializer(BsonType.ObjectId));
        });

        BsonClassMap.TryRegisterClassMap<Incident>(map =>
        {
            map.AutoMap();
            map.MapIdMember(x => x.Id).SetIdGenerator(StringObjectIdGenerator.Instance).SetSerializer(new StringSerializer(BsonType.ObjectId));
        });

        BsonClassMap.TryRegisterClassMap<KnowledgeCase>(map =>
        {
            map.AutoMap();
            map.MapIdMember(x => x.Id).SetIdGenerator(StringObjectIdGenerator.Instance).SetSerializer(new StringSerializer(BsonType.ObjectId));
        });

        // Scenario: Id zaten düz string (slug), ek eşleme gerekmez.

        BsonClassMap.TryRegisterClassMap<User>(map =>
        {
            map.AutoMap();
            map.MapIdMember(x => x.UserCode);      // userCode doğal anahtar: upsert kolay
        });
    }

    private sealed class UtcDateTimeConvention : ConventionBase, IMemberMapConvention
    {
        public void Apply(BsonMemberMap memberMap)
        {
            if (memberMap.MemberType == typeof(DateTime))
                memberMap.SetSerializer(new DateTimeSerializer(DateTimeKind.Utc));
            else if (memberMap.MemberType == typeof(DateTime?))
                memberMap.SetSerializer(new NullableSerializer<DateTime>(new DateTimeSerializer(DateTimeKind.Utc)));
        }
    }
}
