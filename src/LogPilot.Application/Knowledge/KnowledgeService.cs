using LogPilot.Application.Ai;
using LogPilot.Application.Chat;
using LogPilot.Application.Common;
using LogPilot.Domain.Chat;
using LogPilot.Domain.Incidents;
using LogPilot.Domain.Knowledge;
using LogPilot.Domain.Logs;

namespace LogPilot.Application.Knowledge;

public sealed record KnowledgeCaseInput(
    string? Title, string? Resolution, string? RootCause, string? Symptom,
    IReadOnlyList<string>? ErrorCodes, IReadOnlyList<string>? Services);

public sealed record FeedbackInput(bool Helpful, string? Resolution, string? RootCause);

/// Doğrulanmış vaka hafızası. Kurallar:
/// - Hafızaya yalnızca bir İNSANIN onayladığı çözüm girer (👍 + çözüm notu, incident çözüm notu veya elle ekleme).
///   Model kendi cevabını hafızaya yazamaz; böylece kendi hatalarını "öğrenmez".
/// - Kaydedilen her metin maskelenir (sır/kişisel veri hafızaya girmez).
/// - Geçmiş vaka yeni cevaplarda İPUCU'dur: kanıt sayılmaz, güven puanını etkilemez.
public sealed class KnowledgeService(
    IKnowledgeRepository cases,
    IChatRepository chats,
    SensitiveDataMasker masker,
    TimeProvider time)
{
    public const int MaxTitle = 200;
    public const int MaxText = 2000;
    public const int MaxCodes = 10;
    public const int MaxPageSize = 100;
    public const int RelatedLimit = 3;

    public Task<PagedResult<KnowledgeCase>> SearchAsync(string? errorCode, string? service, string? text, int page, int pageSize, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (page < 1) errors["Page"] = ["Page must be at least 1."];
        if (pageSize is < 1 or > MaxPageSize) errors["PageSize"] = [$"PageSize must be between 1 and {MaxPageSize}."];
        if (text?.Length > 100) errors["Q"] = ["Search text must be at most 100 characters."];
        if (errors.Count > 0) throw new RequestValidationException(errors);

        return cases.SearchAsync(new KnowledgeQuery(
            Clean(errorCode) is { } code ? [code] : null,
            Clean(service) is { } svc ? [svc] : null,
            Clean(text), page, pageSize), ct);
    }

    public Task<KnowledgeCase?> GetAsync(string id, CancellationToken ct) => cases.GetByIdAsync(id, ct);

    public async Task<KnowledgeCase> CreateAsync(KnowledgeCaseInput input, CancellationToken ct)
    {
        var now = Now();
        var validated = Validate(input, requireTitle: true);
        return await cases.InsertAsync(new KnowledgeCase
        {
            Title = validated.Title!, Resolution = validated.Resolution, RootCause = validated.RootCause, Symptom = validated.Symptom,
            ErrorCodes = validated.ErrorCodes, Services = validated.Services,
            Source = KnowledgeSource.Manual, CreatedAt = now, UpdatedAt = now
        }, ct);
    }

    public async Task<KnowledgeCase> UpdateAsync(string id, KnowledgeCaseInput input, CancellationToken ct)
    {
        var existing = await cases.GetByIdAsync(id, ct) ?? throw new NotFoundException($"Knowledge case '{id}' was not found.");
        var validated = Validate(input, requireTitle: true);

        existing.Title = validated.Title!;
        existing.Resolution = validated.Resolution;
        existing.RootCause = validated.RootCause;
        existing.Symptom = validated.Symptom;
        existing.ErrorCodes = validated.ErrorCodes;
        existing.Services = validated.Services;
        existing.UpdatedAt = Now();
        await cases.ReplaceAsync(existing, ct);
        return existing;
    }

    public async Task DeleteAsync(string id, CancellationToken ct)
    {
        if (!await cases.DeleteAsync(id, ct)) throw new NotFoundException($"Knowledge case '{id}' was not found.");
    }

    /// Asistan cevabına geri bildirim. 👍 + çözüm notu → vaka hafızasına girer (aynı mesaj tekrar onaylanırsa güncellenir).
    /// 👍 notsuz veya 👎 → yalnızca geri bildirim olarak saklanır.
    public async Task<ChatMessage> SubmitFeedbackAsync(string messageId, FeedbackInput input, CancellationToken ct)
    {
        var message = await chats.GetMessageAsync(messageId, ct) ?? throw new NotFoundException($"Message '{messageId}' was not found.");
        if (message.Role != ChatRole.Assistant || message.Answer is null)
            throw Invalid("MessageId", "Feedback can only be given on assistant answers.");

        var resolution = Clean(input.Resolution);
        var rootCause = Clean(input.RootCause);
        if (!input.Helpful && resolution is not null)
            throw Invalid("Resolution", "A resolution can only be saved with positive feedback.");
        Length("Resolution", resolution, MaxText);
        Length("RootCause", rootCause, MaxText);

        string? caseId = null;
        if (input.Helpful && resolution is not null)
        {
            var question = await FindQuestionAsync(message, ct);
            var answer = message.Answer;
            var now = Now();
            var codes = answer.Evidence.Select(e => e.ErrorCode).OfType<string>().Select(c => c.ToUpperInvariant())
                .OrderBy(c => ErrorCodeCatalog.IsSymptom(c) ? 1 : 0)   // açıklayıcı kodlar önce
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxCodes).ToList();

            var existing = await cases.FindBySourceAsync(KnowledgeSource.ChatFeedback, messageId, ct);
            var knowledgeCase = existing ?? new KnowledgeCase
            {
                Title = "", Resolution = "", Source = KnowledgeSource.ChatFeedback, SourceId = messageId, CreatedAt = now, UpdatedAt = now
            };
            knowledgeCase.Title = Truncate(masker.MaskRequired(answer.ProbableCause ?? answer.Summary), MaxTitle);
            knowledgeCase.Resolution = masker.MaskRequired(resolution);
            knowledgeCase.RootCause = masker.Mask(rootCause ?? answer.ProbableCause);
            knowledgeCase.Symptom = question is null ? null : Truncate(masker.MaskRequired(question), MaxText);
            knowledgeCase.ErrorCodes = codes;
            knowledgeCase.Services = answer.AffectedServices.Distinct(StringComparer.Ordinal).Take(MaxCodes).ToList();
            knowledgeCase.UpdatedAt = now;

            if (existing is null) knowledgeCase = await cases.InsertAsync(knowledgeCase, ct);
            else await cases.ReplaceAsync(knowledgeCase, ct);
            caseId = knowledgeCase.Id;
        }

        var feedback = new MessageFeedback
        {
            Helpful = input.Helpful,
            Resolution = resolution is null ? null : masker.MaskRequired(resolution),
            KnowledgeCaseId = caseId,
            CreatedAt = Now()
        };
        await chats.SetFeedbackAsync(messageId, feedback, ct);
        return await chats.GetMessageAsync(messageId, ct) ?? message;
    }

    /// Incident "Resolved" yapılırken yazılan çözüm notu → vaka hafızası (aynı incident için tek vaka, güncellenir).
    public async Task<KnowledgeCase?> RecordIncidentResolutionAsync(Incident incident, string? note, string? rootCause, CancellationToken ct)
    {
        var resolution = Clean(note);
        if (resolution is null || incident.Id is null) return null;
        Length("ResolutionNote", resolution, MaxText);
        Length("RootCause", Clean(rootCause), MaxText);

        var now = Now();
        var existing = await cases.FindBySourceAsync(KnowledgeSource.IncidentResolution, incident.Id, ct);
        var knowledgeCase = existing ?? new KnowledgeCase
        {
            Title = "", Resolution = "", Source = KnowledgeSource.IncidentResolution, SourceId = incident.Id, CreatedAt = now, UpdatedAt = now
        };
        knowledgeCase.Title = Truncate(masker.MaskRequired(incident.Title), MaxTitle);
        knowledgeCase.Resolution = masker.MaskRequired(resolution);
        knowledgeCase.RootCause = masker.Mask(Clean(rootCause));
        knowledgeCase.Symptom = incident.AiSummary is null ? null : Truncate(masker.MaskRequired(incident.AiSummary), MaxText);
        knowledgeCase.ErrorCodes = [incident.ErrorCode.ToUpperInvariant()];
        knowledgeCase.Services = incident.AffectedServices.Take(MaxCodes).ToList();
        knowledgeCase.UpdatedAt = now;

        if (existing is null) return await cases.InsertAsync(knowledgeCase, ct);
        await cases.ReplaceAsync(knowledgeCase, ct);
        return knowledgeCase;
    }

    /// Bir cevaba eklenecek ilgili geçmiş vakalar: önce aynı açıklayıcı hata kodu, sonra ortak servis sayısı, sonra yenilik.
    /// Hata kodu yoksa hiçbir şey önerilmez (yalnızca servis eşleşmesi çok gürültülü).
    public async Task<IReadOnlyList<KnowledgeCase>> FindRelatedAsync(
        IReadOnlyCollection<string> errorCodes, IReadOnlyCollection<string> services, int limit, CancellationToken ct)
    {
        var codes = errorCodes.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (codes.Count == 0) return [];

        var found = await cases.SearchAsync(new KnowledgeQuery(ErrorCodes: codes, PageSize: 50), ct);
        var serviceSet = new HashSet<string>(services, StringComparer.OrdinalIgnoreCase);
        var codeSet = new HashSet<string>(codes, StringComparer.OrdinalIgnoreCase);

        return found.Items
            .Select(c => new
            {
                Case = c,
                Explanatory = c.ErrorCodes.Count(x => codeSet.Contains(x) && !ErrorCodeCatalog.IsSymptom(x)),
                Codes = c.ErrorCodes.Count(codeSet.Contains),
                Services = c.Services.Count(serviceSet.Contains)
            })
            .Where(x => x.Codes > 0)
            .OrderByDescending(x => x.Explanatory).ThenByDescending(x => x.Codes).ThenByDescending(x => x.Services)
            .ThenByDescending(x => x.Case.UpdatedAt)
            .Take(Math.Clamp(limit, 1, 10))
            .Select(x => x.Case)
            .ToList();
    }

    public Task MarkSuggestedAsync(IReadOnlyCollection<string> ids, CancellationToken ct) =>
        ids.Count == 0 ? Task.CompletedTask : cases.MarkSuggestedAsync(ids, Now(), ct);

    private async Task<string?> FindQuestionAsync(ChatMessage answer, CancellationToken ct)
    {
        var history = await chats.GetMessagesAsync(answer.SessionId, 200, ct);
        return history.Where(m => m.Role == ChatRole.User && m.CreatedAt <= answer.CreatedAt)
            .OrderByDescending(m => m.CreatedAt).FirstOrDefault()?.Content;
    }

    private sealed record Validated(string? Title, string Resolution, string? RootCause, string? Symptom, List<string> ErrorCodes, List<string> Services);

    private Validated Validate(KnowledgeCaseInput input, bool requireTitle)
    {
        var errors = new Dictionary<string, string[]>();
        var title = Clean(input.Title);
        var resolution = Clean(input.Resolution);
        if (requireTitle && title is null) errors["Title"] = ["Title is required."];
        if (title?.Length > MaxTitle) errors["Title"] = [$"Title must be at most {MaxTitle} characters."];
        if (resolution is null) errors["Resolution"] = ["Resolution is required."];
        if (resolution?.Length > MaxText) errors["Resolution"] = [$"Resolution must be at most {MaxText} characters."];
        if (Clean(input.RootCause)?.Length > MaxText) errors["RootCause"] = [$"RootCause must be at most {MaxText} characters."];
        if (Clean(input.Symptom)?.Length > MaxText) errors["Symptom"] = [$"Symptom must be at most {MaxText} characters."];

        var codes = Identifiers(input.ErrorCodes, "ErrorCodes", errors).Select(c => c.ToUpperInvariant()).Distinct().ToList();
        var services = Identifiers(input.Services, "Services", errors);
        if (errors.Count > 0) throw new RequestValidationException(errors);

        return new Validated(
            title is null ? null : masker.MaskRequired(title), masker.MaskRequired(resolution!),
            masker.Mask(Clean(input.RootCause)), masker.Mask(Clean(input.Symptom)), codes, services);
    }

    private static List<string> Identifiers(IReadOnlyList<string>? values, string field, Dictionary<string, string[]> errors)
    {
        var list = (values ?? []).Select(Clean).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (list.Count > MaxCodes) errors[field] = [$"At most {MaxCodes} values are allowed."];
        else if (list.Any(v => !System.Text.RegularExpressions.Regex.IsMatch(v, @"^[A-Za-z0-9_.\-]{1,64}$")))
            errors[field] = ["Values may only contain letters, digits, '_', '.' and '-' (max 64 characters)."];
        return list;
    }

    private static void Length(string field, string? value, int max)
    {
        if (value?.Length > max) throw Invalid(field, $"{field} must be at most {max} characters.");
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";

    private static RequestValidationException Invalid(string field, string message)
        => new(new Dictionary<string, string[]> { [field] = [message] });

    private DateTime Now() => time.GetUtcNow().UtcDateTime;
}
