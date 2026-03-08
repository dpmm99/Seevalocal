using Microsoft.Extensions.Logging;
using Seevalocal.Core.Models;

namespace Seevalocal.Config.Validation;

/// <summary>
/// Validates a <see cref="ResolvedConfig"/> and returns a list of human-readable
/// <see cref="ValidationError"/> records. An empty list means the config is valid.
/// </summary>
public sealed class ConfigValidator(
    ILogger<ConfigValidator> logger,
    IReadOnlySet<string>? registeredPipelineNames = null)
{
    private readonly ILogger<ConfigValidator> _logger = logger;

    /// <summary>Optional set of registered pipeline names used for pipeline-name validation.</summary>
    private readonly IReadOnlySet<string>? _registeredPipelineNames = registeredPipelineNames;

    public IReadOnlyList<ValidationError> Validate(ResolvedConfig config)
    {
        List<ValidationError> errors = [];

        ValidateServer(config.Server, errors);
        ValidateLlamaServer(config.LlamaServer, errors);
        ValidateRunMeta(config.Run, errors);
        ValidateEvalSets(config.EvalSets, errors);
        ValidateJudge(config.Judge, errors);

        if (errors.Count > 0)
            _logger.LogWarning("Config validation produced {ErrorCount} error(s)", errors.Count);

        return errors;
    }

    // -------------------------------------------------------------------------

    private static void ValidateServer(ServerConfig server, List<ValidationError> errors)
    {
        if (server.Manage)
        {
            if (server.Model is null)
                errors.Add(new ValidationError(
                    "server.model",
                    "[ConfigValidator] server.manage is true but server.model is not set"));
        }
        else
        {
            if (string.IsNullOrWhiteSpace(server.BaseUrl))
            {
                errors.Add(new ValidationError(
                    "server.baseUrl",
                    "[ConfigValidator] server.manage is false but server.baseUrl is not set"));
            }
            else if (!Uri.TryCreate(server.BaseUrl, UriKind.Absolute, out _))
            {
                errors.Add(new ValidationError(
                    "server.baseUrl",
                    $"[ConfigValidator] server.baseUrl '{server.BaseUrl}' is not a valid absolute URI"));
            }
        }
    }

    private static void ValidateLlamaServer(LlamaServerSettings ls, List<ValidationError> errors)
    {
        if (ls.ContextWindowTokens <= 0)
            errors.Add(new ValidationError(
                "llamaServer.contextWindowTokens",
                $"[ConfigValidator] llamaServer.contextWindowTokens must be > 0, got {ls.ContextWindowTokens.Value}"));

        if (ls.SamplingTemperature.HasValue)
        {
            var t = ls.SamplingTemperature.Value;
            if (t is < 0.0 or > 2.0)
                errors.Add(new ValidationError(
                    "llamaServer.samplingTemperature",
                    $"[ConfigValidator] llamaServer.samplingTemperature must be in [0, 2], got {t}"));
        }

        if (ls.TopP.HasValue && (ls.TopP.Value < 0.0 || ls.TopP.Value > 1.0))
            errors.Add(new ValidationError(
                "llamaServer.topP",
                $"[ConfigValidator] llamaServer.topP must be in [0, 1], got {ls.TopP.Value}"));

        if (ls.BatchSizeTokens <= 0)
            errors.Add(new ValidationError(
                "llamaServer.batchSizeTokens",
                $"[ConfigValidator] llamaServer.batchSizeTokens must be > 0, got {ls.BatchSizeTokens.Value}"));

        if (ls.ParallelSlotCount <= 0)
            errors.Add(new ValidationError(
                "llamaServer.parallelSlotCount",
                $"[ConfigValidator] llamaServer.parallelSlotCount must be > 0, got {ls.ParallelSlotCount.Value}"));
    }

    private static void ValidateRunMeta(RunMeta run, List<ValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(run.OutputDirectoryPath))
            errors.Add(new ValidationError(
                "run.outputDirectoryPath",
                "[ConfigValidator] run.outputDirectoryPath must not be empty"));
        else
        {
            try
            {
                var fullPath = Path.GetFullPath(run.OutputDirectoryPath);
                // Try to create the directory tree; if it fails the path is not writable.
                _ = Directory.CreateDirectory(fullPath);
            }
            catch (Exception ex)
            {
                errors.Add(new ValidationError(
                    "run.outputDirectoryPath",
                    $"[ConfigValidator] run.outputDirectoryPath '{run.OutputDirectoryPath}' is not writable: {ex.Message}"));
            }
        }

        if (run.MaxConcurrentEvals <= 0)
            errors.Add(new ValidationError(
                "run.maxConcurrentEvals",
                $"[ConfigValidator] run.maxConcurrentEvals must be > 0, got {run.MaxConcurrentEvals.Value}"));
    }

    private void ValidateEvalSets(IReadOnlyList<EvalSetConfig> evalSets, List<ValidationError> errors)
    {
        if (evalSets.Count == 0)
        {
            errors.Add(new ValidationError(
                "evalSets",
                "[ConfigValidator] At least one evalSet must be defined"));
            return;
        }

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < evalSets.Count; i++)
        {
            var es = evalSets[i];
            var prefix = $"evalSets[{i}]";

            if (string.IsNullOrWhiteSpace(es.Id))
            {
                errors.Add(new ValidationError($"{prefix}.id",
                    $"[ConfigValidator] {prefix}.id must not be empty"));
            }
            else if (!seenIds.Add(es.Id))
            {
                errors.Add(new ValidationError($"{prefix}.id",
                    $"[ConfigValidator] Field '{prefix}.id' is not unique: '{es.Id}' appears more than once"));
            }

            if (string.IsNullOrWhiteSpace(es.PipelineName))
            {
                errors.Add(new ValidationError($"{prefix}.pipelineName",
                    $"[ConfigValidator] {prefix}.pipelineName must not be empty"));
            }
            else if (_registeredPipelineNames?.Contains(es.PipelineName) == false)
            {
                errors.Add(new ValidationError($"{prefix}.pipelineName",
                    $"[ConfigValidator] {prefix}.pipelineName '{es.PipelineName}' is not a registered pipeline"));
            }

            ValidateDataSourceConfig(es.DataSource, $"{prefix}.dataSource", errors);
        }
    }

    private static void ValidateDataSourceConfig(DataSourceConfig ds, string prefix, List<ValidationError> errors)
    {
        switch (ds.Kind)
        {
            case DataSourceKind.Directory:
                if (string.IsNullOrWhiteSpace(ds.PromptDirectoryPath))
                    errors.Add(new ValidationError($"{prefix}.promptDirectoryPath",
                        $"[ConfigValidator] {prefix}.promptDirectoryPath must be set when kind is Directory"));
                break;

            case DataSourceKind.SingleFile:
            case DataSourceKind.JsonFile:
            case DataSourceKind.YamlFile:
            case DataSourceKind.CsvFile:
            case DataSourceKind.ParquetFile:
                if (string.IsNullOrWhiteSpace(ds.FilePath))
                    errors.Add(new ValidationError($"{prefix}.filePath",
                        $"[ConfigValidator] {prefix}.filePath must be set when kind is {ds.Kind}"));
                break;
        }
    }

    private static void ValidateJudge(JudgeConfig? judge, List<ValidationError> errors)
    {
        if (judge is null) return;

        // If managing a local judge server
        if (judge.Manage)
        {
            // Validate judge server configuration
            var serverConfig = judge.ServerConfig;
            if (serverConfig?.Manage != true)
            {
                errors.Add(new ValidationError(
                    "judge.serverConfig",
                    "[ConfigValidator] judge.serverConfig.manage must be true when judge.manage is true"));
            }

            // Judge model is required when managing
            if (serverConfig?.Model is null)
            {
                errors.Add(new ValidationError(
                    "judge.serverConfig.model",
                    "[ConfigValidator] judge.serverConfig.model must be set when managing judge server"));
            }

            // Validate judge server settings if provided
            if (judge.ServerSettings is not null)
            {
                ValidateLlamaServerSettings(judge.ServerSettings, "judge.serverSettings", errors);
            }
        }
        else
        {
            // When not managing, BaseUrl is required
            if (string.IsNullOrWhiteSpace(judge.BaseUrl))
                errors.Add(new ValidationError(
                    "judge.baseUrl",
                    "[ConfigValidator] judge.baseUrl must be set when judge.manage is false"));
            else if (!Uri.TryCreate(judge.BaseUrl, UriKind.Absolute, out _))
                errors.Add(new ValidationError(
                    "judge.baseUrl",
                    $"[ConfigValidator] judge.baseUrl '{judge.BaseUrl}' is not a valid absolute URI"));
        }

        // Validate sampling temperature (applies to both managed and external)
        double? samplingTemp = judge.SamplingTemperature ?? judge.JudgeSamplingTemperature;
        if (samplingTemp.HasValue)
        {
            var t = samplingTemp.Value;
            if (t is < 0.0 or > 2.0)
                errors.Add(new ValidationError(
                    "judge.samplingTemperature",
                    $"[ConfigValidator] judge.samplingTemperature must be in [0, 2], got {t}"));
        }
    }

    private static void ValidateLlamaServerSettings(LlamaServerSettings settings, string prefix, List<ValidationError> errors)
    {
        if (settings.ContextWindowTokens.HasValue && settings.ContextWindowTokens <= 0)
            errors.Add(new ValidationError($"{prefix}.contextWindowTokens",
                $"[ConfigValidator] {prefix}.contextWindowTokens must be > 0"));

        if (settings.BatchSizeTokens.HasValue && settings.BatchSizeTokens <= 0)
            errors.Add(new ValidationError($"{prefix}.batchSizeTokens",
                $"[ConfigValidator] {prefix}.batchSizeTokens must be > 0"));

        if (settings.ParallelSlotCount.HasValue && settings.ParallelSlotCount <= 0)
            errors.Add(new ValidationError($"{prefix}.parallelSlotCount",
                $"[ConfigValidator] {prefix}.parallelSlotCount must be > 0"));

        if (settings.GpuLayerCount.HasValue && settings.GpuLayerCount < 0)
            errors.Add(new ValidationError($"{prefix}.gpuLayerCount",
                $"[ConfigValidator] {prefix}.gpuLayerCount must be >= 0"));

        if (settings.SamplingTemperature.HasValue)
        {
            var t = settings.SamplingTemperature.Value;
            if (t is < 0.0 or > 2.0)
                errors.Add(new ValidationError($"{prefix}.samplingTemperature",
                    $"[ConfigValidator] {prefix}.samplingTemperature must be in [0, 2], got {t}"));
        }

        if (settings.TopP.HasValue && (settings.TopP < 0.0 || settings.TopP > 1.0))
            errors.Add(new ValidationError($"{prefix}.topP",
                $"[ConfigValidator] {prefix}.topP must be in [0, 1]"));

        if (settings.TopK.HasValue && settings.TopK < 0)
            errors.Add(new ValidationError($"{prefix}.topK",
                $"[ConfigValidator] {prefix}.topK must be >= 0"));

        if (settings.MinP.HasValue && (settings.MinP < 0.0 || settings.MinP > 1.0))
            errors.Add(new ValidationError($"{prefix}.minP",
                $"[ConfigValidator] {prefix}.minP must be in [0, 1]"));

        if (settings.Seed.HasValue && settings.Seed == 0)
            errors.Add(new ValidationError($"{prefix}.seed",
                $"[ConfigValidator] {prefix}.seed should be -1 for random or > 0"));
    }
}
