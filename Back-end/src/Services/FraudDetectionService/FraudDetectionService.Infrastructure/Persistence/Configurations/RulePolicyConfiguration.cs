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
        b.Property(p => p.AnalysisStrategy).HasDefaultValue("LegacyScore");
        b.Property(p => p.BurstWindowSeconds).HasDefaultValue(60);
        b.Property(p => p.BurstCount).HasDefaultValue(3);
        b.Property(p => p.MinimumHistory).HasDefaultValue(20);
        b.Property(p => p.AmountDeviationThreshold).HasDefaultValue(2.5);
        b.Property(p => p.SharedIpUsers).HasDefaultValue(5);
    }
}
