namespace FraudDetectionService.Domain;

public sealed class Evaluation
{
    public Guid TransactionId
    {
        get; set;
    }
    public string Mode { get; set; } = "RulesOnly";
    public double RuleScore
    {
        get; set;
    }
    public double? MlScore
    {
        get; set;
    }
    public double FinalScore
    {
        get; set;
    }
    public string Decision { get; set; } = "ALLOW";
    public string AiStatus { get; set; } = "Disabled";
    public string? ModelVersion
    {
        get; set;
    }
    public int RuleVersion
    {
        get; set;
    }
    public string MatchesJson { get; set; } = "[]";
    public string AnalysisStrategy { get; set; } = "LegacyScore";
    public string RiskFindingsJson { get; set; } = "[]";
    public string FeaturesJson { get; set; } = "{}";
    public string PolicyJson { get; set; } = "{}";
    public double LatencyMs
    {
        get; set;
    }
    public DateTimeOffset EvaluatedAt { get; set; } = DateTimeOffset.UtcNow;
}
