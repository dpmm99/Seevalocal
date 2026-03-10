using Seevalocal.Core;
using Seevalocal.Core.Models;

namespace Seevalocal.Config.Merging;

/// <summary>
/// <para>
/// Merges an ordered list of <see cref="PartialConfig"/> settings files plus CLI
/// overrides into a single <see cref="ResolvedConfig"/>.
/// </para>
/// <para>
/// Merge rule (right-fold null-coalescing, per 03-config.md §4.1):
///   For each leaf field:
///     1. If CLI overrides has a non-null value → use it.
///     2. Else walk settings files from last to first; use first non-null.
///     3. If still null → field is unset; use defaults.
/// </para>
/// </summary>
public sealed class ConfigurationMerger
{
    /// <summary>
    /// Merges settings files in order; later files override earlier ones.
    /// CLI overrides are applied last (highest priority).
    /// </summary>
    public ResolvedConfig Merge(
        IReadOnlyList<PartialConfig> settingsFiles,
        PartialConfig? cliOverrides = null)
    {
        // Build a combined list ordered lowest → highest priority
        // (index 0 = first file, last = CLI)
        var all = new List<PartialConfig>(settingsFiles);
        if (cliOverrides is not null)
            all.Add(cliOverrides);

        return new ResolvedConfig
        {
            Run = MergeRunMeta(all),
            Server = MergeServerConfig(all),
            LlamaServer = MergeLlamaServerSettings(all),
            EvalSets = MergeEvalSets(all),
            Judge = MergeJudgeConfig(all),
            DataSource = MergeDataSource(all),
        };
    }

    // -------------------------------------------------------------------------
    // Helpers — pick last non-null value from the list for a given selector
    // -------------------------------------------------------------------------

    private static RunMeta MergeRunMeta(IReadOnlyList<PartialConfig> all)
    {
        return new RunMeta
        {
            RunName = LastRun(all, static r => r.RunName),
            OutputDirectoryPath = LastRun(all, static r => r.OutputDirectoryPath),
            ExportShellTarget = LastRunValue(all, static r => r.ExportShellTarget),
            ContinueOnEvalFailure = LastRunValue(all, static r => r.ContinueOnEvalFailure),
            MaxConcurrentEvals = LastRunValue(all, static r => r.MaxConcurrentEvals),
        };

        static string? LastRun(IReadOnlyList<PartialConfig> all, Func<PartialRunMeta, string?> sel)
        {
            for (var i = all.Count - 1; i >= 0; i--)
            {
                var run = all[i].Run;
                if (run is null) continue;
                var v = sel(run);
                if (v is not null) return v;
            }
            return null;
        }

        static T? LastRunValue<T>(IReadOnlyList<PartialConfig> all, Func<PartialRunMeta, T?> sel)
            where T : struct
        {
            for (var i = all.Count - 1; i >= 0; i--)
            {
                var run = all[i].Run;
                if (run is null) continue;
                var v = sel(run);
                if (v.HasValue) return v;
            }
            return null;
        }
    }

    private static ServerConfig MergeServerConfig(IReadOnlyList<PartialConfig> all)
    {
        return new ServerConfig
        {
            Manage = LastSrv(all, static s => s.Manage),
            ExecutablePath = LastSrvStr(all, static s => s.ExecutablePath),
            Model = LastSrvRef(all, static s => s.Model),
            Host = LastSrvStr(all, static s => s.Host),
            Port = LastSrv(all, static s => s.Port),
            ApiKey = LastSrvStr(all, static s => s.ApiKey),
            ExtraArgs = LastSrvRef(all, static s => s.ExtraArgs) ?? [],
            BaseUrl = LastSrvStr(all, static s => s.BaseUrl),
        };

        static T? LastSrv<T>(IReadOnlyList<PartialConfig> all, Func<PartialServerConfig, T?> sel)
            where T : struct
        {
            for (var i = all.Count - 1; i >= 0; i--)
            {
                var s = all[i].Server; if (s is null) continue;
                var v = sel(s); if (v.HasValue) return v;
            }
            return null;
        }
        static string? LastSrvStr(IReadOnlyList<PartialConfig> all, Func<PartialServerConfig, string?> sel)
        {
            for (var i = all.Count - 1; i >= 0; i--)
            {
                var s = all[i].Server; if (s is null) continue;
                var v = sel(s); if (v is not null) return v;
            }
            return null;
        }
        static T? LastSrvRef<T>(IReadOnlyList<PartialConfig> all, Func<PartialServerConfig, T?> sel)
            where T : class
        {
            for (var i = all.Count - 1; i >= 0; i--)
            {
                var s = all[i].Server; if (s is null) continue;
                var v = sel(s); if (v is not null) return v;
            }
            return null;
        }
    }

    private static LlamaServerSettings MergeLlamaServerSettings(IReadOnlyList<PartialConfig> all)
    {
        return new LlamaServerSettings
        {
            ContextWindowTokens = L(all, static s => s.ContextWindowTokens),
            BatchSizeTokens = L(all, static s => s.BatchSizeTokens),
            UbatchSizeTokens = L(all, static s => s.UbatchSizeTokens),
            ParallelSlotCount = L(all, static s => s.ParallelSlotCount),
            EnableContinuousBatching = Lb(all, static s => s.EnableContinuousBatching),
            EnableCachePrompt = Lb(all, static s => s.EnableCachePrompt),
            EnableContextShift = Lb(all, static s => s.EnableContextShift),
            GpuLayerCount = L(all, static s => s.GpuLayerCount),
            SplitMode = Ls(all, static s => s.SplitMode),
            KvCacheTypeK = Ls(all, static s => s.KvCacheTypeK),
            KvCacheTypeV = Ls(all, static s => s.KvCacheTypeV),
            EnableKvOffload = Lb(all, static s => s.EnableKvOffload),
            EnableFlashAttention = Lb(all, static s => s.EnableFlashAttention),
            SamplingTemperature = Ld(all, static s => s.SamplingTemperature),
            TopP = Ld(all, static s => s.TopP),
            TopK = L(all, static s => s.TopK),
            MinP = Ld(all, static s => s.MinP),
            RepeatPenalty = Ld(all, static s => s.RepeatPenalty),
            RepeatLastNTokens = L(all, static s => s.RepeatLastNTokens),
            PresencePenalty = Ld(all, static s => s.PresencePenalty),
            FrequencyPenalty = Ld(all, static s => s.FrequencyPenalty),
            Seed = L(all, static s => s.Seed),
            ThreadCount = L(all, static s => s.ThreadCount),
            HttpThreadCount = L(all, static s => s.HttpThreadCount),
            ChatTemplate = Ls(all, static s => s.ChatTemplate),
            EnableJinja = Lb(all, static s => s.EnableJinja),
            ReasoningFormat = Ls(all, static s => s.ReasoningFormat),
            ModelAlias = Ls(all, static s => s.ModelAlias),
            LogVerbosity = L(all, static s => s.LogVerbosity),
            EnableMlock = Lb(all, static s => s.EnableMlock),
            EnableMmap = Lb(all, static s => s.EnableMmap),
            ServerTimeoutSeconds = Ld(all, static s => s.ServerTimeoutSeconds),
            ExtraArgs = LlamaExtraArgs(all),
        };

        static int? L(IReadOnlyList<PartialConfig> all, Func<PartialLlamaServerSettings, int?> sel)
        {
            for (var i = all.Count - 1; i >= 0; i--)
            {
                var s = all[i].LlamaServer; if (s is null) continue;
                var v = sel(s); if (v.HasValue) return v;
            }
            return null;
        }
        static bool? Lb(IReadOnlyList<PartialConfig> all, Func<PartialLlamaServerSettings, bool?> sel)
        {
            for (var i = all.Count - 1; i >= 0; i--)
            {
                var s = all[i].LlamaServer; if (s is null) continue;
                var v = sel(s); if (v.HasValue) return v;
            }
            return null;
        }
        static double? Ld(IReadOnlyList<PartialConfig> all, Func<PartialLlamaServerSettings, double?> sel)
        {
            for (var i = all.Count - 1; i >= 0; i--)
            {
                var s = all[i].LlamaServer; if (s is null) continue;
                var v = sel(s); if (v.HasValue) return v;
            }
            return null;
        }
        static string? Ls(IReadOnlyList<PartialConfig> all, Func<PartialLlamaServerSettings, string?> sel)
        {
            for (var i = all.Count - 1; i >= 0; i--)
            {
                var s = all[i].LlamaServer; if (s is null) continue;
                var v = sel(s); if (v is not null) return v;
            }
            return null;
        }
        static IReadOnlyList<string> LlamaExtraArgs(IReadOnlyList<PartialConfig> all)
        {
            for (var i = all.Count - 1; i >= 0; i--)
            {
                var s = all[i].LlamaServer;
                if (s?.ExtraArgs is { Count: > 0 } args) return args;
            }
            return [];
        }
    }

    private static IReadOnlyList<EvalSetConfig> MergeEvalSets(List<PartialConfig> all)
    {
        // EvalSets are taken from the highest-priority file that defines them.
        for (var i = all.Count - 1; i >= 0; i--)
        {
            IReadOnlyList<EvalSetConfig>? sets = all[i].EvalSets;
            if (sets is { Count: > 0 })
                return sets;
        }
        return [];
    }

    private static JudgeConfig? MergeJudgeConfig(IReadOnlyList<PartialConfig> all)
    {
        // Check if any judge config exists
        var hasJudge = all.Any(p => p.Judge is not null);
        if (!hasJudge) return null;

        // Merge judge server config
        var serverConfig = MergeJudgeServerConfig(all);

        // Merge judge server settings
        var serverSettings = MergeJudgeServerSettings(all);

        // Get other judge properties from last to first
        return new JudgeConfig
        {
            Enable = LastJudgeBool(all, j => j.Enable) ?? false,
            Manage = LastJudgeBool(all, j => j.ServerConfig?.Manage) ?? false,
            ServerConfig = serverConfig,
            ServerSettings = serverSettings,
            JudgePromptTemplate = LastJudgeStr(all, j => j.JudgePromptTemplate) ?? "standard",
            ResponseFormat = LastJudgeEnum(all, j => j.ResponseFormat) ?? JudgeResponseFormat.StructuredJson,
            ScoreMinValue = LastJudgeDouble(all, j => j.ScoreMinValue) ?? 0.0,
            ScoreMaxValue = LastJudgeDouble(all, j => j.ScoreMaxValue) ?? 10.0,
            JudgeSystemPrompt = LastJudgeStr(all, j => j.JudgeSystemPrompt),
            JudgeMaxTokenCount = LastJudgeInt(all, j => j.JudgeMaxTokenCount) ?? 512,
            JudgeSamplingTemperature = LastJudgeDouble(all, j => j.JudgeSamplingTemperature) ?? 0.0,
            BaseUrl = LastJudgeStr(all, j => j.BaseUrl),
            SamplingTemperature = LastJudgeDouble(all, j => j.SamplingTemperature)
        };
    }

    private static DataSourceConfig MergeDataSource(IReadOnlyList<PartialConfig> all)
    {
        return new DataSourceConfig
        {
            Kind = LastDsEnum(all, d => d.Kind) ?? DataSourceKind.SingleFile,
            FilePath = LastDsStr(all, d => d.FilePath),
            PromptDirectoryPath = LastDsStr(all, d => d.PromptDirectoryPath),
            ExpectedOutputDirectoryPath = LastDsStr(all, d => d.ExpectedOutputDirectoryPath),
        };

        static T? LastDsEnum<T>(IReadOnlyList<PartialConfig> all, Func<PartialDataSourceConfig, T?> sel)
            where T : struct
        {
            for (var i = all.Count - 1; i >= 0; i--)
            {
                var ds = all[i].DataSource;
                if (ds is null) continue;
                var v = sel(ds);
                if (v.HasValue) return v;
            }
            return null;
        }

        static string? LastDsStr(IReadOnlyList<PartialConfig> all, Func<PartialDataSourceConfig, string?> sel)
        {
            for (var i = all.Count - 1; i >= 0; i--)
            {
                var ds = all[i].DataSource;
                if (ds is null) continue;
                var v = sel(ds);
                if (v is not null) return v;
            }
            return null;
        }
    }

    private static ServerConfig MergeJudgeServerConfig(IReadOnlyList<PartialConfig> all)
    {
        return new ServerConfig
        {
            Manage = LastJudgeBool(all, j => j.ServerConfig?.Manage) ?? false,
            ExecutablePath = LastJudgeStr(all, j => j.ServerConfig?.ExecutablePath),
            Model = LastJudgeModelSource(all, j => j.ServerConfig?.Model),
            Host = LastJudgeStr(all, j => j.ServerConfig?.Host),
            Port = LastJudgeInt(all, j => j.ServerConfig?.Port),
            ApiKey = LastJudgeStr(all, j => j.ServerConfig?.ApiKey),
            ExtraArgs = LastJudgeList(all, j => j.ServerConfig?.ExtraArgs) ?? [],
            BaseUrl = LastJudgeStr(all, j => j.ServerConfig?.BaseUrl)
        };
    }

    private static LlamaServerSettings? MergeJudgeServerSettings(IReadOnlyList<PartialConfig> all)
    {
        // Check if any judge server settings exist
        var hasSettings = all.Any(p => p.Judge?.ServerSettings is not null);
        if (!hasSettings) return null;

        return new LlamaServerSettings
        {
            ContextWindowTokens = LastJudgeInt(all, j => j.ServerSettings?.ContextWindowTokens),
            BatchSizeTokens = LastJudgeInt(all, j => j.ServerSettings?.BatchSizeTokens),
            UbatchSizeTokens = LastJudgeInt(all, j => j.ServerSettings?.UbatchSizeTokens),
            ParallelSlotCount = LastJudgeInt(all, j => j.ServerSettings?.ParallelSlotCount),
            EnableContinuousBatching = LastJudgeBool(all, j => j.ServerSettings?.EnableContinuousBatching),
            EnableCachePrompt = LastJudgeBool(all, j => j.ServerSettings?.EnableCachePrompt),
            EnableContextShift = LastJudgeBool(all, j => j.ServerSettings?.EnableContextShift),
            GpuLayerCount = LastJudgeInt(all, j => j.ServerSettings?.GpuLayerCount),
            SplitMode = LastJudgeStr(all, j => j.ServerSettings?.SplitMode),
            KvCacheTypeK = LastJudgeStr(all, j => j.ServerSettings?.KvCacheTypeK),
            KvCacheTypeV = LastJudgeStr(all, j => j.ServerSettings?.KvCacheTypeV),
            EnableKvOffload = LastJudgeBool(all, j => j.ServerSettings?.EnableKvOffload),
            EnableFlashAttention = LastJudgeBool(all, j => j.ServerSettings?.EnableFlashAttention),
            SamplingTemperature = LastJudgeDouble(all, j => j.ServerSettings?.SamplingTemperature),
            TopP = LastJudgeDouble(all, j => j.ServerSettings?.TopP),
            TopK = LastJudgeInt(all, j => j.ServerSettings?.TopK),
            MinP = LastJudgeDouble(all, j => j.ServerSettings?.MinP),
            RepeatPenalty = LastJudgeDouble(all, j => j.ServerSettings?.RepeatPenalty),
            RepeatLastNTokens = LastJudgeInt(all, j => j.ServerSettings?.RepeatLastNTokens),
            PresencePenalty = LastJudgeDouble(all, j => j.ServerSettings?.PresencePenalty),
            FrequencyPenalty = LastJudgeDouble(all, j => j.ServerSettings?.FrequencyPenalty),
            Seed = LastJudgeInt(all, j => j.ServerSettings?.Seed),
            ThreadCount = LastJudgeInt(all, j => j.ServerSettings?.ThreadCount),
            HttpThreadCount = LastJudgeInt(all, j => j.ServerSettings?.HttpThreadCount),
            ChatTemplate = LastJudgeStr(all, j => j.ServerSettings?.ChatTemplate),
            EnableJinja = LastJudgeBool(all, j => j.ServerSettings?.EnableJinja),
            ReasoningFormat = LastJudgeStr(all, j => j.ServerSettings?.ReasoningFormat),
            ModelAlias = LastJudgeStr(all, j => j.ServerSettings?.ModelAlias),
            LogVerbosity = LastJudgeInt(all, j => j.ServerSettings?.LogVerbosity),
            EnableMlock = LastJudgeBool(all, j => j.ServerSettings?.EnableMlock),
            EnableMmap = LastJudgeBool(all, j => j.ServerSettings?.EnableMmap),
            ServerTimeoutSeconds = LastJudgeDouble(all, j => j.ServerSettings?.ServerTimeoutSeconds),
            ExtraArgs = LastJudgeList(all, j => j.ServerSettings?.ExtraArgs) ?? []
        };
    }

    private static string? LastJudgeStr(IReadOnlyList<PartialConfig> all, Func<PartialJudgeConfig, string?> sel)
    {
        for (var i = all.Count - 1; i >= 0; i--)
        {
            var j = all[i].Judge;
            if (j is not null)
            {
                var v = sel(j);
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
        }
        return null;
    }

    private static int? LastJudgeInt(IReadOnlyList<PartialConfig> all, Func<PartialJudgeConfig, int?> sel)
    {
        for (var i = all.Count - 1; i >= 0; i--)
        {
            var j = all[i].Judge;
            if (j is not null)
            {
                var v = sel(j);
                if (v.HasValue) return v;
            }
        }
        return null;
    }

    private static double? LastJudgeDouble(IReadOnlyList<PartialConfig> all, Func<PartialJudgeConfig, double?> sel)
    {
        for (var i = all.Count - 1; i >= 0; i--)
        {
            var j = all[i].Judge;
            if (j is not null)
            {
                var v = sel(j);
                if (v.HasValue) return v;
            }
        }
        return null;
    }

    private static bool? LastJudgeBool(IReadOnlyList<PartialConfig> all, Func<PartialJudgeConfig, bool?> sel)
    {
        for (var i = all.Count - 1; i >= 0; i--)
        {
            var j = all[i].Judge;
            if (j is not null)
            {
                var v = sel(j);
                if (v.HasValue) return v;
            }
        }
        return null;
    }

    private static List<string>? LastJudgeList(IReadOnlyList<PartialConfig> all, Func<PartialJudgeConfig, List<string>?> sel)
    {
        for (var i = all.Count - 1; i >= 0; i--)
        {
            var j = all[i].Judge;
            if (j is not null)
            {
                var v = sel(j);
                if (v?.Count > 0) return v;
            }
        }
        return null;
    }

    private static T? LastJudgeEnum<T>(IReadOnlyList<PartialConfig> all, Func<PartialJudgeConfig, T?> sel)
        where T : struct, Enum
    {
        for (var i = all.Count - 1; i >= 0; i--)
        {
            var j = all[i].Judge;
            if (j is not null)
            {
                var v = sel(j);
                if (v.HasValue) return v;
            }
        }
        return null;
    }

    private static ModelSource? LastJudgeModelSource(IReadOnlyList<PartialConfig> all, Func<PartialJudgeConfig, ModelSource?> sel)
    {
        for (var i = all.Count - 1; i >= 0; i--)
        {
            var j = all[i].Judge;
            if (j is not null)
            {
                var v = sel(j);
                if (v is not null) return v;
            }
        }
        return null;
    }
}
