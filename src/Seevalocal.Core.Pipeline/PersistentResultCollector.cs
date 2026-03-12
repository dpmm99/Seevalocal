using Microsoft.Data.Sqlite;
using Seevalocal.Core.Models;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Seevalocal.Core.Pipeline;

/// <summary>
/// Persists evaluation results and checkpoints to a SQLite database.
/// Supports crash recovery and resume from last checkpoint.
/// Saves all stage outputs and startup parameters for full resume capability.
/// </summary>
public sealed class PersistentResultCollector : IResultCollector, IAsyncDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _connection;
    private readonly ConcurrentDictionary<string, EvalResult> _resultsCache = new();
    private readonly SemaphoreSlim _writeSemaphore = new(1);
    private bool _disposed;
    private bool _finalized;
    private ResolvedConfig? _startupConfig;

    public PersistentResultCollector(string dbPath)
    {
        _dbPath = dbPath;
        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS EvalResults (
                EvalItemId TEXT PRIMARY KEY,
                EvalSetId TEXT NOT NULL,
                Succeeded INTEGER NOT NULL,
                FailureReason TEXT,
                RawLlmResponse TEXT,
                StartedAt TEXT NOT NULL,
                DurationSeconds REAL NOT NULL,
                Phase TEXT NOT NULL DEFAULT 'primary',
                LastCompletedStage TEXT
            );

            CREATE TABLE IF NOT EXISTS StageOutputs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                EvalItemId TEXT NOT NULL,
                StageName TEXT NOT NULL,
                OutputKey TEXT NOT NULL,
                OutputValue TEXT,
                CompletedAt TEXT NOT NULL,
                FOREIGN KEY (EvalItemId) REFERENCES EvalResults(EvalItemId)
            );

            CREATE TABLE IF NOT EXISTS Metrics (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                EvalItemId TEXT NOT NULL,
                MetricName TEXT NOT NULL,
                SourceStage TEXT,
                MetricType TEXT NOT NULL,
                IntValue INTEGER,
                DoubleValue REAL,
                BoolValue INTEGER,
                StringValue TEXT,
                CompletedAt TEXT NOT NULL,
                FOREIGN KEY (EvalItemId) REFERENCES EvalResults(EvalItemId)
            );

            CREATE TABLE IF NOT EXISTS StartupParameters (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Checkpoint (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS RunMetadata (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_StageOutputs_EvalItemId ON StageOutputs(EvalItemId);
            CREATE INDEX IF NOT EXISTS IX_StageOutputs_StageName ON StageOutputs(StageName);
            CREATE INDEX IF NOT EXISTS IX_Metrics_EvalItemId ON Metrics(EvalItemId);
            CREATE INDEX IF NOT EXISTS IX_Metrics_MetricName ON Metrics(MetricName);
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Loads completed item IDs for a specific phase.
    /// </summary>
    public async Task<HashSet<string>> GetCompletedItemIdsAsync(string evalSetId, string phase, CancellationToken ct)
    {
        var completed = new HashSet<string>();

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT EvalItemId FROM EvalResults WHERE EvalSetId = @evalSetId AND Phase = @phase";
        cmd.Parameters.AddWithValue("@evalSetId", evalSetId);
        cmd.Parameters.AddWithValue("@phase", phase);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            completed.Add(reader.GetString(0));
        }

        return completed;
    }

    /// <summary>
    /// Gets all eval set IDs that have results in this checkpoint database.
    /// </summary>
    public async Task<List<string>> GetEvalSetIdsAsync(CancellationToken ct)
    {
        var evalSetIds = new List<string>();

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT EvalSetId FROM EvalResults";

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            evalSetIds.Add(reader.GetString(0));
        }

        return evalSetIds;
    }

    /// <summary>
    /// Gets IDs of items that have completed the judge phase (LastCompletedStage = 'JudgeStage').
    /// </summary>
    public async Task<HashSet<string>> GetJudgeCompletedItemIdsAsync(CancellationToken ct)
    {
        var completed = new HashSet<string>();

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT EvalItemId FROM EvalResults WHERE LastCompletedStage = 'JudgeStage'";

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            completed.Add(reader.GetString(0));
        }

        return completed;
    }

    /// <summary>
    /// Loads all results for a specific phase.
    /// </summary>
    public async Task<IReadOnlyList<EvalResult>> GetResultsForPhaseAsync(string evalSetId, string phase, CancellationToken ct)
    {
        var results = new List<EvalResult>();

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM EvalResults WHERE EvalSetId = @evalSetId AND Phase = @phase";
        cmd.Parameters.AddWithValue("@evalSetId", evalSetId);
        cmd.Parameters.AddWithValue("@phase", phase);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(await ReadResultAsync(reader, ct));
        }

        return results;
    }

    /// <summary>
    /// Saves startup configuration parameters.
    /// </summary>
    public async Task SaveStartupParametersAsync(ResolvedConfig config, CancellationToken ct)
    {
        _startupConfig = config;

        await _writeSemaphore.WaitAsync(ct);
        try
        {
            // Save the complete config to StartupParameters table
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT OR REPLACE INTO StartupParameters (Key, Value) VALUES (@key, @value);
                INSERT OR REPLACE INTO RunMetadata (Key, Value) VALUES (@key2, @value2)
                """;

            var configJson = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            cmd.Parameters.AddWithValue("@key", "startup_config");
            cmd.Parameters.AddWithValue("@value", configJson);
            cmd.Parameters.AddWithValue("@key2", "started_at");
            cmd.Parameters.AddWithValue("@value2", DateTimeOffset.UtcNow.ToString("O"));

            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _writeSemaphore.Release();
        }
    }

    /// <summary>
    /// Saves the resolved llama-server binary path for resume capability.
    /// This should be called after the server is started to capture the actual path used.
    /// </summary>
    public async Task SaveServerBinaryPathAsync(string serverType, string binaryPath, CancellationToken ct)
    {
        await _writeSemaphore.WaitAsync(ct);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO StartupParameters (Key, Value) VALUES (@key, @value)";
            cmd.Parameters.AddWithValue("@key", $"{serverType}_binary_path");
            cmd.Parameters.AddWithValue("@value", binaryPath);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _writeSemaphore.Release();
        }
    }

    /// <summary>
    /// Loads the saved llama-server binary path.
    /// </summary>
    public async Task<string?> LoadServerBinaryPathAsync(string serverType, CancellationToken ct)
    {
        return await LoadStartupParameterAsync($"{serverType}_binary_path", ct);
    }

    /// <summary>
    /// Loads startup configuration parameters.
    /// </summary>
    public async Task<ResolvedConfig?> LoadStartupParametersAsync(CancellationToken ct)
    {
        // Load from StartupParameters table (preferred)
        var configJson = await LoadStartupParameterAsync("startup_config", ct);

        // Fallback to RunMetadata for backward compatibility
        if (string.IsNullOrEmpty(configJson))
        {
            configJson = await LoadMetadataAsync("startup_config", ct);
        }

        if (string.IsNullOrEmpty(configJson)) return null;

        return JsonSerializer.Deserialize<ResolvedConfig>(configJson);
    }

    /// <summary>
    /// Loads a startup parameter value.
    /// </summary>
    private async Task<string?> LoadStartupParameterAsync(string key, CancellationToken ct)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT Value FROM StartupParameters WHERE Key = @key";
        cmd.Parameters.AddWithValue("@key", key);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result as string;
    }

    /// <summary>
    /// Saves a stage output for an eval item.
    /// </summary>
    public async Task SaveStageOutputAsync(string evalItemId, string stageName, string outputKey, object? outputValue, CancellationToken ct)
    {
        await _writeSemaphore.WaitAsync(ct);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO StageOutputs (EvalItemId, StageName, OutputKey, OutputValue, CompletedAt)
                VALUES (@evalItemId, @stageName, @outputKey, @outputValue, @completedAt)
                """;

            cmd.Parameters.AddWithValue("@evalItemId", evalItemId);
            cmd.Parameters.AddWithValue("@stageName", stageName);
            cmd.Parameters.AddWithValue("@outputKey", outputKey);
            cmd.Parameters.AddWithValue("@outputValue", outputValue is string s ? s : (outputValue != null ? JsonSerializer.Serialize(outputValue) : DBNull.Value));
            cmd.Parameters.AddWithValue("@completedAt", DateTimeOffset.UtcNow.ToString("O"));

            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _writeSemaphore.Release();
        }
    }

    /// <summary>
    /// Saves a metric for an eval item.
    /// </summary>
    public async Task SaveMetricAsync(string evalItemId, string metricName, MetricValue metricValue, CancellationToken ct)
    {
        await _writeSemaphore.WaitAsync(ct);
        try
        {
            await SaveMetricInternalAsync(evalItemId, metricName, metricValue, ct);
        }
        finally
        {
            _writeSemaphore.Release();
        }
    }

    /// <summary>
    /// Internal method to save a metric without acquiring the semaphore.
    /// Caller must already hold the semaphore.
    /// </summary>
    private async Task SaveMetricInternalAsync(string evalItemId, string metricName, MetricValue metricValue, CancellationToken ct)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Metrics (EvalItemId, MetricName, SourceStage, MetricType, IntValue, DoubleValue, BoolValue, StringValue, CompletedAt)
            VALUES (@evalItemId, @metricName, @sourceStage, @metricType, @intValue, @doubleValue, @boolValue, @stringValue, @completedAt)
            """;

        cmd.Parameters.AddWithValue("@evalItemId", evalItemId);
        cmd.Parameters.AddWithValue("@metricName", metricName);
        cmd.Parameters.AddWithValue("@sourceStage", metricValue.SourceStage ?? (object)DBNull.Value);

        // Extract type and value from the discriminated union
        string metricType;
        object? intValue = DBNull.Value;
        object? doubleValue = DBNull.Value;
        object? boolValue = DBNull.Value;
        object? stringValue = DBNull.Value;

        switch (metricValue.Value)
        {
            case MetricScalar.IntMetric m:
                metricType = "int";
                intValue = m.Value;
                break;
            case MetricScalar.DoubleMetric m:
                metricType = "double";
                doubleValue = m.Value;
                break;
            case MetricScalar.BoolMetric m:
                metricType = "bool";
                boolValue = m.Value ? 1 : 0;
                break;
            case MetricScalar.StringMetric m:
                metricType = "string";
                stringValue = m.Value;
                break;
            default:
                metricType = "string";
                stringValue = metricValue.Value.ToString();
                break;
        }

        cmd.Parameters.AddWithValue("@metricType", metricType);
        cmd.Parameters.AddWithValue("@intValue", intValue);
        cmd.Parameters.AddWithValue("@doubleValue", doubleValue);
        cmd.Parameters.AddWithValue("@boolValue", boolValue);
        cmd.Parameters.AddWithValue("@stringValue", stringValue);
        cmd.Parameters.AddWithValue("@completedAt", DateTimeOffset.UtcNow.ToString("O"));

        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Gets all metrics for an eval item.
    /// </summary>
    public async Task<List<MetricValue>> GetMetricsAsync(string evalItemId, CancellationToken ct)
    {
        var metrics = new List<MetricValue>();

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT MetricName, SourceStage, MetricType, IntValue, DoubleValue, BoolValue, StringValue FROM Metrics WHERE EvalItemId = @evalItemId";
        cmd.Parameters.AddWithValue("@evalItemId", evalItemId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var metricName = reader.GetString(0);
            var sourceStage = reader.IsDBNull(1) ? null : reader.GetString(1);
            var metricType = reader.GetString(2);

            MetricScalar value = metricType switch
            {
                "int" => new MetricScalar.IntMetric(reader.GetInt32(3)),
                "double" => new MetricScalar.DoubleMetric(reader.GetDouble(4)),
                "bool" => new MetricScalar.BoolMetric(reader.GetInt32(5) == 1),
                "string" => new MetricScalar.StringMetric(reader.IsDBNull(6) ? "" : reader.GetString(6)),
                _ => new MetricScalar.StringMetric("")
            };

            metrics.Add(new MetricValue
            {
                Name = metricName,
                SourceStage = sourceStage,
                Value = value
            });
        }

        return metrics;
    }

    /// <summary>
    /// Clears all metrics for an eval item.
    /// </summary>
    public async Task ClearMetricsAsync(string evalItemId, CancellationToken ct)
    {
        await _writeSemaphore.WaitAsync(ct);
        try
        {
            await ClearMetricsInternalAsync(evalItemId, ct);
        }
        finally
        {
            _writeSemaphore.Release();
        }
    }

    /// <summary>
    /// Internal method to clear metrics without acquiring the semaphore.
    /// Caller must already hold the semaphore.
    /// </summary>
    private async Task ClearMetricsInternalAsync(string evalItemId, CancellationToken ct)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Metrics WHERE EvalItemId = @evalItemId";
        cmd.Parameters.AddWithValue("@evalItemId", evalItemId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Gets the last completed stage for an eval item.
    /// </summary>
    public async Task<string?> GetLastCompletedStageAsync(string evalItemId, CancellationToken ct)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT LastCompletedStage FROM EvalResults WHERE EvalItemId = @evalItemId";
        cmd.Parameters.AddWithValue("@evalItemId", evalItemId);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result as string;
    }

    /// <summary>
    /// Gets all stage outputs for an eval item.
    /// </summary>
    public async Task<Dictionary<string, object?>> GetStageOutputsAsync(string evalItemId, CancellationToken ct)
    {
        var outputs = new Dictionary<string, object?>();

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT StageName, OutputKey, OutputValue FROM StageOutputs WHERE EvalItemId = @evalItemId";
        cmd.Parameters.AddWithValue("@evalItemId", evalItemId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var stageName = reader.GetString(0);
            var outputKey = reader.GetString(1);
            var key = $"{stageName}.{outputKey}";

            if (reader.IsDBNull(2))
            {
                outputs[key] = null;
            }
            else
            {
                var valueStr = reader.GetString(2);
                // Try to parse as JSON first, then as plain string
                outputs[key] = DeserializeJsonValue(valueStr);
            }
        }

        return outputs;
    }

    /// <summary>
    /// Deserializes a JSON value, handling primitives correctly.
    /// Optimized to avoid exceptions for plain strings (which are the common case).
    /// </summary>
    private static object? DeserializeJsonValue(string valueStr)
    {
        if (string.IsNullOrEmpty(valueStr))
            return null;

        // Quick check: if it doesn't look like JSON, return as plain string
        // JSON must start with { [ " or be a number/true/false/null
        var trimmed = valueStr.Trim();
        if (trimmed.Length == 0)
            return null;

        char firstChar = trimmed[0];
        bool looksLikeJson = firstChar == '{' || firstChar == '[' || firstChar == '"' ||
                             char.IsDigit(firstChar) || firstChar == '-' || firstChar == '+' ||
                             trimmed.StartsWith("true", StringComparison.OrdinalIgnoreCase) ||
                             trimmed.StartsWith("false", StringComparison.OrdinalIgnoreCase) ||
                             trimmed.StartsWith("null", StringComparison.OrdinalIgnoreCase);

        if (!looksLikeJson)
            return valueStr;  // Plain string - no parsing needed

        // Try to parse as JSON
        try
        {
            using var doc = JsonDocument.Parse(valueStr);
            var root = doc.RootElement;

            return root.ValueKind switch
            {
                JsonValueKind.String => root.GetString(),
                JsonValueKind.Number => root.TryGetInt32(out var i) ? i :
                                        root.TryGetInt64(out var l) ? l :
                                        root.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                JsonValueKind.Array => JsonSerializer.Deserialize<object[]>(valueStr),
                JsonValueKind.Object => JsonSerializer.Deserialize<Dictionary<string, object>>(valueStr),
                _ => valueStr
            };
        }
        catch
        {
            // Not valid JSON, return as plain string
            return valueStr;
        }
    }

    /// <summary>
    /// Loads a checkpoint value.
    /// </summary>
    public async Task<string?> LoadCheckpointAsync(string key, CancellationToken ct)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT Value FROM Checkpoint WHERE Key = @key";
        cmd.Parameters.AddWithValue("@key", key);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result as string;
    }

    /// <summary>
    /// Saves a checkpoint value.
    /// </summary>
    public async Task SaveCheckpointAsync(string key, string value, CancellationToken ct)
    {
        await _writeSemaphore.WaitAsync(ct);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO Checkpoint (Key, Value) VALUES (@key, @value)";
            cmd.Parameters.AddWithValue("@key", key);
            cmd.Parameters.AddWithValue("@value", value);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _writeSemaphore.Release();
        }
    }

    /// <summary>
    /// Saves run metadata.
    /// </summary>
    public async Task SaveMetadataAsync(string key, string value, CancellationToken ct)
    {
        await _writeSemaphore.WaitAsync(ct);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO RunMetadata (Key, Value) VALUES (@key, @value)";
            cmd.Parameters.AddWithValue("@key", key);
            cmd.Parameters.AddWithValue("@value", value);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _writeSemaphore.Release();
        }
    }

    /// <summary>
    /// Loads run metadata.
    /// </summary>
    public async Task<string?> LoadMetadataAsync(string key, CancellationToken ct)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT Value FROM RunMetadata WHERE Key = @key";
        cmd.Parameters.AddWithValue("@key", key);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result as string;
    }

    /// <summary>
    /// Clears all data for a specific eval set and phase.
    /// </summary>
    public async Task ClearEvalSetAsync(string evalSetId, string phase, CancellationToken ct)
    {
        await _writeSemaphore.WaitAsync(ct);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM EvalResults WHERE EvalSetId = @evalSetId AND Phase = @phase";
            cmd.Parameters.AddWithValue("@evalSetId", evalSetId);
            cmd.Parameters.AddWithValue("@phase", phase);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _writeSemaphore.Release();
        }
    }

    public async Task CollectAsync(EvalResult result, CancellationToken ct)
    {
        _resultsCache[result.EvalItemId] = result;

        await _writeSemaphore.WaitAsync(ct);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT OR REPLACE INTO EvalResults
                (EvalItemId, EvalSetId, Succeeded, FailureReason, RawLlmResponse, StartedAt, DurationSeconds, Phase, LastCompletedStage)
                VALUES
                (@evalItemId, @evalSetId, @succeeded, @failureReason, @rawResponse, @startedAt, @duration, @phase, @lastStage)
                """;

            cmd.Parameters.AddWithValue("@evalItemId", result.EvalItemId);
            cmd.Parameters.AddWithValue("@evalSetId", result.EvalSetId);
            cmd.Parameters.AddWithValue("@succeeded", result.Succeeded ? 1 : 0);
            cmd.Parameters.AddWithValue("@failureReason", result.FailureReason ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@rawResponse", result.RawLlmResponse ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@startedAt", result.StartedAt.ToString("O"));
            cmd.Parameters.AddWithValue("@duration", result.DurationSeconds);
            cmd.Parameters.AddWithValue("@phase", "primary");  // Default phase
            cmd.Parameters.AddWithValue("@lastStage", "Complete");  // Item fully completed

            await cmd.ExecuteNonQueryAsync(ct);

            // Save metrics to the Metrics table (use internal method since we already hold the semaphore)
            foreach (var metric in result.Metrics)
            {
                await SaveMetricInternalAsync(result.EvalItemId, metric.Name, metric, ct);
            }
        }
        finally
        {
            _writeSemaphore.Release();
        }
    }

    /// <summary>
    /// Saves partial progress for an item that's in progress (for crash recovery mid-item).
    /// Does NOT overwrite RawLlmResponse to avoid losing the primary phase response.
    /// </summary>
    public async Task SavePartialProgressAsync(string evalItemId, string evalSetId, string lastStageName, CancellationToken ct)
    {
        await _writeSemaphore.WaitAsync(ct);
        try
        {
            await using var cmd = _connection.CreateCommand();
            // Use UPDATE instead of INSERT OR REPLACE to avoid overwriting RawLlmResponse
            // First check if row exists
            cmd.CommandText = "SELECT COUNT(*) FROM EvalResults WHERE EvalItemId = @evalItemId";
            cmd.Parameters.AddWithValue("@evalItemId", evalItemId);
            var exists = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) > 0;

            if (exists)
            {
                // Update existing row - don't touch RawLlmResponse
                cmd.CommandText = """
                    UPDATE EvalResults SET
                        LastCompletedStage = @lastStage,
                        Phase = @phase
                    WHERE EvalItemId = @evalItemId
                    """;
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@evalItemId", evalItemId);
                cmd.Parameters.AddWithValue("@phase", "primary");
                cmd.Parameters.AddWithValue("@lastStage", lastStageName);
            }
            else
            {
                // Insert new row for items not yet started (no RawLlmResponse yet anyway)
                cmd.CommandText = """
                    INSERT INTO EvalResults
                    (EvalItemId, EvalSetId, Succeeded, FailureReason, RawLlmResponse, StartedAt, DurationSeconds, Phase, LastCompletedStage)
                    VALUES
                    (@evalItemId, @evalSetId, 0, NULL, NULL, @startedAt, 0, @phase, @lastStage)
                    """;
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@evalItemId", evalItemId);
                cmd.Parameters.AddWithValue("@evalSetId", evalSetId);
                cmd.Parameters.AddWithValue("@startedAt", DateTimeOffset.UtcNow.ToString("O"));
                cmd.Parameters.AddWithValue("@phase", "primary");
                cmd.Parameters.AddWithValue("@lastStage", lastStageName);
            }

            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _writeSemaphore.Release();
        }
    }

    /// <summary>
    /// Collects a result for the judge phase.
    /// </summary>
    public async Task CollectJudgeResultAsync(EvalResult result, CancellationToken ct)
    {
        _resultsCache[result.EvalItemId] = result;

        await _writeSemaphore.WaitAsync(ct);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                UPDATE EvalResults SET
                    Succeeded = @succeeded,
                    FailureReason = @failureReason,
                    DurationSeconds = @duration,
                    Phase = 'judge',
                    LastCompletedStage = 'JudgeStage'  -- Use actual stage name for skip logic
                WHERE EvalItemId = @evalItemId
                """;

            cmd.Parameters.AddWithValue("@evalItemId", result.EvalItemId);
            cmd.Parameters.AddWithValue("@succeeded", result.Succeeded ? 1 : 0);
            cmd.Parameters.AddWithValue("@failureReason", result.FailureReason ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@duration", result.DurationSeconds);

            var rowsAffected = await cmd.ExecuteNonQueryAsync(ct);

            // If no rows were updated, the primary phase result doesn't exist - this is an error
            if (rowsAffected == 0)
            {
                // Insert a new row with the judge result (shouldn't normally happen)
                cmd.CommandText = """
                    INSERT INTO EvalResults
                    (EvalItemId, EvalSetId, Succeeded, FailureReason, RawLlmResponse, StartedAt, DurationSeconds, Phase, LastCompletedStage)
                    VALUES
                    (@evalItemId, @evalSetId, @succeeded, @failureReason, NULL, @startedAt, @duration, 'judge', 'JudgeComplete')
                    """;
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@evalItemId", result.EvalItemId);
                cmd.Parameters.AddWithValue("@evalSetId", result.EvalSetId);
                cmd.Parameters.AddWithValue("@succeeded", result.Succeeded ? 1 : 0);
                cmd.Parameters.AddWithValue("@failureReason", result.FailureReason ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@startedAt", DateTimeOffset.UtcNow.ToString("O"));
                cmd.Parameters.AddWithValue("@duration", result.DurationSeconds);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            // Clear existing metrics and save new ones to the Metrics table (use internal methods since we already hold the semaphore)
            await ClearMetricsInternalAsync(result.EvalItemId, ct);
            foreach (var metric in result.Metrics)
            {
                await SaveMetricInternalAsync(result.EvalItemId, metric.Name, metric, ct);
            }
        }
        finally
        {
            _writeSemaphore.Release();
        }
    }

    public async Task FinalizeAsync(CancellationToken ct)
    {
        if (_finalized) return;

        await _writeSemaphore.WaitAsync(ct);
        try
        {
            // Inline the checkpoint save to avoid nested semaphore acquisition (deadlock)
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO Checkpoint (Key, Value) VALUES (@key, @value)";
            cmd.Parameters.AddWithValue("@key", "finalized");
            cmd.Parameters.AddWithValue("@value", "true");
            await cmd.ExecuteNonQueryAsync(ct);

            _finalized = true;
        }
        finally
        {
            _writeSemaphore.Release();
        }
    }

    public IReadOnlyList<EvalResult> GetResults()
    {
        return _resultsCache.Values.ToList();
    }

    /// <summary>
    /// Gets all results from the database with full stage outputs and metrics.
    /// Use this instead of GetResults() when you need complete data from the database.
    /// </summary>
    public async Task<IReadOnlyList<EvalResult>> GetResultsAsync(string evalSetId, string phase, CancellationToken ct)
    {
        return await GetResultsForPhaseAsync(evalSetId, phase, ct);
    }

    private async Task<EvalResult> ReadResultAsync(SqliteDataReader reader, CancellationToken ct)
    {
        var evalItemId = reader.GetString("EvalItemId");
        var stageOutputs = await GetStageOutputsAsync(evalItemId, ct);
        var metrics = await GetMetricsAsync(evalItemId, ct);

        return new EvalResult
        {
            EvalItemId = evalItemId,
            EvalSetId = reader.GetString("EvalSetId"),
            Succeeded = reader.GetInt32("Succeeded") == 1,
            FailureReason = reader.IsDBNull("FailureReason") ? null : reader.GetString("FailureReason"),
            Metrics = metrics,
            AllStageOutputs = stageOutputs,
            RawLlmResponse = reader.IsDBNull("RawLlmResponse") ? null : reader.GetString("RawLlmResponse"),
            StartedAt = DateTimeOffset.Parse(reader.GetString("StartedAt")),
            DurationSeconds = reader.GetDouble("DurationSeconds")
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Wait for any pending write operations to complete
        await _writeSemaphore.WaitAsync();
        try
        {
            _connection.Close();
            _connection.Dispose();

            // Clear the SQLite connection pool to ensure all connections are released
            SqliteConnection.ClearAllPools();
        }
        finally
        {
            _writeSemaphore.Release();
            // Don't dispose the semaphore - it may still be in use by pending operations
            // The semaphore will be garbage collected when no longer referenced
        }
    }
}

/// <summary>
/// Extension methods for SqliteDataReader.
/// </summary>
internal static class SqliteDataReaderExtensions
{
    public static T GetFieldValue<T>(this SqliteDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.GetFieldValue<T>(ordinal);
    }

    public static bool IsDBNull(this SqliteDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal);
    }

    public static string GetString(this SqliteDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.GetString(ordinal);
    }

    public static int GetInt32(this SqliteDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.GetInt32(ordinal);
    }

    public static double GetDouble(this SqliteDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.GetDouble(ordinal);
    }
}
