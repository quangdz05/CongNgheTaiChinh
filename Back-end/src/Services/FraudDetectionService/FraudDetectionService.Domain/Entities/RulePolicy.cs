namespace FraudDetectionService.Domain;

public sealed class RulePolicy
{
    public int Id { get; set; } = 1;
    public int Version { get; set; } = 1;
    public decimal HighAmount { get; set; } = 125_000_000;
    public decimal CriticalAmount { get; set; } = 250_000_000;
    public int VelocityCount { get; set; } = 10;
    public int SharedDeviceUsers { get; set; } = 3;
    public double ReviewThreshold { get; set; } = .4;
    public double BlockThreshold { get; set; } = .8;
    public double MlWeight { get; set; } = .5;
    public bool AmountEnabled { get; set; } = true;
    public bool VelocityEnabled { get; set; } = true;
    public bool DeviceEnabled { get; set; } = true;
    public bool UnusualEnabled { get; set; } = true;
    public string UpdatedBy { get; set; } = "system";
}
