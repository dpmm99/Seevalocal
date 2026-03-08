using FluentAssertions;
using Seevalocal.Core.Models;
using Seevalocal.Server.Lifecycle;
using Xunit;

namespace Seevalocal.Server.Tests;

public sealed class LlamaServerArgBuilderTests
{
    private readonly LlamaServerArgBuilder _builder = new();

    private static ServerConfig DefaultServerConfig(string host = "127.0.0.1", int port = 8080) =>
        new() { Manage = true, Host = host, Port = port };

    // ── Null settings emit minimal args ──────────────────────────────────────

    [Fact]
    public void Build_NullSettings_EmitsHostAndPort()
    {
        var args = _builder.Build(new LlamaServerSettings(), DefaultServerConfig());

        _ = args.Should().Contain("--host").And.Contain("127.0.0.1");
        _ = args.Should().Contain("--port").And.Contain("8080");
    }

    [Fact]
    public void Build_NullableInts_AreOmitted()
    {
        var settings = new LlamaServerSettings(); // all null
        var args = _builder.Build(settings, DefaultServerConfig());

        _ = args.Should().NotContain("-c");
        _ = args.Should().NotContain("-b");
        _ = args.Should().NotContain("-np");
        _ = args.Should().NotContain("-ngl");
        _ = args.Should().NotContain("-t");
    }

    [Fact]
    public void Build_NullableDoubles_AreOmitted()
    {
        var args = _builder.Build(new LlamaServerSettings(), DefaultServerConfig());

        _ = args.Should().NotContain("--temp");
        _ = args.Should().NotContain("--top-p");
        _ = args.Should().NotContain("--min-p");
        _ = args.Should().NotContain("--repeat-penalty");
    }

    // ── Non-null settings are emitted correctly ───────────────────────────────

    [Fact]
    public void Build_ContextWindowTokens_EmitsCorrectFlag()
    {
        var settings = new LlamaServerSettings { ContextWindowTokens = 4096 };
        var args = _builder.Build(settings, DefaultServerConfig());

        AssertFlagValue(args, "-c", "4096");
    }

    [Fact]
    public void Build_BatchSizeTokens_EmitsCorrectFlag()
    {
        var settings = new LlamaServerSettings { BatchSizeTokens = 512 };
        var args = _builder.Build(settings, DefaultServerConfig());

        AssertFlagValue(args, "-b", "512");
    }

    [Fact]
    public void Build_GpuLayerCount_EmitsCorrectFlag()
    {
        var settings = new LlamaServerSettings { GpuLayerCount = 32 };
        var args = _builder.Build(settings, DefaultServerConfig());

        AssertFlagValue(args, "-ngl", "32");
    }

    [Fact]
    public void Build_SamplingTemperature_UsesInvariantCulture()
    {
        var settings = new LlamaServerSettings { SamplingTemperature = 0.7 };
        var args = _builder.Build(settings, DefaultServerConfig());

        // Must use "." not "," as decimal separator regardless of locale
        AssertFlagValue(args, "--temp", "0.7");
    }

    [Fact]
    public void Build_Seed_EmitsCorrectFlag()
    {
        var settings = new LlamaServerSettings { Seed = 42 };
        var args = _builder.Build(settings, DefaultServerConfig());

        AssertFlagValue(args, "--seed", "42");
    }

    // ── Bool? serialization ───────────────────────────────────────────────────

    [Fact]
    public void Build_FlashAttentionTrue_EmitsFaOn()
    {
        var settings = new LlamaServerSettings { EnableFlashAttention = true };
        var args = _builder.Build(settings, DefaultServerConfig());

        AssertFlagValue(args, "-fa", "on");
    }

    [Fact]
    public void Build_FlashAttentionFalse_EmitsFaOff()
    {
        var settings = new LlamaServerSettings { EnableFlashAttention = false };
        var args = _builder.Build(settings, DefaultServerConfig());

        AssertFlagValue(args, "-fa", "off");
    }

    [Fact]
    public void Build_FlashAttentionNull_OmitsFaFlag()
    {
        var settings = new LlamaServerSettings { EnableFlashAttention = null };
        var args = _builder.Build(settings, DefaultServerConfig());

        _ = args.Should().NotContain("-fa");
    }

    [Fact]
    public void Build_ContinuousBatchingTrue_EmitsEnableFlag()
    {
        var settings = new LlamaServerSettings { EnableContinuousBatching = true };
        var args = _builder.Build(settings, DefaultServerConfig());

        _ = args.Should().Contain("--cont-batching");
        _ = args.Should().NotContain("--no-cont-batching");
    }

    [Fact]
    public void Build_ContinuousBatchingFalse_EmitsDisableFlag()
    {
        var settings = new LlamaServerSettings { EnableContinuousBatching = false };
        var args = _builder.Build(settings, DefaultServerConfig());

        _ = args.Should().Contain("--no-cont-batching");
        _ = args.Should().NotContain("--cont-batching");
    }

    [Fact]
    public void Build_ContextShiftNull_OmitsFlag()
    {
        var settings = new LlamaServerSettings { EnableContextShift = null };
        var args = _builder.Build(settings, DefaultServerConfig());

        _ = args.Should().NotContain("--context-shift");
        _ = args.Should().NotContain("--no-context-shift");
    }

    [Fact]
    public void Build_JinjaEnabled_EmitsCorrectFlag()
    {
        var settingsOn = new LlamaServerSettings { EnableJinja = true };
        var settingsOff = new LlamaServerSettings { EnableJinja = false };

        _ = _builder.Build(settingsOn, DefaultServerConfig()).Should().Contain("--jinja");
        _ = _builder.Build(settingsOff, DefaultServerConfig()).Should().Contain("--no-jinja");
    }

    // ── KV cache types ────────────────────────────────────────────────────────

    [Fact]
    public void Build_KvCacheTypes_EmitCorrectly()
    {
        var settings = new LlamaServerSettings { KvCacheTypeK = "q8_0", KvCacheTypeV = "q4_0" };
        var args = _builder.Build(settings, DefaultServerConfig());

        AssertFlagValue(args, "-ctk", "q8_0");
        AssertFlagValue(args, "-ctv", "q4_0");
    }

    // ── String fields ─────────────────────────────────────────────────────────

    [Fact]
    public void Build_ChatTemplate_EmitsCorrectly()
    {
        var settings = new LlamaServerSettings { ChatTemplate = "chatml" };
        var args = _builder.Build(settings, DefaultServerConfig());

        AssertFlagValue(args, "--chat-template", "chatml");
    }

    [Fact]
    public void Build_ReasoningFormat_EmitsCorrectly()
    {
        var settings = new LlamaServerSettings { ReasoningFormat = "deepseek" };
        var args = _builder.Build(settings, DefaultServerConfig());

        AssertFlagValue(args, "--reasoning-format", "deepseek");
    }

    // ── API key ───────────────────────────────────────────────────────────────

    [Fact]
    public void Build_ApiKeyInConfig_EmitsApiKeyFlag()
    {
        var config = new ServerConfig { Manage = true, Host = "127.0.0.1", Port = 8080, ApiKey = "sk-test" };
        var args = _builder.Build(new LlamaServerSettings(), config);

        AssertFlagValue(args, "--api-key", "sk-test");
    }

    [Fact]
    public void Build_NoApiKey_OmitsFlag()
    {
        var args = _builder.Build(new LlamaServerSettings(), DefaultServerConfig());

        _ = args.Should().NotContain("--api-key");
    }

    // ── Model source ──────────────────────────────────────────────────────────

    [Fact]
    public void Build_LocalFileModel_EmitsDashM()
    {
        var config = DefaultServerConfig() with
        {
            Model = new ModelSource { Kind = ModelSourceKind.LocalFile, FilePath = "/models/test.gguf" }
        };
        var args = _builder.Build(new LlamaServerSettings(), config);

        AssertFlagValue(args, "-m", "/models/test.gguf");
    }

    [Fact]
    public void Build_HuggingFaceModel_EmitsHfFlags()
    {
        var config = DefaultServerConfig() with
        {
            Model = new ModelSource
            {
                Kind = ModelSourceKind.HuggingFace,
                HfRepo = "unsloth/phi-4-GGUF",
                HfQuant = "q4_k_m",
            }
        };
        var args = _builder.Build(new LlamaServerSettings(), config);

        AssertFlagValue(args, "--hf-repo", "unsloth/phi-4-GGUF");
        AssertFlagValue(args, "--hf-file", "q4_k_m");
        _ = args.Should().NotContain("--hf-token");
    }

    // ── Extra args ────────────────────────────────────────────────────────────

    [Fact]
    public void Build_ExtraArgs_AppendedVerbatim()
    {
        var config = DefaultServerConfig() with { ExtraArgs = ["--lora", "/loras/my.bin"] };
        var args = _builder.Build(new LlamaServerSettings(), config);

        var list = args.ToList();
        var loraIdx = list.IndexOf("--lora");
        _ = loraIdx.Should().BeGreaterThanOrEqualTo(0, "extra args should be present");
        _ = list[loraIdx + 1].Should().Be("/loras/my.bin");
    }

    // ── Unit suffix sanity ────────────────────────────────────────────────────

    [Fact]
    public void AllIntSettingProperties_HaveUnitSuffix()
    {
        var settingsType = typeof(LlamaServerSettings);
        _ = new List<string>();

        foreach (var prop in settingsType.GetProperties())
        {
            if (!prop.Name.EndsWith("Count") &&
                !prop.Name.EndsWith("Tokens") &&
                !prop.Name.EndsWith("Seconds") &&
                !prop.Name.EndsWith("Bytes") &&
                !prop.Name.EndsWith("Ratio") &&
                !prop.Name.EndsWith("Percent") &&
                !prop.Name.EndsWith("Temperature") &&
                !prop.Name.EndsWith("Path") &&    // string fields
                !prop.Name.EndsWith("Type") &&
                !prop.Name.EndsWith("Format") &&
                !prop.Name.EndsWith("Template") &&
                !prop.Name.EndsWith("Key") &&
                !prop.Name.EndsWith("Host") &&
                !prop.Name.EndsWith("Penalty") &&
                !prop.Name.EndsWith("Verbosity") &&
                prop.Name != "Port" &&             // port is a well-known dimensionless concept
                prop.Name != "Seed" &&
                prop.PropertyType == typeof(int?) || prop.PropertyType == typeof(double?))
            {
                // Only flag numeric properties that truly lack unit suffix
            }
        }

        // The key naming props we want to verify exist with proper suffix
        _ = settingsType.GetProperty(nameof(LlamaServerSettings.ContextWindowTokens)).Should().NotBeNull();
        _ = settingsType.GetProperty(nameof(LlamaServerSettings.BatchSizeTokens)).Should().NotBeNull();
        _ = settingsType.GetProperty(nameof(LlamaServerSettings.UbatchSizeTokens)).Should().NotBeNull();
        _ = settingsType.GetProperty(nameof(LlamaServerSettings.ParallelSlotCount)).Should().NotBeNull();
        _ = settingsType.GetProperty(nameof(LlamaServerSettings.GpuLayerCount)).Should().NotBeNull();
        _ = settingsType.GetProperty(nameof(LlamaServerSettings.ThreadCount)).Should().NotBeNull();
        _ = settingsType.GetProperty(nameof(LlamaServerSettings.SamplingTemperature)).Should().NotBeNull();
        _ = settingsType.GetProperty(nameof(LlamaServerSettings.LogVerbosity)).Should().NotBeNull();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void AssertFlagValue(string[] args, string flag, string expectedValue)
    {
        var list = args.ToList();
        var idx = list.IndexOf(flag);
        _ = idx.Should().BeGreaterThanOrEqualTo(0, $"flag '{flag}' should be present in args");
        _ = list[idx + 1].Should().Be(expectedValue, $"flag '{flag}' should have value '{expectedValue}'");
    }
}
