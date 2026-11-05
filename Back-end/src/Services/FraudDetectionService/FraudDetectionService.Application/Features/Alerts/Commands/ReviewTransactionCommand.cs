using FraudDetectionService.Domain;
using FraudDetectionService.Domain.Repositories;
namespace FraudDetectionService.Application.Features.Alerts.Commands;

public record ReviewTransactionCommand(Guid Id, bool IsFraud, string Notes, string Reviewer);
public sealed class ReviewTransactionCommandHandler(IFraudRepository repository)
{
    public async Task<bool> Handle(ReviewTransactionCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Notes) || command.Notes.Trim().Length < 5 || command.Notes.Length > 1000)
            throw new ArgumentException("Ghi chú phải có từ 5 đến 1000 ký tự.");
        if (await repository.GetAsync(command.Id, ct) == null)
            return false;
        await repository.ReviewAsync(new AnalystReview { TransactionId = command.Id, IsFraud = command.IsFraud, Notes = command.Notes.Trim(), Reviewer = command.Reviewer }, ct);
        return true;
    }
}
