using System.Text.Json;

namespace FraudDetectionService.Domain;

public sealed class Alert
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TransactionId { get; set; }
    public string Status { get; set; } = "New";
    public int? OwnerUserId { get; set; }
    public string Priority { get; set; } = "P2";
    public string PriorityReason { get; set; } = "Initial evaluation";
    public string SourceType { get; set; } = "Rules";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset OriginalEvaluatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; } = 1;
    public string Resolution { get; set; } = "Unknown";
    public string? ClosureReasonCode { get; set; }
    public string? ClosureNotes { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public bool? ClosedWithinSla => ClosedAt.HasValue ? ClosedAt.Value <= DueAt : null;
    public int? ClosedByUserId { get; set; }
    public string EvidenceJson { get; set; } = "[]";
    public string? WaitingType { get; set; }
    public string? WaitingReason { get; set; }
    public DateTimeOffset? WaitingUntil { get; set; }
    public int SlaPolicyVersion { get; set; } = 1;
    public int BudgetMinutes { get; set; } = 60;
    public DateTimeOffset SlaStartedAt { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public int HandlingEpisode { get; set; } = 1;
    public DateTimeOffset? EscalatedAt { get; set; }
    public string? EscalationReason { get; set; }
    public DateTimeOffset EscalationEligibleAt { get; set; }
}

public sealed class AlertEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AlertId { get; set; }
    public string Type { get; set; } = "";
    public int? ActorUserId { get; set; }
    public DateTimeOffset At { get; set; }
    public long BeforeVersion { get; set; }
    public long Version { get; set; }
    public int HandlingEpisode { get; set; }
    public string Reason { get; set; } = "";
    public Guid? RequestId { get; set; }
    public string BeforeJson { get; set; } = "null";
    public string AfterJson { get; set; } = "{}";
}

public sealed class AlertCommandReceipt
{
    public Guid AlertId { get; set; }
    public Guid RequestId { get; set; }
    public int ActorUserId { get; set; }
    public string Command { get; set; } = "";
    public string PayloadHash { get; set; } = "";
    public string ResultJson { get; set; } = "{}";
    public long Version { get; set; }
    public DateTimeOffset At { get; set; }
}

public static class AlertPolicy
{
    private static readonly JsonSerializerOptions FindingsJson = new(JsonSerializerDefaults.Web);
    public static readonly string[] States = ["New", "Assigned", "InProgress", "Waiting", "Escalated", "Closed"];
    public static readonly string[] Priorities = ["P1", "P2", "P3"];
    public static int Budget(string priority) => priority switch { "P1" => 15, "P2" => 60, "P3" => 240, _ => throw new ArgumentException("Mức ưu tiên không hợp lệ.") };
    public static Alert Create(Transaction tx, DateTimeOffset now, bool imported = false)
    {
        var e = tx.Evaluation;
        var fallbackRequiresReview = e.Decision != "BLOCK" && e.AiStatus == "UnavailableReviewFallback"
            && e.AnalysisStrategy == "RiskSignals" && HasNonModelReviewFinding(e.RiskFindingsJson);
        var priority = e.Decision == "BLOCK" ? "P1" : e.AiStatus == "UnavailableReviewFallback" && !fallbackRequiresReview ? "P3" : "P2";
        return new Alert
        {
            TransactionId = tx.Id, CreatedAt = now, UpdatedAt = now, OriginalEvaluatedAt = e.EvaluatedAt,
            SourceType = imported ? "ImportedLegacy" : e.AiStatus == "UnavailableReviewFallback" ? "AiUnavailableFallback" : e.Mode == "Hybrid" ? "Hybrid" : "Rules",
            Priority = priority, PriorityReason = "Evaluation: " + e.Decision
                + (fallbackRequiresReview ? "; AI unavailable but rule signals require review" : ""),
            BudgetMinutes = Budget(priority), SlaStartedAt = now, DueAt = now.AddMinutes(Budget(priority)), EscalationEligibleAt = now
        };
    }

    private static bool HasNonModelReviewFinding(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            var findings = JsonSerializer.Deserialize<List<RiskFinding>>(json, FindingsJson);
            return findings?.Any(f => f != null && f.Source != "model" && f.Action == "REVIEW") == true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
