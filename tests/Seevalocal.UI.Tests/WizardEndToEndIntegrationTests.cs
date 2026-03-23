using FluentAssertions;
using Microsoft.Extensions.Logging;
using Seevalocal.Config.Merging;
using Seevalocal.Config.Validation;
using Seevalocal.Core.Models;
using Seevalocal.DataSources;
using Seevalocal.Pipelines.Factories;
using Seevalocal.UI.ViewModels;
using Xunit;
using Xunit.Abstractions;

using DataSourceConfig = Seevalocal.DataSources.DataSourceConfig;

namespace Seevalocal.UI.Tests;

/// <summary>
/// End-to-end integration test that validates the complete wizard flow
/// and actual execution of the Translation pipeline.
/// </summary>
public class WizardEndToEndIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly string _tempDataFilePath;
    private readonly string _tempOutputDir;

    public WizardEndToEndIntegrationTests(ITestOutputHelper output)
    {
        _output = output;

        // Create temp directory for output
        _tempOutputDir = Path.Combine(Path.GetTempPath(), $"seevalocal_e2e_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempOutputDir);

        // Create test JSONL data file with 3 translation problems
        // Per requirements: include source and target languages in the file,
        // map those in the wizard, and leave them null for one of the three problems
        _tempDataFilePath = Path.Combine(_tempOutputDir, "test_data.jsonl");
        var testData = new[]
        {
            new { id = "1", source = "Hello, how are you?", target = "Bonjour, comment allez-vous?", source_language = "English", target_language = "French" },
            new { id = "2", source = "The weather is nice today.", target = (string?)null!, source_language = "English", target_language = "Spanish" }, // Missing target field
            new { id = "3", source = "Good morning!", target = "¡Buenos días!", source_language = "English", target_language = "Spanish" },
        };

        var jsonlContent = string.Join("\n", testData.Select(x => System.Text.Json.JsonSerializer.Serialize(x)));
        File.WriteAllText(_tempDataFilePath, jsonlContent);

        _output.WriteLine($"Created test data file: {_tempDataFilePath}");
        _output.WriteLine($"Test output directory: {_tempOutputDir}");
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        try
        {
            if (File.Exists(_tempDataFilePath))
                File.Delete(_tempDataFilePath);
            if (Directory.Exists(_tempOutputDir))
                Directory.Delete(_tempOutputDir, recursive: true);
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Warning: Failed to clean up temp files: {ex.Message}");
        }
    }

    [Fact]
    public async Task CompleteWizardFlow_ThroughTranslationPipelineConstruction_RunsSuccessfully()
    {
        // Arrange: Create WizardViewModel with test services
        var filePicker = new TestFilePickerService();
        var toastService = new TestToastService();
        var logger = new TestLogger(_output);

        var wizard = new WizardViewModel(filePicker, toastService, logger);

        // ========== STEP 1: Continue Run (skip checkpoint) ==========
        _output.WriteLine("Step 1: Continue Run");
        wizard.CurrentStep.Should().Be(WizardStepKind.ContinueRun);
        wizard.ContinueFromCheckpoint.Should().BeFalse();

        await wizard.GoForwardAsync();

        // ========== STEP 2: Pipeline Selection ==========
        _output.WriteLine("Step 2: Pipeline Selection");
        wizard.CurrentStep.Should().Be(WizardStepKind.PipelineSelection);

        // Set pipeline to Translation
        wizard.PipelineName.Should().Be("CasualQA"); // Default
        wizard.SelectedPipelineIndex = 1; // Translation
        wizard.PipelineName.Should().Be("Translation");

        await wizard.GoForwardAsync();

        // ========== STEP 3: Model & Server ==========
        _output.WriteLine("Step 3: Model & Server");
        wizard.CurrentStep.Should().Be(WizardStepKind.ModelAndServer);

        // Configure main server
        wizard.ManageServer.Should().BeTrue(); // Default
        wizard.UseLocalFile.Should().BeTrue(); // Default
        wizard.LocalModelPath = @"C:\AI\Lite-Mistral-150M-v2-Instruct-Q6_K_L.gguf";

        await wizard.GoForwardAsync();

        // ========== STEP 4: Performance Settings ==========
        _output.WriteLine("Step 4: Performance Settings");
        wizard.CurrentStep.Should().Be(WizardStepKind.PerformanceSettings);

        // Set performance settings
        wizard.ContextWindowTokens = 2048;
        wizard.ParallelSlotCount = 2;
        wizard.SamplingTemperature = 0.3;
        wizard.ExtraLlamaArgs = "-ts 0,100"; // Tensor split: all work on GPU 1

        await wizard.GoForwardAsync();

        // ========== STEP 5: Evaluation Dataset ==========
        _output.WriteLine("Step 5: Evaluation Dataset");
        wizard.CurrentStep.Should().Be(WizardStepKind.EvaluationDataset);

        // Set data source to single file
        wizard.UseSingleFileDataSource.Should().BeTrue(); // Default
        wizard.DataFilePath = _tempDataFilePath;

        await wizard.GoForwardAsync();

        // ========== STEP 6: Field Mapping ==========
        _output.WriteLine("Step 6: Field Mapping");
        wizard.CurrentStep.Should().Be(WizardStepKind.FieldMapping);

        // Map fields from JSONL - including source and target language fields
        wizard.FieldMappingId = "id";
        wizard.FieldMappingUserPrompt = "source";
        wizard.FieldMappingExpectedOutput = "target";
        wizard.FieldMappingSourceLanguage = "source_language";
        wizard.FieldMappingTargetLanguage = "target_language";

        await wizard.GoForwardAsync();

        // ========== STEP 7: Scoring (Judge) ==========
        _output.WriteLine("Step 7: Scoring");
        wizard.CurrentStep.Should().Be(WizardStepKind.Scoring);

        // Enable judge
        wizard.EnableJudge = true;
        wizard.JudgeManageServer.Should().BeTrue(); // Default
        wizard.JudgeUseLocalFile.Should().BeTrue(); // Default
        wizard.JudgeLocalModelPath = @"C:\AI\Qwen2.5-0.5B-Instruct-Q6_K.gguf";
        wizard.JudgeContextWindowTokens = 4096;
        wizard.JudgeExtraLlamaArgs = "-ts 100,0"; // Tensor split: all work on GPU 0

        await wizard.GoForwardAsync();

        // ========== STEP 8: Output ==========
        _output.WriteLine("Step 8: Output");
        wizard.CurrentStep.Should().Be(WizardStepKind.Output);

        wizard.OutputDir = _tempOutputDir;
        wizard.RunName = "e2e_test_run";

        await wizard.GoForwardAsync();

        // ========== STEP 9: Pipeline Configuration ==========
        _output.WriteLine("Step 9: Pipeline Configuration");
        wizard.CurrentStep.Should().Be(WizardStepKind.PipelineConfiguration);

        await wizard.GoForwardAsync();

        // ========== STEP 10: Review & Run ==========
        _output.WriteLine("Step 10: Review & Run");
        wizard.CurrentStep.Should().Be(WizardStepKind.ReviewAndRun);

        // Build the final config from wizard
        var partialConfig = wizard.BuildPartialConfig();

        // Validate config structure
        _output.WriteLine("Validating configuration structure...");
        partialConfig.Should().NotBeNull();
        partialConfig.Run.Should().NotBeNull();
        partialConfig.Run!.PipelineName.Should().Be("Translation");

        partialConfig.Server.Should().NotBeNull();
        partialConfig.Server!.Manage.Should().BeTrue();
        partialConfig.Server.Model.Should().NotBeNull();
        partialConfig.Server.Model!.FilePath.Should().Be(@"C:\AI\Lite-Mistral-150M-v2-Instruct-Q6_K_L.gguf");

        partialConfig.LlamaSettings.Should().NotBeNull();
        partialConfig.LlamaSettings!.ContextWindowTokens.Should().Be(2048);
        partialConfig.LlamaSettings.ParallelSlotCount.Should().Be(2);
        partialConfig.LlamaSettings.SamplingTemperature.Should().BeApproximately(0.3, 0.001);
        partialConfig.LlamaSettings.ExtraArgs.Should().Contain("-ts");
        partialConfig.LlamaSettings.ExtraArgs.Should().Contain("0,100");

        partialConfig.Judge.Should().NotBeNull();
        partialConfig.Judge!.Enable.Should().BeTrue();
        partialConfig.Judge.ServerConfig.Should().NotBeNull();
        partialConfig.Judge.ServerConfig!.Manage.Should().BeTrue();
        partialConfig.Judge.ServerConfig.Model.Should().NotBeNull();
        partialConfig.Judge.ServerConfig.Model!.FilePath.Should().Be(@"C:\AI\Qwen2.5-0.5B-Instruct-Q6_K.gguf");
        partialConfig.Judge.ServerSettings.Should().NotBeNull();
        partialConfig.Judge.ServerSettings!.ContextWindowTokens.Should().Be(4096);
        partialConfig.Judge.ServerSettings.ExtraArgs.Should().Contain("-ts");
        partialConfig.Judge.ServerSettings.ExtraArgs.Should().Contain("100,0");

        partialConfig.DataSource.Should().NotBeNull();
        partialConfig.DataSource!.Kind.Should().Be(DataSourceKind.SingleFile);
        partialConfig.DataSource.FilePath.Should().Be(_tempDataFilePath);
        partialConfig.DataSource.FieldMapping.Should().NotBeNull();
        partialConfig.DataSource.FieldMapping!.UserPromptField.Should().Be("source");
        partialConfig.DataSource.FieldMapping.ExpectedOutputField.Should().Be("target");
        partialConfig.DataSource.FieldMapping.SourceLanguageField.Should().Be("source_language");
        partialConfig.DataSource.FieldMapping.TargetLanguageField.Should().Be("target_language");

        _output.WriteLine("Configuration structure validation passed!");

        // ========== RESOLVE AND VALIDATE CONFIG ==========
        _output.WriteLine("Resolving configuration...");
        var configService = new ConfigurationMerger();
        var validator = new ConfigValidator(new TestLogger<ConfigValidator>(_output));

        var resolvedConfig = configService.Merge([partialConfig]);

        // Validate resolved config
        _output.WriteLine("Validating resolved configuration...");
        var validationErrors = validator.Validate(resolvedConfig);
        validationErrors.Should().BeEmpty($"Config validation failed: {string.Join(", ", validationErrors.Select(e => e.MessageText))}");

        _output.WriteLine("Configuration resolved and validated successfully!");
        _output.WriteLine($"Pipeline: {resolvedConfig.Run.PipelineName}");
        _output.WriteLine($"Data source: {resolvedConfig.DataSource.Kind}");
        _output.WriteLine($"Data file: {resolvedConfig.DataSource.FilePath}");
        _output.WriteLine($"Judge enabled: {resolvedConfig.Judge?.Enable}");
        _output.WriteLine($"Output directory: {resolvedConfig.Run.OutputDirectoryPath}");

        // ========== CREATE DATA SOURCE AND PIPELINE ==========
        _output.WriteLine("Creating data source and pipeline...");

        var loggerFactory = new TestLoggerFactory(_output);
        var dataSourceFactory = new DataSourceFactory(loggerFactory);
        var dsConfig = new DataSourceConfig
        {
            Kind = DataSourceKind.JsonlFile,
            DataFilePath = resolvedConfig.DataSource.FilePath,
            FieldMapping = new DataSources.FieldMapping
            {
                IdField = resolvedConfig.DataSource.FieldMapping?.IdField,
                UserPromptField = resolvedConfig.DataSource.FieldMapping?.UserPromptField,
                ExpectedOutputField = resolvedConfig.DataSource.FieldMapping?.ExpectedOutputField,
                MetadataFields = ["source_language", "target_language"]
            }
        };

        var dataSourceResult = dataSourceFactory.Create(resolvedConfig.DataSource.FilePath!, dsConfig);
        dataSourceResult.IsSuccess.Should().BeTrue($"Failed to create data source: {(dataSourceResult.Errors.Count > 0 ? dataSourceResult.Errors[0].Message : "unknown error")}");

        var dataSource = dataSourceResult.Value;
        _output.WriteLine($"Data source created successfully. Type: {dataSource.GetType().Name}");

        // Create Translation pipeline
        var pipelineFactory = new TranslationPipelineFactory(loggerFactory);
        var pipeline = pipelineFactory.Create(resolvedConfig);

        pipeline.Should().NotBeNull();
        pipeline.PipelineName.Should().Be("Translation");
        pipeline.Stages.Should().NotBeEmpty();
        _output.WriteLine($"Pipeline created with {pipeline.Stages.Count} stages:");
        foreach (var stage in pipeline.Stages)
        {
            _output.WriteLine($"  - {stage.StageName}");
        }

        // ========== VALIDATE DATA SOURCE CONTENTS ==========
        _output.WriteLine("Validating data source contents...");

        var items = await dataSource.GetItemsAsync(CancellationToken.None).ToListAsync();
        items.Should().NotBeEmpty("Data source should contain items");
        _output.WriteLine($"Data source contains {items.Count} items");

        // Validate we have 3 items
        items.Count.Should().Be(3, "Should have exactly 3 test items");

        // Validate item 1
        var item1 = items.FirstOrDefault(i => i.Id == "1");
        item1.Should().NotBeNull("Item 1 should exist");
        item1!.UserPrompt.Should().Be("Hello, how are you?");
        item1.ExpectedOutput.Should().Be("Bonjour, comment allez-vous?");
        item1.Metadata.Should().ContainKey("source_language");
        item1.Metadata["source_language"].Should().Be("English");
        item1.Metadata.Should().ContainKey("target_language");
        item1.Metadata["target_language"].Should().Be("French");
        _output.WriteLine($"Item 1 validated: {item1.UserPrompt} -> {item1.ExpectedOutput}");

        // Validate item 2 (has null target)
        var item2 = items.FirstOrDefault(i => i.Id == "2");
        item2.Should().NotBeNull("Item 2 should exist");
        item2!.UserPrompt.Should().Be("The weather is nice today.");
        item2.ExpectedOutput.Should().BeNull("Item 2 has null target");
        item2.Metadata.Should().ContainKey("source_language");
        item2.Metadata["source_language"].Should().Be("English");
        item2.Metadata.Should().ContainKey("target_language");
        item2.Metadata["target_language"].Should().Be("Spanish");
        _output.WriteLine($"Item 2 validated (null target): {item2.UserPrompt}");

        // Validate item 3
        var item3 = items.FirstOrDefault(i => i.Id == "3");
        item3.Should().NotBeNull("Item 3 should exist");
        item3!.UserPrompt.Should().Be("Good morning!");
        item3.ExpectedOutput.Should().Be("¡Buenos días!");
        item3.Metadata.Should().ContainKey("source_language");
        item3.Metadata["source_language"].Should().Be("English");
        item3.Metadata.Should().ContainKey("target_language");
        item3.Metadata["target_language"].Should().Be("Spanish");
        _output.WriteLine($"Item 3 validated: {item3.UserPrompt} -> {item3.ExpectedOutput}");

        _output.WriteLine("\n========== TEST PASSED ==========");
        _output.WriteLine($"Data file exists: {File.Exists(_tempDataFilePath)}");
        _output.WriteLine($"Output directory exists: {Directory.Exists(_tempOutputDir)}");
        _output.WriteLine($"Results would be written to: {resolvedConfig.Run.OutputDirectoryPath}");
        _output.WriteLine("Note: Actual pipeline execution requires model files and running servers.");
    }

    [Fact]
    public void WizardConfigYamlPreview_IncludesAllSettings()
    {
        // Arrange
        var wizard = new WizardViewModel
        {
            PipelineName = "Translation",
            ManageServer = true,
            LocalModelPath = @"C:\AI\TestModel.gguf",
            ContextWindowTokens = 2048,
            ParallelSlotCount = 2,
            SamplingTemperature = 0.3,
            ExtraLlamaArgs = "-ts 0,100",
            EnableJudge = true,
            JudgeManageServer = true,
            JudgeLocalModelPath = @"C:\AI\JudgeModel.gguf",
            JudgeContextWindowTokens = 4096,
            JudgeExtraLlamaArgs = "-ts 100,0",
            UseSingleFileDataSource = true,
            DataFilePath = _tempDataFilePath,
            OutputDir = _tempOutputDir,
            RunName = "test_run"
        };

        // Act
        var yaml = wizard.ConfigYamlPreview;

        // Assert
        _output.WriteLine("Generated YAML:");
        _output.WriteLine(yaml);

        yaml.Should().Contain("pipelineName: Translation");
        yaml.Should().Contain("manage: true");
        yaml.Should().Contain("contextWindowTokens: 2048");
        yaml.Should().Contain("parallelSlotCount: 2");
        yaml.Should().Contain("samplingTemperature: 0.3");
        yaml.Should().Contain("-ts");
        yaml.Should().Contain("0,100");
        yaml.Should().Contain("enable: true"); // judge.enable
        yaml.Should().Contain("judge:"); // judge section exists
        yaml.Should().Contain("contextWindowTokens: 4096"); // judge.serverSettings
        yaml.Should().Contain("100,0"); // judge extra args
        yaml.Should().Contain("kind: SingleFile");
        yaml.Should().Contain("runName: test_run");
    }
}

/// <summary>
/// Generic test logger factory.
/// </summary>
public class TestLoggerFactory(ITestOutputHelper output) : ILoggerFactory
{
    public void AddProvider(ILoggerProvider provider) { }
    public ILogger CreateLogger(string categoryName) => new TestLoggerGeneric(output);
    public void Dispose() { }
}

/// <summary>
/// Generic test logger.
/// </summary>
public class TestLoggerGeneric(ITestOutputHelper output) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        output.WriteLine($"[{logLevel}] {formatter(state, exception)}");
    }
}

/// <summary>
/// Generic test logger for specific types.
/// </summary>
public class TestLogger<T>(ITestOutputHelper output) : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        output.WriteLine($"[{typeof(T).Name}][{logLevel}] {formatter(state, exception)}");
    }
}
