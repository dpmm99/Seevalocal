using FluentResults;
using Seevalocal.Core.Models;
using Seevalocal.Metrics.Models;
using Seevalocal.Server;
using Seevalocal.Server.Models;
using Seevalocal.UI.ViewModels;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Seevalocal.UI.Services;

/// <summary>
/// Service for loading and managing configuration.
/// </summary>
public interface IConfigurationService
{
    /// <summary>
    /// Loads settings from the specified files and merges them with CLI overrides.
    /// </summary>
    Task<ResolvedConfig> LoadAndMergeAsync(
        IReadOnlyList<string> settingsFilePaths,
        PartialConfig cliOverrides,
        CancellationToken cancellationToken);

    /// <summary>
    /// Validates a resolved configuration.
    /// </summary>
    IReadOnlyList<ValidationError> Validate(ResolvedConfig config);

    /// <summary>
    /// Loads a partial config from a file.
    /// </summary>
    Task<Result<PartialConfig>> LoadPartialConfigAsync(string filePath, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a list of partial configs into a ResolvedConfig.
    /// </summary>
    Result<ResolvedConfig> Resolve(IReadOnlyList<PartialConfig> partials);
}

/// <summary>
/// Service for running evaluation pipelines.
/// </summary>
public interface IRunnerService
{
    /// <summary>
    /// Executes an evaluation run with the specified configuration.
    /// </summary>
    Task<int> RunAsync(
        ResolvedConfig config,
        bool showProgress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates a ViewModel for an eval run.
    /// </summary>
    Task<IEvalRunViewModel> CreateViewModelAsync(ResolvedConfig config, CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for eval run view models.
/// </summary>
public interface IEvalRunViewModel : INotifyPropertyChanged, IDisposable
{
    ResolvedConfig Config { get; }
    bool IsRunning { get; }
    bool IsPaused { get; }
    double ProgressPercent { get; }
    int CompletedCount { get; }
    int TotalCount { get; }
    double? EstimatedRemainingSeconds { get; }
    double AverageTokensPerSecond { get; }
    string StatusLine { get; }
    RunSummary? Summary { get; }
    bool HadFailures { get; }
    ObservableCollection<EvalResultViewModel> Results { get; }
    int EarlyCompletionsLimit { get; set; }
    IEnumerable<EvalResultViewModel> EarlyCompletions { get; }
    bool HasMoreEarlyCompletions { get; }
    System.Windows.Input.ICommand PauseCommand { get; }
    System.Windows.Input.ICommand CancelCommand { get; }
    System.Windows.Input.ICommand LoadMoreEarlyCompletionsCommand { get; }
    Task StartAsync(CancellationToken externalCt = default);
    void Cancel();
    void TogglePause();
}

/// <summary>
/// Service for managing llama-server lifecycle.
/// </summary>
public interface IServerLifecycleService : IAsyncDisposable
{
    /// <summary>Exposes llama-server loading progress events.</summary>
    event EventHandler<ServerLoadingProgressEventArgs>? LoadingProgressChanged;

    /// <summary>Gets the last reported loading progress (for late subscribers).</summary>
    ServerLoadingProgressEventArgs? LastLoadingProgress { get; }

    /// <summary>
    /// Starts the server with the specified configuration.
    /// </summary>
    Task<Result<ServerInfo>> StartAsync(
        ServerConfig config,
        LlamaServerSettings settings,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets the server properties including total_slots.
    /// </summary>
    Task<Result<ServerProps>> GetPropsAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Shell script exporter interface for DI.
/// </summary>
public interface IShellScriptExporter
{
    string Export(ResolvedConfig config, ShellTarget target);
}
