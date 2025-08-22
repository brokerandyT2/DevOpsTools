using System.ComponentModel.DataAnnotations;

namespace x3squaredcircles.scribe.container.Configuration
{
    /// <summary>
    /// Represents the strongly-typed configuration settings for The Scribe application,
    '// derived from the environment variables defined in the architectural specification.
    /// </summary>
    public class ScribeSettings
    {
        /// <summary>
        /// The key used to bind this configuration from the host configuration (e.g., appsettings.json section).
        /// </summary>
        public const string ConfigurationSectionName = "Scribe";

        /// <summary>
        /// Maps to SCRIBE_WIKI_ROOT_PATH.
        /// Required. The base path where the /RELEASES directory structure will be created.
        /// Example: /src/MyProject.wiki
        /// </summary>
        [Required(AllowEmptyStrings = false, ErrorMessage = "SCRIBE_WIKI_ROOT_PATH is a required environment variable.")]
        public string WikiRootPath { get; set; } = string.Empty;

        /// <summary>
        /// Maps to SCRIBE_APP_NAME.
        /// Required. The name of the application, used to create the sub-folder in the output path.
        /// Example: MyWebApp
        /// </summary>
        [Required(AllowEmptyStrings = false, ErrorMessage = "SCRIBE_APP_NAME is a required environment variable.")]
        public string AppName { get; set; } = string.Empty;

        /// <summary>
        /// Maps to SCRIBE_WORK_ITEM_STYLE.
        /// Optional. Controls the format of the Work_Items.md page.
        /// Defaults to "list".
        /// Example: list, categorized
        /// </summary>
        public string WorkItemStyle { get; set; } = "list";

        /// <summary>
        /// Maps to SCRIBE_WI_URL.
        /// Optional. Triggers the smart detection override for the work item provider.
        /// Example: https://mycompany.atlassian.net
        /// </summary>
        public string? WorkItemUrl { get; set; }

        /// <summary>
        /// Maps to SCRIBE_WI_PAT.
        /// Optional. The Personal Access Token for the override work item provider.
        /// Should be loaded from a secret.
        /// </summary>
        public string? WorkItemPat { get; set; }

        /// <summary>
        /// Maps to SCRIBE_WI_PROVIDER.
        /// Optional. The final escape hatch if smart detection fails.
        /// Example: jira, azuredevops, github
        /// </summary>
        public string? WorkItemProvider { get; set; }
    }
}