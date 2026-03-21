using Microsoft.Extensions.Logging;
using Seevalocal.Core.Models;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Seevalocal.Core.Pipeline.Stages;

/// <summary>
/// Runs an external program, captures stdout/stderr, and extracts named metrics.
/// Thread-safe: each ExecuteAsync call spawns its own process.
/// </summary>
public sealed partial class ExternalProcessStage(ILogger<ExternalProcessStage> logger) : IEvalStage
{
    private readonly ILogger<ExternalProcessStage> _logger = logger;

    public string StageName { get; init; } = "ExternalProcessStage";

    public string ExecutablePath { get; init; } = "";

    /// <summary>
    /// Arguments string. Supports {StageName.outputKey} placeholder substitution
    /// from prior stage outputs, plus {id} for the item ID.
    /// </summary>
    public string Arguments { get; init; } = "";

    public string? WorkingDirectoryPath { get; init; }
    public double TimeoutSeconds { get; init; } = 30.0;

    /// <summary>Patterns to extract named numeric metrics from stdout.</summary>
    public IReadOnlyList<MetricExtractorConfig> MetricExtractors { get; init; } = [];

    /// <summary>If true, a non-zero exit code is treated as a stage failure.</summary>
    public bool FailOnNonZeroExit { get; init; } = true;

    public async Task<StageResult> ExecuteAsync(EvalStageContext context)
    {
        var item = context.Item;
        var ct = context.CancellationToken;

        if (string.IsNullOrWhiteSpace(ExecutablePath))
            return StageResult.Failure($"[{StageName}] ExecutablePath is not configured");

        var resolvedArgs = SubstitutePlaceholders(Arguments, item.Id, context.StageOutputs);
        var workingDir = WorkingDirectoryPath is not null
            ? Path.GetFullPath(SubstitutePlaceholders(WorkingDirectoryPath, item.Id, context.StageOutputs))
            : null;

        _logger.LogDebug("[{StageName}] Launching: {Executable} {Arguments}", StageName, ExecutablePath, resolvedArgs);

        var stdoutSb = new StringBuilder();
        var stderrSb = new StringBuilder();

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = ExecutablePath,
            Arguments = resolvedArgs,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) _ = stdoutSb.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) _ = stderrSb.AppendLine(e.Data);
        };

        var sw = Stopwatch.StartNew();

        try
        {
            _ = process.Start();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{StageName}] Failed to start process for item {EvalItemId}", StageName, item.Id);
            return StageResult.Failure($"[{StageName}] Failed to start process: {ex.Message}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            _logger.LogWarning("[{StageName}] Process timed out after {TimeoutSeconds}s for item {EvalItemId}",
                StageName, TimeoutSeconds, item.Id);
            return StageResult.Failure($"[{StageName}] Process timed out after {TimeoutSeconds}s");
        }

        sw.Stop();

        var exitCode = process.ExitCode;
        var stdout = stdoutSb.ToString();
        var stderr = stderrSb.ToString();

        _logger.LogDebug("[{StageName}] Process exited with code {ExitCode} in {DurationSeconds:F2}s for item {EvalItemId}",
            StageName, exitCode, sw.Elapsed.TotalSeconds, item.Id);

        var outputs = new Dictionary<string, object?>
        {
            [$"{StageName}.stdout"] = stdout,
            [$"{StageName}.stderr"] = stderr,
            [$"{StageName}.exitCode"] = exitCode
        };

        List<MetricValue> metrics =
        [
            new MetricValue { Name = "processExitCode", Value = new MetricScalar.IntMetric(exitCode), SourceStage = StageName },
            new MetricValue { Name = "processDurationSeconds", Value = new MetricScalar.DoubleMetric(sw.Elapsed.TotalSeconds), SourceStage = StageName }
        ];

        // Extract metrics from stdout
        foreach (var extractor in MetricExtractors)
        {
            var extracted = TryExtractMetric(extractor, stdout, item.Id);
            if (extracted is not null)
            {
                metrics.Add(extracted);
                outputs[$"{StageName}.{extractor.MetricName}"] = extracted.Value;
            }
        }

        if (FailOnNonZeroExit && exitCode != 0)
        {
            _logger.LogWarning("[{StageName}] Non-zero exit code {ExitCode} for item {EvalItemId}",
                StageName, exitCode, item.Id);
            return new StageResult
            {
                Outputs = outputs,
                Metrics = metrics,
                Succeeded = false,
                FailureReason = $"[{StageName}] Process exited with code {exitCode}"
            };
        }

        return StageResult.Success(outputs, metrics);
    }

    private MetricValue? TryExtractMetric(MetricExtractorConfig extractor, string stdout, string evalItemId)
    {
        try
        {
            var match = Regex.Match(stdout, extractor.RegexPattern, RegexOptions.Multiline);
            if (!match.Success || !match.Groups["value"].Success)
            {
                _logger.LogWarning("[{StageName}] Metric extractor '{MetricName}' found no match in stdout for item {EvalItemId}",
                    StageName, extractor.MetricName, evalItemId);
                return null;
            }

            var raw = match.Groups["value"].Value;

            return extractor.Type switch
            {
                MetricType.Double when double.TryParse(raw, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var d)
                    => new MetricValue { Name = extractor.MetricName, Value = new MetricScalar.DoubleMetric(d), SourceStage = StageName },

                MetricType.Int when int.TryParse(raw, out var i)
                    => new MetricValue { Name = extractor.MetricName, Value = new MetricScalar.IntMetric(i), SourceStage = StageName },

                MetricType.Bool when bool.TryParse(raw, out var b)
                    => new MetricValue { Name = extractor.MetricName, Value = new MetricScalar.BoolMetric(b), SourceStage = StageName },

                MetricType.String
                    => new MetricValue { Name = extractor.MetricName, Value = new MetricScalar.StringMetric(raw), SourceStage = StageName },

                _ => null
            };
        }
        catch (RegexParseException ex)
        {
            _logger.LogError(ex, "[{StageName}] Invalid regex pattern for metric '{MetricName}'",
                StageName, extractor.MetricName);
            return null;
        }
    }

    private static string SubstitutePlaceholders(
        string template,
        string itemId,
        IReadOnlyDictionary<string, object?> stageOutputs)
    {
        var result = template.Replace("{id}", itemId, StringComparison.OrdinalIgnoreCase);

        // Replace {StageName.outputKey} placeholders
        return PlaceholderSubstitutionRegex().Replace(result, m =>
        {
            var key = m.Groups[1].Value;
            if (stageOutputs.TryGetValue(key, out var val) && val is not null)
                return val.ToString() ?? "";
            return m.Value; // leave unreplaced if not found
        });
    }

    [GeneratedRegex(@"\{([^}]+)\}")]
    private static partial Regex PlaceholderSubstitutionRegex();
}

/// <summary>
/// Configuration for extracting a named metric from process stdout using a regex.
/// </summary>
public record MetricExtractorConfig
{
    /// <summary>Metric name. Must include unit suffix per conventions (e.g., "testPassCount").</summary>
    public string MetricName { get; init; } = "";

    /// <summary>Regex pattern with a named capture group "value".</summary>
    public string RegexPattern { get; init; } = "";

    public MetricType Type { get; init; } = MetricType.Double;
}
