using Microsoft.Extensions.Logging.Abstractions;
using Seevalocal.Core.Models;
using Seevalocal.Core.Pipeline.Stages;
using Xunit;

namespace Seevalocal.Core.Pipeline.Tests;

public class ExternalProcessStageTests
{
    private static string EchoExe => OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh";
    private static string EchoArgs(string msg) =>
        OperatingSystem.IsWindows() ? $"/c echo {msg}" : $"-c \"echo {msg}\"";

    // ── Stdout/stderr capture ─────────────────────────────────────────────────

    [Fact]
    public async Task Execute_CapturesStdout()
    {
        var stage = new ExternalProcessStage(NullLogger<ExternalProcessStage>.Instance)
        {
            ExecutablePath = EchoExe,
            Arguments = EchoArgs("hello from test"),
            FailOnNonZeroExit = true
        };

        var ctx = TestHelpers.MakeContext();
        var result = await stage.ExecuteAsync(ctx);

        Assert.True(result.Succeeded);
        var stdout = result.Outputs["ExternalProcessStage.stdout"] as string;
        Assert.Contains("hello from test", stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Execute_ExitCodePresentInOutputsAndMetrics()
    {
        var stage = new ExternalProcessStage(NullLogger<ExternalProcessStage>.Instance)
        {
            ExecutablePath = EchoExe,
            Arguments = EchoArgs("ok"),
            FailOnNonZeroExit = false
        };

        var ctx = TestHelpers.MakeContext();
        var result = await stage.ExecuteAsync(ctx);

        Assert.Equal(0, result.Outputs["ExternalProcessStage.exitCode"]);
        Assert.Contains(result.Metrics, static m => m.Name == "processExitCode" && m.Value is MetricScalar.IntMetric intVal && intVal.Value == 0);
        Assert.Contains(result.Metrics, static m => m.Name == "processDurationSeconds");
    }

    // ── Non-zero exit code ────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_NonZeroExit_FailsWhenConfigured()
    {
        (var exe, var args) = OperatingSystem.IsWindows()
            ? ("cmd.exe", "/c exit 1")
            : ("/bin/sh", "-c \"exit 1\"");

        var stage = new ExternalProcessStage(NullLogger<ExternalProcessStage>.Instance)
        {
            ExecutablePath = exe,
            Arguments = args,
            FailOnNonZeroExit = true
        };

        var ctx = TestHelpers.MakeContext();
        var result = await stage.ExecuteAsync(ctx);

        Assert.False(result.Succeeded);
        Assert.Contains("exited with code", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Execute_NonZeroExit_SucceedsWhenNotConfiguredToFail()
    {
        (var exe, var args) = OperatingSystem.IsWindows()
            ? ("cmd.exe", "/c exit 2")
            : ("/bin/sh", "-c \"exit 2\"");

        var stage = new ExternalProcessStage(NullLogger<ExternalProcessStage>.Instance)
        {
            ExecutablePath = exe,
            Arguments = args,
            FailOnNonZeroExit = false
        };

        var ctx = TestHelpers.MakeContext();
        var result = await stage.ExecuteAsync(ctx);

        Assert.True(result.Succeeded);
    }

    // ── Metric extraction ─────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_RegexExtractor_ExtractsMetric()
    {
        // Emit a line like "SCORE: 42" and extract it
        (var exe, var args) = OperatingSystem.IsWindows()
            ? ("cmd.exe", "/c echo SCORE: 42")
            : ("/bin/sh", "-c \"echo SCORE: 42\"");

        var stage = new ExternalProcessStage(NullLogger<ExternalProcessStage>.Instance)
        {
            ExecutablePath = exe,
            Arguments = args,
            MetricExtractors =
            [
                new MetricExtractorConfig
                {
                    MetricName = "scoreCount",
                    RegexPattern = @"SCORE:\s+(?<value>\d+)",
                    Type = MetricType.Int
                }
            ]
        };

        var ctx = TestHelpers.MakeContext();
        var result = await stage.ExecuteAsync(ctx);

        Assert.True(result.Succeeded);
        var metric = result.Metrics.SingleOrDefault(static m => m.Name == "scoreCount");
        Assert.NotNull(metric);
        Assert.Equal(42, ((MetricScalar.IntMetric)metric.Value).Value);
    }

    // ── Timeout ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_Timeout_ReturnsFailure()
    {
        (var exe, var args) = OperatingSystem.IsWindows()
            ? ("cmd.exe", "/c ping -n 10 127.0.0.1 > nul")
            : ("/bin/sh", "-c \"sleep 10\"");

        var stage = new ExternalProcessStage(NullLogger<ExternalProcessStage>.Instance)
        {
            ExecutablePath = exe,
            Arguments = args,
            TimeoutSeconds = 0.1   // 100ms — way too short for sleep 10
        };

        var ctx = TestHelpers.MakeContext();
        var result = await stage.ExecuteAsync(ctx);

        Assert.False(result.Succeeded);
        Assert.Contains("timed out", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    // ── Placeholder substitution ──────────────────────────────────────────────

    [Fact]
    public async Task Execute_ArgumentsContainItemIdPlaceholder_IsSubstituted()
    {
        (var exe, var args) = OperatingSystem.IsWindows()
            ? ("cmd.exe", "/c echo {id}")
            : ("/bin/sh", "-c \"echo {id}\"");

        var stage = new ExternalProcessStage(NullLogger<ExternalProcessStage>.Instance)
        {
            ExecutablePath = exe,
            Arguments = args
        };

        var item = TestHelpers.MakeItem(id: "my-item-000001");
        var ctx = TestHelpers.MakeContext(item: item);
        var result = await stage.ExecuteAsync(ctx);

        Assert.True(result.Succeeded);
        var stdout = result.Outputs["ExternalProcessStage.stdout"] as string ?? "";
        Assert.Contains("my-item-000001", stdout);
    }
}
