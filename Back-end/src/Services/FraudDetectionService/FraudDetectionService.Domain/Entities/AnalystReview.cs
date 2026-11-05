namespace FraudDetectionService.Domain;

public sealed class AnalystReview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TransactionId
    {
        get; set;
    }
    public bool IsFraud
    {
        get; set;
    }
    public string Notes { get; set; } = "";
    public string Reviewer { get; set; } = "";
    public DateTimeOffset ReviewedAt { get; set; } = DateTimeOffset.UtcNow;
}
