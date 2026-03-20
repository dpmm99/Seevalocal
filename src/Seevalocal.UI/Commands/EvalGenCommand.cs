using Microsoft.Extensions.Logging;
using Seevalocal.Core.Models;
using Seevalocal.UI.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Seevalocal.UI.Commands;

/// <summary>
/// Implements the 'eval-gen' command: generates an evaluation set agentically.
/// Uses the configured judge LLM to generate categories and problems through a 3-phase process.
/// </summary>
public sealed class EvalGenCommand(
    ILogger<EvalGenCommand> logger,
    IAnsiConsole console,
    IEvalGenService evalGenService) : AsyncCommand<EvalGenCommandSettings>
{
    private readonly ILogger<EvalGenCommand> _logger = logger;
    private readonly IAnsiConsole _console = console;

    public override Task<int> ExecuteAsync(CommandContext context, EvalGenCommandSettings settings, CancellationToken cancellationToken)
    {
        return ExecuteAsyncInternal(settings, cancellationToken);
    }

    private async Task<int> ExecuteAsyncInternal(EvalGenCommandSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            _console.MarkupLine("[bold cyan]🚀 Starting Evaluation Set Generation[/]");
            _console.WriteLine();

            // Build config
            var config = new EvalGenConfig
            {
                Id = Guid.NewGuid().ToString(),
                RunName = settings.RunName ?? $"eval_gen_{DateTime.Now:yyyyMMdd_HHmmss}",
                OutputDirectoryPath = settings.OutputDirectory ?? Directory.GetCurrentDirectory(),
                TargetCategoryCount = settings.TargetCategoryCount,
                TargetProblemsPerCategory = settings.TargetProblemsPerCategory,
                DomainPrompt = settings.DomainPrompt ?? "",
                ContextPrompt = settings.ContextPrompt ?? "",
                SystemPrompt = settings.SystemPrompt ?? "",
                ContinueFromCheckpoint = settings.ContinueFromCheckpoint,
                CheckpointDatabasePath = settings.CheckpointDatabasePath
            };

            // For CLI, judge config comes from settings files or defaults
            // CLI doesn't support managed judge server for eval-gen (use UI for that)
            JudgeConfig? judgeConfig = null;

            if (settings.ContinueFromCheckpoint && !string.IsNullOrEmpty(settings.CheckpointDatabasePath))
            {
                _console.MarkupLine($"[yellow]Continuing from checkpoint: {settings.CheckpointDatabasePath}[/]");
            }
            else
            {
                _console.MarkupLine($"[green]Domain:[/] {config.DomainPrompt}");
                _console.MarkupLine($"[green]Target Categories:[/] {config.TargetCategoryCount}");
                _console.MarkupLine($"[green]Problems per Category:[/] {config.TargetProblemsPerCategory}");
                _console.MarkupLine($"[green]Output Directory:[/] {config.OutputDirectoryPath}");
            }

            _console.WriteLine();

            // Start the generation
            var run = await evalGenService.GenerateAsync(config, judgeConfig, cancellationToken);

            // Subscribe to progress updates
            var progressTask = DisplayProgressAsync(run, cancellationToken);

            // Wait for completion
            await run.WaitAsync();
            await progressTask;

            // Display results
            if (run.IsCancelled)
            {
                _console.MarkupLine("[yellow]⚠️ Generation cancelled[/]");
                _console.MarkupLine($"Checkpoint saved to: [cyan]{config.CheckpointDatabasePath ?? Path.Combine(config.OutputDirectoryPath, $"{config.RunName}_checkpoint.db")}[/]");
                _console.MarkupLine("Resume with: [cyan]seevalocal eval-gen --continue --checkpoint <path>[/]");
                return 1;
            }

            if (!string.IsNullOrEmpty(run.Error))
            {
                _console.MarkupLine($"[red]❌ Error: {run.Error}[/]");
                return 1;
            }

            _console.WriteLine();
            _console.MarkupLine("[bold green]✅ Generation Complete![/]");
            _console.MarkupLine($"[green]Output Directory:[/] {config.OutputDirectoryPath}");
            _console.MarkupLine($"[green]Categories Generated:[/] {run.Progress.CategoriesGenerated}");
            _console.MarkupLine($"[green]Problems Generated:[/] {run.Progress.ProblemsGenerated}");
            _console.MarkupLine($"[green]Problems Fleshed Out:[/] {run.Progress.ProblemsFleshedOut}");
            _console.WriteLine();
            _console.MarkupLine("[cyan]Results saved in SplitDirectories format:[/]");
            _console.MarkupLine($"  [cyan]{Path.Combine(config.OutputDirectoryPath, "prompts")}[/] - Prompt files");
            _console.MarkupLine($"  [cyan]{Path.Combine(config.OutputDirectoryPath, "expected_outputs")}[/] - Expected output files");

            return 0;
        }
        catch (OperationCanceledException)
        {
            _console.MarkupLine("[yellow]⚠️ Generation cancelled[/]");
            return 1;
        }
        catch (Exception ex)
        {
            _console.MarkupLine($"[red]❌ Error: {ex.Message}[/]");
            _logger.LogError(ex, "Error during eval generation");
            return 1;
        }
    }

    private async Task DisplayProgressAsync(EvalGenRun run, CancellationToken cancellationToken)
    {
        var lastPhase = EvalGenPhase.GeneratingCategories;
        var lastStatus = "";

        while (!run.IsCompleted && !cancellationToken.IsCancellationRequested)
        {
            // Update phase indicator
            if (run.Progress.CurrentPhase != lastPhase)
            {
                lastPhase = run.Progress.CurrentPhase;
                var phaseText = lastPhase switch
                {
                    EvalGenPhase.GeneratingCategories => "📁 Generating Categories",
                    EvalGenPhase.GeneratingProblems => "📝 Generating Problems",
                    EvalGenPhase.FleshingOutProblems => "✍️ Fleshing Out Problems",
                    EvalGenPhase.Completed => "✅ Complete",
                    _ => "Processing"
                };
                _console.MarkupLine($"[cyan]{phaseText}[/]");
            }

            // Display current status (only if changed to avoid spam)
            if (run.Progress.StatusMessage != lastStatus)
            {
                lastStatus = run.Progress.StatusMessage;
                _console.Markup($"[dim]{lastStatus}[/]");
            }

            await Task.Delay(500, cancellationToken);
        }
    }
}
