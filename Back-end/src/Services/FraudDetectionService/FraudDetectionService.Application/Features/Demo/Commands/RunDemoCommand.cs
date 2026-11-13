using FluentValidation;

namespace FraudDetectionService.Application.Features.Demo.Commands;

public record RunDemoCommand(string Mode, int? OwnerUserId, AlertActor Actor);
public record DemoResult(string Run, int Count, IReadOnlyList<EvaluationView> Results);
public sealed class RunDemoCommandValidator : AbstractValidator<RunDemoCommand>
{
    public RunDemoCommandValidator()
    {
        RuleFor(x => x.Mode).Must(mode => mode is "RulesOnly" or "Hybrid").WithMessage("Chế độ không hợp lệ.");
        RuleFor(x => x.OwnerUserId).NotNull().GreaterThan(0).WithMessage("Hãy chọn analyst phụ trách.");
    }
}

public sealed class RunDemoCommandHandler(DetectionService detection, IAlertRepository alerts, AlertWorkflowService workflow, TimeProvider clock)
{
    public async Task<DemoResult> Handle(RunDemoCommand command, CancellationToken ct)
    {
        await new RunDemoCommandValidator().ValidateAndThrowAsync(command, ct);
        if (!command.Actor.IsAdmin && (command.Actor.Role != "Analyst" || command.OwnerUserId != command.Actor.Id))
            throw new AlertException(403, "ALERT_FORBIDDEN", "Chỉ Admin được phân công demo cho analyst khác.");
        var users = await alerts.UsersAsync(ct);
        if (!users.Any(u => u.Id == command.OwnerUserId && u.Role == "Analyst"))
            throw new AlertException(400, "INVALID_DEMO_OWNER", "Analyst phụ trách không hợp lệ.");
        var run = Guid.NewGuid().ToString("N")[..6];
        // Fix UTC hour to 12 on the previous day to isolate the demo rules.
        var start = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-1).AddHours(12), TimeSpan.Zero);
        var results = new List<EvaluationView>();
        var i = 0;
        async Task Add(string user, decimal amount, string device, string ip)
        {
            var result = await detection.AnalyzeAsync(new(null, $"DEMO-{run}-{user}", amount, "VND", "Demo Store", "grocery", "VN", $"{run}-{device}", ip, start.AddSeconds(i++ * 10), command.Mode), ct);
            if (result.Decision != "ALLOW")
            {
                var page = await alerts.ListAsync(new(Search: result.Id.ToString()), command.Actor, clock.GetUtcNow(), ct);
                var alert = page.Items.Single();
                await workflow.ExecuteAsync(alert.Id, command.Actor.IsAdmin ? "assign" : "claim", new()
                {
                    RequestId = Guid.NewGuid(), ExpectedVersion = alert.Version,
                    OwnerUserId = command.Actor.IsAdmin ? command.OwnerUserId : null,
                    Reason = command.Actor.IsAdmin ? "Phân công khi tạo dữ liệu demo." : null
                }, command.Actor, ct);
            }
            results.Add(result);
        }
        await Add("NORMAL", 150000, "normal", "192.0.2.2");
        await Add("HIGH", 150000000, "high", "192.0.2.3");
        await Add("CRITICAL", 300000000, "critical", "192.0.2.4");
        for (var n = 0; n < 11; n++)
            await Add("VELOCITY", 100000 + n % 3 * 20000, "velocity", "192.0.2.5");
        for (var n = 0; n < 4; n++)
            await Add($"RING-{n}", 100000, "shared", $"192.0.2.{20 + n}");
        return new(run, results.Count, results);
    }
}
