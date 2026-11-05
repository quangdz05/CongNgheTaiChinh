using FraudDetectionService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace FraudDetectionService.Infrastructure.Persistence.Configurations;

public sealed class RulePolicyConfiguration : IEntityTypeConfiguration<RulePolicy>
{
    public void Configure(EntityTypeBuilder<RulePolicy> b)
    {
        b.HasKey(p => p.Id);
        b.Property(p => p.Version).IsConcurrencyToken();
    }
}
