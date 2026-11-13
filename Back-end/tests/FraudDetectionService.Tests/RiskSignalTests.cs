using System.Text.Json;
using FraudDetectionService.Application;
using FraudDetectionService.Application.Features.Ai.Queries;
using FraudDetectionService.Application.Features.Rules.Commands;
using FraudDetectionService.Domain;
using FraudDetectionService.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FraudDetectionService.Tests;

public class RiskSignalTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-01-02T12:00:00Z");
    private static Transaction Tx(int seconds = 0, string user = "user", decimal amount = 100000, string device = "device", string ip = "ip", string country = "VN") =>
        new() { Id = Guid.NewGuid(), Timestamp = Now.AddSeconds(seconds), UserId = user, Amount = amount, DeviceHash = device, IpHash = ip, Country = country };
    private static RulePolicy Policy() => new() { AnalysisStrategy = "RiskSignals", AmountEnabled = false };
    private static List<RiskFinding> Analyze(Transaction current, List<Transaction> history, RulePolicy? p = null) =>
        RiskSignalAnalyzer.Analyze(current, history, FeatureBuilder.Build(current, history), p ?? Policy());
    [Fact]
    public void ColdStartIsInformationNotFraud()
    {
        var findings = Analyze(Tx(), []);
        Assert.Equal("INFORMATION", Assert.Single(findings).Action);
        Assert.Equal("ALLOW", RiskSignalAnalyzer.Decision(findings));
    }
    [Fact]
    public void FewRapidTransfersFlagEvenWhenBelowOldHourlyLimit()
    {
        var findings = Analyze(Tx(), [Tx(-40), Tx(-20)]);
        Assert.Contains(findings, f => f.Code == "RAPID_SEQUENCE" && f.Evidence.Contains("3 giao dịch"));
        Assert.Equal("REVIEW", RiskSignalAnalyzer.Decision(findings));
        Assert.Contains(findings, f => f.Code == "LIMITED_HISTORY");
    }
    [Fact]
    public void NormalDenseBusinessBaselineAvoidsFrequencyFlag()
    {
        var history = Enumerable.Range(0, 5).SelectMany(batch => Enumerable.Range(0, 4).Select(i => Tx(-3600 - batch * 120 - i * 5))).ToList();
        history.AddRange([Tx(-40), Tx(-20)]);
        Assert.DoesNotContain(Analyze(Tx(), history), f => f.Code == "RAPID_SEQUENCE");
    }
    [Fact]
    public void BurstAbovePersonalBaselineFlagsWithEvidence()
    {
        var history = Enumerable.Range(1, 20).Select(i => Tx(-600 * i)).ToList();
        history.AddRange([Tx(-40), Tx(-20)]);
        Assert.Contains(Analyze(Tx(), history), f => f.Code == "RAPID_SEQUENCE" && f.Evidence.Contains("Cao hơn mức cao nhất 1"));
    }
    [Fact]
    public void WindowExcludesExpiredCurrentFutureAndOtherUser()
    {
        var findings = Analyze(Tx(), [Tx(-61), Tx(0), Tx(10), Tx(-20, "other"), Tx(-90000)]);
        Assert.DoesNotContain(findings, f => f.Group == "frequency");
    }
    [Fact]
    public void ValueAndAccessFindingsCanCoexistWithoutNamingFraudSubtype()
    {
        var history = Enumerable.Range(1, 20).Select(i => Tx(-600 * i, amount: 100000 + i * 100)).ToList();
        var findings = Analyze(Tx(amount: 2000000, device: "new", country: "US"), history);
        Assert.Contains(findings, f => f.Group == "value");
        Assert.Contains(findings, f => f.Group == "context");
        Assert.DoesNotContain(findings, f => f.Action == "BLOCK");
    }
    [Fact]
    public void SharedIpAloneIsObservationWhileSharedDeviceNeedsReview()
    {
        var history = Enumerable.Range(1, 4).Select(i => Tx(-100 * i, "other" + i, device: "different" + i)).ToList();
        var findings = Analyze(Tx(), history);
        Assert.Contains(findings, f => f.Code == "SHARED_IP" && f.Action == "OBSERVE");
        Assert.Equal("ALLOW", RiskSignalAnalyzer.Decision(findings));
        Assert.Contains(Analyze(Tx(), [Tx(-20, "a"), Tx(-40, "b")]), f => f.Code == "SHARED_DEVICE" && f.Action == "REVIEW");
    }
    [Fact]
    public void BusinessLimitRequiresReviewNeverAutomaticBlock()
    {
        var p = Policy(); p.AmountEnabled = true;
        Assert.Equal("REVIEW", RiskSignalAnalyzer.Decision(Analyze(Tx(amount: 300000000), [], p)));
    }
    [Fact]
    public void DisabledGroupsDoNotFlag()
    {
        var p = Policy(); p.VelocityEnabled = false; p.DeviceEnabled = false; p.UnusualEnabled = false;
        Assert.DoesNotContain(Analyze(Tx(), [Tx(-20), Tx(-40)], p), f => f.Action == "REVIEW");
    }
    [Fact]
    public void HybridDoesNotDiluteRulesAndUnavailableAiRequestsReview()
    {
        var p = Policy();
        var rules = Analyze(Tx(), [Tx(-20), Tx(-40)], p);
        Assert.Equal("REVIEW", RiskSignalAnalyzer.Decision(RiskSignalAnalyzer.WithModel(rules, new(0, "test", "1"), "Hybrid", p)));
        Assert.Equal("REVIEW", RiskSignalAnalyzer.Decision(RiskSignalAnalyzer.WithModel([], null, "Hybrid", p)));
        Assert.Contains(RiskSignalAnalyzer.WithModel([], new(.9, "test", "1"), "Hybrid", p), f => f.Group == "model");
        Assert.Empty(RiskSignalAnalyzer.WithModel([], new(.9, "test", "1"), "RulesOnly", p));
    }
    [Fact]
    public void InvalidSignalParametersAreRejected()
    {
        var p = Policy(); p.BurstWindowSeconds = 0; p.MinimumHistory = 0; p.AmountDeviationThreshold = double.NaN;
        var result = new UpdateRulePolicyCommandValidator().Validate(new UpdateRulePolicyCommand(p, "admin"));
        Assert.False(result.IsValid);
        Assert.Equal(3, result.Errors.Select(e => e.PropertyName).Distinct().Count());
    }
    [Fact]
    public async Task FindingsPersistAndCompareUsesOriginalPolicyAfterChanges()
    {
        await using var db = new FraudDbContext(new DbContextOptionsBuilder<FraudDbContext>().UseSqlite("Data Source=:memory:").Options);
        await db.Database.OpenConnectionAsync(); await db.Database.EnsureCreatedAsync();
        var p = Policy(); p.AmountEnabled = true; db.Policies.Add(p); await db.SaveChangesAsync();
        var repo = new FraudRepository(db); var ai = new TestAi(); var svc = new DetectionService(repo, ai, new PiiHasher("test"), new());
        var request = new TransactionRequest(null, "user", 300000000, "VND", "shop", "grocery", "VN", "device", "192.0.2.1", Now, "Hybrid");
        var result = await svc.AnalyzeAsync(request, default);
        Assert.Equal("REVIEW", result.Decision); Assert.Equal("RiskSignals", result.AnalysisStrategy);
        Assert.Null(result.RuleScore); Assert.Null(result.FinalScore);
        Assert.Contains(result.RiskFindings, f => f.Code == "BUSINESS_LIMIT");
        var snapshot = (await repo.GetAsync(result.Id, default))!.Evaluation.RiskFindingsJson;
        await repo.UpdatePolicyAsync(new() { Version = 1 }, "admin", default);
        var stored = await svc.ViewAsync((await repo.GetAsync(result.Id, default))!, default);
        Assert.Equal(JsonSerializer.Serialize(result.RiskFindings), JsonSerializer.Serialize(stored.RiskFindings)); Assert.Equal(1, stored.RuleVersion);
        var page = await repo.ListSummariesAsync(null, null, 1, 20, default);
        Assert.Contains(page[0].RiskFindings!, f => f.Code == "BUSINESS_LIMIT");
        var compare = JsonSerializer.SerializeToElement(await new CompareEvaluationQueryHandler(repo, ai).Handle(new(result.Id), default), DetectionService.Json);
        Assert.Equal("RiskSignals", compare.GetProperty("analysisStrategy").GetString());
        Assert.Equal("REVIEW", compare.GetProperty("hybridDecision").GetString());
        Assert.Equal(1, compare.GetProperty("policyVersion").GetInt32());
        Assert.Equal(snapshot, (await repo.GetAsync(result.Id, default))!.Evaluation.RiskFindingsJson);
        Assert.Equal(1, await db.Alerts.CountAsync());
    }
    [Fact]
    public void SourcesAndInputTrustAreSavedWithEveryNewFinding()
    {
        var findings = Analyze(Tx(), [Tx(-20), Tx(-40)]);
        Assert.All(findings, f =>
        {
            Assert.Equal("Experimental", f.ValidationStatus);
            Assert.Equal("UserProvidedDemo", f.InputTrust);
            Assert.NotEmpty(f.References!);
            Assert.All(f.References!, r => Assert.StartsWith("https://", r.Url));
        });
        Assert.Contains(findings.Single(f => f.Code == "RAPID_SEQUENCE").References!, r => r.Id == "FEDNOW_VELOCITY");
    }
    [Fact]
    public void OldFindingsWithoutMetadataRemainReadable()
    {
        var legacy = JsonSerializer.Deserialize<RiskFinding>("""{"code":"x","group":"value","title":"x","evidence":"x","source":"rule","action":"REVIEW"}""", DetectionService.Json)!;
        Assert.Equal("x", legacy.Code); Assert.Null(legacy.References);
    }
    [Fact]
    public void NewPoliciesCannotReactivateLegacyScoring()
    {
        var result = new UpdateRulePolicyCommandValidator().Validate(new UpdateRulePolicyCommand(new RulePolicy(), "admin"));
        Assert.Contains(result.Errors, e => e.PropertyName == "Policy.AnalysisStrategy");
    }
    [Fact]
    public void TimeOfDayAndMerchantAloneNeverTriggerNewRules()
    {
        var tx = Tx(amount: 60000000); tx.Timestamp = DateTimeOffset.Parse("2026-01-02T02:00:00Z"); tx.Category = "gambling";
        Assert.Equal("ALLOW", RiskSignalAnalyzer.Decision(Analyze(tx, [])));
    }
    private sealed class TestAi : IAiClient
    {
        public Task<AiPrediction?> PredictAsync(Guid id, FeatureVector f, CancellationToken ct) => Task.FromResult<AiPrediction?>(new(0, "test", "1"));
        public Task<string> GetStatusAsync(CancellationToken ct) => Task.FromResult("Ready");
        public Task<string?> GetMetricsAsync(CancellationToken ct) => Task.FromResult<string?>(null);
    }
}
