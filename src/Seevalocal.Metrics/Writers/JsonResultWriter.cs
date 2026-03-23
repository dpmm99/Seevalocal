using Microsoft.Extensions.Logging;
using Seevalocal.Core;
using Seevalocal.Core.Models;
using Seevalocal.Metrics.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Seevalocal.Metrics.Writers;

/// <summary>
/// Writes per-eval JSON files as results arrive and summary.json at finalization.
/// Thread-safe via per-file locking.
/// </summary>
public sealed class JsonResultWriter : IResultWriter
{
    private readonly string _outputDir;
    private readonly bool _includeRawLlmResponse;
    private readonly bool _includeAllStageOutputs;
    private readonly bool _writePerEvalJson;
    private readonly bool _writeSummaryJson;
    private readonly ILogger<JsonResultWriter> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new MetricScalarJsonConverter() }
    };

    public JsonResultWriter(
        string outputDir,
        bool includeRawLlmResponse = true,
        bool includeAllStageOutputs = false,
        bool writePerEvalJson = true,
        bool writeSummaryJson = true,
        ILogger<JsonResultWriter>? logger = null)
    {
        _outputDir = outputDir ?? throw new ArgumentNullException(nameof(outputDir));
        _includeRawLlmResponse = includeRawLlmResponse;
        _includeAllStageOutputs = includeAllStageOutputs;
        _writePerEvalJson = writePerEvalJson;
        _writeSummaryJson = writeSummaryJson;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<JsonResultWriter>.Instance;

        if (_writePerEvalJson)
        {
            var evalsDir = Path.Combine(_outputDir, "evals");
            _ = Directory.CreateDirectory(evalsDir);
        }
    }

    public async Task WriteResultAsync(EvalResult result, CancellationToken ct)
    {
        if (!_writePerEvalJson) return;

        var dto = ToDto(result);
        var filePath = Path.Combine(_outputDir, "evals", $"eval-{result.EvalItemId}.json");

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var stream = File.Create(filePath);
            await JsonSerializer.SerializeAsync(stream, dto, _jsonOptions, ct).ConfigureAwait(false);
            _logger.LogDebug("Wrote eval result {EvalItemId} to {FilePath}", result.EvalItemId, filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[JsonResultWriter] Failed to write eval result {EvalItemId}: {Message}", result.EvalItemId, ex.Message);
            throw;
        }
        finally
        {
            _ = _writeLock.Release();
        }
    }

    public async Task WriteSummaryAsync(RunSummary summary, CancellationToken ct)
    {
        if (!_writeSummaryJson) return;

        var filePath = Path.Combine(_outputDir, "summary.json");
        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, summary, _jsonOptions, ct).ConfigureAwait(false);
        _logger.LogInformation("[JsonResultWriter] Wrote summary to {FilePath}", filePath);
    }

    public Task FinalizeAsync(CancellationToken ct) => Task.CompletedTask;

    private EvalResultDto ToDto(EvalResult result)
    {
        return new EvalResultDto
        {
            EvalItemId = result.EvalItemId,
            Succeeded = result.Succeeded,
            FailureReason = result.FailureReason,
            DurationSeconds = result.DurationSeconds,
            StartedAt = result.StartedAt,
            RawLlmResponse = _includeRawLlmResponse ? result.RawLlmResponse : null,
            AllStageOutputs = _includeAllStageOutputs ? result.AllStageOutputs : null,
            Metrics = result.Metrics.Select(static m => new MetricValueDto
            {
                Name = m.Name,
                Value = m.Value.ToObject(),
                SourceStage = m.SourceStage
            }).ToList()
        };
    }

    private sealed class EvalResultDto
    {
        public string EvalItemId { get; init; } = "";
        public bool Succeeded { get; init; }
        public string? FailureReason { get; init; }
        public double DurationSeconds { get; init; }
        public DateTimeOffset StartedAt { get; init; }
        public string? RawLlmResponse { get; init; }
        public IReadOnlyDictionary<string, object?>? AllStageOutputs { get; init; }
        public List<MetricValueDto> Metrics { get; init; } = [];
    }

    private sealed class MetricValueDto
    {
        public string Name { get; init; } = "";
        public object? Value { get; init; }
        public string? SourceStage { get; init; }
    }
}
