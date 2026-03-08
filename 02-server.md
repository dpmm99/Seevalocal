# Seevalocal — Part 1: Server Lifecycle & HTTP Client

> **Read `00-conventions.md` before this file.**
> Interfaces referenced here are defined in `10-interfaces.md`.
> This part is implemented in project `Seevalocal.Server`.

---

## 1. Responsibilities

- Detect the host GPU type (CUDA, Vulkan, CPU-only, Metal on macOS).
- Find or download the correct `llama-server` release binary.
- Launch `llama-server` with a fully-resolved argument list, capture stdout/stderr, wait until the health endpoint returns OK.
- Wrap every llama-server HTTP endpoint used by the eval pipeline.
- Manage graceful shutdown (SIGTERM / `Process.Kill(entireProcessTree: true)`).
- Expose a `total_slots` value from `GET /props` for the concurrency coordinator.

---

## 2. GPU Detection

### 2.1 Detection Strategy

```
GpuDetector.DetectAsync() → GpuKind { Cuda, Vulkan, Metal, CpuOnly }
```

Detection order:

1. **CUDA**: check for `nvidia-smi` on PATH; parse output for any GPU. On Windows also check registry `HKLM\SOFTWARE\NVIDIA Corporation\NVSMI`.
2. **Metal**: `RuntimeInformation.IsOSPlatform(OSPlatform.OSX)` → always `Metal` on macOS if detection reaches this step.
3. **Vulkan**: check for `vulkaninfo` on PATH or `libvulkan.so.1` / `vulkan-1.dll`. Also use Vulkan if the user has both NVIDIA and AMD GPUs.
4. **CpuOnly**: fallback.

### 2.2 llama-server Release Matrix

| GpuKind | Release asset name pattern |
|---------|---------------------------|
| Cuda | `llama-*-bin-win-cuda-*` / `llama-*-bin-linux-cuda-*` |
| Vulkan | `llama-*-bin-win-vulkan-*` / `llama-*-bin-linux-vulkan-*` |
| Metal | `llama-*-bin-macos-*` (Metal is always included) |
| CpuOnly | `llama-*-bin-win-noavx-*` / `llama-*-bin-linux-*` (no GPU suffix) |

Examples: `llama-b8184-bin-win-cuda-12.4-x64.zip`, `llama-b8184-bin-win-cuda-13.1-x64.zip`, `llama-b8184-bin-win-vulkan-x64.zip`, `llama-b8184-bin-ubuntu-vulkan-x64.tar.gz`, `llama-b8184-bin-macos-arm64.tar.gz`

Asset names come from the GitHub Releases API: `https://api.github.com/repos/ggml-org/llama.cpp/releases/latest`.

### 2.3 Download & Cache

```
~/.Seevalocal/cache/llama-server/<version>/<platform>/llama-server[.exe]
```

On Windows, use a more appropriate path root like `%LOCALAPPDATA%\Seevalocal`.

If the binary already exists and its SHA-256 matches the release checksum, skip download. If no checksum is published, verify by running `llama-server --version`.

---

## 3. Server Endpoint Configuration

### 3.1 Option A — Manage Server

```yaml
server:
  manage: true
  executablePath: null          # null = auto-download
  model:
    source: localFile           # localFile | huggingFace
    filePath: /models/my.gguf   # for localFile
    hfRepo: unsloth/phi-4-GGUF  # for huggingFace
    hfQuant: q4_k_m             # for huggingFace; null = default
  host: 127.0.0.1
  port: 8080
  apiKey: null
  extraArgs: []                 # passed verbatim after all structured args
```

### 3.2 Option B — Connect to Existing Server

```yaml
server:
  manage: false
  baseUrl: http://192.168.1.10:8080
  apiKey: sk-my-key
```

### 3.3 C# Model

```csharp
// In Seevalocal.Server
public record ServerConfig
{
    public bool Manage { get; init; }

    // Option A (Manage = true)
    public string? ExecutablePath { get; init; }
    public ModelSource? Model { get; init; }
    public string Host { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 8080;
    public string? ApiKey { get; init; }
    public IReadOnlyList<string> ExtraArgs { get; init; } = [];

    // Option B (Manage = false)
    public string? BaseUrl { get; init; }
    // ApiKey is shared between both options
}

public record ModelSource
{
    public ModelSourceKind Kind { get; init; }
    public string? FilePath { get; init; }
    public string? HfRepo { get; init; }
    public string? HfQuant { get; init; }
    public string? HfToken { get; init; }
}

public enum ModelSourceKind { LocalFile, HuggingFace }
```

---

## 4. llama-server Argument Builder

`LlamaServerArgBuilder` converts a `LlamaServerSettings` (see `03-config.md`) into a `string[]` for `Process.StartInfo.ArgumentList`. Only non-null fields are emitted.

```csharp
public sealed class LlamaServerArgBuilder
{
    public string[] Build(LlamaServerSettings settings, ServerConfig serverConfig);
}
```

### 4.1 Argument Mapping (representative subset)

| C# Property | CLI Flag | Notes |
|---|---|---|
| `ContextWindowTokens` | `-c` | |
| `BatchSizeTokens` | `-b` | |
| `UbatchSizeTokens` | `-ub` | |
| `ParallelSlotCount` | `-np` | null = auto |
| `GpuLayerCount` | `-ngl` | null = auto |
| `FlashAttention` | `-fa on/off` | bool? → omit if null |
| `EnableContinuousBatching` | `--cont-batching` / `--no-cont-batching` | bool? |
| `EnableCachePrompt` | `--cache-prompt` / `--no-cache-prompt` | bool? |
| `ApiKey` | `--api-key` | |
| `ContextShift` | `--context-shift` / `--no-context-shift` | bool? |
| `Host` | `--host` | |
| `Port` | `--port` | |
| `ReasoningFormat` | `--reasoning-format` | string? |
| `SamplingTemperature` | `--temp` | double? |
| `TopP` | `--top-p` | double? |
| `TopK` | `--top-k` | int? |
| `MinP` | `--min-p` | double? |
| `RepeatPenalty` | `--repeat-penalty` | double? |
| `Seed` | `--seed` | int? |
| `ThreadCount` | `-t` | int? |
| `GpuLayerCount` | `-ngl` | int? |
| `KvCacheTypeK` | `-ctk` | string? |
| `KvCacheTypeV` | `-ctv` | string? |
| `LogVerbosity` | `-lv` | int? |
| `ChatTemplate` | `--chat-template` | string? |
| `JinjaEnabled` | `--jinja` / `--no-jinja` | bool? |

**Deliberately excluded** (too advanced / speculative decoding):
- `--draft`, `--model-draft`, `--draft-p-min`, etc.
- `--lora`, `--control-vector` (these can go in `extraArgs`)

### 4.2 Bool? Serialization Rule

```csharp
private void AppendBool(List<string> args, bool? value, string enableFlag, string disableFlag)
{
    if (value is null) return;
    args.Add(value.Value ? enableFlag : disableFlag);
}
```

---

## 5. Server Lifecycle Manager

```csharp
public sealed class LlamaServerManager : IAsyncDisposable
{
    // Starts server if Manage=true, otherwise validates connectivity.
    public Task<Result<ServerInfo>> StartAsync(
        ServerConfig config,
        LlamaServerSettings settings,
        CancellationToken cancellationToken);

    // Returns props including total_slots.
    public Task<Result<ServerProps>> GetPropsAsync(CancellationToken cancellationToken);

    // Graceful shutdown: sends SIGTERM (Unix) or GenerateConsoleCtrlEvent/Kill (Windows).
    public ValueTask DisposeAsync();

    // Fires when the managed process exits unexpectedly.
    public event EventHandler<ProcessExitedEventArgs>? ProcessExited;
}

public record ServerInfo
{
    public string BaseUrl { get; init; } = "";
    public string? ApiKey { get; init; }
    public int TotalSlots { get; init; }
    public string ModelAlias { get; init; } = "";
}
```

### 5.1 Startup Sequence (Manage = true)

1. Resolve binary path (auto-download if needed).
2. Build argument list via `LlamaServerArgBuilder`.
3. Start `Process`; pipe stdout/stderr to `ILogger`.
4. Poll `GET /health` with exponential back-off (max 2 s, interval 500 ms → 2 s).
5. On success, call `GET /props`; extract `total_slots`.
6. Return `ServerInfo`.

### 5.2 Health Polling

```csharp
private async Task<bool> WaitForHealthAsync(
    string baseUrl, double timeoutSeconds, CancellationToken ct)
{
    var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
    var delayMilliseconds = 500;
    while (DateTime.UtcNow < deadline)
    {
        try
        {
            var response = await _http.GetAsync($"{baseUrl}/health", ct);
            if (response.IsSuccessStatusCode) return true;
        }
        catch { /* server not up yet */ }
        await Task.Delay(delayMilliseconds, ct);
        delayMilliseconds = Math.Min(delayMilliseconds * 2, 2000);
    }
    return false;
}
```

---

## 6. llama-server HTTP Client

`LlamaServerClient` wraps every endpoint used by the eval pipeline. It is constructed with a `ServerInfo`.

```csharp
public sealed class LlamaServerClient
{
    public LlamaServerClient(ServerInfo serverInfo, HttpClient httpClient, ILogger<LlamaServerClient> logger);

    // POST /v1/chat/completions
    public Task<Result<ChatCompletionResponse>> ChatCompletionAsync(
        ChatCompletionRequest request, CancellationToken ct);

    // POST /v1/messages (Anthropic-compatible)
    public Task<Result<AnthropicMessageResponse>> AnthropicMessageAsync(
        AnthropicMessageRequest request, CancellationToken ct);

    // GET /props
    public Task<Result<ServerProps>> GetPropsAsync(CancellationToken ct);

    // GET /health
    public Task<Result<HealthStatus>> GetHealthAsync(CancellationToken ct);

    // POST /tokenize
    public Task<Result<TokenizeResponse>> TokenizeAsync(string content, bool addSpecial, CancellationToken ct);

    // POST /v1/embeddings
    public Task<Result<EmbeddingsResponse>> GetEmbeddingsAsync(EmbeddingsRequest request, CancellationToken ct);
}
```

### 6.1 Request/Response Models (representative)

```csharp
public record ChatCompletionRequest
{
    public string Model { get; init; } = "";
    public IReadOnlyList<ChatMessage> Messages { get; init; } = [];
    public double? Temperature { get; init; }
    public int? MaxTokens { get; init; }
    public IReadOnlyList<string>? Stop { get; init; }
    public bool Stream { get; init; } = false;
}

public record ChatCompletionResponse
{
    public string Id { get; init; } = "";
    public IReadOnlyList<ChatChoice> Choices { get; init; } = [];
    public ChatUsage Usage { get; init; } = new();
    public ChatTimings? Timings { get; init; }
}

public record ChatUsage
{
    public int PromptTokenCount { get; init; }
    public int CompletionTokenCount { get; init; }
    public int TotalTokenCount { get; init; }
}

public record ChatTimings
{
    public double PromptMilliseconds { get; init; }
    public double PredictedMilliseconds { get; init; }
    public double PromptTokensPerSecond { get; init; }
    public double PredictedTokensPerSecond { get; init; }
}

public record ServerProps
{
    public int TotalSlots { get; init; }
    public string ModelPath { get; init; } = "";
    public string ChatTemplate { get; init; } = "";
    public DefaultGenerationSettings DefaultGenerationSettings { get; init; } = new();
}
```

### 6.2 Retry Policy

All HTTP calls use a Polly `ResiliencePipeline` with:
- 3 retries on transient HTTP errors (5xx, timeout)
- Exponential back-off: 1 s, 2 s, 4 s
- Jitter: ±20%
- No retry on 4xx (bad request — not transient)

---

## 7. Auto-Download Logic

```csharp
public sealed class LlamaServerDownloader
{
    // Returns path to the verified binary.
    public Task<Result<string>> EnsureAvailableAsync(
        string? versionOverride,      // null = latest
        GpuKind gpuKind,
        string cacheDirectoryPath,
        CancellationToken ct);
}
```

Steps:
1. Query `https://api.github.com/repos/ggml-org/llama.cpp/releases/latest` (or specific version tag).
2. Find matching asset by `GpuKind` and `RuntimeInformation.RuntimeIdentifier`.
3. If cached binary exists and passes verification, return path.
4. Download to temp, verify, move to cache.
5. On Unix, `chmod +x`.

---

## 8. Unit Tests (Seevalocal.Server.Tests)

| Test class | Coverage |
|---|---|
| `GpuDetectorTests` | Mock PATH/registry; assert correct `GpuKind` for each platform |
| `LlamaServerArgBuilderTests` | Null fields omitted; bool? → correct flag; units in arg names |
| `LlamaServerClientTests` | Mock `HttpMessageHandler`; assert correct serialization/deserialization |
| `LlamaServerManagerTests` | Integration test (trait `[Integration]`): start real server, check health |
| `LlamaServerDownloaderTests` | Mock GitHub API; assert correct asset selection; verify cache hit |
