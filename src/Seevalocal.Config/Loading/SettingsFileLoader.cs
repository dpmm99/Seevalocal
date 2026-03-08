using FluentResults;
using Microsoft.Extensions.Logging;
using Seevalocal.Core.Models;
using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Seevalocal.Config.Loading;

/// <summary>
/// Loads a <see cref="PartialConfig"/> from a YAML, JSON, or TOML settings file.
/// Format is auto-detected by file extension.
/// </summary>
public sealed class SettingsFileLoader(ILogger<SettingsFileLoader> logger)
{
    private readonly ILogger<SettingsFileLoader> _logger = logger;

    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    /// <summary>
    /// Loads a <see cref="PartialConfig"/> from the given file path.
    /// </summary>
    public async Task<Result<PartialConfig>> LoadAsync(string filePath, CancellationToken ct = default)
    {
        var fullPath = Path.GetFullPath(filePath);
        _logger.LogDebug("Loading settings file: {FilePath}", fullPath);

        if (!File.Exists(fullPath))
        {
            _logger.LogError("[SettingsFileLoader] File not found: {FilePath}", fullPath);
            return Result.Fail($"[SettingsFileLoader] File not found: {fullPath}");
        }

        var ext = Path.GetExtension(fullPath).ToLowerInvariant();

        try
        {
            var content = await File.ReadAllTextAsync(fullPath, ct);

            var config = ext switch
            {
                ".yml" or ".yaml" => LoadYaml(content, fullPath),
                ".json" => LoadJson(content, fullPath),
                ".toml" => LoadToml(content, fullPath),
                _ => null,
            };

            if (config is null)
            {
                _logger.LogError("[SettingsFileLoader] Unsupported file format '{Ext}' for {FilePath}", ext, fullPath);
                return Result.Fail($"[SettingsFileLoader] Unsupported file format '{ext}': {fullPath}");
            }

            _logger.LogInformation("Loaded settings file: {FilePath}", fullPath);
            return Result.Ok(config);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SettingsFileLoader] Failed to read {FilePath}", fullPath);
            return Result.Fail($"[SettingsFileLoader] Failed to read {fullPath}: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------

    private static PartialConfig LoadYaml(string content, string filePath)
    {
        try
        {
            return YamlDeserializer.Deserialize<PartialConfig>(content) ?? new PartialConfig();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"YAML parse error in {filePath}: {ex.Message}", ex);
        }
    }

    private static PartialConfig LoadJson(string content, string filePath)
    {
        try
        {
            return JsonSerializer.Deserialize<PartialConfig>(content, JsonOptions) ?? new PartialConfig();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"JSON parse error in {filePath}: {ex.Message}", ex);
        }
    }

    private static PartialConfig LoadToml(string content, string filePath)
    {
        try
        {
            // Tomlet: parse TOML → intermediate object → serialize to JSON → deserialize to PartialConfig
            var tomlDoc = Tomlet.TomlParser.ParseFile(filePath);
            var jsonString = Tomlet.TomletMain.TomlStringFrom(tomlDoc);
            // Tomlet doesn't have direct-to-object; use JSON round-trip
            var intermediate = Tomlet.TomletMain.To<Dictionary<string, object>>(content);
            var json = JsonSerializer.Serialize(intermediate);
            return JsonSerializer.Deserialize<PartialConfig>(json, JsonOptions) ?? new PartialConfig();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"TOML parse error in {filePath}: {ex.Message}", ex);
        }
    }
}
