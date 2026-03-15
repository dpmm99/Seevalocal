using Microsoft.Extensions.Logging;
using Seevalocal.Core.Models;
using Seevalocal.Server;
using Seevalocal.Server.Models;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Seevalocal.UI.Services;

/// <summary>
/// Implementation of IEvalGenService for agentic evaluation set generation.
/// Uses the configured judge LLM to generate categories and problems.
/// </summary>
public sealed partial class EvalGenService(
    IServerLifecycleService? serverLifecycle,
    LlamaServerManager serverManager,
    ILoggerFactory loggerFactory,
    HttpClient httpClient,
    ILogger<EvalGenService> logger) : IEvalGenService, IDisposable
{
    private EvalGenRun? _currentRun;
    private LlamaServerClient? _managedJudgeClient;
    private bool _disposed;

    // Configuration values from JudgeConfig
    private string _judgeBaseUrl = "http://localhost:8081";
    private double _judgeTemperature = 0.7;
    private int _judgeMaxTokens = 4096;
    private int _judgeParallelSlotCount = 4;

    private void InitializeFromJudgeConfig(JudgeConfig? judgeConfig)
    {
        if (judgeConfig == null)
        {
            // Use defaults
            _judgeBaseUrl = "http://localhost:8081";
            _judgeTemperature = 0.7;
            _judgeMaxTokens = 4096;
            _judgeParallelSlotCount = 4;
            return;
        }

        // Get server URL from config
        if (!string.IsNullOrEmpty(judgeConfig.BaseUrl))
        {
            _judgeBaseUrl = judgeConfig.BaseUrl;
        }
        else if (judgeConfig.ServerConfig != null)
        {
            var host = judgeConfig.ServerConfig.Host ?? "localhost";
            var port = judgeConfig.ServerConfig.Port ?? 8081;
            _judgeBaseUrl = $"http://{host}:{port}";
        }

        // Get temperature from settings
        if (judgeConfig.ServerSettings != null)
        {
            _judgeTemperature = judgeConfig.ServerSettings.SamplingTemperature ?? 0.7;
            _judgeParallelSlotCount = judgeConfig.ServerSettings.ParallelSlotCount ?? 4;
            
            // Use context window as max tokens (leave some room for response)
            var contextTokens = judgeConfig.ServerSettings.ContextWindowTokens ?? 4096;
            _judgeMaxTokens = Math.Min(contextTokens, 8192); // Cap at 8K to leave room for prompt
        }
        else
        {
            _judgeTemperature = 0.7;
            _judgeParallelSlotCount = 4;
            _judgeMaxTokens = 4096;
        }

        logger.LogInformation("EvalGenService configured: BaseUrl={Url}, Temperature={Temp}, MaxTokens={Tokens}, Slots={Slots}",
            _judgeBaseUrl, _judgeTemperature, _judgeMaxTokens, _judgeParallelSlotCount);
    }

    /// <summary>
    /// Merges two judge configs, with overlayConfig taking precedence over baseConfig.
    /// Used to overlay checkpoint judge config on top of current settings.
    /// </summary>
    private static JudgeConfig MergeJudgeConfigs(JudgeConfig baseConfig, JudgeConfig overlayConfig)
    {
        // Start with overlay config (checkpoint - takes precedence)
        // Fill in any missing values from base config (current settings from files/UI)
        return overlayConfig with
        {
            // Use overlay values, fall back to base for missing fields
            BaseUrl = overlayConfig.BaseUrl ?? baseConfig.BaseUrl,
            ServerConfig = overlayConfig.ServerConfig with
            {
                // Merge ServerConfig - overlay takes precedence but fill in missing from base
                Host = overlayConfig.ServerConfig?.Host ?? baseConfig.ServerConfig?.Host ?? "localhost",
                Port = overlayConfig.ServerConfig?.Port ?? baseConfig.ServerConfig?.Port ?? 8081,
                ExecutablePath = overlayConfig.ServerConfig?.ExecutablePath ?? baseConfig.ServerConfig?.ExecutablePath,
                Model = overlayConfig.ServerConfig?.Model ?? baseConfig.ServerConfig?.Model,
                ApiKey = overlayConfig.ServerConfig?.ApiKey ?? baseConfig.ServerConfig?.ApiKey,
                ExtraArgs = overlayConfig.ServerConfig?.ExtraArgs ?? baseConfig.ServerConfig?.ExtraArgs ?? [],
                BaseUrl = overlayConfig.ServerConfig?.BaseUrl ?? baseConfig.ServerConfig?.BaseUrl,
                Manage = overlayConfig.ServerConfig?.Manage ?? baseConfig.ServerConfig?.Manage
            },
            ServerSettings = overlayConfig.ServerSettings with
            {
                // Merge ServerSettings - overlay takes precedence but fill in missing from base
                SamplingTemperature = overlayConfig.ServerSettings?.SamplingTemperature ?? baseConfig.ServerSettings?.SamplingTemperature ?? 0.7,
                ContextWindowTokens = overlayConfig.ServerSettings?.ContextWindowTokens ?? baseConfig.ServerSettings?.ContextWindowTokens ?? 4096,
                ParallelSlotCount = overlayConfig.ServerSettings?.ParallelSlotCount ?? baseConfig.ServerSettings?.ParallelSlotCount ?? 4,
                GpuLayerCount = overlayConfig.ServerSettings?.GpuLayerCount ?? baseConfig.ServerSettings?.GpuLayerCount,
                ModelAlias = overlayConfig.ServerSettings?.ModelAlias ?? baseConfig.ServerSettings?.ModelAlias,
                BatchSizeTokens = overlayConfig.ServerSettings?.BatchSizeTokens ?? baseConfig.ServerSettings?.BatchSizeTokens,
                UbatchSizeTokens = overlayConfig.ServerSettings?.UbatchSizeTokens ?? baseConfig.ServerSettings?.UbatchSizeTokens,
                EnableContinuousBatching = overlayConfig.ServerSettings?.EnableContinuousBatching ?? baseConfig.ServerSettings?.EnableContinuousBatching,
                EnableCachePrompt = overlayConfig.ServerSettings?.EnableCachePrompt ?? baseConfig.ServerSettings?.EnableCachePrompt,
                EnableContextShift = overlayConfig.ServerSettings?.EnableContextShift ?? baseConfig.ServerSettings?.EnableContextShift,
                SplitMode = overlayConfig.ServerSettings?.SplitMode ?? baseConfig.ServerSettings?.SplitMode,
                KvCacheTypeK = overlayConfig.ServerSettings?.KvCacheTypeK ?? baseConfig.ServerSettings?.KvCacheTypeK,
                KvCacheTypeV = overlayConfig.ServerSettings?.KvCacheTypeV ?? baseConfig.ServerSettings?.KvCacheTypeV,
                EnableKvOffload = overlayConfig.ServerSettings?.EnableKvOffload ?? baseConfig.ServerSettings?.EnableKvOffload,
                EnableFlashAttention = overlayConfig.ServerSettings?.EnableFlashAttention ?? baseConfig.ServerSettings?.EnableFlashAttention,
                TopP = overlayConfig.ServerSettings?.TopP ?? baseConfig.ServerSettings?.TopP,
                TopK = overlayConfig.ServerSettings?.TopK ?? baseConfig.ServerSettings?.TopK,
                MinP = overlayConfig.ServerSettings?.MinP ?? baseConfig.ServerSettings?.MinP,
                RepeatPenalty = overlayConfig.ServerSettings?.RepeatPenalty ?? baseConfig.ServerSettings?.RepeatPenalty,
                RepeatLastNTokens = overlayConfig.ServerSettings?.RepeatLastNTokens ?? baseConfig.ServerSettings?.RepeatLastNTokens,
                PresencePenalty = overlayConfig.ServerSettings?.PresencePenalty ?? baseConfig.ServerSettings?.PresencePenalty,
                FrequencyPenalty = overlayConfig.ServerSettings?.FrequencyPenalty ?? baseConfig.ServerSettings?.FrequencyPenalty,
                Seed = overlayConfig.ServerSettings?.Seed ?? baseConfig.ServerSettings?.Seed,
                ThreadCount = overlayConfig.ServerSettings?.ThreadCount ?? baseConfig.ServerSettings?.ThreadCount,
                HttpThreadCount = overlayConfig.ServerSettings?.HttpThreadCount ?? baseConfig.ServerSettings?.HttpThreadCount,
                ChatTemplate = overlayConfig.ServerSettings?.ChatTemplate ?? baseConfig.ServerSettings?.ChatTemplate,
                EnableJinja = overlayConfig.ServerSettings?.EnableJinja ?? baseConfig.ServerSettings?.EnableJinja,
                ReasoningFormat = overlayConfig.ServerSettings?.ReasoningFormat ?? baseConfig.ServerSettings?.ReasoningFormat,
                LogVerbosity = overlayConfig.ServerSettings?.LogVerbosity ?? baseConfig.ServerSettings?.LogVerbosity,
                EnableMlock = overlayConfig.ServerSettings?.EnableMlock ?? baseConfig.ServerSettings?.EnableMlock,
                EnableMmap = overlayConfig.ServerSettings?.EnableMmap ?? baseConfig.ServerSettings?.EnableMmap,
                ServerTimeoutSeconds = overlayConfig.ServerSettings?.ServerTimeoutSeconds ?? baseConfig.ServerSettings?.ServerTimeoutSeconds,
                ExtraArgs = overlayConfig.ServerSettings?.ExtraArgs ?? baseConfig.ServerSettings?.ExtraArgs
            },
            // Preserve other overlay fields
            JudgePromptTemplate = overlayConfig.JudgePromptTemplate,
            ResponseFormat = overlayConfig.ResponseFormat,
            ScoreMinValue = overlayConfig.ScoreMinValue,
            ScoreMaxValue = overlayConfig.ScoreMaxValue,
            JudgeSystemPrompt = overlayConfig.JudgeSystemPrompt,
            JudgeMaxTokenCount = overlayConfig.JudgeMaxTokenCount,
            JudgeSamplingTemperature = overlayConfig.JudgeSamplingTemperature
        };
    }

    private static HttpClient CreateHttpClient(ServerInfo serverInfo)
    {
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(serverInfo.BaseUrl),
            Timeout = TimeSpan.FromHours(1)
        };
        if (!string.IsNullOrEmpty(serverInfo.ApiKey))
        {
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", serverInfo.ApiKey);
        }
        return httpClient;
    }

    public bool IsGenerationActive => _currentRun?.IsRunning == true && !_currentRun.IsCompleted;

    public EvalGenRun? CurrentRun => _currentRun;

    public Task<EvalGenRun> GenerateAsync(EvalGenConfig config, JudgeConfig? judgeConfig, CancellationToken cancellationToken)
    {
        if (_currentRun?.IsRunning == true && !_currentRun.IsCompleted)
            throw new InvalidOperationException("An eval generation run is already in progress");

        var checkpointPath = config.CheckpointDatabasePath ??
            Path.Combine(config.OutputDirectoryPath, $"{config.RunName}_checkpoint.db");

        // Ensure output directory exists before creating collector
        try
        {
            Directory.CreateDirectory(config.OutputDirectoryPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to create output directory: {config.OutputDirectoryPath}", ex);
        }

        // Create collector first so it can be shared with UI
        var collector = new EvalGenCheckpointCollector(checkpointPath);

        _currentRun = new EvalGenRun(config, async ct => await ExecuteGenerationAsync(config, judgeConfig, collector, ct));
        _currentRun.CheckpointDatabasePath = checkpointPath;
        _currentRun.Collector = collector;
        _currentRun.Start();
        return Task.FromResult(_currentRun);
    }

    private async Task ExecuteGenerationAsync(EvalGenConfig config, JudgeConfig? judgeConfig, EvalGenCheckpointCollector collector, CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Starting eval generation: {RunName}, ContinueFromCheckpoint={ContinueFromCheckpoint}", config.RunName, config.ContinueFromCheckpoint);

            // Ensure output directory exists
            Directory.CreateDirectory(config.OutputDirectoryPath);
            var promptsDir = Path.Combine(config.OutputDirectoryPath, "prompts");
            var expectedDir = Path.Combine(config.OutputDirectoryPath, "expected_outputs");
            Directory.CreateDirectory(promptsDir);
            Directory.CreateDirectory(expectedDir);

            // Use provided collector (already created and cached)
            await collector.InitializeAsync(cancellationToken);

            // Load or save startup parameters - DO THIS FIRST to get judge config for checkpoint resumes
            var savedParams = await collector.LoadStartupParametersAsync(cancellationToken);
            if (savedParams != null && config.ContinueFromCheckpoint)
            {
                logger.LogInformation("Continuing from checkpoint: {RunName}", savedParams.Value.Config.RunName);
                logger.LogInformation("Checkpoint JudgeConfig: Manage={Manage}, BaseUrl={BaseUrl}, Model={Model}, ExecutablePath={ExecPath}",
                    savedParams.Value.JudgeConfig?.Manage,
                    savedParams.Value.JudgeConfig?.BaseUrl,
                    savedParams.Value.JudgeConfig?.ServerConfig?.Model?.FilePath,
                    savedParams.Value.JudgeConfig?.ServerConfig?.ExecutablePath);
                config = savedParams.Value.Config;
                // Merge: checkpoint judge config is overlaid ON TOP of current judge config from settings
                // Checkpoint values take precedence, but missing values are filled from current config
                if (savedParams.Value.JudgeConfig != null && judgeConfig != null)
                {
                    // Overlay checkpoint config on top of current judge config (checkpoint wins)
                    judgeConfig = MergeJudgeConfigs(baseConfig: judgeConfig, overlayConfig: savedParams.Value.JudgeConfig);
                }
                else if (savedParams.Value.JudgeConfig != null)
                {
                    // No current judge config, use checkpoint config as-is
                    judgeConfig = savedParams.Value.JudgeConfig;
                }
            }
            else
            {
                await collector.SaveStartupParametersAsync(config, judgeConfig, cancellationToken);
            }

            // Initialize configuration from judge config (after checkpoint load so we use the saved config)
            InitializeFromJudgeConfig(judgeConfig);

            logger.LogInformation("Judge config for this run: Manage={Manage}, BaseUrl={BaseUrl}, Model={Model}, ExecutablePath={ExecPath}",
                judgeConfig?.Manage,
                judgeConfig?.BaseUrl ?? $"{judgeConfig?.ServerConfig?.Host}:{judgeConfig?.ServerConfig?.Port}",
                judgeConfig?.ServerConfig?.Model?.FilePath,
                judgeConfig?.ServerConfig?.ExecutablePath);

            // Start managed judge server - use != false pattern like TwoPhaseEvalRunViewModel
            // When resuming from checkpoint, always start the server (don't try to connect to external)
            LlamaServerClient? judgeClientToUse = null;
            bool shouldManageServer = judgeConfig is { Manage: not false } || config.ContinueFromCheckpoint;
            
            if (shouldManageServer && serverLifecycle != null)
            {
                logger.LogInformation("Starting judge llama-server (Manage={Manage})...", judgeConfig?.Manage);
                _currentRun!.UpdateProgress(new EvalGenProgress
                {
                    CurrentPhase = EvalGenPhase.GeneratingCategories,
                    CategoriesGenerated = 0,
                    TargetCategories = config.TargetCategoryCount,
                    ProblemsGenerated = 0,
                    TargetProblems = config.TargetCategoryCount * config.TargetProblemsPerCategory,
                    ProblemsFleshedOut = 0,
                    StatusMessage = "Starting judge llama-server..."
                });

                var judgeServerConfig = judgeConfig?.ServerConfig ?? new ServerConfig { Host = "localhost", Port = 8081 };
                var judgeServerSettings = judgeConfig?.ServerSettings ?? new LlamaServerSettings();

                var judgeStartResult = await serverLifecycle.StartAsync(judgeServerConfig, judgeServerSettings, cancellationToken);
                if (judgeStartResult.IsFailed)
                    throw new InvalidOperationException($"Failed to start judge llama-server: {judgeStartResult.Errors[0].Message}");

                var judgeServerInfo = judgeStartResult.Value;
                logger.LogInformation("Judge llama-server started at {BaseUrl}", judgeServerInfo.BaseUrl);

                var judgeHttpClient = CreateHttpClient(judgeServerInfo);
                var judgeClientLogger = loggerFactory.CreateLogger<LlamaServerClient>();
                var maxConcurrent = judgeServerSettings?.ParallelSlotCount ?? _judgeParallelSlotCount;
                _managedJudgeClient = new LlamaServerClient(judgeServerInfo, judgeHttpClient, judgeClientLogger, maxConcurrent);
                await _managedJudgeClient.InitializeSemaphoreFromServerAsync(cancellationToken);
                judgeClientToUse = _managedJudgeClient;
            }
            else if (serverLifecycle != null)
            {
                // serverLifecycle is available but Manage is explicitly false - connect to external server
                logger.LogInformation("Connecting to external judge server at {BaseUrl}...", _judgeBaseUrl);
                _currentRun!.UpdateProgress(new EvalGenProgress
                {
                    CurrentPhase = EvalGenPhase.GeneratingCategories,
                    CategoriesGenerated = 0,
                    TargetCategories = config.TargetCategoryCount,
                    ProblemsGenerated = 0,
                    TargetProblems = config.TargetCategoryCount * config.TargetProblemsPerCategory,
                    ProblemsFleshedOut = 0,
                    StatusMessage = "Connecting to judge server..."
                });

                var serverConfig = new ServerConfig
                {
                    Manage = false,
                    BaseUrl = _judgeBaseUrl
                };

                var connectResult = await serverManager.StartAsync(serverConfig, null!, cancellationToken);
                if (connectResult.IsFailed)
                {
                    throw new InvalidOperationException(
                        $"Failed to connect to judge server at {_judgeBaseUrl}: {connectResult.Errors[0].Message}. " +
                        $"Please ensure llama-server is running at that address, or configure it to be managed in Settings.");
                }

                var judgeServerInfo = connectResult.Value;
                logger.LogInformation("Connected to judge server at {BaseUrl}", judgeServerInfo.BaseUrl);

                var judgeHttpClient = CreateHttpClient(judgeServerInfo);
                var judgeClientLogger = loggerFactory.CreateLogger<LlamaServerClient>();
                _managedJudgeClient = new LlamaServerClient(judgeServerInfo, judgeHttpClient, judgeClientLogger, judgeServerInfo.TotalSlots);
                await _managedJudgeClient.InitializeSemaphoreFromServerAsync(cancellationToken);
                judgeClientToUse = _managedJudgeClient;
            }
            else
            {
                // No serverLifecycle available - this shouldn't happen in normal UI usage
                throw new InvalidOperationException(
                    "ServerLifecycle service is not available. Cannot start or connect to judge server. " +
                    "Please ensure the application is properly configured.");
            }

            // Load existing progress
            var categories = await collector.LoadCategoriesAsync(cancellationToken);
            var allProblems = await collector.LoadProblemsAsync(cancellationToken);

            _currentRun!.UpdateProgress(new EvalGenProgress
            {
                CurrentPhase = EvalGenPhase.GeneratingCategories,
                CategoriesGenerated = categories.Count,
                TargetCategories = config.TargetCategoryCount,
                ProblemsGenerated = allProblems.Count,
                TargetProblems = config.TargetCategoryCount * config.TargetProblemsPerCategory,
                ProblemsFleshedOut = allProblems.Count(p => p.IsComplete),
                StatusMessage = $"Loaded {categories.Count} categories, {allProblems.Count} problems from checkpoint"
            });

            // Phase 1: Generate categories
            if (categories.Count < config.TargetCategoryCount)
            {
                logger.LogInformation("Phase 1: Generating categories (have {Have}, need {Need})",
                    categories.Count, config.TargetCategoryCount);

                categories = await GenerateCategoriesAsync(config, categories, collector, cancellationToken);

                _currentRun.UpdateProgress(_currentRun.Progress with
                {
                    CurrentPhase = EvalGenPhase.GeneratingCategories,
                    CategoriesGenerated = categories.Count,
                    StatusMessage = $"Generated {categories.Count} categories"
                });

                // Reload problems after category generation to get fresh data
                allProblems = await collector.LoadProblemsAsync(cancellationToken);
            }

            // Phase 2: Generate one-line problems for each category
            var problemsByCategory = allProblems.GroupBy(p => p.CategoryId).ToDictionary(g => g.Key, g => g.ToList());
            bool needMoreProblems = categories.Any(c =>
                (problemsByCategory.GetValueOrDefault(c.Id)?.Count ?? 0) < config.TargetProblemsPerCategory);

            logger.LogInformation("Phase 2 check: categories={CatCount}, problems={ProbCount}, needMore={NeedMore}",
                categories.Count, allProblems.Count, needMoreProblems);

            if (needMoreProblems)
            {
                logger.LogInformation("Phase 2: Generating one-line problem statements");

                _currentRun.UpdateProgress(_currentRun.Progress with
                {
                    CurrentPhase = EvalGenPhase.GeneratingProblems,
                    StatusMessage = "Generating problem statements"
                });

                await GenerateProblemsAsync(config, categories, problemsByCategory, collector, cancellationToken);

                allProblems = await collector.LoadProblemsAsync(cancellationToken);
                problemsByCategory = allProblems.GroupBy(p => p.CategoryId).ToDictionary(g => g.Key, g => g.ToList());

                _currentRun.UpdateProgress(_currentRun.Progress with
                {
                    ProblemsGenerated = allProblems.Count,
                    StatusMessage = $"Generated {allProblems.Count} problem statements"
                });
            }

            // Phase 3: Flesh out problems
            var incompleteProblems = allProblems.Where(p => !p.IsComplete).ToList();
            if (incompleteProblems.Count > 0)
            {
                logger.LogInformation("Phase 3: Fleshing out {Count} problems", incompleteProblems.Count);

                _currentRun.UpdateProgress(_currentRun.Progress with
                {
                    CurrentPhase = EvalGenPhase.FleshingOutProblems,
                    StatusMessage = $"Fleshing out {incompleteProblems.Count} problems"
                });

                await FleshOutProblemsAsync(config, collector, cancellationToken);

                allProblems = await collector.LoadProblemsAsync(cancellationToken);

                _currentRun.UpdateProgress(_currentRun.Progress with
                {
                    ProblemsFleshedOut = allProblems.Count(p => p.IsComplete),
                    StatusMessage = $"Completed {allProblems.Count(p => p.IsComplete)} problems"
                });
            }

            // Save final results in SplitDirectories format
            await SaveResultsAsync(config, categories, allProblems, cancellationToken);

            _currentRun.UpdateProgress(_currentRun.Progress with
            {
                CurrentPhase = EvalGenPhase.Completed,
                StatusMessage = "Generation complete"
            });

            logger.LogInformation("Eval generation completed: {RunName}", config.RunName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Eval generation cancelled: {RunName}", config.RunName);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during eval generation: {RunName}", config.RunName);
            throw;
        }
        finally
        {
            // Cleanup managed judge server
            _managedJudgeClient?.Dispose();
            _managedJudgeClient = null;
        }
    }

    private async Task<List<GeneratedCategory>> GenerateCategoriesAsync(
        EvalGenConfig config,
        List<GeneratedCategory> existingCategories,
        EvalGenCheckpointCollector collector,
        CancellationToken cancellationToken)
    {
        var categories = new ConcurrentBag<GeneratedCategory>(existingCategories);
        int iteration = 0;
        int maxIterations = Math.Max(3, config.TargetCategoryCount / 10);
        int emptyIterations = 0; // Track consecutive iterations with no new categories
        int consecutiveFailures = 0; // Track consecutive failed LLM calls

        // TODO: When adding web search feature:
        // - Search for "LLM evaluation categories", "AI benchmark taxonomies"
        // - Extract category names and descriptions from search results
        // - Include discovered categories in the initial prompt
        // - Pass web search results as additional context to the LLM

        // Purely linear loop, since each generation includes the already-generated categories from the previous as part of the prompt.
        while (categories.Count < config.TargetCategoryCount && iteration < maxIterations && emptyIterations < 5 && consecutiveFailures < 1)
        {
            iteration++;
            cancellationToken.ThrowIfCancellationRequested();
            await _currentRun!.WaitForResumeAsync(cancellationToken);

            int remaining = config.TargetCategoryCount - categories.Count;
            logger.LogInformation("Category generation iteration {Iteration}: need {Remaining} more categories",
                iteration, remaining);

            var existingCategoryNames = categories.Select(c => c.Name).ToList();
            List<GeneratedCategory> newCategories;
            
            try
            {
                newCategories = await GenerateCategoryBatchAsync(
                    config,
                    existingCategoryNames,
                    cancellationToken);
                consecutiveFailures = 0; // Reset on success
            }
            catch (Exception ex) when (ex is not TaskCanceledException and not OperationCanceledException)
            {
                logger.LogError(ex, "Category generation iteration {Iteration} failed", iteration);
                consecutiveFailures++;
                newCategories = [];

                // If temperature is 0, give up after first failure (will keep failing)
                if (_judgeTemperature == 0)
                {
                    _currentRun!.TotalCategoriesFailed++;
                    logger.LogWarning("Temperature is 0, giving up on category generation after failure");
                    break;
                }
                continue;
            }

            int newCount = 0;
            foreach (var category in newCategories)
            {
                if (!categories.Any(c => c.Name.Equals(category.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    categories.Add(category);
                    await collector.SaveCategoryAsync(category, cancellationToken);
                    newCount++;
                }
            }

            // Track empty iterations
            if (newCount == 0)
            {
                emptyIterations++;
                logger.LogWarning("Category iteration {Iteration} produced no new categories (empty iteration {Empty})",
                    iteration, emptyIterations);
            }
            else
            {
                emptyIterations = 0; // Reset on success
                logger.LogInformation("Category iteration {Iteration} added {NewCount} new categories", iteration, newCount);
            }

            _currentRun.UpdateProgress(_currentRun.Progress with
            {
                CategoriesGenerated = categories.Count,
                StatusMessage = $"Generated {categories.Count}/{config.TargetCategoryCount} categories"
            });
        }

        logger.LogInformation("Category generation complete: {Count}/{Target} categories generated after {Iterations} iterations",
            categories.Count, config.TargetCategoryCount, iteration);

        return categories.ToList();
    }

    private async Task<List<GeneratedCategory>> GenerateCategoryBatchAsync(
        EvalGenConfig config,
        List<string> existingCategories,
        CancellationToken cancellationToken)
    {
        var prompt = BuildCategoryGenerationPrompt(config, existingCategories);
        var response = await CallJudgeLLMAsync(prompt, config.SystemPrompt, cancellationToken);

        var categories = ParseCategoriesFromResponse(response, existingCategories);
        logger.LogInformation("Generated {Count} new categories from LLM response", categories.Count);

        return categories;
    }

    private string BuildCategoryGenerationPrompt(
        EvalGenConfig config,
        List<string> existingCategories)
    {
        // Use custom template if provided, otherwise use default
        var template = config.Phase1PromptTemplate ?? DefaultPhase1Prompt;
        
        var existingSection = existingCategories.Count > 0
            ? $"""
            We already have these categories (do NOT duplicate them):
            {string.Join("\n", existingCategories.Select((c, i) => $"{i + 1}. {c}"))}
            """
            : "";

        var contextPromptSection = config.ContextPrompt != null
            ? $"Additional context: {config.ContextPrompt}"
            : "";

        // Replace template tags with actual values
        return template
            .Replace("{TargetCategoryCount}", config.TargetCategoryCount.ToString())
            .Replace("{DomainPrompt}", config.DomainPrompt ?? "General LLM capabilities")
            .Replace("{ExistingCategoriesSection}", existingSection)
            .Replace("{ContextPromptSection}", contextPromptSection);
    }

    /// <summary>
    /// Default prompt template for Phase 1 (category generation).
    /// Use tags like {TargetCategoryCount}, {DomainPrompt}, {ExistingCategoriesSection}, {ContextPromptSection}.
    /// </summary>
    public const string DefaultPhase1Prompt = """
        You are an expert at designing evaluation datasets for large language models.

        Task: Generate a total of {TargetCategoryCount} NON-OVERLAPPING categories of problems that would challenge an LLM.

        Context:
        Domain focus: {DomainPrompt}
        {ExistingCategoriesSection}
        {ContextPromptSection}

        Requirements:
        - Categories must be distinct and non-overlapping
        - Each category should cover a specific skill or knowledge area
        - Categories should be specific enough to generate concrete problems
        - Avoid generic categories like "General Knowledge" or "Basic Reasoning"
        - Output ONE category name per line

        First principles for reasoning:
        - Exhaust the orthogonal axes. Find the dimensions that cut across the domain independently — e.g., for a coding domain: error type, system layer, temporal behavior, scope. Then take cross-products where interesting.
        - Separate "what goes wrong" from "what kind of task." Categories should cover both failure modes (bugs, edge cases, misunderstandings) and task types (generation, diagnosis, transformation, explanation). Conflating them creates blind spots.
        - Distinguish surface similarity from structural similarity. Two problems can look alike syntactically but require fundamentally different reasoning. Good categories cluster by *required cognitive operation*, not by keywords or surface topic.
        - Include the meta-categories. Every domain has categories about the domain itself: ambiguous requirements, conflicting constraints, knowing when a problem is unsolvable or out of scope. These are often the hardest and most revealing.
        - Check for coverage of difficulty gradients within each category. A category with only hard problems or only trivial ones is incomplete. Each category should be stretchy enough to contain a spectrum.
        - Prefer categories that are independently failable. A model can be excellent at one and poor at another. If two categories always co-fail or co-succeed, they're probably the same category.

        Respond with one category per line inside XML-like <Categories> tags (no actual XML encoding), like this:
        <Categories>
        Category Name 1
        Category Name 2
        Category Name 3
        </Categories>
        """;

    private List<GeneratedCategory> ParseCategoriesFromResponse(string response, List<string> existingCategories)
    {
        var categories = new List<GeneratedCategory>();

        try
        {
            // Find <Categories> tags using IndexOf (more robust than regex)
            var openTag = "<Categories>";
            var closeTag = "</Categories>";
            
            var startIndex = response.IndexOf(openTag, StringComparison.OrdinalIgnoreCase);
            var endIndex = response.IndexOf(closeTag, StringComparison.OrdinalIgnoreCase);
            
            if (startIndex < 0 || endIndex < 0 || endIndex <= startIndex)
            {
                logger.LogWarning("No <Categories> tags found in LLM response");
                return categories;
            }

            var content = response.Substring(startIndex + openTag.Length, endIndex - startIndex - openTag.Length);
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var name = line.Trim();
                if (name.StartsWith("<Category>")) name = name.Replace("<Category>", "").Replace("</Category>", "");
                if (string.IsNullOrWhiteSpace(name) || name.StartsWith('<'))
                    continue;

                // Remove leading numbers/bullets
                name = ListItemPrefixRegex().Replace(name, "").Trim();
                
                // Skip if too short or looks like XML/instructions
                if (name.Length < 3 || name.Contains("</") || name.Contains(">/") || 
                    name.StartsWith("Here") || name.StartsWith("Sure") || name.StartsWith("Below"))
                    continue;
                
                // Skip duplicates
                if (existingCategories.Any(c => c.Equals(name, StringComparison.OrdinalIgnoreCase)) ||
                    categories.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    continue;

                categories.Add(new GeneratedCategory
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = name
                });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error parsing categories from LLM response");
        }

        return categories;
    }

    private async Task GenerateProblemsAsync(
        EvalGenConfig config,
        List<GeneratedCategory> categories,
        Dictionary<string, List<GeneratedProblem>> problemsByCategory,
        EvalGenCheckpointCollector collector,
        CancellationToken cancellationToken)
    {
        // Track categories that have failed (for temp=0, we skip them after first failure)
        var failedCategories = new ConcurrentBag<string>();
        
        // Loop until all categories have enough problems
        int maxIterations = config.TargetCategoryCount * 3; // Prevent infinite loops
        int emptyIterations = 0; // Track consecutive iterations with no new problems

        // Outer loop (iterations) is purely linear, since each generation includes the already-generated problem statements from the past runs *for the current category* as part of the prompt.
        for (int iteration = 0; iteration < maxIterations && emptyIterations < 5; iteration++)
        {
            // Check which categories still need problems (excluding failed ones if temp=0)
            var categoriesNeedingProblems = new List<(GeneratedCategory Category, int Remaining)>();
            foreach (var category in categories)
            {
                // Skip categories that failed if temperature is 0
                if (_judgeTemperature == 0 && failedCategories.Contains(category.Id))
                    continue;
                    
                var existingProblems = problemsByCategory.GetValueOrDefault(category.Id) ?? [];
                int existingCount = existingProblems.Count;
                if (existingCount < config.TargetProblemsPerCategory)
                {
                    categoriesNeedingProblems.Add((category, config.TargetProblemsPerCategory - existingCount));
                }
            }

            if (categoriesNeedingProblems.Count == 0)
            {
                logger.LogInformation("Phase 2 complete: all categories have {Target} problems each", config.TargetProblemsPerCategory);
                break;
            }

            logger.LogInformation("Phase 2 iteration {Iteration}: {Count} categories still need problems",
                iteration + 1, categoriesNeedingProblems.Count);

            // Generate problems for categories that need them
            var semaphore = new SemaphoreSlim(_managedJudgeClient.CurrentMaxConcurrent, _managedJudgeClient.CurrentMaxConcurrent);
            var tasks = new List<Task>();
            int totalNewProblems = 0;

            foreach (var (category, remaining) in categoriesNeedingProblems)
            {
                var existingProblems = problemsByCategory.GetValueOrDefault(category.Id) ?? [];
                await semaphore.WaitAsync(cancellationToken);

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await _currentRun!.WaitForResumeAsync(cancellationToken);

                        var existingStatements = existingProblems.Select(p => p.OneLineStatement).ToList();

                        logger.LogDebug("Generating problems for category '{Category}': need {Remaining}",
                            category.Name, remaining);

                        var newProblems = await GenerateProblemsForCategoryAsync(
                            config, category, existingStatements, cancellationToken);

                        foreach (var problem in newProblems)
                        {
                            await collector.SaveProblemAsync(problem, cancellationToken);
                            if (!problemsByCategory.TryGetValue(category.Id, out var problemList))
                            {
                                problemList = [];
                                problemsByCategory[category.Id] = problemList;
                            }
                            problemList.Add(problem);
                            totalNewProblems++;

                            _currentRun.UpdateProgress(_currentRun.Progress with
                            {
                                ProblemsGenerated = _currentRun.Progress.ProblemsGenerated + 1,
                                StatusMessage = $"Generated {_currentRun.Progress.ProblemsGenerated} problem statements"
                            });
                        }
                    }
                    catch (Exception ex) when (ex is not TaskCanceledException and not OperationCanceledException)
                    {
                        logger.LogError(ex, "Failed to generate problems for category '{Category}'", category.Name);

                        // If temperature is 0, mark this category as failed (won't retry)
                        if (_judgeTemperature == 0)
                        {
                            failedCategories.Add(category.Id);
                            _currentRun!.TotalProblemsFailed++;
                            logger.LogWarning("Temperature is 0, skipping category '{Category}' after failure", category.Name);
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken));
            }

            await Task.WhenAll(tasks);

            // Track empty iterations
            if (totalNewProblems == 0)
            {
                emptyIterations++;
                logger.LogWarning("Phase 2 iteration {Iteration} produced no new problems (empty iteration {Empty})",
                    iteration + 1, emptyIterations);
            }
            else
            {
                emptyIterations = 0; // Reset on success
                logger.LogInformation("Phase 2 iteration {Iteration} added {NewCount} new problems", iteration + 1, totalNewProblems);
            }
        }

        logger.LogInformation("Phase 2 complete: generated {Total} problems after multiple iterations",
            problemsByCategory.Values.Sum(list => list.Count));
    }

    private async Task<List<GeneratedProblem>> GenerateProblemsForCategoryAsync(
        EvalGenConfig config,
        GeneratedCategory category,
        List<string> existingProblems,
        CancellationToken cancellationToken)
    {
        var prompt = BuildProblemGenerationPrompt(config, category, existingProblems);
        var response = await CallJudgeLLMAsync(prompt, config.SystemPrompt, cancellationToken);

        var problems = ParseProblemsFromResponse(response, category.Id, existingProblems);
        logger.LogDebug("Generated {Count} problems for category '{Category}'", problems.Count, category.Name);

        return problems;
    }

    private string BuildProblemGenerationPrompt(
        EvalGenConfig config,
        GeneratedCategory category,
        List<string> existingProblems)
    {
        // Use custom template if provided, otherwise use default
        var template = config.Phase2PromptTemplate ?? DefaultPhase2Prompt;

        var existingSection = existingProblems.Count > 0
            ? string.Join("\n", existingProblems.Select((p, i) => $"{i + 1}. {p}"))
            : "";

        var contextPromptSection = config.ContextPrompt != null
            ? $"Additional context: {config.ContextPrompt}"
            : "";

        // Replace template tags with actual values
        return template
            .Replace("{CategoryName}", category.Name)
            .Replace("{TargetProblemsPerCategory}", config.TargetProblemsPerCategory.ToString())
            .Replace("{DomainPrompt}", config.DomainPrompt ?? "General LLM capabilities")
            .Replace("{ExistingProblemsSection}", existingSection)
            .Replace("{ContextPromptSection}", contextPromptSection);
    }

    /// <summary>
    /// Default prompt template for Phase 2 (problem generation).
    /// Use tags like {CategoryName}, {TargetProblemsPerCategory}, {DomainPrompt}, {ExistingProblemsSection}, {ContextPromptSection}.
    /// </summary>
    public const string DefaultPhase2Prompt = """
        You are an expert at designing evaluation problems for large language models.

        Category: {CategoryName}

        Task: Generate a total of {TargetProblemsPerCategory} specific, concrete, one-line problem statements for this category.

        {ExistingProblemsSection}
        Domain focus: {DomainPrompt}
        {ContextPromptSection}

        Requirements:
        - Each problem should be specific and concrete (one line)
        - Problems should be challenging but solvable
        - Avoid duplicating existing problems
        - Include variety in difficulty and specific scenarios
        - Problems should be self-contained (no external references)
        - Output ONE problem statement per line

        First principles for reasoning:
        - Fix every degree of freedom that isn't being tested. Ambiguity in the setup contaminates the signal. If you're testing reasoning about X, make everything except X fully determined.
        - Vary the "load-bearing detail" location. The key information that changes the answer should sometimes appear early, sometimes late, sometimes buried — because retrieval position is itself a real-world variable.
        - Include problems where the obvious answer is wrong. At least some examples in each category should have a plausible-but-incorrect surface answer (a trap). This separates shallow pattern-matching from actual reasoning.
        - Cover the boundary, not just the interior. The most diagnostic problems sit at the edge of the category — where it almost-but-doesn't violate a constraint, where two rules just barely conflict, where an assumption nearly holds.
        - Avoid synthetic problems that only LLMs encounter. Problem statements should feel like something a real person would genuinely send. Artificial constructions reveal artifacts of your benchmark design, not real capability.
        - One problem, one crux. Each example should hinge on a single decision point or insight. Multi-crux problems are useful eventually, but they make failure analysis ambiguous — you can't tell which crux broke.
        
        Respond with one problem per line inside XML-like <ProblemStatements> tags (no actual XML encoding), like this:
        <ProblemStatements>
        Problem statement 1
        Problem statement 2
        Problem statement 3
        </ProblemStatements>
        """;

    private List<GeneratedProblem> ParseProblemsFromResponse(string response, string categoryId, List<string> existingProblems)
    {
        var problems = new List<GeneratedProblem>();

        try
        {
            // Find <ProblemStatements> tags using IndexOf (more robust than regex)
            var openTag = "<ProblemStatements>";
            var closeTag = "</ProblemStatements>";
            
            var startIndex = response.IndexOf(openTag, StringComparison.OrdinalIgnoreCase);
            var endIndex = response.IndexOf(closeTag, StringComparison.OrdinalIgnoreCase);
            
            if (startIndex < 0 || endIndex < 0 || endIndex <= startIndex)
            {
                logger.LogWarning("No <ProblemStatements> tags found in LLM response");
                return problems;
            }

            var content = response.Substring(startIndex + openTag.Length, endIndex - startIndex - openTag.Length);
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var statement = line.Trim();
                if (statement.StartsWith("<ProblemStatement>")) statement = statement.Replace("<ProblemStatement>", "").Replace("</ProblemStatement>", "");
                if (string.IsNullOrWhiteSpace(statement) || statement.StartsWith('<'))
                    continue;

                // Remove leading numbers/bullets
                statement = ListItemPrefixRegex().Replace(statement, "").Trim();

                // Skip if too short or looks like XML/instructions
                if (statement.Length < 5 || statement.Contains("</") || statement.Contains(">/") ||
                    statement.StartsWith("Here") || statement.StartsWith("Sure") || statement.StartsWith("Below"))
                    continue;

                // Skip duplicates
                if (existingProblems.Any(p => p.Equals(statement, StringComparison.OrdinalIgnoreCase)) ||
                    problems.Any(p => p.OneLineStatement.Equals(statement, StringComparison.OrdinalIgnoreCase)))
                    continue;

                problems.Add(new GeneratedProblem
                {
                    Id = Guid.NewGuid().ToString(),
                    CategoryId = categoryId,
                    OneLineStatement = statement
                });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error parsing problems from LLM response");
        }

        return problems;
    }

    private async Task FleshOutProblemsAsync(
        EvalGenConfig config,
        EvalGenCheckpointCollector collector,
        CancellationToken cancellationToken)
    {
        // Track problems that have failed (for temp=0, we skip them after first failure)
        var failedProblems = new ConcurrentBag<string>();
        
        // Loop until all problems are fleshed out
        int maxIterations = config.TargetProblemsPerCategory * 3; // Prevent infinite loops
        int emptyIterations = 0; // Track consecutive iterations with no progress

        for (int iteration = 0; iteration < maxIterations && emptyIterations < 5; iteration++)
        {
            // Reload incomplete problems from DB
            var allProblems = await collector.LoadProblemsAsync(cancellationToken);
            var stillIncomplete = allProblems.Where(p => !p.IsComplete).ToList();

            if (stillIncomplete.Count == 0)
            {
                logger.LogInformation("Phase 3 complete: all {Total} problems fleshed out", allProblems.Count);
                break;
            }

            logger.LogInformation("Phase 3 iteration {Iteration}: {Count} problems still need fleshing out",
                iteration + 1, stillIncomplete.Count);

            var semaphore = new SemaphoreSlim(_managedJudgeClient.CurrentMaxConcurrent, _managedJudgeClient.CurrentMaxConcurrent);
            var tasks = new List<Task>();
            int completedThisIteration = 0;

            foreach (var problem in stillIncomplete)
            {
                // Skip problems that failed if temperature is 0
                if (_judgeTemperature == 0 && failedProblems.Contains(problem.Id))
                    continue;
                    
                await semaphore.WaitAsync(cancellationToken);

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await _currentRun!.WaitForResumeAsync(cancellationToken);

                        logger.LogDebug("Fleshing out problem: {Statement}", problem.OneLineStatement);

                        var (fullPrompt, expectedOutput) = await FleshOutProblemAsync(
                            config, problem, cancellationToken);

                        // Only save if we got valid output
                        if (!string.IsNullOrEmpty(fullPrompt) && !string.IsNullOrEmpty(expectedOutput))
                        {
                            var updatedProblem = problem with
                            {
                                FullPrompt = fullPrompt,
                                ExpectedOutput = expectedOutput
                            };

                            await collector.SaveProblemAsync(updatedProblem, cancellationToken);
                            completedThisIteration++;

                            _currentRun.UpdateProgress(_currentRun.Progress with
                            {
                                ProblemsFleshedOut = _currentRun.Progress.ProblemsFleshedOut + 1,
                                StatusMessage = $"Fleshed out {_currentRun.Progress.ProblemsFleshedOut} problems"
                            });
                        }
                    }
                    catch (Exception ex) when (ex is not TaskCanceledException and not OperationCanceledException)
                    {
                        logger.LogError(ex, "Failed to flesh out problem: {Statement}", problem.OneLineStatement);

                        // If temperature is 0, mark this problem as failed (won't retry)
                        if (_judgeTemperature == 0)
                        {
                            failedProblems.Add(problem.Id);
                            _currentRun!.TotalFleshOutFailed++;
                            logger.LogWarning("Temperature is 0, skipping problem after failure: {Statement}", problem.OneLineStatement);
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken));
            }

            await Task.WhenAll(tasks);

            // Track empty iterations
            if (completedThisIteration == 0)
            {
                emptyIterations++;
                logger.LogWarning("Phase 3 iteration {Iteration} completed no problems (empty iteration {Empty})",
                    iteration + 1, emptyIterations);
            }
            else
            {
                emptyIterations = 0; // Reset on success
                logger.LogInformation("Phase 3 iteration {Iteration} fleshed out {Count} problems", iteration + 1, completedThisIteration);
            }
        }

        // Final status
        var finalProblems = await collector.LoadProblemsAsync(cancellationToken);
        logger.LogInformation("Phase 3 complete: {Complete}/{Total} problems fleshed out",
            finalProblems.Count(p => p.IsComplete), finalProblems.Count);
    }

    private async Task<(string FullPrompt, string ExpectedOutput)> FleshOutProblemAsync(
        EvalGenConfig config,
        GeneratedProblem problem,
        CancellationToken cancellationToken)
    {
        var prompt = BuildFleshOutPrompt(config, problem);
        var response = await CallJudgeLLMAsync(prompt, config.SystemPrompt, cancellationToken);

        return ParseFleshedOutProblem(response);
    }

    private string BuildFleshOutPrompt(EvalGenConfig config, GeneratedProblem problem)
    {
        // Use custom template if provided, otherwise use default
        var template = config.Phase3PromptTemplate ?? DefaultPhase3Prompt;
        
        var contextPromptSection = config.ContextPrompt != null
            ? $"Additional context: {config.ContextPrompt}"
            : "";

        // Replace template tags with actual values
        return template
            .Replace("{OneLineStatement}", problem.OneLineStatement)
            .Replace("{DomainPrompt}", config.DomainPrompt ?? "General LLM capabilities")
            .Replace("{ContextPromptSection}", contextPromptSection);
    }

    /// <summary>
    /// Default prompt template for Phase 3 (flesh-out).
    /// Use tags like {OneLineStatement}, {DomainPrompt}, {ContextPromptSection}.
    /// </summary>
    public const string DefaultPhase3Prompt = """
        You are an expert at creating evaluation test cases for large language models.

        Problem statement: {OneLineStatement}
        Domain focus: {DomainPrompt}
        {ContextPromptSection}

        Task: Create a complete, self-contained prompt-response pair for this problem.

        Requirements:
        - The prompt should be detailed enough to be unambiguous
        - The expected output should be a high-quality response
        - Include any necessary context within the prompt
        - For coding problems, include clear requirements and example I/O if relevant
        - For reasoning problems, ensure the logic is sound

        First principles for reasoning:
        - The prompt should be maximally realistic, not maximally clean--like an average user, not an expert. Real inputs contain minor irrelevancies, slightly imprecise language, unstated assumptions. Scrubbing all of this makes the benchmark easier than reality and the expected response harder to transfer.
        - Write the expected response before finalizing the prompt. If the expected response is hard to write, the prompt is probably underspecified or the crux is unclear. The expected response is a forcing function on prompt quality.
        - The expected response must be *correct*, not merely *good-looking*. LLM outputs are fluent by default. The expected response should be graded on whether the crux is resolved correctly, not on whether it sounds authoritative or comprehensive.
        - Specify the failure mode the expected response must avoid. For every example, there is a characteristic wrong answer. The expected response isn't fully defined until you've also specified what it's contrasted against — this is what enables automated or model-based grading.
        - Calibrate length and format to the realistic task, not to thoroughness. If the real answer is two sentences, the expected response shouldn't be six paragraphs. Inflated expected responses bias evaluations toward verbosity and penalize correct concision.
        - Separate "must contain" from "should not contain." The grading criteria for an expected response should specify both required elements (the correct reasoning step, the right conclusion) and disqualifying elements (confident assertions of the wrong answer, hallucinated facts). Both are needed for reliable evaluation.
        - Make implicit knowledge explicit in the expected response, not in the prompt. If solving the problem requires background knowledge, the expected response should demonstrate that knowledge — don't smuggle hints into the problem statement to make it tractable.

        Respond with the prompt and response inside XML-like <Prompt> and <Response> tags respectively (no actual XML encoding), like this:
        <Prompt>
        The complete, fleshed-out prompt to give to an LLM
        </Prompt>
        <Response>
        The expected high-quality response
        </Response>
        """;

    private (string FullPrompt, string ExpectedOutput) ParseFleshedOutProblem(string response)
    {
        try
        {
            // Find <Prompt> and <Response> tags using IndexOf (more robust than regex)
            var promptOpen = "<Prompt>";
            var promptClose = "</Prompt>";
            var responseOpen = "<Response>";
            var responseClose = "</Response>";
            
            var promptStart = response.IndexOf(promptOpen, StringComparison.OrdinalIgnoreCase);
            var promptEnd = response.IndexOf(promptClose, StringComparison.OrdinalIgnoreCase);
            var responseStart = response.IndexOf(responseOpen, StringComparison.OrdinalIgnoreCase);
            var responseEnd = response.IndexOf(responseClose, StringComparison.OrdinalIgnoreCase);
            
            var prompt = "";
            var expectedOutput = "";
            
            if (promptStart >= 0 && promptEnd > promptStart)
            {
                prompt = response.Substring(promptStart + promptOpen.Length, promptEnd - promptStart - promptOpen.Length).Trim();
            }
            
            if (responseStart >= 0 && responseEnd > responseStart)
            {
                expectedOutput = response.Substring(responseStart + responseOpen.Length, responseEnd - responseStart - responseOpen.Length).Trim();
            }

            return (prompt, expectedOutput);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error parsing fleshed-out problem from LLM response");
            return ("", "");
        }
    }

    private async Task<string> CallJudgeLLMAsync(
        string userPrompt,
        string? systemPrompt,
        CancellationToken cancellationToken)
    {
        var messages = new List<ChatMessage>();

        if (!string.IsNullOrEmpty(systemPrompt))
        {
            messages.Add(new ChatMessage { Role = "system", Content = systemPrompt });
        }
        else
        {
            messages.Add(new ChatMessage
            {
                Role = "system",
                Content = "You are a helpful assistant that responds in structured XML format."
            });
        }

        messages.Add(new ChatMessage { Role = "user", Content = userPrompt });

        var request = new ChatCompletionRequest
        {
            Messages = messages,
            Temperature = _judgeTemperature,
            MaxTokens = _judgeMaxTokens
        };

        // Use managed judge client if available, otherwise create ad-hoc client
        var clientToUse = _managedJudgeClient;
        if (clientToUse == null)
        {
            // Create ad-hoc client using configured base URL
            var serverInfo = new ServerInfo { BaseUrl = _judgeBaseUrl, TotalSlots = _judgeParallelSlotCount };
            clientToUse = new LlamaServerClient(serverInfo, httpClient, loggerFactory.CreateLogger<LlamaServerClient>());
        }

        var result = await clientToUse.ChatCompletionAsync(request, cancellationToken);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Judge LLM call failed: {result.Errors.FirstOrDefault()?.Message ?? "Unknown error"}");
        }

        return result.Value.Choices.FirstOrDefault()?.Message?.Content ?? "";
    }

    private async Task SaveResultsAsync(
        EvalGenConfig config,
        List<GeneratedCategory> categories,
        List<GeneratedProblem> allProblems,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Saving results in SplitDirectories format");

        var promptsDir = Path.Combine(config.OutputDirectoryPath, "prompts");
        var expectedDir = Path.Combine(config.OutputDirectoryPath, "expected_outputs");

        foreach (var problem in allProblems.Where(p => p.IsComplete))
        {
            // Use category name as prefix for file organization
            var category = categories.FirstOrDefault(c => c.Id == problem.CategoryId);
            var categoryName = category?.Name?.Replace(" ", "_").Replace("/", "_") ?? "uncategorized";
            var safeName = $"{categoryName}_{problem.Id}";

            // Truncate if too long (Windows max path is 260 chars)
            if (safeName.Length > 100)
                safeName = safeName[..100];

            var promptPath = Path.Combine(promptsDir, $"{safeName}.txt");
            var expectedPath = Path.Combine(expectedDir, $"{safeName}.txt");

            await File.WriteAllTextAsync(promptPath, problem.FullPrompt!, cancellationToken);
            await File.WriteAllTextAsync(expectedPath, problem.ExpectedOutput!, cancellationToken);
        }

        // Also save a manifest file with metadata
        var manifest = new
        {
            config.Id,
            config.RunName,
            GeneratedAt = DateTimeOffset.Now.ToString("O"),
            Categories = categories.Select(c => new { c.Id, c.Name }),
            ProblemCount = allProblems.Count,
            CompleteProblemCount = allProblems.Count(p => p.IsComplete)
        };

        var manifestPath = Path.Combine(config.OutputDirectoryPath, "manifest.json");
        var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(manifestPath, manifestJson, cancellationToken);

        logger.LogInformation("Saved {CompleteCount}/{TotalCount} complete problems to {OutputDir}",
            allProblems.Count(p => p.IsComplete), allProblems.Count, config.OutputDirectoryPath);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _currentRun?.CancellationToken.ThrowIfCancellationRequested();
        }
    }

    [GeneratedRegex(@"^[\d\.\-\*]+\s*")]
    private static partial Regex ListItemPrefixRegex();
}
