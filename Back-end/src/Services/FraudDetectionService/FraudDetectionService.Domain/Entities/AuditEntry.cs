namespace FraudDetectionService.Domain;

public sealed class AuditEntry
{
    public long Id
    {
        get; set;
    }
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string Details { get; set; } = "";
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}
