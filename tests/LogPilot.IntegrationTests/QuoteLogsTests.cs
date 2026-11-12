using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using LogPilot.Application.Ai;
using LogPilot.Application.Quotes;
using LogPilot.Application.Notifications;
using LogPilot.Application.Users;
using LogPilot.Domain.Quotes;
using LogPilot.LogGenerator.Config;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;

namespace LogPilot.IntegrationTests;

/// Gerçek MongoDB'de: quote_logs indeksleri, kimlik anahtarlarıyla VEYA eşleştirmesi, huni ve log detayı yedeği.
public class QuoteLogsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<IServiceScope> SeedAsync(params (string Scenario, int Seed, string User)[] runs)
    {
        _ = factory.CreateClient();
        var users = SeedFileLoader.LoadUsers(Path.Combine(RepoPaths.FindDirectory("seed"), "users.json"));
        await factory.Services.GetRequiredService<IUserRepository>().UpsertManyAsync(users, CancellationToken.None);
        foreach (var (scenario, seed, user) in runs)
            await Seed.RunScenarioAsync(factory.Services, scenario, DateTime.UtcNow.AddMinutes(-5), seed, user);
        return factory.Services.CreateScope();
    }

    [MongoFact]
    public async Task Startup_creates_the_quote_and_user_indexes()
    {
        _ = factory.CreateClient();
        var db = factory.Services.GetRequiredService<IMongoClient>().GetDatabase(factory.DatabaseName);

        var quoteIndexes = (await db.GetCollection<BsonDocument>("quote_logs").Indexes.List().ToListAsync()).Select(i => i["name"].AsString);
        quoteIndexes.Should().Contain(["ix_timestamp_ttl", "ix_quote_timestamp", "ix_customer_timestamp", "ix_identity_timestamp",
            "ix_user_timestamp", "ix_correlation", "ix_insurer_timestamp", "ix_errorcode_timestamp"]);

        var userIndexes = (await db.GetCollection<BsonDocument>("users").Indexes.List().ToListAsync()).Select(i => i["name"].AsString);
        userIndexes.Should().Contain(["ix_customerno", "ix_identityno"]);
    }

    [MongoFact]
    public async Task A_web_quote_is_found_for_the_customer_by_identity_number_only()
    {
        using var scope = await SeedAsync(("normal-quote-web", 11, "PAUL"));
        var tools = scope.ServiceProvider.GetRequiredService<ILogAnalysisTools>();
        var ctx = new ToolContext(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddMinutes(1));

        var journey = await tools.TraceCustomerJourneyAsync(new CustomerJourneyArgs(UserCode: "PAUL"), ctx, CancellationToken.None);

        journey.Customer!.CustomerNo.Should().Be("CUS-100109");
        journey.Customer.IdentityNoMasked.Should().Be("*********71");
        journey.Links.Should().Contain(l => l.Channel == "Web");
        journey.Timeline.Should().Contain(l => l.Source == LogSources.QuoteLogs && l.UserCode == null);
        JsonSerializer.Serialize(journey).Should().NotContain("99009071271");
    }

    [MongoFact]
    public async Task The_funnel_counts_partial_offers_and_the_failing_insurer()
    {
        using var scope = await SeedAsync(("quote-insurer-pricing-error", 21, "JOHN"));
        var quotes = scope.ServiceProvider.GetRequiredService<IQuoteLogRepository>();

        var funnel = await quotes.GetFunnelAsync(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddMinutes(1), null, QuoteChannel.Mobile, "INS_NRD", CancellationToken.None);

        funnel.QuotesWithPartialOffers.Should().BeGreaterThanOrEqualTo(1);
        funnel.Insurers.Should().Contain(i => i.InsurerCode == "INS_NRD" && i.Failed >= 1);
        funnel.Insurers.Should().Contain(i => i.InsurerCode == "INS_AKS" && i.AvgPremium > 0, "premiums round-trip as Decimal128");
    }

    [MongoFact]
    public async Task A_quote_log_id_opens_in_the_log_detail_endpoint()
    {
        using var scope = await SeedAsync(("quote-vehicle-registry-timeout", 31, "ELIF"));
        var quotes = scope.ServiceProvider.GetRequiredService<IQuoteLogRepository>();
        var failed = (await quotes.SearchAsync(new QuoteLogSearchRequest(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddMinutes(1),
            UserCodes: ["ELIF"], ErrorCode: "VEHICLE_REGISTRY_TIMEOUT"), CancellationToken.None)).Items.First();

        var json = await factory.CreateClient().GetFromJsonAsync<JsonElement>($"/api/logs/{failed.Id}");

        json.GetProperty("application").GetString().Should().Be("QuoteEngine");
        json.GetProperty("metadata").GetProperty("source").GetString().Should().Be(LogSources.QuoteLogs);
        json.GetProperty("metadata").GetProperty("quoteId").GetString().Should().Be(failed.QuoteId);
    }

    [MongoFact]
    public async Task Startup_creates_the_sms_indexes()
    {
        _ = factory.CreateClient();
        var db = factory.Services.GetRequiredService<IMongoClient>().GetDatabase(factory.DatabaseName);
        var names = (await db.GetCollection<BsonDocument>("sms_logs").Indexes.List().ToListAsync()).Select(i => i["name"].AsString);
        names.Should().Contain(["ix_timestamp_ttl", "ix_phone_timestamp", "ix_messageid", "ix_template_status_timestamp"]);
        (await db.GetCollection<BsonDocument>("quote_logs").Indexes.List().ToListAsync()).Select(i => i["name"].AsString).Should().Contain("ix_policyno");
    }

    [MongoFact]
    public async Task A_policy_sms_is_traced_from_the_policy_number_across_three_collections_in_mongo()
    {
        using var scope = await SeedAsync(("policy-sms-provider-failure", 41, "NINA"));
        var tools = scope.ServiceProvider.GetRequiredService<ILogAnalysisTools>();
        var ctx = new ToolContext(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddMinutes(1));
        var policyNo = (await scope.ServiceProvider.GetRequiredService<IQuoteLogRepository>().SearchAsync(
            new QuoteLogSearchRequest(ctx.From, ctx.To, UserCodes: [], IdentityNos: ["99008063352"], Step: "PolicyIssued"), CancellationToken.None)).Items.First().PolicyNo!;

        var trace = await tools.TraceNotificationDeliveryAsync(new NotificationTraceArgs(PolicyNo: policyNo), ctx, CancellationToken.None);

        trace.Path.Select(h => h.Source).Should().Equal("quote_logs", "users", "sms_logs");
        trace.Path.Should().OnlyContain(h => h.Found);
        trace.Customer!.UserCode.Should().Be("NINA");
        trace.Verdict.Should().Be("FailedToSend");
        trace.Evidence.Should().Contain(l => l.Source == LogSources.SmsLogs && l.ErrorCode == "SMS_PROVIDER_ERROR");
        JsonSerializer.Serialize(trace).Should().NotContain("+905551087832").And.NotContain("99008063352");

        var smsId = trace.Evidence.First(l => l.Source == LogSources.SmsLogs).Id;
        var json = await factory.CreateClient().GetFromJsonAsync<JsonElement>($"/api/logs/{smsId}");
        json.GetProperty("application").GetString().Should().Be("SmsGateway");
        json.GetProperty("metadata").GetProperty("phone").GetString().Should().StartWith("+90").And.Contain("*");
    }
}
