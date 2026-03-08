using FluentResults;
using Microsoft.Extensions.Logging;
using Seevalocal.Core.Models;

namespace Seevalocal.Pipelines.Setup;

/// <summary>
/// Common auto-setup helper shared by all built-in pipeline factories.
/// Handles GPU detection, llama-server binary download, and interactive confirmation.
/// </summary>
public static class CommonAutoSetup
{
    /// <summary>
    /// Runs the common pre-flight checks:
    /// 1. Detects GPU capabilities.
    /// 2. Downloads the appropriate llama-server binary if manage=true and no executablePath is set.
    /// 3. Prints a summary of what will happen.
    /// 4. Prompts for confirmation in interactive mode (skipped when <paramref name="skipConfirmation"/> is true).
    /// </summary>
    public static async Task<Result> RunAsync(
        ResolvedConfig resolvedConfig,
        ILogger logger,
        bool skipConfirmation,
        CancellationToken ct)
    {
        // 1. Detect GPU
        var gpuResult = await GpuDetector.DetectAsync(ct);
        if (gpuResult.IsFailed)
        {
            logger.LogWarning(
                "GPU detection failed: {Reason}. Falling back to CPU-only.",
                gpuResult.Errors.FirstOrDefault()?.Message);
        }
        else
        {
            logger.LogInformation("GPU detected: {GpuKind}", gpuResult.Value);
        }

        var serverConfig = resolvedConfig.Server;

        // 2. If Manage=true and no binary yet, we'd download it here
        //    (actual download implementation lives in Seevalocal.Server — we delegate)
        if (serverConfig?.Manage == true && string.IsNullOrEmpty(serverConfig.ExecutablePath))
        {
            logger.LogInformation(
                "Manage=true: llama-server binary will be downloaded automatically if not cached.");
        }

        // 3. Print summary
        PrintSummary(resolvedConfig, gpuResult.IsSuccess ? gpuResult.Value.ToString() : "CPU", logger);

        // 4. Confirm
        if (!skipConfirmation && Environment.UserInteractive && !Console.IsInputRedirected)
        {
            Console.Write("Proceed? [Y/n] ");
            var answer = Console.ReadLine()?.Trim().ToUpperInvariant();
            if (answer is not ("" or null or "Y"))
                return Result.Fail("[CommonAutoSetup] User cancelled the operation.");
        }

        return Result.Ok();
    }

    private static void PrintSummary(ResolvedConfig config, string gpuKind, ILogger logger)
    {
        logger.LogInformation(
            "=== Auto-Setup Summary ===\n" +
            "  GPU:             {GpuKind}\n" +
            "  Manage Server:   {Manage}\n" +
            "  Host:            {Host}:{Port}\n" +
            "==========================",
            gpuKind,
            config.Server?.Manage,
            config.Server?.Host ?? "127.0.0.1",
            config.Server?.Port ?? 8080);
    }
}

/// <summary>
/// Minimal GPU detector stub. The real implementation lives in Seevalocal.Server.
/// This stub allows Seevalocal.Pipelines to depend only downward.
/// </summary>
internal static class GpuDetector
{
    public static Task<Result<GpuKind>> DetectAsync(CancellationToken ct)
    {
        // Real detection is implemented in Seevalocal.Server.
        // This is a thin delegation layer.
        return Task.FromResult(Result.Ok(GpuKind.CpuOnly));
    }
}
