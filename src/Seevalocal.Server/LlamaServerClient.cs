using FluentResults;
using Microsoft.Extensions.Logging;
using Seevalocal.Core;
using Seevalocal.Core.Models;
using Seevalocal.Server.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Seevalocal.Server;

/// <summary>
/// Pre-configured HTTP client for a llama-server instance.
/// Constructed from a <see cref="ServerInfo"/>. Thread-safe; share one instance per endpoint.
/// Includes a semaphore to prevent flooding the server with concurrent requests.
/// </summary>
/// <remarks>
/// Creates a new LlamaServerClient with the specified concurrency limit.
/// </remarks>
/// <param name="serverInfo">Server connection info</param>
/// <param name="httpClient">HTTP client (should be shared/reused)</param>
/// <param name="logger">Logger</param>
/// <param name="maxConcurrentRequests">Maximum concurrent requests to this server (default: 10)</param>
public sealed class LlamaServerClient(
    ServerInfo serverInfo,
    HttpClient httpClient,
    ILogger<LlamaServerClient> logger,
    int maxConcurrentRequests = 10) : ILlamaServerClient, IDisposable
{
    private readonly ServerInfo _serverInfo = serverInfo ?? throw new ArgumentNullException(nameof(serverInfo));
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly ILogger<LlamaServerClient> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private SemaphoreSlim _semaphore = new(maxConcurrentRequests, maxConcurrentRequests);
    private bool _disposed;

    /// <summary>
    /// Gets the current semaphore count (for diagnostics).
    /// </summary>
    public int CurrentMaxConcurrent => _semaphore.CurrentCount;

    /// <summary>
    /// Initializes the semaphore based on the actual slot count from the server.
    /// Call this after creating the client to ensure correct concurrency limits.
    /// </summary>
    public async Task InitializeSemaphoreFromServerAsync(CancellationToken ct)
    {
        var propsResult = await GetPropsAsync(ct);
        if (propsResult.IsSuccess)
        {
            var slots = propsResult.Value.TotalSlots;
            if (slots > 0 && slots != _semaphore.CurrentCount)
            {
                _logger.LogInformation("Setting concurrency limit to {Slots} based on server props", slots);
                var oldSemaphore = _semaphore;
                _semaphore = new SemaphoreSlim(slots, slots);
                oldSemaphore.Dispose();
            }
        }
        else
        {
            _logger.LogWarning("Failed to get server props, keeping default concurrency limit of {DefaultCount}", _semaphore.CurrentCount);
        }
    }

    // ── Chat Completions (/v1/chat/completions) ───────────────────────────────

    public async Task<Result<ChatCompletionResponse>> ChatCompletionAsync(
        ChatCompletionRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Wait for semaphore to avoid flooding the server
        await _semaphore.WaitAsync(ct);
        try
        {
            return await PostAsync<ChatCompletionRequest, ChatCompletionResponse>(
                "/v1/chat/completions", request, ct);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    // ── Anthropic-compatible (/v1/messages) ───────────────────────────────────

    public async Task<Result<AnthropicMessageResponse>> AnthropicMessageAsync(
        AnthropicMessageRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _semaphore.WaitAsync(ct);
        try
        {
            return await PostAsync<AnthropicMessageRequest, AnthropicMessageResponse>(
                "/v1/messages", request, ct);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    // ── Props (/props) ────────────────────────────────────────────────────────

    public async Task<Result<ServerProps>> GetPropsAsync(CancellationToken ct)
    {
        return await GetAsync<ServerProps>("/props", ct);
    }

    // ── Health (/health) ──────────────────────────────────────────────────────

    public async Task<Result<HealthStatus>> GetHealthAsync(CancellationToken ct)
    {
        return await GetAsync<HealthStatus>("/health", ct);
    }

    // ── Tokenize (/tokenize) ──────────────────────────────────────────────────

    public async Task<Result<TokenizeResponse>> TokenizeAsync(
        string content,
        bool addSpecial,
        CancellationToken ct)
    {
        var body = new { content, add_special = addSpecial };
        return await PostAsync<object, TokenizeResponse>("/tokenize", body, ct);
    }

    // ── Embeddings (/v1/embeddings) ───────────────────────────────────────────

    public async Task<Result<EmbeddingsResponse>> GetEmbeddingsAsync(
        EmbeddingsRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await PostAsync<EmbeddingsRequest, EmbeddingsResponse>("/v1/embeddings", request, ct);
    }

    // ── Private HTTP helpers ─────────────────────────────────────────────────

    private async Task<Result<TResponse>> PostAsync<TRequest, TResponse>(
        string path,
        TRequest payload,
        CancellationToken ct)
    {
        var url = BuildUrl(path);

        try
        {
            _logger.LogDebug("POST {Url}", url);

            var json = JsonSerializer.Serialize(payload, LlamaJsonOptions.Request);
            using var content = new StringContent(json, Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            AddAuthHeader(requestMessage);

            var response = await _httpClient.SendAsync(requestMessage, ct);

            return await DeserializeResponseAsync<TResponse>(response, url, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "[LlamaServerClient] POST {Url} failed: {Message}", url, ex.Message);
            return Result.Fail($"[LlamaServerClient] POST {path} failed: {ex.Message}");
        }
    }

    private async Task<Result<TResponse>> GetAsync<TResponse>(string path, CancellationToken ct)
    {
        var url = BuildUrl(path);

        try
        {
            _logger.LogDebug("GET {Url}", url);

            using var requestMessage = new HttpRequestMessage(HttpMethod.Get, url);
            AddAuthHeader(requestMessage);

            var response = await _httpClient.SendAsync(requestMessage, ct);
            return await DeserializeResponseAsync<TResponse>(response, url, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "[LlamaServerClient] GET {Url} failed: {Message}", url, ex.Message);
            return Result.Fail($"[LlamaServerClient] GET {path} failed: {ex.Message}");
        }
    }

    private async Task<Result<TResponse>> DeserializeResponseAsync<TResponse>(
        HttpResponseMessage response,
        string url,
        CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "[LlamaServerClient] {Url} returned HTTP {StatusCode}: {Body}",
                url, (int)response.StatusCode, body);
            return Result.Fail(
                $"[LlamaServerClient] {url} returned HTTP {(int)response.StatusCode}: {body}");
        }

        try
        {
            var result = JsonSerializer.Deserialize<TResponse>(body, LlamaJsonOptions.LlamaServer);
            return result is null ? (Result<TResponse>)Result.Fail($"[LlamaServerClient] Deserialized null from {url}") : Result.Ok(result);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "[LlamaServerClient] Failed to deserialize response from {Url}", url);
            return Result.Fail(
                $"[LlamaServerClient] JSON deserialization failed for {url}: {ex.Message}. Body: {body[..Math.Min(body.Length, 500)]}");
        }
    }

    private string BuildUrl(string path) => $"{_serverInfo.BaseUrl.TrimEnd('/')}{path}";

    private void AddAuthHeader(HttpRequestMessage request)
    {
        if (_serverInfo.ApiKey is { Length: > 0 } key)
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", key);
    }

    /// <summary>
    /// Disposes the semaphore. The HttpClient is NOT disposed here as it should be managed externally.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _semaphore.Dispose();
    }
}

/// <summary>
/// JSON serialization options for llama-server HTTP client.
/// </summary>
public static class LlamaJsonOptions
{
    public static readonly JsonSerializerOptions Request = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static readonly JsonSerializerOptions LlamaServer = new()
    {
        PropertyNameCaseInsensitive = true,  // Accepts both snake_case and PascalCase
    };
}
