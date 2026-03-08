using FluentResults;
using Microsoft.Extensions.Logging;
using Seevalocal.Core.Models;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Seevalocal.Server.Download;

/// <summary>
/// Downloads and caches llama-server binaries from GitHub Releases.
/// </summary>
public sealed class LlamaServerDownloader(HttpClient httpClient, ILogger<LlamaServerDownloader> logger)
{
    private const string GitHubApiBase = "https://api.github.com/repos/ggml-org/llama.cpp";
    private const string UserAgent = "Seevalocal/1.0";

    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<LlamaServerDownloader> _logger = logger;

    /// <summary>
    /// Ensures a llama-server binary is available, downloading if necessary.
    /// </summary>
    /// <param name="versionOverride">Tag name (e.g. "b8184"). Null = latest release.</param>
    /// <param name="gpuKind">GPU type to select the correct asset.</param>
    /// <param name="cacheDirectoryPath">Root cache directory path.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Absolute path to the verified llama-server binary.</returns>
    public async Task<Result<string>> EnsureAvailableAsync(
        string? versionOverride,
        GpuKind gpuKind,
        string cacheDirectoryPath,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Ensuring llama-server binary (version={Version}, gpu={GpuKind})",
            versionOverride ?? "latest", gpuKind);

        var releaseResult = await FetchReleaseAsync(versionOverride, ct);
        if (releaseResult.IsFailed)
            return releaseResult.ToResult<string>();

        var release = releaseResult.Value;

        var assetResult = SelectAsset(release.Assets, gpuKind);
        if (assetResult.IsFailed)
            return assetResult.ToResult<string>();

        var asset = assetResult.Value;
        var binaryName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "llama-server.exe"
            : "llama-server";

        var versionTag = release.TagName;
        var platformDir = GetPlatformDir();
        var cacheDir = Path.GetFullPath(
            Path.Combine(cacheDirectoryPath, "llama-server", versionTag, platformDir));
        var binaryPath = Path.Combine(cacheDir, binaryName);

        // Cache hit
        if (File.Exists(binaryPath))
        {
            _logger.LogInformation("llama-server cache hit: {Path}", binaryPath);
            if (await VerifyBinaryAsync(binaryPath, ct))
                return Result.Ok(binaryPath);

            _logger.LogWarning("Cached binary failed verification, re-downloading");
            File.Delete(binaryPath);
        }

        // Download
        _ = Directory.CreateDirectory(cacheDir);
        var downloadResult = await DownloadAndExtractAsync(asset, cacheDir, binaryName, ct);
        if (downloadResult.IsFailed)
            return downloadResult;

        // Make executable on Unix
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            await MakeExecutableAsync(binaryPath, ct);

        _logger.LogInformation("llama-server binary ready: {Path}", binaryPath);
        return Result.Ok(binaryPath);
    }

    // ── GitHub API ────────────────────────────────────────────────────────────

    private async Task<Result<GithubRelease>> FetchReleaseAsync(string? versionTag, CancellationToken ct)
    {
        var url = versionTag is null
            ? $"{GitHubApiBase}/releases/latest"
            : $"{GitHubApiBase}/releases/tags/{versionTag}";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", UserAgent);
            request.Headers.Add("Accept", "application/vnd.github+json");

            var response = await _httpClient.SendAsync(request, ct);
            _ = response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            var release = JsonSerializer.Deserialize<GithubRelease>(json, _jsonOptions);

            return release is null ? (Result<GithubRelease>)Result.Fail("[LlamaServerDownloader] GitHub API returned null release") : Result.Ok(release);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to fetch GitHub release info");
            return Result.Fail($"[LlamaServerDownloader] Failed to fetch release info: {ex.Message}");
        }
    }

    // ── Asset Selection ───────────────────────────────────────────────────────

    private Result<GithubAsset> SelectAsset(IReadOnlyList<GithubAsset> assets, GpuKind gpuKind)
    {
        var rid = GetRuntimeIdentifier();
        var patterns = GetAssetPatterns(gpuKind, rid);

        _logger.LogDebug(
            "Selecting asset for GpuKind={GpuKind}, RID={Rid}, patterns=[{Patterns}]",
            gpuKind, rid, string.Join(", ", patterns));

        foreach (var pattern in patterns)
        {
            var match = assets.FirstOrDefault(a => a.Name.StartsWith("llama") &&
                a.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                _logger.LogInformation("Selected asset: {AssetName}", match.Name);
                return Result.Ok(match);
            }
        }

        return Result.Fail(
            $"[LlamaServerDownloader] No matching asset found for GpuKind={gpuKind}, RID={rid}. " +
            $"Available: [{string.Join(", ", assets.Select(a => a.Name))}]");
    }

    private static string[] GetAssetPatterns(GpuKind gpuKind, string rid)
    {
        var isWindows = rid.StartsWith("win", StringComparison.OrdinalIgnoreCase);
        var isMacOs = rid.StartsWith("osx", StringComparison.OrdinalIgnoreCase);
        var isLinux = rid.StartsWith("linux", StringComparison.OrdinalIgnoreCase);
        var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";

        return gpuKind switch
        {
            GpuKind.Cuda when isWindows => [$"bin-win-cuda-{arch}", "bin-win-cuda"],
            GpuKind.Cuda when isLinux => [$"bin-linux-cuda-{arch}", "bin-linux-cuda", "bin-ubuntu-cuda"],
            GpuKind.Vulkan when isWindows => [$"bin-win-vulkan-{arch}", "bin-win-vulkan"],
            GpuKind.Vulkan when isLinux => [$"bin-ubuntu-vulkan-{arch}", "bin-ubuntu-vulkan", "bin-linux-vulkan"],
            GpuKind.Metal when isMacOs => [$"bin-macos-{arch}", "bin-macos"],
            GpuKind.CpuOnly when isWindows => [$"bin-win-noavx-{arch}", "bin-win-noavx", $"bin-win-{arch}"],
            GpuKind.CpuOnly when isLinux => [$"bin-ubuntu-{arch}", $"bin-linux-{arch}"],
            _ => ["bin-"]  // last-ditch
        };
    }

    private static string GetRuntimeIdentifier()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64"
            : RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
    }

    private static string GetPlatformDir()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "win"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx" : "linux";
    }

    // ── Download & Extract ────────────────────────────────────────────────────

    private async Task<Result<string>> DownloadAndExtractAsync(
        GithubAsset asset, string targetDirectory, string binaryName, CancellationToken ct)
    {
        var binaryPath = Path.Combine(targetDirectory, binaryName);
        var tempArchivePath = Path.Combine(Path.GetTempPath(), $"seevalocal-{Guid.NewGuid()}{Path.GetExtension(asset.Name)}");

        try
        {
            _logger.LogInformation("Downloading {AssetName} ({SizeBytes} bytes)...", asset.Name, asset.Size);

            using var request = new HttpRequestMessage(HttpMethod.Get, asset.BrowserDownloadUrl);
            request.Headers.Add("User-Agent", UserAgent);

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            _ = response.EnsureSuccessStatusCode();

            await using var fileStream = File.Create(tempArchivePath);
            await response.Content.CopyToAsync(fileStream, ct);

            fileStream.Close();
            _logger.LogDebug("Archive downloaded to {TempPath}, extracting...", tempArchivePath);

            await ExtractBinaryAsync(tempArchivePath, asset.Name, binaryName, targetDirectory, ct);

            return !File.Exists(binaryPath)
                ? (Result<string>)Result.Fail(
                    $"[LlamaServerDownloader] Binary '{binaryName}' not found after extraction of '{asset.Name}'")
                : Result.Ok(binaryPath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to download or extract llama-server");
            return Result.Fail($"[LlamaServerDownloader] Download failed: {ex.Message}");
        }
        finally
        {
            if (File.Exists(tempArchivePath))
                File.Delete(tempArchivePath);
        }
    }

    private static async Task ExtractBinaryAsync(
        string archivePath, string assetName, string binaryName, string targetDirectory, CancellationToken ct)
    {
        if (assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            System.IO.Compression.ZipFile.ExtractToDirectory(archivePath, targetDirectory, overwriteFiles: true);
        }
        else if (assetName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
              || assetName.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            // Use tar process on Unix; on Windows use .NET APIs when available
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "tar",
                Arguments = $"-xzf \"{archivePath}\" -C \"{targetDirectory}\"",
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var process = System.Diagnostics.Process.Start(psi)!;
            await process.WaitForExitAsync(ct);
            if (process.ExitCode != 0)
            {
                var err = await process.StandardError.ReadToEndAsync(ct);
                throw new InvalidOperationException($"tar failed with exit code {process.ExitCode}: {err}");
            }
        }
        else
        {
            throw new NotSupportedException($"Unsupported archive format: {assetName}");
        }
    }

    // ── Verification ──────────────────────────────────────────────────────────

    private async Task<bool> VerifyBinaryAsync(string binaryPath, CancellationToken ct)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = binaryPath,
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null) return false;

            await process.WaitForExitAsync(ct);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task MakeExecutableAsync(string binaryPath, CancellationToken ct)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "chmod",
            Arguments = $"+x \"{binaryPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = System.Diagnostics.Process.Start(psi);
        if (process is not null)
            await process.WaitForExitAsync(ct);
    }

    // ── JSON DTOs ─────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private record GithubRelease
    {
        public string TagName { get; init; } = "";
        public IReadOnlyList<GithubAsset> Assets { get; init; } = [];
    }

    private record GithubAsset
    {
        public string Name { get; init; } = "";
        public string BrowserDownloadUrl { get; init; } = "";
        public long Size { get; init; }
    }
}
