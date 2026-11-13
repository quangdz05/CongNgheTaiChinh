using FraudDetectionService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace FraudDetectionService.Infrastructure.Persistence.Configurations;

public sealed class EvaluationConfiguration : IEntityTypeConfiguration<Evaluation>
{
    public void Configure(EntityTypeBuilder<Evaluation> b)
    {
        b.HasKey(e => e.TransactionId);
        b.Property(e => e.AnalysisStrategy).HasDefaultValue("LegacyScore");
        b.Property(e => e.RiskFindingsJson).HasDefaultValue("[]");
    }
}
