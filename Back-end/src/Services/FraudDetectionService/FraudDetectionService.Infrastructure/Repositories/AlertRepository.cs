using FraudDetectionService.Application;
using FraudDetectionService.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FraudDetectionService.Infrastructure;

public sealed class AlertRepository(FraudDbContext db) : IAlertRepository
{
    public Task<Alert?> FindAsync(Guid id, CancellationToken ct) => db.Alerts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct);
    public Task<AlertCommandReceipt?> ReceiptAsync(Guid id, Guid requestId, CancellationToken ct) => db.AlertCommandReceipts.AsNoTracking().SingleOrDefaultAsync(r => r.AlertId == id && r.RequestId == requestId, ct);
    public async Task SaveAsync(Alert alert, AlertEvent entry, AlertCommandReceipt? receipt, CancellationToken ct)
    {
        db.Attach(alert);
        db.Entry(alert).State = EntityState.Modified;
        db.Entry(alert).Property(a => a.Version).OriginalValue = entry.BeforeVersion;
        db.AlertEvents.Add(entry);
        if (receipt != null) db.AlertCommandReceipts.Add(receipt);
        db.Audits.Add(new AuditEntry { Actor = entry.ActorUserId?.ToString() ?? "System", Action = "ALERT_" + entry.Type.ToUpperInvariant(), At = entry.At,
            Details = $"AlertId={alert.Id};version={alert.Version};requestId={entry.RequestId}" });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new AlertException(409, "ALERT_CONFLICT", "Cảnh báo đã được người khác cập nhật. Hãy tải lại."); }
        catch (DbUpdateException e) when (e.InnerException is SqliteException { SqliteErrorCode: 19 }) { throw new AlertException(409, "ALERT_CONFLICT", "Lệnh đã được xử lý hoặc cảnh báo đã thay đổi."); }
        catch (Exception e) when (e is SqliteException { SqliteErrorCode: 5 or 6 } || e.InnerException is SqliteException { SqliteErrorCode: 5 or 6 }) { throw new AlertException(503, "ALERT_BUSY", "Database đang bận. Hãy thử lại cùng requestId."); }
        finally { db.ChangeTracker.Clear(); }
    }
    public async Task<AlertPage> ListAsync(AlertFilter f, AlertActor actor, DateTimeOffset now, CancellationToken ct)
    {
        if (f.Page is < 1 or > 100000 || f.Size is < 1 or > 100 || f.Search?.Length > 100 || (f.Status != null && f.Status != "All" && !AlertPolicy.States.Contains(f.Status)) || (f.Priority != null && !AlertPolicy.Priorities.Contains(f.Priority)))
            throw new AlertException(400, "INVALID_ALERT_FILTER", "Bộ lọc/phân trang không hợp lệ.");
        var query = db.Alerts.AsNoTracking().AsQueryable();
        query = f.Status switch { null => query.Where(a => a.Status != "Closed"), "All" => query, _ => query.Where(a => a.Status == f.Status) };
        if (f.Priority != null) query = query.Where(a => a.Priority == f.Priority);
        if (f.Owner == "mine") query = query.Where(a => a.OwnerUserId == actor.Id);
        else if (f.Owner == "unassigned") query = query.Where(a => a.OwnerUserId == null);
        else if (!string.IsNullOrWhiteSpace(f.Owner))
        {
            if (!actor.IsAdmin) throw new AlertException(403, "ALERT_FORBIDDEN", "Chỉ Admin được lọc tài khoản cụ thể.");
            if (!int.TryParse(f.Owner, out var owner) || owner <= 0) throw new AlertException(400, "INVALID_ALERT_FILTER", "Owner không hợp lệ.");
            query = query.Where(a => a.OwnerUserId == owner);
        }
        if (f.Overdue.HasValue) query = f.Overdue.Value ? query.Where(a => a.Status != "Closed" && a.DueAt <= now) : query.Where(a => a.Status == "Closed" || a.DueAt > now);
        var rows = from a in query
                   join t in db.Transactions on a.TransactionId equals t.Id
                   join e in db.Evaluations on a.TransactionId equals e.TransactionId
                   join u in db.Users on a.OwnerUserId equals u.Id into owners
                   from u in owners.DefaultIfEmpty()
                   select new { a.Id, a.TransactionId, a.Status, a.Priority, a.OwnerUserId, OwnerName = u == null ? null : u.Username, a.Resolution, a.CreatedAt, a.DueAt, a.Version, t.UserId, t.Merchant, t.Amount, t.Currency, e.Decision };
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var text = f.Search.Trim();
            if (Guid.TryParse(text, out var id)) rows = rows.Where(r => r.Id == id || r.TransactionId == id);
            else rows = rows.Where(r => r.UserId.Contains(text) || r.Merchant.Contains(text));
        }
        var total = await rows.CountAsync(ct);
        var items = await rows.OrderBy(r => r.Priority).ThenBy(r => r.DueAt).ThenBy(r => r.CreatedAt).ThenBy(r => r.Id).Skip((f.Page - 1) * f.Size).Take(f.Size)
            .Select(r => new AlertRow(r.Id, r.TransactionId, r.Status, r.Priority, r.OwnerUserId, r.OwnerName, r.Resolution, r.CreatedAt, r.DueAt, r.Version, r.UserId, r.Merchant, r.Amount, r.Currency, r.Decision)).ToListAsync(ct);
        return new(items, total, f.Page, f.Size, now);
    }
    public async Task<AlertStats> StatsAsync(DateTimeOffset now, CancellationToken ct)
    {
        var counts = await db.Alerts.GroupBy(a => a.Status).Select(g => new { Key = g.Key, Count = g.Count() }).ToListAsync(ct);
        var states = AlertPolicy.States.ToDictionary(s => s, s => counts.Find(c => c.Key == s)?.Count ?? 0);
        var priorities = await db.Alerts.Where(a => a.Status != "Closed").GroupBy(a => a.Priority).Select(g => new { Key = g.Key, Count = g.Count() }).ToListAsync(ct);
        var owners = await db.Alerts.Where(a => a.Status != "Closed").GroupBy(a => a.OwnerUserId).Select(g => new { Key = g.Key, Count = g.Count() }).ToListAsync(ct);
        return new(states.Where(s => s.Key != "Closed").Sum(s => s.Value),
            await db.Alerts.CountAsync(a => a.Status != "Closed" && a.DueAt <= now, ct),
            await db.Alerts.CountAsync(a => a.Status == "Closed" && a.Resolution == "Unknown", ct),
            await db.Alerts.CountAsync(a => a.Status == "Closed" && a.Resolution == "Disputed", ct), states,
            priorities.ToDictionary(p => p.Key, p => p.Count), owners.ToDictionary(o => o.Key?.ToString() ?? "unassigned", o => o.Count), now);
    }
    public Task<List<AlertUser>> UsersAsync(CancellationToken ct) => db.Users.AsNoTracking().Where(u => u.Role == "Admin" || u.Role == "Analyst").OrderBy(u => u.Username).Select(u => new AlertUser(u.Id, u.Username, u.Role)).ToListAsync(ct);
    public async Task<AlertTimeline> TimelineAsync(Guid id, int page, int size, CancellationToken ct)
    {
        if (page is < 1 or > 100000 || size is < 1 or > 100) throw new AlertException(400, "INVALID_ALERT_FILTER", "Phân trang không hợp lệ.");
        var query = db.AlertEvents.AsNoTracking().Where(e => e.AlertId == id);
        return new(await query.OrderByDescending(e => e.Version).ThenBy(e => e.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct), await query.CountAsync(ct), page, size);
    }
    public Task<List<Guid>> DueAsync(DateTimeOffset now, CancellationToken ct) => db.Alerts.AsNoTracking()
        .Where(a => a.Status != "Closed" && a.Status != "Escalated" && a.EscalationEligibleAt <= now && (a.DueAt <= now || (a.Status == "Waiting" && a.WaitingUntil <= now)))
        .OrderBy(a => a.DueAt).ThenBy(a => a.Id).Take(500).Select(a => a.Id).ToListAsync(ct);
}
