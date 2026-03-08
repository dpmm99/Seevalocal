using Microsoft.Extensions.Logging;
using Seevalocal.Core.Models;
using Seevalocal.Server;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace Seevalocal.UI.Commands;

/// <summary>
/// Implements the 'server start' command: starts llama-server for debugging.
/// </summary>
public sealed class ServerStartCommand(
    ILogger<ServerStartCommand> logger,
    IAnsiConsole console,
    LlamaServerManager manager) : AsyncCommand<ServerStartCommandSettings>
{
    private readonly ILogger<ServerStartCommand> _logger = logger;
    private readonly IAnsiConsole _console = console;
    private readonly LlamaServerManager _manager = manager;

    public override async Task<int> ExecuteAsync(CommandContext context, ServerStartCommandSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var config = new ServerConfig
            {
                Manage = true,
                ExecutablePath = settings.ExecutablePath,
                Host = settings.Host ?? "127.0.0.1",
                Port = settings.Port ?? 8080,
                Model = settings.ModelFilePath != null
                    ? new ModelSource { Kind = ModelSourceKind.LocalFile, FilePath = settings.ModelFilePath }
                    : settings.HfRepo != null
                    ? new ModelSource { Kind = ModelSourceKind.HuggingFace, HfRepo = settings.HfRepo }
                    : null,
            };

            var llamaSettings = new LlamaServerSettings
            {
                ContextWindowTokens = settings.ContextWindowTokens,
                ParallelSlotCount = settings.ParallelSlotCount,
                GpuLayerCount = settings.GpuLayerCount,
                SamplingTemperature = settings.SamplingTemperature,
            };

            _console.MarkupLine("[bold]Starting llama-server...[/]");

            var result = await _manager.StartAsync(config, llamaSettings, cancellationToken);

            if (result.IsFailed)
            {
                _console.MarkupLine($"[red]Failed to start server: {result.Errors.FirstOrDefault()?.Message}[/]");
                return 1;
            }

            var info = result.Value;
            _console.MarkupLine($"[bold green]Server started at {info.BaseUrl}[/]");
            _console.MarkupLine($"Model: {info.ModelAlias}");
            _console.MarkupLine($"Total slots: {info.TotalSlots}");
            _console.MarkupLine("[yellow]Press Ctrl+C to stop the server.[/]");

            // Wait for cancellation
            var tcs = new TaskCompletionSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                _ = tcs.TrySetResult();
            };

            await tcs.Task;

            await _manager.DisposeAsync();
            _console.MarkupLine("[bold]Server stopped.[/]");

            return 0;
        }
        catch (Exception ex)
        {
            _console.MarkupLine($"[red]Error: {ex.Message}[/]");
            _logger.LogError(ex, "Error in ServerStartCommand");
            return 1;
        }
    }
}

public sealed class ServerStartCommandSettings : CommandSettings
{ //TODO: SO many options are missing.
    [CommandOption("--executable")]
    [Description("Path to llama-server binary")]
    public string? ExecutablePath { get; set; }

    [CommandOption("--model-file")]
    [Description("Path to model file")]
    public string? ModelFilePath { get; set; }

    [CommandOption("--hf-repo")]
    [Description("HuggingFace repo")]
    public string? HfRepo { get; set; }

    [CommandOption("--host")]
    [Description("Host (default: 127.0.0.1)")]
    public string? Host { get; set; }

    [CommandOption("--port")]
    [Description("Port (default: 8080)")]
    public int? Port { get; set; }

    [CommandOption("--ctx")]
    [Description("Context window tokens")]
    public int? ContextWindowTokens { get; set; }

    [CommandOption("--parallel")]
    [Description("Parallel slots")]
    public int? ParallelSlotCount { get; set; }

    [CommandOption("--ngl")]
    [Description("GPU layers")]
    public int? GpuLayerCount { get; set; }

    [CommandOption("--temp")]
    [Description("Temperature")]
    public double? SamplingTemperature { get; set; }
}
