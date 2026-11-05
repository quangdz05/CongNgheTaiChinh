using FraudDetectionService.Domain.Repositories;
namespace FraudDetectionService.Application.Features.Transactions.Queries;

public record GetTransactionsQuery(string? Decision, string? Search, int Page = 1, int Size = 20);
public record TransactionPage(IReadOnlyList<EvaluationView> Items, int Total, int Page, int Size);
public sealed class GetTransactionsQueryHandler(IFraudRepository store, DetectionService detection)
{
    public async Task<TransactionPage> Handle(GetTransactionsQuery q, CancellationToken ct)
    {
        if (q.Page < 1 || q.Size is < 1 or > 100)
            throw new ArgumentException("Phân trang không hợp lệ.");
        if (q.Decision is not (null or "" or "ALLOW" or "REVIEW" or "BLOCK" or "ALERTS"))
            throw new ArgumentException("Quyết định không hợp lệ.");
        var rows = await store.ListAsync(q.Decision, q.Search, q.Page, q.Size, ct);
        var views = new List<EvaluationView>();
        foreach (var row in rows)
            views.Add(await detection.ViewAsync(row, ct));
        return new(views, await store.CountAsync(q.Decision, q.Search, ct), q.Page, q.Size);
    }
}
