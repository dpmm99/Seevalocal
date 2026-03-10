using Microsoft.Extensions.Logging;
using Seevalocal.Core;
using Seevalocal.Core.Models;
using Seevalocal.Core.Pipeline;
using Seevalocal.Core.Pipeline.Stages;
using Seevalocal.Judge;

namespace Seevalocal.Pipelines.Factories;

/// <summary>
/// Builds a pipeline that evaluates language translation quality.
/// Stages: PromptStage → JudgeStage
/// </summary>
public sealed class TranslationPipelineFactory(ILoggerFactory loggerFactory) : IBuiltinPipelineFactory
{
    public string PipelineName => "Translation";
    public string Description => "Evaluates language translation quality using LLM-as-judge for accuracy scoring.";

    private readonly ILoggerFactory _loggerFactory = loggerFactory;

    public DataSourceConfig DefaultDataSourceConfig => new()
    {
        Kind = DataSourceKind.SplitDirectories,
        PromptDirectoryPath = "./data/source",
        ExpectedOutputDirectoryPath = "./data/reference",
        FilePattern = "*.txt",
    };

    public IReadOnlyList<ValidationError> Validate(EvalSetConfig evalSetConfig)
    {
        List<ValidationError> errors = [];
        var opts = evalSetConfig.PipelineOptions;

        if (opts is not null)
        {
            if (opts.TryGetValue("sourceLanguage", out var src) && src is not string)
                errors.Add(new ValidationError("pipelineOptions.sourceLanguage", "Must be a string value."));
            if (opts.TryGetValue("targetLanguage", out var tgt) && tgt is not string)
                errors.Add(new ValidationError("pipelineOptions.targetLanguage", "Must be a string value."));
        }

        return errors;
    }

    public EvalPipeline Create(EvalSetConfig evalSetConfig, ResolvedConfig resolvedConfig)
    {
        var opts = evalSetConfig.PipelineOptions;
        var sourceLanguage = opts?.GetValueOrDefault("sourceLanguage") as string ?? "English";
        var targetLanguage = opts?.GetValueOrDefault("targetLanguage") as string ?? "French";
        _ =
            "You are a professional translator. " +
            $"Translate the following text from {sourceLanguage} to {targetLanguage} accurately and naturally. " +
            "Output only the translation, with no explanation or preamble.";

        var promptStage = new PromptStage(_loggerFactory.CreateLogger<PromptStage>())
        {
            MaxTokens = null,
            StopSequences = [],
        };

        // For translation, we use a system prompt via the item's SystemPrompt field
        // The pipeline expects items to have SystemPrompt set, or we can inject it via data source

        // Create JudgeStage using JudgeConfig from resolved config
        // If no judge config exists, create a default one with pipeline-specific template
        var judgeConfig = resolvedConfig.Judge ?? new JudgeConfig
        {
            JudgePromptTemplate = "translation",
            ScoreMinValue = 0,
            ScoreMaxValue = 10,
        };

        var judgeStage = new JudgeStage(
            judgeConfig,
            _loggerFactory.CreateLogger<JudgeStage>(),
            _loggerFactory.CreateLogger<JudgePromptRenderer>(),
            _loggerFactory.CreateLogger<JudgeResponseParser>());

        return new EvalPipeline(_loggerFactory.CreateLogger<EvalPipeline>())
        {
            PipelineName = PipelineName,
            Stages = [promptStage, judgeStage],
        };
    }

    /// <summary>
    /// Checks that the judge endpoint is configured and that the data directories exist.
    /// </summary>
    public static FluentResults.Result EnsurePrerequisites(
        EvalSetConfig evalSetConfig,
        ResolvedConfig resolvedConfig)
    {
        List<string> errors = [];

        if (resolvedConfig.Judge is null)
            errors.Add("[TranslationPipelineFactory] Judge endpoint is not configured. Set 'judge' in settings.");

        var sourceDir = evalSetConfig.DataSource?.PromptDirectoryPath ?? "./data/source";
        var refDir = evalSetConfig.DataSource?.ExpectedOutputDirectoryPath ?? "./data/reference";

        if (!Directory.Exists(sourceDir))
            errors.Add($"[TranslationPipelineFactory] Source directory not found: {Path.GetFullPath(sourceDir)}");
        if (!Directory.Exists(refDir))
            errors.Add($"[TranslationPipelineFactory] Reference directory not found: {Path.GetFullPath(refDir)}");

        return errors.Count > 0 ? FluentResults.Result.Fail(errors) : FluentResults.Result.Ok();
    }
}
