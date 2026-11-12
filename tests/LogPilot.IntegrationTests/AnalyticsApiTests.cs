using AwesomeAssertions;
using LogPilot.Domain.Logs;

namespace LogPilot.IntegrationTests;

/// Aggregation pipeline'larının GERÇEK MongoDB'de doğru sonuç verdiğini bilinen bir veri kümesiyle kanıtlar.
public class AnalyticsApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [MongoFact]
    public async Task Analytics_endpoints_match_the_seeded_data()
    {
        var client = factory.CreateClient();
        var t = DateTime.UtcNow.AddMinutes(-10);
        LogEntry E(string service, LogSeverity level, string? user = null, string? code = null,
            OperationOutcome? outcome = null, int? duration = null, int sec = 0)
            => Seed.Entry(t.AddSeconds(sec), level, service, user, code, outcome, duration);

        var logs = new List<LogEntry>();
        // AuthenticationService: 6 info (3 başarılı), 1 warning, 2 error  → 9 log
        logs.AddRange([
            E("AuthenticationService", LogSeverity.Information, "TCLI", outcome: OperationOutcome.Success, duration: 100, sec: 1),
            E("AuthenticationService", LogSeverity.Information, "ALEX", outcome: OperationOutcome.Success, duration: 200, sec: 2),
            E("AuthenticationService", LogSeverity.Information, "BOB", outcome: OperationOutcome.Success, duration: 300, sec: 3),
            E("AuthenticationService", LogSeverity.Information, sec: 4), E("AuthenticationService", LogSeverity.Information, sec: 5),
            E("AuthenticationService", LogSeverity.Information, sec: 6),
            E("AuthenticationService", LogSeverity.Warning, sec: 7),
            E("AuthenticationService", LogSeverity.Error, "TCLI", "TOKEN_EXPIRED", sec: 8),
            E("AuthenticationService", LogSeverity.Error, "ALEX", "TOKEN_EXPIRED", sec: 9)
        ]);
        // ProposalService: 4 info, 3 error (2'si Failure) → 7 log
        logs.AddRange([
            E("ProposalService", LogSeverity.Information, sec: 10), E("ProposalService", LogSeverity.Information, sec: 11),
            E("ProposalService", LogSeverity.Information, sec: 12), E("ProposalService", LogSeverity.Information, sec: 13),
            E("ProposalService", LogSeverity.Error, "U1", "REQUEST_TIMEOUT", OperationOutcome.Failure, sec: 14),
            E("ProposalService", LogSeverity.Error, "U2", "REQUEST_TIMEOUT", OperationOutcome.Failure, sec: 15),
            E("ProposalService", LogSeverity.Error, "U3", "REQUEST_TIMEOUT", sec: 16)
        ]);
        await Seed.AddAsync(factory.Services, logs.ToArray());

        // ---- özet: 16 log, 5 hata, 1 uyarı; 5 tamamlanan istek, 2'si başarısız ----
        var summary = await client.GetJsonAsync("/api/analytics/summary");
        summary.GetProperty("totalLogs").GetInt64().Should().Be(16);
        summary.GetProperty("errorCount").GetInt64().Should().Be(5);
        summary.GetProperty("warningCount").GetInt64().Should().Be(1);
        summary.GetProperty("logErrorRatio").GetDouble().Should().BeApproximately(5.0 / 16, 1e-9);
        var requests = summary.GetProperty("requests");
        requests.GetProperty("completed").GetInt64().Should().Be(5);
        requests.GetProperty("failed").GetInt64().Should().Be(2);
        requests.GetProperty("failureRate").GetDouble().Should().BeApproximately(0.4, 1e-9);   // log oranından FARKLI

        // ---- trend: kovalar toplamı veriyle tutarlı, boş kovalar sıfırla dolu ----
        var trend = await client.GetJsonAsync("/api/analytics/error-trend");
        trend.GetArrayLength().Should().BeInRange(96, 97);   // from kova sınırına hizalı değilse ilk kova kısmidir
        trend.EnumerateArray().Sum(p => p.GetProperty("total").GetInt64()).Should().Be(16);
        trend.EnumerateArray().Sum(p => p.GetProperty("errors").GetInt64()).Should().Be(5);
        trend.EnumerateArray().Sum(p => p.GetProperty("warnings").GetInt64()).Should().Be(1);

        // ---- servis dağılımı ----
        var byService = await client.GetJsonAsync("/api/analytics/errors-by-service");
        byService[0].GetProperty("service").GetString().Should().Be("ProposalService");
        byService[0].GetProperty("errors").GetInt64().Should().Be(3);
        byService[0].GetProperty("shareOfAllErrors").GetDouble().Should().BeApproximately(0.6, 1e-9);

        // ---- en sık hata kodları ----
        var top = await client.GetJsonAsync("/api/analytics/top-error-codes?limit=5");
        top[0].GetProperty("errorCode").GetString().Should().Be("REQUEST_TIMEOUT");
        top[0].GetProperty("count").GetInt64().Should().Be(3);
        top[0].GetProperty("distinctUsers").GetInt64().Should().Be(3);
        top[1].GetProperty("errorCode").GetString().Should().Be("TOKEN_EXPIRED");
        top[1].GetProperty("distinctUsers").GetInt64().Should().Be(2);

        // ---- servis sağlığı ----
        var health = await client.GetJsonAsync("/api/analytics/service-health");
        var proposal = health.EnumerateArray().Single(h => h.GetProperty("service").GetString() == "ProposalService");
        proposal.GetProperty("totalLogs").GetInt64().Should().Be(7);
        proposal.GetProperty("status").GetString().Should().Be("Unhealthy");   // 3/7 ≥ %20
        var auth = health.EnumerateArray().Single(h => h.GetProperty("service").GetString() == "AuthenticationService");
        auth.GetProperty("avgDurationMs").GetDouble().Should().BeApproximately(200, 1e-9);
        auth.GetProperty("maxDurationMs").GetInt64().Should().Be(300);

        // ---- hata grupları ----
        var groups = await client.GetJsonAsync("/api/analytics/error-groups");
        groups.GetArrayLength().Should().Be(2);
        groups[0].GetProperty("errorCode").GetString().Should().Be("REQUEST_TIMEOUT");
        groups[0].GetProperty("userCodes").GetArrayLength().Should().Be(3);
        groups[0].GetProperty("recentLogIds").GetArrayLength().Should().Be(3);
    }

    [MongoFact]
    public async Task Invalid_analytics_arguments_are_rejected()
    {
        var client = factory.CreateClient();

        (await client.GetAsync("/api/analytics/error-trend?bucketMinutes=7")).StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        (await client.GetAsync("/api/analytics/top-error-codes?limit=0")).StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        (await client.GetAsync($"/api/analytics/summary?from={DateTime.UtcNow:o}&to={DateTime.UtcNow.AddDays(-1):o}")).StatusCode
            .Should().Be(System.Net.HttpStatusCode.BadRequest);
    }
}
