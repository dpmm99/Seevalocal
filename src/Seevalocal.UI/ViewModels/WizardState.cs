using Seevalocal.Core.Models;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Seevalocal.UI.ViewModels;

/// <summary>
/// All mutable wizard state in one place, replacing ~150 private backing fields
/// that were previously scattered across <see cref="WizardViewModel"/>.
///
/// Fields are public so <see cref="WizardViewModel"/> can take refs to them for
/// the <c>SetState</c> pattern, keeping property-change tracking in the VM.
///
/// The naming convention for judge fields is: prefix every llama-server field name
/// with "Judge" (e.g. <see cref="ContextWindowTokens"/> → <see cref="JudgeContextWindowTokens"/>).
/// <see cref="LlamaSettingNames"/> and the two getter delegates exploit this symmetry
/// to eliminate the duplicated <c>BuildLlamaServerSettings</c> / <c>BuildJudgeLlamaServerSettings</c>
/// pattern.
/// </summary>
public class WizardState
{
    // ── Server management ────────────────────────────────────────────────────
    public bool ManageServer = true;
    public bool UseLocalFile = true;
    public string? LocalModelPath;
    public string? HfRepo;
    public string? HfToken;
    public string? ServerUrl;
    public string? LlamaServerExecutablePath;
    public string Host = "127.0.0.1";
    public int Port = 8080;
    public string? ApiKey;

    // ── Llama-server settings (main) ─────────────────────────────────────────
    public int? ContextWindowTokens;
    public int? BatchSizeTokens;
    public int? UbatchSizeTokens;
    public int? ParallelSlotCount;
    public bool? EnableContinuousBatching;
    public bool? EnableCachePrompt;
    public bool? EnableContextShift;
    public int? GpuLayerCount;
    public string? SplitMode;
    public string? KvCacheTypeK;
    public string? KvCacheTypeV;
    public bool? EnableKvOffload;
    public bool? EnableFlashAttention;
    public double? SamplingTemperature;
    public double? TopP;
    public int? TopK;
    public double? MinP;
    public double? RepeatPenalty;
    public int? RepeatLastNTokens;
    public double? PresencePenalty;
    public double? FrequencyPenalty;
    public int? Seed;
    public int? ThreadCount;
    public int? HttpThreadCount;
    public string? ChatTemplate;
    public bool? EnableJinja;
    public string? ReasoningFormat;
    public string? ModelAlias;
    public int? ReasoningBudget;
    public string? ReasoningBudgetMessage;
    public int? LogVerbosity;
    public bool? EnableMlock;
    public bool? EnableMmap;
    public double? ServerTimeoutSeconds;
    public string? ExtraLlamaArgs;

    // ── Pipeline / dataset ───────────────────────────────────────────────────
    public string PipelineName = "CasualQA";
    public string? DataFilePath;
    public string? PromptDir;
    public string? ExpectedDir;
    public bool UseSingleFileDataSource = true;

    // ── Field mapping ────────────────────────────────────────────────────────
    public string? FieldMappingId;
    public string? FieldMappingUserPrompt;
    public string? FieldMappingExpectedOutput;
    public string? FieldMappingSystemPrompt;
    public string? FieldMappingSourceLanguage;
    public string? FieldMappingTargetLanguage;
    public string? FieldMappingTestFile;
    public string? FieldMappingBuildScript;

    // ── Pipeline-specific config ─────────────────────────────────────────────
    public string? TranslationSourceLanguage = "English";
    public string? TranslationTargetLanguage = "French";
    public string? TranslationSystemPrompt;
    public string? CodeBuildScriptPath;

    // ── Judge ────────────────────────────────────────────────────────────────
    public bool EnableJudge;
    public bool JudgeManageServer = true;
    public bool JudgeUseLocalFile = true;
    public string? JudgeLocalModelPath;
    public string? JudgeHfRepo;
    public string? JudgeHfToken;
    public string? JudgeApiKey;
    public string? JudgeServerUrl;
    public string? JudgeExecutablePath;

    // ── Judge llama-server settings (mirrors main, prefixed "Judge") ─────────
    public int? JudgeContextWindowTokens;
    public int? JudgeBatchSizeTokens;
    public int? JudgeUbatchSizeTokens;
    public int? JudgeParallelSlotCount;
    public bool? JudgeEnableContinuousBatching;
    public bool? JudgeEnableCachePrompt;
    public bool? JudgeEnableContextShift;
    public int? JudgeGpuLayerCount;
    public string? JudgeSplitMode;
    public string? JudgeKvCacheTypeK;
    public string? JudgeKvCacheTypeV;
    public bool? JudgeEnableKvOffload;
    public bool? JudgeEnableFlashAttention;
    public double? JudgeSamplingTemperature;
    public double? JudgeTopP;
    public int? JudgeTopK;
    public double? JudgeMinP;
    public double? JudgeRepeatPenalty;
    public int? JudgeRepeatLastNTokens;
    public double? JudgePresencePenalty;
    public double? JudgeFrequencyPenalty;
    public int? JudgeSeed;
    public int? JudgeThreadCount;
    public int? JudgeHttpThreadCount;
    public string? JudgeChatTemplate;
    public bool? JudgeEnableJinja;
    public string? JudgeReasoningFormat;
    public string? JudgeModelAlias;
    public int? JudgeReasoningBudget;
    public string? JudgeReasoningBudgetMessage;
    public int? JudgeLogVerbosity;
    public bool? JudgeEnableMlock;
    public bool? JudgeEnableMmap;
    public double? JudgeServerTimeoutSeconds;
    public string? JudgeExtraLlamaArgs;
    public string JudgeTemplate = "standard";

    // ── Output ───────────────────────────────────────────────────────────────
    public string OutputDir = "./results";
    public string? RunName;
    public ShellTarget? ShellTarget;
    public bool WritePerEvalJson;
    public bool WriteSummaryJson = true;
    public bool WriteSummaryCsv;
    public bool WriteResultsParquet;
    public bool IncludeRawLlmResponse = true;
    public bool ContinueOnEvalFailure = true;
    public int? MaxConcurrentEvals;
    public bool ContinueFromCheckpoint;
    public string? CheckpointDatabasePath;

    // ── Canonical llama-server setting names (without "Judge" prefix) ─────────
    // Used to drive BuildServerSettingsFromState without enumerating fields manually.
    public static readonly IReadOnlyList<string> LlamaSettingNames =
    [
        nameof(ContextWindowTokens), nameof(BatchSizeTokens), nameof(UbatchSizeTokens),
        nameof(ParallelSlotCount), nameof(EnableContinuousBatching), nameof(EnableCachePrompt),
        nameof(EnableContextShift), nameof(GpuLayerCount), nameof(SplitMode),
        nameof(KvCacheTypeK), nameof(KvCacheTypeV), nameof(EnableKvOffload),
        nameof(EnableFlashAttention), nameof(SamplingTemperature), nameof(TopP),
        nameof(TopK), nameof(MinP), nameof(RepeatPenalty), nameof(RepeatLastNTokens),
        nameof(PresencePenalty), nameof(FrequencyPenalty), nameof(Seed),
        nameof(ThreadCount), nameof(HttpThreadCount), nameof(ChatTemplate),
        nameof(EnableJinja), nameof(ReasoningFormat), nameof(ModelAlias),
        nameof(ReasoningBudget), nameof(ReasoningBudgetMessage),
        nameof(LogVerbosity), nameof(EnableMlock), nameof(EnableMmap),
        nameof(ServerTimeoutSeconds), nameof(ExtraLlamaArgs)
    ];

    // ── Field access for BuildServerSettingsFromState ─────────────────────────

    private static readonly Dictionary<string, FieldInfo> _stateFields =
        typeof(WizardState)
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(f => f.Name, f => f);

    /// <summary>Returns the value of the main llama-server field with the given name.</summary>
    public static object? GetLlamaField(WizardState state, string settingName) =>
        _stateFields.TryGetValue(settingName, out var f) ? f.GetValue(state) : null;

    /// <summary>Returns the value of the judge llama-server field (name prefixed with "Judge").</summary>
    public static object? GetJudgeLlamaField(WizardState state, string settingName) =>
        _stateFields.TryGetValue("Judge" + settingName, out var f) ? f.GetValue(state) : null;

    // ── Factory ───────────────────────────────────────────────────────────────

    public static WizardState CreateDefaults() => new()
    {
        ShellTarget = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Core.Models.ShellTarget.PowerShell
            : Core.Models.ShellTarget.Bash,
    };

    // ── Bulk apply from ResolvedConfig ────────────────────────────────────────

    /// <summary>
    /// Copies values from <paramref name="config"/> into <paramref name="state"/>,
    /// optionally skipping fields already present in <paramref name="editedFields"/>.
    /// Also adds applied fields to <paramref name="editedFields"/> when not in "only unedited" mode.
    /// </summary>
    public static void ApplyResolvedConfig(
        WizardState state,
        ResolvedConfig config,
        HashSet<string> editedFields,
        bool onlyUnedited = false)
    {
        void Set<T>(string propName, T? value) where T : class
        {
            if (value == null) return;
            if (onlyUnedited && editedFields.Contains(propName)) return;
            if (_stateFields.TryGetValue(propName, out var f)) f.SetValue(state, value);
            if (!onlyUnedited) editedFields.Add(propName);
        }

        void SetVal<T>(string propName, T? value) where T : struct
        {
            if (!value.HasValue) return;
            if (onlyUnedited && editedFields.Contains(propName)) return;
            if (_stateFields.TryGetValue(propName, out var f)) f.SetValue(state, (object)value.Value);
            if (!onlyUnedited) editedFields.Add(propName);
        }

        // ── Server ────────────────────────────────────────────────────────────
        if (config.Server is { } srv)
        {
            SetVal(nameof(state.ManageServer), srv.Manage);
            Set(nameof(state.LlamaServerExecutablePath), srv.ExecutablePath);
            Set(nameof(state.Host), srv.Host);
            SetVal(nameof(state.Port), srv.Port);
            Set(nameof(state.ApiKey), srv.ApiKey);
            Set(nameof(state.ServerUrl), srv.BaseUrl);

            ApplyModelSource(srv.Model, onlyUnedited, editedFields,
                nameof(state.UseLocalFile), nameof(state.LocalModelPath),
                nameof(state.HfRepo), nameof(state.HfToken), state,
                (useLocal, path, repo, token) =>
                {
                    state.UseLocalFile = useLocal;
                    state.LocalModelPath = path;
                    state.HfRepo = repo;
                    state.HfToken = token;
                });
        }

        // ── LlamaServer settings ──────────────────────────────────────────────
        if (config.LlamaServer is { } ls)
            ApplyLlamaSettings(ls, state, editedFields, onlyUnedited, prefix: "");

        // ── ExtraArgs ─────────────────────────────────────────────────────────
        if (config.LlamaServer?.ExtraArgs is { Count: > 0 } extraArgs)
        {
            if (!onlyUnedited || !editedFields.Contains(nameof(state.ExtraLlamaArgs)))
            {
                state.ExtraLlamaArgs = string.Join(" ", extraArgs);
                if (!onlyUnedited) editedFields.Add(nameof(state.ExtraLlamaArgs));
            }
        }

        // ── Judge ─────────────────────────────────────────────────────────────
        if (config.Judge is { } judge)
        {
            SetVal(nameof(state.EnableJudge), (bool?)judge.Enable);
            SetVal(nameof(state.JudgeManageServer), (bool?)judge.Manage);
            Set(nameof(state.JudgeServerUrl), judge.BaseUrl);
            Set(nameof(state.JudgeTemplate), judge.JudgePromptTemplate);

            if (judge.ServerConfig is { } jsc)
            {
                Set(nameof(state.JudgeExecutablePath), jsc.ExecutablePath);
                Set(nameof(state.JudgeApiKey), jsc.ApiKey);

                ApplyModelSource(jsc.Model, onlyUnedited, editedFields,
                    nameof(state.JudgeUseLocalFile), nameof(state.JudgeLocalModelPath),
                    nameof(state.JudgeHfRepo), nameof(state.JudgeHfToken), state,
                    (useLocal, path, repo, token) =>
                    {
                        state.JudgeUseLocalFile = useLocal;
                        state.JudgeLocalModelPath = path;
                        state.JudgeHfRepo = repo;
                        state.JudgeHfToken = token;
                    });
            }

            if (judge.ServerSettings is { } jss)
                ApplyLlamaSettings(jss, state, editedFields, onlyUnedited, prefix: "Judge");

            // ── ExtraArgs ─────────────────────────────────────────────────────────
            if (judge.ServerSettings?.ExtraArgs is { Count: > 0 } judgeExtraArgs)
            {
                if (!onlyUnedited || !editedFields.Contains(nameof(state.JudgeExtraLlamaArgs)))
                {
                    state.JudgeExtraLlamaArgs = string.Join(" ", judgeExtraArgs);
                    if (!onlyUnedited) editedFields.Add(nameof(state.JudgeExtraLlamaArgs));
                }
            }
        }

        // ── Run ───────────────────────────────────────────────────────────────
        if (config.Run is { } run)
        {
            Set(nameof(state.RunName), run.RunName);
            Set(nameof(state.OutputDir), run.OutputDirectoryPath);
            SetVal(nameof(state.ShellTarget), run.ExportShellTarget);
            SetVal(nameof(state.ContinueOnEvalFailure), run.ContinueOnEvalFailure);
            SetVal(nameof(state.MaxConcurrentEvals), run.MaxConcurrentEvals);
            Set(nameof(state.CheckpointDatabasePath), run.CheckpointDatabasePath);
        }

        // ── EvalSets / DataSource ─────────────────────────────────────────────
        if (config.EvalSets.Count > 0)
        {
            var es = config.EvalSets[0];
            Set(nameof(state.PipelineName), es.PipelineName);

            var ds = es.DataSource;
            ApplyDataSource(ds, state, editedFields, onlyUnedited);
            ApplyPipelineOptions(es, state, editedFields, onlyUnedited);
        }

        // Also apply top-level DataSource if present (PartialConfig compatibility)
        if (config.DataSource is { } topDs && !string.IsNullOrEmpty(topDs.FilePath ?? topDs.PromptDirectory))
            ApplyDataSource(topDs, state, editedFields, onlyUnedited);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Copies llama-server settings from a <see cref="LlamaServerSettings"/> into state fields,
    /// using the provided prefix to find the matching state field (e.g. "Judge" prefix for judge fields).
    /// Driven entirely by <see cref="LlamaSettingNames"/> — no manual per-field code.
    /// </summary>
    private static void ApplyLlamaSettings(
        LlamaServerSettings settings,
        WizardState state,
        HashSet<string> editedFields,
        bool onlyUnedited,
        string prefix)
    {
        var settingsType = settings.GetType();

        foreach (var settingName in LlamaSettingNames)
        {
            var stateFieldName = prefix + settingName;
            if (onlyUnedited && editedFields.Contains(stateFieldName)) continue;

            var srcProp = settingsType.GetProperty(settingName);
            if (srcProp == null) continue;

            var value = srcProp.GetValue(settings);
            if (value == null) continue;

            if (_stateFields.TryGetValue(stateFieldName, out var dstField))
            {
                dstField.SetValue(state, value);
                if (!onlyUnedited) editedFields.Add(stateFieldName);
            }
        }
    }

    private static void ApplyModelSource(
        ModelSource? model,
        bool onlyUnedited,
        HashSet<string> editedFields,
        string useLocalName, string localPathName, string hfRepoName, string hfTokenName,
        WizardState state,
        Action<bool, string?, string?, string?> apply)
    {
        if (model == null) return;
        if (onlyUnedited && editedFields.Contains(useLocalName)) return;

        bool useLocal = model.Kind == ModelSourceKind.LocalFile;
        apply(useLocal, model.FilePath, model.HfRepo, model.HfToken);

        if (!onlyUnedited)
        {
            editedFields.Add(useLocalName);
            editedFields.Add(useLocal ? localPathName : hfRepoName);
            if (!string.IsNullOrEmpty(model.HfToken)) editedFields.Add(hfTokenName);
        }
    }

    private static void ApplyDataSource(
        DataSourceConfig ds,
        WizardState state,
        HashSet<string> editedFields,
        bool onlyUnedited)
    {
        bool isSingleFile = ds.Kind is DataSourceKind.SingleFile or DataSourceKind.JsonFile
            or DataSourceKind.JsonlFile or DataSourceKind.YamlFile or DataSourceKind.CsvFile
            or DataSourceKind.ParquetFile or DataSourceKind.File;

        bool isDirectory = ds.Kind is DataSourceKind.SplitDirectories or DataSourceKind.DirectoryPair
            or DataSourceKind.Directory;

        void MaybeSet(string name, string? value)
        {
            if (string.IsNullOrEmpty(value)) return;
            if (onlyUnedited && editedFields.Contains(name)) return;
            if (_stateFields.TryGetValue(name, out var f)) f.SetValue(state, value);
            if (!onlyUnedited) editedFields.Add(name);
        }

        void MaybeSetBool(string name, bool value)
        {
            if (onlyUnedited && editedFields.Contains(name)) return;
            if (_stateFields.TryGetValue(name, out var f)) f.SetValue(state, (object)value);
            if (!onlyUnedited) editedFields.Add(name);
        }

        if (isSingleFile)
        {
            MaybeSetBool(nameof(state.UseSingleFileDataSource), true);
            MaybeSet(nameof(state.DataFilePath), ds.FilePath);
        }
        else if (isDirectory)
        {
            MaybeSetBool(nameof(state.UseSingleFileDataSource), false);
            MaybeSet(nameof(state.PromptDir), ds.PromptDirectory);
            MaybeSet(nameof(state.ExpectedDir), ds.ExpectedDirectory);
        }

        // Field mapping
        if (ds.FieldMapping is { } fm)
        {
            MaybeSet(nameof(state.FieldMappingId), fm.IdField);
            MaybeSet(nameof(state.FieldMappingUserPrompt), fm.UserPromptField);
            MaybeSet(nameof(state.FieldMappingExpectedOutput), fm.ExpectedOutputField);
            MaybeSet(nameof(state.FieldMappingSystemPrompt), fm.SystemPromptField);
            MaybeSet(nameof(state.FieldMappingSourceLanguage), fm.SourceLanguageField);
            MaybeSet(nameof(state.FieldMappingTargetLanguage), fm.TargetLanguageField);
        }
    }

    private static void ApplyPipelineOptions(
        EvalSetConfig es,
        WizardState state,
        HashSet<string> editedFields,
        bool onlyUnedited)
    {
        if (es.PipelineOptions == null) return;

        void MaybeSet(string stateName, string optionKey)
        {
            if (!es.PipelineOptions.TryGetValue(optionKey, out var val) || val is not string str) return;
            if (onlyUnedited && editedFields.Contains(stateName)) return;
            if (_stateFields.TryGetValue(stateName, out var f)) f.SetValue(state, str);
            if (!onlyUnedited) editedFields.Add(stateName);
        }

        switch (es.PipelineName)
        {
            case "Translation":
                MaybeSet(nameof(state.TranslationSourceLanguage), "sourceLanguage");
                MaybeSet(nameof(state.TranslationTargetLanguage), "targetLanguage");
                MaybeSet(nameof(state.TranslationSystemPrompt), "systemPrompt");
                break;
            case "CSharpCoding":
                MaybeSet(nameof(state.CodeBuildScriptPath), "buildScriptPath");
                MaybeSet(nameof(state.FieldMappingTestFile), "testFilePath");
                break;
        }
    }
}