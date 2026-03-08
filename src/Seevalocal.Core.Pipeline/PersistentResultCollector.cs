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
                MetricsJson TEXT NOT NULL,
                AllStageOutputsJson TEXT NOT NULL,
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
            results.Add(ReadResult(reader));
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
            // Inline the metadata save to avoid nested semaphore acquisition (deadlock)
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT OR REPLACE INTO RunMetadata (Key, Value) VALUES (@key, @value);
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
    /// Loads startup configuration parameters.
    /// </summary>
    public async Task<ResolvedConfig?> LoadStartupParametersAsync(CancellationToken ct)
    {
        var configJson = await LoadMetadataAsync("startup_config", ct);
        if (string.IsNullOrEmpty(configJson)) return null;

        return JsonSerializer.Deserialize<ResolvedConfig>(configJson);
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
                try
                {
                    outputs[key] = JsonSerializer.Deserialize<object>(valueStr);
                }
                catch
                {
                    outputs[key] = valueStr;
                }
            }
        }

        return outputs;
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
                (EvalItemId, EvalSetId, Succeeded, FailureReason, MetricsJson, AllStageOutputsJson, RawLlmResponse, StartedAt, DurationSeconds, Phase, LastCompletedStage)
                VALUES
                (@evalItemId, @evalSetId, @succeeded, @failureReason, @metricsJson, @outputsJson, @rawResponse, @startedAt, @duration, @phase, @lastStage)
                """;

            cmd.Parameters.AddWithValue("@evalItemId", result.EvalItemId);
            cmd.Parameters.AddWithValue("@evalSetId", result.EvalSetId);
            cmd.Parameters.AddWithValue("@succeeded", result.Succeeded ? 1 : 0);
            cmd.Parameters.AddWithValue("@failureReason", result.FailureReason ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@metricsJson", JsonSerializer.Serialize(result.Metrics));
            cmd.Parameters.AddWithValue("@outputsJson", JsonSerializer.Serialize(result.AllStageOutputs));
            cmd.Parameters.AddWithValue("@rawResponse", result.RawLlmResponse ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@startedAt", result.StartedAt.ToString("O"));
            cmd.Parameters.AddWithValue("@duration", result.DurationSeconds);
            cmd.Parameters.AddWithValue("@phase", "primary");  // Default phase
            cmd.Parameters.AddWithValue("@lastStage", "Complete");  // Item fully completed

            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _writeSemaphore.Release();
        }
    }

    /// <summary>
    /// Saves partial progress for an item that's in progress (for crash recovery mid-item).
    /// </summary>
    public async Task SavePartialProgressAsync(string evalItemId, string evalSetId, string lastStageName, CancellationToken ct)
    {
        await _writeSemaphore.WaitAsync(ct);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT OR REPLACE INTO EvalResults
                (EvalItemId, EvalSetId, Succeeded, FailureReason, MetricsJson, AllStageOutputsJson, RawLlmResponse, StartedAt, DurationSeconds, Phase, LastCompletedStage)
                VALUES
                (@evalItemId, @evalSetId, 0, NULL, '[]', '{}', NULL, @startedAt, 0, @phase, @lastStage)
                """;

            cmd.Parameters.AddWithValue("@evalItemId", evalItemId);
            cmd.Parameters.AddWithValue("@evalSetId", evalSetId);
            cmd.Parameters.AddWithValue("@startedAt", DateTimeOffset.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("@phase", "primary");
            cmd.Parameters.AddWithValue("@lastStage", lastStageName);

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
                    MetricsJson = @metricsJson,
                    AllStageOutputsJson = @outputsJson,
                    DurationSeconds = @duration,
                    Phase = 'judge',
                    LastCompletedStage = 'JudgeComplete'
                WHERE EvalItemId = @evalItemId
                """;

            cmd.Parameters.AddWithValue("@evalItemId", result.EvalItemId);
            cmd.Parameters.AddWithValue("@succeeded", result.Succeeded ? 1 : 0);
            cmd.Parameters.AddWithValue("@failureReason", result.FailureReason ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@metricsJson", JsonSerializer.Serialize(result.Metrics));
            cmd.Parameters.AddWithValue("@outputsJson", JsonSerializer.Serialize(result.AllStageOutputs));
            cmd.Parameters.AddWithValue("@duration", result.DurationSeconds);

            await cmd.ExecuteNonQueryAsync(ct);
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

    private static EvalResult ReadResult(SqliteDataReader reader)
    {
        return new EvalResult
        {
            EvalItemId = reader.GetString("EvalItemId"),
            EvalSetId = reader.GetString("EvalSetId"),
            Succeeded = reader.GetInt32("Succeeded") == 1,
            FailureReason = reader.IsDBNull("FailureReason") ? null : reader.GetString("FailureReason"),
            Metrics = JsonSerializer.Deserialize<List<MetricValue>>(reader.GetString("MetricsJson")) ?? [],
            AllStageOutputs = JsonSerializer.Deserialize<Dictionary<string, object?>>(reader.GetString("AllStageOutputsJson")) ?? [],
            RawLlmResponse = reader.IsDBNull("RawLlmResponse") ? null : reader.GetString("RawLlmResponse"),
            StartedAt = DateTimeOffset.Parse(reader.GetString("StartedAt")),
            DurationSeconds = reader.GetDouble("DurationSeconds")
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

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
            _writeSemaphore.Dispose();
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
