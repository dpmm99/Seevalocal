using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace Seevalocal.UI.Commands;

/// <summary>
/// Settings for the 'eval-gen' command.
/// </summary>
public sealed class EvalGenCommandSettings : CommandSettings
{
    [Description("Output directory for the generated eval set (SplitDirectories format).")]
    [CommandArgument(0, "[OUTPUT_DIR]")]
    public string? OutputDirectory { get; init; }

    [Description("Domain prompt describing the focus area for the evaluation set.")]
    [CommandOption("-d|--domain <DOMAIN>")]
    public string? DomainPrompt { get; init; }

    [Description("Additional context or constraints for problem generation.")]
    [CommandOption("-c|--context <CONTEXT>")]
    public string? ContextPrompt { get; init; }

    [Description("System prompt to guide the LLM's role. Optional.")]
    [CommandOption("-s|--system <SYSTEM>")]
    public string? SystemPrompt { get; init; }

    [Description("Target number of categories to generate. Default: 10")]
    [CommandOption("--categories <COUNT>")]
    [DefaultValue(10)]
    public int TargetCategoryCount { get; init; } = 10;

    [Description("Target number of problems per category. Default: 5")]
    [CommandOption("--problems-per-category <COUNT>")]
    [DefaultValue(5)]
    public int TargetProblemsPerCategory { get; init; } = 5;

    [Description("Judge LLM base URL. Default: http://localhost:8081")]
    [CommandOption("--judge-url <URL>")]
    public string? JudgeUrl { get; init; }

    [Description("Checkpoint database path for resuming a cancelled/failed run.")]
    [CommandOption("--checkpoint <PATH>")]
    public string? CheckpointDatabasePath { get; init; }

    [Description("Continue from a previous checkpoint.")]
    [CommandOption("--continue")]
    public bool ContinueFromCheckpoint { get; init; }

    [Description("Run name for this generation.")]
    [CommandOption("-n|--name <NAME>")]
    public string? RunName { get; init; }

    public override ValidationResult Validate()
    {
        return string.IsNullOrEmpty(OutputDirectory) && !ContinueFromCheckpoint
            ? ValidationResult.Error("Output directory is required unless continuing from checkpoint.")
            : TargetCategoryCount is < 1 or > 100
            ? ValidationResult.Error("Target category count must be between 1 and 100.")
            : TargetProblemsPerCategory is < 1 or > 50
            ? ValidationResult.Error("Target problems per category must be between 1 and 50.")
            : ValidationResult.Success();
    }
}
