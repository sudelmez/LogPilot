using System.Text.Json;
using System.Text.Json.Serialization;

namespace LogPilot.AI.Providers;

public static class ToolNames
{
    public const string SearchLogs = "SearchLogs";
    public const string GetUserRecentErrors = "GetUserRecentErrors";
    public const string GetServiceHealth = "GetServiceHealth";
    public const string GetErrorStatistics = "GetErrorStatistics";
    public const string FindRelatedLogs = "FindRelatedLogs";
    public const string GetActiveIncidents = "GetActiveIncidents";
    public const string GetIncidentDetails = "GetIncidentDetails";
    public const string SearchKnowledgeBase = "SearchKnowledgeBase";
    public const string GetCustomerProfile = "GetCustomerProfile";
    public const string SearchQuoteLogs = "SearchQuoteLogs";
    public const string GetQuoteFunnel = "GetQuoteFunnel";
    public const string TraceCustomerJourney = "TraceCustomerJourney";
    public const string TraceNotificationDelivery = "TraceNotificationDelivery";
    public const string SearchSmsLogs = "SearchSmsLogs";
    public const string TracePolicyPurchase = "TracePolicyPurchase";
    public const string SearchProposalSteps = "SearchProposalSteps";
    public const string SearchWinsureTransactions = "SearchWinsureTransactions";
    public const string SearchPaymentTransactions = "SearchPaymentTransactions";
    public const string GetPurchaseStats = "GetPurchaseStats";
}

public static class ToolJson
{
    /// Sonuçları serileştirmek / okumak için.
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// LLM'den gelen argümanlar için katı mod: tanımsız alan = hata ("izinli filtre alanları" kuralı).
    public static readonly JsonSerializerOptions StrictArguments = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };
}
