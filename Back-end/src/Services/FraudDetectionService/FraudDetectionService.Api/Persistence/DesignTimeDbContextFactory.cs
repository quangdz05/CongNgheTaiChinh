using FraudDetectionService.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace FraudDetectionService.Api.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FraudDbContext>
{
    public FraudDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<FraudDbContext>().UseSqlite("Data Source=data/fraudshield.db").Options);
}
