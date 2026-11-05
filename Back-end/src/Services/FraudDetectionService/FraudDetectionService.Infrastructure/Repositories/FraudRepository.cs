using FraudDetectionService.Application;
using FraudDetectionService.Domain;
using FraudDetectionService.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
namespace FraudDetectionService.Infrastructure;

public sealed class FraudRepository(FraudDbContext db) : IFraudRepository
{
    private DbSet<Transaction> Transactions => db.Transactions;
    private DbSet<Evaluation> Evaluations => db.Evaluations;
    private DbSet<AnalystReview> Reviews => db.Reviews;
    private DbSet<RulePolicy> Policies => db.Policies;
    private DbSet<AppUser> Users => db.Users;
    private DbSet<AuditEntry> Audits => db.Audits;
    public Task<Transaction?> GetAsync(Guid id, CancellationToken ct) => Transactions.Include(t => t.Evaluation).SingleOrDefaultAsync(t => t.Id == id, ct);
    public Task<List<Transaction>> HistoryAsync(DateTimeOffset before, CancellationToken ct) => Transactions.AsNoTracking().Where(t => t.Timestamp < before && t.Timestamp >= before.AddHours(-24)).ToListAsync(ct);
    public async Task AddAsync(Transaction tx, CancellationToken ct)
    {
        Transactions.Add(tx);
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
        var rows = await Transactions.AsNoTracking().Include(t => t.Evaluation).OrderByDescending(t => t.Timestamp).Take(10000).ToListAsync(ct);
        var reviews = await Reviews.AsNoTracking().OrderByDescending(r => r.ReviewedAt).Take(10000).ToListAsync(ct);
        var reviewed = reviews.Select(r => r.TransactionId).ToHashSet();
        return new
        {
            total = await Transactions.CountAsync(ct),
            sampled = rows.Count,
            amount = rows.Sum(t => t.Amount),
            allow = rows.Count(t => t.Evaluation.Decision == "ALLOW"),
            review = rows.Count(t => t.Evaluation.Decision == "REVIEW"),
            block = rows.Count(t => t.Evaluation.Decision == "BLOCK"),
            pending = rows.Count(t => t.Evaluation.Decision != "ALLOW" && !reviewed.Contains(t.Id)),
            confirmedFraud = reviews.GroupBy(r => r.TransactionId).Count(g => g.First().IsFraud),
            averageLatencyMs = rows.Count == 0 ? 0 : rows.Average(t => t.Evaluation.LatencyMs),
            daily = rows.GroupBy(t => t.Timestamp.UtcDateTime.ToString("yyyy-MM-dd")).OrderBy(g => g.Key).Select(g => new { date = g.Key, total = g.Count(), flagged = g.Count(t => t.Evaluation.Decision != "ALLOW") }),
            rings = rows.GroupBy(t => t.DeviceHash).Where(g => g.Select(t => t.UserId).Distinct().Count() >= 3).Select(g => new { device = g.Key[..12], users = g.Select(t => t.UserId).Distinct().ToList(), transactions = g.Count() }).Take(20)
        };
    }
    public Task<AppUser?> UserAsync(string username, CancellationToken ct) => Users.AsNoTracking().SingleOrDefaultAsync(u => u.Username == username, ct);
    public Task<List<AuditEntry>> AuditAsync(CancellationToken ct) => Audits.AsNoTracking().OrderByDescending(a => a.At).Take(100).ToListAsync(ct);
}
