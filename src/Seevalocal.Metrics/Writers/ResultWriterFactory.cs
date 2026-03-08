using Microsoft.Extensions.Logging;
using Seevalocal.Core.Models;

namespace Seevalocal.Metrics.Writers;

/// <summary>
/// Factory that creates a <see cref="CompositeResultWriter"/> from an <see cref="OutputConfig"/>
/// and a run-specific output directory.
/// </summary>
public static class ResultWriterFactory
{
    public static CompositeResultWriter Create(
        OutputConfig config,
        string outputDir,
        ILoggerFactory? loggerFactory = null)
    {
        _ = Directory.CreateDirectory(outputDir);
        List<IResultWriter> writers = [];

        if (config.WritePerEvalJson || config.WriteSummaryJson)
        {
            writers.Add(new JsonResultWriter(
                outputDir,
                includeRawLlmResponse: config.IncludeRawLlmResponse,
                includeAllStageOutputs: config.IncludeAllStageOutputs,
                writePerEvalJson: config.WritePerEvalJson,
                writeSummaryJson: config.WriteSummaryJson,
                logger: loggerFactory?.CreateLogger<JsonResultWriter>()));
        }

        if (config.WriteSummaryCsv)
        {
            writers.Add(new CsvResultWriter(
                outputDir,
                writeSummaryCsv: true,
                logger: loggerFactory?.CreateLogger<CsvResultWriter>()));
        }

        if (config.WriteResultsParquet)
        {
            writers.Add(new ParquetResultWriter(
                outputDir,
                writeResultsParquet: true,
                logger: loggerFactory?.CreateLogger<ParquetResultWriter>()));
        }

        return new CompositeResultWriter(writers);
    }

    /// <summary>
    /// Generates the timestamped run directory name.
    /// Format: run-2025-03-01T12-00-00Z
    /// </summary>
    public static string CreateRunDirectoryName(DateTimeOffset timestamp)
    {
        var ts = timestamp.UtcDateTime.ToString("yyyy-MM-ddTHH-mm-ss") + "Z";
        return $"run-{ts}";
    }
}
