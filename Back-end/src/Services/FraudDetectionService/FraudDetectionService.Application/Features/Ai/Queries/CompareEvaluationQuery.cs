using System.Text.Json;
using FraudDetectionService.Domain;
using FraudDetectionService.Domain.Repositories;
namespace FraudDetectionService.Application.Features.Ai.Queries;

public record CompareEvaluationQuery(Guid Id);
public sealed class CompareEvaluationQueryHandler(IFraudRepository repository, IAiClient ai)
{
    public async Task<object?> Handle(CompareEvaluationQuery query, CancellationToken ct)
    {
        var tx = await repository.GetAsync(query.Id, ct);
        if (tx == null)
            return null;
        var f = JsonSerializer.Deserialize<FeatureVector>(tx.Evaluation.FeaturesJson, DetectionService.Json)!;
        var prediction = await ai.PredictAsync(tx.Id, f, ct);
        if (tx.Evaluation.AnalysisStrategy == "RiskSignals")
        {
            // Use immutable evidence and policy from the original assessment, never today's history.
            var savedPolicy = JsonSerializer.Deserialize<RulePolicy>(tx.Evaluation.PolicyJson, DetectionService.Json)!;
            var savedFindings = JsonSerializer.Deserialize<List<RiskFinding>>(tx.Evaluation.RiskFindingsJson, DetectionService.Json)!;
            var rules = savedFindings.Where(x => x.Source != "model").ToList();
            var hybridFindings = RiskSignalAnalyzer.WithModel(rules, prediction, "Hybrid", savedPolicy);
            return new { analysisStrategy = "RiskSignals", ruleScore = (double?)null,
                ruleDecision = RiskSignalAnalyzer.Decision(rules), mlScore = prediction?.Score,
                modelVersion = prediction?.ModelVersion, hybridScore = (double?)null,
                hybridDecision = RiskSignalAnalyzer.Decision(hybridFindings), riskFindings = hybridFindings,
                aiStatus = prediction == null ? "Unavailable" : "Available", policyVersion = savedPolicy.Version,
                note = "Dấu hiệu và chính sách gốc đã lưu; chạy lại AI trên đặc trưng gốc. Hybrid chuyển REVIEW nếu luật hoặc AI yêu cầu; không cộng điểm, không sửa kết quả đã lưu." };
        }
        var p = await repository.PolicyAsync(ct);
        var matches = RuleEngine.Evaluate(f, p);
        var rule = matches.Count == 0 ? 0 : matches.Max(m => m.Score);
        var hybrid = prediction == null ? (double?)null : (1 - p.MlWeight) * rule + p.MlWeight * prediction.Score;
        var critical = matches.Any(m => m.Critical);
        if (critical && hybrid.HasValue)
            hybrid = Math.Max(hybrid.Value, .95);
        return new
        {
            ruleScore = rule,
            ruleDecision = RiskDecision.From(rule, p, critical),
            mlScore = prediction?.Score,
            modelVersion = prediction?.ModelVersion,
            hybridScore = hybrid,
            hybridDecision = hybrid == null ? null : RiskDecision.From(hybrid.Value, p, critical || rule >= p.BlockThreshold),
            aiStatus = prediction == null ? "Unavailable" : "Available",
            policyVersion = p.Version,
            note = "So sánh trên cùng đặc trưng đã lưu, dùng cấu hình hiện tại; không thêm giao dịch."
        };
    }
}
