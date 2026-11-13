using FraudDetectionService.Application;
using FraudDetectionService.Domain;
using FraudDetectionService.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
namespace FraudDetectionService.Infrastructure;

public sealed class FraudRepository(FraudDbContext db, TimeProvider? clock = null) : IFraudRepository
{
    private DbSet<Transaction> Transactions => db.Transactions;
    private DbSet<Evaluation> Evaluations => db.Evaluations;
    private DbSet<AnalystReview> Reviews => db.Reviews;
    private DbSet<RulePolicy> Policies => db.Policies;
    private DbSet<AppUser> Users => db.Users;
    private DbSet<AuditEntry> Audits => db.Audits;
    public Task<Transaction?> GetAsync(Guid id, CancellationToken ct) => Transactions.Include(t => t.Evaluation).SingleOrDefaultAsync(t => t.Id == id, ct);
    public Task<List<Transaction>> HistoryAsync(DateTimeOffset before, string userId, string deviceHash, string ipHash, CancellationToken ct) => Transactions.AsNoTracking()
        .Where(t => t.Timestamp < before && t.Timestamp >= before.AddHours(-24)
            && (t.UserId == userId || t.DeviceHash == deviceHash || t.IpHash == ipHash))
        .Select(t => new Transaction { UserId = t.UserId, Amount = t.Amount, Timestamp = t.Timestamp, DeviceHash = t.DeviceHash, IpHash = t.IpHash, Country = t.Country })
        .ToListAsync(ct);
    public async Task AddAsync(Transaction tx, CancellationToken ct)
    {
        Transactions.Add(tx);
        if (tx.Evaluation.Decision != "ALLOW")
        {
            var alert = AlertPolicy.Create(tx, (clock ?? TimeProvider.System).GetUtcNow());
            db.Alerts.Add(alert);
            db.AlertEvents.Add(AlertWorkflowService.Event(alert, "Created", alert.CreatedAt));
        }
        await db.SaveChangesAsync(ct);
    }
    public Task<RulePolicy> PolicyAsync(CancellationToken ct) => Policies.SingleAsync(ct);
    private IQueryable<Transaction> Filter(string? decision, string? search)
    {
        var q = Transactions.AsNoTracking().Include(t => t.Evaluation).AsQueryable();
        if (decision == "ALERTS")
            q = q.Where(t => t.Evaluation.Decision != "ALLOW");
        else if (!string.IsNullOrWhiteSpace(decision))
            q = q.Where(t => t.Evaluation.Decision == decision);
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(t => t.UserId.Contains(search) || t.Merchant.Contains(search));
        return q;
    }
    public Task<List<Transaction>> ListAsync(string? decision, string? search, int page, int size, CancellationToken ct) => Filter(decision, search).OrderByDescending(t => t.Timestamp).ThenBy(t => t.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
    public async Task<List<TransactionSummary>> ListSummariesAsync(string? decision, string? search, int page, int size, CancellationToken ct)
    {
        var rows = await Filter(decision, search).OrderByDescending(t => t.Timestamp).ThenBy(t => t.Id)
            .Skip((page - 1) * size).Take(size)
            .Select(t => new { t.Id, t.UserId, t.Amount, t.Currency, t.Merchant, t.Timestamp,
                t.Evaluation.FinalScore, t.Evaluation.Decision, t.Evaluation.AnalysisStrategy, t.Evaluation.RiskFindingsJson }).ToListAsync(ct);
        return rows.Select(t => new TransactionSummary(t.Id, t.UserId, t.Amount, t.Currency, t.Merchant, t.Timestamp,
            t.AnalysisStrategy == "RiskSignals" ? null : t.FinalScore, t.Decision, t.AnalysisStrategy, JsonSerializer.Deserialize<List<RiskFinding>>(t.RiskFindingsJson, DetectionService.Json))).ToList();
    }
    public Task<int> CountAsync(string? decision, string? search, CancellationToken ct) => Filter(decision, search).CountAsync(ct);
    public Task<List<AnalystReview>> ReviewsAsync(Guid id, CancellationToken ct) => Reviews.AsNoTracking().Where(r => r.TransactionId == id).OrderByDescending(r => r.ReviewedAt).ToListAsync(ct);
    public async Task ReviewAsync(AnalystReview review, CancellationToken ct)
    {
        Reviews.Add(review);
        Audits.Add(new()
        {
            Actor = review.Reviewer,
            Action = "REVIEW",
            Details = $"{review.TransactionId}: {review.IsFraud}"
        });
        await db.SaveChangesAsync(ct);
    }
    public async Task UpdatePolicyAsync(RulePolicy p, string actor, CancellationToken ct)
    {
        var current = await PolicyAsync(ct);
        if (current.Version != p.Version)
            throw new ConflictException("Cấu hình đã thay đổi. Hãy tải lại trước khi lưu.");
        p.Id = 1;
        p.Version++;
        p.UpdatedBy = actor;
        db.Entry(current).CurrentValues.SetValues(p);
        Audits.Add(new()
        {
            Actor = actor,
            Action = "POLICY_UPDATED",
            Details = JsonSerializer.Serialize(p, DetectionService.Json)
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException) { throw new ConflictException("Xung đột cấu hình. Hãy tải lại."); }
    }
    public async Task<object> StatsAsync(CancellationToken ct)
    {
        var totals = await Transactions.GroupBy(t => 1).Select(g => new
        {
            Total = g.Count(), Amount = g.Sum(t => t.Amount),
            Allow = g.Count(t => t.Evaluation.Decision == "ALLOW"),
            Review = g.Count(t => t.Evaluation.Decision == "REVIEW"),
            Block = g.Count(t => t.Evaluation.Decision == "BLOCK"),
            AverageLatencyMs = g.Average(t => t.Evaluation.LatencyMs)
        }).SingleOrDefaultAsync(ct);
        var pending = await Transactions.CountAsync(t => t.Evaluation.Decision != "ALLOW" && !Reviews.Any(r => r.TransactionId == t.Id), ct);
        var confirmedFraud = await Transactions.CountAsync(t => Reviews.Where(r => r.TransactionId == t.Id)
            .OrderByDescending(r => r.ReviewedAt).ThenByDescending(r => r.Id).Select(r => r.IsFraud).FirstOrDefault(), ct);
        // Only charts and device links use a bounded sample; cards cover the entire database.
        var rows = await Transactions.AsNoTracking().OrderByDescending(t => t.Timestamp).ThenBy(t => t.Id).Take(10000)
            .Select(t => new { t.Timestamp, t.DeviceHash, t.UserId, Decision = t.Evaluation.Decision }).ToListAsync(ct);
        return new
        {
            total = totals?.Total ?? 0,
            sampled = rows.Count,
            amount = totals?.Amount ?? 0,
            allow = totals?.Allow ?? 0,
            review = totals?.Review ?? 0,
            block = totals?.Block ?? 0,
            pending,
            confirmedFraud,
            averageLatencyMs = totals?.AverageLatencyMs ?? 0,
            daily = rows.GroupBy(t => t.Timestamp.UtcDateTime.ToString("yyyy-MM-dd")).OrderBy(g => g.Key).Select(g => new { date = g.Key, total = g.Count(), flagged = g.Count(t => t.Decision != "ALLOW") }),
            rings = rows.GroupBy(t => t.DeviceHash).Where(g => g.Select(t => t.UserId).Distinct().Count() >= 3).Select(g => new { device = g.Key[..12], users = g.Select(t => t.UserId).Distinct().ToList(), transactions = g.Count() }).Take(20)
        };
    }
    public Task<AppUser?> UserAsync(string username, CancellationToken ct) => Users.AsNoTracking().SingleOrDefaultAsync(u => u.Username == username, ct);
    public Task<List<AuditEntry>> AuditAsync(CancellationToken ct) => Audits.AsNoTracking().OrderByDescending(a => a.At).Take(100).ToListAsync(ct);
}
