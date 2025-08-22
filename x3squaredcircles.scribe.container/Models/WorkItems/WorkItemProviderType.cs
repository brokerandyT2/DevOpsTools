namespace x3squaredcircles.scribe.container.Models.WorkItems
{
    /// <summary>
    /// Represents the supported work item providers as defined in the architectural specification.
    /// This provides a strongly-typed way to identify and manage different provider implementations.
    /// </summary>
    public enum WorkItemProviderType
    {
        /// <summary>
        /// The provider could not be determined, or the Scribe is operating in a disconnected state.
        /// This is the default value and signals that no enrichment should be attempted.
        /// </summary>
        Unknown,

        /// <summary>
        /// Atlassian Jira.
        /// </summary>
        Jira,

        /// <summary>
        /// Microsoft Azure DevOps.
        /// </summary>
        AzureDevOps,

        /// <summary>
        /// GitHub Issues.
        /// </summary>
        GitHub
    }
}