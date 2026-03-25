using Seevalocal.Core.Models;
using Seevalocal.UI.Commands;

namespace Seevalocal.UI;

/// <summary>
/// Converts parsed CLI flags into a <see cref="PartialConfig"/> that can be layered
/// over file-based configs using ConfigurationMerger.
/// </summary>
public static class CliSettingsAdapter
{
    public static PartialConfig ToPartialConfig(RunCommandSettings s)
    {
        // Determine Manage tri-state
        var manage = s.NoManage ? false : s.Manage;
        // Imply manage=true when executable or model are specified
        if (s.ExecutablePath != null || s.ModelFilePath != null || s.HfRepo != null)
            manage ??= true;
        // Imply manage=false when server-url is specified
        if (s.ServerUrl != null)
            manage ??= false;

        ModelSource? model = null;
        if (s.ModelFilePath != null)
        {
            model = new ModelSource
            {
                Kind = ModelSourceKind.LocalFile,
                FilePath = s.ModelFilePath
            };
        }
        else if (s.HfRepo != null)
        {
            var parts = s.HfRepo.Split(':', 2);
            model = new ModelSource
            {
                Kind = ModelSourceKind.HuggingFace,
                HfRepo = parts[0],
                HfQuant = parts.Length > 1 ? parts[1] : null,
                HfToken = s.HfToken
            };
        }

        var serverConfig = manage != null || model != null
                            || s.ServerUrl != null || s.ApiKey != null
            ? new PartialServerConfig
            {
                Manage = manage,
                ExecutablePath = s.ExecutablePath,
                Model = model,
                ApiKey = s.ApiKey,
                BaseUrl = s.ServerUrl,
            }
            : null;

        var llamaSettings = HasAnyLlamaSettings(s)
            ? new PartialLlamaServerSettings
            {
                ContextWindowTokens = s.ContextWindowTokens,
                BatchSizeTokens = s.BatchSizeTokens,
                UbatchSizeTokens = s.UbatchSizeTokens,
                ParallelSlotCount = s.ParallelSlotCount,
                GpuLayerCount = s.GpuLayerCount,
                EnableFlashAttention = s.EnableFlashAttention,
                EnableCachePrompt = s.EnableCachePrompt,
                EnableContextShift = s.EnableContextShift,
                KvCacheTypeK = s.KvCacheTypeK,
                KvCacheTypeV = s.KvCacheTypeV,
                ThreadCount = s.ThreadCount,
                SamplingTemperature = s.SamplingTemperature,
                TopP = s.TopP,
                TopK = s.TopK,
                MinP = s.MinP,
                Seed = s.Seed,
                ChatTemplate = s.ChatTemplate,
                ReasoningFormat = s.ReasoningFormat,
                LogVerbosity = s.LogVerbosity,
                ExtraArgs = s.ExtraArgs != null && s.ExtraArgs.Length > 0 ? [.. s.ExtraArgs] : null
            }
            : null;

        // Judge config uses ServerConfig for connection and properties for scoring
        var judgeConfig = s.JudgeUrl != null || s.JudgeModelFilePath != null || s.JudgeHfRepo != null
            ? new PartialJudgeConfig
            {
                ServerConfig = new PartialServerConfig
                {
                    Manage = false,
                    BaseUrl = s.JudgeUrl,
                    ApiKey = s.JudgeApiKey,
                },
                JudgePromptTemplate = s.JudgeTemplate ?? "standard",
                ServerSettings = s.JudgeModelFilePath != null || s.JudgeHfRepo != null
                    ? new PartialLlamaServerSettings
                    {
                        ModelAlias = s.JudgeModelFilePath ?? s.JudgeHfRepo
                    }
                    : null
            }
            : null;

        var runMeta = s.PipelineName != null || s.MaxConcurrent != null || s.StopOnFailure || s.ContinueOnFailure
            ? new PartialRunMeta
            {
                PipelineName = s.PipelineName ?? "CasualQA",
                MaxConcurrentEvals = s.MaxConcurrent,
                ContinueOnEvalFailure = !s.StopOnFailure,
            }
            : null;

        var dataSource = BuildDataSourceConfig(s);

        // Build OutputConfig if any output options are present
        var outputConfig = HasAnyOutputSettings(s)
            ? new OutputConfig
            {
                OutputDir = s.OutputDir,
                ShellTarget = ParseShellTarget(s.ShellDialect),
                WriteResultsParquet = !s.NoParquet,
                IncludeRawLlmResponse = !s.NoRawResponse,
            }
            : null;

        return new PartialConfig
        {
            Server = serverConfig,
            LlamaSettings = llamaSettings,
            Run = runMeta,
            Judge = judgeConfig,  // Also keep at top level for backward compatibility
            Output = outputConfig,
            DataSource = dataSource
        };
    }

    private static bool HasAnyOutputSettings(RunCommandSettings s) =>
        s.OutputDir != null || s.ShellDialect != null || s.NoParquet || s.NoRawResponse;

    private static bool HasAnyLlamaSettings(RunCommandSettings s) =>
        s.ContextWindowTokens != null || s.BatchSizeTokens != null || s.UbatchSizeTokens != null
        || s.ParallelSlotCount != null || s.GpuLayerCount != null || s.EnableFlashAttention != null
        || s.EnableCachePrompt != null || s.EnableContextShift != null || s.KvCacheTypeK != null
        || s.KvCacheTypeV != null || s.ThreadCount != null || s.SamplingTemperature != null
        || s.TopP != null || s.TopK != null || s.MinP != null || s.Seed != null
        || s.ChatTemplate != null || s.ReasoningFormat != null || s.LogVerbosity != null || s.ExtraArgs != null;

    private static PartialDataSourceConfig? BuildDataSourceConfig(RunCommandSettings s)
    {
        return s.DataFilePath != null
            ? new PartialDataSourceConfig { Kind = DataSourceKind.SingleFile, FilePath = s.DataFilePath }
            : s.PromptDir != null
            ? new PartialDataSourceConfig
            {
                Kind = DataSourceKind.SplitDirectories,
                PromptDirectory = s.PromptDir,
                ExpectedDirectory = s.ExpectedDir
            }
            : null;
    }

    private static ShellTarget? ParseShellTarget(string? dialect) => dialect?.ToLowerInvariant() switch
    {
        "bash" => ShellTarget.Bash,
        "powershell" or "ps" or "ps1" => ShellTarget.PowerShell,
        _ => null
    };
}
