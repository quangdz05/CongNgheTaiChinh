using FraudDetectionService.Application;
using FraudDetectionService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Text.Json;
namespace FraudDetectionService.Infrastructure;

public sealed class FraudDbContext(DbContextOptions<FraudDbContext> options) : DbContext(options)
{
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Evaluation> Evaluations => Set<Evaluation>();
    public DbSet<AnalystReview> Reviews => Set<AnalystReview>();
    public DbSet<RulePolicy> Policies => Set<RulePolicy>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<AuditEntry> Audits => Set<AuditEntry>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<AlertEvent> AlertEvents => Set<AlertEvent>();
    public DbSet<AlertCommandReceipt> AlertCommandReceipts => Set<AlertCommandReceipt>();
    protected override void ConfigureConventions(ModelConfigurationBuilder builder) => builder.Properties<DateTimeOffset>().HaveConversion<UtcTicksConverter>();
    protected override void OnModelCreating(ModelBuilder b) => b.ApplyConfigurationsFromAssembly(typeof(FraudDbContext).Assembly);
}
public sealed class UtcTicksConverter() : ValueConverter<DateTimeOffset, long>(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
