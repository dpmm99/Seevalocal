using FluentResults;
using Microsoft.Extensions.Logging;
using Seevalocal.Core;
using Seevalocal.DataSources.Internal;
using Seevalocal.DataSources.Sources;

namespace Seevalocal.DataSources;

/// <summary>
/// Creates IDataSource instances from DataSourceConfig.
/// </summary>
public sealed class DataSourceFactory(ILoggerFactory loggerFactory)
{
    private readonly ILoggerFactory _loggerFactory = loggerFactory;
    private readonly PromptTemplateEngine _templateEngine = new();

    /// <summary>
    /// Creates an IDataSource from the given config.
    /// The returned data source is wrapped with template, shuffle, limit, and duplicate checks.
    /// </summary>
    /// <param name="name">Human-readable name for this data source (used in logging and auto-IDs).</param>
    /// <param name="config">The data source configuration.</param>
    /// <returns>Result containing the IDataSource, or failure with error message.</returns>
    public Result<IDataSource> Create(string name, DataSourceConfig config)
    {
        var validation = DataSourceValidator.Validate(config);
        if (validation.IsFailed)
            return Result.Fail<IDataSource>(validation.Errors);

        var logger = _loggerFactory.CreateLogger<DataSourceFactory>();
        IDataSource inner = config.Kind switch
        {
            DataSourceKind.Directory or DataSourceKind.SplitDirectories
                => new DirectoryDataSource(name, config, logger),
            DataSourceKind.JsonFile
                => new JsonDataSource(name, config, isJsonl: false, logger),
            DataSourceKind.JsonlFile
                => new JsonDataSource(name, config, isJsonl: true, logger),
            DataSourceKind.YamlFile
                => new YamlDataSource(name, config, logger),
            DataSourceKind.CsvFile
                => new CsvDataSource(name, config, logger),
            DataSourceKind.ParquetFile
                => new ParquetDataSource(name, config, logger),
            DataSourceKind.InlineList
                => new InlineDataSource(name, config),
            _ => throw new ArgumentOutOfRangeException(nameof(config.Kind), config.Kind, "Unknown DataSourceKind")
        };

        var wrapped = new WrappedDataSource(inner, config, _templateEngine);
        return Result.Ok<IDataSource>(wrapped);
    }
}
