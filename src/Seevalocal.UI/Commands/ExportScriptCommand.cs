using Microsoft.Extensions.Logging;
using Seevalocal.Config.Export;
using Seevalocal.Config.Loading;
using Seevalocal.Config.Merging;
using Seevalocal.Core.Models;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace Seevalocal.UI.Commands;

/// <summary>
/// Implements the 'export-script' command: exports a shell script from a settings file.
/// </summary>
public sealed class ExportScriptCommand(
    ILogger<ExportScriptCommand> logger,
    IAnsiConsole console,
    SettingsFileLoader settingsFileLoader,
    ConfigurationMerger configurationMerger,
    ShellScriptExporter exporter) : AsyncCommand<ExportScriptCommandSettings>
{
    private readonly ILogger<ExportScriptCommand> _logger = logger;
    private readonly IAnsiConsole _console = console;
    private readonly SettingsFileLoader _settingsFileLoader = settingsFileLoader;
    private readonly ConfigurationMerger _configurationMerger = configurationMerger;
    private readonly ShellScriptExporter _exporter = exporter;

    public override async Task<int> ExecuteAsync(CommandContext context, ExportScriptCommandSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            if (settings.SettingsFile == null)
            {
                _console.MarkupLine("[red]No settings file specified. Use --settings <file>.[/]");
                return 1;
            }

            if (!File.Exists(settings.SettingsFile))
            {
                _console.MarkupLine($"[red]Settings file not found: {settings.SettingsFile}[/]");
                return 1;
            }

            var loadResult = await _settingsFileLoader.LoadAsync(settings.SettingsFile, cancellationToken);
            if (loadResult.IsFailed)
            {
                _console.MarkupLine("[red]Failed to load settings file.[/]");
                return 1;
            }

            var resolved = _configurationMerger.Merge([loadResult.Value]);

            var target = settings.Shell?.ToLowerInvariant() switch
            {
                "powershell" or "ps" or "ps1" => ShellTarget.PowerShell,
                _ => ShellTarget.Bash
            };

            var script = _exporter.Export(resolved, target);

            if (!string.IsNullOrEmpty(settings.OutputFile))
            {
                File.WriteAllText(settings.OutputFile, script);
                _console.MarkupLine($"[bold green]Script exported to: {settings.OutputFile}[/]");
            }
            else
            {
                _console.WriteLine(script);
            }

            return 0;
        }
        catch (Exception ex)
        {
            _console.MarkupLine($"[red]Error: {ex.Message}[/]");
            _logger.LogError(ex, "Error in ExportScriptCommand");
            return 1;
        }
    }
}

public sealed class ExportScriptCommandSettings : CommandSettings
{
    [CommandOption("--settings")]
    [Description("Settings file to export from")]
    public string? SettingsFile { get; set; }

    [CommandOption("--shell")]
    [Description("Shell dialect: bash (default) or powershell")]
    public string? Shell { get; set; }

    [CommandOption("--output")]
    [Description("Output file path (default: stdout)")]
    public string? OutputFile { get; set; }
}
