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
            llamaServer:
              contextWindowTokens: 4096
              samplingTemperature: 0.5
              enableFlashAttention: true
              parallelSlotCount: 2
            """;

        var path = WriteTemp(".yaml", yaml);
        var result = await _loader.LoadAsync(path);

        _ = result.IsSuccess.Should().BeTrue();
        _ = result.Value.LlamaServer!.ContextWindowTokens.Should().Be(4096);
        _ = result.Value.LlamaServer!.SamplingTemperature.Should().Be(0.5);
        _ = result.Value.LlamaServer!.EnableFlashAttention.Should().BeTrue();
        _ = result.Value.LlamaServer!.ParallelSlotCount.Should().Be(2);
    }

    [Fact]
    public async Task Load_Yaml_IgnoresUnknownFields()
    {
        var yaml = """
            unknownTopLevel: hello
            llamaServer:
              contextWindowTokens: 1024
              completelyMadeUpField: 999
            """;

        var path = WriteTemp(".yml", yaml);
        var result = await _loader.LoadAsync(path);

        _ = result.IsSuccess.Should().BeTrue();
        _ = result.Value.LlamaServer!.ContextWindowTokens.Should().Be(1024);
    }

    [Fact]
    public async Task Load_Yaml_ParsesEvalSets()
    {
        var yaml = """
            evalSets:
              - id: my-eval
                name: "My Eval"
                pipelineName: CSharpCoding
                dataSource:
                  kind: Directory
                  promptDirectoryPath: ./prompts
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
        _ = result.Value.EvalSets.Should().HaveCount(1);
        _ = result.Value.EvalSets![0].Id.Should().Be("my-eval");
        _ = result.Value.EvalSets![0].PipelineName.Should().Be("CSharpCoding");
        _ = result.Value.EvalSets![0].DataSource.PromptDirectoryPath.Should().Be("./prompts");
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
              "llamaServer": {
                "contextWindowTokens": 8192,
                "gpuLayerCount": 35
              }
            }
            """;

        var path = WriteTemp(".json", json);
        var result = await _loader.LoadAsync(path);

        _ = result.IsSuccess.Should().BeTrue();
        _ = result.Value.LlamaServer!.ContextWindowTokens.Should().Be(8192);
        _ = result.Value.LlamaServer!.GpuLayerCount.Should().Be(35);
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
        _ = result.Value.LlamaServer.Should().BeNull();
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
              host: 0.0.0.0
              port: 9090
              model:
                kind: LocalFile
                filePath: /models/test.gguf
            """;

        var path = WriteTemp(".yml", yaml);
        var result = await _loader.LoadAsync(path);

        _ = result.IsSuccess.Should().BeTrue();
        _ = result.Value.Server!.Manage.Should().BeTrue();
        _ = result.Value.Server!.Host.Should().Be("0.0.0.0");
        _ = result.Value.Server!.Port.Should().Be(9090);
        _ = result.Value.Server!.Model!.FilePath.Should().Be("/models/test.gguf");
    }
}
