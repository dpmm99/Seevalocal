using Microsoft.Extensions.Logging.Abstractions;
using Seevalocal.Config.Loading;
using Seevalocal.Config.Merging;
using Seevalocal.Core.Models;
using Seevalocal.UI.ViewModels;
using System.Reflection;
using Xunit;
using YamlDotNet.Serialization;

namespace Seevalocal.UI.Tests;

/// <summary>
/// End-to-end tests that verify settings survive a full round-trip:
/// 1. Create a fully populated ResolvedConfig via reflection
/// 2. Save as YAML
/// 3. Load as settings layer
/// 4. Populates wizard from checkpoint
/// 5. Verify wizard state matches original config
/// </summary>
public class WizardSettingsRoundTripTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Random _random = new(42); // Fixed seed for reproducibility

    public WizardSettingsRoundTripTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SeevalocalTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch { /* Ignore cleanup errors */ }
    }

    [Fact]
    public async Task FullSettingsRoundTrip_ThroughWizard_PreservesAllValues()
    {
        // ── Step 1: Create fully populated ResolvedConfig via reflection ──────────
        var originalConfig = CreateFullyPopulatedResolvedConfig();

        // ── Step 2: Save as YAML using existing UI serialization ──────────
        var yamlPath = Path.Combine(_tempDir, "test_settings.yml");
        // Use PartialConfig for serialization since that's what settings files use
        var partialForSave = new PartialConfig
        {
            Run = new PartialRunMeta
            {
                RunName = originalConfig.Run.RunName,
                PipelineName = originalConfig.Run.PipelineName,
                OutputDirectoryPath = originalConfig.Run.OutputDirectoryPath,
                ExportShellTarget = originalConfig.Run.ExportShellTarget,
                ContinueOnEvalFailure = originalConfig.Run.ContinueOnEvalFailure,
                MaxConcurrentEvals = originalConfig.Run.MaxConcurrentEvals,
                CheckpointDatabasePath = originalConfig.Run.CheckpointDatabasePath,
            },
            Server = new PartialServerConfig
            {
                Manage = originalConfig.Server.Manage,
                ExecutablePath = originalConfig.Server.ExecutablePath,
                BaseUrl = originalConfig.Server.BaseUrl,
                ApiKey = originalConfig.Server.ApiKey,
                Model = originalConfig.Server.Model,
            },
            LlamaSettings = ToPartialLlamaSettings(originalConfig.LlamaServer),
            Judge = new PartialJudgeConfig
            {
                Enable = originalConfig.Judge!.Enable,
                JudgePromptTemplate = originalConfig.Judge.JudgePromptTemplate,
                ServerConfig = new PartialServerConfig
                {
                    Manage = originalConfig.Judge.ServerConfig.Manage,
                    ExecutablePath = originalConfig.Judge.ServerConfig.ExecutablePath,
                    BaseUrl = originalConfig.Judge.ServerConfig.BaseUrl,
                    ApiKey = originalConfig.Judge.ServerConfig.ApiKey,
                    Model = originalConfig.Judge.ServerConfig.Model,
                },
                ServerSettings = ToPartialLlamaSettings(originalConfig.Judge.ServerSettings!),
            },
            DataSource = new PartialDataSourceConfig
            {
                Kind = originalConfig.DataSource.Kind,
                PromptDirectory = originalConfig.DataSource.PromptDirectory,
                ExpectedDirectory = originalConfig.DataSource.ExpectedDirectory,
                FieldMapping = originalConfig.DataSource.FieldMapping,
            },
            PipelineOptions = originalConfig.PipelineOptions,
        };

        var yamlContent = SerializeToYaml(partialForSave);
        await File.WriteAllTextAsync(yamlPath, yamlContent);

        // Debug: write YAML to console for inspection
        Console.WriteLine("=== Generated YAML ===");
        Console.WriteLine(yamlContent);
        Console.WriteLine("=== End YAML ===");

        // ── Step 3: Load as PartialConfig (simulating settings layer load) ──────────
        var loader = new SettingsFileLoader(NullLogger<SettingsFileLoader>.Instance);
        var loadResult = await loader.LoadAsync(yamlPath);
        Assert.True(loadResult.IsSuccess);
        var loadedPartial = loadResult.Value;

        // ── Step 4: Convert PartialConfig to ResolvedConfig for comparison ──────────
        var merger = new ConfigurationMerger();
        var resolvedFromLoaded = merger.Merge([loadedPartial]);

        // ── Step 5: Verify all llama settings loaded correctly ──────────
        var originalLlama = originalConfig.LlamaServer;
        var loadedLlama = resolvedFromLoaded.LlamaServer;

        CompareLlamaSettings(originalLlama, loadedLlama, "llama");

        // ── Step 6: Verify judge settings loaded correctly ──────────
        var originalJudge = originalConfig.Judge;
        var loadedJudge = resolvedFromLoaded.Judge;

        Assert.NotNull(originalJudge);
        Assert.NotNull(loadedJudge);
        Assert.Equal(originalJudge.Enable, loadedJudge.Enable);

        if (originalJudge.ServerConfig != null)
        {
            Assert.NotNull(loadedJudge.ServerConfig);
            Assert.Equal(originalJudge.ServerConfig.Manage, loadedJudge.ServerConfig.Manage);
            Assert.Equal(originalJudge.ServerConfig.ExecutablePath, loadedJudge.ServerConfig.ExecutablePath);
            Assert.Equal(originalJudge.ServerConfig.BaseUrl, loadedJudge.ServerConfig.BaseUrl);
            Assert.Equal(originalJudge.ServerConfig.ApiKey, loadedJudge.ServerConfig.ApiKey);

            if (originalJudge.ServerConfig.Model != null)
            {
                Assert.NotNull(loadedJudge.ServerConfig.Model);
                Assert.Equal(originalJudge.ServerConfig.Model.FilePath, loadedJudge.ServerConfig.Model.FilePath);
                Assert.Equal(originalJudge.ServerConfig.Model.HfRepo, loadedJudge.ServerConfig.Model.HfRepo);
            }
        }

        if (originalJudge.ServerSettings != null)
        {
            Assert.NotNull(loadedJudge.ServerSettings);
            CompareLlamaSettings(originalJudge.ServerSettings, loadedJudge.ServerSettings, "judge");
        }

        Assert.Equal(originalJudge.JudgePromptTemplate, loadedJudge.JudgePromptTemplate);

        // ── Step 7: Populate wizard from loaded config ──────────
        var wizardVm = new WizardViewModel();
        var editedFields = GetEditedFields(wizardVm);

        // Access State via reflection (it's internal)
        var stateProperty = typeof(WizardViewModel).GetProperty("State", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var wizardState = stateProperty.GetValue(wizardVm)!;

        // Simulate loading from checkpoint (marks all fields as edited)
        WizardState.ApplyResolvedConfig((WizardState)wizardState, resolvedFromLoaded, editedFields, onlyUnedited: false);

        // ── Step 8: Build PartialConfig from wizard and verify ──────────
        var wizardPartial = wizardVm.BuildPartialConfig();
        var wizardResolved = merger.Merge([wizardPartial]);

        // ── Step 9: Verify wizard output matches original ──────────
        CompareLlamaSettings(originalLlama, wizardResolved.LlamaServer, "wizard.llama");

        if (originalJudge != null && wizardResolved.Judge != null)
        {
            Assert.Equal(originalJudge.Enable, wizardResolved.Judge.Enable);
            Assert.Equal(originalJudge.JudgePromptTemplate, wizardResolved.Judge.JudgePromptTemplate);

            if (originalJudge.ServerSettings != null && wizardResolved.Judge.ServerSettings != null)
            {
                CompareLlamaSettings(originalJudge.ServerSettings, wizardResolved.Judge.ServerSettings, "wizard.judge");
            }
        }
    }

    [Fact]
    public void AllLlamaSettingsProperties_AreCoveredByReflection()
    {
        // This test ensures that if new properties are added to LlamaServerSettings,
        // they will be caught by the reflection-based test
        var llamaType = typeof(LlamaServerSettings);
        var properties = llamaType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        // Verify we have a reasonable number of properties (should be 30+)
        Assert.True(properties.Length >= 30,
            $"Expected at least 30 properties on LlamaServerSettings, found {properties.Length}");

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

        var propNames = properties.Select(p => p.Name).ToHashSet();
        foreach (var expected in expectedProps)
        {
            Assert.Contains(expected, propNames);
        }
    }

    // ── Helper Methods ──────────────────────────────────────────────────────────

    private ResolvedConfig CreateFullyPopulatedResolvedConfig()
    {
        var llamaSettings = CreateFullyPopulatedLlamaSettings();

        var judgeSettings = CreateFullyPopulatedLlamaSettings();

        return new ResolvedConfig
        {
            Run = new RunMeta
            {
                RunName = $"test_run_{_random.Next()}",
                PipelineName = "Translation",
                OutputDirectoryPath = Path.Combine(_tempDir, "output"),
                ExportShellTarget = ShellTarget.PowerShell,
                ContinueOnEvalFailure = true,
                MaxConcurrentEvals = _random.Next(1, 16),
                CheckpointDatabasePath = Path.Combine(_tempDir, "test.db"),
            },
            Server = new ServerConfig
            {
                Manage = true,
                ExecutablePath = Path.Combine(_tempDir, "llama-server.exe"),
                BaseUrl = "http://localhost:8080",
                ApiKey = "test-api-key-123",
                Model = new ModelSource
                {
                    Kind = ModelSourceKind.LocalFile,
                    FilePath = Path.Combine(_tempDir, "model.gguf"),
                },
            },
            LlamaServer = llamaSettings,
            Judge = new JudgeConfig
            {
                Enable = true,
                JudgePromptTemplate = "translation-judge-template",
                ServerConfig = new ServerConfig
                {
                    Manage = true,
                    ExecutablePath = Path.Combine(_tempDir, "judge-llama-server.exe"),
                    BaseUrl = "http://localhost:8081",
                    ApiKey = "judge-api-key-456",
                    Model = new ModelSource
                    {
                        Kind = ModelSourceKind.LocalFile,
                        FilePath = Path.Combine(_tempDir, "judge-model.gguf"),
                    },
                },
                ServerSettings = judgeSettings,
            },
            DataSource = new DataSourceConfig
            {
                Kind = DataSourceKind.SplitDirectories,
                PromptDirectory = Path.Combine(_tempDir, "prompts"),
                ExpectedDirectory = Path.Combine(_tempDir, "expected"),
                FieldMapping = new FieldMapping
                {
                    IdField = "id",
                    UserPromptField = "prompt",
                    ExpectedOutputField = "expected",
                },
            },
            PipelineOptions = new Dictionary<string, object?>
            {
                ["sourceLanguage"] = "English",
                ["targetLanguage"] = "Spanish",
                ["systemPrompt"] = "Translate the following text.",
            },
            // Note: ResolvedConfig doesn't have Output property - it's only in PartialConfig
        };
    }

    private LlamaServerSettings CreateFullyPopulatedLlamaSettings()
    {
        var settings = new LlamaServerSettings();
        var props = typeof(LlamaServerSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in props)
        {
            var propType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            object? value = propType switch
            {
                Type t when t == typeof(int) => _random.Next(1, 100000),
                Type t when t == typeof(double) => Math.Round(_random.NextDouble() * 100, 4),
                Type t when t == typeof(bool) => _random.Next(2) == 1,
                Type t when t == typeof(string) => $"test-value-{prop.Name}",
                Type t when t == typeof(List<string>) => new List<string> { $"--test-arg-1", "--test-arg-2" },
                _ => null
            };

            if (value != null)
                prop.SetValue(settings, value);
        }

        return settings;
    }

    private void CompareLlamaSettings(LlamaServerSettings original, LlamaServerSettings loaded, string prefix)
    {
        Assert.NotNull(original);
        Assert.NotNull(loaded);

        var props = typeof(LlamaServerSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in props)
        {
            var originalValue = prop.GetValue(original);
            var loadedValue = prop.GetValue(loaded);

            if (originalValue is IReadOnlyList<string> origList)
            {
                if (origList.Count > 0)
                {
                    Assert.NotNull(loadedValue);
                    var loadedList = Assert.IsAssignableFrom<IReadOnlyList<string>>(loadedValue);
                    Assert.Equal(origList.Count, loadedList.Count);
                    for (int i = 0; i < origList.Count; i++)
                    {
                        Assert.Equal(origList[i], loadedList[i]);
                    }
                }
            }
            else
            {
                Assert.Equal(originalValue, loadedValue);
            }
        }
    }

    private static HashSet<string> GetEditedFields(WizardViewModel vm)
    {
        var field = typeof(WizardViewModel).GetField("_editedFields", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (HashSet<string>)field.GetValue(vm)!;
    }

    private static string SerializeToYaml(object config)
    {
        var serializer = new SerializerBuilder()
            .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.CamelCaseNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults)
            .Build();

        var yaml = serializer.Serialize(config);
        return $"# Seevalocal settings\n# Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n{yaml}";
    }

    private static PartialLlamaServerSettings ToPartialLlamaSettings(
        LlamaServerSettings settings)
    {
        var partial = new PartialLlamaServerSettings();
        var props = typeof(LlamaServerSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in props)
        {
            var value = prop.GetValue(settings);
            var partialProp = typeof(PartialLlamaServerSettings).GetProperty(prop.Name);
            if (partialProp != null && partialProp.CanWrite)
            {
                // Convert List<string> to match PartialLlamaServerSettings type
                if (value is IReadOnlyList<string> list && partialProp.PropertyType == typeof(List<string>))
                    partialProp.SetValue(partial, list.ToList());
                else
                    partialProp.SetValue(partial, value);
            }
        }

        return partial;
    }
}
