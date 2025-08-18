using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using x3squaredcircles.License.Server.Data;
using x3squaredcircles.License.Server.Services;

// ========================================================================
// 3SC LICENSING CONTAINER - MAIN ENTRY POINT
// ========================================================================

var builder = WebApplication.CreateBuilder(args);

// --- 1. CONFIGURE SERVICES ---

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure the database connection for SQLite.
// The data source is the persistent volume mounted inside the container.
// We use a DbContextFactory for safe multi-threading access from singleton services.
builder.Services.AddDbContextFactory<LicenseDbContext>(options =>
    options.UseSqlite("Data Source=/data/license.db"));

// Register our custom application services.
builder.Services.AddSingleton<ILicenseConfigService, LicenseConfigService>();
builder.Services.AddScoped<ILicenseService, LicenseService>();

// The ContributorCountService is registered as a Hosted Service, so ASP.NET Core
// will automatically manage its start and stop lifecycle in the background.
builder.Services.AddHostedService<ContributorCountService>();

// Register the EncryptionValidationService if needed (assuming it's still part of the startup check)
// For simplicity, startup checks will be handled directly in the Program.cs file.

builder.Logging.AddConsole();


// --- 2. BUILD THE APPLICATION ---
var app = builder.Build();


// --- 3. CRITICAL STARTUP VALIDATION & INITIALIZATION ---

// We execute these critical tasks within a scope to safely access scoped services.
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();

    try
    {
        // A. Ensure the database schema is created and migrated.
        logger.LogInformation("Applying database migrations if necessary...");
        var dbContext = services.GetRequiredService<LicenseDbContext>();
        await dbContext.Database.MigrateAsync();
        logger.LogInformation("✓ Database schema is up to date.");

        // B. Initialize the license configuration from the embedded key.
        // This is a blocking call. If it fails, the application will not start.
        logger.LogInformation("Initializing and validating license configuration...");
        var configService = services.GetRequiredService<ILicenseConfigService>();
        await configService.InitializeLicenseConfigAsync();
        logger.LogInformation("✓ License configuration loaded and validated successfully.");
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "FATAL STARTUP ERROR: A critical error occurred during initialization. The container will now exit.");
        // Use a specific exit code to indicate a startup failure.
        Environment.ExitCode = -1;
        // Force the application to stop if initialization fails.
        // This is a security measure to prevent the container from running in an invalid state.
        return;
    }
}


// --- 4. CONFIGURE THE HTTP REQUEST PIPELINE ---

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();
app.MapControllers();

// Define simple, minimal API endpoints for health and metrics.
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTime.UtcNow.ToString("o"),
    version = "2.0.0"
}));

app.MapGet("/metrics", async (ILicenseService licenseService) =>
{
    var status = await licenseService.GetLicenseStatusAsync();
    return Results.Ok(new
    {
        concurrent_sessions = status.CurrentConcurrent,
        max_concurrent = status.MaxConcurrent,
        bursts_used_this_quarter = status.BurstsUsedThisQuarter,
        license_is_valid = status.IsLicenseValid,
        license_expires_at_utc = status.LicenseExpiresAt.ToString("o")
    });
});


// --- 5. RUN THE APPLICATION ---

app.Logger.LogInformation("======================================================");
app.Logger.LogInformation("      3SC Licensing Container Started Successfully");
app.Logger.LogInformation("======================================================");
app.Logger.LogInformation("-> Health Check Endpoint: GET /health");
app.Logger.LogInformation("-> Metrics Endpoint:      GET /metrics");
app.Logger.LogInformation("-> Core License API:      /License/*");
app.Logger.LogInformation("-> Daily contributor count service is running in the background.");

app.Run();