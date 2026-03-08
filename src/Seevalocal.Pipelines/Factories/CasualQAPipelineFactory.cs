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
        var judgeMaxScore = ParseInt(opts, "judgeMaxScore", 10);
        var passThresholdRatio = ParseDouble(opts, "judgePassThresholdRatio", 0.6);

        List<IEvalStage> stages =
        [
            new PromptStage(_loggerFactory.CreateLogger<PromptStage>()),
        ];

        if (enableExactMatch)
            stages.Add(new ExactMatchStage(_loggerFactory.CreateLogger<ExactMatchStage>()));

        stages.Add(new JudgeStage(
            _loggerFactory.CreateLogger<JudgeStage>(),
            promptTemplate: DefaultTemplates.CasualQAJudgeTemplate,
            maxScore: judgeMaxScore,
            passThresholdRatio: passThresholdRatio));

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

    private static int ParseInt(IReadOnlyDictionary<string, object?> opts, string key, int fallback)
    {
        return !opts.TryGetValue(key, out var raw) || raw is null
            ? fallback
            : raw switch
            {
                int i => i,
                double d => (int)d,
                string s when int.TryParse(s, out var p) => p,
                _ => fallback,
            };
    }

    private static double ParseDouble(IReadOnlyDictionary<string, object?> opts, string key, double fallback)
    {
        return !opts.TryGetValue(key, out var raw) || raw is null
            ? fallback
            : raw switch
            {
                double d => d,
                int i => i,
                string s when double.TryParse(s, out var p) => p,
                _ => fallback,
            };
    }
}
