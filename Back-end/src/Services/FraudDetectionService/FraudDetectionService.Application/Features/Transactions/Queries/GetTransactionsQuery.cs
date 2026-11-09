using FraudDetectionService.Domain.Repositories;
using FraudDetectionService.Domain;
namespace FraudDetectionService.Application.Features.Transactions.Queries;

public record GetTransactionsQuery(string? Decision, string? Search, int Page = 1, int Size = 20);
public record TransactionPage(IReadOnlyList<TransactionSummary> Items, int Total, int Page, int Size);
public sealed class GetTransactionsQueryHandler(IFraudRepository store)
{
    public async Task<TransactionPage> Handle(GetTransactionsQuery q, CancellationToken ct)
    {
        if (q.Page < 1 || q.Size is < 1 or > 100)
            throw new ArgumentException("Phân trang không hợp lệ.");
        if (q.Decision is not (null or "" or "ALLOW" or "REVIEW" or "BLOCK" or "ALERTS"))
            throw new ArgumentException("Quyết định không hợp lệ.");
        var rows = await store.ListSummariesAsync(q.Decision, q.Search, q.Page, q.Size, ct);
        return new(rows, await store.CountAsync(q.Decision, q.Search, ct), q.Page, q.Size);
    }
}
