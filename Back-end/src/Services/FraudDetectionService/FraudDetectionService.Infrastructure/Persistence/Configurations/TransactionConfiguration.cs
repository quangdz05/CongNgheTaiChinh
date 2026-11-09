using FraudDetectionService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace FraudDetectionService.Infrastructure.Persistence.Configurations;

public sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> b)
    {
        b.HasKey(t => t.Id);
        b.HasIndex(t => new { t.UserId, t.Timestamp });
        b.HasIndex(t => t.Timestamp);
        b.HasIndex(t => t.DeviceHash);
        b.HasIndex(t => new { t.DeviceHash, t.Timestamp });
        b.HasIndex(t => new { t.IpHash, t.Timestamp });
        b.HasOne(t => t.Evaluation).WithOne().HasForeignKey<Evaluation>(e => e.TransactionId).OnDelete(DeleteBehavior.Cascade);
    }
}
