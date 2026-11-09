using System.Text.Json.Serialization;
using FraudDetectionService.Domain;

namespace FraudDetectionService.Application;

public sealed class AlertException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public record AlertActor(int Id, string Username, string Role)
{
    public bool IsAdmin => Role == "Admin";
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record AlertEvidence(string SourceType, string Reference, string Description, DateTimeOffset CapturedAt, int? CreatorUserId = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AlertCommand
{
    public Guid RequestId { get; init; }
    public long ExpectedVersion { get; init; }
    public int? OwnerUserId { get; init; }
    public string? Reason { get; init; }
    public string? Priority { get; init; }
    public string? WaitingType { get; init; }
    public DateTimeOffset? WaitingUntil { get; init; }
    public string? Resolution { get; init; }
    public string? ClosureReasonCode { get; init; }
    public string? Notes { get; init; }
    public List<AlertEvidence>? Evidence { get; init; }
}

public record AlertFilter(string? Status = null, string? Priority = null, string? Owner = null, bool? Overdue = null, string? Search = null, int Page = 1, int Size = 20);
public record AlertRow(Guid Id, Guid TransactionId, string Status, string Priority, int? OwnerUserId, string? OwnerName, string Resolution,
    DateTimeOffset CreatedAt, DateTimeOffset DueAt, long Version, string UserId, string Merchant, decimal Amount, string Currency, string Decision);
public record AlertPage(List<AlertRow> Items, int Total, int Page, int Size, DateTimeOffset ServerNow);
public record AlertUser(int Id, string Username, string Role);
public record AlertTimeline(List<AlertEvent> Items, int Total, int Page, int Size);
public record AlertDetail(Alert Alert, string? OwnerName, List<string> AllowedActions, DateTimeOffset ServerNow);
public record AlertMutationResult(Alert Alert, long AppliedVersion, bool Replayed);
public record AlertStats(int OpenAlerts, int Overdue, int ClosedUnknown, int ClosedDisputed, Dictionary<string, int> States, Dictionary<string, int> Priorities, Dictionary<string, int> Owners, DateTimeOffset AsOf);

public interface IAlertRepository
{
    Task<Alert?> FindAsync(Guid id, CancellationToken ct);
    Task<AlertCommandReceipt?> ReceiptAsync(Guid id, Guid requestId, CancellationToken ct);
    Task SaveAsync(Alert alert, AlertEvent entry, AlertCommandReceipt? receipt, CancellationToken ct);
    Task<AlertPage> ListAsync(AlertFilter filter, AlertActor actor, DateTimeOffset now, CancellationToken ct);
    Task<AlertStats> StatsAsync(DateTimeOffset now, CancellationToken ct);
    Task<List<AlertUser>> UsersAsync(CancellationToken ct);
    Task<AlertTimeline> TimelineAsync(Guid id, int page, int size, CancellationToken ct);
    Task<List<Guid>> DueAsync(DateTimeOffset now, CancellationToken ct);
}
