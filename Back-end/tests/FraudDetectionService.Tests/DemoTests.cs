using FluentValidation;
using FraudDetectionService.Application;
using FraudDetectionService.Application.Features.Demo.Commands;
using FraudDetectionService.Domain;
using FraudDetectionService.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FraudDetectionService.Tests;

public sealed class DemoTests
{
    private sealed class OfflineAi : IAiClient
    {
        public int Calls { get; private set; }
        public Task<AiPrediction?> PredictAsync(Guid transactionId, FeatureVector features, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult<AiPrediction?>(null);
        }
        public Task<string> GetStatusAsync(CancellationToken ct) => Task.FromResult("Unavailable");
        public Task<string?> GetMetricsAsync(CancellationToken ct) => Task.FromResult<string?>(null);
    }

    private static async Task Seed(FraudDbContext db)
    {
        await db.Database.EnsureCreatedAsync();
        db.Users.AddRange(new AppUser { Id = 1, Username = "admin", Role = "Admin" },
            new AppUser { Id = 2, Username = "analyst-a", Role = "Analyst" },
            new AppUser { Id = 3, Username = "analyst-b", Role = "Analyst" });
        db.Policies.Add(new RulePolicy());
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static RunDemoCommandHandler Handler(FraudDbContext db, OfflineAi ai)
    {
        var alerts = new AlertRepository(db);
        return new(new DetectionService(new FraudRepository(db), ai, new PiiHasher("demo-test"), new EvaluationGate()),
            alerts, new AlertWorkflowService(alerts, TimeProvider.System), TimeProvider.System);
    }

    [Theory]
    [InlineData(1, "Admin", 3, "RulesOnly", "Reassigned")]
    [InlineData(1, "Admin", 2, "Hybrid", "Reassigned")]
    [InlineData(2, "Analyst", 2, "RulesOnly", "Claimed")]
    public async Task DemoAssignsOnlyNewAlertsAndPersistsOwnerAndHistory(int actorId, string role, int owner, string mode, string eventType)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new FraudDbContext(new DbContextOptionsBuilder<FraudDbContext>().UseSqlite(connection).Options);
        await Seed(db);
        var ai = new OfflineAi();
        var handler = Handler(db, ai);
        var actor = new AlertActor(actorId, "creator", role);
        var prior = await handler.Handle(new("RulesOnly", 2, new(1, "admin", "Admin")), default);
        var result = await handler.Handle(new(mode, owner, actor), default);
        Assert.Equal(18, result.Count);
        var ids = result.Results.Select(r => r.Id).ToList();
        db.ChangeTracker.Clear();
        var alerts = await db.Alerts.Where(a => ids.Contains(a.TransactionId)).ToListAsync();
        Assert.NotEmpty(alerts);
        Assert.Equal(result.Results.Count(r => r.Decision != "ALLOW"), alerts.Count);
        Assert.All(alerts, a =>
        {
            Assert.Equal(owner, a.OwnerUserId);
            Assert.Equal("Assigned", a.Status);
            Assert.Equal(2, a.Version);
        });
        var alertIds = alerts.Select(a => a.Id).ToList();
        var events = await db.AlertEvents.Where(e => alertIds.Contains(e.AlertId) && e.Type == eventType).ToListAsync();
        Assert.Equal(alerts.Count, events.Count);
        Assert.All(events, e => Assert.Equal(actorId, e.ActorUserId));
        var page = await new AlertRepository(db).ListAsync(new(Owner: owner.ToString(), Search: "DEMO-" + result.Run), new(1, "admin", "Admin"), DateTimeOffset.UtcNow, default);
        Assert.Equal(alerts.Count, page.Total);
        Assert.All(page.Items, a => Assert.Equal(owner == 2 ? "analyst-a" : "analyst-b", a.OwnerName));
        var oldIds = prior.Results.Select(r => r.Id).ToList();
        Assert.All(await db.Alerts.Where(a => oldIds.Contains(a.TransactionId)).ToListAsync(), a => Assert.Equal(2, a.OwnerUserId));
        Assert.Equal(mode == "Hybrid" ? 18 : 0, ai.Calls);
    }

    [Theory]
    [InlineData(1, "Admin", null, 400)]
    [InlineData(1, "Admin", 0, 400)]
    [InlineData(1, "Admin", 999, 400)]
    [InlineData(1, "Admin", 1, 400)]
    [InlineData(2, "Analyst", 3, 403)]
    [InlineData(4, "Viewer", 2, 403)]
    public async Task InvalidAssignmentIsRejectedBeforeCreatingTransactions(int actorId, string role, int? owner, int status)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new FraudDbContext(new DbContextOptionsBuilder<FraudDbContext>().UseSqlite(connection).Options);
        await Seed(db);
        var ai = new OfflineAi();
        var exception = await Record.ExceptionAsync(() => Handler(db, ai).Handle(new("Hybrid", owner, new(actorId, "creator", role)), default));
        if (owner is null or <= 0) Assert.IsType<ValidationException>(exception);
        else Assert.Equal(status, Assert.IsType<AlertException>(exception).Status);
        Assert.Equal(0, await db.Transactions.CountAsync());
        Assert.Equal(0, await db.Alerts.CountAsync());
        Assert.Equal(0, ai.Calls);
    }
}
