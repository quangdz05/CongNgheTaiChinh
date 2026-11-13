namespace FraudDetectionService.Domain;

public sealed record TransactionSummary(Guid Id, string UserId, decimal Amount, string Currency,
    string Merchant, DateTimeOffset Timestamp, double? FinalScore, string Decision,
    string AnalysisStrategy = "LegacyScore", IReadOnlyList<RiskFinding>? RiskFindings = null);
