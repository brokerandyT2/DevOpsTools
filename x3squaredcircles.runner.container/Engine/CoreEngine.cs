using Microsoft.Extensions.Logging;
using System.Threading.Channels;
using x3squaredcircles.runner.container.Config;
using X3SquaredCircles.Runner.Container.Engine;

namespace x3squaredcircles.runner.container.Engine;

public class CoreEngine
{
    private enum EngineSignal
    {
        Refresh,
        FileChange
    }

    private readonly ILogger<CoreEngine> _logger;
    private readonly IEnumerable<IPlatformAdapter> _platformAdapters;
    private readonly IConfigService _configService;
    private readonly IServiceProvider _serviceProvider;
    private readonly Channel<EngineSignal> _signalChannel;
    private readonly string _projectRoot;

    private IPlatformAdapter? _activeAdapter;
    private UniversalBlueprint? _blueprint;
    private string? _pipelineFilePath;
    private string? _configFilePath;
    private FileSystemWatcher? _watcher;

    public CoreEngine(
        ILogger<CoreEngine> logger,
        IEnumerable<IPlatformAdapter> platformAdapters,
        IConfigService configService,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _platformAdapters = platformAdapters;
        _configService = configService;
        _serviceProvider = serviceProvider;
        _projectRoot = "/src"; // The container's working directory is always the project root.
        _signalChannel = Channel.CreateUnbounded<EngineSignal>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    public async Task StartAsync(CancellationToken stoppingToken)
    {
        try
        {
            await InitializeAsync();
            StartWatcher();
            _logger.LogInformation("Conductor is live. Watching for file changes.");

            await ProcessSignalsAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Core engine is shutting down.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "A fatal error occurred in the Core Engine. The service is stopping.");
        }
        finally
        {
            _watcher?.Dispose();
        }
    }

    public async Task RequestRefreshAsync()
    {
        await _signalChannel.Writer.WriteAsync(EngineSignal.Refresh);
    }

    private async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing Core Engine...");

        _activeAdapter = null;
        foreach (var adapter in _platformAdapters)
        {
            if (await adapter.CanHandleAsync(_projectRoot))
            {
                _activeAdapter = adapter;
                break;
            }
        }

        if (_activeAdapter is null)
        {
            throw new InvalidOperationException("No compatible CI/CD platform found in project. The Conductor requires a supported pipeline file (e.g., in .github/workflows/).");
        }

        _logger.LogInformation("Detected compatible platform: {PlatformId}", _activeAdapter.PlatformId);
        await LoadConfigurationAndBlueprintAsync();
    }

    private async Task LoadConfigurationAndBlueprintAsync()
    {
        // This is a simplification. A full implementation would ask the adapter for the file path(s).
        // For now, we assume a single file in a conventional location.
        var (pipelinePath, configPath) = GetDefaultFilePaths();
        _pipelineFilePath = pipelinePath;
        _configFilePath = configPath;

        _blueprint = await _activeAdapter!.ParseAsync(_projectRoot);
        await _configService.BootstrapAsync(_configFilePath, _blueprint);

        _logger.LogInformation("Pipeline blueprint and configuration loaded successfully.");
    }

    private void StartWatcher()
    {
        _watcher = new FileSystemWatcher(_projectRoot)
        {
            IncludeSubdirectories = true,
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName
        };

        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
        _watcher.Deleted += OnFileChanged;
        _watcher.Renamed += OnFileChanged;
        _watcher.Error += (sender, args) => _logger.LogError(args.GetException(), "File system watcher encountered an error.");

        _logger.LogDebug("File watcher started on path: {Path}", _projectRoot);
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // Ignore changes to the pipeline or config files themselves, and common noisy directories.
        if (e.FullPath == _pipelineFilePath ||
            e.FullPath == _configFilePath ||
            e.FullPath.Contains(".git") ||
            e.FullPath.Contains(".idea") ||
            e.FullPath.Contains(".vs"))
        {
            return;
        }

        _logger.LogInformation("Change detected in '{Path}'. Triggering pipeline run.", Path.GetRelativePath(_projectRoot, e.FullPath));
        _signalChannel.Writer.TryWrite(EngineSignal.FileChange);
    }

    private async Task ProcessSignalsAsync(CancellationToken stoppingToken)
    {
        await foreach (var signal in _signalChannel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                switch (signal)
                {
                    case EngineSignal.Refresh:
                        _logger.LogInformation("Processing REFRESH signal...");
                        await LoadConfigurationAndBlueprintAsync();
                        break;

                    case EngineSignal.FileChange:
                        _logger.LogInformation("Processing FILE_CHANGE signal...");
                        await TriggerRunAsync(stoppingToken);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while processing an engine signal.");
            }
        }
    }

    private async Task TriggerRunAsync(CancellationToken stoppingToken)
    {
        if (_blueprint is null || _configFilePath is null || _activeAdapter is null)
        {
            _logger.LogError("Cannot trigger run: Core Engine is not fully initialized.");
            return;
        }

        var config = await _configService.LoadAsync(_configFilePath);

        // Use the service provider to create a new Orchestrator for each run.
        // This ensures it has a clean state and its own scoped dependencies if needed.
        var orchestrator = ActivatorUtilities.CreateInstance<Orchestrator>(_serviceProvider, _blueprint, config, _activeAdapter);

        _logger.LogInformation("--- Starting Pipeline Run ---");
        var success = await orchestrator.ExecuteAsync(stoppingToken);
        if (success)
        {
            _logger.LogInformation("--- Pipeline Run SUCCEEDED ---");
        }
        else
        {
            _logger.LogWarning("--- Pipeline Run FAILED ---");
        }
    }

    private (string pipelinePath, string configPath) GetDefaultFilePaths()
    {
        // This logic will become more sophisticated as more adapters are added.
        var workflowDir = Path.Combine(_projectRoot, ".github", "workflows");
        var pipelinePath = Directory.EnumerateFiles(workflowDir)
            .FirstOrDefault(f => f.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrEmpty(pipelinePath))
        {
            throw new FileNotFoundException("Could not locate a primary workflow file in .github/workflows.");
        }

        var extension = Path.GetExtension(pipelinePath);
        var configPath = pipelinePath.Replace(extension, "-config.json");
        return (pipelinePath, configPath);
    }
}