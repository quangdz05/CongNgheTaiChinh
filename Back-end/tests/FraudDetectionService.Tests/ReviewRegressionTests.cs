using System.Data.Common;
using System.Text.Json;
using FraudDetectionService.Application;
using FraudDetectionService.Application.Features.Ai.Queries;
using FraudDetectionService.Application.Features.Transactions.Queries;
using FraudDetectionService.Domain;
using FraudDetectionService.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace FraudDetectionService.Tests;

public sealed class ReviewRegressionTests
{
    private static async Task<FraudDbContext> OpenDb(CommandCounter? counter = null)
    {
        var options = new DbContextOptionsBuilder<FraudDbContext>().UseSqlite("Data Source=:memory:");
        if (counter != null) options.AddInterceptors(counter);
        var db = new FraudDbContext(options.Options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        db.Policies.Add(new());
        await db.SaveChangesAsync();
        return db;
    }
    private static Transaction Tx(string user = "u", string device = "d", string ip = "i", string decision = "ALLOW")
    {
        var id = Guid.NewGuid();
        return new Transaction { Id = id, UserId = user, DeviceHash = device, IpHash = ip, Amount = 1,
            Timestamp = DateTimeOffset.Parse("2026-01-02T12:00:00Z"), Country = "VN",
            Evaluation = new Evaluation { TransactionId = id, Decision = decision } };
    }

    [Fact]
    public async Task GateAllowsUnrelatedWorkAndCancellationReleasesPartialLocks()
    {
        var gate = new EvaluationGate();
        using var first = await gate.EnterAsync(["user:a", "device:a"], default);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var unrelated = await gate.EnterAsync(["user:b", "device:b"], timeout.Token);
        using var cancel = new CancellationTokenSource();
        var waiting = gate.EnterAsync(["a-partial", "device:a"], cancel.Token);
        Assert.False(waiting.IsCompleted);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await waiting);
        using var recovered = await gate.EnterAsync(["a-partial"], timeout.Token);
    }

    [Theory]
    [InlineData("user:shared")]
    [InlineData("device:shared")]
    [InlineData("ip:shared")]
    [InlineData("id:shared")]
    public async Task GateSerializesEverySharedResource(string key)
    {
        var gate = new EvaluationGate();
        var first = await gate.EnterAsync([key], default);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var second = gate.EnterAsync([key], timeout.Token);
        Assert.False(second.IsCompleted);
        first.Dispose();
        using var acquired = await second;
    }

    [Fact]
    public async Task ComparisonReevaluatesRulesWithTheCurrentPolicy()
    {
        await using var db = await OpenDb();
        var tx = Tx();
        tx.Evaluation.RuleScore = .7;
        tx.Evaluation.FeaturesJson = JsonSerializer.Serialize(new FeatureVector(150_000_000, 0, 0, 0, 1, 1, 0, 0, 12, .1), DetectionService.Json);
        db.Transactions.Add(tx);
        (await db.Policies.SingleAsync()).HighAmount = 200_000_000;
        await db.SaveChangesAsync();
        var handler = new CompareEvaluationQueryHandler(new FraudRepository(db), new FakeAi());
        var result = JsonSerializer.SerializeToElement(await handler.Handle(new(tx.Id), default), DetectionService.Json);
        Assert.Equal(0, result.GetProperty("ruleScore").GetDouble());
        Assert.Equal("ALLOW", result.GetProperty("ruleDecision").GetString());
    }

    [Fact]
    public async Task HistoryIncludesOnlyRelevantUnexpiredRowsWithoutChangingFeatures()
    {
        await using var db = await OpenDb();
        var target = Tx("u", "d", "i"); target.Timestamp = target.Timestamp.AddMinutes(10);
        var history = new[] { Tx("u", "different", "different"), Tx("other", "d", "different"), Tx("third", "different", "i"), Tx("irrelevant", "x", "y") };
        db.Transactions.AddRange(history); await db.SaveChangesAsync();
        var selected = await new FraudRepository(db).HistoryAsync(target.Timestamp, target.UserId, target.DeviceHash, target.IpHash, default);
        Assert.Equal(3, selected.Count);
        Assert.Equal(FeatureBuilder.Build(target, history), FeatureBuilder.Build(target, selected));
    }

    [Fact]
    public async Task ListingUsesTwoQueriesRegardlessOfPageSize()
    {
        var counter = new CommandCounter();
        await using var db = await OpenDb(counter);
        db.Transactions.AddRange(Enumerable.Range(0, 12).Select(_ => Tx()));
        await db.SaveChangesAsync();
        counter.Reads = 0;
        var page = await new GetTransactionsQueryHandler(new FraudRepository(db)).Handle(new(null, null, 1, 12), default);
        Assert.Equal(12, page.Items.Count);
        Assert.Equal(2, counter.Reads);
    }

    [Fact]
    public async Task DashboardCountsAllRowsAndReviewsBeyondTheChartSample()
    {
        await using var db = await OpenDb();
        var oldAlert = Tx(decision: "REVIEW"); oldAlert.Timestamp = oldAlert.Timestamp.AddDays(-1);
        var normal = Enumerable.Range(0, 10_000).Select(_ => Tx()).ToArray();
        db.Transactions.Add(oldAlert); db.Transactions.AddRange(normal);
        db.Reviews.Add(new AnalystReview { TransactionId = oldAlert.Id, IsFraud = true, ReviewedAt = oldAlert.Timestamp });
        db.Reviews.AddRange(Enumerable.Range(0, 10_000).Select(_ => new AnalystReview { TransactionId = normal[0].Id, IsFraud = false }));
        await db.SaveChangesAsync();
        var stats = JsonSerializer.SerializeToElement(await new FraudRepository(db).StatsAsync(default), DetectionService.Json);
        Assert.Equal(10_001, stats.GetProperty("total").GetInt32());
        Assert.Equal(10_000, stats.GetProperty("allow").GetInt32());
        Assert.Equal(1, stats.GetProperty("review").GetInt32());
        Assert.Equal(0, stats.GetProperty("pending").GetInt32());
        Assert.Equal(1, stats.GetProperty("confirmedFraud").GetInt32());
        Assert.Equal(10_001m, stats.GetProperty("amount").GetDecimal());
        Assert.Equal(10_000, stats.GetProperty("sampled").GetInt32());
    }

    public sealed class CommandCounter : DbCommandInterceptor
    {
        public int Reads;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Reads++;
            return ValueTask.FromResult(result);
        }
    }
    private sealed class FakeAi : IAiClient
    {
        public Task<AiPrediction?> PredictAsync(Guid id, FeatureVector f, CancellationToken ct) => Task.FromResult<AiPrediction?>(new(.1, "regression", "1"));
        public Task<string> GetStatusAsync(CancellationToken ct) => Task.FromResult("Ready");
        public Task<string?> GetMetricsAsync(CancellationToken ct) => Task.FromResult<string?>(null);
    }
}
