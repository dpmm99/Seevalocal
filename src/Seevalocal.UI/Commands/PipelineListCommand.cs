using Microsoft.Extensions.Logging;
using Seevalocal.Core.Pipeline;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Seevalocal.UI.Commands;

/// <summary>
/// Implements the 'pipeline list' command: lists registered pipeline names and descriptions.
/// </summary>
public sealed class PipelineListCommand(
    ILogger<PipelineListCommand> logger,
    IAnsiConsole console,
    IEnumerable<IBuiltinPipelineFactory> factories) : Command<PipelineListCommandSettings>
{
    private readonly IAnsiConsole _console = console;
    private readonly IEnumerable<IBuiltinPipelineFactory> _factories = factories;

    public override int Execute(CommandContext context, PipelineListCommandSettings settings, CancellationToken cancellationToken)
    {
        var factoriesList = _factories.ToList();

        _console.MarkupLine("[bold]Registered Pipelines:[/]");
        _console.WriteLine();

        var table = new Table();
        _ = table.AddColumn("Name");
        _ = table.AddColumn("Description");

        foreach (var factory in factoriesList)
        {
            _ = table.AddRow(factory.PipelineName, factory.Description);
        }

        _console.Write(table);

        return 0;
    }
}

public sealed class PipelineListCommandSettings : CommandSettings
{
}
