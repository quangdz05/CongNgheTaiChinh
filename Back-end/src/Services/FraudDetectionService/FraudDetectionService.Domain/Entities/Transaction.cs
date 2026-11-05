namespace FraudDetectionService.Domain;

public sealed class Transaction
{
    public Guid Id
    {
        get; set;
    }
    public string UserId { get; set; } = "";
    public decimal Amount
    {
        get; set;
    }
    public string Currency { get; set; } = "VND";
    public string Merchant { get; set; } = "";
    public string Category { get; set; } = "grocery";
    public string Country { get; set; } = "VN";
    public string DeviceHash { get; set; } = "";
    public string IpHash { get; set; } = "";
    public DateTimeOffset Timestamp
    {
        get; set;
    }
    public Evaluation Evaluation { get; set; } = null!;
}
