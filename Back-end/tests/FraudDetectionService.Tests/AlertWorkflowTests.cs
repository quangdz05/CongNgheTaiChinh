using System.Text.Json;
using FraudDetectionService.Application;
using FraudDetectionService.Domain;
using FraudDetectionService.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace FraudDetectionService.Tests;

public sealed class AlertWorkflowTests
{
    private static readonly AlertActor Admin = new(1, "admin", "Admin");
    private static readonly AlertActor Analyst = new(2, "a", "Analyst");
    private static readonly AlertActor Other = new(3, "b", "Analyst");
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-11-09T10:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Rig : IAsyncDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), "fraud-alert-" + Guid.NewGuid() + ".db");
        public Clock Clock { get; } = new();
        public FraudDbContext Open() => new(new DbContextOptionsBuilder<FraudDbContext>().UseSqlite($"Data Source={path};Pooling=False;Default Timeout=10").Options);
        public static async Task<Rig> Create()
        {
            var rig = new Rig();
            await using var db = rig.Open();
            await db.Database.EnsureCreatedAsync();
            db.Users.AddRange(new AppUser { Id = 1, Username = "admin", Role = "Admin" }, new AppUser { Id = 2, Username = "a", Role = "Analyst" }, new AppUser { Id = 3, Username = "b", Role = "Analyst" });
            await db.SaveChangesAsync();
            return rig;
        }
        public async Task<Alert> New(string decision = "REVIEW", string aiStatus = "Disabled")
        {
            await using var db = Open();
            var tx = Tx(decision, aiStatus);
            await new FraudRepository(db, Clock).AddAsync(tx, default);
            return await db.Alerts.AsNoTracking().SingleAsync(a => a.TransactionId == tx.Id);
        }
        public async Task<AlertMutationResult> Do(Alert a, string command, AlertActor? actor = null, AlertCommand? request = null)
        {
            await using var db = Open();
            return await new AlertWorkflowService(new AlertRepository(db), Clock).ExecuteAsync(a.Id, command, request ?? Cmd(a), actor ?? Analyst, default);
        }
        public async Task<Alert> Working()
        {
            var a = (await Do(await New(), "claim")).Alert;
            return (await Do(a, "start")).Alert;
        }
        public async Task Scan()
        {
            await using var db = Open();
            await new AlertWorkflowService(new AlertRepository(db), Clock).EscalateDueAsync(default);
        }
        public async Task<Alert> Read(Guid id)
        {
            await using var db = Open();
            return await db.Alerts.SingleAsync(a => a.Id == id);
        }
        public ValueTask DisposeAsync() { File.Delete(path); File.Delete(path + "-wal"); File.Delete(path + "-shm"); return ValueTask.CompletedTask; }
    }
    private static Transaction Tx(string decision = "REVIEW", string aiStatus = "Disabled")
    {
        var id = Guid.NewGuid();
        return new() { Id = id, UserId = "customer", Merchant = "shop", Timestamp = DateTimeOffset.Parse("2020-01-01T00:00:00Z"), Amount = 100,
            Evaluation = new() { TransactionId = id, Decision = decision, AiStatus = aiStatus } };
    }
    private static AlertCommand Cmd(Alert a) => new() { RequestId = Guid.NewGuid(), ExpectedVersion = a.Version };
    private sealed class OfflineAi : IAiClient
    {
        public Task<AiPrediction?> PredictAsync(Guid transactionId, FeatureVector features, CancellationToken ct) => Task.FromResult<AiPrediction?>(null);
        public Task<string> GetStatusAsync(CancellationToken ct) => Task.FromResult("Unavailable");
        public Task<string?> GetMetricsAsync(CancellationToken ct) => Task.FromResult<string?>(null);
    }
    private static async Task Fails(int status, Func<Task> action)
    {
        var ex = await Assert.ThrowsAsync<AlertException>(action);
        Assert.Equal(status, ex.Status);
    }

    [Theory]
    [InlineData("BLOCK", "Disabled", "P1", 15)]
    [InlineData("REVIEW", "Available", "P2", 60)]
    [InlineData("REVIEW", "UnavailableReviewFallback", "P3", 240)]
    public async Task CreationSeparatesEvaluationAndSlaClock(string decision, string ai, string priority, int minutes)
    {
        await using var rig = await Rig.Create();
        var a = await rig.New(decision, ai);
        Assert.Equal("New", a.Status); Assert.Equal("Unknown", a.Resolution); Assert.Null(a.OwnerUserId);
        Assert.Equal(priority, a.Priority); Assert.Equal(rig.Clock.Now.AddMinutes(minutes), a.DueAt);
        await using var db = rig.Open();
        Assert.Equal(1, await db.AlertEvents.CountAsync());
        Assert.Equal(decision, (await db.Evaluations.SingleAsync()).Decision);
    }
    [Fact]
    public async Task AllowNeverCreatesAlert()
    {
        await using var rig = await Rig.Create(); await using var db = rig.Open();
        await new FraudRepository(db).AddAsync(Tx("ALLOW"), default);
        Assert.Equal(0, await db.Alerts.CountAsync()); Assert.Equal(0, await db.AlertEvents.CountAsync());
    }
    [Fact]
    public async Task DetectionReplayedOneHundredTimesCreatesOneAlertAndSnapshot()
    {
        await using var rig = await Rig.Create(); await using var db = rig.Open();
        db.Policies.Add(new()); await db.SaveChangesAsync();
        var detection = new DetectionService(new FraudRepository(db, rig.Clock), new OfflineAi(), new PiiHasher("test"), new EvaluationGate());
        var req = new TransactionRequest(Guid.NewGuid(), "replay", 150000000, "VND", "shop", "grocery", "VN", "device", "192.0.2.99", DateTimeOffset.Parse("2026-01-02T12:00:00Z"), "RulesOnly");
        for (var i = 0; i < 100; i++) Assert.Equal("REVIEW", (await detection.AnalyzeAsync(req, default)).Decision);
        await Assert.ThrowsAsync<ConflictException>(() => detection.AnalyzeAsync(req with { Amount = 150000001 }, default));
        Assert.Equal(1, await db.Transactions.CountAsync()); Assert.Equal(1, await db.Evaluations.CountAsync()); Assert.Equal(1, await db.Alerts.CountAsync()); Assert.Equal(1, await db.AlertEvents.CountAsync());
    }
    [Fact]
    public async Task EventFailureRollsBackTransactionEvaluationAndAlert()
    {
        await using var rig = await Rig.Create(); await using var db = rig.Open();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_alert BEFORE INSERT ON AlertEvents BEGIN SELECT RAISE(ABORT,'test failure'); END;");
        var tx = Tx();
        await Assert.ThrowsAsync<DbUpdateException>(() => new FraudRepository(db).AddAsync(tx, default));
        db.ChangeTracker.Clear();
        Assert.Equal(0, await db.Transactions.CountAsync()); Assert.Equal(0, await db.Evaluations.CountAsync()); Assert.Equal(0, await db.Alerts.CountAsync());
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_alert;");
        await new FraudRepository(db).AddAsync(tx, default);
        Assert.Equal(1, await db.Alerts.CountAsync());
    }
    [Fact]
    public async Task ConcurrentClaimsOnSeparateConnectionsHaveOneWinner()
    {
        await using var rig = await Rig.Create(); var a = await rig.New();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<int> Claim(AlertActor actor)
        {
            await gate.Task;
            try { await rig.Do(a, "claim", actor); return 200; } catch (AlertException e) { return e.Status; }
        }
        var first = Task.Run(() => Claim(Analyst)); var second = Task.Run(() => Claim(Other));
        gate.SetResult();
        Assert.Equal(new[] { 200, 409 }, (await Task.WhenAll(first, second)).Order().ToArray());
        await using var db = rig.Open();
        var current = await db.Alerts.SingleAsync(); Assert.Equal(2, current.Version); Assert.Contains(current.OwnerUserId, new int?[] { 2, 3 });
        Assert.Equal(1, await db.AlertEvents.CountAsync(e => e.Type == "Claimed"));
    }
    [Fact]
    public async Task ReceiptReplaySurvivesNewerStateButRejectsDifferentActorOrPayload()
    {
        await using var rig = await Rig.Create(); var a = await rig.New(); var cmd = Cmd(a);
        var claimed = await rig.Do(a, "claim", request: cmd);
        await rig.Do(claimed.Alert, "start");
        var replay = await rig.Do(a, "claim", request: cmd);
        Assert.True(replay.Replayed); Assert.Equal(2, replay.AppliedVersion); Assert.Equal("Assigned", replay.Alert.Status);
        await Fails(409, () => rig.Do(a, "claim", Other, cmd));
        await Fails(409, () => rig.Do(a, "start", request: cmd));
        Assert.Equal(3, (await rig.Read(a.Id)).Version);
    }
    [Fact]
    public async Task OwnerAndAdminCannotBypassWorkflow()
    {
        await using var rig = await Rig.Create(); var a = (await rig.Do(await rig.New(), "claim")).Alert;
        await Fails(403, () => rig.Do(a, "start", Other));
        await Fails(403, () => rig.Do(a, "start", Admin));
        await Fails(409, () => rig.Do(a, "close", request: Cmd(a) with { Notes = "enough notes", ClosureReasonCode = "InsufficientEvidence", Resolution = "Unknown" }));
        await Fails(400, () => rig.Do(a, "start", request: Cmd(a) with { OwnerUserId = 3 }));
        await Fails(403, () => rig.Do(a, "assign", request: Cmd(a) with { OwnerUserId = 3, Reason = "handover now" }));
    }
    [Fact]
    public async Task WaitingKeepsOwnerAndDeadlineStaleCloseFails()
    {
        await using var rig = await Rig.Create(); var a = await rig.Working();
        var waiting = (await rig.Do(a, "wait", request: Cmd(a) with { Reason = "Await records", WaitingType = "ExternalInformation", WaitingUntil = rig.Clock.Now.AddMinutes(10) })).Alert;
        Assert.Equal(a.DueAt, waiting.DueAt); Assert.Equal(a.OwnerUserId, waiting.OwnerUserId);
        await Fails(409, () => rig.Do(a, "close", request: Cmd(a) with { Notes = "still unclear", ClosureReasonCode = "InsufficientEvidence", Resolution = "Unknown" }));
        var resumed = (await rig.Do(waiting, "resume")).Alert;
        Assert.Equal("InProgress", resumed.Status); Assert.Null(resumed.WaitingUntil);
    }
    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(10081)]
    public async Task WaitingDeadlineBoundsAreValidated(int minutes)
    {
        await using var rig = await Rig.Create(); var a = await rig.Working();
        await Fails(400, () => rig.Do(a, "wait", request: Cmd(a) with { Reason = "Await records", WaitingType = "Other", WaitingUntil = rig.Clock.Now.AddMinutes(minutes) }));
    }
    [Fact]
    public async Task AdminReassignmentAndReleasePreserveSla()
    {
        await using var rig = await Rig.Create(); var a = await rig.Working();
        var moved = (await rig.Do(a, "assign", Admin, Cmd(a) with { OwnerUserId = 3, Reason = "Team handover" })).Alert;
        Assert.Equal("Assigned", moved.Status); Assert.Equal(3, moved.OwnerUserId); Assert.Equal(a.DueAt, moved.DueAt);
        await Fails(400, () => rig.Do(moved, "assign", Admin, Cmd(moved) with { OwnerUserId = 999, Reason = "Missing user" }));
        var released = (await rig.Do(moved, "release", Admin, Cmd(moved) with { Reason = "Return to queue" })).Alert;
        Assert.Equal("New", released.Status); Assert.Null(released.OwnerUserId); Assert.Equal(a.DueAt, released.DueAt);
        await Fails(409, () => rig.Do(a, "release", Admin, Cmd(a) with { Reason = "Invalid state" }));
    }
    [Fact]
    public async Task PriorityUpgradeShortensButDowngradeNeverExtendsDeadline()
    {
        await using var rig = await Rig.Create(); var a = await rig.New();
        var high = (await rig.Do(a, "priority", Admin, Cmd(a) with { Priority = "P1", Reason = "Urgent triage" })).Alert;
        Assert.Equal(a.SlaStartedAt.AddMinutes(15), high.DueAt);
        var low = (await rig.Do(high, "priority", Admin, Cmd(high) with { Priority = "P3", Reason = "Lower priority" })).Alert;
        Assert.Equal(high.DueAt, low.DueAt);
        await Fails(403, () => rig.Do(low, "priority", request: Cmd(low) with { Priority = "P1", Reason = "Not authorized" }));
    }
    [Theory]
    [InlineData("InsufficientEvidence", "Unknown")]
    [InlineData("ConflictingEvidence", "Disputed")]
    [InlineData("FraudConfirmed", "Fraud")]
    [InlineData("LegitimateConfirmed", "Legitimate")]
    public async Task ClosurePairsAreExplicitAndNeverWriteLegacyBool(string reason, string resolution)
    {
        await using var rig = await Rig.Create(); var a = await rig.Working();
        var evidence = resolution is "Fraud" or "Legitimate" ? new List<AlertEvidence> { new("InternalRecord", "CASE-123", "Document checked", rig.Clock.Now) } : null;
        var closed = (await rig.Do(a, "close", request: Cmd(a) with { ClosureReasonCode = reason, Resolution = resolution, Notes = "Reviewed with available information", Evidence = evidence })).Alert;
        Assert.Equal("Closed", closed.Status); Assert.Equal(resolution, closed.Resolution);
        await using var db = rig.Open();
        Assert.Empty(await db.Reviews.ToListAsync()); Assert.Equal("REVIEW", (await db.Evaluations.SingleAsync()).Decision);
        if (evidence != null) Assert.Contains("creatorUserId", closed.EvidenceJson);
    }
    [Theory]
    [InlineData(4)] [InlineData(1001)]
    public async Task ClosureRejectsNotesOutsideBounds(int length)
    {
        await using var rig = await Rig.Create(); var a = await rig.Working();
        await Fails(400, () => rig.Do(a, "close", request: Cmd(a) with { Notes = new string('x', length), Resolution = "Unknown", ClosureReasonCode = "InsufficientEvidence" }));
    }
    [Fact]
    public async Task ClosureRejectsMismatchedResolutionAndMissingOrSpoofedEvidence()
    {
        await using var rig = await Rig.Create(); var a = await rig.Working();
        var cmd = Cmd(a) with { Notes = "Investigation notes", Resolution = "Fraud", ClosureReasonCode = "FraudConfirmed" };
        await Fails(400, () => rig.Do(a, "close", request: cmd));
        await Fails(400, () => rig.Do(a, "close", request: cmd with { Evidence = [null!] }));
        await Fails(400, () => rig.Do(a, "close", request: cmd with { Resolution = "Unknown" }));
        await Fails(400, () => rig.Do(a, "close", request: cmd with { Evidence = [new("InternalRecord", "ref", "Reference notes", rig.Clock.Now, 3)] }));
        await Fails(400, () => rig.Do(a, "close", request: cmd with { Evidence = [new("InternalRecord", "ref", "Reference notes", rig.Clock.Now.AddSeconds(1))] }));
    }
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task SlaBoundaryCountsOverdueOpenAndPreservesClosedUnknownSeparately(int ticksLate, bool withinSla)
    {
        await using var rig = await Rig.Create(); var a = await rig.Working(); rig.Clock.Now = a.DueAt.AddTicks(ticksLate);
        await using var db = rig.Open(); var repo = new AlertRepository(db);
        Assert.Equal(1, (await repo.StatsAsync(rig.Clock.Now, default)).Overdue);
        var closed = (await rig.Do(a, "close", request: Cmd(a) with { Notes = "No evidence yet", Resolution = "Unknown", ClosureReasonCode = "InsufficientEvidence" })).Alert;
        Assert.Equal(withinSla, closed.ClosedAt <= closed.DueAt);
        Assert.Equal(withinSla, closed.ClosedWithinSla);
        var stats = await repo.StatsAsync(rig.Clock.Now, default);
        Assert.Equal(0, stats.OpenAlerts); Assert.Equal(0, stats.Overdue); Assert.Equal(1, stats.ClosedUnknown);
        await rig.Scan(); Assert.Equal("Closed", (await rig.Read(a.Id)).Status);
    }
    [Fact]
    public async Task WorkerEscalatesWaitingOnceRetainsOwnerAndRecoversAfterRestart()
    {
        await using var rig = await Rig.Create(); var a = await rig.Working();
        var waiting = (await rig.Do(a, "wait", request: Cmd(a) with { Reason = "Await records", WaitingType = "Other", WaitingUntil = rig.Clock.Now.AddMinutes(1) })).Alert;
        rig.Clock.Now = rig.Clock.Now.AddMinutes(2); await rig.Scan(); await rig.Scan();
        var escalated = await rig.Read(a.Id); Assert.Equal("Escalated", escalated.Status); Assert.Equal("WaitingExpired", escalated.EscalationReason); Assert.Equal(2, escalated.OwnerUserId); Assert.Null(escalated.WaitingUntil);
        await using var db = rig.Open(); Assert.Equal(1, await db.AlertEvents.CountAsync(e => e.Type == "Escalated"));
        var unowned = await rig.New(); rig.Clock.Now = unowned.DueAt.AddHours(1); await rig.Scan();
        var late = await rig.Read(unowned.Id); Assert.Equal("Escalated", late.Status); Assert.Null(late.OwnerUserId); Assert.Equal("SlaBreached", late.EscalationReason);
    }
    [Fact]
    public async Task TakingEscalatedWorkStartsGraceButDoesNotEraseBreach()
    {
        await using var rig = await Rig.Create(); var a = await rig.New(); rig.Clock.Now = a.DueAt; await rig.Scan();
        var escalated = await rig.Read(a.Id);
        await Fails(403, () => rig.Do(escalated, "assign", request: Cmd(escalated) with { Reason = "Accept escalation", OwnerUserId = 2 }));
        var assigned = (await rig.Do(escalated, "assign", Admin, Cmd(escalated) with { Reason = "Accept escalation", OwnerUserId = 2 })).Alert;
        Assert.Equal(2, assigned.HandlingEpisode); Assert.Equal(a.DueAt, assigned.DueAt); Assert.Null(assigned.EscalatedAt);
        rig.Clock.Now = rig.Clock.Now.AddMinutes(5).AddTicks(-1); await rig.Scan(); Assert.Equal("Assigned", (await rig.Read(a.Id)).Status);
        rig.Clock.Now = rig.Clock.Now.AddTicks(1); await rig.Scan(); Assert.Equal("Escalated", (await rig.Read(a.Id)).Status);
        await using var db = rig.Open(); Assert.Equal(2, await db.AlertEvents.CountAsync(e => e.Type == "Escalated"));
    }
    [Fact]
    public async Task ConcurrentWorkerAndCloseNeverCommitTheSameVersion()
    {
        await using var rig = await Rig.Create(); var a = await rig.Working(); rig.Clock.Now = a.DueAt;
        var close = Task.Run(async () =>
        {
            try { await rig.Do(a, "close", request: Cmd(a) with { Notes = "No evidence yet", Resolution = "Unknown", ClosureReasonCode = "InsufficientEvidence" }); return 200; }
            catch (AlertException e) { return e.Status; }
        });
        var scan = Task.Run(rig.Scan); await Task.WhenAll(close, scan);
        var current = await rig.Read(a.Id);
        Assert.Contains(current.Status, new[] { "Closed", "Escalated" }); Assert.Equal(a.Version + 1, current.Version);
        Assert.Equal(current.Status == "Closed" ? 200 : 409, await close);
        await using var db = rig.Open(); Assert.Equal(1, await db.AlertEvents.CountAsync(e => e.AlertId == a.Id && e.Version == current.Version));
    }
    [Fact]
    public async Task ReopenClearsCurrentClosureAndPreservesPreviousCycleInHistory()
    {
        await using var rig = await Rig.Create(); var a = await rig.Working();
        var closed = (await rig.Do(a, "close", request: Cmd(a) with { Notes = "Evidence conflicts", Resolution = "Disputed", ClosureReasonCode = "ConflictingEvidence" })).Alert;
        rig.Clock.Now = rig.Clock.Now.AddDays(1);
        await Fails(403, () => rig.Do(closed, "reopen", request: Cmd(closed) with { Reason = "New information", OwnerUserId = 2 }));
        var reopened = (await rig.Do(closed, "reopen", Admin, Cmd(closed) with { Reason = "New information", OwnerUserId = 2 })).Alert;
        Assert.Equal("Assigned", reopened.Status); Assert.Equal("Unknown", reopened.Resolution); Assert.Null(reopened.ClosedAt); Assert.Null(reopened.ClosureNotes); Assert.Equal(rig.Clock.Now.AddMinutes(60), reopened.DueAt);
        await using var db = rig.Open(); var old = await db.AlertEvents.SingleAsync(e => e.Type == "Closed");
        Assert.Equal("Disputed", JsonSerializer.Deserialize<Alert>(old.AfterJson, DetectionService.Json)!.Resolution);
    }
    [Fact]
    public async Task NotesAreAppendOnlyAndAtomicWithAuditReceipt()
    {
        await using var rig = await Rig.Create(); var a = await rig.Working();
        var noted = (await rig.Do(a, "notes", request: Cmd(a) with { Notes = "<script>text</script>" })).Alert;
        await Fails(403, () => rig.Do(noted, "notes", Other, Cmd(noted) with { Notes = "Not the owner" }));
        await using var db = rig.Open(); Assert.Equal(1, await db.AlertEvents.CountAsync(e => e.Type == "NoteAdded"));
        Assert.Equal(3, await db.AlertCommandReceipts.CountAsync()); Assert.Equal(3, await db.Audits.CountAsync(audit => audit.Action.StartsWith("ALERT_")));
    }
    [Fact]
    public async Task EventFailureDuringMutationRollsBackOwnerVersionAuditAndReceipt()
    {
        await using var rig = await Rig.Create(); var a = await rig.New();
        await using (var db = rig.Open()) await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_event BEFORE INSERT ON AlertEvents BEGIN SELECT RAISE(ABORT,'test'); END;");
        await Fails(409, () => rig.Do(a, "claim"));
        var unchanged = await rig.Read(a.Id); Assert.Equal(1, unchanged.Version); Assert.Null(unchanged.OwnerUserId);
        await using var verify = rig.Open(); Assert.Equal(0, await verify.AlertCommandReceipts.CountAsync()); Assert.Equal(0, await verify.Audits.CountAsync());
    }
    [Fact]
    public async Task FiltersTotalsAndPaginationAreAppliedInDatabase()
    {
        await using var rig = await Rig.Create(); var a = await rig.New("BLOCK"); await rig.New(); await rig.New("REVIEW", "UnavailableReviewFallback");
        await rig.Do(a, "claim");
        await using var db = rig.Open(); var repo = new AlertRepository(db);
        var first = await repo.ListAsync(new(Size: 1), Analyst, rig.Clock.Now, default);
        Assert.Equal(3, first.Total); Assert.Single(first.Items); Assert.Equal("P1", first.Items[0].Priority);
        Assert.Single((await repo.ListAsync(new(Owner: "mine"), Analyst, rig.Clock.Now, default)).Items);
        Assert.Equal(2, (await repo.ListAsync(new(Owner: "unassigned"), Analyst, rig.Clock.Now, default)).Total);
        Assert.Single((await repo.ListAsync(new(Search: a.TransactionId.ToString()), Analyst, rig.Clock.Now, default)).Items);
        await Fails(400, () => repo.ListAsync(new(Status: "Bad"), Analyst, rig.Clock.Now, default));
        await Fails(400, () => repo.ListAsync(new(Size: 101), Analyst, rig.Clock.Now, default));
        await Fails(403, () => repo.ListAsync(new(Owner: "3"), Analyst, rig.Clock.Now, default));
    }
    [Fact]
    public async Task LegacyBackfillKeepsConflictingReviewsAndDoesNotResetOnRerun()
    {
        await using var rig = await Rig.Create();
        await using var db = rig.Open(); var tx = Tx(); var allowed = Tx("ALLOW");
        db.Transactions.AddRange(tx, allowed); db.Reviews.AddRange(new AnalystReview { TransactionId = tx.Id, IsFraud = false, Reviewer = "deleted-user", Notes = "Old review" }, new AnalystReview { TransactionId = tx.Id, IsFraud = true, Reviewer = "old-user", Notes = "Conflicting review" }); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var before = await db.Evaluations.AsNoTracking().ToListAsync();
        await AlertBackfill.RunAsync(db, rig.Clock); var a = await db.Alerts.SingleAsync(); var created = a.CreatedAt;
        rig.Clock.Now = rig.Clock.Now.AddDays(1); await AlertBackfill.RunAsync(db, rig.Clock);
        Assert.Equal("New", a.Status); Assert.Equal("Unknown", a.Resolution); Assert.Equal("ImportedLegacy", a.SourceType); Assert.Null(a.OwnerUserId);
        Assert.Equal(created, (await db.Alerts.SingleAsync()).CreatedAt); Assert.Equal(1, await db.AlertEvents.CountAsync()); Assert.Equal(2, await db.Reviews.CountAsync());
        Assert.Equal(2, await db.Transactions.CountAsync()); Assert.Equal(before.Select(e => e.Decision).Order(), (await db.Evaluations.ToListAsync()).Select(e => e.Decision).Order());
    }
    [Fact]
    public async Task ActualMigrationsUpgradeExistingDatabaseWithoutChangingLegacyRows()
    {
        await using var rig = new Rig(); await using var db = rig.Open();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261106021653_OptimizeHistoryIndexes");
        var tx = Tx(); db.Transactions.Add(tx); await db.SaveChangesAsync();
        await db.Database.MigrateAsync(); await AlertBackfill.RunAsync(db, rig.Clock);
        Assert.Equal(1, await db.Alerts.CountAsync()); Assert.Equal(tx.Id, (await db.Transactions.SingleAsync()).Id);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
