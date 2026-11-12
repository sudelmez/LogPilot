namespace LogPilot.Application.Ai;

/// Yapay zekânın veriye ulaşabildiği TEK kapı. Serbest MongoDB sorgusu yok; her yöntem doğrulanmış, sınırlı
/// ve salt-okunur. Argüman hataları ToolArgumentException ile döner (LLM'e "araç hatası" olarak iletilir).
public interface ILogAnalysisTools
{
    Task<LogToolResult> SearchLogsAsync(SearchLogsArgs args, ToolContext context, CancellationToken ct);
    Task<LogToolResult> GetUserRecentErrorsAsync(UserRecentErrorsArgs args, ToolContext context, CancellationToken ct);
    Task<ServiceHealthToolResult> GetServiceHealthAsync(ServiceHealthArgs args, ToolContext context, CancellationToken ct);
    Task<ErrorStatisticsToolResult> GetErrorStatisticsAsync(ErrorStatisticsArgs args, ToolContext context, CancellationToken ct);
    Task<LogToolResult> FindRelatedLogsAsync(RelatedLogsArgs args, ToolContext context, CancellationToken ct);
    Task<IncidentListToolResult> GetActiveIncidentsAsync(ActiveIncidentsArgs args, ToolContext context, CancellationToken ct);
    Task<IncidentDetailsToolResult> GetIncidentDetailsAsync(IncidentDetailsArgs args, ToolContext context, CancellationToken ct);
    Task<KnowledgeToolResult> SearchKnowledgeAsync(SearchKnowledgeArgs args, CancellationToken ct);

    // ---- Sigorta teklifi (quote_logs) + müşteri dizini (users) ----
    Task<CustomerProfileToolResult> GetCustomerProfileAsync(CustomerProfileArgs args, ToolContext context, CancellationToken ct);
    Task<LogToolResult> SearchQuoteLogsAsync(SearchQuoteLogsArgs args, ToolContext context, CancellationToken ct);
    Task<QuoteFunnelToolResult> GetQuoteFunnelAsync(QuoteFunnelArgs args, ToolContext context, CancellationToken ct);
    Task<CustomerJourneyToolResult> TraceCustomerJourneyAsync(CustomerJourneyArgs args, ToolContext context, CancellationToken ct);

    // ---- Bildirim (sms_logs): zincirleme quote_logs → users → sms_logs ----
    Task<NotificationTraceToolResult> TraceNotificationDeliveryAsync(NotificationTraceArgs args, ToolContext context, CancellationToken ct);
    Task<LogToolResult> SearchSmsLogsAsync(SearchSmsLogsArgs args, ToolContext context, CancellationToken ct);
}
