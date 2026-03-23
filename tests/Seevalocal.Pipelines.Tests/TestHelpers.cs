using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Seevalocal.Core.Models;
using Seevalocal.Server;
using Seevalocal.Server.Models;

namespace Seevalocal.Pipelines.Tests;

internal static class TestHelpers
{
    public static ILoggerFactory LoggerFactory => NullLoggerFactory.Instance;

    public static EvalItem MakeItem(
        string id = "test-001",
        string prompt = "Hello",
        string? expected = null,
        string? system = null) =>
        new()
        {
            Id = id,
            UserPrompt = prompt,
            ExpectedOutput = expected,
            SystemPrompt = system,
        };

    public static Core.EvalStageContext MakeContext(
        EvalItem? item = null,
        IReadOnlyDictionary<string, object?>? outputs = null,
        LlamaServerClient? primary = null) =>
        new()
        {
            Item = item ?? MakeItem(),
            Config = DefaultConfig(),
            StageOutputs = outputs ?? new Dictionary<string, object?>(),
            PrimaryClient = primary ?? StubClient(),
        };

    public static ResolvedConfig MakeConfig(
        bool continueOnFailure = true,
        int? maxConcurrent = null) =>
        new()
        {
            Run = new RunConfig
            {
                Id = "test-run",
                ContinueOnEvalFailure = continueOnFailure,
                MaxConcurrentEvals = maxConcurrent,
            }
        };

    public static ResolvedConfig DefaultConfig() => new()
    {
        Run = new RunConfig
        {
            Id = "default-run",
            ContinueOnEvalFailure = true,
            MaxConcurrentEvals = null,
        },
        Server = new ServerConfig
        {
            Manage = false,
            BaseUrl = "http://localhost:8080",
        },
        LlamaServer = new LlamaServerSettings(),
    };

    public static ResolvedConfig MakeConfigWithPipeline(
        string pipeline = "CasualQA",
        Dictionary<string, object?>? opts = null) =>
        new()
        {
            Run = new RunConfig
            {
                Id = "test-run",
                PipelineName = pipeline,
                ContinueOnEvalFailure = true,
            },
            Server = new ServerConfig
            {
                Manage = false,
                BaseUrl = "http://localhost:8080",
            },
            LlamaServer = new LlamaServerSettings(),
            PipelineOptions = opts ?? [],
        };

    /// <summary>Returns a stub client with no real HTTP connection.</summary>
    public static LlamaServerClient StubClient()
    {
        var info = new ServerInfo { BaseUrl = "http://localhost:8080", TotalSlots = 4 };
        return new LlamaServerClient(info, new HttpClient(), NullLogger<LlamaServerClient>.Instance);
    }
}
