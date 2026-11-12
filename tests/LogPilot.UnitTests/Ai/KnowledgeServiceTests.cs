using AwesomeAssertions;
using LogPilot.Application.Ai;
using LogPilot.Application.Chat;
using LogPilot.Application.Common;
using LogPilot.Application.Knowledge;
using LogPilot.Domain.Chat;
using LogPilot.Domain.Incidents;
using LogPilot.Domain.Knowledge;

namespace LogPilot.UnitTests.Ai;

public class KnowledgeServiceTests
{
    private readonly InMemoryKnowledge _cases = new();
    private readonly InMemoryChats _chats = new();
    private readonly KnowledgeService _sut;

    public KnowledgeServiceTests() => _sut = new KnowledgeService(_cases, _chats, new SensitiveDataMasker(), TimeProvider.System);

    private ChatMessage Answer(string question = "Why can't TCLI log in?") =>
        _chats.AddPair(question, new AssistantAnswer
        {
            Summary = "TCLI's token expired.",
            ProbableCause = "The access token had expired.",
            Evidence =
            [
                new EvidenceItem { ErrorCode = "LOGIN_FAILED", Service = "MobileGateway", Count = 1 },
                new EvidenceItem { ErrorCode = "token_expired", Service = "AuthenticationService", Count = 1 }
            ],
            AffectedServices = ["MobileGateway", "AuthenticationService"]
        });

    [Fact]
    public async Task Helpful_feedback_with_a_resolution_becomes_a_verified_case()
    {
        var answer = Answer();

        var updated = await _sut.SubmitFeedbackAsync(answer.Id!,
            new FeedbackInput(true, "Raised token lifetime; contact ops@example.com", null), CancellationToken.None);

        var saved = _cases.Items.Should().ContainSingle().Subject;
        saved.Source.Should().Be(KnowledgeSource.ChatFeedback);
        saved.SourceId.Should().Be(answer.Id);
        saved.ErrorCodes.Should().Equal("TOKEN_EXPIRED", "LOGIN_FAILED");   // açıklayıcı kod önce, büyük harf
        saved.Services.Should().Equal("MobileGateway", "AuthenticationService");
        saved.Title.Should().Be("The access token had expired.");
        saved.RootCause.Should().Be("The access token had expired.");
        saved.Symptom.Should().Be("Why can't TCLI log in?");
        saved.Resolution.Should().NotContain("ops@example.com", "kişisel veri hafızaya girmez");

        updated.Feedback!.Helpful.Should().BeTrue();
        updated.Feedback.KnowledgeCaseId.Should().Be(saved.Id);
    }

    [Fact]
    public async Task Helpful_feedback_without_a_resolution_is_not_remembered()
    {
        var answer = Answer();

        var updated = await _sut.SubmitFeedbackAsync(answer.Id!, new FeedbackInput(true, "  ", null), CancellationToken.None);

        _cases.Items.Should().BeEmpty();
        updated.Feedback!.KnowledgeCaseId.Should().BeNull();
    }

    [Fact]
    public async Task Negative_feedback_cannot_carry_a_resolution()
    {
        var answer = Answer();

        var act = () => _sut.SubmitFeedbackAsync(answer.Id!, new FeedbackInput(false, "something", null), CancellationToken.None);

        await act.Should().ThrowAsync<RequestValidationException>();
        _cases.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Confirming_the_same_answer_again_updates_the_same_case()
    {
        var answer = Answer();

        await _sut.SubmitFeedbackAsync(answer.Id!, new FeedbackInput(true, "First fix", null), CancellationToken.None);
        await _sut.SubmitFeedbackAsync(answer.Id!, new FeedbackInput(true, "Better fix", "Clock skew"), CancellationToken.None);

        var saved = _cases.Items.Should().ContainSingle().Subject;
        saved.Resolution.Should().Be("Better fix");
        saved.RootCause.Should().Be("Clock skew");
    }

    [Fact]
    public async Task Feedback_is_only_accepted_on_assistant_answers()
    {
        var answer = Answer();
        var question = _chats.Messages.First(m => m.Role == ChatRole.User);

        var act = () => _sut.SubmitFeedbackAsync(question.Id!, new FeedbackInput(true, "x", null), CancellationToken.None);
        await act.Should().ThrowAsync<RequestValidationException>();

        var missing = () => _sut.SubmitFeedbackAsync("507f1f77bcf86cd799439011", new FeedbackInput(true, null, null), CancellationToken.None);
        await missing.Should().ThrowAsync<NotFoundException>();
        answer.Should().NotBeNull();
    }

    [Fact]
    public async Task An_incident_resolution_note_becomes_one_case_per_incident()
    {
        var incident = new Incident
        {
            Id = "inc1", Title = "MONGO_CONNECTION_FAILED: 4 user(s)", ErrorCode = "MONGO_CONNECTION_FAILED",
            Severity = IncidentSeverity.Critical, Status = IncidentStatus.Resolved, AffectedServices = ["UserService"],
            FirstSeenAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };

        (await _sut.RecordIncidentResolutionAsync(incident, "  ", null, CancellationToken.None)).Should().BeNull();
        await _sut.RecordIncidentResolutionAsync(incident, "Restarted the primary", null, CancellationToken.None);
        await _sut.RecordIncidentResolutionAsync(incident, "Restarted the primary and raised pool size", "Pool exhausted", CancellationToken.None);

        var saved = _cases.Items.Should().ContainSingle().Subject;
        saved.Source.Should().Be(KnowledgeSource.IncidentResolution);
        saved.ErrorCodes.Should().Equal("MONGO_CONNECTION_FAILED");
        saved.Resolution.Should().Be("Restarted the primary and raised pool size");
        saved.RootCause.Should().Be("Pool exhausted");
    }

    [Fact]
    public async Task Related_cases_prefer_the_explanatory_error_code_and_ignore_answers_without_codes()
    {
        await _sut.CreateAsync(new("Symptom only", "Look at related logs", null, null, ["LOGIN_FAILED"], ["MobileGateway"]), CancellationToken.None);
        await _sut.CreateAsync(new("Token expired", "Re-login", null, null, ["TOKEN_EXPIRED"], ["AuthenticationService"]), CancellationToken.None);
        await _sut.CreateAsync(new("Unrelated", "n/a", null, null, ["SMS_PROVIDER_ERROR"], []), CancellationToken.None);

        var related = await _sut.FindRelatedAsync(["LOGIN_FAILED", "TOKEN_EXPIRED"], ["AuthenticationService"], 3, CancellationToken.None);

        related.Select(c => c.Title).Should().Equal("Token expired", "Symptom only");
        (await _sut.FindRelatedAsync([], ["AuthenticationService"], 3, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task Manual_cases_are_validated()
    {
        var noResolution = () => _sut.CreateAsync(new("Title", " ", null, null, null, null), CancellationToken.None);
        await noResolution.Should().ThrowAsync<RequestValidationException>();

        var badCode = () => _sut.CreateAsync(new("Title", "Fix", null, null, ["NOT A CODE!"], null), CancellationToken.None);
        await badCode.Should().ThrowAsync<RequestValidationException>();

        var created = await _sut.CreateAsync(new("Title", "Fix", null, null, ["token_expired"], null), CancellationToken.None);
        created.ErrorCodes.Should().Equal("TOKEN_EXPIRED");
        created.Source.Should().Be(KnowledgeSource.Manual);
    }

    [Fact]
    public async Task The_assistant_tool_needs_a_filter_and_returns_masked_cases()
    {
        await _sut.CreateAsync(new("Token expired", "Call +90 555 123 45 67 and re-login", null, null, ["TOKEN_EXPIRED"], []), CancellationToken.None);
        var tools = new LogAnalysisTools(null!, null!, null!, new SensitiveDataMasker(), _cases);

        var noFilter = () => tools.SearchKnowledgeAsync(new SearchKnowledgeArgs(), CancellationToken.None);
        await noFilter.Should().ThrowAsync<ToolArgumentException>();

        var result = await tools.SearchKnowledgeAsync(new SearchKnowledgeArgs(ErrorCode: "TOKEN_EXPIRED"), CancellationToken.None);
        var found = result.Cases.Should().ContainSingle().Subject;
        found.Resolution.Should().NotContain("555 123 45 67");
    }

    // ---- basit bellek içi depolar ----

    private sealed class InMemoryKnowledge : IKnowledgeRepository
    {
        public List<KnowledgeCase> Items { get; } = [];
        private int _next;

        public Task<KnowledgeCase> InsertAsync(KnowledgeCase c, CancellationToken ct)
        {
            var stored = new KnowledgeCase
            {
                Id = $"{++_next:x24}", Title = c.Title, ErrorCodes = c.ErrorCodes, Services = c.Services, Symptom = c.Symptom,
                RootCause = c.RootCause, Resolution = c.Resolution, Source = c.Source, SourceId = c.SourceId,
                CreatedAt = c.CreatedAt, UpdatedAt = c.UpdatedAt
            };
            Items.Add(stored);
            return Task.FromResult(stored);
        }

        public Task<KnowledgeCase?> GetByIdAsync(string id, CancellationToken ct) => Task.FromResult(Items.FirstOrDefault(c => c.Id == id));

        public Task<KnowledgeCase?> FindBySourceAsync(KnowledgeSource source, string sourceId, CancellationToken ct)
            => Task.FromResult(Items.FirstOrDefault(c => c.Source == source && c.SourceId == sourceId));

        public Task ReplaceAsync(KnowledgeCase c, CancellationToken ct)
        {
            Items[Items.FindIndex(x => x.Id == c.Id)] = c;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(string id, CancellationToken ct) => Task.FromResult(Items.RemoveAll(c => c.Id == id) > 0);

        public Task<PagedResult<KnowledgeCase>> SearchAsync(KnowledgeQuery q, CancellationToken ct)
        {
            var codes = q.ErrorCodes?.Select(c => c.ToUpperInvariant()).ToHashSet();
            var matches = Items.Where(c =>
                    (codes is null || c.ErrorCodes.Any(codes.Contains))
                    && (q.Services is null || c.Services.Any(s => q.Services.Contains(s, StringComparer.OrdinalIgnoreCase)))
                    && (q.Text is null || c.Title.Contains(q.Text, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(c => c.UpdatedAt).ToList();
            return Task.FromResult(new PagedResult<KnowledgeCase>(matches.Take(q.PageSize).ToList(), matches.Count, q.Page, q.PageSize));
        }

        public Task MarkSuggestedAsync(IReadOnlyCollection<string> ids, DateTime nowUtc, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class InMemoryChats : IChatRepository
    {
        public List<ChatMessage> Messages { get; } = [];
        private readonly Dictionary<string, MessageFeedback> _feedback = [];
        private int _next;

        public ChatMessage AddPair(string question, AssistantAnswer answer)
        {
            var now = DateTime.UtcNow;
            Messages.Add(new ChatMessage { Id = $"{++_next:x24}", SessionId = "s1", Role = ChatRole.User, Content = question, CreatedAt = now.AddSeconds(-1) });
            var reply = new ChatMessage { Id = $"{++_next:x24}", SessionId = "s1", Role = ChatRole.Assistant, Content = answer.Summary, Answer = answer, CreatedAt = now };
            Messages.Add(reply);
            return reply;
        }

        public Task<ChatMessage?> GetMessageAsync(string id, CancellationToken ct)
        {
            var m = Messages.FirstOrDefault(x => x.Id == id);
            if (m is null) return Task.FromResult<ChatMessage?>(null);
            return Task.FromResult<ChatMessage?>(_feedback.TryGetValue(id, out var f)
                ? new ChatMessage { Id = m.Id, SessionId = m.SessionId, Role = m.Role, Content = m.Content, Answer = m.Answer, CreatedAt = m.CreatedAt, Feedback = f }
                : m);
        }

        public Task SetFeedbackAsync(string messageId, MessageFeedback feedback, CancellationToken ct)
        {
            _feedback[messageId] = feedback;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(string sessionId, int limit, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ChatMessage>>(Messages.Where(m => m.SessionId == sessionId).ToList());

        public Task<ChatSession> CreateSessionAsync(string userName, string title, DateTime nowUtc, CancellationToken ct) => throw new NotSupportedException();
        public Task<ChatSession?> GetSessionAsync(string id, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ChatSession>> ListSessionsAsync(int limit, CancellationToken ct) => throw new NotSupportedException();
        public Task TouchSessionAsync(string id, string? newTitle, DateTime nowUtc, CancellationToken ct) => throw new NotSupportedException();
        public Task<ChatMessage> AddMessageAsync(ChatMessage message, CancellationToken ct) => throw new NotSupportedException();
    }
}
