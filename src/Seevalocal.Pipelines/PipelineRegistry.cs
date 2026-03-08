using Seevalocal.Core.Pipeline;
using Seevalocal.Pipelines.Factories;

namespace Seevalocal.Pipelines;

/// <summary>
/// Central registry for all built-in pipeline factories.
/// Keyed by <see cref="IBuiltinPipelineFactory.PipelineName"/>.
/// </summary>
public sealed class PipelineRegistry(IEnumerable<IBuiltinPipelineFactory> factories)
{
    private readonly Dictionary<string, IBuiltinPipelineFactory> _factories = factories.ToDictionary(static f => f.PipelineName, StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the factory for the given pipeline name.</summary>
    /// <exception cref="InvalidOperationException">Thrown when name is unknown.</exception>
    public IBuiltinPipelineFactory Get(string pipelineName)
    {
        if (_factories.TryGetValue(pipelineName, out var factory))
            return factory;

        var known = string.Join(", ", _factories.Keys.Order());
        throw new InvalidOperationException(
            $"[PipelineRegistry] Unknown pipeline name '{pipelineName}'. Known pipelines: {known}");
    }

    public bool TryGet(string pipelineName, out IBuiltinPipelineFactory? factory)
        => _factories.TryGetValue(pipelineName, out factory);

    public IReadOnlyCollection<IBuiltinPipelineFactory> All => _factories.Values;

    /// <summary>Creates a default registry with all three built-in pipelines.</summary>
    public static PipelineRegistry CreateDefault(
        Microsoft.Extensions.Logging.ILoggerFactory loggerFactory)
    {
        return new PipelineRegistry(
        [
            new TranslationPipelineFactory(loggerFactory),
            new CSharpCodingPipelineFactory(loggerFactory),
            new CasualQAPipelineFactory(loggerFactory),
        ]);
    }
}
