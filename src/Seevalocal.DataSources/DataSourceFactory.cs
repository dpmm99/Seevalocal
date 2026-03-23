using FluentResults;
using Microsoft.Extensions.Logging;
using Seevalocal.Core;
using Seevalocal.Core.Models;
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

        // Apply default field mapping for JSONL files if not specified
        var configToUse = config;
        if (config.Kind == DataSourceKind.JsonlFile && config.FieldMapping == null)
        {
            configToUse = config with { FieldMapping = FieldMapping.ForJsonl() };
        }

        IDataSource inner = configToUse.Kind switch
        {
            DataSourceKind.SplitDirectories or DataSourceKind.SplitDirectories or DataSourceKind.SplitDirectories
                => new DirectoryDataSource(name, configToUse, logger),
            DataSourceKind.SingleFile or DataSourceKind.SingleFile or DataSourceKind.JsonFile
                => new JsonDataSource(name, configToUse, isJsonl: false, logger),
            DataSourceKind.JsonlFile
                => new JsonDataSource(name, configToUse, isJsonl: true, logger),
            DataSourceKind.YamlFile
                => new YamlDataSource(name, configToUse, logger),
            DataSourceKind.CsvFile
                => new CsvDataSource(name, configToUse, logger),
            DataSourceKind.ParquetFile
                => new ParquetDataSource(name, configToUse, logger),
            DataSourceKind.InlineList
                => new InlineDataSource(name, configToUse),
            _ => throw new ArgumentOutOfRangeException(nameof(config.Kind), config.Kind, "Unknown DataSourceKind")
        };

        var wrapped = new WrappedDataSource(inner, configToUse, _templateEngine);
        return Result.Ok<IDataSource>(wrapped);
    }
}
