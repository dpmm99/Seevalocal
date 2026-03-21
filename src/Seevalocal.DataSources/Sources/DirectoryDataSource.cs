using Microsoft.Extensions.Logging;
using Seevalocal.Core;
using Seevalocal.Core.Models;

namespace Seevalocal.DataSources.Sources;

/// <summary>
/// Loads EvalItems from a directory of prompt files, optionally paired with
/// an expected-output directory and a system prompt file.
/// </summary>
internal sealed class DirectoryDataSource(string name, DataSourceConfig config, ILogger logger) : IDataSource
{
    private readonly DataSourceConfig _config = config;
    private readonly ILogger _logger = logger;

    public string Name { get; } = name;

    public async IAsyncEnumerable<EvalItem> GetItemsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var promptDir = Path.GetFullPath(_config.PromptDirectory!);
        var promptFiles = Directory.GetFiles(promptDir, _config.FileExtensionFilter)
            .Order()
            .ToArray();

        string? globalSystemPrompt = null;
        if (_config.SystemPromptFilePath is not null)
        {
            var sysPath = Path.GetFullPath(_config.SystemPromptFilePath);
            globalSystemPrompt = await File.ReadAllTextAsync(sysPath, ct);
            _logger.LogDebug("[{Name}] Loaded system prompt from {Path}", Name, sysPath);
        }
        else if (_config.DefaultSystemPrompt is not null)
        {
            globalSystemPrompt = _config.DefaultSystemPrompt;
        }

        var index = 0;
        foreach (var promptFile in promptFiles)
        {
            ct.ThrowIfCancellationRequested();

            var stem = Path.GetFileNameWithoutExtension(promptFile);
            var userPrompt = await File.ReadAllTextAsync(promptFile, ct);

            string? expectedOutput = null;
            if (_config.ExpectedDirectory is not null)
            {
                var expectedDir = Path.GetFullPath(_config.ExpectedDirectory);
                // Try the same extension first, then any matching stem
                var expectedFile = Directory.GetFiles(expectedDir, $"{stem}.*")
                    .FirstOrDefault();
                if (expectedFile is not null)
                    expectedOutput = await File.ReadAllTextAsync(expectedFile, ct);
            }

            yield return new EvalItem
            {
                Id = stem,
                SystemPrompt = globalSystemPrompt,
                UserPrompt = userPrompt,
                ExpectedOutput = expectedOutput,
            };

            index++;
        }
    }

    public Task<int?> GetCountAsync(CancellationToken ct)
    {
        if (_config.PromptDirectory is null)
            return Task.FromResult<int?>(null);

        var promptDir = Path.GetFullPath(_config.PromptDirectory);
        if (!Directory.Exists(promptDir))
            return Task.FromResult<int?>(null);

        var count = Directory.GetFiles(promptDir, _config.FileExtensionFilter).Length;
        return Task.FromResult<int?>(count);
    }
}
