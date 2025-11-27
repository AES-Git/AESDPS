using DocumentProcessor.Web.Components;
using DocumentProcessor.Web.Data;
using DocumentProcessor.Web.Services;
using DocumentProcessor.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddServerSideBlazor().AddCircuitOptions(o => { if (builder.Environment.IsDevelopment()) o.DetailedErrors = true; });

string connectionString;
try
{
    var secretsService = new SecretsService();
    var secretJson = await secretsService.GetSecretByDescriptionPrefixAsync("Password for RDS MSSQL used for MAM319.");
    var username = secretsService.GetFieldFromSecret(secretJson, "username");
    var password = secretsService.GetFieldFromSecret(secretJson, "password");
    var host = secretsService.GetFieldFromSecret(secretJson, "host");
    var port = secretsService.GetFieldFromSecret(secretJson, "port");
    var dbname = secretsService.GetFieldFromSecret(secretJson, "dbname");
    connectionString = $"Server={host},{port};Database={dbname};User Id={username};Password={password};TrustServerCertificate=true;Encrypt=true";
    builder.Services.AddSingleton(secretsService);
}
catch (Exception ex)
{
    Console.WriteLine($"Warning: AWS Secrets failed: {ex.Message}");
    connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Server=localhost;Database=DocumentProcessor;Integrated Security=true;TrustServerCertificate=True;";
}

builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString));
builder.Services.AddScoped<DocumentRepository>();
builder.Services.AddScoped<FileStorageService>();
builder.Services.AddScoped<AIService>();
builder.Services.AddSingleton<DocumentProcessingService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DocumentProcessingService>());
builder.Services.AddHealthChecks();
builder.Services.AddLogging(l => { l.ClearProviders(); l.AddConsole(); l.AddDebug(); l.SetMinimumLevel(LogLevel.Information); });
builder.Services.AddRateLimiter(o => o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    RateLimitPartition.GetFixedWindowLimiter(ctx.User.Identity?.Name ?? ctx.Request.Headers.Host.ToString(),
    _ => new FixedWindowRateLimiterOptions { AutoReplenishment = true, PermitLimit = 100, QueueLimit = 20, Window = TimeSpan.FromMinutes(1) })));
builder.Services.AddResponseCompression(o => o.EnableForHttps = true);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
    var repo = scope.ServiceProvider.GetRequiredService<DocumentRepository>();
    var proc = scope.ServiceProvider.GetRequiredService<DocumentProcessingService>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var stuck = (await repo.GetByStatusAsync(DocumentStatus.Pending)).Concat(await repo.GetByStatusAsync(DocumentStatus.Queued)).ToList();
    if (stuck.Any())
    {
        logger.LogInformation("Re-queuing {Count} stuck documents", stuck.Count);
        foreach (var doc in stuck)
        {
            try { await proc.QueueDocumentForProcessingAsync(doc.Id); }
            catch (Exception ex) { logger.LogError(ex, "Re-queue failed for {Id}", doc.Id); }
        }
    }
}

if (app.Environment.IsDevelopment()) app.UseDeveloperExceptionPage();
else { app.UseExceptionHandler("/Error", createScopeForErrors: true); app.UseHsts(); }

app.UseHttpsRedirection();
app.UseResponseCompression();
app.UseRateLimiter();
app.UseAntiforgery();

var provider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
provider.Mappings[".styles.css"] = "text/css";
app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = provider });

string uploadsPath = Path.Combine(builder.Environment.ContentRootPath, "uploads");
if (!Directory.Exists(uploadsPath)) Directory.CreateDirectory(uploadsPath);
app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(uploadsPath), RequestPath = "/uploads" });

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = c => c.Tags.Contains("live") });

app.MapGet("/admin/cleanup-stuck-documents", async (IServiceProvider services) =>
{
    using var scope = services.CreateScope();
    var repo = scope.ServiceProvider.GetRequiredService<DocumentRepository>();
    var proc = scope.ServiceProvider.GetRequiredService<DocumentProcessingService>();
    var stuck = (await repo.GetByStatusAsync(DocumentStatus.Processing)).Where(d => d.ProcessingStartedAt.HasValue && d.ProcessingStartedAt.Value < DateTime.UtcNow.AddMinutes(-30)).ToList();
    foreach (var doc in stuck) await proc.QueueDocumentForProcessingAsync(doc.Id);
    return Results.Ok(new { message = "Cleanup complete", count = stuck.Count });
});

app.Run();