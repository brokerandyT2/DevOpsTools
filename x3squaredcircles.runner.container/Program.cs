using Serilog;
using x3squaredcircles.runner.container;
using x3squaredcircles.runner.container.Adapters.GitHub;

using x3squaredcircles.runner.container.Engine;
using X3SquaredCircles.Runner.Container.Engine;
using x3squaredcircles.runner.container.Adapters.Azure;
using x3squaredcircles.runner.container.Adapters.GitLab;
using x3squaredcircles.runner.container.Api;
using x3squaredcircles.runner.container.Adapters.Jenkins;

// Use a static logger for any errors that occur during the bootstrap process
// before the host and its dependency-injected logger are fully configured.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    IHost host = Host.CreateDefaultBuilder(args)
        .UseSerilog((context, services, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"))
        .ConfigureServices(services =>
        {
            // Register the main background service which is the application's entry point after the host is built.
            services.AddHostedService<ConductorService>();

            // Register the core application engine as a singleton to ensure a single, consistent state.
            services.AddSingleton<CoreEngine>();

            // Register all available Platform Adapters.
            // The CoreEngine will request an IEnumerable<IPlatformAdapter>
            // to dynamically discover all supported platforms at runtime.
            services.AddSingleton<IPlatformAdapter, GitHubAdapter>();
            // When we add support for more platforms, they will be registered here:
            services.AddSingleton<IPlatformAdapter, AzureAdapter>();
            services.AddSingleton<IPlatformAdapter, GitLabAdapter>();
            services.AddSingleton<IPlatformAdapter, JenkinsAdapter>();

            // Register the API service as a singleton.
            services.AddSingleton<ApiService>();
        })
        .Build();

    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

