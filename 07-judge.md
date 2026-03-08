# Seevalocal — Part 6: LLM-as-Judge

> **Read `00-conventions.md` before this file.**  
> Interfaces referenced here are defined in `10-interfaces.md`.  
> This part is implemented in project `Seevalocal.Judge`.

---

## 1. Responsibilities

- Provide a `JudgeStage` (`IEvalStage`) that sends the item's prompt, expected output, and actual LLM response to a second LLM for scoring.
- Support configurable judge prompt templates.
- Parse structured judge responses (numeric score, pass/fail, free-text rationale).
- Support a separate judge endpoint with its own `ServerConfig` and `LlamaServerSettings`.
- Manage the judge endpoint's concurrency independently from the primary endpoint.

---

## 2. JudgeConfig

```csharp
public record JudgeConfig
{
    /// <summary>
    /// Connection to the judge LLM endpoint.
    /// May be a second local llama-server, a remote OpenAI-compatible endpoint, etc.
    /// If Manage = true, a second llama-server process is started.
    /// </summary>
    public ServerConfig ServerConfig { get; init; } = new();

    /// <summary>LlamaServerSettings for the judge endpoint (if managed).</summary>
    public LlamaServerSettings? ServerSettings { get; init; }

    /// <summary>
    /// Jinja2-style template for the judge prompt.
    /// Available variables: {prompt}, {expectedOutput}, {actualOutput}, {metadata.*}
    /// </summary>
    public string JudgePromptTemplate { get; init; } = DefaultTemplates.Standard;

    /// <summary>
    /// How to parse the judge's response.
    /// </summary>
    public JudgeResponseFormat ResponseFormat { get; init; } = JudgeResponseFormat.NumericScore;

    /// <summary>
    /// For NumericScore format: the range expected from the judge.
    /// The score is normalized to [0, 1] for the judgeScoreRatio metric.
    /// </summary>
    public double ScoreMinValue { get; init; } = 0.0;
    public double ScoreMaxValue { get; init; } = 10.0;

    /// <summary>System prompt for the judge LLM.</summary>
    public string? JudgeSystemPrompt { get; init; }

    /// <summary>Max tokens the judge is allowed to generate.</summary>
    public int JudgeMaxTokens { get; init; } = 512;

    /// <summary>Temperature for judge responses. Lower = more deterministic scoring.</summary>
    public double JudgeSamplingTemperature { get; init; } = 0.0;
}

public enum JudgeResponseFormat
{
    /// <summary>Judge outputs a number (e.g., "7.5" or "Score: 7.5/10").</summary>
    NumericScore,

    /// <summary>Judge outputs "PASS" or "FAIL" (case-insensitive).</summary>
    PassFail,

    /// <summary>
    /// Judge outputs JSON: {"score": 7.5, "rationale": "...", "passed": true}
    /// </summary>
    StructuredJson,
}
```

---

## 3. Default Judge Prompt Templates

```csharp
public static class DefaultTemplates
{
    public const string Standard = """
        You are an expert evaluator. You will be given a task prompt, an expected output, and an actual output produced by an AI model.

        Task Prompt:
        {prompt}

        Expected Output:
        {expectedOutput}

        Actual Output:
        {actualOutput}

        Please evaluate the quality of the Actual Output on a scale from 0 to 10, where:
        - 0 = completely wrong or irrelevant
        - 5 = partially correct
        - 10 = perfect match in meaning and quality

        Respond with only a single number between 0 and 10.
        """; // TODO: never expect a response with only a single number; LLMs are terrible at that kind of judgment. // TODO: also, it needs a rubric. HOW should the outputs match? Does 'expected output' DESCRIBE the output or is it an EXAMPLE of what you want or EXACTLY what you want? etc.

    public const string PassFail = """
        You are an expert evaluator. Determine whether the Actual Output correctly answers the Task Prompt given the Expected Output as a reference.

        Task Prompt:
        {prompt}

        Expected Output:
        {expectedOutput}

        Actual Output:
        {actualOutput}

        Respond with exactly one word: PASS if the actual output is correct, or FAIL if it is not.
        """; // TODO: never expect a response with only a single word; LLMs are terrible at that kind of judgment.

    public const string StructuredJson = """
        You are an expert evaluator. Evaluate the Actual Output and respond with a JSON object.

        Task Prompt:
        {prompt}

        Expected Output:
        {expectedOutput}

        Actual Output:
        {actualOutput}

        Respond ONLY with a JSON object in this exact format (no markdown, no explanation other than the rationale field, and rationale MUST come first):
        {"rationale": "<one sentence>", "score": <0-10>, "passed": <true|false>}
        """;
}
```

---

## 4. JudgeStage

```csharp
public sealed class JudgeStage : IEvalStage
{
    public string StageName => "JudgeStage";

    private readonly JudgeConfig _config;
    private readonly JudgePromptRenderer _renderer;
    private readonly JudgeResponseParser _parser;

    public JudgeStage(JudgeConfig config, ILogger<JudgeStage> logger);

    public async Task<StageResult> ExecuteAsync(EvalStageContext context)
    {
        var judgeClient = context.JudgeClient
            ?? throw new InvalidOperationException("JudgeStage requires a judge client.");

        var actualOutput = context.StageOutputs.GetValueOrDefault("PromptStage.response") as string
            ?? "";

        var judgePrompt = _renderer.Render(
            _config.JudgePromptTemplate,
            context.Item.UserPrompt,
            context.Item.ExpectedOutput ?? "",
            actualOutput,
            context.Item.Metadata);

        var response = await judgeClient.ChatCompletionAsync(new ChatCompletionRequest
        {
            Model = "", // use whatever model the judge server has loaded
            Messages = [
                new ChatMessage { Role = "system", Content = _config.JudgeSystemPrompt ?? "" },
                new ChatMessage { Role = "user", Content = judgePrompt }
            ],
            MaxTokens = _config.JudgeMaxTokens,
            Temperature = _config.JudgeSamplingTemperature,
        }, context.CancellationToken);

        if (!response.IsSuccess)
            return new StageResult { Succeeded = false, FailureReason = response.Errors.FirstOrDefault()?.Message };

        var judgeText = response.Value.Choices[0].Message.Content;
        var parsed = _parser.Parse(judgeText, _config);

        return new StageResult
        {
            Succeeded = parsed.ParseSucceeded,
            FailureReason = parsed.ParseSucceeded ? null : $"Could not parse judge response: {judgeText}",
            Outputs = new Dictionary<string, object?>
            {
                ["JudgeStage.rawResponse"] = judgeText,
                ["JudgeStage.score"] = parsed.NormalizedScore,
                ["JudgeStage.passed"] = parsed.Passed,
                ["JudgeStage.rationale"] = parsed.Rationale,
            },
            Metrics =
            [
                new MetricValue { Name = "judgeScoreRatio", Value = new MetricScalar.DoubleMetric(parsed.NormalizedScore ?? 0.0), SourceStage = StageName },
                new MetricValue { Name = "judgePassedBool", Value = new MetricScalar.BoolMetric(parsed.Passed ?? false), SourceStage = StageName },
            ]
        };
    }
}
```

---

## 5. JudgeResponseParser

```csharp
public sealed class JudgeResponseParser
{
    public ParsedJudgeResponse Parse(string rawText, JudgeConfig config);
}

public record ParsedJudgeResponse
{
    public bool ParseSucceeded { get; init; }
    /// <summary>Normalized score in [0, 1]. Null if not applicable.</summary>
    public double? NormalizedScore { get; init; }
    public bool? Passed { get; init; }
    public string? Rationale { get; init; }
}
```

### 5.1 Parsing Strategies

**NumericScore**: Extract first floating-point number from response. Normalize: `(raw - min) / (max - min)`. Clamp to [0, 1].

**PassFail**: Regex `\b(PASS|FAIL)\b` (case-insensitive). Normalize: PASS → score 1.0, FAIL → score 0.0.

**StructuredJson**: Strip markdown fences if present, then `JsonSerializer.Deserialize`. Extract `score`, `passed`, `rationale` fields.

---

## 6. Judge Endpoint Lifecycle

The judge endpoint is managed by the same `LlamaServerManager` used for the primary endpoint, but as a separate instance. The `PipelineOrchestrator` receives a pre-started `LlamaServerClient` for the judge.

If the judge and primary endpoints are the same URL (e.g., the user only has one GPU), they share a client and the concurrency semaphore is sized from a single `total_slots`.

---

## 7. JudgePromptRenderer

```csharp
public sealed class JudgePromptRenderer
{
    /// <summary>
    /// Substitutes {prompt}, {expectedOutput}, {actualOutput}, {metadata.KEY} in template.
    /// </summary>
    public string Render(
        string template,
        string userPrompt,
        string expectedOutput,
        string actualOutput,
        IReadOnlyDictionary<string, string> metadata);
}
```

Unknown `{metadata.KEY}` placeholders are replaced with an empty string and a warning is logged.

---

## 8. Unit Tests (Seevalocal.Judge.Tests)

| Test class | Coverage |
|---|---|
| `JudgeResponseParserTests` | Numeric: integer, float, "Score: 7.5/10"; PassFail: mixed case; JSON: valid and malformed |
| `JudgePromptRendererTests` | All placeholders substituted; unknown metadata key → empty + warning |
| `JudgeStageTests` | Mock judge client; metric values correct; parse failure → stage failure |
| `JudgeNormalizationTests` | Score min/max normalization; clamping outside range |
