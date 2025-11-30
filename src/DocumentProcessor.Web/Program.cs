using DocumentProcessor.Web.Components;
using DocumentProcessor.Web.Data;
using DocumentProcessor.Web.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// Configure database connection
var dbInfo = new DatabaseInfoService();
string connectionString = string.Empty;
try
{
    var secretsService = new SecretsService();
    string secretJson;

    // First: Try to get secret with "target" in name (Postgres)
    try
    {
        secretJson = await secretsService.GetSecretAsync("atx-db-modernization-atx-db-modernization-1-target");
        if (!string.IsNullOrWhiteSpace(secretJson))
        {
            var username = secretsService.GetFieldFromSecret(secretJson, "username");
            var password = secretsService.GetFieldFromSecret(secretJson, "password");
            var host = secretsService.GetFieldFromSecret(secretJson, "host");
            var port = secretsService.GetFieldFromSecret(secretJson, "port");
            var dbname = "postgres";
            connectionString = $"Host={host};Port={port};Database={dbname};Username={username};Password={password};SSL Mode=Require;Trust Server Certificate=true";
            dbInfo.DatabaseType = "PostgreSQL"; dbInfo.SecretName = "atx-db-modernization-atx-db-modernization-1-target"; dbInfo.HostAddress = $"{host}:{port}";
        }
        else throw new Exception("Secret was empty");
    }
    catch
    {
        // Second: Try to get secret by description (SQL Server)
        secretJson = await secretsService.GetSecretByDescriptionPrefixAsync("Password for RDS MSSQL used for MAM319.");
        if (!string.IsNullOrWhiteSpace(secretJson))
        {
            var username = secretsService.GetFieldFromSecret(secretJson, "username");
            var password = secretsService.GetFieldFromSecret(secretJson, "password");
            var host = secretsService.GetFieldFromSecret(secretJson, "host");
            var port = secretsService.GetFieldFromSecret(secretJson, "port");
            var dbname = secretsService.GetFieldFromSecret(secretJson, "dbname");
            connectionString = $"Server={host},{port};Database={dbname};User Id={username};Password={password};TrustServerCertificate=true;Encrypt=true";
            dbInfo.DatabaseType = "SQL Server"; dbInfo.SecretName = "MAM319 RDS MSSQL"; dbInfo.HostAddress = $"{host}:{port}";
        }
        else throw new Exception("Failed to retrieve database credentials from Secrets Manager");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Warning: Could not load connection string from AWS Secrets Manager: {ex.Message}");
    Console.WriteLine("Falling back to appsettings.json connection string");
    connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Server=localhost;Database=DocumentProcessor;Integrated Security=true;TrustServerCertificate=True;";
    dbInfo.DatabaseType = "SQL Server (Local)"; dbInfo.SecretName = "appsettings.json"; dbInfo.HostAddress = "localhost";
}

builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString));
builder.Services.AddSingleton(dbInfo);
builder.Services.AddScoped<FileStorageService>();
builder.Services.AddScoped<AIService>();
builder.Services.AddScoped<DocumentProcessingService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    try
    {
        logger.LogInformation("Ensuring database is created...");
        var created = await db.Database.EnsureCreatedAsync();
        if (created)
        {
            logger.LogInformation("Database created successfully");
        }
        else
        {
            logger.LogInformation("Database already exists");
        }

        // Test connection by checking if Documents table exists
        var canConnect = await db.Database.CanConnectAsync();
        logger.LogInformation("Database connection test: {CanConnect}", canConnect);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to initialize database: {Message}", ex.Message);
        throw;
    }
}

if (app.Environment.IsDevelopment())
    app.UseDeveloperExceptionPage();
else
    app.UseExceptionHandler("/Error");

app.UseHttpsRedirection();
app.UseAntiforgery();
app.UseStaticFiles();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
