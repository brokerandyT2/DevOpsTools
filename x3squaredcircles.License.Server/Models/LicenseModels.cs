using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace x3squaredcircles.License.Server.Models
{
    // ========================================================================
    // DATABASE ENTITIES
    // ========================================================================

    public class LicenseConfig
    {
        [Key]
        public int Id { get; set; }
        public int MaxConcurrent { get; set; }
        public string ToolsLicensed { get; set; } = string.Empty;
        public int BurstMultiplier { get; set; } = 2;
        public int BurstAllowancePerQuarter { get; set; } = 2;
    }

    public class QuarterlyUsage
    {
        [Key]
        public string QuarterId { get; set; } = string.Empty;
        public int BurstEventsUsed { get; set; } = 0;
        public DateTime QuarterStartDate { get; set; }
    }

    public class ActiveSession
    {
        [Key]
        public string SessionId { get; set; } = string.Empty;
        public string ToolName { get; set; } = string.Empty;
        public string ToolVersion { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime LastHeartbeat { get; set; }
        public string IpAddress { get; set; } = string.Empty;
        public string BuildId { get; set; } = string.Empty;
    }

    /// <summary>
    /// The definitive, local log for the daily contributor count, used for the annual true-up.
    /// This table will contain a maximum of ~380 rows.
    /// </summary>
    public class DailyContributorHistory
    {
        /// <summary>
        /// The date of the record in "YYYY-MM-DD" format.
        /// </summary>
        [Key]
        public string Date { get; set; } = string.Empty;

        /// <summary>
        /// The number of unique contributors reported by the CI/CD platform on this date.
        /// </summary>
        public int ContributorCount { get; set; }
    }


    // ========================================================================
    // LICENSE KEY & USAGE REPORT MODELS
    // ========================================================================

    public class LicenseKey
    {
        public string CustomerId { get; set; } = string.Empty;
        public DateTime ValidFrom { get; set; }
        public DateTime ValidUntil { get; set; }
        public int MaxConcurrent { get; set; }
        public List<string> LicensedTools { get; set; } = new List<string>();
        public int BurstMultiplier { get; set; } = 2;
        public int BurstAllowancePerQuarter { get; set; } = 2;
        public string Signature { get; set; } = string.Empty;
    }

    public class AnnualUsageReport
    {
        public string CustomerId { get; set; } = string.Empty;
        public DateTime ReportGeneratedAt { get; set; }
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public int PeakConcurrentUsage { get; set; }
        public int PeakContributorCount { get; set; }
        public int TotalBurstEventsUsed { get; set; }
        public string Signature { get; set; } = string.Empty;
    }

    // ========================================================================
    // API REQUEST & RESPONSE MODELS
    // ========================================================================

    public class LicenseAcquireRequest
    {
        [JsonPropertyName("tool_name")]
        public string ToolName { get; set; } = string.Empty;
        [JsonPropertyName("tool_version")]
        public string ToolVersion { get; set; } = string.Empty;
        [JsonPropertyName("ip_address")]
        public string IpAddress { get; set; } = string.Empty;
        [JsonPropertyName("build_id")]
        public string BuildId { get; set; } = string.Empty;

        public int ContributorId { get; set; } = 0;
    }

    public class LicenseAcquireResponse
    {
        [JsonPropertyName("license_granted")]
        public bool LicenseGranted { get; set; }
        [JsonPropertyName("session_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SessionId { get; set; }
        [JsonPropertyName("reason")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Reason { get; set; }
        [JsonPropertyName("is_burst_mode")]
        public bool IsBurstMode { get; set; }
        [JsonPropertyName("bursts_remaining_this_quarter")]
        public int BurstsRemainingThisQuarter { get; set; }
        [JsonPropertyName("warning")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public LicenseWarning? Warning { get; set; }
    }

    public class LicenseWarning
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
        [JsonPropertyName("days_remaining")]
        public int DaysRemaining { get; set; }
    }

    public class LicenseHeartbeatRequest
    {
        [JsonPropertyName("session_id")]
        public string SessionId { get; set; } = string.Empty;
    }

    public class LicenseHeartbeatResponse
    {
        [JsonPropertyName("session_valid")]
        public bool SessionValid { get; set; }
    }

    public class LicenseReleaseRequest
    {
        [JsonPropertyName("session_id")]
        public string SessionId { get; set; } = string.Empty;
    }

    public class LicenseReleaseResponse
    {
        [JsonPropertyName("session_released")]
        public bool SessionReleased { get; set; }
    }

    public class LicenseStatusResponse
    {
        [JsonPropertyName("is_license_valid")]
        public bool IsLicenseValid { get; set; }
        [JsonPropertyName("license_expires_at")]
        public DateTime LicenseExpiresAt { get; set; }
        [JsonPropertyName("max_concurrent")]
        public int MaxConcurrent { get; set; }
        [JsonPropertyName("current_concurrent")]
        public int CurrentConcurrent { get; set; }
        [JsonPropertyName("is_burst_available")]
        public bool IsBurstAvailable { get; set; }
        [JsonPropertyName("bursts_used_this_quarter")]
        public int BurstsUsedThisQuarter { get; set; }
        [JsonPropertyName("bursts_remaining_this_quarter")]
        public int BurstsRemainingThisQuarter { get; set; }
        [JsonPropertyName("licensed_tools")]
        public List<string> LicensedTools { get; set; } = new List<string>();
    }
}