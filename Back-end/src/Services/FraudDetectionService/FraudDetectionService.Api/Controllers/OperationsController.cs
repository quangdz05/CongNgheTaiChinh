using FraudDetectionService.Application;
using Shared.Security.Permissions;
using FraudDetectionService.Application.Features.Rules.Commands;
using FraudDetectionService.Application.Features.Demo.Commands;
using FraudDetectionService.Application.Features.Ai.Queries;
using FraudDetectionService.Domain;
using FraudDetectionService.Domain.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace FraudDetectionService.Api.Controllers;

[ApiController, Route("api"), Authorize]
public sealed class OperationsController(IFraudRepository repository, IAiClient ai, UpdateRulePolicyCommandHandler updatePolicy, RunDemoCommandHandler demo, CompareEvaluationQueryHandler compare) : ControllerBase
{
    [HttpGet("dashboard")] public Task<object> Dashboard(CancellationToken ct) => repository.StatsAsync(ct);
    [HttpGet("rules")] public Task<RulePolicy> Rules(CancellationToken ct) => repository.PolicyAsync(ct);
    [HttpPut("rules"), Authorize(Policy = FraudPermissions.ManageRules)]
    public Task<RulePolicy> Update(RulePolicy policy, CancellationToken ct) => updatePolicy.Handle(new(policy, User.Identity!.Name!), ct);
    [HttpGet("audit"), Authorize(Policy = FraudPermissions.ReadAudit)] public Task<List<AuditEntry>> Audit(CancellationToken ct) => repository.AuditAsync(ct);
    [HttpGet("ai/status")] public async Task<object> AiStatus(CancellationToken ct) => new { status = await ai.GetStatusAsync(ct), featureSchemaVersion = FeatureVector.SchemaVersion };
    [HttpGet("ai/metrics")]
    public async Task<IActionResult> AiMetrics(CancellationToken ct)
    {
        var json = await ai.GetMetricsAsync(ct);
        return json == null ? StatusCode(503, new
        {
            error = "Chưa có báo cáo mô hình hoặc AI API chưa sẵn sàng."
        }) : Content(json, "application/json");
    }
    [HttpPost("demo"), Authorize(Roles = "Admin,Analyst")]
    public Task<DemoResult> Demo(DemoRequest request, CancellationToken ct) => demo.Handle(new(request.Mode), ct);
    [HttpPost("compare/{id:guid}")]
    public async Task<IActionResult> Compare(Guid id, CancellationToken ct)
    {
        var result = await compare.Handle(new(id), ct);
        return result == null ? NotFound() : Ok(result);
    }
}
public record DemoRequest(string Mode = "RulesOnly");
