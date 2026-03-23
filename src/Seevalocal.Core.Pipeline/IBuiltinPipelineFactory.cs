using Seevalocal.Core.Models;

namespace Seevalocal.Core.Pipeline;

/// <summary>
/// Creates a specific built-in pipeline from configuration.
/// Registered by pipeline name in <see cref="PipelineRegistry"/>.
/// </summary>
public interface IBuiltinPipelineFactory
{
    string PipelineName { get; }
    string Description { get; }

    /// <summary>Builds an EvalPipeline from the merged config.</summary>
    EvalPipeline Create(ResolvedConfig resolvedConfig);

    /// <summary>
    /// Validates pipeline-specific options in ResolvedConfig.PipelineOptions.
    /// Returns empty list on success.
    /// </summary>
    IReadOnlyList<ValidationError> Validate(ResolvedConfig resolvedConfig);

    /// <summary>
    /// Returns the default DataSourceConfig for this pipeline type.
    /// Used when the user doesn't specify one.
    /// </summary>
    DataSourceConfig DefaultDataSourceConfig { get; }
}
