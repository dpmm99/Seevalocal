using FluentAssertions;
using Seevalocal.Config.Export;
using Seevalocal.Core.Models;
using Xunit;

namespace Seevalocal.Config.Tests;

public sealed class ShellScriptExporterTests
{
    private readonly ShellScriptExporter _exporter = new();

    private static ResolvedConfig ManagedConfig(string runName = "test-run") => new()
    {
        Run = new RunMeta
        {
            RunName = runName,
            OutputDirectoryPath = "./results",
        },
        Server = new ServerConfig
        {
            Manage = true,
            BaseUrl = "http://127.0.0.1:8080",
            Model = new ModelSource
            {
                Kind = ModelSourceKind.LocalFile,
                FilePath = "/models/phi-4-Q4_K_M.gguf",
            },
        },
        LlamaServer = new LlamaServerSettings
        {
            ContextWindowTokens = 8192,
            ParallelSlotCount = 4,
            EnableFlashAttention = true,
            SamplingTemperature = 0.2,
        },
    };

    private static ResolvedConfig UnmanagedConfig() => new()
    {
        Run = new RunMeta { RunName = "ext-run", OutputDirectoryPath = "./out" },
        Server = new ServerConfig { Manage = false, BaseUrl = "http://remote:8080" },
        LlamaServer = new LlamaServerSettings(),
    };

    // -------------------------------------------------------------------------
    // Bash output
    // -------------------------------------------------------------------------

    [Fact]
    public void Export_Bash_ContainsShebang()
    {
        var script = ShellScriptExporter.Export(ManagedConfig(), ShellTarget.Bash);
        _ = script.Should().StartWith("#!/usr/bin/env bash");
    }

    [Fact]
    public void Export_Bash_ContainsRunName()
    {
        var script = ShellScriptExporter.Export(ManagedConfig("my-run"), ShellTarget.Bash);
        _ = script.Should().Contain("my-run");
    }

    [Fact]
    public void Export_Bash_ContainsModelPath()
    {
        var script = ShellScriptExporter.Export(ManagedConfig(), ShellTarget.Bash);
        _ = script.Should().Contain("/models/phi-4-Q4_K_M.gguf");
    }

    [Fact]
    public void Export_Bash_ContainsContextWindowFlag()
    {
        var script = ShellScriptExporter.Export(ManagedConfig(), ShellTarget.Bash);
        _ = script.Should().Contain("--ctx").And.Contain("8192");
    }

    [Fact]
    public void Export_Bash_ContainsParallelSlotFlag()
    {
        var script = ShellScriptExporter.Export(ManagedConfig(), ShellTarget.Bash);
        _ = script.Should().Contain("--parallel").And.Contain("4");
    }

    [Fact]
    public void Export_Bash_ContainsFlashAttentionFlag()
    {
        var script = ShellScriptExporter.Export(ManagedConfig(), ShellTarget.Bash);
        _ = script.Should().Contain("--flash-attn");
    }

    [Fact]
    public void Export_Bash_ContainsTemperatureFlag()
    {
        var script = ShellScriptExporter.Export(ManagedConfig(), ShellTarget.Bash);
        _ = script.Should().Contain("--temp").And.Contain("0.2");
    }

    [Fact]
    public void Export_Bash_ContainsSeevalocalCommand()
    {
        var script = ShellScriptExporter.Export(ManagedConfig(), ShellTarget.Bash);
        _ = script.Should().Contain("seevalocal");
    }

    [Fact]
    public void Export_Bash_ContainsManageFlag()
    {
        var script = ShellScriptExporter.Export(ManagedConfig(), ShellTarget.Bash);
        _ = script.Should().Contain("--manage");
    }

    [Fact]
    public void Export_Bash_Unmanaged_ContainsNoManageFlag()
    {
        var script = ShellScriptExporter.Export(UnmanagedConfig(), ShellTarget.Bash);
        _ = script.Should().Contain("--no-manage");
    }

    [Fact]
    public void Export_Bash_Unmanaged_UsesBaseUrl()
    {
        var script = ShellScriptExporter.Export(UnmanagedConfig(), ShellTarget.Bash);
        _ = script.Should().Contain("http://remote:8080");
    }

    [Fact]
    public void Export_Bash_Unmanaged_DoesNotContainManageFlag()
    {
        var script = ShellScriptExporter.Export(UnmanagedConfig(), ShellTarget.Bash);
        _ = script.Should().NotContain("--manage");
    }

    // -------------------------------------------------------------------------
    // Null fields omitted
    // -------------------------------------------------------------------------

    [Fact]
    public void Export_Bash_NullFields_NotIncluded()
    {
        var config = ManagedConfig() with
        {
            LlamaServer = new LlamaServerSettings
            {
                // Only set temperature; everything else is null
                SamplingTemperature = 0.7,
            },
        };

        var script = ShellScriptExporter.Export(config, ShellTarget.Bash);

        _ = script.Should().NotContain("--parallel");
        _ = script.Should().NotContain("--ctx");
        _ = script.Should().Contain("--temp");
    }

    // -------------------------------------------------------------------------
    // PowerShell output
    // -------------------------------------------------------------------------

    [Fact]
    public void Export_PowerShell_ContainsSeevalocalCommand()
    {
        var script = ShellScriptExporter.Export(ManagedConfig(), ShellTarget.PowerShell);
        _ = script.Should().Contain("seevalocal");
    }

    [Fact]
    public void Export_PowerShell_ContainsModelPath()
    {
        var script = ShellScriptExporter.Export(ManagedConfig(), ShellTarget.PowerShell);
        _ = script.Should().Contain("/models/phi-4-Q4_K_M.gguf");
    }

    [Fact]
    public void Export_PowerShell_ContainsManageFlag()
    {
        var script = ShellScriptExporter.Export(ManagedConfig(), ShellTarget.PowerShell);
        _ = script.Should().Contain("--manage");
    }

    [Fact]
    public void Export_PowerShell_Unmanaged_NoManageFlag()
    {
        var script = ShellScriptExporter.Export(UnmanagedConfig(), ShellTarget.PowerShell);
        _ = script.Should().NotContain("--manage");
        _ = script.Should().Contain("--no-manage");
    }

    [Fact]
    public void Export_PowerShell_NoLlamaServerCommands()
    {
        var script = ShellScriptExporter.Export(ManagedConfig(), ShellTarget.PowerShell);
        _ = script.Should().NotContain("Start-Process");
        _ = script.Should().NotContain("Stop-Process");
        _ = script.Should().NotContain("-PassThru");
    }

    // -------------------------------------------------------------------------
    // Bash escaping
    // -------------------------------------------------------------------------

    [Fact]
    public void Export_Bash_PathWithSpaces_IsQuoted()
    {
        var config = ManagedConfig() with
        {
            Server = new ServerConfig
            {
                Manage = true,
                BaseUrl = "http://127.0.0.1:8080",
                Model = new ModelSource
                {
                    Kind = ModelSourceKind.LocalFile,
                    FilePath = "/my models/phi 4.gguf",
                },
            },
        };

        var script = ShellScriptExporter.Export(config, ShellTarget.Bash);

        _ = script.Should().Contain("'");  // single-quoted
        _ = script.Should().Contain("/my models/phi 4.gguf");
    }

    // -------------------------------------------------------------------------
    // HuggingFace model source
    // -------------------------------------------------------------------------

    [Fact]
    public void Export_Bash_HfModel_UsesHfRepoFlag()
    {
        var config = ManagedConfig() with
        {
            Server = new ServerConfig
            {
                Manage = true,
                BaseUrl = "http://127.0.0.1:8080",
                Model = new ModelSource
                {
                    Kind = ModelSourceKind.HuggingFace,
                    HfRepo = "bartowski/phi-4-GGUF",
                    HfQuant = "phi-4-Q4_K_M.gguf",
                },
            },
        };

        var script = ShellScriptExporter.Export(config, ShellTarget.Bash);

        _ = script.Should().Contain("--hf-repo").And.Contain("bartowski/phi-4-GGUF");
    }
}
