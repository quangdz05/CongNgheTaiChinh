using FraudDetectionService.Application;

namespace FraudDetectionService.Api.Services;

public sealed class AlertSlaWorker(IServiceScopeFactory scopes, ILogger<AlertSlaWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<AlertWorkflowService>().EscalateDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e) { logger.LogError(e, "Alert SLA scan failed; retrying next scan"); }
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }
}
