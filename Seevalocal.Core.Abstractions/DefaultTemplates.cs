namespace Seevalocal.Core;

/// <summary>
/// Built-in Jinja2-style judge prompt templates.
/// Variables: {prompt}, {expectedOutput}, {actualOutput}, {metadata.KEY}
/// </summary>
public static class DefaultTemplates
{
    /// <summary>
    /// Asks the judge to score the actual output on a 0–10 scale.
    /// Uses StructuredJson response format for robustness.
    /// </summary>
    public const string Standard = """
        You are an expert evaluator. You will be given a task prompt, an expected output, and an actual output produced by an AI model.

        <TaskPrompt>
        {prompt}
        </TaskPrompt>

        <ExpectedOutput>
        {expectedOutput}
        </ExpectedOutput>

        <ActualOutput>
        {actualOutput}
        </ActualOutput>

        Evaluate the quality of the Actual Output. Consider:
        - Accuracy, 50 points: Does it match the expected output in meaning and content?
        - Completeness, 30 points: Does it address the full scope of the prompt?
        - Quality, 20 points: Is it well-formed, clear, and free of errors?

        Respond ONLY with a JSON object in this exact format (rationale MUST come first and MUST consider all parts of the rubric verbally BEFORE giving numbers):
        {"rationale": "<one sentence explaining your score>", "score": <0-100>}
        """;

    /// <summary>
    /// Asks the judge to give a binary PASS/FAIL verdict.
    /// Uses StructuredJson response format for robustness.
    /// </summary>
    public const string PassFail = """
        You are an expert evaluator. Determine whether the Actual Output correctly answers the Task Prompt, using the Expected Output as a reference.

        Task Prompt:
        {prompt}

        Expected Output:
        {expectedOutput}

        Actual Output:
        {actualOutput}

        Criteria for PASS:
        - The actual output conveys the same essential information or meaning as the expected output.
        - Minor wording differences are acceptable; factual errors or omissions are not.

        Respond ONLY with a JSON object in this exact format (rationale MUST come first):
        {"rationale": "<one sentence explaining your verdict>", "score": <10 if passed else 0>, "passed": <true|false>}
        """;

    /// <summary>
    /// Asks the judge to respond with a fully structured JSON object.
    /// Preferred format when the pipeline needs score, rationale, and pass/fail together.
    /// </summary>
    public const string StructuredJson = """
        You are an expert evaluator. Evaluate the Actual Output and respond with a JSON object.

        Task Prompt:
        {prompt}

        Expected Output:
        {expectedOutput}

        Actual Output:
        {actualOutput}

        Scoring rubric (0–10):
        0  — Completely wrong or irrelevant
        5  — Partially correct
        10 — Perfect match in meaning and quality

        Respond ONLY with a JSON object in this exact format (no markdown, no extra text, rationale MUST come first):
        {"rationale": "<one sentence>", "score": <0-10>, "passed": <true|false>}
        """;

    /// <summary>
    /// Template for translation evaluation. Scores translation accuracy on 0-10 scale.
    /// </summary>
    public const string TranslationJudgeTemplate = """
        You are an expert translator evaluator. Evaluate the quality of a translation.

        Task Prompt:
        {prompt}

        Expected Output (reference translation):
        {expectedOutput}

        Actual Output (model translation):
        {actualOutput}

        Scoring rubric (0–10):
        0  — Completely wrong language or gibberish
        5  — Partially correct translation with significant errors
        10 — Perfect, natural translation with equivalent meaning

        Respond ONLY with a JSON object in this exact format (rationale MUST come first):
        {"rationale": "<one sentence>", "score": <0-10>, "passed": <true if score >= 6>}
        """;

    /// <summary>
    /// Template for casual Q&A evaluation. Scores semantic correctness.
    /// </summary>
    public const string CasualQAJudgeTemplate = """
        You are an expert evaluator of conversational responses.

        Task Prompt:
        {prompt}

        Expected Output:
        {expectedOutput}

        Actual Output:
        {actualOutput}

        Evaluate whether the Actual Output conveys the same essential information as the Expected Output.
        Minor wording differences are acceptable; factual errors are not.

        Scoring rubric (0–10):
        0  — Completely wrong or irrelevant
        5  — Partially correct but missing key information
        10 — Semantically equivalent to expected output

        Respond ONLY with a JSON object in this exact format (rationale MUST come first):
        {"rationale": "<one sentence>", "score": <0-10>, "passed": <true if score >= 6>}
        """;

    /// <summary>
    /// Template for code quality evaluation. Scores code correctness and style.
    /// </summary>
    public const string CodeQualityJudgeTemplate = """
        You are an expert code reviewer. Evaluate the quality of generated code.

        Task Prompt:
        {prompt}

        Expected Output (reference solution):
        {expectedOutput}

        Actual Output (generated code):
        {actualOutput}

        Evaluate the code for:
        - Correctness: Does it solve the problem?
        - Style: Is it idiomatic and well-formatted?
        - Completeness: Does it handle edge cases?

        Scoring rubric (0–10):
        0  — Does not compile or completely wrong
        5  — Compiles but has significant bugs or style issues
        10 — Correct, clean, idiomatic code

        Respond ONLY with a JSON object in this exact format (rationale MUST come first):
        {"rationale": "<one sentence>", "score": <0-10>, "passed": <true if score >= 6>}
        """;
}
