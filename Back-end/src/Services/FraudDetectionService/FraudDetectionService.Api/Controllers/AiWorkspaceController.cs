using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FraudDetectionService.Api.Controllers;

[ApiController, Route("api/ai/workspace"), Authorize(Roles = "Admin,Analyst")]
public sealed class AiWorkspaceController(IHttpClientFactory clients) : ControllerBase
{
    [HttpGet("hybrid-model")]
    public Task<IActionResult> HybridModel(CancellationToken ct) => Forward("hybrid-model", HttpMethod.Get, ct);
    [HttpPost("hybrid-model"), Authorize(Roles = "Admin")]
    public Task<IActionResult> SelectHybridModel(CancellationToken ct) => Forward("hybrid-model", HttpMethod.Post, ct);
    [HttpGet("ulb")] public Task<IActionResult> Ulb(CancellationToken ct) => Forward("ulb", HttpMethod.Get, ct);
    [HttpGet("ulb/samples")] public Task<IActionResult> Samples(CancellationToken ct) => Forward("ulb/samples", HttpMethod.Get, ct);
    [HttpPost("ulb/predict")] public Task<IActionResult> Predict(CancellationToken ct) => Forward("ulb/predict", HttpMethod.Post, ct);
    [HttpGet("datasets")] public Task<IActionResult> List(CancellationToken ct) => Forward("datasets", HttpMethod.Get, ct);
    [HttpPost("datasets")] public Task<IActionResult> Create(CancellationToken ct) => Forward("datasets", HttpMethod.Post, ct);
    [HttpGet("datasets/{id}")]
    public Task<IActionResult> Detail(string id, CancellationToken ct) => Dataset(id, null, HttpMethod.Get, ct);
    [HttpGet("datasets/{id}/csv")]
    public Task<IActionResult> Csv(string id, CancellationToken ct) => Dataset(id, "csv", HttpMethod.Get, ct);
    [HttpDelete("datasets/{id}")]
    public Task<IActionResult> Delete(string id, CancellationToken ct) => Dataset(id, null, HttpMethod.Delete, ct);
    [HttpPost("datasets/{id}/{command}")]
    public Task<IActionResult> Execute(string id, string command, CancellationToken ct) =>
        command is "rows" or "generate" or "train" or "predict" ? Dataset(id, command, HttpMethod.Post, ct) : Task.FromResult<IActionResult>(NotFound());

    private Task<IActionResult> Dataset(string id, string? command, HttpMethod method, CancellationToken ct)
    {
        if (id.Length != 32 || !id.All(c => char.IsAsciiHexDigit(c)) || id != id.ToLowerInvariant())
            return Task.FromResult<IActionResult>(NotFound());
        return Forward($"datasets/{id}" + (command == null ? "" : $"/{command}"), method, ct);
    }

    private async Task<IActionResult> Forward(string path, HttpMethod method, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, "v1/workspace/" + path);
        request.Headers.Add("X-Actor", User.Identity!.Name!);
        request.Headers.Add("X-Actor-Role", User.IsInRole("Admin") ? "Admin" : "Analyst");
        if (method == HttpMethod.Post)
        {
            request.Content = new StreamContent(Request.Body);
            request.Content.Headers.ContentType = new("application/json");
        }
        try
        {
            using var response = await clients.CreateClient("AiWorkspace").SendAsync(request, ct);
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                string error = "Dịch vụ AI chưa sẵn sàng. Hãy thử lại.";
                try
                {
                    using var json = JsonDocument.Parse(bytes);
                    if (json.RootElement.TryGetProperty("detail", out var detail))
                        error = detail.ValueKind == JsonValueKind.String ? detail.GetString()! : "Dữ liệu nhập chưa hợp lệ. Kiểm tra các trường bắt buộc.";
                }
                catch (JsonException) { }
                return StatusCode((int)response.StatusCode, new { error });
            }
            return path.EndsWith("/csv", StringComparison.Ordinal)
                ? File(bytes, "text/csv; charset=utf-8", "fraudshield-dataset.csv")
                : Content(System.Text.Encoding.UTF8.GetString(bytes), "application/json");
        }
        catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException && !ct.IsCancellationRequested)
        {
            return StatusCode((int)HttpStatusCode.ServiceUnavailable, new { error = "Dịch vụ AI chưa kết nối được. Hãy thử lại sau." });
        }
    }
}
