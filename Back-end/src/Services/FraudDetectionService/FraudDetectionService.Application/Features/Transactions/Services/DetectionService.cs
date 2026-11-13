using FraudDetectionService.Domain.Repositories;
using System.Diagnostics;
using System.Text.Json;
using FraudDetectionService.Domain;
namespace FraudDetectionService.Application;

public sealed class DetectionService(IFraudRepository store, IAiClient ai, IPiiHasher hasher, EvaluationGate gate)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<EvaluationView> AnalyzeAsync(TransactionRequest req, CancellationToken ct)
    {
        if (!System.Net.IPAddress.TryParse(req.IpAddress, out var parsedIp))
            throw new ArgumentException("Địa chỉ IP không hợp lệ.");
        var canonicalIp = parsedIp.ToString();
        var timestamp = req.Timestamp ?? DateTimeOffset.UtcNow;
        if (timestamp > DateTimeOffset.UtcNow.AddMinutes(1))
            throw new ArgumentException("Thời gian giao dịch không được ở tương lai.");
        var id = req.Id ?? Guid.NewGuid();
        var userId = req.UserId.Trim();
        var deviceHash = hasher.Hash(req.DeviceId);
        var ipHash = hasher.Hash(canonicalIp);
        var sw = Stopwatch.StartNew();
        using var lease = await gate.EnterAsync([$"id:{id}", $"user:{userId}", $"device:{deviceHash}", $"ip:{ipHash}"], ct);
        {
            var existing = await store.GetAsync(id, ct);
            if (existing != null)
            {
                if (existing.UserId != req.UserId.Trim() || existing.Amount != req.Amount || existing.Currency != req.Currency || existing.Merchant != req.Merchant || existing.Category != req.Category || existing.Country != req.Country || existing.DeviceHash != hasher.Hash(req.DeviceId) || existing.IpHash != hasher.Hash(canonicalIp) || existing.Evaluation.Mode != req.Mode || (req.Timestamp.HasValue && existing.Timestamp != timestamp))
                    throw new ConflictException("Mã giao dịch đã tồn tại với nội dung khác.");
                return await ViewAsync(existing, ct);
            }
            var p = await store.PolicyAsync(ct);
            var tx = new Transaction { Id = id, UserId = req.UserId.Trim(), Amount = req.Amount, Currency = req.Currency, Merchant = req.Merchant, Category = req.Category, Country = req.Country, DeviceHash = hasher.Hash(req.DeviceId), IpHash = hasher.Hash(canonicalIp), Timestamp = timestamp };
            var history = await store.HistoryAsync(timestamp, userId, deviceHash, ipHash, ct);
            var features = FeatureBuilder.Build(tx, history);
            var matches = p.AnalysisStrategy == "RiskSignals" ? new List<RuleMatch>() : RuleEngine.Evaluate(features, p);
            var ruleScore = matches.Count == 0 ? 0 : matches.Max(r => r.Score);
            AiPrediction? prediction = null;
            if (req.Mode == "Hybrid")
                prediction = await ai.PredictAsync(id, features, ct);
            double score;
            string decision;
            List<RiskFinding> findings = [];
            if (p.AnalysisStrategy == "RiskSignals")
            {
                findings = RiskSignalAnalyzer.WithModel(RiskSignalAnalyzer.Analyze(tx, history, features, p), prediction, req.Mode, p);
                decision = RiskSignalAnalyzer.Decision(findings);
                // Compatibility numeric fields have no meaning for a signal assessment. Never blend scores.
                ruleScore = 0;
                score = 0;
                matches = [];
            }
            else
            {
                var critical = matches.Any(m => m.Critical);
                score = prediction == null ? ruleScore : (1 - p.MlWeight) * ruleScore + p.MlWeight * prediction.Score;
                if (critical) score = Math.Max(score, .95);
                // Historical scoring is retained only for the explicitly selected legacy policy.
                decision = RiskDecision.From(score, p, critical || ruleScore >= p.BlockThreshold);
                if (req.Mode == "Hybrid" && prediction == null && decision == "ALLOW") decision = "REVIEW";
            }
            tx.Evaluation = new Evaluation { TransactionId = id, Mode = req.Mode, RuleScore = ruleScore, MlScore = prediction?.Score, FinalScore = Math.Round(score, 4), Decision = decision, AiStatus = req.Mode == "RulesOnly" ? "Disabled" : prediction == null ? "UnavailableReviewFallback" : "Available", ModelVersion = prediction?.ModelVersion, RuleVersion = p.Version, MatchesJson = JsonSerializer.Serialize(matches, Json), FeaturesJson = JsonSerializer.Serialize(features, Json), PolicyJson = JsonSerializer.Serialize(p, Json), LatencyMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2) };
            tx.Evaluation.AnalysisStrategy = p.AnalysisStrategy;
            tx.Evaluation.RiskFindingsJson = JsonSerializer.Serialize(findings, Json);
            await store.AddAsync(tx, ct);
            return await ViewAsync(tx, ct);
        }
    }
    public async Task<EvaluationView> ViewAsync(Transaction tx, CancellationToken ct) => new(tx.Id, tx.UserId, tx.Amount, tx.Currency, tx.Merchant, tx.Category, tx.Country, tx.DeviceHash, tx.IpHash, tx.Timestamp, tx.Evaluation.Mode, tx.Evaluation.AnalysisStrategy == "RiskSignals" ? null : tx.Evaluation.RuleScore, tx.Evaluation.MlScore, tx.Evaluation.AnalysisStrategy == "RiskSignals" ? null : tx.Evaluation.FinalScore, tx.Evaluation.Decision, tx.Evaluation.AiStatus, tx.Evaluation.ModelVersion, tx.Evaluation.RuleVersion, tx.Evaluation.LatencyMs, JsonSerializer.Deserialize<List<RuleMatch>>(tx.Evaluation.MatchesJson, Json)!, JsonSerializer.Deserialize<FeatureVector>(tx.Evaluation.FeaturesJson, Json)!, await store.ReviewsAsync(tx.Id, ct), tx.Evaluation.AnalysisStrategy, JsonSerializer.Deserialize<List<RiskFinding>>(tx.Evaluation.RiskFindingsJson, Json)!);
}
