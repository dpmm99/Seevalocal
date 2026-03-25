using Microsoft.Extensions.Logging;
using Seevalocal.Core.Models;
using Seevalocal.Core.Pipeline;
using Seevalocal.Core.Pipeline.Stages;
using Seevalocal.Judge;

namespace Seevalocal.Pipelines.Factories;

/// <summary>
/// Builds a pipeline that evaluates language translation quality.
/// Stages: PromptStage (with translation system prompt) → JudgeStage
/// 
/// The PromptStage uses a system prompt that instructs the model to translate
/// from sourceLanguage to targetLanguage. The JudgeStage then evaluates the
/// translation quality using the translation-specific judge template.
/// </summary>
public sealed class TranslationPipelineFactory(ILoggerFactory loggerFactory) : IBuiltinPipelineFactory
{
    public string PipelineName => "Translation";
    public string Description => "Evaluates language translation quality using LLM-as-judge for accuracy scoring.";

    private readonly ILoggerFactory _loggerFactory = loggerFactory;

    public DataSourceConfig DefaultDataSourceConfig => new()
    {
        Kind = DataSourceKind.SplitDirectories,
        PromptDirectory = "./data/source",
        ExpectedDirectory = "./data/reference",
        FilePattern = "*.txt",
    };

    public IReadOnlyList<ValidationError> Validate(ResolvedConfig config)
    {
        List<ValidationError> errors = [];
        var opts = config.PipelineOptions;

        if (opts is not null)
        {
            if (opts.TryGetValue("sourceLanguage", out var src) && src is not string)
                errors.Add(new ValidationError("pipelineOptions.sourceLanguage", "Must be a string value."));
            if (opts.TryGetValue("targetLanguage", out var tgt) && tgt is not string)
                errors.Add(new ValidationError("pipelineOptions.targetLanguage", "Must be a string value."));
        }

        return errors;
    }

    public EvalPipeline Create(ResolvedConfig resolvedConfig)
    {
        // Note: System prompt is now handled by the data source via DataSourceConfig.DefaultSystemPrompt
        // The data source will use per-item SystemPromptField if provided, otherwise fall back to
        // DefaultSystemPrompt which is generated from source/target language in the Wizard.

        var promptStage = new PromptStage(_loggerFactory.CreateLogger<PromptStage>())
        {
            MaxTokens = null,
            StopSequences = [],
            // SystemPrompt is populated per-item by the data source
        };

        // Create JudgeStage using JudgeConfig from resolved config
        // If no judge config exists, create a default one with pipeline-specific template
        var judgeConfig = resolvedConfig.Judge ?? new JudgeConfig
        {
            JudgePromptTemplate = "translation",
        };

        var judgeStage = new JudgeStage(
            judgeConfig,
            _loggerFactory.CreateLogger<JudgeStage>(),
            _loggerFactory.CreateLogger<JudgePromptRenderer>(),
            _loggerFactory.CreateLogger<JudgeResponseParser>());

        return new EvalPipeline(_loggerFactory.CreateLogger<EvalPipeline>())
        {
            PipelineName = PipelineName,
            Stages = [new ItemLoadStage(_loggerFactory.CreateLogger<ItemLoadStage>()), promptStage, judgeStage],
        };
    }

    /// <summary>
    /// Checks that the judge endpoint is configured and that the data directories exist.
    /// </summary>
    public static FluentResults.Result EnsurePrerequisites(
        ResolvedConfig resolvedConfig)
    {
        List<string> errors = [];

        if (resolvedConfig.Judge is null)
            errors.Add("[TranslationPipelineFactory] Judge endpoint is not configured. Set 'judge' in settings.");

        var sourceDir = resolvedConfig.DataSource?.PromptDirectory ?? "./data/source";
        var refDir = resolvedConfig.DataSource?.ExpectedDirectory ?? "./data/reference";

        if (!Directory.Exists(sourceDir))
            errors.Add($"[TranslationPipelineFactory] Source directory not found: {Path.GetFullPath(sourceDir)}");
        if (!Directory.Exists(refDir))
            errors.Add($"[TranslationPipelineFactory] Reference directory not found: {Path.GetFullPath(refDir)}");

        return errors.Count > 0 ? FluentResults.Result.Fail(errors) : FluentResults.Result.Ok();
    }
}
