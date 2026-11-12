using System.Net;
using AwesomeAssertions;
using LogPilot.Domain.Logs;

namespace LogPilot.IntegrationTests;

public class LogsApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [MongoFact]
    public async Task Search_filters_paginates_and_sorts()
    {
        var client = factory.CreateClient();
        var user = Seed.Unique("U");
        var start = DateTime.UtcNow.AddMinutes(-30);
        await Seed.AddAsync(factory.Services,
            Enumerable.Range(0, 5).Select(i => Seed.Entry(start.AddMinutes(i), LogSeverity.Information, user: user)).ToArray());

        var page2 = await client.GetJsonAsync($"/api/logs?userCode={user}&pageSize=2&page=2");
        page2.GetProperty("totalCount").GetInt64().Should().Be(5);
        page2.GetProperty("totalPages").GetInt32().Should().Be(3);
        page2.GetProperty("items").GetArrayLength().Should().Be(2);

        var desc = await client.GetJsonAsync($"/api/logs?userCode={user}&pageSize=5");
        var asc = await client.GetJsonAsync($"/api/logs?userCode={user}&pageSize=5&ascending=true");
        desc.GetProperty("items")[0].GetProperty("timestamp").GetDateTime()
            .Should().BeAfter(asc.GetProperty("items")[0].GetProperty("timestamp").GetDateTime());
    }

    [MongoFact]
    public async Task Lookups_by_id_user_correlation_and_transaction()
    {
        var client = factory.CreateClient();
        var user = Seed.Unique("U");
        var correlation = "COR-" + Seed.Unique("T");
        var start = DateTime.UtcNow.AddMinutes(-20);
        await Seed.AddAsync(factory.Services,
            Seed.Entry(start, LogSeverity.Information, user: user, correlationId: correlation),
            Seed.Entry(start.AddSeconds(1), LogSeverity.Error, "MobileGateway", user, "LOGIN_FAILED", correlationId: correlation));

        var flow = await client.GetJsonAsync($"/api/logs/by-correlation/{correlation}");
        flow.GetProperty("items").GetArrayLength().Should().Be(2);
        flow.GetProperty("items")[0].GetProperty("level").GetString().Should().Be("Information");   // kronolojik

        var byUser = await client.GetJsonAsync($"/api/logs/by-user/{user}");
        byUser.GetProperty("totalCount").GetInt64().Should().Be(2);

        var id = flow.GetProperty("items")[0].GetProperty("id").GetString();
        var detail = await client.GetJsonAsync($"/api/logs/{id}");
        detail.GetProperty("userCode").GetString().Should().Be(user);

        (await client.GetAsync("/api/logs/not-an-id")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/api/logs/507f1f77bcf86cd799439011")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [MongoFact]
    public async Task Validation_problems_are_returned_as_problem_details()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/logs?pageSize=500");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("PageSize");
    }

    [MongoFact]
    public async Task Recent_errors_only_returns_error_and_critical_logs()
    {
        var client = factory.CreateClient();
        var now = DateTime.UtcNow.AddMinutes(-5);
        await Seed.AddAsync(factory.Services,
            Seed.Entry(now, LogSeverity.Information), Seed.Entry(now, LogSeverity.Warning),
            Seed.Entry(now, LogSeverity.Error, errorCode: "E1"), Seed.Entry(now, LogSeverity.Critical, errorCode: "E2"));

        var recent = await client.GetJsonAsync("/api/logs/recent-errors?limit=100");

        recent.EnumerateArray().Should().NotBeEmpty();
        recent.EnumerateArray().Select(e => e.GetProperty("level").GetString()).Should().OnlyContain(l => l == "Error" || l == "Critical");
    }

    [MongoFact]
    public async Task A_valid_correlation_header_is_echoed_and_an_invalid_one_is_replaced()
    {
        var client = factory.CreateClient();

        var ok = new HttpRequestMessage(HttpMethod.Get, "/api/logs?pageSize=1");
        ok.Headers.Add("X-Correlation-Id", "test-123");
        (await client.SendAsync(ok)).Headers.GetValues("X-Correlation-Id").Single().Should().Be("test-123");

        var bad = new HttpRequestMessage(HttpMethod.Get, "/api/logs?pageSize=1");
        bad.Headers.Add("X-Correlation-Id", "x y\"z");   // log sahteciliği denemesi
        var echoed = (await client.SendAsync(bad)).Headers.GetValues("X-Correlation-Id").Single();
        echoed.Should().NotBe("x y\"z").And.HaveLength(32);
    }
}
