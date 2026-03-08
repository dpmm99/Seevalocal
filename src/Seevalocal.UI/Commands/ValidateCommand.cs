using Microsoft.Extensions.Logging;
using Seevalocal.Config.Loading;
using Seevalocal.Config.Merging;
using Seevalocal.Config.Validation;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace Seevalocal.UI.Commands;

/// <summary>
/// Implements the 'validate' command: validates a settings file without running.
/// </summary>
public sealed class ValidateCommand(
    ILogger<ValidateCommand> logger,
    IAnsiConsole console,
    SettingsFileLoader settingsFileLoader,
    ConfigurationMerger configurationMerger,
    ConfigValidator configValidator) : AsyncCommand<ValidateCommandSettings>
{
    private readonly ILogger<ValidateCommand> _logger = logger;
    private readonly IAnsiConsole _console = console;
    private readonly SettingsFileLoader _settingsFileLoader = settingsFileLoader;
    private readonly ConfigurationMerger _configurationMerger = configurationMerger;
    private readonly ConfigValidator _configValidator = configValidator;

    public override async Task<int> ExecuteAsync(CommandContext context, ValidateCommandSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            if (settings.SettingsFile == null)
            {
                _console.MarkupLine("[red]No settings file specified.[/]");
                return 1;
            }

            if (!File.Exists(settings.SettingsFile))
            {
                _console.MarkupLine($"[red]Settings file not found: {settings.SettingsFile}[/]");
                return 1;
            }

            _console.MarkupLine($"[bold]Validating: {settings.SettingsFile}[/]");

            var loadResult = await _settingsFileLoader.LoadAsync(settings.SettingsFile, cancellationToken);
            if (loadResult.IsFailed)
            {
                _console.MarkupLine("[red]Failed to load settings file.[/]");
                return 1;
            }

            var resolved = _configurationMerger.Merge([loadResult.Value]);
            var errors = _configValidator.Validate(resolved);

            if (errors.Count == 0)
            {
                _console.MarkupLine("[bold green]Configuration is valid.[/]");
                return 0;
            }

            _console.MarkupLine("[red]Validation errors:[/]");
            foreach (var err in errors)
            {
                _console.MarkupLine($"  [yellow]{err.Field}[/]: {err.MessageText}");
            }

            return 1;
        }
        catch (Exception ex)
        {
            _console.MarkupLine($"[red]Error: {ex.Message}[/]");
            _logger.LogError(ex, "Error in ValidateCommand");
            return 1;
        }
    }
}

public sealed class ValidateCommandSettings : CommandSettings
{
    [CommandArgument(0, "[SETTINGS_FILE]")]
    [Description("Path to the settings file to validate")]
    public string? SettingsFile { get; set; }
}
