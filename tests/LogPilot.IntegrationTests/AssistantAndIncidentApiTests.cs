using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using LogPilot.Application.Incidents;
using Microsoft.Extensions.DependencyInjection;

namespace LogPilot.IntegrationTests;

public class AssistantApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [MongoFact]
    public async Task The_assistant_answers_from_real_logs_and_keeps_history()
    {
        await Seed.RunScenarioAsync(factory.Services, "token-expired", DateTime.UtcNow.AddMinutes(-5), 1, "TCLI");
        var client = factory.CreateClient();

        var session = await client.SendJsonAsync(HttpMethod.Post, "/api/chat/sessions", new { });
        session.Status.Should().Be(HttpStatusCode.Created);
        var id = session.Body.GetProperty("id").GetString();

        var reply = await client.SendJsonAsync(HttpMethod.Post, $"/api/chat/sessions/{id}/messages",
            new { content = "Why can't TCLI log in to the mobile application?" });

        reply.Status.Should().Be(HttpStatusCode.OK);
        var answer = reply.Body.GetProperty("answer");
        answer.GetProperty("probableCause").GetString().Should().NotBeNullOrWhiteSpace();
        answer.GetProperty("confidence").GetString().Should().BeOneOf("Medium", "High");
        answer.GetProperty("evidence").EnumerateArray().Should().Contain(e => e.GetProperty("errorCode").GetString() == "TOKEN_EXPIRED");
        answer.GetProperty("suggestedActions").GetArrayLength().Should().BeGreaterThan(0);
        reply.Body.GetProperty("toolExecutions").GetArrayLength().Should().BeGreaterThan(0);

        var history = await client.GetJsonAsync($"/api/chat/sessions/{id}/messages");
        history.GetArrayLength().Should().Be(2);
        history[0].GetProperty("role").GetString().Should().Be("User");
        history[1].GetProperty("role").GetString().Should().Be("Assistant");

        var sessions = await client.GetJsonAsync("/api/chat/sessions");
        sessions.EnumerateArray().Should().Contain(s => s.GetProperty("title").GetString()!.StartsWith("Why can't TCLI"));
    }

    [MongoFact]
    public async Task A_time_range_in_the_question_overrides_the_selected_range()
    {
        // LENA'nın hatası 40 dk önce: arayüzün varsayılan 24 saatinde var, "son 15 dakika"da yok.
        await Seed.RunScenarioAsync(factory.Services, "token-expired", DateTime.UtcNow.AddMinutes(-40), 1, "LENA");
        var client = factory.CreateClient();
        var id = (await client.SendJsonAsync(HttpMethod.Post, "/api/chat/sessions", new { })).Body.GetProperty("id").GetString();

        var reply = await client.SendJsonAsync(HttpMethod.Post, $"/api/chat/sessions/{id}/messages",
            new { content = "Son 15 dakikada LENA neden giriş yapamadı?" });

        reply.Status.Should().Be(HttpStatusCode.OK);
        var answer = reply.Body.GetProperty("answer");
        // Aynı veritabanında başka testlerin yakın zamanlı logları olabilir; önemli olan 40 dk önceki LENA loglarının dışarıda kalması.
        answer.GetProperty("evidence").EnumerateArray()
            .Should().NotContain(e => e.GetProperty("userCodes").EnumerateArray().Any(u => u.GetString() == "LENA"));
        answer.GetProperty("dataLimitations").EnumerateArray()
            .Should().Contain(l => l.GetString()!.Contains("taken from the question: \"Son 15 dakikada\""));

        var question = (await client.GetJsonAsync($"/api/chat/sessions/{id}/messages"))[0];
        var from = question.GetProperty("rangeFrom").GetDateTime();
        var to = question.GetProperty("rangeTo").GetDateTime();
        (to - from).Should().Be(TimeSpan.FromMinutes(15));
    }

    [MongoFact]
    public async Task Missing_evidence_means_insufficient_not_a_guess()
    {
        var client = factory.CreateClient();
        var id = (await client.SendJsonAsync(HttpMethod.Post, "/api/chat/sessions", new { })).Body.GetProperty("id").GetString();

        var reply = await client.SendJsonAsync(HttpMethod.Post, $"/api/chat/sessions/{id}/messages",
            new { content = "Why can't NOBODY log in?" });

        var answer = reply.Body.GetProperty("answer");
        answer.GetProperty("confidence").GetString().Should().Be("Insufficient");
        var hasCause = answer.TryGetProperty("probableCause", out var cause) && cause.ValueKind != JsonValueKind.Null;
        hasCause.Should().BeFalse("no cause may be invented");
    }

    [MongoFact]
    public async Task Chat_input_is_validated()
    {
        var client = factory.CreateClient();
        var id = (await client.SendJsonAsync(HttpMethod.Post, "/api/chat/sessions", new { })).Body.GetProperty("id").GetString();

        (await client.SendJsonAsync(HttpMethod.Post, $"/api/chat/sessions/{id}/messages", new { content = "  " })).Status.Should().Be(HttpStatusCode.BadRequest);
        (await client.SendJsonAsync(HttpMethod.Post, $"/api/chat/sessions/{id}/messages", new { content = new string('a', 2001) })).Status.Should().Be(HttpStatusCode.BadRequest);
        (await client.SendJsonAsync(HttpMethod.Post, $"/api/chat/sessions/{id}/messages",
            new { content = "hello", from = DateTime.UtcNow.AddDays(-30), to = DateTime.UtcNow })).Status.Should().Be(HttpStatusCode.BadRequest);
        (await client.SendJsonAsync(HttpMethod.Post, "/api/chat/sessions/507f1f77bcf86cd799439011/messages", new { content = "hello" })).Status.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/api/chat/sessions/not-an-id/messages")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

public class IncidentApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [MongoFact]
    public async Task A_multi_user_outage_becomes_one_critical_incident_that_a_human_resolves()
    {
        var client = factory.CreateClient();
        await Seed.RunScenarioAsync(factory.Services, "mongo-outage", DateTime.UtcNow.AddMinutes(-2), 5);

        using (var scope = factory.Services.CreateScope())
        {
            var detection = scope.ServiceProvider.GetRequiredService<IncidentDetectionService>();
            var result = await detection.DetectAsync(DateTime.UtcNow, 15, CancellationToken.None);
            result.Created.Should().Be(1, "LOGIN_FAILED is only a symptom and must not open its own incident");

            // Tekrar çalıştırmak yeni incident açmaz.
            (await detection.DetectAsync(DateTime.UtcNow, 15, CancellationToken.None)).Created.Should().Be(0);
        }

        var active = await client.GetJsonAsync("/api/incidents?activeOnly=true");
        active.GetProperty("totalCount").GetInt64().Should().Be(1);
        var incident = active.GetProperty("items")[0];
        incident.GetProperty("errorCode").GetString().Should().Be("MONGO_CONNECTION_FAILED");
        incident.GetProperty("severity").GetString().Should().Be("Critical");
        incident.GetProperty("status").GetString().Should().Be("Open");
        incident.GetProperty("aiSummary").GetString().Should().Contain("MONGO_CONNECTION_FAILED").And.Contain("does not confirm");
        var id = incident.GetProperty("id").GetString();

        var logs = await client.GetJsonAsync($"/api/incidents/{id}/logs");
        logs.GetArrayLength().Should().BeGreaterThan(0);

        var resolved = await client.SendJsonAsync(HttpMethod.Patch, $"/api/incidents/{id}/status", new { status = "Resolved" });
        resolved.Status.Should().Be(HttpStatusCode.OK);
        resolved.Body.GetProperty("status").GetString().Should().Be("Resolved");
        resolved.Body.GetProperty("resolvedAt").ValueKind.Should().NotBe(JsonValueKind.Null);

        (await client.GetJsonAsync("/api/incidents?activeOnly=true")).GetProperty("totalCount").GetInt64().Should().Be(0);
        (await client.SendJsonAsync(HttpMethod.Patch, "/api/incidents/507f1f77bcf86cd799439011/status", new { status = "Open" })).Status.Should().Be(HttpStatusCode.NotFound);
        (await client.SendJsonAsync(HttpMethod.Post, $"/api/incidents/{id}/summary")).Status.Should().Be(HttpStatusCode.OK);
    }
}
