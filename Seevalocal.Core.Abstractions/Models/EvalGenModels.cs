namespace Seevalocal.Core.Models;

/// <summary>
/// Configuration for agentic evaluation set generation.
/// </summary>
public record EvalGenConfig
{
    /// <summary>
    /// Unique identifier for this eval generation run.
    /// Used for checkpoint resumption.
    /// Default: new GUID (for new runs).
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Name for this eval generation run.
    /// </summary>
    public string RunName { get; init; } = "";

    /// <summary>
    /// Output directory for the generated eval set.
    /// Will contain prompts/ and expected_outputs/ subdirectories (SplitDirectories format).
    /// </summary>
    public string OutputDirectoryPath { get; init; } = "";

    /// <summary>
    /// Target number of categories to generate.
    /// </summary>
    public int TargetCategoryCount { get; init; } = 10;

    /// <summary>
    /// Target number of problems per category.
    /// </summary>
    public int TargetProblemsPerCategory { get; init; } = 5;

    /// <summary>
    /// Primary prompt describing the domain/focus of the eval set.
    /// This is the main input for category generation.
    /// </summary>
    public string? DomainPrompt { get; init; }

    /// <summary>
    /// Additional context or constraints for problem generation.
    /// E.g., "Focus on edge cases" or "Include multi-step reasoning problems".
    /// </summary>
    public string? ContextPrompt { get; init; }

    /// <summary>
    /// Optional system prompt to guide the LLM's role during generation.
    /// If not provided, a default system prompt will be used.
    /// </summary>
    public string? SystemPrompt { get; init; }

    /// <summary>
    /// Whether to continue from a previous checkpoint.
    /// </summary>
    public bool ContinueFromCheckpoint { get; init; }

    /// <summary>
    /// Path to the checkpoint database for resumption.
    /// </summary>
    public string? CheckpointDatabasePath { get; init; }

    /// <summary>
    /// Custom prompt template for Phase 1 (category generation).
    /// Use tags like {TargetCategoryCount}, {DomainPrompt}, {ExistingCategoriesSection}, {ContextPromptSection}.
    /// </summary>
    public string? Phase1PromptTemplate { get; init; }

    /// <summary>
    /// Custom prompt template for Phase 2 (problem generation).
    /// Use tags like {CategoryName}, {TargetProblemsPerCategory}, {DomainPrompt}, {ExistingProblemsSection}, {ContextPromptSection}.
    /// </summary>
    public string? Phase2PromptTemplate { get; init; }

    /// <summary>
    /// Custom prompt template for Phase 3 (flesh-out).
    /// Use tags like {OneLineStatement}, {DomainPrompt}, {ContextPromptSection}.
    /// </summary>
    public string? Phase3PromptTemplate { get; init; }
}

/// <summary>
/// Represents a generated category with its problems.
/// </summary>
public record GeneratedCategory
{
    /// <summary>
    /// Unique identifier for this category.
    /// </summary>
    public string Id { get; init; } = "";

    /// <summary>
    /// The category name/description.
    /// </summary>
    public string Name { get; init; } = "";

    /// <summary>
    /// The one-line problem statements for this category.
    /// </summary>
    public List<GeneratedProblem> Problems { get; set; } = [];
}

/// <summary>
/// Represents a generated problem at different stages of completion.
/// </summary>
public record GeneratedProblem
{
    /// <summary>
    /// Unique identifier for this problem.
    /// </summary>
    public string Id { get; init; } = "";

    /// <summary>
    /// The category this problem belongs to.
    /// </summary>
    public string CategoryId { get; init; } = "";

    /// <summary>
    /// The one-line problem statement (Phase 2 output).
    /// </summary>
    public string OneLineStatement { get; init; } = "";

    /// <summary>
    /// The fully fleshed-out prompt (Phase 3 output).
    /// </summary>
    public string? FullPrompt { get; init; }

    /// <summary>
    /// The expected output/response for this problem (Phase 3 output).
    /// </summary>
    public string? ExpectedOutput { get; init; }

    /// <summary>
    /// Whether this problem is fully complete (has FullPrompt and ExpectedOutput).
    /// </summary>
    public bool IsComplete => !string.IsNullOrEmpty(FullPrompt) && !string.IsNullOrEmpty(ExpectedOutput);
}

/// <summary>
/// Represents the current phase of eval generation.
/// </summary>
public enum EvalGenPhase
{
    /// <summary>
    /// Phase 1: Generating categories.
    /// </summary>
    GeneratingCategories,

    /// <summary>
    /// Phase 2: Generating one-line problem statements per category.
    /// </summary>
    GeneratingProblems,

    /// <summary>
    /// Phase 3: Fleshing out problems into full prompt-response pairs.
    /// </summary>
    FleshingOutProblems,

    /// <summary>
    /// All phases complete.
    /// </summary>
    Completed
}

/// <summary>
/// Progress information for eval generation.
/// </summary>
public record EvalGenProgress
{
    /// <summary>
    /// Current phase of generation.
    /// </summary>
    public EvalGenPhase CurrentPhase { get; init; }

    /// <summary>
    /// Categories generated so far.
    /// </summary>
    public int CategoriesGenerated { get; init; }

    /// <summary>
    /// Target number of categories.
    /// </summary>
    public int TargetCategories { get; init; }

    /// <summary>
    /// Problems generated so far (across all categories).
    /// </summary>
    public int ProblemsGenerated { get; init; }

    /// <summary>
    /// Target number of problems (categories * problems per category).
    /// </summary>
    public int TargetProblems { get; init; }

    /// <summary>
    /// Problems fully fleshed out so far.
    /// </summary>
    public int ProblemsFleshedOut { get; init; }

    /// <summary>
    /// Overall progress percentage (0-100).
    /// </summary>
    public double OverallProgressPercent
    {
        get
        {
            // Weight phases: Categories 5%, Problems 15%, Flesh-out 80%
            double categoryProgress = TargetCategories > 0 ? Math.Min(1, (double)CategoriesGenerated / TargetCategories) : 0;
            double problemProgress = TargetProblems > 0 ? Math.Min(1, (double)ProblemsGenerated / TargetProblems) : 0;
            double fleshOutProgress = TargetProblems > 0 ? Math.Min(1, (double)ProblemsFleshedOut / TargetProblems) : 0;

            return (categoryProgress * 0.05 + problemProgress * 0.15 + fleshOutProgress * 0.8) * 100;
        }
    }

    /// <summary>
    /// Current status message.
    /// </summary>
    public string StatusMessage { get; init; } = "";
}
