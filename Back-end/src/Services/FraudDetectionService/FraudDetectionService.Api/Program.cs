using FraudDetectionService.Domain.Repositories;
using FraudDetectionService.Application.Features.Auth.Commands;
using Shared.Security.Permissions;
using FraudDetectionService.Application.Features.Transactions.Commands;
using FraudDetectionService.Application.Features.Transactions.Queries;
using FraudDetectionService.Application.Features.Alerts.Commands;
using FraudDetectionService.Application.Features.Rules.Commands;
using FraudDetectionService.Application.Features.Demo.Commands;
using FraudDetectionService.Application.Features.Ai.Queries;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using FraudDetectionService.Application;
using FraudDetectionService.Domain;
using FraudDetectionService.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 64 * 1024);
var dev = builder.Environment.IsDevelopment();
string Secret(string name, string fallback)
{
    var value = builder.Configuration[name];
    if (string.IsNullOrWhiteSpace(value))
    {
        if (!dev)
            throw new InvalidOperationException($"Missing configuration: {name}");
        value = fallback;
    }
    if (value.Length < 32)
        throw new InvalidOperationException($"{name} must contain at least 32 characters.");
    return value;
}
var jwtSecret = Secret("Security:JwtSecret", "local-development-jwt-secret-change-before-deploy-2026");
var piiSecret = Secret("Security:PiiSalt", "local-development-pii-salt-stable-demo-2026");
var aiKey = builder.Configuration["Ai:ApiKey"] ?? (dev ? "local-ai-service-key" : throw new InvalidOperationException("Missing Ai:ApiKey"));
if (string.IsNullOrWhiteSpace(aiKey)) throw new InvalidOperationException("Ai:ApiKey cannot be empty.");
Directory.CreateDirectory("data");
builder.Services.AddDbContext<FraudDbContext>(o => o.UseSqlite(builder.Configuration.GetConnectionString("FraudShield")));
builder.Services.AddScoped<IFraudRepository, FraudRepository>();
builder.Services.AddSingleton<IPiiHasher>(new PiiHasher(piiSecret));
builder.Services.AddSingleton<IPasswordVerifier, PasswordVerifier>();
builder.Services.AddScoped<LoginCommandHandler>();
builder.Services.AddSingleton<EvaluationGate>();
builder.Services.AddScoped<DetectionService>();
builder.Services.AddScoped<AnalyzeTransactionCommandHandler>();
builder.Services.AddScoped<GetTransactionsQueryHandler>();
builder.Services.AddScoped<ReviewTransactionCommandHandler>();
builder.Services.AddScoped<UpdateRulePolicyCommandHandler>();
builder.Services.AddScoped<RunDemoCommandHandler>();
builder.Services.AddScoped<CompareEvaluationQueryHandler>();
builder.Services.AddHttpClient<IAiClient, AiClient>(http => { http.BaseAddress = new Uri(builder.Configuration["Ai:BaseUrl"] ?? "http://127.0.0.1:8001/"); http.Timeout = TimeSpan.FromSeconds(Math.Clamp(builder.Configuration.GetValue("Ai:TimeoutSeconds", 3), 1, 30)); http.DefaultRequestHeaders.Add("X-Service-Key", aiKey); });
builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => o.TokenValidationParameters = new() { ValidateIssuer = true, ValidIssuer = "FraudShield", ValidateAudience = true, ValidAudience = "FraudShield.Web", ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(15), ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)) });
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy(FraudPermissions.ManageRules, p => p.RequireRole("Admin"));
    o.AddPolicy(FraudPermissions.ReadAudit, p => p.RequireRole("Admin"));
    o.AddPolicy(FraudPermissions.ReviewTransactions, p => p.RequireRole("Admin", "Analyst"));
});
builder.Services.AddRateLimiter(o => { o.RejectionStatusCode = 429; o.GlobalLimiter = PartitionedRateLimiter.Create<Microsoft.AspNetCore.Http.HttpContext, string>(ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new() { PermitLimit = 300, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })); o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new() { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })); });
builder.Services.AddSingleton<ITokenIssuer>(new TokenIssuer(jwtSecret));
var app = builder.Build();
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'";
    try
    {
        await next();
    }
    catch (Exception e) when (!ctx.Response.HasStarted && e is not OperationCanceledException)
    {
        var status = e is ConflictException or DbUpdateConcurrencyException ? 409 : e is ArgumentException or FluentValidation.ValidationException ? 400 : 500;
        if (status == 500)
            app.Logger.LogError(e, "Request failed {TraceId}", ctx.TraceIdentifier);
        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(new
        {
            error = status == 500 ? "Không thể hoàn tất yêu cầu." : e.Message,
            traceId = ctx.TraceIdentifier
        });
    }
});
app.UseRateLimiter();
var hasWebRoot = Directory.Exists(Path.Combine(app.Environment.ContentRootPath, "wwwroot"));
if (hasWebRoot) { app.UseDefaultFiles(); app.UseStaticFiles(); }
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", application = "FraudShield .NET", featureSchemaVersion = FeatureVector.SchemaVersion }));
app.MapFallback("/api/{**path}", () => Results.NotFound(new { error = "API endpoint không tồn tại." }));
if (hasWebRoot)
    app.MapFallbackToFile("index.html");
else
    app.MapGet("/", () => Results.Ok(new { name = "FraudShield API", health = "/health", frontend = "http://127.0.0.1:5173" }));
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FraudDbContext>();
    await db.Database.MigrateAsync();
    if (!await db.Policies.AnyAsync())
        db.Policies.Add(new());
    if (!await db.Users.AnyAsync())
    {
        var password = builder.Configuration["Security:AdminPassword"] ?? (dev ? "Admin@123456" : throw new InvalidOperationException("Set Security:AdminPassword for initial admin."));
        if (password.Length < 12)
            throw new InvalidOperationException("Admin password must contain at least 12 characters.");
        db.Users.Add(new()
        {
            Username = "admin",
            PasswordHash = Passwords.Hash(password),
            Role = "Admin"
        });
        if (dev)
            db.Users.Add(new()
            {
                Username = "analyst",
                PasswordHash = Passwords.Hash("Analyst@123456"),
                Role = "Analyst"
            });
    }
    await db.SaveChangesAsync();
}
app.Run();

public sealed class TokenIssuer(string secret) : ITokenIssuer
{
    public string Create(AppUser user) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("FraudShield", "FraudShield.Web", [new(ClaimTypes.Name, user.Username), new(ClaimTypes.Role, user.Role)], expires: DateTime.UtcNow.AddHours(1), signingCredentials: new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), SecurityAlgorithms.HmacSha256)));
}
public partial class Program;
