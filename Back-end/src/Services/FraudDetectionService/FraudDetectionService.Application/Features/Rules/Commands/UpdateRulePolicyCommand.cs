using FluentValidation;
using FraudDetectionService.Domain;
using FraudDetectionService.Domain.Repositories;
namespace FraudDetectionService.Application.Features.Rules.Commands;

public record UpdateRulePolicyCommand(RulePolicy Policy, string Actor);
public sealed class UpdateRulePolicyCommandValidator : AbstractValidator<UpdateRulePolicyCommand>
{
    public UpdateRulePolicyCommandValidator()
    {
        RuleFor(c => c.Policy.HighAmount).GreaterThan(0);
        RuleFor(c => c.Policy.CriticalAmount).GreaterThan(c => c.Policy.HighAmount).LessThanOrEqualTo(100_000_000_000);
        RuleFor(c => c.Policy.VelocityCount).InclusiveBetween(2, 1000);
        RuleFor(c => c.Policy.SharedDeviceUsers).InclusiveBetween(2, 1000);
        RuleFor(c => c.Policy.ReviewThreshold).Must(double.IsFinite).GreaterThan(0).LessThan(c => c.Policy.BlockThreshold);
        RuleFor(c => c.Policy.BlockThreshold).Must(double.IsFinite).LessThanOrEqualTo(1);
        RuleFor(c => c.Policy.MlWeight).Must(double.IsFinite).InclusiveBetween(0, 1);
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
