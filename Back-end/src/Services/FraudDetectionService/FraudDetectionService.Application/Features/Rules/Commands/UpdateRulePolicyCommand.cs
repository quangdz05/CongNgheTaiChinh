using FluentValidation;
using FraudDetectionService.Domain;
using FraudDetectionService.Domain.Repositories;
namespace FraudDetectionService.Application.Features.Rules.Commands;

public record UpdateRulePolicyCommand(RulePolicy Policy, string Actor);
public sealed class UpdateRulePolicyCommandValidator : AbstractValidator<UpdateRulePolicyCommand>
{
    public UpdateRulePolicyCommandValidator()
    {
        RuleFor(c => c.Policy.AnalysisStrategy).Equal("RiskSignals")
            .WithMessage("Chấm điểm cũ chỉ dùng để đọc lịch sử. Hãy chọn phân nhóm dấu hiệu và bằng chứng cho chính sách mới.");
        RuleFor(c => c.Policy.BurstWindowSeconds).InclusiveBetween(10, 3600);
        RuleFor(c => c.Policy.BurstCount).InclusiveBetween(2, 1000);
        RuleFor(c => c.Policy.MinimumHistory).InclusiveBetween(5, 1000);
        RuleFor(c => c.Policy.AmountDeviationThreshold).Must(double.IsFinite).InclusiveBetween(1, 10);
        RuleFor(c => c.Policy.SharedIpUsers).InclusiveBetween(2, 1000);
        RuleFor(c => c.Policy.HighAmount).GreaterThan(0);
        RuleFor(c => c.Policy.CriticalAmount).GreaterThan(c => c.Policy.HighAmount).LessThanOrEqualTo(100_000_000_000).When(c => c.Policy.AnalysisStrategy == "LegacyScore");
        RuleFor(c => c.Policy.VelocityCount).InclusiveBetween(2, 1000).When(c => c.Policy.AnalysisStrategy == "LegacyScore");
        RuleFor(c => c.Policy.SharedDeviceUsers).InclusiveBetween(2, 1000);
        RuleFor(c => c.Policy.ReviewThreshold).Must(double.IsFinite).GreaterThan(0).LessThanOrEqualTo(1);
        RuleFor(c => c.Policy.ReviewThreshold).LessThan(c => c.Policy.BlockThreshold).When(c => c.Policy.AnalysisStrategy == "LegacyScore");
        RuleFor(c => c.Policy.BlockThreshold).Must(double.IsFinite).LessThanOrEqualTo(1).When(c => c.Policy.AnalysisStrategy == "LegacyScore");
        RuleFor(c => c.Policy.MlWeight).Must(double.IsFinite).InclusiveBetween(0, 1).When(c => c.Policy.AnalysisStrategy == "LegacyScore");
    }
}
public sealed class UpdateRulePolicyCommandHandler(IFraudRepository repository)
{
    public async Task<RulePolicy> Handle(UpdateRulePolicyCommand command, CancellationToken ct)
    {
        await new UpdateRulePolicyCommandValidator().ValidateAndThrowAsync(command, ct);
        await repository.UpdatePolicyAsync(command.Policy, command.Actor, ct);
        return await repository.PolicyAsync(ct);
    }
}
