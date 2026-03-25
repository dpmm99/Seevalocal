using Seevalocal.Config.Loading;
using Seevalocal.Config.Merging;
using Seevalocal.Core;
using Seevalocal.Core.Models;
using Xunit;
using System.Reflection;

namespace Seevalocal.UI.Tests;

/// <summary>
/// Tests that verify settings layering and resolution works correctly.
/// Simulates the actual UI flow: defaults → settings file → wizard → resolve.
/// </summary>
public class SettingsLayeringTests
{
    [Fact]
    public async Task LoadDefaultSettings_ThenYamlFile_ThenResolve_AllValuesComeFromYaml()
    {
        var yamlPath = @"C:\AI\SeevalocalFastest320Test.yml";
        
        // Skip if file doesn't exist (for CI environments)
        if (!File.Exists(yamlPath))
        {
            Console.WriteLine($"Skipping test - file not found: {yamlPath}");
            return;
        }

        // ── Step 1: Create default/empty config (simulating default settings) ──────────
        var defaultConfig = new PartialConfig
        {
            Run = new PartialRunMeta
            {
                PipelineName = "CasualQA",  // Default
            },
            Server = new PartialServerConfig
            {
                Manage = true,
            },
            LlamaSettings = new PartialLlamaServerSettings
            {
                ContextWindowTokens = 2048,  // Default
                ParallelSlotCount = 4,       // Default
            },
            Judge = new PartialJudgeConfig
            {
                Enable = false,  // Default
                JudgePromptTemplate = "standard",  // Default
                ServerConfig = new PartialServerConfig
                {
                    Manage = true,
                },
                ServerSettings = new PartialLlamaServerSettings
                {
                    ContextWindowTokens = 2048,  // Default
                    ParallelSlotCount = 4,       // Default
                },
            },
            DataSource = new PartialDataSourceConfig
            {
                Kind = DataSourceKind.SingleFile,  // Default
            },
            PipelineOptions = [],
        };

        // ── Step 2: Load YAML file as settings layer ──────────
        var loader = new SettingsFileLoader(Microsoft.Extensions.Logging.Abstractions.NullLogger<SettingsFileLoader>.Instance);
        var loadResult = await loader.LoadAsync(yamlPath);
        
        Assert.True(loadResult.IsSuccess, $"Failed to load YAML: {string.Join(", ", loadResult.Errors)}");
        var yamlConfig = loadResult.Value;
        
        // ── Step 3: Merge configs (default → yaml file) - simulating UI behavior ──────────
        var merger = new ConfigurationMerger();
        var merged = merger.Merge([defaultConfig, yamlConfig]);
        
        // ── Step 4: Verify ALL values come from YAML, not defaults ──────────
        
        // Run settings should come from YAML
        Assert.Equal("Translation", merged.Run.PipelineName);  // NOT "CasualQA"
        Assert.Equal("fastest320test", merged.Run.RunName);    // NOT null
        
        // Server settings should come from YAML
        Assert.True(merged.Server.Manage);
        Assert.Equal(@"C:\AI\Falcon-H1-Tiny-R-90M.Q8_0.gguf", merged.Server.Model.FilePath);  // NOT null
        
        // LlamaSettings should come from YAML
        var llama = merged.LlamaServer;
        Assert.Equal(16384, llama.ContextWindowTokens);     // NOT 2048
        Assert.Equal(8, llama.ParallelSlotCount);           // NOT 4
        Assert.Equal(999, llama.GpuLayerCount);             // NOT null
        Assert.Equal(0.95, llama.TopP);                     // NOT null
        Assert.Equal(20, llama.TopK);                       // NOT null
        Assert.Equal(0.05, llama.MinP);                     // NOT null
        Assert.Equal(0, llama.RepeatPenalty);               // NOT null
        Assert.Equal(0, llama.RepeatLastNTokens);           // NOT null
        Assert.Equal(0, llama.PresencePenalty);             // NOT null
        Assert.Equal(0, llama.FrequencyPenalty);            // NOT null
        Assert.Equal(-1, llama.Seed);                       // NOT null
        Assert.Equal(8, llama.ThreadCount);                 // NOT null
        Assert.Equal(2048, llama.ReasoningBudget);          // NOT null
        Assert.Equal("--wait, I've exhausted my reasoning budget. I must give my final response now.", llama.ReasoningBudgetMessage); // NOT null
        Assert.Equal(300, llama.ServerTimeoutSeconds);      // NOT null
        Assert.NotNull(llama.ExtraArgs);
        Assert.Equal(2, llama.ExtraArgs.Count);
        
        // Judge settings should come from YAML
        var judge = merged.Judge;
        Assert.NotNull(judge);
        Assert.True(judge.Enable);                          // NOT false
        Assert.Equal("translation-judge-template", judge.JudgePromptTemplate);  // NOT "standard"
        
        Assert.NotNull(judge.ServerConfig);
        Assert.True(judge.ServerConfig.Manage);
        Assert.Equal(@"C:\AI\granite-3.1-1b-a400m-instruct-IQ4_XS.gguf", judge.ServerConfig.Model.FilePath);
        
        Assert.NotNull(judge.ServerSettings);
        var judgeSettings = judge.ServerSettings;
        Assert.Equal(65536, judgeSettings.ContextWindowTokens);  // NOT 2048
        Assert.Equal(8, judgeSettings.ParallelSlotCount);        // NOT 4
        Assert.Equal(998, judgeSettings.GpuLayerCount);          // NOT null
        Assert.Equal(0.9, judgeSettings.TopP);                   // NOT null
        Assert.Equal(25, judgeSettings.TopK);                    // NOT null
        Assert.Equal(0.1, judgeSettings.MinP);                   // NOT null
        Assert.Equal(2048, judgeSettings.ReasoningBudget);       // NOT null
        Assert.Equal("--wait, I've exhausted my reasoning budget. I must give my final response now.", judgeSettings.ReasoningBudgetMessage); // NOT null
        Assert.Equal(350, judgeSettings.ServerTimeoutSeconds);   // NOT null
        Assert.NotNull(judgeSettings.ExtraArgs);
        Assert.Equal(2, judgeSettings.ExtraArgs.Count);
        
        // DataSource should come from YAML
        var ds = merged.DataSource;
        Assert.Equal(DataSourceKind.SplitDirectories, ds.Kind);  // NOT SingleFile
        Assert.NotNull(ds.PromptDirectory);
        Assert.NotNull(ds.ExpectedDirectory);
    }

    [Fact]
    public async Task ResolveConfig_WithOnlyYamlFile_AllSettingsLoadCorrectly()
    {
        var yamlPath = @"C:\AI\SeevalocalFastest320Test.yml";

        // Skip if file doesn't exist
        if (!File.Exists(yamlPath))
        {
            System.Console.WriteLine($"Skipping test - file not found: {yamlPath}");
            return;
        }

        // Load only the YAML file (no defaults)
        var loader = new SettingsFileLoader(Microsoft.Extensions.Logging.Abstractions.NullLogger<SettingsFileLoader>.Instance);
        var loadResult = await loader.LoadAsync(yamlPath);
        
        Assert.True(loadResult.IsSuccess);
        var yamlConfig = loadResult.Value;
        
        // Resolve with only the YAML file
        var merger = new ConfigurationMerger();
        var resolved = merger.Merge([yamlConfig]);
        
        // Verify key settings that were reported as not loading
        Assert.Equal("Translation", resolved.Run.PipelineName);
        Assert.Equal(16384, resolved.LlamaServer.ContextWindowTokens);
        Assert.Equal(8, resolved.LlamaServer.ParallelSlotCount);
        Assert.Equal(999, resolved.LlamaServer.GpuLayerCount);
        Assert.Equal(0.95, resolved.LlamaServer.TopP);
        Assert.Equal(20, resolved.LlamaServer.TopK);
        Assert.Equal(0.05, resolved.LlamaServer.MinP);
        Assert.Equal(2048, resolved.LlamaServer.ReasoningBudget);
        Assert.Equal("--wait, I've exhausted my reasoning budget. I must give my final response now.", resolved.LlamaServer.ReasoningBudgetMessage);
        
        Assert.True(resolved.Judge.Enable);
        Assert.Equal("translation-judge-template", resolved.Judge.JudgePromptTemplate);
        Assert.Equal(65536, resolved.Judge.ServerSettings.ContextWindowTokens);
        Assert.Equal(8, resolved.Judge.ServerSettings.ParallelSlotCount);
        Assert.Equal(998, resolved.Judge.ServerSettings.GpuLayerCount);
        Assert.Equal(0.9, resolved.Judge.ServerSettings.TopP);
        Assert.Equal(25, resolved.Judge.ServerSettings.TopK);
        Assert.Equal(0.1, resolved.Judge.ServerSettings.MinP);
        Assert.Equal(2048, resolved.Judge.ServerSettings.ReasoningBudget);
    }
}
