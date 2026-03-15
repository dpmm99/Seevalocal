using FluentAssertions;
using Seevalocal.Core.Models;
using Seevalocal.UI.Services;
using Xunit;

namespace Seevalocal.UI.Tests;

public class FieldMappingDetectorTests
{
    [Fact]
    public async Task AnalyzeJsonFile_DetectsFields_And_SuggestsMappings()
    {
        // Arrange
        var tempFile = Path.GetTempFileName() + ".json";
        const string jsonData = """
            [
                {"id": "1", "prompt": "What is AI?", "answer": "Artificial Intelligence"},
                {"id": "2", "prompt": "What is ML?", "answer": "Machine Learning"}
            ]
            """;
        await File.WriteAllTextAsync(tempFile, jsonData);

        try
        {
            // Act
            var analysis = await FieldMappingDetector.AnalyzeFileAsync(tempFile);

            // Assert
            analysis.HasError.Should().BeFalse();
            analysis.AvailableFields.Should().Contain(["id", "prompt", "answer"]);
            analysis.SuggestedMapping.IdField.Should().Be("id");
            analysis.SuggestedMapping.UserPromptField.Should().Be("prompt");
            analysis.SuggestedMapping.ExpectedOutputField.Should().Be("answer");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task AnalyzeJsonlFile_DetectsFields_And_SuggestsMappings()
    {
        // Arrange
        var tempFile = Path.GetTempFileName() + ".jsonl";
        const string jsonlData = """
            {"question": "What is 2+2?", "response": "4"}
            {"question": "What is 3+3?", "response": "6"}
            """;
        await File.WriteAllTextAsync(tempFile, jsonlData);

        try
        {
            // Act
            var analysis = await FieldMappingDetector.AnalyzeFileAsync(tempFile);

            // Assert
            analysis.HasError.Should().BeFalse();
            analysis.AvailableFields.Should().Contain(["question", "response"]);
            analysis.SuggestedMapping.UserPromptField.Should().Be("question");
            analysis.SuggestedMapping.ExpectedOutputField.Should().Be("response");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task AnalyzeCsvFile_DetectsFields_And_SuggestsMappings()
    {
        // Arrange
        var tempFile = Path.GetTempFileName() + ".csv";
        const string csvData = """
            input,output,context
            "Hello","Hi","greeting"
            "Goodbye","Bye","farewell"
            """;
        await File.WriteAllTextAsync(tempFile, csvData);

        try
        {
            // Act
            var analysis = await FieldMappingDetector.AnalyzeFileAsync(tempFile);

            // Assert
            analysis.HasError.Should().BeFalse();
            analysis.AvailableFields.Should().Contain(["input", "output", "context"]);
            analysis.SuggestedMapping.UserPromptField.Should().Be("input");
            analysis.SuggestedMapping.ExpectedOutputField.Should().Be("output");
            analysis.SuggestedMapping.SystemPromptField.Should().Be("context");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task AnalyzeFile_WithSmartDefaults_MatchesCommonPatterns()
    {
        // Arrange - test various common field name patterns
        var tempFile = Path.GetTempFileName() + ".json";

        // Test with alternative field names
        const string jsonData = """
            [
                {"item_id": "1", "user_input": "Hello", "expected_result": "Hi"}
            ]
            """;
        await File.WriteAllTextAsync(tempFile, jsonData);

        try
        {
            // Act
            var analysis = await FieldMappingDetector.AnalyzeFileAsync(tempFile);

            // Assert - should match patterns even with different naming
            analysis.HasError.Should().BeFalse();
            analysis.SuggestedMapping.IdField.Should().Be("item_id");
            analysis.SuggestedMapping.UserPromptField.Should().Be("user_input");
            analysis.SuggestedMapping.ExpectedOutputField.Should().Be("expected_result");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task AnalyzeFile_WithInvalidFile_ReturnsError()
    {
        // Arrange
        var tempFile = Path.GetTempFileName() + ".invalid";
        await File.WriteAllTextAsync(tempFile, "not valid json");

        try
        {
            // Act
            var analysis = await FieldMappingDetector.AnalyzeFileAsync(tempFile);

            // Assert
            analysis.HasError.Should().BeTrue();
            analysis.Error.Should().NotBeNullOrEmpty();
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}

public class FieldMappingSerializationTests
{
    [Fact]
    public void FieldMapping_Serializes_And_Deserializes_Correctly()
    {
        // Arrange
        var original = new FieldMapping
        {
            IdField = "item_id",
            UserPromptField = "user_prompt",
            ExpectedOutputField = "expected_output",
            SystemPromptField = "system_prompt"
        };

        // Act - serialize and deserialize
        var json = System.Text.Json.JsonSerializer.Serialize(original);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<FieldMapping>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.IdField.Should().Be(original.IdField);
        deserialized.UserPromptField.Should().Be(original.UserPromptField);
        deserialized.ExpectedOutputField.Should().Be(original.ExpectedOutputField);
        deserialized.SystemPromptField.Should().Be(original.SystemPromptField);
    }

    [Fact]
    public void PartialDataSourceConfig_WithFieldMapping_Serializes_Correctly()
    {
        // Arrange
        var original = new PartialDataSourceConfig
        {
            Kind = DataSourceKind.SingleFile,
            FilePath = "./data/test.json",
            FieldMapping = new FieldMapping
            {
                IdField = "id",
                UserPromptField = "prompt",
                ExpectedOutputField = "answer"
            }
        };

        // Act - serialize and deserialize
        var json = System.Text.Json.JsonSerializer.Serialize(original);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<PartialDataSourceConfig>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Kind.Should().Be(original.Kind);
        deserialized.FilePath.Should().Be(original.FilePath);
        deserialized.FieldMapping.Should().NotBeNull();
        deserialized.FieldMapping!.IdField.Should().Be(original.FieldMapping.IdField);
        deserialized.FieldMapping.UserPromptField.Should().Be(original.FieldMapping.UserPromptField);
        deserialized.FieldMapping.ExpectedOutputField.Should().Be(original.FieldMapping.ExpectedOutputField);
    }

    [Fact]
    public void PartialConfig_WithPipelineOptions_Serializes_Correctly()
    {
        // Arrange
        var original = new PartialConfig
        {
            PipelineOptions = new Dictionary<string, object?>
            {
                ["sourceLanguage"] = "English",
                ["targetLanguage"] = "French",
                ["systemPrompt"] = "Custom translation prompt",
                ["buildScriptPath"] = "./build.sh",
                ["testFilePath"] = "./tests.cs"
            }
        };

        // Act - serialize and deserialize with proper options for dictionaries
        var options = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = false
        };
        var json = System.Text.Json.JsonSerializer.Serialize(original, options);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<PartialConfig>(json, options);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.PipelineOptions.Should().NotBeNull();

        // Handle JsonElement deserialization
        deserialized.PipelineOptions!.TryGetValue("sourceLanguage", out var sourceLang).Should().BeTrue();
        (sourceLang?.ToString()).Should().Be("English");

        deserialized.PipelineOptions.TryGetValue("targetLanguage", out var targetLang).Should().BeTrue();
        (targetLang?.ToString()).Should().Be("French");

        deserialized.PipelineOptions.TryGetValue("systemPrompt", out var sysPrompt).Should().BeTrue();
        (sysPrompt?.ToString()).Should().Be("Custom translation prompt");

        deserialized.PipelineOptions.TryGetValue("buildScriptPath", out var buildScript).Should().BeTrue();
        (buildScript?.ToString()).Should().Be("./build.sh");

        deserialized.PipelineOptions.TryGetValue("testFilePath", out var testFile).Should().BeTrue();
        (testFile?.ToString()).Should().Be("./tests.cs");
    }
}
