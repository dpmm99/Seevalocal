using Seevalocal.Core.Models;
using Seevalocal.Server;

namespace Seevalocal.UI.Services;

/// <summary>
/// Service for agentic evaluation set generation.
/// Generates eval sets through a 3-phase process:
/// 1. Generate non-overlapping categories using the judge LLM
/// 2. Generate one-line problem statements for each category
/// 3. Flesh out problems into full prompt-response pairs
/// </summary>
public interface IEvalGenService
{
    /// <summary>
    /// Start an eval generation run.
    /// Returns immediately; progress is reported via the returned task.
    /// </summary>
    Task<EvalGenRun> GenerateAsync(
        EvalGenConfig config,
        JudgeConfig? judgeConfig,
        CancellationToken cancellationToken);

    /// <summary>
    /// Check if an eval generation run is currently active.
    /// </summary>
    bool IsGenerationActive { get; }

    /// <summary>
    /// Get the current eval generation run, if any.
    /// </summary>
    EvalGenRun? CurrentRun { get; }
}

/// <summary>
/// Represents an active or completed eval generation run.
/// Provides progress tracking and control methods.
/// </summary>
/// <remarks>
/// Constructs an EvalGenRun without starting execution.
/// Call <see cref="Start"/> to begin execution.
/// </remarks>
public sealed class EvalGenRun(EvalGenConfig config, Func<CancellationToken, Task> executionFunc)
{
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    private Task? _executionTask;
    private bool _isPaused;
    private readonly object _pauseLock = new();
    private TaskCompletionSource? _pauseResumeTcs;

    public string Id { get; } = config.Id;
    public string RunName { get; } = config.RunName;
    public EvalGenConfig Config { get; } = config;
    public string CheckpointDatabasePath { get; set; } = "";
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;
    public EvalGenCheckpointCollector? Collector { get; set; }

    /// <summary>
    /// Gets or sets the judge server manager used by this run.
    /// This is disposed when the run completes or is cancelled.
    /// </summary>
    public LlamaServerManager? JudgeServerManager { get; set; }

    public EvalGenProgress Progress { get; private set; } = new()
    {
        CurrentPhase = EvalGenPhase.GeneratingCategories,
        CategoriesGenerated = 0,
        TargetCategories = 0,
        ProblemsGenerated = 0,
        TargetProblems = 0,
        ProblemsFleshedOut = 0,
        StatusMessage = "Initializing"
    };

    public bool IsRunning { get; private set; }
    public bool IsCompleted { get; private set; }
    public bool IsCancelled { get; private set; }
    public bool IsPaused
    {
        get
        {
            lock (_pauseLock)
                return _isPaused;
        }
        private set
        {
            lock (_pauseLock)
                _isPaused = value;
        }
    }

    public string? Error { get; private set; }
    public int TotalCategoriesFailed { get; set; }
    public int TotalProblemsFailed { get; set; }
    public int TotalFleshOutFailed { get; set; }
    
    /// <summary>
    /// Total tokens used across all LLM calls.
    /// </summary>
    public int TotalTokensUsed { get; set; }
    
    /// <summary>
    /// Average tokens per second across all LLM calls.
    /// </summary>
    public double AverageTokensPerSecond { get; set; }

    public event Action<EvalGenProgress>? ProgressChanged;
    public event Action? RunCompleted;

    /// <summary>
    /// Starts the execution task. Must be called after construction.
    /// </summary>
    public void Start()
    {
        if (_executionTask != null)
            throw new InvalidOperationException("Execution already started");

        IsRunning = true;
        _executionTask = executionFunc(_cts.Token).ContinueWith(t =>
        {
            IsRunning = false;
            if (t.IsFaulted)
            {
                Error = t.Exception?.GetBaseException().Message ?? "Unknown error";
            }
            else if (t.IsCanceled)
            {
                IsCancelled = true;
            }
            IsCompleted = true;
            RunCompleted?.Invoke();
        }, TaskScheduler.Default);
    }

    public void UpdateProgress(EvalGenProgress progress)
    {
        Progress = progress;
        ProgressChanged?.Invoke(progress);
    }

    public void Pause()
    {
        if (IsPaused || !IsRunning)
            return;

        lock (_pauseLock)
        {
            _pauseResumeTcs = new TaskCompletionSource();
            IsPaused = true;
        }
    }

    public void Resume()
    {
        if (!IsPaused)
            return;

        lock (_pauseLock)
        {
            _pauseResumeTcs?.SetResult();
            _pauseResumeTcs = null;
            IsPaused = false;
        }
    }

    public void Cancel()
    {
        _cts.Cancel();
    }

    /// <summary>
    /// Wait for pause to be resumed. Call this at pause points in the generation logic.
    /// </summary>
    public async Task WaitForResumeAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource? tcs;
        lock (_pauseLock)
        {
            if (!IsPaused)
                return;
            tcs = _pauseResumeTcs;
        }

        if (tcs != null)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            await tcs.Task.WaitAsync(linkedCts.Token);
        }
    }

    public CancellationToken CancellationToken => _cts.Token;

    public async Task WaitAsync()
    {
        if (_executionTask == null)
            throw new InvalidOperationException("Execution not started");
        await _executionTask;
    }
}
