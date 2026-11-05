using FraudDetectionService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace FraudDetectionService.Infrastructure.Persistence.Configurations;

public sealed class AnalystReviewConfiguration : IEntityTypeConfiguration<AnalystReview>
{
    public void Configure(EntityTypeBuilder<AnalystReview> b)
    {
        b.HasOne<Transaction>().WithMany().HasForeignKey(r => r.TransactionId);
    }
}
