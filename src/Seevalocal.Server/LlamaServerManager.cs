using FluentResults;
using Microsoft.Extensions.Logging;
using Seevalocal.Core.Models;
using Seevalocal.Server.Models;
using System.Runtime.InteropServices;
using System.Text;

namespace Seevalocal.Server;

/// <summary>
/// Manages the full lifecycle of a llama-server process.
/// If <see cref="ServerConfig.Manage"/> is false, simply validates connectivity.
/// Thread-safe after <see cref="StartAsync"/> completes.
/// Process cleanup strategy:
/// - Windows: Uses Job Objects to automatically kill llama-server when this process dies
/// - Unix: Spawns a monitor process that watches for parent death and kills llama-server
/// </summary>
public sealed partial class LlamaServerManager(
    LlamaServerDownloader downloader,
    GpuDetector gpuDetector,
    HttpClient httpClient,
    ILogger<LlamaServerManager> logger) : IAsyncDisposable
{
    private const double DefaultHealthTimeoutSeconds = 300.0;
    private const int ExpectedLoadingDots = 94;  // llama-server typically outputs ~94 dots during model load

    private readonly LlamaServerDownloader _downloader = downloader;
    private readonly GpuDetector _gpuDetector = gpuDetector;
    private readonly ILogger<LlamaServerManager> _logger = logger;
    private readonly HttpClient _httpClient = httpClient;

    private System.Diagnostics.Process? _process;
    private ServerInfo? _serverInfo;
    private string? _binaryPath;  // Track the binary path for managed servers
    private bool _disposed;
    private int _loadingDotCount;
    private bool _loadingStarted;  // True after seeing "load_tensors"
    private bool _loadingComplete;

    // Process cleanup helpers
    private WindowsJobObject? _jobObject;  // Windows only
    private System.Diagnostics.Process? _monitorProcess;  // Unix only

    /// <summary>Fires when the managed process exits unexpectedly (not via DisposeAsync).</summary>
    public event EventHandler<ProcessExitedEventArgs>? ProcessExited;

    /// <summary>Fires during llama-server startup with loading progress (0-100%).</summary>
    public event EventHandler<ServerLoadingProgressEventArgs>? LoadingProgressChanged;

    /// <summary>Fires when error output is received from llama-server.</summary>
    public event EventHandler<ServerErrorEventArgs>? ServerErrorReceived;

    /// <summary>
    /// Starts (or connects to) a llama-server instance.
    /// Returns a <see cref="ServerInfo"/> on success.
    /// </summary>
    public async Task<Result<ServerInfo>> StartAsync(
        ServerConfig config,
        LlamaServerSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config.Manage != false) ArgumentNullException.ThrowIfNull(settings);

        // Reset loading state
        _loadingDotCount = 0;
        _loadingStarted = false;
        _loadingComplete = false;

        return config.Manage != false
            ? await StartManagedAsync(config, settings, cancellationToken)
            : await ConnectExistingAsync(config, cancellationToken);
    }

    /// <summary>Returns current server props (including slot count).</summary>
    public async Task<Result<ServerProps>> GetPropsAsync(CancellationToken cancellationToken = default)
    {
        if (_serverInfo is null)
            return Result.Fail("[LlamaServerManager] Server has not been started");

        var client = BuildClient(_serverInfo);
        return await client.GetPropsAsync(cancellationToken);
    }

    // ── Managed Start ─────────────────────────────────────────────────────────

    private async Task<Result<ServerInfo>> StartManagedAsync(
        ServerConfig config,
        LlamaServerSettings settings,
        CancellationToken ct)
    {
        // 1. Resolve binary (0-10%)
        LoadingProgressChanged?.Invoke(this, new ServerLoadingProgressEventArgs
        {
            ProgressPercent = 5,
            Message = "Resolving and/or downloading llama-server binary..." //TODO: should show download progress (using the full progress bar range) if downloading
        });
        var binaryResult = await ResolveBinaryPathAsync(config, ct);
        if (binaryResult.IsFailed) return binaryResult.ToResult<ServerInfo>();
        var binaryPath = binaryResult.Value;
        _binaryPath = binaryPath;  // Store for later use in ServerInfo

        // 2. Build args (instantaneous)
        var args = LlamaServerArgBuilder.Build(settings, config);
        _logger.LogDebug("llama-server args: {Args}", string.Join(" ", args));

        // 3. Start process (10-25%)
        LoadingProgressChanged?.Invoke(this, new ServerLoadingProgressEventArgs
        {
            ProgressPercent = 10,
            Message = "Starting llama-server process..."
        });
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = binaryPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        _process = new System.Diagnostics.Process { StartInfo = psi, EnableRaisingEvents = true };

        // Use character-by-character reading to capture loading dots in real-time
        // llama-server outputs dots one at a time without newlines during model loading
        var outputLock = new object();
        var errors = new List<string>() { "error", "failed", "unable", "insufficient", "lost device", "device lost", "out of memory", "fatal", "violation" };

        _process.Exited += OnProcessExited;

        try
        {
            _ = _process.Start();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start llama-server process");
            return Result.Fail($"[LlamaServerManager] Failed to start process: {ex.Message}");
        }

        // Set up process cleanup (Job Object on Windows, monitor process on Unix)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                _jobObject = new WindowsJobObject();
                _jobObject.AddProcess(_process);
                _logger.LogDebug("Added llama-server (PID={Pid}) to Windows Job Object for automatic cleanup", _process.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to set up Job Object for llama-server; process may not be cleaned up on crash");
            }
        }
        else
        {
            // Unix: start a monitor process that will kill llama-server if we die
            try
            {
                var parentPid = Environment.ProcessId;
                var childPid = _process.Id;
                _monitorProcess = ProcessCleanupMonitor.StartMonitor(parentPid, childPid);
                if (_monitorProcess != null)
                {
                    _logger.LogDebug("Started monitor process (PID={MonitorPid}) for llama-server (PID={ChildPid})",
                        _monitorProcess.Id, childPid);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to start monitor process for llama-server; process may not be cleaned up on crash");
            }
        }

        // Start background tasks to read output character-by-character
        // This captures loading dots in real-time (they're output one at a time without newlines)
        _ = Task.Run(() => ReadOutputStreamAsync(_process.StandardOutput, outputLock, isErrorStream: false, ct), ct);
        _ = Task.Run(() => ReadOutputStreamAsync(_process.StandardError, outputLock, isErrorStream: true, ct), ct);

        _logger.LogInformation("llama-server process started (PID={Pid})", _process.Id);

        // 4. Wait for health (80-95%)
        var baseUrl = $"http://{config.Host ?? "127.0.0.1"}:{config.Port ?? 8080}";
        var healthy = await WaitForHealthAsync(baseUrl, DefaultHealthTimeoutSeconds, ct);
        if (!healthy)
        {
            return Result.Fail(
                $"[LlamaServerManager] Health check timed out after {DefaultHealthTimeoutSeconds:F0} seconds: " +
                "process may have crashed");
        }

        // 5. Fetch props (95-100%)
        LoadingProgressChanged?.Invoke(this, new ServerLoadingProgressEventArgs
        {
            ProgressPercent = 95,
            Message = "Fetching server properties..."
        });
        var tempInfo = new ServerInfo { BaseUrl = baseUrl, ApiKey = config.ApiKey };
        var client = BuildClient(tempInfo);
        var propsResult = await client.GetPropsAsync(ct);
        if (propsResult.IsFailed) return propsResult.ToResult<ServerInfo>();

        var props = propsResult.Value;
        _serverInfo = new ServerInfo
        {
            BaseUrl = baseUrl,
            ApiKey = config.ApiKey,
            TotalSlots = props.TotalSlots,
            ModelAlias = Path.GetFileName(props.ModelPath),
            BinaryPath = _binaryPath,
        };

        _logger.LogInformation(
            "llama-server ready at {BaseUrl} (slots={TotalSlots}, model={ModelAlias})",
            _serverInfo.BaseUrl, _serverInfo.TotalSlots, _serverInfo.ModelAlias);

        // Report 100% - server fully ready
        LoadingProgressChanged?.Invoke(this, new ServerLoadingProgressEventArgs
        {
            ProgressPercent = 100,
            Message = $"llama-server ready (slots={props.TotalSlots})"
        });

        return Result.Ok(_serverInfo);
    }

    // ── Connect Existing ──────────────────────────────────────────────────────

    private async Task<Result<ServerInfo>> ConnectExistingAsync(ServerConfig config, CancellationToken ct)
    {
        var baseUrl = config.BaseUrl
            ?? throw new ArgumentException("ServerConfig.BaseUrl is required when Manage=false", nameof(config));

        var tempInfo = new ServerInfo { BaseUrl = baseUrl, ApiKey = config.ApiKey };
        var client = BuildClient(tempInfo);

        var healthResult = await client.GetHealthAsync(ct);
        if (healthResult.IsFailed)
            return healthResult.ToResult<ServerInfo>();

        if (!healthResult.Value.IsOk)
            return Result.Fail($"[LlamaServerManager] Server at {baseUrl} returned unhealthy status");

        var propsResult = await client.GetPropsAsync(ct);
        if (propsResult.IsFailed) return propsResult.ToResult<ServerInfo>();

        _serverInfo = new ServerInfo
        {
            BaseUrl = baseUrl,
            ApiKey = config.ApiKey,
            TotalSlots = propsResult.Value.TotalSlots,
            ModelAlias = Path.GetFileName(propsResult.Value.ModelPath),
        };

        _logger.LogInformation(
            "Connected to existing llama-server at {BaseUrl} (slots={TotalSlots})",
            _serverInfo.BaseUrl, _serverInfo.TotalSlots);

        return Result.Ok(_serverInfo);
    }

    // ── Binary Resolution ─────────────────────────────────────────────────────

    private async Task<Result<string>> ResolveBinaryPathAsync(ServerConfig config, CancellationToken ct)
    {
        if (config.ExecutablePath is not null)
        {
            var path = Path.GetFullPath(config.ExecutablePath);
            if (!File.Exists(path))
                return Result.Fail($"[LlamaServerManager] Executable not found: {path}");
            _logger.LogInformation("Using explicit llama-server path: {Path}", path);
            return Result.Ok(path);
        }

        var gpuKind = await _gpuDetector.DetectAsync(ct);
        var cacheRoot = GetCacheRoot();

        return await _downloader.EnsureAvailableAsync(null, gpuKind, cacheRoot, ct);
    }

    private static string GetCacheRoot()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(appData, "Seevalocal", "cache");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".Seevalocal", "cache");
    }

    // ── Health Polling ────────────────────────────────────────────────────────

    private async Task<bool> WaitForHealthAsync(string baseUrl, double timeoutSeconds, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        var delayMilliseconds = 500;

        _logger.LogDebug("Waiting for llama-server health at {BaseUrl} (timeout={TimeoutSeconds}s)",
            baseUrl, timeoutSeconds);

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var response = await _httpClient.GetAsync($"{baseUrl}/health", ct);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("llama-server health OK");
                    return true;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Server not up yet; keep polling
                _logger.LogTrace("Health poll failed: {Message}", ex.Message);
            }

            await Task.Delay(delayMilliseconds, ct);
            delayMilliseconds = Math.Min(delayMilliseconds * 2, 2000);
        }

        return false;
    }

    // ── Process Events ────────────────────────────────────────────────────────

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (_disposed) return;

        int exitCode = -192849262;

        try
        {
            exitCode = _process?.ExitCode ?? -1;
        }
        catch { }
        _logger.LogWarning("llama-server process exited unexpectedly (exit code={ExitCode})", exitCode == -192849262 ? "unknown" : exitCode.ToString());
        ProcessExited?.Invoke(this, new ProcessExitedEventArgs(exitCode));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private LlamaServerClient BuildClient(ServerInfo info) =>
        new(info, _httpClient, Microsoft.Extensions.Logging.Abstractions.NullLogger<LlamaServerClient>.Instance);

    // ── Dispose ───────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_process?.HasExited != false)
        {
            _process?.Dispose();
            _jobObject?.Dispose();
            _monitorProcess?.Dispose();
            return;
        }

        _logger.LogInformation("Shutting down llama-server (PID={Pid})", _process.Id);

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _process.Kill(entireProcessTree: true);
            }
            else
            {
                // Send SIGTERM first
                _process.Kill(false);  // false = not forced, but Kill() on Unix sends SIGKILL
                // Try graceful first with a short wait
                var exited = await Task.WhenAny(
                    _process.WaitForExitAsync(),
                    Task.Delay(TimeSpan.FromSeconds(5)));
                if (!_process.HasExited)
                    _process.Kill(entireProcessTree: true);
            }

            await _process.WaitForExitAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Exception while shutting down llama-server process");
        }
        finally
        {
            _process.Dispose();
            _jobObject?.Dispose();

            // Clean up monitor process (Unix only)
            if (_monitorProcess?.HasExited == false)
            {
                try
                {
                    _monitorProcess.Kill();
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await _monitorProcess.WaitForExitAsync(cts.Token);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Exception while cleaning up monitor process");
                }
                finally
                {
                    _monitorProcess.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// Reads output stream character-by-character to capture loading dots in real-time.
    /// llama-server outputs dots one at a time without newlines during model loading.
    /// </summary>
    private async Task ReadOutputStreamAsync(StreamReader reader, object outputLock, bool isErrorStream = false, CancellationToken ct = default)
    {
        var buffer = new char[1];
        var lineBuffer = new StringBuilder();

        try
        {
            int charsRead;
            do
            {
                charsRead = await reader.ReadAsync(buffer, ct);
                if (charsRead > 0)
                {
                    var ch = buffer[0];

                    if (ch == '.' && _loadingStarted && !_loadingComplete)
                    {
                        HandleLoadingDot();  // real-time, no newline needed
                        lineBuffer.Append(ch);
                    }
                    else if (ch == '\n' || ch == '\r')
                    {
                        var line = lineBuffer.ToString().Trim();
                        lineBuffer.Clear();

                        if (!string.IsNullOrEmpty(line))
                        {
                            lock (outputLock) { ProcessOutputLine(line, isErrorStream); }
                        }
                    }
                    else
                    {
                        lineBuffer.Append(ch);
                    }
                }
            } while (charsRead > 0);
        }
        catch (ObjectDisposedException) { /* Expected during shutdown */ }
        catch (OperationCanceledException) { /* Expected during cancellation */ }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading llama-server output stream");
        }
    }

    private void HandleLoadingDot()
    {
        _loadingDotCount++;
        // Map dots (0–94) to progress range 15–80%
        var progressPercent = 15 + Math.Min(65, _loadingDotCount * 65 / ExpectedLoadingDots);
        LoadingProgressChanged?.Invoke(this, new ServerLoadingProgressEventArgs
        {
            ProgressPercent = progressPercent,
            Message = $"Loading model... ({_loadingDotCount}/{ExpectedLoadingDots})"
        });
    }

    private void ProcessOutputLine(string line, bool isErrorStream)
    {
        // Must be called with outputLock held

        if (isErrorStream)
            ProcessStderrLine(line);
        else
            _logger.LogDebug("[llama-server] {Line}", line);

        ProcessLoadingMarkers(line);
    }

    private void ProcessStderrLine(string line)
    {
        _logger.LogDebug("[llama-server stderr] {Line}", line);

        var errorKeywords = new[] { "error", "failed", "unable", "insufficient", "lost device", "device lost", "out of memory", "fatal", "violation" };
        if (errorKeywords.Any(kw => line.Contains(kw, StringComparison.OrdinalIgnoreCase)))
            ServerErrorReceived?.Invoke(this, new ServerErrorEventArgs(line));
    }

    private void ProcessLoadingMarkers(string line)
    {
        if (line.StartsWith("load_tensors", StringComparison.OrdinalIgnoreCase))
        {
            if (!_loadingStarted)
            {
                _loadingStarted = true;
                _loadingDotCount = 0;
                _logger.LogDebug("[llama-server] Model loading started");
                LoadingProgressChanged?.Invoke(this, new ServerLoadingProgressEventArgs
                {
                    ProgressPercent = 15,
                    Message = "Loading model tensors..."
                });
            }
        }
        else if (_loadingStarted && _loadingDotCount > 5 && line.StartsWith("common_init_result", StringComparison.OrdinalIgnoreCase))
        {
            _loadingComplete = true;
            _logger.LogDebug("[llama-server] Model loading complete");
            LoadingProgressChanged?.Invoke(this, new ServerLoadingProgressEventArgs
            {
                ProgressPercent = 80,
                Message = "Model loaded, starting health check..."
            });
        }
    }
}

/// <summary>
/// Event args for llama-server loading progress.
/// </summary>
public sealed class ServerLoadingProgressEventArgs : EventArgs
{
    /// <summary>Progress percentage (0-100).</summary>
    public double ProgressPercent { get; init; }

    /// <summary>Optional status message.</summary>
    public string? Message { get; init; }
}

/// <summary>
/// Event args for llama-server error output.
/// </summary>
public sealed class ServerErrorEventArgs(string errorMessage) : EventArgs
{
    /// <summary>The error message line.</summary>
    public string ErrorMessage { get; } = errorMessage;
}
