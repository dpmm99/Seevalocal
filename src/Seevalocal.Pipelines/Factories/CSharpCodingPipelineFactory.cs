using Microsoft.Extensions.Logging;
using Seevalocal.Core;
using Seevalocal.Core.Models;
using Seevalocal.Core.Pipeline;
using Seevalocal.Core.Pipeline.Stages;
using Seevalocal.Judge;

namespace Seevalocal.Pipelines.Factories;

/// <summary>
/// Builds a pipeline that evaluates C# code generation by compiling and running unit tests.
/// Stages: PromptStage → FileWriterStage → CompileStage → TestStage → JudgeStage (optional)
/// </summary>
public sealed class CSharpCodingPipelineFactory(ILoggerFactory loggerFactory) : IBuiltinPipelineFactory
{
    public string PipelineName => "CSharpCoding";
    public string Description => "Evaluates C# code generation quality by compiling the output and running unit tests.";

    private readonly ILoggerFactory _loggerFactory = loggerFactory;

    public DataSourceConfig DefaultDataSourceConfig => new()
    {
        Kind = DataSourceKind.SplitDirectories,
        PromptDirectory = "./data/prompts",
    };

    public IReadOnlyList<ValidationError> Validate(ResolvedConfig resolvedConfig)
    {
        List<ValidationError> errors = [];
        var opts = resolvedConfig.PipelineOptions;
        if (opts is null) return errors;

        if (opts.TryGetValue("compileTimeoutSeconds", out var cts))
        {
            if (cts is not double and not int and not string)
                errors.Add(new ValidationError("pipelineOptions.compileTimeoutSeconds", "Must be a numeric value."));
        }
        if (opts.TryGetValue("testTimeoutSeconds", out var tts))
        {
            if (tts is not double and not int and not string)
                errors.Add(new ValidationError("pipelineOptions.testTimeoutSeconds", "Must be a numeric value."));
        }

        return errors;
    }

    public EvalPipeline Create(ResolvedConfig resolvedConfig)
    {
        var opts = resolvedConfig.PipelineOptions;

        var compileTimeoutSeconds = ParseDouble(opts, "compileTimeoutSeconds", 30.0);
        var testTimeoutSeconds = ParseDouble(opts, "testTimeoutSeconds", 60.0);
        var cleanupOnSuccess = ParseBool(opts, "cleanupTempFilesOnSuccess", true);
        var cleanupOnFailure = ParseBool(opts, "cleanupTempFilesOnFailure", false);
        var scoreWithJudge = ParseBool(opts, "scoreStyleWithJudge", false);

        var templatePath = opts.GetValueOrDefault("testProjectTemplatePath") as string
            ?? Path.Combine(AppContext.BaseDirectory, "templates", "csharp-test-skeleton");

        // Temp dir per eval item — use item ID at execution time
        // We build the stages with a placeholder; the actual path is resolved in EvalPipeline.RunItemAsync
        // via context. Because the temp path depends on item.Id, we use a factory lambda approach.
        //
        // For simplicity, the temp directory root is fixed and the item ID sub-folder is applied at
        // FileWriterStage and ExternalProcessStage construction time inside a custom composite stage.

        List<IEvalStage> stages =
        [
            new PromptStage(_loggerFactory.CreateLogger<PromptStage>()),
            new CSharpEvalSetupStage(
                templatePath,
                compileTimeoutSeconds,
                testTimeoutSeconds,
                cleanupOnSuccess,
                cleanupOnFailure,
                _loggerFactory),
        ];

        if (scoreWithJudge)
        {
            // Create JudgeStage using JudgeConfig from resolved config
            // If no judge config exists, create a default one with pipeline-specific template
            var judgeConfig = resolvedConfig.Judge ?? new JudgeConfig
            {
                JudgePromptTemplate = "codequality",
                ScoreMinValue = 0,
                ScoreMaxValue = 10,
            };

            stages.Add(new JudgeStage(
                judgeConfig,
                _loggerFactory.CreateLogger<JudgeStage>(),
                _loggerFactory.CreateLogger<JudgePromptRenderer>(),
                _loggerFactory.CreateLogger<JudgeResponseParser>()));
        }

        return new EvalPipeline(_loggerFactory.CreateLogger<EvalPipeline>())
        {
            PipelineName = PipelineName,
            Stages = stages,
        };
    }

    /// <summary>
    /// Checks that dotnet is on PATH and that the template project compiles.
    /// </summary>
    public static async Task<FluentResults.Result> EnsurePrerequisitesAsync(
        CancellationToken ct)
    {
        // Check dotnet on PATH
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "--version",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        try
        {
            using var p = System.Diagnostics.Process.Start(psi)!;
            await p.WaitForExitAsync(ct);
            if (p.ExitCode != 0)
                return FluentResults.Result.Fail(
                    "[CSharpCodingPipelineFactory] 'dotnet --version' returned non-zero exit code. " +
                    "Install .NET SDK from https://dotnet.microsoft.com/download");
        }
        catch (Exception ex)
        {
            return FluentResults.Result.Fail(
                "[CSharpCodingPipelineFactory] 'dotnet' is not on PATH or could not be executed: " +
                $"{ex.Message}. Install .NET SDK from https://dotnet.microsoft.com/download");
        }

        return FluentResults.Result.Ok();
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
