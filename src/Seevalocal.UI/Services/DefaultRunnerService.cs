using Microsoft.Extensions.Logging;
using Seevalocal.Core.Models;
using Seevalocal.Core.Pipeline;
using Seevalocal.Pipelines;
using Seevalocal.Server;
using Seevalocal.Server.Models;
using Seevalocal.UI.ViewModels;
using System.Net.Http.Headers;

namespace Seevalocal.UI.Services;

/// <summary>
/// Default implementation of IRunnerService for UI mode.
/// Creates EvalRunViewModel instances with full pipeline wiring.
/// Supports two-phase execution: primary evaluation followed by judge evaluation.
/// Judge server is only started after primary phase completes (for locally managed judges).
/// Supports checkpoint/resume from SQLite database.
/// </summary>
public sealed class DefaultRunnerService(
    PipelineRegistry pipelineRegistry,
    DataSources.DataSourceFactory dataSourceFactory,
    IServerLifecycleService serverLifecycleService,
    ILoggerFactory loggerFactory,
    ILogger<DefaultRunnerService> logger) : IRunnerService
{
    private readonly PipelineRegistry _pipelineRegistry = pipelineRegistry;
    private readonly DataSources.DataSourceFactory _dataSourceFactory = dataSourceFactory;
    private readonly IServerLifecycleService _serverLifecycleService = serverLifecycleService;
    private readonly ILoggerFactory _loggerFactory = loggerFactory;
    private readonly ILogger<DefaultRunnerService> _logger = logger;

    public async Task<int> RunAsync(
        ResolvedConfig config,
        bool showProgress,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting evaluation run: {RunName}", config.Run.RunName);

            // Note: Full implementation requires wiring up the pipeline orchestrator
            // with data sources, stages, and result writers.
            // This is a placeholder that will be completed when the full pipeline is wired.

            await Task.CompletedTask;

            _logger.LogInformation("Evaluation completed successfully");
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during evaluation run");
            return 1;
        }
    }

    public async Task<IEvalRunViewModel> CreateViewModelAsync(ResolvedConfig config, CancellationToken cancellationToken = default)
    {
        var runLogger = _loggerFactory.CreateLogger($"EvalRun.{config.Run.RunName ?? "unnamed"}");

        // Get the first eval set (UI supports single eval set per run)
        var evalSet = config.EvalSets.FirstOrDefault()
            ?? throw new InvalidOperationException("No eval sets configured");

        // 1. Create the pipeline for the specified pipeline name
        var pipelineFactory = _pipelineRegistry.Get(evalSet.PipelineName);
        var fullPipeline = pipelineFactory.Create(evalSet, config);

        // 2. Create a primary-phase pipeline (without JudgeStage if both model-being-evaluated server and judge server are locally managed)
        // The judge stage should only run in phase 2 when judge server is started
        bool needsJudgePhase = PipelineOrchestratorFactory.NeedsJudgePhase(config);
        var primaryPipeline = needsJudgePhase
            ? CreatePipelineWithoutJudgeStage(fullPipeline)
            : fullPipeline;

        // 3. Create data source from config
        var dataSourceLogger = _loggerFactory.CreateLogger("DataSource");
        var dsConfig = ConvertDataSourceConfig(evalSet.DataSource);
        var dataSourceResult = _dataSourceFactory.Create(evalSet.Name, dsConfig);
        var dataSource = dataSourceResult.IsSuccess
            ? dataSourceResult.Value
            : throw new InvalidOperationException($"Failed to create data source: {dataSourceResult.Errors[0].Message}");

        // 4. Create persistent result collector with checkpoint database
        var dbPath = GetCheckpointDatabasePath(config);
        var collectorLogger = _loggerFactory.CreateLogger<PersistentResultCollector>();
        var collector = new PersistentResultCollector(dbPath);
        _logger.LogInformation("Using checkpoint database: {DbPath}", dbPath);

        // Check if we're continuing from a checkpoint
        bool isContinuing = config.Run.ContinueFromCheckpoint;
        if (isContinuing)
        {
            var savedConfig = await collector.LoadStartupParametersAsync(cancellationToken);
            if (savedConfig != null)
            {
                _logger.LogInformation("Continuing run from checkpoint: {RunName}", savedConfig.Run.RunName);
                // Use the saved config for the run
                config = savedConfig;
            }
            else
            {
                _logger.LogWarning("No checkpoint found, starting fresh run");
                isContinuing = false;
            }
        }

        // 5. Create the primary phase orchestrator (server will be started in StartAsync)
        var orchestratorLogger = _loggerFactory.CreateLogger<PipelineOrchestrator>();
        var progress = new Progress<Core.EvalProgress>();

        // Create external server client if not managing server
        LlamaServerClient? primaryClient = null;
        if (config.Server.Manage == false)
        {
            var primaryServerInfo = new ServerInfo
            {
                BaseUrl = config.Server.BaseUrl ?? $"http://{config.Server.Host}:{config.Server.Port}",
                ApiKey = config.Server.ApiKey,
                TotalSlots = config.LlamaServer.ParallelSlotCount ?? 4,
                ModelAlias = config.LlamaServer.ModelAlias ?? ""
            };
            var primaryHttpClient = CreateHttpClient(primaryServerInfo);
            var primaryClientLogger = _loggerFactory.CreateLogger<LlamaServerClient>();
            var maxConcurrent = config.Run?.MaxConcurrentEvals ?? 4;
            primaryClient = new LlamaServerClient(primaryServerInfo, primaryHttpClient, primaryClientLogger, maxConcurrent);

            // Initialize semaphore based on actual server slot count
            await primaryClient.InitializeSemaphoreFromServerAsync(cancellationToken);
        }

        // Create external judge client if the original pipeline has JudgeStage
        // (for locally managed judges, the judge client is created in TwoPhaseEvalRunViewModel.RunJudgePhaseAsync)
        LlamaServerClient? judgeClient = null;
        bool fullPipelineHasJudgeStage = fullPipeline.Stages.Any(s => s.StageName.Equals("JudgeStage", StringComparison.OrdinalIgnoreCase));
        if (fullPipelineHasJudgeStage && config.Judge != null && !needsJudgePhase)
        {
            // Judge stage is in the pipeline AND we're NOT doing two-phase execution,
            // so we need a judge client right now for one-phase execution
            var judgeBaseUrl = config.Judge.BaseUrl;
            if (!string.IsNullOrEmpty(judgeBaseUrl))
            {
                var judgeServerInfo = new ServerInfo
                {
                    BaseUrl = judgeBaseUrl,
                    ApiKey = config.Judge.ServerConfig?.ApiKey,
                    TotalSlots = config.Judge.ServerSettings?.ParallelSlotCount ?? 4,
                    ModelAlias = ""
                };
                var judgeHttpClient = CreateHttpClient(judgeServerInfo);
                var judgeClientLogger = _loggerFactory.CreateLogger<LlamaServerClient>();
                var maxConcurrent = config.Run?.MaxConcurrentEvals ?? 4;
                judgeClient = new LlamaServerClient(judgeServerInfo, judgeHttpClient, judgeClientLogger, maxConcurrent);

                // Initialize semaphore based on actual server slot count
                await judgeClient.InitializeSemaphoreFromServerAsync(cancellationToken);

                _logger.LogInformation("Created judge client for external judge at {BaseUrl}", judgeBaseUrl);
            }
            else
            {
                throw new InvalidOperationException(
                    "Judge stage is configured in pipeline but no judge BaseUrl is provided. " +
                    "Either provide Judge.BaseUrl for external judge or set Judge.Manage=true for locally managed judge.");
            }
        }

        // Create orchestrator - will be initialized with server when StartAsync is called
        var primaryOrchestrator = await PipelineOrchestratorFactory.CreatePrimaryAsync(
            dataSource,
            primaryPipeline,
            evalSet,
            config,
            primaryClient!,  // Will be null for managed servers - will be created in StartAsync
            collector,
            progress,
            orchestratorLogger,
            cancellationToken,
            judgeClient);  // Pass judge client for external judges

        // 6. Create and return the appropriate ViewModel based on execution mode
        if (needsJudgePhase)
        {
            // TWO-PHASE EXECUTION (two locally managed llama-server instances, so don't run them simultaneously)
            // Server will be started in StartAsync() so the view can show progress
            return new TwoPhaseEvalRunViewModel(
                config,
                evalSet,
                fullPipeline,  // Full pipeline for judge phase
                dataSource,
                collector,
                _serverLifecycleService,  // For both primary and judge servers (sequential)
                _loggerFactory,
                runLogger,
                progress,
                primaryPipeline);  // Primary pipeline without JudgeStage
        }
        else
        {
            // SINGLE-PHASE EXECUTION (zero or one locally managed llama-server instance)
            // ViewModel will create orchestrator after starting managed servers
            var serverLifecycle = config.Server.Manage != false || config.Judge?.Manage == true ? _serverLifecycleService : null;

            return new EvalRunViewModel(
                config,
                fullPipeline,
                dataSource,
                collector,
                evalSet,
                _loggerFactory,
                runLogger,
                serverLifecycle,
                config.Server.Manage != false ? config.Server : null,
                config.Server.Manage != false ? config.LlamaServer : null,
                config.Judge is { Manage: true } ? config.Judge.ServerConfig : null,
                config.Judge is { Manage: true } ? config.Judge.ServerSettings : null,
                config.Server.Manage == false ? primaryClient : null,
                config.Judge is { Manage: false } ? judgeClient : null);
        }
    }

    /// <summary>
    /// Creates a new pipeline without the JudgeStage.
    /// Used for the primary phase when judge server is locally managed.
    /// </summary>
    private EvalPipeline CreatePipelineWithoutJudgeStage(EvalPipeline originalPipeline)
    {
        var stagesWithoutJudge = originalPipeline.Stages
            .Where(s => !s.StageName.Equals("JudgeStage", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var logger = _loggerFactory.CreateLogger<EvalPipeline>();
        return new EvalPipeline(logger)
        {
            PipelineName = originalPipeline.PipelineName,
            Stages = stagesWithoutJudge,
        };
    }

    private static string GetCheckpointDatabasePath(ResolvedConfig config)
    {
        var outputDir = config.Run.OutputDirectoryPath ?? "./results";
        var runName = config.Run.RunName ?? "unnamed";
        var safeRunName = string.Concat(runName.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
        return Path.Combine(outputDir, $".{safeRunName}_checkpoint.db");
    }

    private static HttpClient CreateHttpClient(ServerInfo serverInfo)
    {
        var handler = new SocketsHttpHandler
        {
            // No timeout for inference requests - LLM inference can take hours
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            EnableMultipleHttp2Connections = true
        };

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(serverInfo.BaseUrl),
            // Long Timeout - inference requests can take hours on local machines
            Timeout = TimeSpan.FromHours(6),
        };

        // Set authorization header if API key is provided
        if (!string.IsNullOrEmpty(serverInfo.ApiKey))
        {
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", serverInfo.ApiKey);
        }

        // Set User-Agent
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Seevalocal.UI/1.0");

        return httpClient;
    }

    /// <summary>
    /// Converts from Seevalocal.Core.Models.DataSourceConfig to Seevalocal.DataSources.DataSourceConfig.
    /// File-based kinds (SingleFile/File) are detected from the file extension.
    /// JSONL files get default field mapping (question/answer) if not specified.
    /// </summary>
    private static DataSources.DataSourceConfig ConvertDataSourceConfig(DataSourceConfig coreConfig)
    {
        // Detect actual file type from extension if Kind is generic (SingleFile/File)
        var effectiveKind = coreConfig.Kind;
        if (coreConfig.Kind is DataSourceKind.SingleFile or DataSourceKind.File)
        {
            effectiveKind = DetectFileKindFromExtension(coreConfig.FilePath);
        }

        // Apply JSONL defaults for field mapping if not specified
        var fieldMapping = coreConfig.FieldMapping;
        if (effectiveKind == DataSourceKind.JsonlFile)
        {
            if (fieldMapping.IdField == null) fieldMapping = fieldMapping with { IdField = "id" };
            if (fieldMapping.UserPromptField == null) fieldMapping = fieldMapping with { UserPromptField = "question" };
            if (fieldMapping.ExpectedOutputField == null) fieldMapping = fieldMapping with { ExpectedOutputField = "answer" };
        }

        return new DataSources.DataSourceConfig
        {
            Kind = effectiveKind,  // Now the same enum, no cast needed
            PromptDirectoryPath = coreConfig.PromptDirectoryPath,
            ExpectedOutputDirectoryPath = coreConfig.ExpectedOutputDirectoryPath,
            SystemPromptFilePath = coreConfig.DefaultSystemPromptFilePath,
            FileExtensionFilter = coreConfig.FileExtensionFilter ?? "*",
            DataFilePath = coreConfig.FilePath,
            FieldMapping = fieldMapping != null ? ConvertFieldMapping(fieldMapping) : null,
            DefaultSystemPrompt = coreConfig.DefaultSystemPrompt,
            MaxItemCount = null,  // Not used in UI mode
            ShuffleRandomSeed = null  // Not used in UI mode
        };
    }

    /// <summary>
    /// Detects the DataSourceKind from a file extension.
    /// </summary>
    private static DataSourceKind DetectFileKindFromExtension(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return DataSourceKind.SingleFile;

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".json" => DataSourceKind.JsonFile,
            ".jsonl" => DataSourceKind.JsonlFile,
            ".yaml" or ".yml" => DataSourceKind.YamlFile,
            ".csv" => DataSourceKind.CsvFile,
            ".parquet" => DataSourceKind.ParquetFile,
            _ => DataSourceKind.SingleFile,
        };
    }

    private static DataSources.FieldMapping ConvertFieldMapping(FieldMapping coreMapping)
    {
        return new DataSources.FieldMapping
        {
            IdField = coreMapping.IdField ?? "id",
            UserPromptField = coreMapping.UserPromptField ?? "prompt",
            ExpectedOutputField = coreMapping.ExpectedOutputField ?? "expected",
            SystemPromptField = coreMapping.SystemPromptField,
            MetadataFields = []  // Core FieldMapping doesn't have metadata fields
        };
    }
}
