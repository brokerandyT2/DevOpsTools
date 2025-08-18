using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.License.Server.Models;
using x3squaredcircles.License.Server.Services;

namespace x3squaredcircles.License.Server.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class LicenseController : ControllerBase
    {
        private readonly ILicenseService _licenseService;
        private readonly ILogger<LicenseController> _logger;

        public LicenseController(ILicenseService licenseService, ILogger<LicenseController> logger)
        {
            _licenseService = licenseService;
            _logger = logger;
        }

        [HttpPost("acquire")]
        public async Task<ActionResult<LicenseAcquireResponse>> AcquireLicense([FromBody] LicenseAcquireRequest request)
        {
            try
            {
                if (!ModelState.IsValid || string.IsNullOrEmpty(request.ToolName) || string.IsNullOrEmpty(request.ContributorId))
                {
                    _logger.LogWarning("Invalid license acquire request received.");
                    return BadRequest(new { reason = "invalid_request_payload" });
                }

                request.IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                var response = await _licenseService.AcquireLicenseAsync(request);

                var logLevel = response.LicenseGranted ? LogLevel.Information : LogLevel.Warning;
                _logger.Log(logLevel, "License acquisition result: {Result} for {ToolName} by {Contributor}. Reason: {Reason}",
                    response.LicenseGranted ? "GRANTED" : "DENIED",
                    request.ToolName,
                    request.ContributorId,
                    response.Reason ?? "success");

                if (response.LicenseGranted)
                {
                    return Ok(response);
                }

                // --- NEW ENFORCEMENT LOGIC ---
                // If the license has expired, we return a 423 Locked status.
                // This is the signal for the client tool to PAUSE the pipeline and require manual approval.
                if (response.Reason == "license_expired_hard_lock")
                {
                    return StatusCode(423, response); // 423 Locked
                }

                if (response.Reason == "concurrent_limit_exceeded")
                {
                    // 429 Too Many Requests is the correct semantic code for rate limiting.
                    return StatusCode(429, response);
                }

                return StatusCode(403, response); // 403 Forbidden for other denial reasons
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred during license acquisition for tool: {ToolName}", request?.ToolName ?? "unknown");
                return StatusCode(500, new { reason = "internal_server_error" });
            }
        }

        [HttpPost("heartbeat")]
        public async Task<ActionResult<LicenseHeartbeatResponse>> Heartbeat([FromBody] LicenseHeartbeatRequest request)
        {
            if (string.IsNullOrEmpty(request.SessionId)) return BadRequest();

            var response = await _licenseService.HeartbeatAsync(request);
            if (!response.SessionValid)
            {
                _logger.LogWarning("Heartbeat failed for invalid or expired session: {SessionId}", request.SessionId);
                return NotFound();
            }
            return Ok(response);
        }

        [HttpPost("release")]
        public async Task<ActionResult<LicenseReleaseResponse>> ReleaseLicense([FromBody] LicenseReleaseRequest request)
        {
            if (string.IsNullOrEmpty(request.SessionId)) return BadRequest();

            var response = await _licenseService.ReleaseLicenseAsync(request);
            if (!response.SessionReleased)
            {
                // This is not a client error if the session is already gone, so we can return OK.
                _logger.LogWarning("Attempted to release a non-existent session: {SessionId}", request.SessionId);
            }
            return Ok(response);
        }

        [HttpGet("status")]
        public async Task<ActionResult<LicenseStatusResponse>> GetLicenseStatus()
        {
            var response = await _licenseService.GetLicenseStatusAsync();
            return Ok(response);
        }

        /// <summary>
        /// A secure, admin-only endpoint to generate the annual usage report.
        /// In a real system, this would be protected by an authentication mechanism.
        /// </summary>
        [HttpGet("report")]
        public async Task<IActionResult> GenerateUsageReport()
        {
            try
            {
                // TODO: Add robust authentication/authorization for this endpoint.
                // For now, we allow access but log a warning.
                _logger.LogWarning("ADMIN ACTION: Annual usage report generation triggered from {IpAddress}", HttpContext.Connection.RemoteIpAddress);

                var report = await _licenseService.GenerateAnnualUsageReportAsync();

                // Serialize the report to a JSON string
                var reportJson = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });

                // Return the signed report as a downloadable binary file.
                return File(Encoding.UTF8.GetBytes(reportJson), "application/octet-stream", $"3SC_Usage_Report_{DateTime.UtcNow:yyyy-MM-dd}.json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate annual usage report.");
                return StatusCode(500, new { reason = "report_generation_failed" });
            }
        }

        [HttpPost("cleanup")]
        public async Task<IActionResult> CleanupExpiredSessions()
        {
            // TODO: Protect this admin endpoint.
            _logger.LogWarning("ADMIN ACTION: Manual session cleanup triggered from {IpAddress}", HttpContext.Connection.RemoteIpAddress);
            await _licenseService.CleanupExpiredSessionsAsync();
            return Ok(new { message = "Expired sessions cleaned up successfully." });
        }
    }
}