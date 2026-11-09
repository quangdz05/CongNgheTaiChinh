using FraudDetectionService.Domain.Repositories;
using Shared.Security.Permissions;
using FraudDetectionService.Application.Features.Transactions.Commands;
using FraudDetectionService.Application.Features.Transactions.Queries;
using FraudDetectionService.Application.Features.Alerts.Commands;
using System.ComponentModel.DataAnnotations;
using System.Text;
using FraudDetectionService.Application;
using FraudDetectionService.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace FraudDetectionService.Api.Controllers;

[ApiController, Route("api/transactions"), Authorize]
public sealed class TransactionsController(IFraudRepository store, DetectionService detection, AnalyzeTransactionCommandHandler analyze, GetTransactionsQueryHandler list) : ControllerBase
{
    [HttpPost, Authorize(Policy = FraudPermissions.ReviewTransactions)]
    public Task<EvaluationView> Analyze(TransactionRequest req, CancellationToken ct) => analyze.Handle(new(req), ct);
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? decision, [FromQuery] string? search, [FromQuery, Range(1, 100000)] int page = 1, [FromQuery, Range(1, 100)] int size = 20, CancellationToken ct = default)
    {
        return Ok(await list.Handle(new(decision, search, page, size), ct));
    }
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct)
    {
        var tx = await store.GetAsync(id, ct);
        return tx == null ? NotFound() : Ok(await detection.ViewAsync(tx, ct));
    }
    [HttpPost("{id:guid}/reviews"), Authorize(Policy = FraudPermissions.ReviewTransactions)]
    public IActionResult Review(Guid id) => Conflict(new { error = "Review cũ đã ngừng ghi. Hãy xử lý và đóng cảnh báo tại /api/alerts.", code = "LEGACY_REVIEW_DISABLED", traceId = HttpContext.TraceIdentifier, transactionId = id });
    [HttpGet("export")]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var rows = await store.ListAsync(null, null, 1, 10000, ct);
        static string Csv(string x) => "\"" + (x.StartsWith('=') || x.StartsWith('+') || x.StartsWith('-') || x.StartsWith('@') ? "'" : "") + x.Replace("\"", "\"\"") + "\"";
        var sb = new StringBuilder("id,userId,amount,currency,timestamp,decision,ruleScore,mlScore,aiStatus\n");
        foreach (var t in rows)
            sb.AppendLine(string.Join(',', t.Id, Csv(t.UserId), t.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture), t.Currency, t.Timestamp.ToString("O"), t.Evaluation.Decision, t.Evaluation.RuleScore.ToString(System.Globalization.CultureInfo.InvariantCulture), t.Evaluation.MlScore?.ToString(System.Globalization.CultureInfo.InvariantCulture), t.Evaluation.AiStatus));
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(), "text/csv", "fraudshield-transactions.csv");
    }
}
public record ReviewRequest(bool IsFraud, [Required, StringLength(1000, MinimumLength = 5)] string Notes);
