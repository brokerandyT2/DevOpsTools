using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.License.Server.Data;
using x3squaredcircles.License.Server.Models;

namespace x3squaredcircles.License.Server.Services
{
    /// <summary>
    // The core service for handling real-time license operations. It enforces
    // concurrency, manages bursting, and tracks active sessions.
    /// </summary>
    public interface ILicenseService
    {
        Task<LicenseAcquireResponse> AcquireLicenseAsync(LicenseAcquireRequest request);
        Task<LicenseHeartbeatResponse> HeartbeatAsync(LicenseHeartbeatRequest request);
        Task<LicenseReleaseResponse> ReleaseLicenseAsync(LicenseReleaseRequest request);
        Task<LicenseStatusResponse> GetLicenseStatusAsync();
        Task CleanupExpiredSessionsAsync();
        Task<AnnualUsageReport> GenerateAnnualUsageReportAsync();
    }

    public class LicenseService : ILicenseService
    {
        private readonly IDbContextFactory<LicenseDbContext> _contextFactory;
        private readonly ILicenseConfigService _configService;
        private readonly ILogger<LicenseService> _logger;
        private static readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(5);
        private static readonly byte[] HmacSecretKey = Encoding.UTF8.GetBytes("Th1s-Is-A-V3ry-S3cur3-And-Pr0pr13tary-S3cr3t-K3y!");


        public LicenseService(
            IDbContextFactory<LicenseDbContext> contextFactory,
            ILicenseConfigService configService,
            ILogger<LicenseService> logger)
        {
            _contextFactory = contextFactory;
            _configService = configService;
            _logger = logger;
        }

        public async Task<LicenseAcquireResponse> AcquireLicenseAsync(LicenseAcquireRequest request)
        {
            using var context = _contextFactory.CreateDbContext();

            // --- Step 1: Validate License Key Expiration ---
            var licenseKey = _configService.GetCurrentLicenseKey();
            var daysRemaining = (int)Math.Ceiling((licenseKey.ValidUntil - DateTime.UtcNow).TotalDays);

            // Enforce a 7-day grace period for renewal. After that, it's a hard stop.
            if (daysRemaining < -7)
            {
                _logger.LogCritical("LICENSE EXPIRED: Denying new session for {ToolName}. License expired on {ExpiryDate} and grace period has ended.",
                    request.ToolName, licenseKey.ValidUntil.ToString("O"));
                return new LicenseAcquireResponse
                {
                    LicenseGranted = false,
                    Reason = "license_expired_hard_lock"
                };
            }

            // --- Step 2: Validate Tool is Licensed ---
            if (!await _configService.IsToolLicensedAsync(request.ToolName))
            {
                _logger.LogWarning("License denied - tool not licensed: {ToolName}", request.ToolName);
                return new LicenseAcquireResponse { LicenseGranted = false, Reason = "tool_not_licensed" };
            }

            // --- Step 3: Perform Housekeeping and Get Current State ---
            await CleanupExpiredSessionsAsync(context);
            var config = await _configService.GetLicenseConfigAsync();
            var currentConcurrent = await context.ActiveSessions.CountAsync();
            var quarterlyUsage = await GetOrCreateCurrentQuarterlyUsageAsync(context, licenseKey);

            // --- Step 4: Check Base Concurrency Limit ---
            if (currentConcurrent < config.MaxConcurrent)
            {
                var sessionId = await CreateSessionAsync(request, context);
                _logger.LogInformation("License granted within base capacity for {ToolName}. Session: {SessionId}", request.ToolName, sessionId);
                return CreateSuccessResponse(sessionId, false, config, quarterlyUsage, daysRemaining);
            }

            // --- Step 5: Check Burst Capacity ---
            var burstLimit = config.MaxConcurrent * config.BurstMultiplier;
            bool canUseBurst = quarterlyUsage.BurstEventsUsed < config.BurstAllowancePerQuarter;

            if (currentConcurrent < burstLimit && canUseBurst)
            {
                quarterlyUsage.BurstEventsUsed++;
                await context.SaveChangesAsync();
                var sessionId = await CreateSessionAsync(request, context);

                _logger.LogWarning("LICENSE BURST ACTIVATED for {ToolName}. Burst events used this quarter: {Used}/{Allowed}. Session: {SessionId}",
                    request.ToolName, quarterlyUsage.BurstEventsUsed, config.BurstAllowancePerQuarter, sessionId);

                return CreateSuccessResponse(sessionId, true, config, quarterlyUsage, daysRemaining);
            }

            // --- Step 6: Deny License ---
            _logger.LogWarning("License denied - concurrent limit exceeded for {ToolName}. Current: {Current}, Max: {Max}",
                request.ToolName, currentConcurrent, config.MaxConcurrent);

            return new LicenseAcquireResponse { LicenseGranted = false, Reason = "concurrent_limit_exceeded" };
        }

        public async Task<LicenseHeartbeatResponse> HeartbeatAsync(LicenseHeartbeatRequest request)
        {
            using var context = _contextFactory.CreateDbContext();
            var session = await context.ActiveSessions.FirstOrDefaultAsync(s => s.SessionId == request.SessionId);

            if (session == null)
            {
                _logger.LogWarning("Heartbeat failed - session not found: {SessionId}", request.SessionId);
                return new LicenseHeartbeatResponse { SessionValid = false };
            }

            session.LastHeartbeat = DateTime.UtcNow;
            await context.SaveChangesAsync();
            return new LicenseHeartbeatResponse { SessionValid = true };
        }

        public async Task<LicenseReleaseResponse> ReleaseLicenseAsync(LicenseReleaseRequest request)
        {
            using var context = _contextFactory.CreateDbContext();
            var session = await context.ActiveSessions.FirstOrDefaultAsync(s => s.SessionId == request.SessionId);

            if (session == null)
            {
                return new LicenseReleaseResponse { SessionReleased = false };
            }

            context.ActiveSessions.Remove(session);
            await context.SaveChangesAsync();

            _logger.LogInformation("License released for session: {SessionId}", request.SessionId);
            return new LicenseReleaseResponse { SessionReleased = true };
        }

        public async Task<LicenseStatusResponse> GetLicenseStatusAsync()
        {
            using var context = _contextFactory.CreateDbContext();
            await CleanupExpiredSessionsAsync(context);

            var config = await _configService.GetLicenseConfigAsync();
            var licenseKey = _configService.GetCurrentLicenseKey();
            var currentConcurrent = await context.ActiveSessions.CountAsync();
            var quarterlyUsage = await GetOrCreateCurrentQuarterlyUsageAsync(context, licenseKey);
            var licensedTools = JsonSerializer.Deserialize<List<string>>(config.ToolsLicensed) ?? new List<string>();


            return new LicenseStatusResponse
            {
                IsLicenseValid = DateTime.UtcNow < licenseKey.ValidUntil,
                LicenseExpiresAt = licenseKey.ValidUntil,
                MaxConcurrent = config.MaxConcurrent,
                CurrentConcurrent = currentConcurrent,
                IsBurstAvailable = quarterlyUsage.BurstEventsUsed < config.BurstAllowancePerQuarter,
                BurstsUsedThisQuarter = quarterlyUsage.BurstEventsUsed,
                BurstsRemainingThisQuarter = Math.Max(0, config.BurstAllowancePerQuarter - quarterlyUsage.BurstEventsUsed),
                LicensedTools = licensedTools
            };
        }

        public async Task CleanupExpiredSessionsAsync()
        {
            using var context = _contextFactory.CreateDbContext();
            await CleanupExpiredSessionsAsync(context);
        }

        public async Task<AnnualUsageReport> GenerateAnnualUsageReportAsync()
        {
            using var context = _contextFactory.CreateDbContext();
            _logger.LogInformation("Generating annual usage report...");
            var licenseKey = _configService.GetCurrentLicenseKey();

            var periodStart = licenseKey.ValidFrom;
            var periodEnd = DateTime.UtcNow;

            // Get the historical data for the license period.
            var history = await context.DailyContributorHistories
                .Where(h => EF.Property<DateTime>(h, "Date") >= periodStart && EF.Property<DateTime>(h, "Date") <= periodEnd)
                .ToListAsync();

            var report = new AnnualUsageReport
            {
                CustomerId = licenseKey.CustomerId,
                ReportGeneratedAt = DateTime.UtcNow,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                // These are now calculated from our historical tables.
                PeakConcurrentUsage = 0, // This metric is no longer critical for billing.
                PeakContributorCount = history.Any() ? history.Max(h => h.ContributorCount) : 0,
                TotalBurstEventsUsed = await context.QuarterlyUsages.SumAsync(q => q.BurstEventsUsed)
            };

            // Sign the report to ensure its integrity.
            report.Signature = GenerateHmacSignature(report);
            _logger.LogInformation("Annual usage report generated and signed successfully.");
            return report;
        }


        private async Task CleanupExpiredSessionsAsync(LicenseDbContext context)
        {
            var cutoffTime = DateTime.UtcNow.Subtract(SessionTimeout);
            var expiredSessions = await context.ActiveSessions
                .Where(s => s.LastHeartbeat < cutoffTime)
                .ToListAsync();

            if (expiredSessions.Any())
            {
                _logger.LogWarning("Cleaning up {Count} expired/stale sessions.", expiredSessions.Count);
                context.ActiveSessions.RemoveRange(expiredSessions);
                await context.SaveChangesAsync();
            }
        }

        private async Task<string> CreateSessionAsync(LicenseAcquireRequest request, LicenseDbContext context)
        {
            var sessionId = Guid.NewGuid().ToString("N");
            var now = DateTime.UtcNow;

            context.ActiveSessions.Add(new ActiveSession
            {
                SessionId = sessionId,
                ToolName = request.ToolName,
                ToolVersion = request.ToolVersion,
                StartTime = now,
                LastHeartbeat = now,
                IpAddress = request.IpAddress,
                BuildId = request.BuildId
            });
            await context.SaveChangesAsync();
            return sessionId;
        }

        private async Task<QuarterlyUsage> GetOrCreateCurrentQuarterlyUsageAsync(LicenseDbContext context, LicenseKey licenseKey)
        {
            var now = DateTime.UtcNow;
            var daysSinceStart = Math.Max(0, (now - licenseKey.ValidFrom).TotalDays);
            var currentQuarterIndex = (int)Math.Floor(daysSinceStart / 92);
            var currentQuarterStartDate = licenseKey.ValidFrom.AddDays(currentQuarterIndex * 92);
            var quarterId = currentQuarterStartDate.ToString("yyyy-MM-dd");

            var usage = await context.QuarterlyUsages.FirstOrDefaultAsync(u => u.QuarterId == quarterId);

            if (usage == null)
            {
                _logger.LogInformation("New license quarter detected. Starting new usage period: {QuarterId}", quarterId);
                usage = new QuarterlyUsage
                {
                    QuarterId = quarterId,
                    BurstEventsUsed = 0,
                    QuarterStartDate = currentQuarterStartDate
                };
                context.QuarterlyUsages.Add(usage);
                await context.SaveChangesAsync();
            }
            return usage;
        }

        private LicenseAcquireResponse CreateSuccessResponse(string sessionId, bool isBurst, LicenseConfig config, QuarterlyUsage usage, int daysRemaining)
        {
            var response = new LicenseAcquireResponse
            {
                LicenseGranted = true,
                SessionId = sessionId,
                IsBurstMode = isBurst,
                BurstsRemainingThisQuarter = Math.Max(0, config.BurstAllowancePerQuarter - usage.BurstEventsUsed),
            };

            // Add the "expiring soon" warning if we are within the 45-day nudge period.
            if (daysRemaining > 0 && daysRemaining <= 45)
            {
                response.Warning = new LicenseWarning
                {
                    Code = "LICENSE_EXPIRING_SOON",
                    Message = $"Your annual 3SC Platform license expires in {daysRemaining} days. Please have your administrator renew your license to avoid service interruptions.",
                    DaysRemaining = daysRemaining
                };
            }

            return response;
        }

        private static string GenerateHmacSignature(AnnualUsageReport report)
        {
            var canonicalPayload = new StringBuilder();
            canonicalPayload.Append(report.CustomerId);
            canonicalPayload.Append(report.ReportGeneratedAt.ToString("o"));
            canonicalPayload.Append(report.PeriodStart.ToString("o"));
            canonicalPayload.Append(report.PeriodEnd.ToString("o"));
            canonicalPayload.Append(report.PeakConcurrentUsage);
            canonicalPayload.Append(report.PeakContributorCount);
            canonicalPayload.Append(report.TotalBurstEventsUsed);

            using (var hmac = new HMACSHA256(HmacSecretKey))
            {
                var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonicalPayload.ToString()));
                return Convert.ToHexString(hash);
            }
        }
    }
}