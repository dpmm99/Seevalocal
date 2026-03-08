namespace Seevalocal.Core.Models;

// ── Chat Completion Request/Response ─────────────────────────────────────────

public record ChatCompletionRequest
{
    public string Model { get; init; } = "";
    public IReadOnlyList<ChatMessage> Messages { get; init; } = [];
    public double? Temperature { get; init; }
    public int? MaxTokens { get; init; }
    public IReadOnlyList<string>? Stop { get; init; }
    public bool Stream { get; init; } = false;
}

public record ChatMessage
{
    public string Role { get; init; } = "";
    public string Content { get; init; } = "";
}

public record ChatCompletionResponse
{
    public string Id { get; init; } = "";
    public IReadOnlyList<ChatChoice> Choices { get; init; } = [];
    public ChatUsage Usage { get; init; } = new();
    public ChatTimings? Timings { get; init; }
}

public record ChatChoice
{
    public int Index { get; init; }
    public ChatMessage Message { get; init; } = new();
    public string FinishReason { get; init; } = "";
}

public record ChatUsage
{
    [System.Text.Json.Serialization.JsonPropertyName("prompt_tokens")]
    public int PromptTokenCount { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("completion_tokens")]
    public int CompletionTokenCount { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("total_tokens")]
    public int TotalTokenCount { get; init; }

    // Aliases for common property names
    public int PromptTokens => PromptTokenCount;
    public int CompletionTokens => CompletionTokenCount;
    public int TotalTokens => TotalTokenCount;
}

public record ChatTimings
{
    [System.Text.Json.Serialization.JsonPropertyName("prompt_ms")]
    public double PromptMilliseconds { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("predicted_ms")]
    public double PredictedMilliseconds { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("prompt_tokens_per_second")]
    public double PromptTokensPerSecond { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("predicted_tokens_per_second")]
    public double PredictedTokensPerSecond { get; init; }

    // Aliases for common property names
    public double PromptMs => PromptMilliseconds;
    public double PredictedMs => PredictedMilliseconds;
}

// ── Server Props ─────────────────────────────────────────────────────────────

public record ServerProps
{
    [System.Text.Json.Serialization.JsonPropertyName("total_slots")]
    public int TotalSlots { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("model_path")]
    public string ModelPath { get; init; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("chat_template")]
    public string ChatTemplate { get; init; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("default_generation_settings")]
    public DefaultGenerationSettings DefaultGenerationSettings { get; init; } = new();
}

public record DefaultGenerationSettings
{
    public double Temperature { get; init; }
    public int MaxTokens { get; init; }
    public double TopP { get; init; }
}

// ── Health Status ────────────────────────────────────────────────────────────

public record HealthStatus
{
    public bool Healthy { get; init; }
    public string Status { get; init; } = "";
    public int SlotsTotal { get; init; }
    public int SlotsDeferred { get; init; }
    public int SlotsProcessing { get; init; }

    /// <summary>Convenience property - true if Healthy is true.</summary>
    public bool IsOk => Healthy;
}

// ── Tokenize ─────────────────────────────────────────────────────────────────

public record TokenizeResponse
{
    public IReadOnlyList<int> Tokens { get; init; } = [];
    public int Count { get; init; }
}

// ── Embeddings ───────────────────────────────────────────────────────────────

public record EmbeddingsRequest
{
    public string Model { get; init; } = "";
    public string Input { get; init; } = "";
    public bool Normalize { get; init; }
}

public record EmbeddingsResponse
{
    public string Id { get; init; } = "";
    public IReadOnlyList<EmbeddingData> Data { get; init; } = [];
    public EmbeddingUsage Usage { get; init; } = new();
}

public record EmbeddingData
{
    public string Object { get; init; } = "";
    public int Index { get; init; }
    public IReadOnlyList<float> Embedding { get; init; } = [];
}

public record EmbeddingUsage
{
    public int PromptTokens { get; init; }
    public int TotalTokens { get; init; }
}

// ── Anthropic Message API ────────────────────────────────────────────────────

public record AnthropicMessageRequest
{
    public string Model { get; init; } = "";
    public int MaxTokens { get; init; } = 1024;
    public IReadOnlyList<AnthropicMessage> Messages { get; init; } = [];
    public IReadOnlyList<AnthropicMessage>? System { get; init; }
    public double? Temperature { get; init; }
    public IReadOnlyList<string>? StopSequences { get; init; }
}

public record AnthropicMessage
{
    public string Role { get; init; } = "";
    public IReadOnlyList<AnthropicContent> Content { get; init; } = [];
}

public record AnthropicContent
{
    public string Type { get; init; } = "";
    public string? Text { get; init; }
}

public record AnthropicMessageResponse
{
    public string Id { get; init; } = "";
    public string Model { get; init; } = "";
    public IReadOnlyList<AnthropicContentBlock> Content { get; init; } = [];
    public string Role { get; init; } = "";
    public AnthropicUsage Usage { get; init; } = new();
}

public record AnthropicContentBlock
{
    public string Type { get; init; } = "";
    public string? Text { get; init; }
}

public record AnthropicUsage
{
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
}
