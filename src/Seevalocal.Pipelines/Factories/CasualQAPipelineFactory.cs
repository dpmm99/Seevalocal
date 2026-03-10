using Microsoft.Extensions.Logging;
using Seevalocal.Core;
using Seevalocal.Core.Models;
using Seevalocal.Core.Pipeline;
using Seevalocal.Core.Pipeline.Stages;
using Seevalocal.Judge;

namespace Seevalocal.Pipelines.Factories;

/// <summary>
/// Builds a pipeline that evaluates casual conversational Q&amp;A via semantic judge scoring.
/// Stages: PromptStage → ExactMatchStage (optional) → JudgeStage
/// </summary>
public sealed class CasualQAPipelineFactory(ILoggerFactory loggerFactory) : IBuiltinPipelineFactory
{
    public string PipelineName => "CasualQA";
    public string Description =>
        "Evaluates casual conversational Q&A — open-ended questions scored by LLM-as-judge for semantic accuracy.";

    private readonly ILoggerFactory _loggerFactory = loggerFactory;

    public DataSourceConfig DefaultDataSourceConfig => new()
    {
        Kind = DataSourceKind.JsonFile,
        FilePath = "./data/qa.json",
        FieldMapping = new FieldMapping
        {
            IdField = "id",
            UserPromptField = "question",
            ExpectedOutputField = "answer",
        },
    };

    public IReadOnlyList<ValidationError> Validate(EvalSetConfig evalSetConfig)
    {
        List<ValidationError> errors = [];
        var opts = evalSetConfig.PipelineOptions;
        if (opts is null) return errors;

        if (opts.TryGetValue("judgeMaxScore", out var maxScore))
        {
            if (maxScore is not int and not double and not string)
                errors.Add(new ValidationError("pipelineOptions.judgeMaxScore", "Must be a numeric value."));
        }
        if (opts.TryGetValue("judgePassThresholdRatio", out var thr))
        {
            if (thr is not double and not int and not string)
                errors.Add(new ValidationError("pipelineOptions.judgePassThresholdRatio", "Must be a numeric value."));
        }

        return errors;
    }

    public EvalPipeline Create(EvalSetConfig evalSetConfig, ResolvedConfig resolvedConfig)
    {
        var opts = evalSetConfig.PipelineOptions ?? new Dictionary<string, object?>();

        var enableExactMatch = ParseBool(opts, "enableExactMatch", false);

        List<IEvalStage> stages =
        [
            new PromptStage(_loggerFactory.CreateLogger<PromptStage>()),
        ];

        if (enableExactMatch)
            stages.Add(new ExactMatchStage(_loggerFactory.CreateLogger<ExactMatchStage>()));

        // Create JudgeStage using JudgeConfig from resolved config
        // If no judge config exists, create a default one with pipeline-specific template
        var judgeConfig = resolvedConfig.Judge ?? new JudgeConfig
        {
            JudgePromptTemplate = "casualqa",
            ScoreMinValue = 0,
            ScoreMaxValue = 10,
        };
        
        stages.Add(new JudgeStage(
            judgeConfig,
            _loggerFactory.CreateLogger<JudgeStage>(),
            _loggerFactory.CreateLogger<JudgePromptRenderer>(),
            _loggerFactory.CreateLogger<JudgeResponseParser>()));

        return new EvalPipeline(_loggerFactory.CreateLogger<EvalPipeline>())
        {
            PipelineName = PipelineName,
            Stages = stages,
        };
    }

    private static bool ParseBool(IReadOnlyDictionary<string, object?> opts, string key, bool fallback)
    {
        return !opts.TryGetValue(key, out var raw) || raw is null
            ? fallback
            : raw switch
            {
                bool b => b,
                string s => bool.TryParse(s, out var p) ? p : fallback,
                _ => fallback,
            };
    }
}
