using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Seevalocal.Config.Loading;
using Xunit;

namespace Seevalocal.Config.Tests;

public sealed class SettingsFileLoaderTests : IDisposable
{
    private readonly SettingsFileLoader _loader = new(NullLogger<SettingsFileLoader>.Instance);
    private readonly List<string> _tempFiles = [];

    private string WriteTemp(string extension, string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"seevalocal-test-{Guid.NewGuid()}{extension}");
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            if (File.Exists(f)) File.Delete(f);
    }

    // -------------------------------------------------------------------------
    // YAML round-trip
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Load_Yaml_ParsesRunMeta()
    {
        var yaml = """
            run:
              runName: yaml-test
              outputDirectoryPath: /tmp/yaml-out
              exportShellTarget: Bash
            """;

        var path = WriteTemp(".yml", yaml);
        var result = await _loader.LoadAsync(path);

        _ = result.IsSuccess.Should().BeTrue();
        _ = result.Value.Run!.RunName.Should().Be("yaml-test");
        _ = result.Value.Run!.OutputDirectoryPath.Should().Be("/tmp/yaml-out");
    }

    [Fact]
    public async Task Load_Yaml_ParsesLlamaServerSettings()
    {
        var yaml = """
            llamaSettings:
              contextWindowTokens: 4096
              samplingTemperature: 0.5
              enableFlashAttention: true
              parallelSlotCount: 2
            """;

        var path = WriteTemp(".yaml", yaml);
        var result = await _loader.LoadAsync(path);

        _ = result.IsSuccess.Should().BeTrue();
        _ = result.Value.LlamaSettings!.ContextWindowTokens.Should().Be(4096);
        _ = result.Value.LlamaSettings!.SamplingTemperature.Should().Be(0.5);
        _ = result.Value.LlamaSettings!.EnableFlashAttention.Should().BeTrue();
        _ = result.Value.LlamaSettings!.ParallelSlotCount.Should().Be(2);
    }

    [Fact]
    public async Task Load_Yaml_IgnoresUnknownFields()
    {
        var yaml = """
            unknownTopLevel: hello
            llamaSettings:
              contextWindowTokens: 1024
              completelyMadeUpField: 999
            """;

        var path = WriteTemp(".yml", yaml);
        var result = await _loader.LoadAsync(path);

        _ = result.IsSuccess.Should().BeTrue();
        _ = result.Value.LlamaSettings!.ContextWindowTokens.Should().Be(1024);
    }

    [Fact]
    public async Task Load_Yaml_ParsesPipelineAndDataSource()
    {
        var yaml = """
            run:
              pipelineName: CSharpCoding
            dataSource:
              kind: SplitDirectories
              promptDirectory: ./prompts
              filePattern: "*"
            """;

        var path = WriteTemp(".yml", yaml);
        var result = await _loader.LoadAsync(path);

        if (result.IsFailed)
        {
            foreach (var error in result.Errors)
            {
                Console.WriteLine($"Error: {error}");
            }
        }

        _ = result.IsSuccess.Should().BeTrue();
        _ = result.Value.Run.PipelineName.Should().Be("CSharpCoding");
        _ = result.Value.DataSource.PromptDirectory.Should().Be("./prompts");
    }

    // -------------------------------------------------------------------------
    // JSON round-trip
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Load_Json_ParsesRunMeta()
    {
        var json = """
            {
              "run": {
                "runName": "json-test",
                "outputDirectoryPath": "/tmp/json-out"
              }
            }
            """;

        var path = WriteTemp(".json", json);
        var result = await _loader.LoadAsync(path);

        _ = result.IsSuccess.Should().BeTrue();
        _ = result.Value.Run!.RunName.Should().Be("json-test");
        _ = result.Value.Run!.OutputDirectoryPath.Should().Be("/tmp/json-out");
    }

    [Fact]
    public async Task Load_Json_ParsesLlamaServerSettings()
    {
        var json = """
            {
              "llamaSettings": {
                "contextWindowTokens": 8192,
                "gpuLayerCount": 35
              }
            }
            """;

        var path = WriteTemp(".json", json);
        var result = await _loader.LoadAsync(path);

        _ = result.IsSuccess.Should().BeTrue();
        _ = result.Value.LlamaSettings!.ContextWindowTokens.Should().Be(8192);
        _ = result.Value.LlamaSettings!.GpuLayerCount.Should().Be(35);
    }

    // -------------------------------------------------------------------------
    // Format auto-detection
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Load_UnsupportedExtension_ReturnsFailure()
    {
        var path = WriteTemp(".txt", "anything");
        var result = await _loader.LoadAsync(path);

        _ = result.IsFailed.Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // Missing file
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Load_MissingFile_ReturnsFailure()
    {
        var result = await _loader.LoadAsync("/nonexistent/path/settings.yml");

        _ = result.IsFailed.Should().BeTrue();
        _ = result.Errors[0].Message.Should().Contain("[SettingsFileLoader]");
    }

    // -------------------------------------------------------------------------
    // Empty / minimal file
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Load_EmptyYaml_ReturnsEmptyPartialConfig()
    {
        var path = WriteTemp(".yml", "");
        var result = await _loader.LoadAsync(path);

        _ = result.IsSuccess.Should().BeTrue();
        _ = result.Value.Should().NotBeNull();
        _ = result.Value.Run.Should().BeNull();
        _ = result.Value.LlamaSettings.Should().BeNull();
    }

    [Fact]
    public async Task Load_EmptyJson_ReturnsEmptyPartialConfig()
    {
        var path = WriteTemp(".json", "{}");
        var result = await _loader.LoadAsync(path);

        _ = result.IsSuccess.Should().BeTrue();
        _ = result.Value.Should().NotBeNull();
    }

    // -------------------------------------------------------------------------
    // Server config
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Load_Yaml_ParsesServerConfig()
    {
        var yaml = """
            server:
              manage: true
              baseUrl: http://0.0.0.0:9090
              model:
                kind: LocalFile
                filePath: /models/test.gguf
            """;

        var path = WriteTemp(".yml", yaml);
        var result = await _loader.LoadAsync(path);

        _ = result.IsSuccess.Should().BeTrue();
        _ = result.Value.Server!.Manage.Should().BeTrue();
        _ = result.Value.Server!.BaseUrl.Should().Be("http://0.0.0.0:9090");
        _ = result.Value.Server!.Model!.FilePath.Should().Be("/models/test.gguf");
    }
}
