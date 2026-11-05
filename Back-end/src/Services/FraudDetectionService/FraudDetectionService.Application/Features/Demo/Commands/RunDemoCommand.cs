namespace FraudDetectionService.Application.Features.Demo.Commands;

public record RunDemoCommand(string Mode);
public record DemoResult(string Run, int Count, IReadOnlyList<EvaluationView> Results);
public sealed class RunDemoCommandHandler(DetectionService detection)
{
    public async Task<DemoResult> Handle(RunDemoCommand command, CancellationToken ct)
    {
        if (command.Mode is not ("RulesOnly" or "Hybrid"))
            throw new ArgumentException("Chế độ không hợp lệ.");
        var run = Guid.NewGuid().ToString("N")[..6];
        // Fix UTC hour to 12 on the previous day to isolate the demo rules.
        var start = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-1).AddHours(12), TimeSpan.Zero);
        var results = new List<EvaluationView>();
        var i = 0;
        async Task Add(string user, decimal amount, string device, string ip) => results.Add(await detection.AnalyzeAsync(new(null, $"DEMO-{run}-{user}", amount, "VND", "Demo Store", "grocery", "VN", $"{run}-{device}", ip, start.AddSeconds(i++ * 10), command.Mode), ct));
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
