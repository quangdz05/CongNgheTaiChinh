namespace FraudDetectionService.Application;

public interface IAiClient
{
    Task<AiPrediction?> PredictAsync(Guid transactionId, FeatureVector features, CancellationToken ct);
    Task<string> GetStatusAsync(CancellationToken ct);
    Task<string?> GetMetricsAsync(CancellationToken ct);
}
public interface IPiiHasher
{
    string Hash(string value);
}
