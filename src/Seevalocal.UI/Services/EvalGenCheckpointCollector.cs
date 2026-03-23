using Microsoft.Data.Sqlite;
using Seevalocal.Core.Models;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Seevalocal.UI.Services;

/// <summary>
/// Persists eval generation progress to a SQLite database.
/// Supports crash recovery and resume from last checkpoint.
/// Saves categories, problems, and startup parameters.
/// </summary>
public sealed class EvalGenCheckpointCollector : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ConcurrentDictionary<string, GeneratedCategory> _categoriesCache = new();
    private readonly ConcurrentDictionary<string, GeneratedProblem> _problemsCache = new();
    private bool _disposed;

    public EvalGenCheckpointCollector(string dbPath)
    {
        _connection = new SqliteConnection($"Data Source={dbPath}");
        try
        {
            _connection.Open();
            InitializeDatabase();
            LoadCacheFromDatabase();
        }
        catch
        {
            // If database can't be opened, continue with empty cache
            // This can happen if the path is invalid or permissions issue
        }
    }

    private void LoadCacheFromDatabase()
    {
        try
        {
            // Load categories
            using var catCmd = _connection.CreateCommand();
            catCmd.CommandText = "SELECT Id, Name FROM Categories";
            using var catReader = catCmd.ExecuteReader();
            while (catReader.Read())
            {
                _categoriesCache[catReader.GetString(0)] = new GeneratedCategory
                {
                    Id = catReader.GetString(0),
                    Name = catReader.GetString(1),
                    Problems = []
                };
            }

            // Load problems
            using var probCmd = _connection.CreateCommand();
            probCmd.CommandText = "SELECT Id, CategoryId, OneLineStatement, FullPrompt, ExpectedOutput FROM Problems";
            using var probReader = probCmd.ExecuteReader();
            while (probReader.Read())
            {
                _problemsCache[probReader.GetString(0)] = new GeneratedProblem
                {
                    Id = probReader.GetString(0),
                    CategoryId = probReader.GetString(1),
                    OneLineStatement = probReader.GetString(2),
                    FullPrompt = probReader.IsDBNull(3) ? null : probReader.GetString(3),
                    ExpectedOutput = probReader.IsDBNull(4) ? null : probReader.GetString(4)
                };
            }

            // Group problems by category
            var problemsByCategory = _problemsCache.Values.GroupBy(p => p.CategoryId)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var category in _categoriesCache.Values)
            {
                if (problemsByCategory.TryGetValue(category.Id, out var problems))
                {
                    category.Problems = problems;
                }
            }
        }
        catch
        {
            // Ignore cache load errors, will just start fresh
        }
    }

    private void InitializeDatabase()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Categories (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Problems (
                Id TEXT PRIMARY KEY,
                CategoryId TEXT NOT NULL,
                OneLineStatement TEXT NOT NULL,
                FullPrompt TEXT,
                ExpectedOutput TEXT,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT,
                FOREIGN KEY (CategoryId) REFERENCES Categories(Id)
            );

            CREATE TABLE IF NOT EXISTS StartupParameters (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Checkpoint (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_Problems_CategoryId ON Problems(CategoryId);
            CREATE INDEX IF NOT EXISTS IX_Problems_Complete ON Problems(Id) WHERE FullPrompt IS NOT NULL AND ExpectedOutput IS NOT NULL;
            """;
        cmd.ExecuteNonQuery();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        // Database is already initialized in constructor
        await Task.CompletedTask;
    }

    /// <summary>
    /// Saves the startup configuration parameters for resume capability.
    /// </summary>
    public async Task SaveStartupParametersAsync(EvalGenConfig config, JudgeConfig? judgeConfig, CancellationToken cancellationToken)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO StartupParameters (Key, Value) VALUES
            ('Id', @id),
            ('RunName', @runName),
            ('OutputDirectoryPath', @outputDir),
            ('TargetCategoryCount', @targetCategories),
            ('TargetProblemsPerCategory', @targetProblems),
            ('DomainPrompt', @domainPrompt),
            ('ContextPrompt', @contextPrompt),
            ('SystemPrompt', @systemPrompt),
            ('Phase1PromptTemplate', @phase1Prompt),
            ('Phase2PromptTemplate', @phase2Prompt),
            ('Phase3PromptTemplate', @phase3Prompt),
            ('JudgeConfigJson', @judgeConfigJson)
            """;

        cmd.Parameters.AddWithValue("@id", config.Id);
        cmd.Parameters.AddWithValue("@runName", config.RunName ?? "");
        cmd.Parameters.AddWithValue("@outputDir", config.OutputDirectoryPath ?? "");
        cmd.Parameters.AddWithValue("@targetCategories", config.TargetCategoryCount);
        cmd.Parameters.AddWithValue("@targetProblems", config.TargetProblemsPerCategory);
        cmd.Parameters.AddWithValue("@domainPrompt", config.DomainPrompt ?? "");
        cmd.Parameters.AddWithValue("@contextPrompt", config.ContextPrompt ?? "");
        cmd.Parameters.AddWithValue("@systemPrompt", config.SystemPrompt ?? "");
        cmd.Parameters.AddWithValue("@phase1Prompt", config.Phase1PromptTemplate ?? EvalGenService.DefaultPhase1Prompt);
        cmd.Parameters.AddWithValue("@phase2Prompt", config.Phase2PromptTemplate ?? EvalGenService.DefaultPhase2Prompt);
        cmd.Parameters.AddWithValue("@phase3Prompt", config.Phase3PromptTemplate ?? EvalGenService.DefaultPhase3Prompt);

        // Save complete JudgeConfig as JSON to preserve all settings including model path
        var judgeConfigJson = JsonSerializer.Serialize(judgeConfig, new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
        cmd.Parameters.AddWithValue("@judgeConfigJson", judgeConfigJson);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Loads startup parameters from a previous run.
    /// Returns null if no previous run found.
    /// </summary>
    public async Task<(EvalGenConfig Config, JudgeConfig? JudgeConfig)?> LoadStartupParametersAsync(CancellationToken cancellationToken)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT Key, Value FROM StartupParameters";

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var values = new Dictionary<string, string>();

        while (await reader.ReadAsync(cancellationToken))
        {
            values[reader.GetString(0)] = reader.GetString(1);
        }

        if (!values.ContainsKey("Id"))
            return null;

        // Helper to convert empty strings to null
        static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;
        static double ParseDouble(string? s, double defaultValue) => double.TryParse(s, out var d) ? d : defaultValue;
        static int ParseInt(string? s, int defaultValue) => int.TryParse(s, out var i) ? i : defaultValue;
        static bool ParseBool(string? s, bool defaultValue) => bool.TryParse(s, out var b) ? b : defaultValue;

        var evalConfig = new EvalGenConfig
        {
            Id = values.GetValueOrDefault("Id", Guid.NewGuid().ToString()),
            RunName = values.GetValueOrDefault("RunName", ""),
            OutputDirectoryPath = values.GetValueOrDefault("OutputDirectoryPath", ""),
            TargetCategoryCount = ParseInt(values.GetValueOrDefault("TargetCategoryCount", "0"), 10),
            TargetProblemsPerCategory = ParseInt(values.GetValueOrDefault("TargetProblemsPerCategory", "0"), 5),
            DomainPrompt = NullIfEmpty(values.GetValueOrDefault("DomainPrompt", "")),
            ContextPrompt = NullIfEmpty(values.GetValueOrDefault("ContextPrompt", "")),
            SystemPrompt = NullIfEmpty(values.GetValueOrDefault("SystemPrompt", "")),
            Phase1PromptTemplate = NullIfEmpty(values.GetValueOrDefault("Phase1PromptTemplate", "")),
            Phase2PromptTemplate = NullIfEmpty(values.GetValueOrDefault("Phase2PromptTemplate", "")),
            Phase3PromptTemplate = NullIfEmpty(values.GetValueOrDefault("Phase3PromptTemplate", "")),
            ContinueFromCheckpoint = true,
            CheckpointDatabasePath = _connection.DataSource
        };

        // Load JudgeConfig from JSON (preserves all settings including model path)
        JudgeConfig? judgeConfig = null;
        if (values.TryGetValue("JudgeConfigJson", out var judgeConfigJson) && !string.IsNullOrEmpty(judgeConfigJson))
        {
            try
            {
                judgeConfig = JsonSerializer.Deserialize<JudgeConfig>(judgeConfigJson);
                // Ensure ServerConfig and ServerSettings are not null
                if (judgeConfig != null)
                {
                    judgeConfig = judgeConfig with
                    {
                        ServerConfig = judgeConfig.ServerConfig ?? new ServerConfig(),
                        ServerSettings = judgeConfig.ServerSettings ?? new LlamaServerSettings()
                    };
                }
            }
            catch
            {
                // If JSON deserialization fails, fall back to parsing individual fields (legacy support)
                judgeConfig = new JudgeConfig
                {
                    ServerConfig = new ServerConfig
                    {
                        Manage = ParseBool(values.GetValueOrDefault("JudgeManage", "false"), false),
                        BaseUrl = NullIfEmpty(values.GetValueOrDefault("JudgeBaseUrl", "http://localhost:8081"))
                    },
                    ServerSettings = new LlamaServerSettings
                    {
                        SamplingTemperature = ParseDouble(values.GetValueOrDefault("JudgeTemperature", "0.7"), 0.7),
                        ContextWindowTokens = ParseInt(values.GetValueOrDefault("JudgeMaxTokens", "0"), 4096),
                        ParallelSlotCount = ParseInt(values.GetValueOrDefault("JudgeParallelSlotCount", "0"), 4)
                    }
                };
            }
        }

        return (evalConfig, judgeConfig);
    }

    /// <summary>
    /// Saves a generated category.
    /// </summary>
    public async Task SaveCategoryAsync(GeneratedCategory category, CancellationToken cancellationToken)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO Categories (Id, Name, CreatedAt)
            VALUES (@id, @name, @createdAt)
            """;

        cmd.Parameters.AddWithValue("@id", category.Id);
        cmd.Parameters.AddWithValue("@name", category.Name);
        cmd.Parameters.AddWithValue("@createdAt", DateTimeOffset.Now.ToString("O"));

        await cmd.ExecuteNonQueryAsync(cancellationToken);

        // Update cache
        _categoriesCache[category.Id] = category;
    }

    /// <summary>
    /// Gets all categories from cache (fast, no database access).
    /// </summary>
    public List<GeneratedCategory> GetCategories()
    {
        // Update problems for each category from cache
        var problemsByCategory = _problemsCache.Values.GroupBy(p => p.CategoryId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var categories = _categoriesCache.Values.ToList();
        foreach (var category in categories)
        {
            if (problemsByCategory.TryGetValue(category.Id, out var problems))
            {
                category.Problems = problems;
            }
        }
        return categories;
    }

    /// <summary>
    /// Loads all categories from database (slow, use only for initial load).
    /// </summary>
    public async Task<List<GeneratedCategory>> LoadCategoriesAsync(CancellationToken cancellationToken)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT Id, Name FROM Categories ORDER BY CreatedAt";

        var categories = new List<GeneratedCategory>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            categories.Add(new GeneratedCategory
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                Problems = []
            });
        }

        return categories;
    }

    /// <summary>
    /// Saves a problem (creates or updates).
    /// </summary>
    public async Task SaveProblemAsync(GeneratedProblem problem, CancellationToken cancellationToken)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO Problems (Id, CategoryId, OneLineStatement, FullPrompt, ExpectedOutput, CreatedAt, UpdatedAt)
            VALUES (@id, @categoryId, @statement, @fullPrompt, @expectedOutput, @createdAt, @updatedAt)
            """;

        cmd.Parameters.AddWithValue("@id", problem.Id);
        cmd.Parameters.AddWithValue("@categoryId", problem.CategoryId);
        cmd.Parameters.AddWithValue("@statement", problem.OneLineStatement);
        cmd.Parameters.AddWithValue("@fullPrompt", problem.FullPrompt ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@expectedOutput", problem.ExpectedOutput ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@createdAt", DateTimeOffset.Now.ToString("O"));
        cmd.Parameters.AddWithValue("@updatedAt", problem.IsComplete ? DateTimeOffset.Now.ToString("O") : DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);

        // Update cache
        _problemsCache[problem.Id] = problem;
    }

    /// <summary>
    /// Gets all problems from cache (fast, no database access).
    /// </summary>
    public List<GeneratedProblem> GetProblems()
    {
        return _problemsCache.Values.ToList();
    }

    /// <summary>
    /// Loads all problems from database (slow, use only for initial load).
    /// </summary>
    public async Task<List<GeneratedProblem>> LoadProblemsAsync(CancellationToken cancellationToken)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, CategoryId, OneLineStatement, FullPrompt, ExpectedOutput
            FROM Problems
            ORDER BY CreatedAt
            """;

        var problems = new List<GeneratedProblem>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            problems.Add(new GeneratedProblem
            {
                Id = reader.GetString(0),
                CategoryId = reader.GetString(1),
                OneLineStatement = reader.GetString(2),
                FullPrompt = reader.IsDBNull(3) ? null : reader.GetString(3),
                ExpectedOutput = reader.IsDBNull(4) ? null : reader.GetString(4)
            });
        }

        return problems;
    }

    /// <summary>
    /// Saves a checkpoint value for custom state tracking.
    /// </summary>
    public async Task SaveCheckpointAsync(string key, string value, CancellationToken cancellationToken)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO Checkpoint (Key, Value) VALUES (@key, @value)";
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Loads a checkpoint value.
    /// </summary>
    public async Task<string?> LoadCheckpointAsync(string key, CancellationToken cancellationToken)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT Value FROM Checkpoint WHERE Key = @key";
        cmd.Parameters.AddWithValue("@key", key);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result as string;
    }

    /// <summary>
    /// Gets the count of completed problems (those with FullPrompt and ExpectedOutput).
    /// </summary>
    public async Task<int> GetCompletedProblemCountAsync(CancellationToken cancellationToken)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM Problems 
            WHERE FullPrompt IS NOT NULL AND ExpectedOutput IS NOT NULL
            """;
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    /// <summary>
    /// Gets the count of problems by category.
    /// </summary>
    public async Task<Dictionary<string, int>> GetProblemCountsByCategoryAsync(CancellationToken cancellationToken)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT CategoryId, COUNT(*) as Count
            FROM Problems
            GROUP BY CategoryId
            """;

        var counts = new Dictionary<string, int>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            counts[reader.GetString(0)] = reader.GetInt32(1);
        }

        return counts;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            if (_connection.State == System.Data.ConnectionState.Open)
            {
                await _connection.CloseAsync();
                await _connection.DisposeAsync();
            }

            // Clear the SQLite connection pool to ensure all connections are released
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        }
    }
}
