namespace FraudDetectionService.Domain.Repositories;

public interface IFraudRepository
{
    Task<Transaction?> GetAsync(Guid id, CancellationToken ct);
    Task<List<Transaction>> HistoryAsync(DateTimeOffset before, string userId, string deviceHash, string ipHash, CancellationToken ct);
    Task<List<TransactionSummary>> ListSummariesAsync(string? decision, string? search, int page, int size, CancellationToken ct);
    Task AddAsync(Transaction tx, CancellationToken ct);
    Task<RulePolicy> PolicyAsync(CancellationToken ct);
    Task<List<Transaction>> ListAsync(string? decision, string? search, int page, int size, CancellationToken ct);
    Task<int> CountAsync(string? decision, string? search, CancellationToken ct);
    Task<List<AnalystReview>> ReviewsAsync(Guid id, CancellationToken ct);
    Task ReviewAsync(AnalystReview review, CancellationToken ct);
    Task UpdatePolicyAsync(RulePolicy policy, string actor, CancellationToken ct);
    Task<object> StatsAsync(CancellationToken ct);
    Task<AppUser?> UserAsync(string username, CancellationToken ct);
    Task<List<AuditEntry>> AuditAsync(CancellationToken ct);
}
