using LibreHardwareMonitor.Hardware;
using Microsoft.Extensions.Logging;
using Seevalocal.Core.Models;
using System.Runtime.InteropServices;

namespace Seevalocal.Server;

/// <summary>
/// Detects the available GPU type on the current host.
/// Detection order: Multi-GPU (Vulkan) → CUDA → Metal → Vulkan → CpuOnly.
/// Prefers Vulkan for multi-GPU setups with mixed NVIDIA/AMD GPUs having similar VRAM.
/// </summary>
public sealed class GpuDetector(ILogger<GpuDetector> logger)
{
    private readonly ILogger<GpuDetector> _logger = logger;

    /// <summary>
    /// Minimum VRAM (in MB) to consider a GPU as "not puny" for multi-GPU detection.
    /// iGPUs typically have less than 2GB shared memory.
    /// </summary>
    private const int MIN_VRAM_FOR_MULTI_GPU_MB = 2048;

    public async Task<GpuKind> DetectAsync(CancellationToken cancellationToken = default)
    {
        // 1. Check for multi-GPU setup with mixed vendors (NVIDIA + AMD)
        var gpuInfo = DetectAllGpus();
        if (gpuInfo.HasMixedVendor)
        {
            // Multi-GPU setup with at least one non-puny GPU - prefer Vulkan for better compatibility
            _logger.LogInformation(
                "GPU detection: Mixed vendor GPUs detected (NVIDIA: {NvidiaCount}, AMD: {AmdCount}). Using Vulkan for multi-GPU support.",
                gpuInfo.NvidiaCount, gpuInfo.AmdCount);
            return GpuKind.Vulkan;
        }

        // 2. CUDA check (only if no mixed vendor setup)
        if (gpuInfo.NvidiaCount > 0 && await HasCudaAsync(cancellationToken))
        {
            _logger.LogInformation("GPU detection: CUDA detected");
            return GpuKind.Cuda;
        }

        // 3. Metal (macOS always has Metal if we reach here)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            _logger.LogInformation("GPU detection: Metal detected (macOS)");
            return GpuKind.Metal;
        }

        // 4. Vulkan
        if (HasVulkan())
        {
            _logger.LogInformation("GPU detection: Vulkan detected");
            return GpuKind.Vulkan;
        }

        // 5. Fallback
        _logger.LogInformation("GPU detection: no GPU found, using CPU-only");
        return GpuKind.CpuOnly;
    }

    /// <summary>
    /// Detects all GPUs and their vendors/VRAM using LibreHardwareMonitorLib.
    /// </summary>
    private GpuInfo DetectAllGpus()
    {
        var info = new GpuInfo();
        var gpus = new List<GpuDevice>();

        try
        {
            var computer = new Computer
            {
                IsGpuEnabled = true,
                IsMemoryEnabled = false,
                IsCpuEnabled = false,
                IsMotherboardEnabled = false,
                IsControllerEnabled = false,
                IsNetworkEnabled = false,
                IsStorageEnabled = false
            };

            computer.Open();

            foreach (var hardware in computer.Hardware)
            {
                if (hardware.HardwareType != HardwareType.GpuAmd &&
                    hardware.HardwareType != HardwareType.GpuNvidia &&
                    hardware.HardwareType != HardwareType.GpuIntel)
                {
                    continue;
                }

                hardware.Update();

                var gpuDevice = new GpuDevice
                {
                    Name = hardware.Name,
                    Vendor = GetVendorFromHardwareType(hardware.HardwareType),
                    VramMb = GetVramFromHardware(hardware)
                };

                gpus.Add(gpuDevice);
            }

            computer.Close();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to detect GPUs using LibreHardwareMonitorLib");
        }

        // Separate by vendor
        var nvidiaGpus = gpus.Where(g => g.Vendor == "NVIDIA").ToList();
        var amdGpus = gpus.Where(g => g.Vendor == "AMD").ToList();

        info.NvidiaCount = nvidiaGpus.Count;
        info.AmdCount = amdGpus.Count;
        info.TotalVramMb = gpus.Sum(g => g.VramMb);
        info.HasMixedVendor = nvidiaGpus.Any(g => g.VramMb >= MIN_VRAM_FOR_MULTI_GPU_MB) && amdGpus.Any(g => g.VramMb >= MIN_VRAM_FOR_MULTI_GPU_MB);

        _logger.LogDebug(
            "GPU scan: NVIDIA={NvidiaCount}, AMD={AmdCount}, TotalVRAM={TotalVramMb}MB, MixedVendor={HasMixedVendor}",
            info.NvidiaCount, info.AmdCount, info.TotalVramMb, info.HasMixedVendor);

        return info;
    }

    private static string GetVendorFromHardwareType(HardwareType type) => type switch
    {
        HardwareType.GpuNvidia => "NVIDIA",
        HardwareType.GpuAmd => "AMD",
        HardwareType.GpuIntel => "Intel",
        _ => "Unknown"
    };

    private static int GetVramFromHardware(IHardware hardware)
    {
        // Try to find memory-related sensors
        foreach (var sensor in hardware.Sensors)
        { //TODO: Prefer "D3D Dedicated Memory Total"
            if ((sensor.Name?.Contains("Memory Total", StringComparison.OrdinalIgnoreCase) == true
                || sensor.Name?.Contains("VRAM", StringComparison.OrdinalIgnoreCase) == true)
                && sensor.Value.HasValue && sensor.SensorType == SensorType.SmallData)
            {
                return (int)sensor.Value.Value;
            }
        }

        return 0;
    }

    private async Task<bool> HasCudaAsync(CancellationToken ct)
    {
        // Check Windows registry first
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && HasNvidiaRegistry())
            return true;

        // Try nvidia-smi
        return await TryRunToolAsync("nvidia-smi", "--query-gpu=name --format=csv,noheader", ct);
    }

    private static bool HasNvidiaRegistry()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine
                .OpenSubKey(@"SOFTWARE\NVIDIA Corporation\NVSMI");
            return key is not null;
        }
        catch
        {
            return false;
        }
    }

    private static bool HasVulkan()
    {
        // Check for Vulkan runtime DLL/SO
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return File.Exists(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System), "vulkan-1.dll"));
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Common locations for libvulkan on Linux
            return File.Exists("/usr/lib/x86_64-linux-gnu/libvulkan.so.1")
                || File.Exists("/usr/lib/libvulkan.so.1")
                || File.Exists("/usr/lib64/libvulkan.so.1");
        }

        return false;
    }

    private async Task<bool> TryRunToolAsync(string toolName, string arguments, CancellationToken ct)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = toolName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var process = new System.Diagnostics.Process { StartInfo = psi };
            _ = process.Start();

            // Read output but don't wait more than 5 seconds
            var outputTask = process.StandardOutput.ReadToEndAsync(ct);
            var finished = await Task.WhenAny(
                process.WaitForExitAsync(ct),
                Task.Delay(TimeSpan.FromSeconds(5), ct));

            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                return false;
            }

            var output = await outputTask;
            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug("Tool '{Tool}' not found or failed: {Message}", toolName, ex.Message);
            return false;
        }
    }
}

/// <summary>
/// Information about detected GPUs.
/// </summary>
internal sealed class GpuInfo
{
    public int NvidiaCount { get; set; }
    public int AmdCount { get; set; }
    public int TotalVramMb { get; set; }
    public bool HasMixedVendor { get; set; }
}

/// <summary>
/// Information about a single GPU device.
/// </summary>
internal sealed class GpuDevice
{
    public string Name { get; set; } = string.Empty;
    public int VramMb { get; set; }
    public string Vendor { get; set; } = string.Empty;
}
