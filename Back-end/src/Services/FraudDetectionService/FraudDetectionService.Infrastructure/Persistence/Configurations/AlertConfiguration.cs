using FraudDetectionService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FraudDetectionService.Infrastructure.Persistence.Configurations;

public sealed class AlertConfiguration : IEntityTypeConfiguration<Alert>
{
    public void Configure(EntityTypeBuilder<Alert> b)
    {
        b.HasKey(a => a.Id);
        b.Property(a => a.Version).IsConcurrencyToken();
        b.HasIndex(a => a.TransactionId).IsUnique();
        b.HasIndex(a => new { a.Status, a.Priority, a.DueAt, a.Id });
        b.HasIndex(a => new { a.OwnerUserId, a.Status, a.DueAt });
        b.HasIndex(a => new { a.Status, a.DueAt });
        b.HasOne<Evaluation>().WithOne().HasForeignKey<Alert>(a => a.TransactionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(a => a.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(a => a.ClosedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable("Alerts", t =>
        {
            t.HasCheckConstraint("CK_Alert_State", "Status IN ('New','Assigned','InProgress','Waiting','Escalated','Closed')");
            t.HasCheckConstraint("CK_Alert_Owner", "(Status = 'New' AND OwnerUserId IS NULL) OR (Status = 'Escalated') OR (Status IN ('Assigned','InProgress','Waiting','Closed') AND OwnerUserId IS NOT NULL)");
            t.HasCheckConstraint("CK_Alert_Priority", "Priority IN ('P1','P2','P3')");
        });
    }
}
public sealed class AlertEventConfiguration : IEntityTypeConfiguration<AlertEvent>
{
    public void Configure(EntityTypeBuilder<AlertEvent> b)
    {
        b.HasKey(e => e.Id);
        b.HasOne<Alert>().WithMany().HasForeignKey(e => e.AlertId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(e => e.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.AlertId, e.Version }).IsUnique();
        b.HasIndex(e => new { e.AlertId, e.HandlingEpisode }).IsUnique().HasFilter("Type = 'Escalated'");
    }
}
public sealed class AlertCommandReceiptConfiguration : IEntityTypeConfiguration<AlertCommandReceipt>
{
    public void Configure(EntityTypeBuilder<AlertCommandReceipt> b)
    {
        b.HasKey(r => new { r.AlertId, r.RequestId });
        b.HasOne<Alert>().WithMany().HasForeignKey(r => r.AlertId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(r => r.ActorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
