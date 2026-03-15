using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Seevalocal.Core;
using Seevalocal.Core.Models;
using Seevalocal.Server;
using Seevalocal.UI.Services;
using System.Net;
using System.Net.Http;
using System.Text;
using Xunit;

namespace Seevalocal.UI.Tests;

/// <summary>
/// Unit tests for EvalGenService with mocked LLM responses.
/// Focus on basic functionality that can be reliably tested with mocks.
/// </summary>
public class EvalGenServiceTests : IAsyncLifetime
{
    private readonly HttpClient _httpClient;
    private readonly MockHttpMessageHandler _mockHttpHandler;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<EvalGenService> _logger;
    private readonly LlamaServerManager _serverManager;
    private readonly string _tempDir;

    public EvalGenServiceTests()
    {
        _mockHttpHandler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_mockHttpHandler);
        _loggerFactory = NullLoggerFactory.Instance;
        _logger = NullLogger<EvalGenService>.Instance;
        _tempDir = Path.Combine(Path.GetTempPath(), $"eval_gen_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        
        // Create mock server manager
        var gpuDetector = new GpuDetector(NullLogger<GpuDetector>.Instance);
        var downloader = new LlamaServerDownloader(_httpClient, NullLogger<LlamaServerDownloader>.Instance);
        _serverManager = new LlamaServerManager(downloader, gpuDetector, _httpClient, NullLogger<LlamaServerManager>.Instance);
    }

    public async Task InitializeAsync()
    {
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _httpClient.Dispose();
        _mockHttpHandler.Dispose();
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch { }
    }

    private EvalGenService CreateService()
    {
        return new EvalGenService(null, _serverManager, _loggerFactory, _httpClient, _logger);
    }

    #region Constructor and Properties Tests

    [Fact]
    public void IsGenerationActive_Initially_False()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert
        service.IsGenerationActive.Should().BeFalse();
    }

    [Fact]
    public void CurrentRun_Initially_Null()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert
        service.CurrentRun.Should().BeNull();
    }

    #endregion

    #region GenerateAsync Tests

    [Fact]
    public async Task GenerateAsync_ValidConfig_CreatesEvalGenRun()
    {
        // Arrange
        var service = CreateService();
        var config = new EvalGenConfig
        {
            RunName = "TestRun",
            OutputDirectoryPath = _tempDir,
            TargetCategoryCount = 1,
            TargetProblemsPerCategory = 1,
            DomainPrompt = "Test domain"
        };

        // Act
        var run = await service.GenerateAsync(config, null, CancellationToken.None);

        // Assert - run should be started and active
        run.Should().NotBeNull();
        run.IsRunning.Should().BeTrue("Run should be started");
        run.RunName.Should().Be("TestRun");
        run.Config.Should().Be(config);
        service.CurrentRun.Should().Be(run);
    }

    #endregion

    #region Output Tests

    [Fact]
    public async Task GenerateAsync_CreatesOutputDirectories()
    {
        // Arrange
        var service = CreateService();
        var config = new EvalGenConfig
        {
            RunName = "OutputTest",
            OutputDirectoryPath = _tempDir,
            TargetCategoryCount = 1,
            TargetProblemsPerCategory = 1
        };

        // Act
        var run = await service.GenerateAsync(config, null, CancellationToken.None);
        await run.WaitAsync();

        // Assert - directories should be created even if no problems generated
        var promptsDir = Path.Combine(_tempDir, "prompts");
        var expectedDir = Path.Combine(_tempDir, "expected_outputs");

        Directory.Exists(promptsDir).Should().BeTrue();
        Directory.Exists(expectedDir).Should().BeTrue();
    }

    #endregion

    #region Checkpoint Tests

    [Fact]
    public async Task GenerateAsync_Checkpoint_CreatesDatabase()
    {
        // Arrange
        var service = CreateService();
        var dbPath = Path.Combine(_tempDir, "test_checkpoint.db");
        var config = new EvalGenConfig
        {
            RunName = "CheckpointTest",
            OutputDirectoryPath = _tempDir,
            TargetCategoryCount = 1,
            TargetProblemsPerCategory = 1,
            CheckpointDatabasePath = dbPath
        };

        // Act
        var run = await service.GenerateAsync(config, null, CancellationToken.None);
        await run.WaitAsync();

        // Give time for file to be released
        await Task.Delay(100);

        // Assert - checkpoint database should exist
        File.Exists(dbPath).Should().BeTrue();
    }

    #endregion

    /// <summary>
    /// Simple mock HTTP message handler that returns predefined responses.
    /// </summary>
    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses = new();
        private readonly Queue<TimeSpan> _delays = new();
        private int _callCount = 0;

        public void AddResponse(string jsonResponse, TimeSpan? delay = null)
        {
            _responses.Enqueue(jsonResponse);
            _delays.Enqueue(delay ?? TimeSpan.Zero);
        }

        public void AddDelayedResponse(string jsonResponse, TimeSpan delay)
        {
            AddResponse(jsonResponse, delay);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_responses.Count == 0)
            {
                // Return default response if no more mocked responses
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"choices":[{"message":{"content":"<response></response>"}}]}""", Encoding.UTF8, "application/json")
                };
            }

            var delay = _delays.Dequeue();
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }

            var response = _responses.Dequeue();
            _callCount++;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }

        public int CallCount => _callCount;
    }
}
