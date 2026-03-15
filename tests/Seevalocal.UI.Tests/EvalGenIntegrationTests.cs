using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Seevalocal.Core;
using Seevalocal.Core.Models;
using Seevalocal.Server;
using Seevalocal.Server.Models;
using Seevalocal.UI.Services;
using Xunit;
using Xunit.Abstractions;

namespace Seevalocal.UI.Tests;

/// <summary>
/// End-to-end integration tests for eval generation with real llama-server.
/// Uses C:\AI\vulkan\llama-server.exe with C:\AI\Qwen2.5-0.5B-Instruct-Q6_K.gguf model.
/// </summary>
public class EvalGenIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _modelPath;
    private readonly string _serverPath;
    private readonly string _testOutputDir;
    private readonly string _checkpointDbPath;
    private readonly int _serverPort;
    private LlamaServerManager? _serverManager;
    private HttpClient? _httpClient;
    private bool _serverStarted;

    public EvalGenIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _modelPath = @"C:\AI\Qwen2.5-0.5B-Instruct-Q6_K.gguf";
        _serverPath = @"C:\AI\vulkan\llama-server.exe";
        _serverPort = 8081;
        _testOutputDir = Path.Combine(Path.GetTempPath(), $"eval_gen_integration_{Guid.NewGuid()}");
        _checkpointDbPath = Path.Combine(_testOutputDir, "checkpoint.db");
        
        Directory.CreateDirectory(_testOutputDir);

        // Create logger factory that writes to test output
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new TestOutputLoggerProvider(output));
            builder.SetMinimumLevel(LogLevel.Information);
        });
    }

    public async Task InitializeAsync()
    {
        // Start llama-server if both model and server exist
        if (File.Exists(_serverPath) && File.Exists(_modelPath))
        {
            await StartServerAsync();
        }
        else
        {
            if (!File.Exists(_serverPath))
                _output.WriteLine($"llama-server not found at: {_serverPath}");
            if (!File.Exists(_modelPath))
                _output.WriteLine($"Model not found at: {_modelPath}");
        }
    }

    public async Task DisposeAsync()
    {
        // Stop server
        if (_serverManager != null)
        {
            try
            {
                await _serverManager.DisposeAsync();
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error disposing server manager: {ex.Message}");
            }
        }

        _httpClient?.Dispose();

        // Cleanup test output
        try
        {
            if (Directory.Exists(_testOutputDir))
                Directory.Delete(_testOutputDir, true);
        }
        catch { }
    }

    private async Task StartServerAsync()
    {
        _output.WriteLine($"Starting llama-server at {_serverPath}");
        _output.WriteLine($"Model: {_modelPath}");
        _output.WriteLine($"Port: {_serverPort}");

        var config = new ServerConfig
        {
            Manage = true,
            Model = new ModelSource { Kind = ModelSourceKind.LocalFile, FilePath = _modelPath },
            Host = "localhost",
            Port = _serverPort
        };

        var llamaSettings = new LlamaServerSettings
        {
            ContextWindowTokens = 2048,
            ParallelSlotCount = 1,
            SamplingTemperature = 0.0, // Deterministic for tests
            ThreadCount = 4,
            GpuLayerCount = 0 // Use CPU for consistency
        };

        var argBuilder = new LlamaServerArgBuilder();
        var gpuDetector = new GpuDetector(NullLogger<GpuDetector>.Instance);
        var downloader = new LlamaServerDownloader(new HttpClient(), NullLogger<LlamaServerDownloader>.Instance);
        
        _serverManager = new LlamaServerManager(
            downloader,
            gpuDetector,
            new HttpClient { Timeout = TimeSpan.FromMinutes(5) },
            _loggerFactory.CreateLogger<LlamaServerManager>());

        var result = await _serverManager.StartAsync(config, llamaSettings, CancellationToken.None);
        
        if (!result.IsSuccess)
        {
            _output.WriteLine($"Failed to start server: {string.Join(", ", result.Errors.Select(e => e.Message))}");
            throw new InvalidOperationException($"Failed to start llama-server: {string.Join(", ", result.Errors.Select(e => e.Message))}");
        }

        _serverStarted = true;
        var serverInfo = result.Value;
        _output.WriteLine($"Server started at {serverInfo.BaseUrl}");

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(serverInfo.BaseUrl),
            Timeout = TimeSpan.FromMinutes(10)
        };

        // Wait for server to be healthy and model loaded
        await WaitForServerHealthyAsync();
    }

    private async Task WaitForServerHealthyAsync()
    {
        _output.WriteLine("Waiting for server to become healthy and model loaded...");
        
        for (int i = 0; i < 300; i++) // Wait up to 300 seconds (5 minutes) for model load
        {
            try
            {
                var response = await _httpClient!.GetAsync("/health");
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    
                    // Check for model_loaded or similar indicator that model is ready
                    if (content.Contains("model_loaded", StringComparison.OrdinalIgnoreCase) ||
                        content.Contains("\"ok\"", StringComparison.OrdinalIgnoreCase) ||
                        content.Contains("\"Healthy\"", StringComparison.OrdinalIgnoreCase))
                    {
                        _output.WriteLine($"Server is healthy after {i + 1} seconds!");
                        
                        // Additional check - try /props endpoint to confirm model is ready
                        try
                        {
                            var propsResponse = await _httpClient.GetAsync("/props");
                            if (propsResponse.IsSuccessStatusCode)
                            {
                                _output.WriteLine("Server /props endpoint is ready!");
                                return;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Health check attempt {i + 1} failed: {ex.Message}");
            }
            
            await Task.Delay(1000);
        }
        
        throw new TimeoutException("Server did not become healthy within 300 seconds");
    }

    #region Integration Tests

    [Fact]
    public async Task Server_StartsAndRespondsToHealthCheck()
    {
        // Skip if server didn't start
        if (!_serverStarted || _httpClient == null)
        {
            _output.WriteLine("Skipping test - server not available");
            return;
        }

        // Act - health check already passed in InitializeAsync
        var response = await _httpClient.GetAsync("/health");
        
        // Assert
        response.IsSuccessStatusCode.Should().BeTrue("Server should respond to health check");
    }

    [Fact]
    public async Task GenerateAsync_WithRealServer_CreatesOutputDirectories()
    {
        // Skip if server didn't start
        if (!_serverStarted || _httpClient == null)
        {
            _output.WriteLine("Skipping test - server not available");
            return;
        }

        // Arrange
        var service = CreateEvalGenService();
        var config = new EvalGenConfig
        {
            Id = "integration-test-1",
            RunName = "IntegrationTest_OutputDirs",
            OutputDirectoryPath = _testOutputDir,
            TargetCategoryCount = 1,
            TargetProblemsPerCategory = 1,
            DomainPrompt = "Simple test",
            MaxConcurrentCategoryGenerations = 1,
            MaxConcurrentProblemGenerations = 1,
            MaxConcurrentFleshOutGenerations = 1,
            CheckpointDatabasePath = _checkpointDbPath
        };

        _output.WriteLine($"Starting eval generation: {config.RunName}");

        // Act
        var run = await service.GenerateAsync(config, null, CancellationToken.None);
        await run.WaitAsync();

        // Assert - directories should be created
        var promptsDir = Path.Combine(_testOutputDir, "prompts");
        var expectedDir = Path.Combine(_testOutputDir, "expected_outputs");

        Directory.Exists(promptsDir).Should().BeTrue("Prompts directory should exist");
        Directory.Exists(expectedDir).Should().BeTrue("Expected outputs directory should exist");
        
        // Checkpoint database should be created
        File.Exists(_checkpointDbPath).Should().BeTrue("Checkpoint database should exist");
    }

    [Fact]
    public async Task GenerateAsync_CheckpointDatabase_ContainsStartupParameters()
    {
        // Skip if server didn't start
        if (!_serverStarted || _httpClient == null)
        {
            _output.WriteLine("Skipping test - server not available");
            return;
        }

        // Arrange
        var service = CreateEvalGenService();
        var config = new EvalGenConfig
        {
            Id = "checkpoint-test-1",
            RunName = "CheckpointTest",
            OutputDirectoryPath = _testOutputDir,
            TargetCategoryCount = 1,
            TargetProblemsPerCategory = 1,
            DomainPrompt = "Simple test",
            CheckpointDatabasePath = _checkpointDbPath
        };

        // Act
        var run = await service.GenerateAsync(config, null, CancellationToken.None);
        await run.WaitAsync();

        // Wait for file to be released
        await Task.Delay(500);

        // Assert - checkpoint database should contain startup parameters
        var collector = new EvalGenCheckpointCollector(_checkpointDbPath);
        var savedParams = await collector.LoadStartupParametersAsync(CancellationToken.None);

        savedParams.Should().NotBeNull("Should have saved startup parameters");
        savedParams!.Value.Config.RunName.Should().Be("CheckpointTest");

        await collector.DisposeAsync();
    }

    #endregion

    private EvalGenService CreateEvalGenService()
    {
        var gpuDetector = new GpuDetector(NullLogger<GpuDetector>.Instance);
        var downloader = new LlamaServerDownloader(_httpClient, NullLogger<LlamaServerDownloader>.Instance);
        var serverManager = new LlamaServerManager(downloader, gpuDetector, _httpClient, _loggerFactory.CreateLogger<LlamaServerManager>());
        return new EvalGenService(null, serverManager, _loggerFactory, _httpClient, _loggerFactory.CreateLogger<EvalGenService>());
    }

    /// <summary>
    /// Logger provider that writes to ITestOutputHelper.
    /// </summary>
    private sealed class TestOutputLoggerProvider(ITestOutputHelper output) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new TestOutputLogger(output, categoryName);

        public void Dispose() { }
    }

    /// <summary>
    /// Logger that writes to ITestOutputHelper.
    /// </summary>
    private sealed class TestOutputLogger(ITestOutputHelper output, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            output.WriteLine($"[{logLevel}] [{categoryName}] {message}");
            if (exception != null)
            {
                output.WriteLine($"Exception: {exception}");
            }
        }
    }
}
