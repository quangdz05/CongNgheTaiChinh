namespace FraudDetectionService.Domain;

public static class RiskDecision
{
    public static string From(double score, RulePolicy policy, bool critical = false) =>
        critical || score >= policy.BlockThreshold ? "BLOCK" : score >= policy.ReviewThreshold ? "REVIEW" : "ALLOW";
}
