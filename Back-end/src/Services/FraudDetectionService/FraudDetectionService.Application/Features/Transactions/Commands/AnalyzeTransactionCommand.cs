using FluentValidation;
namespace FraudDetectionService.Application.Features.Transactions.Commands;

public record AnalyzeTransactionCommand(TransactionRequest Transaction);
public sealed class AnalyzeTransactionCommandValidator : AbstractValidator<AnalyzeTransactionCommand>
{
    public AnalyzeTransactionCommandValidator()
    {
        RuleFor(c => c.Transaction.UserId).NotEmpty().MaximumLength(80);
        RuleFor(c => c.Transaction.Amount).GreaterThan(0).LessThanOrEqualTo(100_000_000_000);
        RuleFor(c => c.Transaction.Currency).Equal("VND");
        RuleFor(c => c.Transaction.Merchant).NotEmpty().MaximumLength(80);
        RuleFor(c => c.Transaction.Category).Must(x => new[] { "grocery", "electronics", "travel", "jewelry", "crypto", "gambling", "other" }.Contains(x));
        RuleFor(c => c.Transaction.DeviceId).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Transaction.Country).Matches("^[A-Z]{2}$");
        RuleFor(c => c.Transaction.IpAddress).Must(x => System.Net.IPAddress.TryParse(x, out _));
        RuleFor(c => c.Transaction.Mode).Must(x => x is "RulesOnly" or "Hybrid");
    }
}
public sealed class AnalyzeTransactionCommandHandler(DetectionService detection)
{
    public async Task<EvaluationView> Handle(AnalyzeTransactionCommand command, CancellationToken ct)
    {
        await new AnalyzeTransactionCommandValidator().ValidateAndThrowAsync(command, ct);
        return await detection.AnalyzeAsync(command.Transaction, ct);
    }
}
