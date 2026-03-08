using FluentResults;
using Microsoft.Extensions.Logging;
using Seevalocal.Config.Loading;
using Seevalocal.Config.Merging;
using Seevalocal.Config.Validation;
using Seevalocal.Core.Models;

namespace Seevalocal.UI.Services;

/// <summary>
/// Default implementation of IConfigurationService.
/// </summary>
public sealed class DefaultConfigurationService(
    ILogger<DefaultConfigurationService> logger,
    SettingsFileLoader settingsFileLoader,
    ConfigurationMerger configurationMerger,
    ConfigValidator configValidator) : IConfigurationService
{
    private readonly ILogger<DefaultConfigurationService> _logger = logger;
    private readonly SettingsFileLoader _settingsFileLoader = settingsFileLoader;
    private readonly ConfigurationMerger _configurationMerger = configurationMerger;
    private readonly ConfigValidator _configValidator = configValidator;

    public async Task<ResolvedConfig> LoadAndMergeAsync(
        IReadOnlyList<string> settingsFilePaths,
        PartialConfig cliOverrides,
        CancellationToken cancellationToken)
    {
        List<PartialConfig> configs = [];

        // Load file-based configs
        foreach (var file in settingsFilePaths)
        {
            if (!File.Exists(file))
            {
                _logger.LogWarning("Settings file not found: {FilePath}", file);
                continue;
            }

            var result = await _settingsFileLoader.LoadAsync(file, cancellationToken);
            if (result.IsSuccess && result.Value != null)
            {
                configs.Add(result.Value);
                _logger.LogDebug("Loaded settings file: {FilePath}", file);
            }
            else
            {
                _logger.LogWarning("Failed to load settings file {FilePath}: {Error}", file, result.Errors.FirstOrDefault()?.Message);
            }
        }

        // Add CLI overrides as highest priority
        configs.Add(cliOverrides);

        // Merge
        var resolved = _configurationMerger.Merge(configs);
        _logger.LogInformation("Configuration merged from {FileCount} sources", configs.Count);

        return resolved;
    }

    public IReadOnlyList<ValidationError> Validate(ResolvedConfig config)
    {
        return _configValidator.Validate(config);
    }

    public async Task<Result<PartialConfig>> LoadPartialConfigAsync(string filePath, CancellationToken cancellationToken)
    {
        return await _settingsFileLoader.LoadAsync(filePath, cancellationToken);
    }

    public Result<ResolvedConfig> Resolve(IReadOnlyList<PartialConfig> partials)
    {
        try
        {
            var resolved = _configurationMerger.Merge(partials);
            return Result.Ok(resolved);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving configuration");
            return Result.Fail<ResolvedConfig>($"Error resolving configuration: {ex.Message}");
        }
    }
}
