using Seevalocal.Core;
using Seevalocal.Core.Models;

namespace Seevalocal.UI.Commands;

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

        var serverConfig = (manage != null || s.Host != null || s.Port != null || model != null
                            || s.ServerUrl != null || s.ApiKey != null)
            ? new PartialServerConfig
            {
                Manage = manage ?? false,
                ExecutablePath = s.ExecutablePath,
                Model = model,
                Host = s.Host ?? "127.0.0.1",
                Port = s.Port ?? 8080,
                ApiKey = s.ApiKey,
                BaseUrl = s.ServerUrl,
                ExtraArgs = s.ExtraArgs?.ToList()
            }
            : null;

        var llamaSettings = HasAnyLlamaSettings(s)
            ? new PartialLlamaServerSettings
            {
                ContextWindowTokens = s.ContextWindowTokens,
                BatchSizeTokens = s.BatchTokens,
                UbatchSizeTokens = s.UBatchTokens,
                ParallelSlotCount = s.ParallelSlotCount,
                GpuLayerCount = s.GpuLayerCount,
                EnableFlashAttention = s.EnableFlashAttention,
                EnableCachePrompt = s.EnableCachePrompt,
                EnableContextShift = s.EnableContextShift,
                KvCacheTypeK = s.KvTypeK,
                KvCacheTypeV = s.KvTypeV,
                ThreadCount = s.ThreadCount,
                SamplingTemperature = s.SamplingTemperature,
                TopP = s.TopP,
                TopK = s.TopK,
                MinP = s.MinP,
                Seed = s.Seed,
                ChatTemplate = s.ChatTemplate,
                ReasoningFormat = s.ReasoningFormat,
                LogVerbosity = s.LogVerbosity
            }
            : null;

        // Judge config uses ServerConfig for connection and properties for scoring
        var judgeConfig = (s.JudgeUrl != null || s.JudgeModelFilePath != null || s.JudgeHfRepo != null)
            ? new PartialJudgeConfig
            {
                Manage = false,
                ServerConfig = new PartialServerConfig
                {
                    Manage = false,
                    BaseUrl = s.JudgeUrl,
                    ApiKey = s.JudgeApiKey
                },
                BaseUrl = s.JudgeUrl,
                JudgePromptTemplate = s.JudgeTemplate ?? DefaultTemplates.Standard,
                ScoreMinValue = s.JudgeScoreMin ?? 0,
                ScoreMaxValue = s.JudgeScoreMax ?? 10,
                ServerSettings = s.JudgeModelFilePath != null || s.JudgeHfRepo != null
                    ? new PartialLlamaServerSettings
                    {
                        ModelAlias = s.JudgeModelFilePath ?? s.JudgeHfRepo
                    }
                    : null
            }
            : null;

        var runMeta = (s.MaxConcurrent != null || s.StopOnFailure || s.ContinueOnFailure || s.TimeoutSeconds != null || s.RetryCount != null)
            ? new PartialRunMeta
            {
                MaxConcurrentEvals = s.MaxConcurrent,
                ContinueOnEvalFailure = !s.StopOnFailure,
                TimeoutSeconds = s.TimeoutSeconds,
                RetryCount = s.RetryCount,
            }
            : null;

        // Build a single EvalSetConfig if any eval options are present
        var evalSet = HasAnyEvalSettings(s)
            ? new EvalSetConfig
            {
                PipelineName = s.PipelineName ?? "CasualQA",
                DataSource = BuildDataSourceConfig(s),
            }
            : null;

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
            LlamaServer = llamaSettings,
            EvalSets = evalSet != null ? [evalSet] : null,
            Run = runMeta,
            Judge = judgeConfig,  // Also keep at top level for backward compatibility
            Output = outputConfig,
        };
    }

    private static bool HasAnyOutputSettings(RunCommandSettings s) =>
        s.OutputDir != null || s.ShellDialect != null || s.NoParquet || s.NoRawResponse;

    private static bool HasAnyLlamaSettings(RunCommandSettings s) =>
        s.ContextWindowTokens != null || s.BatchTokens != null || s.UBatchTokens != null
        || s.ParallelSlotCount != null || s.GpuLayerCount != null || s.EnableFlashAttention != null
        || s.EnableCachePrompt != null || s.EnableContextShift != null || s.KvTypeK != null
        || s.KvTypeV != null || s.ThreadCount != null || s.SamplingTemperature != null
        || s.TopP != null || s.TopK != null || s.MinP != null || s.Seed != null
        || s.ChatTemplate != null || s.ReasoningFormat != null || s.LogVerbosity != null;

    private static bool HasAnyEvalSettings(RunCommandSettings s) =>
        s.PipelineName != null || s.PromptDir != null || s.ExpectedDir != null
        || s.DataFilePath != null;

    private static DataSourceConfig BuildDataSourceConfig(RunCommandSettings s)
    {
        return s.DataFilePath != null
            ? new DataSourceConfig { Kind = DataSourceKind.File, FilePath = s.DataFilePath }
            : s.PromptDir != null
            ? new DataSourceConfig
            {
                Kind = DataSourceKind.DirectoryPair,
                PromptDirectoryPath = s.PromptDir,
                ExpectedOutputDirectoryPath = s.ExpectedDir
            }
            : new DataSourceConfig { Kind = DataSourceKind.SingleFile };
    }

    private static ShellTarget? ParseShellTarget(string? dialect) => dialect?.ToLowerInvariant() switch
    {
        "bash" => ShellTarget.Bash,
        "powershell" or "ps" or "ps1" => ShellTarget.PowerShell,
        _ => null
    };
}
