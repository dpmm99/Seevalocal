using FluentResults;
using Microsoft.Extensions.Logging;
using Seevalocal.Core.Models;
using Seevalocal.Server;
using Seevalocal.Server.Models;
using System.Collections.Concurrent;

namespace Seevalocal.UI.Services;

/// <summary>
/// Default implementation of IServerLifecycleService.
/// Manages multiple llama-server instances (primary + judge).
/// </summary>
public sealed class DefaultServerLifecycleService(
    ILogger<DefaultServerLifecycleService> logger,
    ILoggerFactory loggerFactory,
    LlamaServerArgBuilder argBuilder,
    LlamaServerDownloader downloader,
    GpuDetector gpuDetector,
    HttpClient httpClient) : IServerLifecycleService
{
    private readonly ILogger<DefaultServerLifecycleService> _logger = logger;
    private readonly ILoggerFactory _loggerFactory = loggerFactory;
    private readonly ConcurrentBag<LlamaServerManager> _managers = [];

    /// <summary>Exposes the llama-server loading progress event from the most recently started server.</summary>
    public event EventHandler<ServerLoadingProgressEventArgs>? LoadingProgressChanged;

    /// <summary>Exposes the llama-server error output event from the most recently started server.</summary>
    public event EventHandler<ServerErrorEventArgs>? ServerErrorReceived;

    private LlamaServerManager? _activeManager;
    private ServerLoadingProgressEventArgs? _lastLoadingProgress;

    private LlamaServerManager? ActiveManager
    {
        get => _activeManager;
        set
        {
            if (_activeManager != null)
            {
                _activeManager.LoadingProgressChanged -= OnLoadingProgressChanged;
                _activeManager.ServerErrorReceived -= OnServerErrorReceived;
            }

            _activeManager = value;

            if (_activeManager != null)
            {
                _activeManager.LoadingProgressChanged += OnLoadingProgressChanged;
                _activeManager.ServerErrorReceived += OnServerErrorReceived;
            }
        }
    }

    private void OnLoadingProgressChanged(object? sender, ServerLoadingProgressEventArgs e)
    {
        _lastLoadingProgress = e;
        LoadingProgressChanged?.Invoke(sender, e);
    }

    private void OnServerErrorReceived(object? sender, ServerErrorEventArgs e)
    {
        ServerErrorReceived?.Invoke(sender, e);
    }

    /// <summary>Gets the last reported loading progress (for late subscribers).</summary>
    public ServerLoadingProgressEventArgs? LastLoadingProgress => _lastLoadingProgress;

    public async Task<Result<ServerInfo>> StartAsync(
        ServerConfig config,
        LlamaServerSettings settings,
        CancellationToken cancellationToken)
    {
        // Create a new manager for this server instance
        var managerLogger = _loggerFactory.CreateLogger<LlamaServerManager>();
        var manager = new LlamaServerManager(downloader, gpuDetector, httpClient, managerLogger);
        _managers.Add(manager);
        ActiveManager = manager;

        var result = await manager.StartAsync(config, settings, cancellationToken);
        return result;
    }

    public async Task<Result<ServerProps>> GetPropsAsync(CancellationToken cancellationToken)
    {
        return ActiveManager == null ? Result.Fail<ServerProps>("No server started") : await ActiveManager.GetPropsAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        // Stop all managed servers
        foreach (var manager in _managers)
        {
            try
            {
                await manager.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error stopping llama-server instance");
            }
        }

        _managers.Clear();
        ActiveManager = null;
    }
}
