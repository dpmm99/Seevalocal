using Microsoft.Extensions.Logging;
using Seevalocal.Server.Client;
using Seevalocal.Server.Models;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace Seevalocal.UI.Commands;

/// <summary>
/// Implements the 'server check' command: checks if a URL is a healthy llama-server.
/// </summary>
public sealed class ServerCheckCommand(
    ILogger<ServerCheckCommand> logger,
    IAnsiConsole console,
    ILoggerFactory loggerFactory) : AsyncCommand<ServerCheckCommandSettings>
{
    private readonly ILogger<ServerCheckCommand> _logger = logger;
    private readonly IAnsiConsole _console = console;
    private readonly ILoggerFactory _loggerFactory = loggerFactory;

    public override async Task<int> ExecuteAsync(CommandContext context, ServerCheckCommandSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrEmpty(settings.Url))
            {
                _console.MarkupLine("[red]No URL specified. Use --url <url>.[/]");
                return 1;
            }

            _console.MarkupLine($"[bold]Checking server health: {settings.Url}[/]");

            var info = new ServerInfo
            {
                BaseUrl = settings.Url,
                ApiKey = settings.ApiKey,
                TotalSlots = 4,
            };

            var httpClient = new HttpClient { BaseAddress = new Uri(settings.Url), Timeout = TimeSpan.FromHours(6) };
            if (settings.ApiKey != null)
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {settings.ApiKey}");

            var clientLogger = _loggerFactory.CreateLogger<LlamaServerClient>();
            var client = new LlamaServerClient(info, httpClient, clientLogger);

            var healthResult = await client.GetHealthAsync(cancellationToken);

            if (healthResult.IsFailed)
            {
                _console.MarkupLine($"[red]Server is not healthy: {healthResult.Errors.FirstOrDefault()?.Message}[/]");
                return 1;
            }

            var health = healthResult.Value;
            _console.MarkupLine("[bold green]Server is healthy![/]");
            _console.MarkupLine($"  Status: {health.Status}");
            _console.MarkupLine($"  Slots: {health.SlotsTotal} total, {health.SlotsProcessing} processing, {health.SlotsDeferred} deferred");

            // Also get props
            var propsResult = await client.GetPropsAsync(cancellationToken);
            if (propsResult.IsSuccess)
            {
                var props = propsResult.Value;
                _console.MarkupLine($"  Total slots: {props.TotalSlots}");
                _console.MarkupLine($"  Model path: {props.ModelPath}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            _console.MarkupLine($"[red]Error: {ex.Message}[/]");
            _logger.LogError(ex, "Error in ServerCheckCommand");
            return 1;
        }
    }
}

public sealed class ServerCheckCommandSettings : CommandSettings
{
    [CommandOption("--url")]
    [Description("Server URL to check")]
    public string? Url { get; set; }

    [CommandOption("--api-key")]
    [Description("API key for authentication")]
    public string? ApiKey { get; set; }
}
