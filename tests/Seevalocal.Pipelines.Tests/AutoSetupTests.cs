using Seevalocal.Core.Models;
using Seevalocal.Pipelines.Factories;
using Xunit;

namespace Seevalocal.Pipelines.Tests;

public sealed class AutoSetupTests
{
    /// <summary>
    /// When dotnet is present on PATH the method should return success.
    /// This test is environment-dependent — skipped in CI if dotnet not installed.
    /// </summary>
    [Fact]
    public async Task EnsurePrerequisites_DotnetOnPath_Succeeds()
    {
        // Verify dotnet is actually available before asserting
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "--version",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        bool dotnetAvailable;
        try
        {
            using var p = System.Diagnostics.Process.Start(psi)!;
            await p.WaitForExitAsync();
            dotnetAvailable = p.ExitCode == 0;
        }
        catch
        {
            dotnetAvailable = false;
        }

        if (!dotnetAvailable)
            return; // dotnet not installed — skip assertion

        var result = await CSharpCodingPipelineFactory.EnsurePrerequisitesAsync(
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void TranslationFactory_EnsurePrerequisites_NoJudgeConfig_Fails()
    {
        var config = new ResolvedConfig
        {
            Judge = null, // no judge configured
        };

        // Use directories that definitely don't exist
        var evalSet = new EvalSetConfig
        {
            Id = "eval",
            PipelineName = "Translation",
            DataSource = new DataSourceConfig
            {
                PromptDirectory = "/no/such/dir/source",
                ExpectedDirectory = "/no/such/dir/ref",
            }
        };

        var result = TranslationPipelineFactory.EnsurePrerequisites(evalSet, config);

        Assert.True(result.IsFailed);
        Assert.Contains(result.Errors, static e => e.Message.Contains("Judge endpoint is not configured"));
    }
}
