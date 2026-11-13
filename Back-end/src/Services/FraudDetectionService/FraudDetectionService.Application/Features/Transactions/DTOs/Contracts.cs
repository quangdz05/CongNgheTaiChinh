using System.ComponentModel.DataAnnotations;
using FraudDetectionService.Domain;
namespace FraudDetectionService.Application;

public sealed record TransactionRequest(
    Guid? Id,
    [Required, StringLength(80, MinimumLength = 1)] string UserId,
    [Range(typeof(decimal), "1", "100000000000")] decimal Amount,
    [Required, RegularExpression("^VND$")] string Currency,
    [Required, StringLength(80)] string Merchant,
    [Required, RegularExpression("^(grocery|electronics|travel|jewelry|crypto|gambling|other)$")] string Category,
    [Required, RegularExpression("^[A-Z]{2}$")] string Country,
    [Required, StringLength(100)] string DeviceId,
    [Required, StringLength(45)] string IpAddress,
    DateTimeOffset? Timestamp,
    [RegularExpression("^(RulesOnly|Hybrid)$")] string Mode = "RulesOnly");
public sealed record FeatureVector(double Amount, double TransactionCount1h, double TransactionCount24h,
    double AmountZScore, double IsNewDevice, double IsNewLocation, double SharedDeviceUsers,
    double SharedIpUsers, double HourOfDay, double MerchantRisk)
{
    public const string SchemaVersion = "1";
}
public sealed record EvaluationView(Guid Id, string UserId, decimal Amount, string Currency,
    string Merchant, string Category, string Country, string DeviceHash, string IpHash, DateTimeOffset Timestamp,
    string Mode, double? RuleScore, double? MlScore, double? FinalScore, string Decision, string AiStatus,
    string? ModelVersion, int RuleVersion, double LatencyMs, IReadOnlyList<RuleMatch> Matches,
    FeatureVector Features, IReadOnlyList<AnalystReview> Reviews, string AnalysisStrategy,
    IReadOnlyList<RiskFinding> RiskFindings);
public record AiPrediction(double Score, string ModelVersion, string FeatureSchemaVersion);
