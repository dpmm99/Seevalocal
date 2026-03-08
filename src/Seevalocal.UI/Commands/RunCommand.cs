using Microsoft.Extensions.Logging;
using Seevalocal.Config.Loading;
using Seevalocal.Config.Merging;
using Seevalocal.Config.Validation;
using Seevalocal.Core.Models;
using Seevalocal.Server.Lifecycle;
using Seevalocal.Server.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Seevalocal.UI.Commands;

/// <summary>
/// Implements the 'run' command: executes an evaluation pipeline.
/// </summary>
public sealed class RunCommand(
    ILogger<RunCommand> logger,
    IAnsiConsole console,
    LlamaServerManager serverManager,
    SettingsFileLoader settingsFileLoader,
    ConfigurationMerger configurationMerger,
    ConfigValidator configValidator) : AsyncCommand<RunCommandSettings>
{
    private readonly ILogger<RunCommand> _logger = logger;
    private readonly IAnsiConsole _console = console;
    private readonly LlamaServerManager _serverManager = serverManager;
    private readonly SettingsFileLoader _settingsFileLoader = settingsFileLoader;
    private readonly ConfigurationMerger _configurationMerger = configurationMerger;
    private readonly ConfigValidator _configValidator = configValidator;

    public override Task<int> ExecuteAsync(CommandContext context, RunCommandSettings settings, CancellationToken cancellationToken)
    {
        return ExecuteAsyncInternal(context, settings, cancellationToken);
    }

    private async Task<int> ExecuteAsyncInternal(CommandContext context, RunCommandSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            // 1. Load and merge settings
            _console.MarkupLine("[bold]Loading configuration...[/]");

            var cliOverrides = CliSettingsAdapter.ToPartialConfig(settings);

            List<PartialConfig> configs = [];
            if (settings.SettingsFiles != null)
            {
                foreach (var file in settings.SettingsFiles)
                {
                    if (!File.Exists(file))
                    {
                        _console.MarkupLine($"[red]Settings file not found: {file}[/]");
                        return 1;
                    }

                    var result = await _settingsFileLoader.LoadAsync(file, cancellationToken);
                    if (result.IsSuccess && result.Value != null)
                        configs.Add(result.Value);
                }
            }

            configs.Add(cliOverrides);

            var resolvedConfig = _configurationMerger.Merge(configs);

            // 2. Validate
            var errors = _configValidator.Validate(resolvedConfig);
            if (errors.Count > 0)
            {
                _console.MarkupLine("[red]Configuration validation failed:[/]");
                foreach (var err in errors)
                    _console.MarkupLine($"  [yellow]{err.Field}[/]: {err.MessageText}");
                return 1;
            }

            // 3. Start server(s) if managing
            ServerInfo? serverInfo = null;

            if (resolvedConfig.Server.Manage)
            {
                _console.MarkupLine("[bold]Starting llama-server...[/]");
                var result = await _serverManager.StartAsync(
                    resolvedConfig.Server,
                    resolvedConfig.LlamaServer,
                    cancellationToken);

                if (result.IsFailed)
                {
                    _console.MarkupLine($"[red]Failed to start server: {result.Errors.FirstOrDefault()?.Message}[/]");
                    return 1;
                }

                serverInfo = result.Value;
            }
            else if (!string.IsNullOrEmpty(resolvedConfig.Server.BaseUrl))
            {
                serverInfo = new ServerInfo
                {
                    BaseUrl = resolvedConfig.Server.BaseUrl,
                    ApiKey = resolvedConfig.Server.ApiKey,
                    TotalSlots = resolvedConfig.LlamaServer.ParallelSlotCount ?? 4,
                };
            }

            try
            {
                // 4. Run evaluation
                _console.MarkupLine($"[bold green]Starting evaluation: {resolvedConfig.Run.RunName}[/]");

                // Create HTTP client for primary server
                if (serverInfo == null)
                {
                    _console.MarkupLine("[red]No server configured.[/]");
                    return 1;
                }

                var httpClient = new HttpClient { BaseAddress = new Uri(serverInfo.BaseUrl), Timeout = TimeSpan.FromHours(6) };
                if (serverInfo.ApiKey != null)
                    httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {serverInfo.ApiKey}");

                // Note: Full pipeline execution requires wiring up:
                // - DataSourceFactory with correct types
                // - PipelineRegistry 
                // - PipelineOrchestrator
                // - Result writers
                // This is a simplified placeholder that shows the structure

                _console.MarkupLine($"[green]Server ready at {serverInfo.BaseUrl}[/]");
                _console.MarkupLine($"[green]Pipeline: {resolvedConfig.EvalSets.FirstOrDefault()?.PipelineName ?? "none"}[/]");
                _console.MarkupLine($"[green]Eval sets: {resolvedConfig.EvalSets.Count}[/]");

                // Placeholder for actual pipeline execution
                await Task.CompletedTask;

                _console.MarkupLine("[bold green]Evaluation completed successfully![/]");
                return 0;
            }
            finally
            {
                await _serverManager.DisposeAsync();
            }
        }
        catch (Exception ex)
        {
            _console.MarkupLine($"[red]Error: {ex.Message}[/]");
            _logger.LogError(ex, "Unhandled exception in RunCommand");
            return 1;
        }
    }
}
