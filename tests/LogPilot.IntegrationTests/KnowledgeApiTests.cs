using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using LogPilot.Application.Incidents;
using Microsoft.Extensions.DependencyInjection;

namespace LogPilot.IntegrationTests;

/// Vaka hafızası uçtan uca: cevap → 👍 + çözüm → yeni sohbette aynı sorun → geçmiş vaka önerilir.
public class KnowledgeApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [MongoFact]
    public async Task A_confirmed_fix_is_suggested_in_a_later_conversation()
    {
        await Seed.RunScenarioAsync(factory.Services, "token-expired", DateTime.UtcNow.AddMinutes(-5), 1, "TCLI");
        var client = factory.CreateClient();

        // 1) İlk sohbet: henüz hafıza boş.
        var first = await Ask(client, "Why can't TCLI log in to the mobile application?");
        first.GetProperty("answer").GetProperty("relatedCases").GetArrayLength().Should().Be(0);
        var messageId = first.GetProperty("id").GetString();

        // 2) 👍 + gerçek çözüm → hafızaya girer.
        var feedback = await client.SendJsonAsync(HttpMethod.Post, $"/api/chat/messages/{messageId}/feedback",
            new { helpful = true, resolution = "Raised the access token lifetime to 30 minutes; user signed in again." });
        feedback.Status.Should().Be(HttpStatusCode.OK);
        var caseId = feedback.Body.GetProperty("feedback").GetProperty("knowledgeCaseId").GetString();
        caseId.Should().NotBeNullOrEmpty();

        var stored = await client.GetJsonAsync($"/api/knowledge/{caseId}");
        stored.GetProperty("source").GetString().Should().Be("ChatFeedback");
        stored.GetProperty("errorCodes").EnumerateArray().Select(e => e.GetString()).Should().Contain("TOKEN_EXPIRED");

        // 3) Yeni sohbet, aynı sorun: geçmiş vaka backend tarafından eklenir ve sayacı artar.
        var second = await Ask(client, "TCLI cannot log in again, why?");
        var related = second.GetProperty("answer").GetProperty("relatedCases");
        related.EnumerateArray().Should().Contain(c => c.GetProperty("id").GetString() == caseId);
        related[0].GetProperty("resolution").GetString().Should().Contain("token lifetime");
        (await client.GetJsonAsync($"/api/knowledge/{caseId}")).GetProperty("timesSuggested").GetInt64().Should().BeGreaterThan(0);

        // 4) Arama ve silme.
        var search = await client.GetJsonAsync("/api/knowledge?errorCode=token_expired");
        search.GetProperty("items").EnumerateArray().Should().Contain(c => c.GetProperty("id").GetString() == caseId);
        (await client.DeleteAsync($"/api/knowledge/{caseId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.DeleteAsync($"/api/knowledge/{caseId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [MongoFact]
    public async Task Resolving_an_incident_with_a_note_records_a_case()
    {
        var client = factory.CreateClient();
        await Seed.RunScenarioAsync(factory.Services, "mongo-outage", DateTime.UtcNow.AddMinutes(-2), 5);
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IncidentDetectionService>().DetectAsync(DateTime.UtcNow, 15, CancellationToken.None);

        var incident = (await client.GetJsonAsync("/api/incidents?activeOnly=true")).GetProperty("items").EnumerateArray()
            .First(i => i.GetProperty("errorCode").GetString() == "MONGO_CONNECTION_FAILED");
        var id = incident.GetProperty("id").GetString();

        // Not yalnızca çözülürken verilebilir.
        (await client.SendJsonAsync(HttpMethod.Patch, $"/api/incidents/{id}/status",
            new { status = "Investigating", resolutionNote = "x" })).Status.Should().Be(HttpStatusCode.BadRequest);

        var resolved = await client.SendJsonAsync(HttpMethod.Patch, $"/api/incidents/{id}/status",
            new { status = "Resolved", resolutionNote = "Restarted the MongoDB primary.", rootCause = "Connection pool exhausted" });
        resolved.Status.Should().Be(HttpStatusCode.OK);
        resolved.Body.GetProperty("resolutionNote").GetString().Should().Be("Restarted the MongoDB primary.");

        var cases = await client.GetJsonAsync("/api/knowledge?errorCode=MONGO_CONNECTION_FAILED");
        var saved = cases.GetProperty("items").EnumerateArray().Should().ContainSingle().Subject;
        saved.GetProperty("source").GetString().Should().Be("IncidentResolution");
        saved.GetProperty("rootCause").GetString().Should().Be("Connection pool exhausted");
    }

    [MongoFact]
    public async Task Manual_cases_can_be_created_edited_and_validated()
    {
        var client = factory.CreateClient();

        (await client.SendJsonAsync(HttpMethod.Post, "/api/knowledge", new { title = "No fix" })).Status.Should().Be(HttpStatusCode.BadRequest);

        var created = await client.SendJsonAsync(HttpMethod.Post, "/api/knowledge",
            new { title = "Pricing timeouts", resolution = "Scaled PricingService", errorCodes = new[] { "request_timeout" }, services = new[] { "PricingService" } });
        created.Status.Should().Be(HttpStatusCode.Created);
        created.Body.GetProperty("errorCodes")[0].GetString().Should().Be("REQUEST_TIMEOUT");
        var id = created.Body.GetProperty("id").GetString();

        var updated = await client.SendJsonAsync(HttpMethod.Put, $"/api/knowledge/{id}",
            new { title = "Pricing timeouts", resolution = "Scaled PricingService to 3 replicas", errorCodes = new[] { "REQUEST_TIMEOUT" } });
        updated.Status.Should().Be(HttpStatusCode.OK);
        updated.Body.GetProperty("resolution").GetString().Should().EndWith("3 replicas");
        updated.Body.GetProperty("source").GetString().Should().Be("Manual");

        (await client.SendJsonAsync(HttpMethod.Post, "/api/chat/messages/507f1f77bcf86cd799439011/feedback", new { helpful = true }))
            .Status.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<JsonElement> Ask(HttpClient client, string question)
    {
        var session = (await client.SendJsonAsync(HttpMethod.Post, "/api/chat/sessions", new { })).Body.GetProperty("id").GetString();
        var reply = await client.SendJsonAsync(HttpMethod.Post, $"/api/chat/sessions/{session}/messages", new { content = question });
        reply.Status.Should().Be(HttpStatusCode.OK);
        return reply.Body;
    }
}
