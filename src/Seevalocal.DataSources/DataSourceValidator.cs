using FluentResults;

namespace Seevalocal.DataSources;

/// <summary>
/// Validates a DataSourceConfig before use.
/// </summary>
public static class DataSourceValidator
{
    public static Result Validate(DataSourceConfig config)
    {
        List<string> errors = [];

        switch (config.Kind)
        {
            case DataSourceKind.Directory:
            case DataSourceKind.SplitDirectories:
                if (string.IsNullOrWhiteSpace(config.PromptDirectoryPath))
                {
                    errors.Add("[DataSourceValidator] PromptDirectoryPath is required for Directory/SplitDirectories kind");
                    break;
                }
                var promptDir = Path.GetFullPath(config.PromptDirectoryPath);
                if (!Directory.Exists(promptDir))
                    errors.Add($"[DataSourceValidator] PromptDirectoryPath does not exist: {promptDir}");

                if (config.ExpectedOutputDirectoryPath is not null)
                {
                    var expDir = Path.GetFullPath(config.ExpectedOutputDirectoryPath);
                    if (!Directory.Exists(expDir))
                        errors.Add($"[DataSourceValidator] ExpectedOutputDirectoryPath does not exist: {expDir}");
                }
                if (config.SystemPromptFilePath is not null)
                {
                    var sysFile = Path.GetFullPath(config.SystemPromptFilePath);
                    if (!File.Exists(sysFile))
                        errors.Add($"[DataSourceValidator] SystemPromptFilePath does not exist: {sysFile}");
                }
                break;

            case DataSourceKind.JsonFile:
            case DataSourceKind.JsonlFile:
            case DataSourceKind.YamlFile:
            case DataSourceKind.CsvFile:
            case DataSourceKind.ParquetFile:
                if (string.IsNullOrWhiteSpace(config.DataFilePath))
                {
                    errors.Add($"[DataSourceValidator] DataFilePath is required for kind {config.Kind}");
                    break;
                }
                var dataFile = Path.GetFullPath(config.DataFilePath);
                if (!File.Exists(dataFile))
                    errors.Add($"[DataSourceValidator] DataFilePath does not exist: {dataFile}");
                break;

            case DataSourceKind.InlineList:
                if (config.InlineItems is null || config.InlineItems.Count == 0)
                    errors.Add("[DataSourceValidator] InlineList kind requires at least one item in InlineItems");
                break;
        }

        if (config.PromptTemplate?.Contains("{prompt}") == false)
            errors.Add("[DataSourceValidator] PromptTemplate must contain the {prompt} placeholder");

        return errors.Count > 0 ? Result.Fail(errors) : Result.Ok();
    }
}
