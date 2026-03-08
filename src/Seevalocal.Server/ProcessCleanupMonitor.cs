using System.Diagnostics;

namespace Seevalocal.Server;

/// <summary>
/// Monitors a parent process and automatically kills a child process when the parent dies.
/// Used on Unix systems to ensure llama-server processes are cleaned up if the main app crashes.
/// 
/// Usage: ProcessCleanupMonitor <parentPid> <childPid>
/// 
/// This is designed to be started as a separate process that outlives potential parent crashes.
/// </summary>
public static class ProcessCleanupMonitor
{
    /// <summary>
    /// Main entry point for the monitor process.
    /// Call this from a separate process that watches the parent.
    /// </summary>
    public static async Task RunAsync(int parentPid, int childPid, CancellationToken ct)
    {
        try
        {
            // Wait for parent to exit or cancellation
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var parent = Process.GetProcessById(parentPid);
                    if (parent.HasExited)
                    {
                        break;
                    }
                }
                catch (ArgumentException)
                {
                    // Parent process no longer exists (already exited)
                    break;
                }

                await Task.Delay(1000, ct);
            }

            // Parent died or cancellation requested - kill the child
            KillChildProcess(childPid);
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation - still kill the child
            KillChildProcess(childPid);
        }
        catch (Exception)
        {
            // On any error, try to kill the child anyway
            try { KillChildProcess(childPid); } catch { }
        }
    }

    private static void KillChildProcess(int pid)
    {
#pragma warning disable RCS1075 // Avoid empty catch clause that catches System.Exception - don't care; just trying to kill a child process if we can...
        try
        {
            var child = Process.GetProcessById(pid);
            if (!child.HasExited)
            {
                // Try graceful shutdown first
                try
                {
                    child.Kill(entireProcessTree: false);
                    child.WaitForExit(5000);
                }
                catch
                {
                    // If graceful fails, force kill
                    child.Kill();
                }
            }
        }
        catch (ArgumentException)
        {
            // Process already exited - that's fine
        }
        catch (Exception)
        {
            // Ignore other errors - we tried our best
        }
#pragma warning restore RCS1075 // Avoid empty catch clause that catches System.Exception
    }

    /// <summary>
    /// Starts a monitor process for the given parent and child PIDs.
    /// Returns the monitor process, which should be tracked for cleanup.
    /// </summary>
    public static Process? StartMonitor(int parentPid, int childPid)
    {
        try
        {
            var currentExe = Environment.ProcessPath ??
                Environment.GetCommandLineArgs().First();

            var psi = new ProcessStartInfo
            {
                FileName = currentExe,
                Arguments = $"--monitor-process {parentPid} {childPid}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
            };

            var monitor = Process.Start(psi);
            return monitor;
        }
        catch (Exception)
        {
            // Failed to start monitor - log but don't fail
            return null;
        }
    }
}
