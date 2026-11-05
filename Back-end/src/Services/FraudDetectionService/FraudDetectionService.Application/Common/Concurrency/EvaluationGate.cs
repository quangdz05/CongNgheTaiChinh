using FraudDetectionService.Domain.Repositories;
using System.Diagnostics;
using System.Text.Json;
using FraudDetectionService.Domain;
namespace FraudDetectionService.Application;

public sealed class EvaluationGate
{
    public SemaphoreSlim Semaphore { get; } = new(1, 1);
}
