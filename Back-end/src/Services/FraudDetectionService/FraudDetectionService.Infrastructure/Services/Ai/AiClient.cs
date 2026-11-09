using FraudDetectionService.Application;
using System.Net.Http.Json;
using System.Text.Json;
namespace FraudDetectionService.Infrastructure;

public sealed class AiClient(HttpClient http) : IAiClient
{
    public async Task<AiPrediction?> PredictAsync(Guid id, FeatureVector features, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("v1/predict", new
            {
                transactionId = id,
                featureSchemaVersion = FeatureVector.SchemaVersion,
                features
            }, DetectionService.Json, ct);
            if (!response.IsSuccessStatusCode)
                return null;
            var prediction = await response.Content.ReadFromJsonAsync<AiPrediction>(DetectionService.Json, ct);
            return prediction is { Score: >= 0 and <= 1 } && double.IsFinite(prediction.Score) && prediction.FeatureSchemaVersion == FeatureVector.SchemaVersion && !string.IsNullOrWhiteSpace(prediction.ModelVersion) ? prediction : null;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException || e is TaskCanceledException && !ct.IsCancellationRequested) { return null; }
    }
    public async Task<string> GetStatusAsync(CancellationToken ct)
    {
        try
        {
            using var r = await http.GetAsync("health", ct);
            return r.IsSuccessStatusCode ? "Ready" : "ModelNotReady";
        }
        catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException && !ct.IsCancellationRequested) { return "Unavailable"; }
    }
    public async Task<string?> GetMetricsAsync(CancellationToken ct)
    {
        try
        {
            using var r = await http.GetAsync("v1/metrics", ct);
            return r.IsSuccessStatusCode ? await r.Content.ReadAsStringAsync(ct) : null;
        }
        catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException && !ct.IsCancellationRequested) { return null; }
    }
}
