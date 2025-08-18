using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using x3squaredcircles.License.Server.Data;
using x3squaredcircles.License.Server.Models;

namespace x3squaredcircles.License.Server.Services
{
    /// <summary>
    /// A singleton service responsible for initializing and providing access to the
    /// core license configuration. It reads the embedded, time-bombed license key
    /// on startup, cryptographically validates it, and hydrates the database.
    /// </summary>
    public interface ILicenseConfigService
    {
        /// <summary>
        /// Initializes the license configuration from the embedded key file.
        /// This should be called once on application startup.
        /// </summary>
        Task InitializeLicenseConfigAsync();

        /// <summary>
        /// Gets the cached, authoritative license configuration.
        /// </summary>
        Task<LicenseConfig> GetLicenseConfigAsync();

        /// <summary>
        /// Gets the current, valid license key details.
        /// </summary>
        LicenseKey GetCurrentLicenseKey();

        /// <summary>
        /// Checks if a specific tool is licensed according to the current key.
        /// </summary>
        Task<bool> IsToolLicensedAsync(string toolName);
    }

    public class LicenseConfigService : ILicenseConfigService
    {
        // Using IDbContextFactory for safe, concurrent database access in a singleton service.
        private readonly IDbContextFactory<LicenseDbContext> _contextFactory;
        private readonly ILogger<LicenseConfigService> _logger;
        private LicenseConfig? _cachedConfig;
        private LicenseKey? _cachedLicenseKey;

        // This is our secret key for HMAC signature validation.
        // In a real production system, this would be injected via a secure mechanism
        // (like another environment variable or a startup secret), not hardcoded.
        private static readonly byte[] HmacSecretKey = Encoding.UTF8.GetBytes("Th1s-Is-A-V3ry-S3cur3-And-Pr0pr13tary-S3cr3t-K3y!");

        private const string EmbeddedKeyPath = "/app/config/license.key";

        public LicenseConfigService(IDbContextFactory<LicenseDbContext> contextFactory, ILogger<LicenseConfigService> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;
        }

        public async Task InitializeLicenseConfigAsync()
        {
            // A using declaration ensures the DbContext is properly disposed.
            using var context = _contextFactory.CreateDbContext();

            try
            {
                _logger.LogInformation("Initializing license configuration...");

                // Step 1: Read and validate the physical license key from its embedded location. This is the source of truth.
                var licenseKey = GetAndValidateEmbeddedLicenseKey();
                _cachedLicenseKey = licenseKey;

                // Step 2: Check if the database config matches the key. This prevents unnecessary writes.
                var existingConfig = await context.LicenseConfigs.FirstOrDefaultAsync();
                if (existingConfig != null && IsConfigSyncedWithKey(existingConfig, licenseKey))
                {
                    _logger.LogInformation("License configuration is already initialized and in sync with the current key.");
                    _cachedConfig = existingConfig;
                    return;
                }

                // Step 3: If out of sync or it's the first run, clear the old config and hydrate the DB from the new key.
                if (existingConfig != null)
                {
                    _logger.LogWarning("Existing database configuration is out of sync with the embedded license key. Re-initializing.");
                    context.LicenseConfigs.RemoveRange(context.LicenseConfigs);
                }

                var newConfig = new LicenseConfig
                {
                    MaxConcurrent = licenseKey.MaxConcurrent,
                    ToolsLicensed = JsonSerializer.Serialize(licenseKey.LicensedTools),
                    BurstMultiplier = licenseKey.BurstMultiplier,
                    BurstAllowancePerQuarter = licenseKey.BurstAllowancePerQuarter
                };

                context.LicenseConfigs.Add(newConfig);
                await context.SaveChangesAsync();
                _cachedConfig = newConfig;

                _logger.LogInformation("License configuration initialized successfully from embedded key for Customer: {CustomerId}", licenseKey.CustomerId);
                _logger.LogInformation("-> License valid until: {ExpirationDate}", licenseKey.ValidUntil.ToString("O"));
                _logger.LogInformation("-> Max Concurrent: {MaxConcurrent}", newConfig.MaxConcurrent);
                _logger.LogInformation("-> Burst Allowance: {BurstAllowance}/quarter", newConfig.BurstAllowancePerQuarter);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "FATAL: Failed to initialize license configuration. The embedded license key may be invalid, missing, or corrupted.");
                // We re-throw here to ensure the application fails to start if the license is invalid. This is a critical security measure.
                throw;
            }
        }

        public async Task<LicenseConfig> GetLicenseConfigAsync()
        {
            if (_cachedConfig != null) return _cachedConfig;

            using var context = _contextFactory.CreateDbContext();
            // Use AsNoTracking for read-only queries for better performance.
            var config = await context.LicenseConfigs.AsNoTracking().FirstOrDefaultAsync();
            if (config == null)
            {
                // This state should be unreachable if Initialize is called on startup.
                throw new InvalidOperationException("License configuration has not been initialized.");
            }

            _cachedConfig = config;
            return config;
        }

        public LicenseKey GetCurrentLicenseKey()
        {
            if (_cachedLicenseKey == null)
            {
                throw new InvalidOperationException("License key has not been loaded. Call InitializeLicenseConfigAsync on startup.");
            }
            return _cachedLicenseKey;
        }

        public async Task<bool> IsToolLicensedAsync(string toolName)
        {
            var config = await GetLicenseConfigAsync();
            var licensedTools = JsonSerializer.Deserialize<List<string>>(config.ToolsLicensed) ?? new List<string>();
            return licensedTools.Contains(toolName, StringComparer.OrdinalIgnoreCase);
        }

        private bool IsConfigSyncedWithKey(LicenseConfig config, LicenseKey key)
        {
            // A more robust way to compare the lists of tools, insensitive to order.
            var configTools = JsonSerializer.Deserialize<List<string>>(config.ToolsLicensed)?.ToHashSet() ?? new HashSet<string>();
            var keyTools = key.LicensedTools.ToHashSet();

            return config.MaxConcurrent == key.MaxConcurrent &&
                   configTools.SetEquals(keyTools) &&
                   config.BurstMultiplier == key.BurstMultiplier &&
                   config.BurstAllowancePerQuarter == key.BurstAllowancePerQuarter;
        }

        private LicenseKey GetAndValidateEmbeddedLicenseKey()
        {
            _logger.LogInformation("Attempting to load license key from embedded path: {KeyPath}", EmbeddedKeyPath);

            if (!File.Exists(EmbeddedKeyPath))
            {
                _logger.LogCritical("Embedded license key file not found at {KeyPath}. This indicates a critical build or deployment error.", EmbeddedKeyPath);
                throw new FileNotFoundException("Embedded license key file not found.", EmbeddedKeyPath);
            }

            var keyContent = File.ReadAllText(EmbeddedKeyPath);
            var licenseKey = JsonSerializer.Deserialize<LicenseKey>(keyContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (licenseKey == null)
            {
                throw new InvalidDataException("Failed to deserialize the embedded license key. The file may be corrupted.");
            }

            // --- FULLY IMPLEMENTED SIGNATURE VALIDATION ---
            var expectedSignature = GenerateHmacSignature(licenseKey);
            if (string.IsNullOrEmpty(licenseKey.Signature) || !licenseKey.Signature.Equals(expectedSignature, StringComparison.Ordinal))
            {
                _logger.LogCritical("Embedded license key signature is INVALID! Expected '{Expected}', but got '{Actual}'. This is a critical security failure, indicating tampering.", expectedSignature, licenseKey.Signature);
                throw new SecurityException("License key signature validation failed. The key may have been tampered with.");
            }
            _logger.LogInformation("✓ Embedded license key signature validated successfully.");

            if (DateTime.UtcNow > licenseKey.ValidUntil)
            {
                _logger.LogWarning("ATTENTION: The embedded license key has expired! Expiration date: {ExpirationDate}", licenseKey.ValidUntil.ToString("O"));
            }
            else
            {
                _logger.LogInformation("✓ Embedded license key is within its validity period.");
            }

            return licenseKey;
        }

        /// <summary>
        /// Generates a cryptographic HMAC signature for a LicenseKey object.
        /// This ensures the license data has not been tampered with.
        /// </summary>
        /// <param name="key">The license key to sign.</param>
        /// <returns>A hex-encoded HMAC-SHA256 signature string.</returns>
        private static string GenerateHmacSignature(LicenseKey key)
        {
            // We create a canonical string representation of the license data.
            // This is critical - the data must be in a consistent order to produce a stable signature.
            var canonicalPayload = new StringBuilder();
            canonicalPayload.Append(key.CustomerId);
            canonicalPayload.Append(key.ValidFrom.ToString("o")); // ISO 8601 format
            canonicalPayload.Append(key.ValidUntil.ToString("o"));
            canonicalPayload.Append(key.MaxConcurrent);

            // Sort the tools list to ensure consistent order
            var sortedTools = key.LicensedTools.OrderBy(t => t).ToList();
            canonicalPayload.Append(JsonSerializer.Serialize(sortedTools));

            canonicalPayload.Append(key.BurstMultiplier);
            canonicalPayload.Append(key.BurstAllowancePerQuarter);

            using (var hmac = new HMACSHA256(HmacSecretKey))
            {
                var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonicalPayload.ToString()));
                return Convert.ToHexString(hash);
            }
        }
    }
}