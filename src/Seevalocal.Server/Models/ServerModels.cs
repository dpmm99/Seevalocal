// Re-export types from Seevalocal.Core.Models for backward compatibility
// The actual definitions are in Seevalocal.Core.Abstractions

namespace Seevalocal.Server.Models;

// ServerInfo is specific to the Server project
/// <summary>
/// Runtime information about a running llama-server instance.
/// Produced by LlamaServerManager.StartAsync().
/// </summary>
public record ServerInfo
{
    /// <summary>Base URL, e.g., "http://127.0.0.1:8080".</summary>
    public required string BaseUrl { get; init; }
    public string? ApiKey { get; init; }
    /// <summary>From GET /props. Used to size the concurrency semaphore.</summary>
    public int TotalSlots { get; init; }
    public string ModelAlias { get; init; } = "";
    /// <summary>Absolute path to the llama-server binary that was started (for managed servers).</summary>
    public string? BinaryPath { get; init; }
}

/// <summary>
/// Event args for when a managed process exits unexpectedly.
/// </summary>
public class ProcessExitedEventArgs(int exitCode, string? errorMessage = null) : EventArgs
{
    public int ExitCode { get; init; } = exitCode;
    public string? ErrorMessage { get; init; } = errorMessage;
}
