using FraudDetectionService.Application;
using Microsoft.EntityFrameworkCore;

namespace FraudDetectionService.Infrastructure;

public static class AlertBackfill
{
    // Startup is awaited before hosted workers/HTTP start. Unique TransactionId makes reruns safe.
    public static async Task RunAsync(FraudDbContext db, TimeProvider clock, CancellationToken ct = default)
    {
        while (true)
        {
            var batch = await db.Transactions.AsNoTracking().Include(t => t.Evaluation)
                .Where(t => t.Evaluation.Decision != "ALLOW" && !db.Alerts.Any(a => a.TransactionId == t.Id)).OrderBy(t => t.Id).Take(500).ToListAsync(ct);
            if (batch.Count == 0) break;
            var now = clock.GetUtcNow();
            foreach (var tx in batch)
            {
                var a = Domain.AlertPolicy.Create(tx, now, imported: true);
                db.Alerts.Add(a);
                db.AlertEvents.Add(AlertWorkflowService.Event(a, "ImportedLegacy", now));
            }
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
    }
}
