using Seevalocal.Config.Loading;
using Seevalocal.Config.Merging;
using Seevalocal.Core.Models;
using Xunit;
using System.Reflection;

namespace Seevalocal.UI.Tests;

/// <summary>
/// Tests that verify settings load correctly from actual YAML files.
/// </summary>
public class SettingsFileLoadingTests
{
    [Fact]
    public async Task LoadFastest320TestYaml_AllSettingsLoadCorrectly()
    {
        var yamlPath = @"C:\AI\SeevalocalFastest320Test.yml";
        
        // Skip if file doesn't exist (for CI environments)
        if (!File.Exists(yamlPath))
        {
            System.Console.WriteLine($"Skipping test - file not found: {yamlPath}");
            return;
        }

        // Load the YAML file
        var loader = new SettingsFileLoader(Microsoft.Extensions.Logging.Abstractions.NullLogger<SettingsFileLoader>.Instance);
        var loadResult = await loader.LoadAsync(yamlPath);
        
        Assert.True(loadResult.IsSuccess, $"Failed to load YAML: {string.Join(", ", loadResult.Errors)}");
        var partial = loadResult.Value;
        
        // Merge to get a ResolvedConfig
        var merger = new ConfigurationMerger();
        var resolved = merger.Merge([partial]);
        
        // ── Verify Run settings ──────────────────────────────────────────────────
        Assert.Equal("Translation", resolved.Run.PipelineName);
        Assert.Equal("fastest320test", resolved.Run.RunName);
        
        // ── Verify Server settings ──────────────────────────────────────────────
        Assert.True(resolved.Server.Manage);
        Assert.Equal(@"C:\AI\Falcon-H1-Tiny-R-90M.Q8_0.gguf", resolved.Server.Model.FilePath);
        
        // ── Verify LlamaSettings ────────────────────────────────────────────────
        var llama = resolved.LlamaServer;
        Assert.NotNull(llama);
        Assert.Equal(16384, llama.ContextWindowTokens);
        Assert.Equal(8, llama.ParallelSlotCount);
        Assert.Equal(999, llama.GpuLayerCount);
        Assert.Equal(0.95, llama.TopP);
        Assert.Equal(20, llama.TopK);
        Assert.Equal(0.05, llama.MinP);
        Assert.Equal(0, llama.RepeatPenalty);
        Assert.Equal(0, llama.RepeatLastNTokens);
        Assert.Equal(0, llama.PresencePenalty);
        Assert.Equal(0, llama.FrequencyPenalty);
        Assert.Equal(-1, llama.Seed);
        Assert.Equal(8, llama.ThreadCount);
        Assert.Equal(2048, llama.ReasoningBudget);
        Assert.Equal("--wait, I've exhausted my reasoning budget. I must give my final response now.", llama.ReasoningBudgetMessage);
        Assert.Equal(300, llama.ServerTimeoutSeconds);
        Assert.NotNull(llama.ExtraArgs);
        // YAML has: - -ts\n  - 0,100 which loads as 2 items
        Assert.Equal(2, llama.ExtraArgs.Count);
        Assert.Equal("-ts", llama.ExtraArgs[0]);
        Assert.Equal("0,100", llama.ExtraArgs[1]);
        
        // ── Verify Judge settings ───────────────────────────────────────────────
        var judge = resolved.Judge;
        Assert.NotNull(judge);
        Assert.True(judge.Enable);
        Assert.Equal("translation-judge-template", judge.JudgePromptTemplate);
        
        Assert.NotNull(judge.ServerConfig);
        Assert.True(judge.ServerConfig.Manage);
        Assert.Equal(@"C:\AI\granite-3.1-1b-a400m-instruct-IQ4_XS.gguf", judge.ServerConfig.Model.FilePath);
        
        Assert.NotNull(judge.ServerSettings);
        var judgeSettings = judge.ServerSettings;
        Assert.Equal(65536, judgeSettings.ContextWindowTokens);
        Assert.Equal(8, judgeSettings.ParallelSlotCount);
        Assert.Equal(998, judgeSettings.GpuLayerCount);
        Assert.Equal(0.9, judgeSettings.TopP);
        Assert.Equal(25, judgeSettings.TopK);
        Assert.Equal(0.1, judgeSettings.MinP);
        Assert.Equal(0, judgeSettings.RepeatPenalty);
        Assert.Equal(0, judgeSettings.PresencePenalty);
        Assert.Equal(0, judgeSettings.FrequencyPenalty);
        Assert.Equal(-1, judgeSettings.Seed);
        Assert.Equal(6, judgeSettings.ThreadCount);
        Assert.Equal(2048, judgeSettings.ReasoningBudget);
        Assert.Equal("--wait, I've exhausted my reasoning budget. I must give my final response now.", judgeSettings.ReasoningBudgetMessage);
        Assert.Equal(350, judgeSettings.ServerTimeoutSeconds);
        Assert.NotNull(judgeSettings.ExtraArgs);
        // YAML has: - -ts\n  - 0,100 which loads as 2 items
        Assert.Equal(2, judgeSettings.ExtraArgs.Count);
        Assert.Equal("-ts", judgeSettings.ExtraArgs[0]);
        Assert.Equal("0,100", judgeSettings.ExtraArgs[1]);
        
        // ── Verify DataSource settings ──────────────────────────────────────────
        var ds = resolved.DataSource;
        Assert.NotNull(ds);
        Assert.Equal(DataSourceKind.SplitDirectories, ds.Kind);
        Assert.Equal(@"C:\DePro\CodeProjects\CSharp\Seevalocal\src\Seevalocal.UI\bin\Debug\net10.0\generated_evals\320Translations\prompts\", ds.PromptDirectory);
        Assert.Equal(@"C:\DePro\CodeProjects\CSharp\Seevalocal\src\Seevalocal.UI\bin\Debug\net10.0\generated_evals\320Translations\expected_outputs\", ds.ExpectedDirectory);
        Assert.Equal("You are a professional translator. Translate the following text from the original to the reference translation's language accurately and naturally. Output only the translation, with no explanation or preamble.", ds.DefaultSystemPrompt);
        
        // ── Verify PipelineOptions ──────────────────────────────────────────────
        Assert.NotNull(resolved.PipelineOptions);
        Assert.Equal("the original", resolved.PipelineOptions["sourceLanguage"]);
        Assert.Equal("the reference translation's language", resolved.PipelineOptions["targetLanguage"]);
    }

    [Fact]
    public void AllLlamaSettingsProperties_AreTestedByReflection()
    {
        // This test ensures that if new properties are added to LlamaServerSettings,
        // they will be caught by the reflection-based round-trip test
        var llamaType = typeof(LlamaServerSettings);
        var props = llamaType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        
        // Verify we have a reasonable number of properties (should be 30+)
        Assert.True(props.Length >= 30, 
            $"Expected at least 30 properties on LlamaServerSettings, found {props.Length}");

        // Verify key properties exist
        var expectedProps = new[]
        {
            nameof(LlamaServerSettings.ContextWindowTokens),
            nameof(LlamaServerSettings.GpuLayerCount),
            nameof(LlamaServerSettings.TopP),
            nameof(LlamaServerSettings.TopK),
            nameof(LlamaServerSettings.MinP),
            nameof(LlamaServerSettings.RepeatPenalty),
            nameof(LlamaServerSettings.PresencePenalty),
            nameof(LlamaServerSettings.FrequencyPenalty),
            nameof(LlamaServerSettings.Seed),
            nameof(LlamaServerSettings.ThreadCount),
            nameof(LlamaServerSettings.ReasoningBudget),
            nameof(LlamaServerSettings.ReasoningBudgetMessage),
            nameof(LlamaServerSettings.ExtraArgs),
        };

        var propNames = props.Select(p => p.Name).ToHashSet();
        foreach (var expected in expectedProps)
        {
            Assert.True(propNames.Contains(expected), $"{expected} property not found on LlamaServerSettings");
        }
    }
}
