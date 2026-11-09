using FraudDetectionService.Application;
using FraudDetectionService.Domain.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Security.Permissions;

namespace FraudDetectionService.Api.Controllers;

[ApiController, Route("api/alerts"), Authorize(Policy = FraudPermissions.ReviewTransactions)]
public sealed class AlertsController(IAlertRepository store, IFraudRepository users, AlertWorkflowService workflow, TimeProvider clock) : ControllerBase
{
    private async Task<AlertActor> Actor(CancellationToken ct)
    {
        var user = await users.UserAsync(User.Identity?.Name ?? "", ct);
        if (user == null || user.Role is not ("Admin" or "Analyst")) throw new AlertException(403, "ALERT_FORBIDDEN", "Tài khoản không còn quyền xử lý cảnh báo.");
        return new(user.Id, user.Username, user.Role);
    }
    [HttpGet]
    public async Task<AlertPage> List([FromQuery] string? status, [FromQuery] string? priority, [FromQuery] string? owner, [FromQuery] bool? overdue, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int size = 20, CancellationToken ct = default)
        => await store.ListAsync(new(status, priority, owner, overdue, search, page, size), await Actor(ct), clock.GetUtcNow(), ct);
    [HttpGet("stats")]
    public async Task<AlertStats> Stats(CancellationToken ct) { await Actor(ct); return await store.StatsAsync(clock.GetUtcNow(), ct); }
    [HttpGet("users")]
    public async Task<List<AlertUser>> Users(CancellationToken ct)
    {
        var actor = await Actor(ct);
        var result = await store.UsersAsync(ct);
        return actor.IsAdmin ? result : result.Where(u => u.Id == actor.Id).ToList();
    }
    [HttpGet("{id:guid}")]
    public async Task<AlertDetail> Detail(Guid id, CancellationToken ct) => await workflow.DetailAsync(id, await Actor(ct), ct);
    [HttpGet("{id:guid}/timeline")]
    public async Task<AlertTimeline> Timeline(Guid id, [FromQuery] int page = 1, [FromQuery] int size = 20, CancellationToken ct = default)
    {
        await workflow.DetailAsync(id, await Actor(ct), ct);
        return await store.TimelineAsync(id, page, size, ct);
    }
    [HttpPost("{id:guid}/{command}")]
    public async Task<AlertMutationResult> Execute(Guid id, string command, AlertCommand request, CancellationToken ct)
        => await workflow.ExecuteAsync(id, command, request, await Actor(ct), ct);
}
