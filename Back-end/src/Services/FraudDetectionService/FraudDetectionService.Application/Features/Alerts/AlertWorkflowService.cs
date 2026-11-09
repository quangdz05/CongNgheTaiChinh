using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentValidation;
using FraudDetectionService.Domain;

namespace FraudDetectionService.Application;

public sealed class AlertCommandValidator : AbstractValidator<AlertCommand>
{
    public AlertCommandValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.ExpectedVersion).GreaterThan(0);
    }
}

public sealed class AlertWorkflowService(IAlertRepository store, TimeProvider clock)
{
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, DetectionService.Json);
    private static AlertException Invalid(string message) => new(400, "INVALID_ALERT_COMMAND", message);
    private static AlertException Conflict() => new(409, "ALERT_CONFLICT", "Cảnh báo đã thay đổi hoặc trạng thái không cho phép. Hãy tải lại.");
    private static void Require(bool allowed) { if (!allowed) throw new AlertException(403, "ALERT_FORBIDDEN", "Bạn không có quyền thực hiện thao tác này."); }
    private static string Text(string? value)
    {
        var text = value?.Trim() ?? "";
        if (text.Length is < 5 or > 1000) throw Invalid("Lý do/ghi chú phải có từ 5 đến 1000 ký tự.");
        return text;
    }
    public static List<string> Allowed(Alert a, AlertActor actor)
    {
        var result = new List<string>();
        if (a.Status == "New") result.Add("claim");
        if (actor.IsAdmin)
        {
            if (a.Status != "Closed") result.AddRange(["assign", "priority"]);
            if (a.Status == "Assigned") result.Add("release");
            if (a.Status == "Closed") result.Add("reopen");
        }
        if (actor.Id == a.OwnerUserId)
        {
            if (a.Status == "Assigned") result.Add("start");
            if (a.Status == "InProgress") result.Add("wait");
            if (a.Status == "Waiting") result.Add("resume");
            if (a.Status is "InProgress" or "Waiting") result.Add("close");
            if (a.Status != "Closed") result.Add("notes");
        }
        if (a.Status is not ("Closed" or "Escalated") && (actor.IsAdmin || actor.Id == a.OwnerUserId)) result.Add("escalate");
        return result;
    }
    public async Task<AlertDetail> DetailAsync(Guid id, AlertActor actor, CancellationToken ct)
    {
        var a = await store.FindAsync(id, ct) ?? throw new AlertException(404, "ALERT_NOT_FOUND", "Không tìm thấy cảnh báo.");
        var owner = a.OwnerUserId.HasValue ? (await store.UsersAsync(ct)).Find(u => u.Id == a.OwnerUserId)?.Username : null;
        return new(a, owner, Allowed(a, actor), clock.GetUtcNow());
    }
    public async Task<AlertMutationResult> ExecuteAsync(Guid id, string command, AlertCommand request, AlertActor actor, CancellationToken ct)
    {
        await new AlertCommandValidator().ValidateAndThrowAsync(request, ct);
        ValidateFields(command, request);
        if (actor.Role is not ("Admin" or "Analyst")) Require(false);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(request))));
        var old = await store.ReceiptAsync(id, request.RequestId, ct);
        if (old != null) return Replay(old, actor, command, hash);
        var a = await store.FindAsync(id, ct) ?? throw new AlertException(404, "ALERT_NOT_FOUND", "Không tìm thấy cảnh báo.");
        // Check ownership before version to avoid exposing another user's mutation details.
        var administrative = command is "assign" or "release" or "priority" or "reopen";
        Require(administrative ? actor.IsAdmin : command == "claim" || (command == "escalate" && actor.IsAdmin) || a.OwnerUserId == actor.Id);
        if (a.Version != request.ExpectedVersion) throw Conflict();
        if (!Allowed(a, actor).Contains(command)) throw Conflict();
        var now = clock.GetUtcNow();
        var before = Serialize(a);
        var reason = "";
        var eventType = "";
        var previousStatus = a.Status;
        switch (command)
        {
            case "claim":
                if (request.OwnerUserId != null) throw Invalid("Nhận việc chỉ áp dụng cho chính tài khoản hiện tại.");
                a.OwnerUserId = actor.Id; a.Status = "Assigned"; eventType = "Claimed"; break;
            case "assign":
            case "reopen":
                reason = Text(request.Reason);
                var users = await store.UsersAsync(ct);
                if (!request.OwnerUserId.HasValue || !users.Any(u => u.Id == request.OwnerUserId)) throw Invalid("Người phụ trách không hợp lệ.");
                if (command == "assign" && a.Status != "Escalated" && a.OwnerUserId == request.OwnerUserId) throw Invalid("Hãy chọn người phụ trách khác.");
                a.OwnerUserId = request.OwnerUserId; a.Status = "Assigned";
                eventType = command == "reopen" ? "Reopened" : "Reassigned";
                if (previousStatus is "Escalated" or "Closed")
                {
                    a.HandlingEpisode++;
                    a.EscalationEligibleAt = now.AddMinutes(5);
                }
                if (command == "reopen")
                {
                    a.Resolution = "Unknown"; a.ClosureReasonCode = null; a.ClosureNotes = null; a.ClosedAt = null; a.ClosedByUserId = null; a.EvidenceJson = "[]";
                    a.SlaStartedAt = now; a.BudgetMinutes = AlertPolicy.Budget(a.Priority); a.DueAt = now.AddMinutes(a.BudgetMinutes); a.EscalationEligibleAt = now;
                }
                break;
            case "start": a.Status = "InProgress"; eventType = "Started"; break;
            case "release": reason = Text(request.Reason); a.Status = "New"; a.OwnerUserId = null; eventType = "Released"; break;
            case "wait":
                reason = Text(request.Reason);
                if (request.WaitingType is not ("InternalReview" or "ExternalInformation" or "Other") || request.WaitingUntil is not { } until || until <= now || until > now.AddDays(7)) throw Invalid("Loại chờ/hạn chờ không hợp lệ (tối đa 7 ngày).");
                a.Status = "Waiting"; a.WaitingType = request.WaitingType; a.WaitingReason = reason; a.WaitingUntil = until; eventType = "WaitingEntered"; break;
            case "resume": a.Status = "InProgress"; eventType = "Resumed"; break;
            case "priority":
                reason = Text(request.Reason);
                if (!AlertPolicy.Priorities.Contains(request.Priority)) throw Invalid("Mức ưu tiên không hợp lệ.");
                if (request.Priority == a.Priority) throw Invalid("Mức ưu tiên chưa thay đổi.");
                a.Priority = request.Priority!; a.PriorityReason = reason; a.BudgetMinutes = AlertPolicy.Budget(a.Priority);
                var candidate = a.SlaStartedAt.AddMinutes(a.BudgetMinutes);
                if (candidate < a.DueAt) a.DueAt = candidate;
                eventType = "PriorityChanged"; break;
            case "escalate":
                reason = Text(request.Reason); a.Status = "Escalated"; a.EscalatedAt = now; a.EscalationReason = "Manual"; eventType = "Escalated"; break;
            case "close":
                reason = Text(request.Notes);
                var expectedResolution = request.ClosureReasonCode switch { "FraudConfirmed" => "Fraud", "LegitimateConfirmed" => "Legitimate", "InsufficientEvidence" => "Unknown", "ConflictingEvidence" => "Disputed", _ => null };
                if (expectedResolution == null || request.Resolution != expectedResolution) throw Invalid("Lý do đóng và kết luận không khớp.");
                var evidence = request.Evidence ?? [];
                if (evidence.Count > 10 || (expectedResolution is "Fraud" or "Legitimate" && evidence.Count == 0)) throw Invalid("Kết luận thật/giả cần bằng chứng tham chiếu, tối đa 10 mục.");
                foreach (var item in evidence)
                    if (item is null || item.SourceType is not ("ManualInvestigation" or "InternalRecord" or "Other") || string.IsNullOrWhiteSpace(item.Reference) || item.Reference.Trim().Length > 300 || string.IsNullOrWhiteSpace(item.Description) || item.Description.Trim().Length is < 5 or > 1000 || item.CapturedAt > now || item.CapturedAt == default || item.CreatorUserId != null)
                        throw Invalid("Bằng chứng tham chiếu không hợp lệ; người tạo do server ghi.");
                a.EvidenceJson = Serialize(evidence.Select(e => e with { Reference = e.Reference.Trim(), Description = e.Description.Trim(), CreatorUserId = actor.Id }));
                a.Status = "Closed"; a.Resolution = expectedResolution; a.ClosureReasonCode = request.ClosureReasonCode; a.ClosureNotes = reason; a.ClosedAt = now; a.ClosedByUserId = actor.Id; eventType = "Closed"; break;
            case "notes": reason = Text(request.Notes); eventType = "NoteAdded"; break;
            default: throw Invalid("Lệnh không được hỗ trợ.");
        }
        if (a.Status != "Waiting") { a.WaitingType = null; a.WaitingReason = null; a.WaitingUntil = null; }
        if (a.Status != "Escalated") { a.EscalatedAt = null; a.EscalationReason = null; }
        a.Version++; a.UpdatedAt = now;
        var snapshot = Serialize(a);
        var entry = Event(a, eventType, now, before, reason, actor.Id, request.RequestId);
        var receipt = new AlertCommandReceipt { AlertId = a.Id, RequestId = request.RequestId, ActorUserId = actor.Id, Command = command, PayloadHash = hash, ResultJson = snapshot, Version = a.Version, At = now };
        try { await store.SaveAsync(a, entry, receipt, ct); }
        catch (AlertException e) when (e.Status == 409)
        {
            var committed = await store.ReceiptAsync(id, request.RequestId, ct);
            if (committed != null) return Replay(committed, actor, command, hash);
            throw;
        }
        return new(a, a.Version, false);
    }
    private static AlertMutationResult Replay(AlertCommandReceipt receipt, AlertActor actor, string command, string hash)
    {
        if (receipt.ActorUserId != actor.Id || receipt.Command != command || receipt.PayloadHash != hash) throw new AlertException(409, "REQUEST_ID_CONFLICT", "requestId đã được dùng với nội dung/tài khoản khác.");
        return new(JsonSerializer.Deserialize<Alert>(receipt.ResultJson, DetectionService.Json)!, receipt.Version, true);
    }
    private static void ValidateFields(string command, AlertCommand r)
    {
        if (command is not ("claim" or "start" or "wait" or "resume" or "assign" or "release" or "priority" or "escalate" or "close" or "reopen" or "notes")) throw Invalid("Lệnh không được hỗ trợ.");
        if ((r.OwnerUserId != null && command is not ("assign" or "reopen")) ||
            (r.Priority != null && command != "priority") ||
            ((r.WaitingType != null || r.WaitingUntil != null) && command != "wait") ||
            ((r.Resolution != null || r.ClosureReasonCode != null || r.Evidence != null) && command != "close") ||
            (r.Notes != null && command is not ("close" or "notes")) ||
            (r.Reason != null && command is not ("assign" or "release" or "priority" or "escalate" or "reopen" or "wait")))
            throw Invalid("Lệnh chứa trường không được hỗ trợ.");
    }
    public static AlertEvent Event(Alert a, string type, DateTimeOffset now, string before = "null", string reason = "", int? actor = null, Guid? requestId = null) => new()
    {
        AlertId = a.Id, Type = type, ActorUserId = actor, At = now, BeforeVersion = a.Version - 1, Version = a.Version, HandlingEpisode = a.HandlingEpisode,
        Reason = reason, RequestId = requestId, BeforeJson = before, AfterJson = Serialize(a)
    };
    public async Task EscalateDueAsync(CancellationToken ct)
    {
        foreach (var id in await store.DueAsync(clock.GetUtcNow(), ct))
        {
            var a = await store.FindAsync(id, ct);
            var now = clock.GetUtcNow();
            if (a == null || a.Status is "Closed" or "Escalated" || now < a.EscalationEligibleAt || (now < a.DueAt && !(a.Status == "Waiting" && a.WaitingUntil <= now))) continue;
            var before = Serialize(a);
            var waitingExpired = a.Status == "Waiting" && a.WaitingUntil <= now;
            a.Status = "Escalated"; a.EscalatedAt = now; a.EscalationReason = now >= a.DueAt ? "SlaBreached" : "WaitingExpired";
            a.WaitingUntil = null; a.WaitingReason = null; a.WaitingType = null; a.Version++; a.UpdatedAt = now;
            try { await store.SaveAsync(a, Event(a, "Escalated", now, before, a.EscalationReason + (waitingExpired ? ";WaitingExpired" : "")), null, ct); }
            catch (AlertException e) when (e.Status == 409) { /* Another command committed first; re-evaluate next scan. */ }
        }
    }
}
