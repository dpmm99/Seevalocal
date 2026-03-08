using Microsoft.Extensions.Logging;
using Seevalocal.Core;
using Seevalocal.Core.Models;
using Seevalocal.Core.Pipeline.Stages;

namespace Seevalocal.Pipelines.Factories;

/// <summary>
/// <para>
/// Composite stage for the C# coding pipeline that:
/// 1. Copies the template project to a per-item temp directory.
/// 2. Writes the generated code to Generated.cs (via FileWriterStage).
/// 3. Optionally copies the item's test file.
/// 4. Runs dotnet build (CompileStage).
/// 5. Runs dotnet test (TestStage).
/// 6. Optionally cleans up the temp directory.
/// </para>
/// <para>This stage is self-contained so that the temp path can use the item ID.</para>
/// </summary>
internal sealed class CSharpEvalSetupStage(
    string templatePath,
    double compileTimeoutSeconds,
    double testTimeoutSeconds,
    bool cleanupOnSuccess,
    bool cleanupOnFailure,
    ILoggerFactory loggerFactory) : IEvalStage
{
    public string StageName => "CSharpEvalSetupStage";

    private readonly string _templatePath = templatePath;
    private readonly double _compileTimeoutSeconds = compileTimeoutSeconds;
    private readonly double _testTimeoutSeconds = testTimeoutSeconds;
    private readonly bool _cleanupOnSuccess = cleanupOnSuccess;
    private readonly bool _cleanupOnFailure = cleanupOnFailure;
    private readonly ILoggerFactory _loggerFactory = loggerFactory;

    public async Task<StageResult> ExecuteAsync(EvalStageContext context)
    {
        var item = context.Item;
        _ = context.CancellationToken;

        // Per-item temp directory
        var tempRoot = Path.Combine(Path.GetTempPath(), "seevalocal-csharp");
        var evalDir = Path.Combine(tempRoot, $"eval-{item.Id}");
        var generatedCsPath = Path.Combine(evalDir, "Generated.cs");
        var testSuiteSource = item.Metadata.TryGetValue("testFilePath", out var tf) ? tf : null!;

        var succeeded = false;
        Dictionary<string, object?> allOutputs = [];
        List<MetricValue> allMetrics = [];

        try
        {
            // 1. Copy template
            CopyDirectory(_templatePath, evalDir);

            // 2. Copy test suite if specified
            if (!string.IsNullOrEmpty(testSuiteSource) && File.Exists(testSuiteSource))
            {
                var destTestsDir = Path.Combine(evalDir, "Tests");
                _ = Directory.CreateDirectory(destTestsDir);
                File.Copy(testSuiteSource, Path.Combine(destTestsDir, "TestSuite.cs"), overwrite: true);
            }

            // 3. Write generated code
            var fileWriter = new FileWriterStage(_loggerFactory.CreateLogger<FileWriterStage>())
            {
                OutputFilePathTemplate = generatedCsPath,
                StripMarkdownCodeFences = true,
            };
            var writeResult = await fileWriter.ExecuteAsync(context);
            if (!writeResult.Succeeded)
                return writeResult;
            MergeInto(allOutputs, allMetrics, writeResult);

            // 4. CompileStage
            var compileStage = BuildCompileStage(evalDir);
            var compileCtx = context with { StageOutputs = allOutputs };
            var compileResult = await compileStage.ExecuteAsync(compileCtx);
            MergeInto(allOutputs, allMetrics, compileResult);

            if (!compileResult.Succeeded)
            {
                return StageResult.Success(allOutputs, allMetrics) with
                {
                    Succeeded = false,
                    FailureReason = compileResult.FailureReason
                };
            }

            // 5. TestStage
            var testStage = BuildTestStage(evalDir);
            var testCtx = context with { StageOutputs = allOutputs };
            var testResult = await testStage.ExecuteAsync(testCtx);
            MergeInto(allOutputs, allMetrics, testResult);

            // Derived test metrics
            var pass = (allOutputs.GetValueOrDefault("TestStage.testPassCount") as int?) ?? 0;
            var fail = (allOutputs.GetValueOrDefault("TestStage.testFailCount") as int?) ?? 0;
            var skip = (allOutputs.GetValueOrDefault("TestStage.testSkipCount") as int?) ?? 0;
            var total = pass + fail + skip;
            var passPercent = total > 0 ? (double)pass / total * 100.0 : 0;

            allOutputs[$"{StageName}.testTotalCount"] = total;
            allOutputs[$"{StageName}.testPassRatioPercent"] = passPercent;
            allMetrics.Add(new() { Name = "testTotalCount", Value = new MetricScalar.IntMetric(total) });
            allMetrics.Add(new() { Name = "testPassRatioPercent", Value = new MetricScalar.DoubleMetric(passPercent) });

            succeeded = testResult.Succeeded;
        }
        catch (Exception ex)
        {
            return StageResult.Failure($"[CSharpEvalSetupStage] Unexpected error for item '{item.Id}': {ex.Message}");
        }
        finally
        {
            // Cleanup
            var doCleanup = succeeded ? _cleanupOnSuccess : _cleanupOnFailure;
            if (doCleanup && Directory.Exists(evalDir))
            {
                try { Directory.Delete(evalDir, recursive: true); }
                catch { /* best-effort */ }
            }
        }

        return new StageResult
        {
            Outputs = allOutputs,
            Metrics = allMetrics,
            Succeeded = succeeded,
        };
    }

    private ExternalProcessStage BuildCompileStage(string evalDir) =>
        new(_loggerFactory.CreateLogger<ExternalProcessStage>())
        {
            StageName = "CompileStage",
            ExecutablePath = "dotnet",
            Arguments = "build --no-incremental -v minimal",
            WorkingDirectoryPath = evalDir,
            TimeoutSeconds = _compileTimeoutSeconds,
            MetricExtractors =
            [
                new MetricExtractorConfig
                {
                    MetricName = "compilationSucceededBool",
                    RegexPattern = "Build succeeded",
                    Type = MetricType.Bool,
                },
                new MetricExtractorConfig
                {
                    MetricName = "compilationErrorCount",
                    RegexPattern = @"(\d+)\s+Error",
                    Type = MetricType.Int,
                },
                new MetricExtractorConfig
                {
                    MetricName = "compilationWarningCount",
                    RegexPattern = @"(\d+)\s+Warning",
                    Type = MetricType.Int,
                },
            ],
        };

    private ExternalProcessStage BuildTestStage(string evalDir) =>
        new(_loggerFactory.CreateLogger<ExternalProcessStage>())
        {
            StageName = "TestStage",
            ExecutablePath = "dotnet",
            Arguments = "test --logger \"console;verbosity=normal\" --no-build",
            WorkingDirectoryPath = evalDir,
            TimeoutSeconds = _testTimeoutSeconds,
            MetricExtractors =
            [
                new MetricExtractorConfig
                {
                    MetricName = "testPassCount",
                    RegexPattern = @"Passed:\s*(\d+)",
                    Type = MetricType.Int,
                },
                new MetricExtractorConfig
                {
                    MetricName = "testFailCount",
                    RegexPattern = @"Failed:\s*(\d+)",
                    Type = MetricType.Int,
                },
                new MetricExtractorConfig
                {
                    MetricName = "testSkipCount",
                    RegexPattern = @"Skipped:\s*(\d+)",
                    Type = MetricType.Int,
                },
            ],
        };

    private static void CopyDirectory(string source, string destination)
    {
        _ = Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }

    private static void MergeInto(
        Dictionary<string, object?> outputs,
        List<MetricValue> metrics,
        StageResult result)
    {
        foreach ((var k, var v) in result.Outputs)
            outputs[k] = v;
        metrics.AddRange(result.Metrics);
    }
}
