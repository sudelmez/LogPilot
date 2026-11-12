using LogPilot.Application.Common;
using LogPilot.Application.Logs;
using LogPilot.Application.Ai;
using LogPilot.Application.Quotes;
using LogPilot.Application.Notifications;
using LogPilot.Domain.Notifications;
using LogPilot.Domain.Quotes;
using LogPilot.Domain.Logs;

namespace LogPilot.Api.Endpoints;

public static class LogEndpoints
{
    public static IEndpointRouteBuilder MapLogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/logs").WithTags("Logs");

        group.MapGet("/", async ([AsParameters] LogSearchRequest request, LogService logs, CancellationToken ct)
                => TypedResults.Ok(await logs.SearchAsync(request, ct)))
            .Produces<PagedResult<LogEntry>>()
            .ProducesValidationProblem();

        group.MapGet("/recent-errors", async (LogService logs, CancellationToken ct, int limit = 20, int? minutes = null)
                => TypedResults.Ok(await logs.GetRecentErrorsAsync(limit, minutes, ct)))
            .Produces<IReadOnlyList<LogEntry>>()
            .ProducesValidationProblem();

        group.MapGet("/by-user/{userCode}", async (string userCode, DateTimeOffset? from, DateTimeOffset? to,
                LogService logs, CancellationToken ct, int page = 1, int pageSize = 50)
                => TypedResults.Ok(await logs.GetByUserAsync(userCode, from, to, page, pageSize, ct)))
            .Produces<PagedResult<LogEntry>>()
            .ProducesValidationProblem();

        group.MapGet("/by-correlation/{correlationId}", async (string correlationId, LogService logs, CancellationToken ct)
                => TypedResults.Ok(await logs.GetByCorrelationIdAsync(correlationId, ct)))
            .Produces<PagedResult<LogEntry>>()
            .ProducesValidationProblem();

        group.MapGet("/by-transaction/{transactionId}", async (string transactionId, LogService logs, CancellationToken ct)
                => TypedResults.Ok(await logs.GetByTransactionIdAsync(transactionId, ct)))
            .Produces<PagedResult<LogEntry>>()
            .ProducesValidationProblem();

        // Asistanın kanıtları iki tablodan gelebilir: id `logs`'ta yoksa `quote_logs`'a bakılır ve aynı şekilde döndürülür
        // (arayüz tek bir log detay çekmecesiyle ikisini de gösterebilsin).
        group.MapGet("/{id}", async (string id, LogService logs, IQuoteLogRepository quotes, ISmsLogRepository sms, CancellationToken ct) =>
                await logs.GetByIdAsync(id, ct) is { } log ? Results.Ok(log)
                : await quotes.GetByIdAsync(id, ct) is { } quote ? Results.Ok(ToLogShape(quote))
                : await sms.GetByIdAsync(id, ct) is { } message ? Results.Ok(ToLogShape(message))
                : Results.NotFound())
            .Produces<LogEntry>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        var quoteGroup = app.MapGroup("/api/quote-logs").WithTags("Quote logs");

        quoteGroup.MapGet("/by-quote/{quoteId}", async (string quoteId, IQuoteLogRepository quotes, TimeProvider time, CancellationToken ct) =>
            {
                var now = time.GetUtcNow().UtcDateTime;
                var page = await quotes.SearchAsync(new QuoteLogSearchRequest(now.AddDays(-30), now.AddMinutes(1),
                    QuoteId: quoteId.Trim().ToUpperInvariant(), Limit: 100, Ascending: true), ct);
                return TypedResults.Ok(page.Items.Select(ToLogShape).ToList());
            })
            .Produces<List<LogEntry>>();

        quoteGroup.MapGet("/funnel", async (IQuoteLogRepository quotes, TimeProvider time, CancellationToken ct, int minutes = 1440) =>
            {
                var now = time.GetUtcNow().UtcDateTime;
                return TypedResults.Ok(await quotes.GetFunnelAsync(now.AddMinutes(-Math.Clamp(minutes, 1, 7 * 24 * 60)), now.AddMinutes(1), null, null, null, ct));
            })
            .Produces<QuoteFunnel>();

        return app;
    }

    /// SMS logunu uygulama log şeklinde gösterir. Telefon numarası maskelenir.
    private static LogEntry ToLogShape(SmsLogEntry m)
    {
        var metadata = new Dictionary<string, string>(m.Metadata)
        {
            ["source"] = LogSources.SmsLogs,
            ["messageId"] = m.MessageId,
            ["phone"] = LogAnalysisTools.MaskPhone(m.Phone) ?? "",
            ["status"] = m.Status.ToString()
        };
        if (m.Provider is not null) metadata["provider"] = m.Provider;

        return new LogEntry
        {
            Id = m.Id, Timestamp = m.Timestamp, Level = m.Level, Application = "SmsGateway", Service = m.Service,
            Environment = m.Environment, Operation = m.TemplateCode, ErrorCode = m.ErrorCode, Message = m.Message,
            DurationMs = m.DurationMs, Metadata = metadata,
            Outcome = m.Status switch
            {
                SmsStatus.Delivered => OperationOutcome.Success,
                SmsStatus.Failed or SmsStatus.Undelivered => OperationOutcome.Failure,
                _ => null
            }
        };
    }

    /// Teklif logunu uygulama log şeklinde gösterir. Kimlik numarası burada da maskelenir.
    private static LogEntry ToLogShape(QuoteLogEntry q)
    {
        var metadata = new Dictionary<string, string>(q.Metadata)
        {
            ["source"] = LogSources.QuoteLogs,
            ["quoteId"] = q.QuoteId,
            ["channel"] = q.Channel.ToString(),
            ["product"] = q.Product
        };
        if (q.CustomerNo is not null) metadata["customerNo"] = q.CustomerNo;
        if (LogAnalysisTools.MaskIdentity(q.IdentityNo) is { } identity) metadata["identityNo"] = identity;
        if (q.InsurerCode is not null) metadata["insurerCode"] = q.InsurerCode;
        if (q.PremiumAmount is { } premium) metadata["premiumTry"] = premium.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

        return new LogEntry
        {
            Id = q.Id, Timestamp = q.Timestamp, Level = q.Level, Application = "QuoteEngine", Service = q.Service,
            Environment = q.Environment, UserCode = q.UserCode, CorrelationId = q.CorrelationId, Operation = q.Step,
            Outcome = q.Outcome, ErrorCode = q.ErrorCode, Message = q.Message, ExceptionType = q.ExceptionType,
            DurationMs = q.DurationMs, Metadata = metadata
        };
    }
}
