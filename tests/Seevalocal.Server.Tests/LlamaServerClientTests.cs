using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RichardSzalay.MockHttp;
using Seevalocal.Core.Models;
using Seevalocal.Server.Client;
using Seevalocal.Server.Models;
using System.Net;
using Xunit;

namespace Seevalocal.Server.Tests;

public sealed class LlamaServerClientTests
{
    private const string BaseUrl = "http://127.0.0.1:8080";

    private static ServerInfo MakeServerInfo(string? apiKey = null) =>
        new() { BaseUrl = BaseUrl, ApiKey = apiKey, TotalSlots = 4 };

    private static LlamaServerClient MakeClient(MockHttpMessageHandler mock, string? apiKey = null)
    {
        var httpClient = mock.ToHttpClient();
        return new LlamaServerClient(
            MakeServerInfo(apiKey),
            httpClient,
            NullLogger<LlamaServerClient>.Instance);
    }

    // ── /health ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetHealthAsync_ReturnsOk_WhenServerRespondsOk()
    {
        var mock = new MockHttpMessageHandler();
        _ = mock.When($"{BaseUrl}/health")
            .Respond("application/json", """{"healthy":true,"status":"ok"}""");

        var client = MakeClient(mock);
        var result = await client.GetHealthAsync(CancellationToken.None);

        _ = result.IsSuccess.Should().BeTrue();
        _ = result.Value.IsOk.Should().BeTrue();
    }

    [Fact]
    public async Task GetHealthAsync_ReturnsFailed_On500()
    {
        var mock = new MockHttpMessageHandler();
        _ = mock.When($"{BaseUrl}/health")
            .Respond(HttpStatusCode.InternalServerError, "application/json", """{"error":"crash"}""");

        var client = MakeClient(mock);
        var result = await client.GetHealthAsync(CancellationToken.None);

        _ = result.IsFailed.Should().BeTrue();
        _ = result.Errors[0].Message.Should().Contain("500");
    }

    // ── /props ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPropsAsync_DeserializesCorrectly()
    {
        var json = """
            {
                "total_slots": 4,
                "model_path": "/models/phi-4.gguf",
                "chat_template": "chatml",
                "default_generation_settings": { "temperature": 0.8, "n_predict": 512 }
            }
            """;

        var mock = new MockHttpMessageHandler();
        _ = mock.When($"{BaseUrl}/props").Respond("application/json", json);

        var client = MakeClient(mock);
        var result = await client.GetPropsAsync(CancellationToken.None);

        _ = result.IsSuccess.Should().BeTrue();
        _ = result.Value.TotalSlots.Should().Be(4);
        _ = result.Value.ModelPath.Should().Be("/models/phi-4.gguf");
        _ = result.Value.ChatTemplate.Should().Be("chatml");
    }

    // ── /v1/chat/completions ─────────────────────────────────────────────────

    [Fact]
    public async Task ChatCompletionAsync_SendsCorrectRequest_AndDeserializesResponse()
    {
        var responseJson = """
            {
                "id": "chatcmpl-123",
                "choices": [{
                    "index": 0,
                    "message": { "role": "assistant", "content": "Hello!" },
                    "finish_reason": "stop"
                }],
                "usage": {
                    "prompt_tokens": 10,
                    "completion_tokens": 5,
                    "total_tokens": 15
                }
            }
            """;

        string? capturedBody = null;
        var mock = new MockHttpMessageHandler();
        _ = mock.When(HttpMethod.Post, $"{BaseUrl}/v1/chat/completions")
            .With(req =>
            {
                capturedBody = req.Content?.ReadAsStringAsync().Result;
                return true;
            })
            .Respond("application/json", responseJson);

        var client = MakeClient(mock);
        var request = new ChatCompletionRequest
        {
            Model = "phi-4",
            Messages = [new ChatMessage { Role = "user", Content = "Hi" }],
            Temperature = 0.7,
        };

        var result = await client.ChatCompletionAsync(request, CancellationToken.None);

        _ = result.IsSuccess.Should().BeTrue();
        _ = result.Value.Id.Should().Be("chatcmpl-123");
        _ = result.Value.Choices.Should().HaveCount(1);
        _ = result.Value.Choices[0].Message.Content.Should().Be("Hello!");
        _ = result.Value.Usage.PromptTokenCount.Should().Be(10);
        _ = result.Value.Usage.CompletionTokenCount.Should().Be(5);

        // Verify request body uses snake_case
        _ = capturedBody.Should().NotBeNull();
        _ = capturedBody!.Should().Contain("\"messages\"");
    }

    [Fact]
    public async Task ChatCompletionAsync_NullTemperature_OmitsFromRequest()
    {
        string? capturedBody = null;
        var mock = new MockHttpMessageHandler();
        _ = mock.When(HttpMethod.Post, $"{BaseUrl}/v1/chat/completions")
            .With(req =>
            {
                capturedBody = req.Content?.ReadAsStringAsync().Result;
                return true;
            })
            .Respond("application/json", """
                {"id":"x","choices":[{"index":0,"message":{"role":"assistant","content":"hi"},"finish_reason":"stop"}],"usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}
                """);

        var client = MakeClient(mock);
        var request = new ChatCompletionRequest
        {
            Model = "test",
            Messages = [new ChatMessage { Role = "user", Content = "hi" }],
            Temperature = null,  // should be omitted
        };

        _ = await client.ChatCompletionAsync(request, CancellationToken.None);

        _ = capturedBody.Should().NotContain("temperature");
    }

    // ── Auth header ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetHealthAsync_WithApiKey_SendsBearerToken()
    {
        string? capturedAuth = null;
        var mock = new MockHttpMessageHandler();
        _ = mock.When($"{BaseUrl}/health")
            .With(req =>
            {
                capturedAuth = req.Headers.Authorization?.ToString();
                return true;
            })
            .Respond("application/json", """{"status":"ok"}""");

        var client = MakeClient(mock, apiKey: "sk-secret");
        _ = await client.GetHealthAsync(CancellationToken.None);

        _ = capturedAuth.Should().Be("Bearer sk-secret");
    }

    [Fact]
    public async Task GetHealthAsync_WithoutApiKey_SendsNoAuthHeader()
    {
        string? capturedAuth = null;
        var mock = new MockHttpMessageHandler();
        _ = mock.When($"{BaseUrl}/health")
            .With(req =>
            {
                capturedAuth = req.Headers.Authorization?.ToString();
                return true;
            })
            .Respond("application/json", """{"status":"ok"}""");

        var client = MakeClient(mock, apiKey: null);
        _ = await client.GetHealthAsync(CancellationToken.None);

        _ = capturedAuth.Should().BeNull();
    }

    // ── /tokenize ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task TokenizeAsync_DeserializesTokenList()
    {
        var mock = new MockHttpMessageHandler();
        _ = mock.When(HttpMethod.Post, $"{BaseUrl}/tokenize")
            .Respond("application/json", """{"tokens":[1,2,3,42]}""");

        var client = MakeClient(mock);
        var result = await client.TokenizeAsync("hello world", addSpecial: true, CancellationToken.None);

        _ = result.IsSuccess.Should().BeTrue();
        _ = result.Value.Tokens.Should().Equal(1, 2, 3, 42);
    }

    // ── Network failure ───────────────────────────────────────────────────────

    [Fact]
    public async Task ChatCompletionAsync_ReturnsFailedResult_OnNetworkError()
    {
        var mock = new MockHttpMessageHandler();
        _ = mock.When(HttpMethod.Post, $"{BaseUrl}/v1/chat/completions")
            .Throw(new HttpRequestException("connection refused"));

        var client = MakeClient(mock);
        var result = await client.ChatCompletionAsync(
            new ChatCompletionRequest { Model = "x", Messages = [] },
            CancellationToken.None);

        _ = result.IsFailed.Should().BeTrue();
        _ = result.Errors[0].Message.Should().Contain("[LlamaServerClient]");
    }

    // ── ChatTimings deserialization ───────────────────────────────────────────

    [Fact]
    public async Task ChatCompletionAsync_DeserializesTimings()
    {
        var json = """
            {
                "id": "x",
                "choices": [{
                    "index": 0,
                    "message": {"role":"assistant","content":"hi"},
                    "finish_reason": "stop"
                }],
                "usage": {"prompt_tokens":5,"completion_tokens":3,"total_tokens":8},
                "timings": {
                    "prompt_ms": 120.5,
                    "predicted_ms": 350.2,
                    "prompt_per_second": 41.5,
                    "predicted_per_second": 8.6
                }
            }
            """;

        var mock = new MockHttpMessageHandler();
        _ = mock.When(HttpMethod.Post, $"{BaseUrl}/v1/chat/completions")
            .Respond("application/json", json);

        var client = MakeClient(mock);
        var result = await client.ChatCompletionAsync(
            new ChatCompletionRequest { Model = "x", Messages = [] },
            CancellationToken.None);

        _ = result.IsSuccess.Should().BeTrue();
        _ = result.Value.Timings.Should().NotBeNull();
        _ = result.Value.Timings!.PromptMilliseconds.Should().BeApproximately(120.5, 0.001);
        _ = result.Value.Timings.PredictedMilliseconds.Should().BeApproximately(350.2, 0.001);
    }
}
