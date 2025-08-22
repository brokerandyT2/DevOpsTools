using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Threading.Tasks;
using x3squaredcircles.scribe.container.Configuration;

namespace x3squaredcircles.scribe.container.Services
{
    /// <summary>
    /// Implements the logic for managing the Scribe's output directory structure.
    /// This service creates the standardized, non-configurable output path mandated
    /// by the architectural specification.
    /// </summary>
    public class OutputManagerService : IOutputManagerService
    {
        private readonly ILogger<OutputManagerService> _logger;
        private readonly ScribeSettings _settings;

        /// <inheritdoc />
        public string FinalReleasePath { get; private set; } = string.Empty;

        /// <inheritdoc />
        public string AttachmentsPath { get; private set; } = string.Empty;

        /// <summary>
        /// Initializes a new instance of the <see cref="OutputManagerService"/> class.
        /// </summary>
        /// <param name="settings">The application's strongly-typed configuration.</param>
        /// <param name="logger">The logger for forensic and operational messages.</param>
        public OutputManagerService(IOptions<ScribeSettings> settings, ILogger<OutputManagerService> logger)
        {
            _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public Task InitializeOutputStructureAsync(string releaseVersion)
        {
            if (string.IsNullOrWhiteSpace(releaseVersion))
            {
                throw new ArgumentException("Release version cannot be null or whitespace.", nameof(releaseVersion));
            }

            _logger.LogInformation("Initializing output directory structure for version {Version}", releaseVersion);

            try
            {
                //
                // Path Template: [SCRIBE_WIKI_ROOT_PATH]/RELEASES/[SCRIBE_APP_NAME]/[Version]-[Date]/
                // -----------------------------------------------------------------------------------
                // This logic directly implements the hardcoded path structure from the specification.
                //
                var dateStamp = DateTime.UtcNow.ToString("yyyyMMdd");
                var releaseFolderName = $"{releaseVersion}-{dateStamp}";

                // Use Path.Combine for cross-platform compatibility and to prevent path traversal issues.
                FinalReleasePath = Path.Combine(_settings.WikiRootPath, "RELEASES", _settings.AppName, releaseFolderName);
                AttachmentsPath = Path.Combine(FinalReleasePath, "attachments");

                _logger.LogInformation("Calculated final release path: {Path}", FinalReleasePath);

                // Directory.CreateDirectory is idempotent: it does nothing if the directory already exists.
                _logger.LogDebug("Ensuring creation of release directory: {Path}", FinalReleasePath);
                Directory.CreateDirectory(FinalReleasePath);

                _logger.LogDebug("Ensuring creation of attachments directory: {Path}", AttachmentsPath);
                Directory.CreateDirectory(AttachmentsPath);

                _logger.LogInformation("Output directory structure is ready.");
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "A critical error occurred while creating the output directory structure at {Path}", FinalReleasePath);
                // This is a fatal error. Re-throw to halt the application's execution.
                throw new IOException($"Failed to create required directory structure at '{FinalReleasePath}'. See inner exception for details.", ex);
            }

            // Directory creation is a synchronous operation, so we can return a completed task.
            return Task.CompletedTask;
        }
    }
}