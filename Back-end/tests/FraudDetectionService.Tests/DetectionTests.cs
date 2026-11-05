using FraudDetectionService.Application;
using FraudDetectionService.Domain;
using FraudDetectionService.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace FraudDetectionService.Tests;

public sealed class DetectionTests
{
    private static FeatureVector Normal(double amount = 100000) => new(amount, 0, 0, 0, 1, 1, 0, 0, 12, .1);
    [Theory]
    [InlineData(124999999, "ALLOW")]
    [InlineData(125000000, "REVIEW")]
    [InlineData(249999999, "REVIEW")]
    [InlineData(250000000, "BLOCK")]
    public void AmountBoundaries(double amount, string expected)
    {
        var p = new RulePolicy();
        var rules = RuleEngine.Evaluate(Normal(amount), p);
        Assert.Equal(expected, RiskDecision.From(rules.Count == 0 ? 0 : rules.Max(r => r.Score), p, rules.Any(r => r.Critical)));
    }
    [Fact]
    public void NewDeviceAloneDoesNotFlagNewUser() => Assert.Empty(RuleEngine.Evaluate(Normal(), new()));
    [Fact]
    public void FeatureHistoryExcludesCurrentFutureAndExpired()
    {
        var now = DateTimeOffset.Parse("2026-01-02T12:00:00Z");
        Transaction Tx(DateTimeOffset at, string user = "u") => new()
        {
            Timestamp = at,
            UserId = user,
            DeviceHash = "d",
            IpHash = "i",
            Amount = 100,
            Country = "VN"
        };
        var f = FeatureBuilder.Build(Tx(now), [Tx(now), Tx(now.AddSeconds(1)), Tx(now.AddHours(-25)), Tx(now.AddMinutes(-10))]);
        Assert.Equal(1, f.TransactionCount1h);
        Assert.Equal(1, f.TransactionCount24h);
        Assert.Equal(0, f.IsNewDevice);
        Assert.Equal(1, f.SharedDeviceUsers);
    }
    private static async Task<FraudDbContext> Db()
    {
        var db = new FraudDbContext(new DbContextOptionsBuilder<FraudDbContext>().UseSqlite("Data Source=:memory:").Options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        db.Policies.Add(new());
        await db.SaveChangesAsync();
        return db;
    }
    private static TransactionRequest Request(Guid? id = null, string mode = "RulesOnly", decimal amount = 100000) => new(id, "user", amount, "VND", "shop", "grocery", "VN", "device", "192.0.2.1", DateTimeOffset.Parse("2026-01-02T12:00:00Z"), mode);
    [Fact]
    public async Task RulesOnlyNeverCallsAiAndPersistsNullScore()
    {
        await using var db = await Db();
        var ai = new FakeAi(new(.9, "test", "1"));
        var svc = new DetectionService(new FraudRepository(db), ai, new PiiHasher("test"), new());
        var r = await svc.AnalyzeAsync(Request(), default);
        Assert.Equal(0, ai.Calls);
        Assert.Null(r.MlScore);
        Assert.Equal("Disabled", r.AiStatus);
        Assert.Equal("ALLOW", r.Decision);
    }
    [Fact]
    public async Task UnavailableAiDoesNotFailOpen()
    {
        await using var db = await Db();
        var svc = new DetectionService(new FraudRepository(db), new FakeAi(null), new PiiHasher("test"), new());
        var r = await svc.AnalyzeAsync(Request(mode: "Hybrid"), default);
        Assert.Equal("REVIEW", r.Decision);
        Assert.Null(r.MlScore);
        Assert.Equal("UnavailableReviewFallback", r.AiStatus);
    }
    [Fact]
    public async Task CriticalRuleCannotBeDilutedByModel()
    {
        await using var db = await Db();
        var svc = new DetectionService(new FraudRepository(db), new FakeAi(new(0, "test", "1")), new PiiHasher("test"), new());
        var r = await svc.AnalyzeAsync(Request(mode: "Hybrid", amount: 300000000), default);
        Assert.Equal("BLOCK", r.Decision);
        Assert.True(r.FinalScore >= .95);
    }
    [Fact]
    public async Task IdempotentReplayDoesNotAddHistoryAndConflictingReplayFails()
    {
        await using var db = await Db();
        var svc = new DetectionService(new FraudRepository(db), new FakeAi(null), new PiiHasher("test"), new());
        var req = Request(Guid.NewGuid());
        var first = await svc.AnalyzeAsync(req, default);
        var second = await svc.AnalyzeAsync(req, default);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await db.Transactions.CountAsync());
        await Assert.ThrowsAsync<ConflictException>(() => svc.AnalyzeAsync(req with { Amount = 200000 }, default));
    }
    [Fact]
    public async Task SnapshotKeepsRuleVersionAfterPolicyChanges()
    {
        await using var db = await Db();
        var repo = new FraudRepository(db);
        var svc = new DetectionService(repo, new FakeAi(null), new PiiHasher("test"), new());
        var r = await svc.AnalyzeAsync(Request(), default);
        var policy = await repo.PolicyAsync(default);
        // Use a detached request to exercise optimistic version checking.
        var request = new RulePolicy { Version = policy.Version, HighAmount = 1000000 };
        await repo.UpdatePolicyAsync(request, "admin", default);
        Assert.Equal(1, (await repo.GetAsync(r.Id, default))!.Evaluation.RuleVersion);
        Assert.Equal(2, (await repo.PolicyAsync(default)).Version);
        await Assert.ThrowsAsync<ConflictException>(() => repo.UpdatePolicyAsync(new RulePolicy { Version = 1 }, "admin", default));
    }
    [Fact]
    public void HmacIsStableAndDoesNotStoreRawIdentifier()
    {
        var h = new PiiHasher("secret");
        Assert.Equal(h.Hash("device"), h.Hash("device"));
        Assert.NotEqual("device", h.Hash("device"));
        Assert.NotEqual(h.Hash("device"), new PiiHasher("other").Hash("device"));
    }
    private sealed class FakeAi(AiPrediction? prediction) : IAiClient
    {
        public int Calls;
        public Task<AiPrediction?> PredictAsync(Guid id, FeatureVector f, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(prediction);
        }
        public Task<string> GetStatusAsync(CancellationToken ct) => Task.FromResult("Test");
        public Task<string?> GetMetricsAsync(CancellationToken ct) => Task.FromResult<string?>(null);
    }
}
