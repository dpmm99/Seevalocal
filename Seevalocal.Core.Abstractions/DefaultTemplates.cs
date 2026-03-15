namespace Seevalocal.Core;

/// <summary>
/// Built-in Jinja2-style judge prompt templates.
/// Variables: {prompt}, {expectedOutput}, {actualOutput}, {metadata.KEY}
/// 
/// Design principles based on LLM next-token prediction:
/// 1. XML delimiters for clear structure (models trained on XML-heavy data)
/// 2. Chain-of-thought BEFORE scoring (forces reasoning before commitment)
/// 3. Response format at END (recency bias - format is fresh in context)
/// 4. Explicit negative constraints ("DO NOT...")
/// 5. Concrete examples when possible
/// </summary>
public static class DefaultTemplates
{
    /// <summary>
    /// Asks the judge to score the actual output on a 0–10 scale.
    /// Uses StructuredJson response format for robustness.
    /// 
    /// LLM First Principles:
    /// - XML tags for clear section boundaries
    /// - Explicit CoT instruction BEFORE score (reasoning before commitment)
    /// - Response format at END (recency bias)
    /// - Explicit "DO NOT" constraints
    /// </summary>
    public const string Standard = """
        You are an expert evaluator. Your task is to evaluate the quality of an AI model's output.

        <instructions>
        1. Read the Task Prompt, Expected Output, and Actual Output carefully.
        2. Think through your reasoning step-by-step BEFORE assigning any score.
        3. Consider accuracy, completeness, and quality.
        4. Output ONLY a JSON object - no markdown, no explanations outside the JSON.
        </instructions>

        <task-prompt>
        {prompt}
        </task-prompt>

        <expected-output>
        {expectedOutput}
        </expected-output>

        <actual-output>
        {actualOutput}
        </actual-output>

        <evaluation-criteria>
        - Accuracy (50 points): Does the meaning match the expected output?
        - Completeness (30 points): Does it address the full scope of the prompt?
        - Quality (20 points): Is it well-formed, clear, and error-free?
        </evaluation-criteria>

        <response-format>
        Respond ONLY with a JSON object in this exact format:
        {"rationale": "<your step-by-step reasoning here>", "score": <number 0-100>}

        IMPORTANT: 
        - The "rationale" field MUST contain your reasoning BEFORE you give the score.
        - The "score" field MUST be a number between 0 and 100.
        - DO NOT include any text outside the JSON object.
        - DO NOT use markdown code fences like ```json.
        </response-format>
        """;

    /// <summary>
    /// Asks the judge to give a binary PASS/FAIL verdict.
    /// Uses StructuredJson response format for robustness.
    /// </summary>
    public const string PassFail = """
        You are an expert evaluator. Determine whether the Actual Output correctly answers the Task Prompt.

        <instructions>
        1. Compare the Actual Output to the Expected Output.
        2. Think through whether they convey the same essential information.
        3. Minor wording differences are acceptable; factual errors are not.
        4. Output ONLY a JSON object - no other text.
        </instructions>

        <task-prompt>
        {prompt}
        </task-prompt>

        <expected-output>
        {expectedOutput}
        </expected-output>

        <actual-output>
        {actualOutput}
        </actual-output>

        <pass-criteria>
        - The actual output conveys the same essential information as the expected output.
        - No factual errors or critical omissions.
        </pass-criteria>

        <response-format>
        Respond ONLY with a JSON object in this exact format:
        {"rationale": "<your reasoning here>", "score": <10 if passed else 0>, "passed": <true|false>}

        IMPORTANT:
        - The "rationale" MUST explain your reasoning BEFORE the verdict.
        - The "passed" field MUST be true or false (lowercase, no quotes).
        - DO NOT include any text outside the JSON object.
        </response-format>
        """;

    /// <summary>
    /// Asks the judge to respond with a fully structured JSON object.
    /// Preferred format when the pipeline needs score, rationale, and pass/fail together.
    /// </summary>
    public const string StructuredJson = """
        You are an expert evaluator. Evaluate the Actual Output and respond with a JSON object.

        <instructions>
        1. Read all inputs carefully.
        2. Think through your reasoning step-by-step.
        3. Assign a score based on the rubric.
        4. Output ONLY the JSON - no markdown, no extra text.
        </instructions>

        <task-prompt>
        {prompt}
        </task-prompt>

        <expected-output>
        {expectedOutput}
        </expected-output>

        <actual-output>
        {actualOutput}
        </actual-output>

        <scoring-rubric>
        0  — Completely wrong or irrelevant
        5  — Partially correct
        10 — Perfect match in meaning and quality
        </scoring-rubric>

        <response-format>
        Respond ONLY with a JSON object in this exact format:
        {"rationale": "<your step-by-step reasoning>", "score": <0-10>, "passed": <true|false>}

        IMPORTANT:
        - The "rationale" MUST come FIRST and contain your reasoning.
        - The "score" MUST be a number.
        - The "passed" MUST be true or false (lowercase).
        - DO NOT include any text outside the JSON.
        - DO NOT use markdown code fences.
        </response-format>
        """;

    /// <summary>
    /// Template for translation evaluation. Scores translation accuracy on 0-10 scale.
    /// 
    /// LLM First Principles:
    /// - Clear source/target language specification
    /// - Explicit evaluation criteria
    /// - CoT before scoring
    /// </summary>
    public const string TranslationJudgeTemplate = """
        You are an expert translator evaluator. Evaluate the quality of a translation.

        <instructions>
        1. Compare the model's translation to the reference translation.
        2. Consider accuracy, fluency, and naturalness.
        3. Think through your reasoning BEFORE assigning a score.
        4. Output ONLY a JSON object.
        </instructions>

        <source-text>
        {prompt}
        </source-text>

        <reference-translation>
        {expectedOutput}
        </reference-translation>

        <model-translation>
        {actualOutput}
        </model-translation>

        <scoring-rubric>
        0  — Wrong language or gibberish
        3  — Major errors, wrong meaning
        5  — Partially correct with significant errors
        7  — Good translation with minor issues
        10 — Perfect, natural, equivalent meaning
        </scoring-rubric>

        <response-format>
        Respond ONLY with a JSON object in this exact format:
        {"rationale": "<your step-by-step reasoning>", "score": <0-10>, "passed": <true if score >= 6 else false>}

        IMPORTANT:
        - The "rationale" MUST explain your reasoning first.
        - The "score" MUST be a number 0-10.
        - The "passed" MUST be true or false.
        - DO NOT include any text outside the JSON.
        </response-format>
        """;

    /// <summary>
    /// Template for casual Q&A evaluation. Scores semantic correctness.
    /// 
    /// LLM First Principles:
    /// - Focus on semantic equivalence, not exact wording
    /// - Clear pass/fail threshold
    /// - CoT before scoring
    /// </summary>
    public const string CasualQAJudgeTemplate = """
        You are an expert evaluator of conversational responses.

        <instructions>
        1. Compare the model's answer to the expected answer.
        2. Focus on SEMANTIC EQUIVALENCE, not exact wording.
        3. Minor phrasing differences are acceptable.
        4. Factual errors or missing key information are not acceptable.
        5. Think through your reasoning BEFORE assigning a score.
        6. Output ONLY a JSON object.
        </instructions>

        <question>
        {prompt}
        </question>

        <expected-answer>
        {expectedOutput}
        </expected-answer>

        <model-answer>
        {actualOutput}
        </model-answer>

        <scoring-rubric>
        0  — Completely wrong or irrelevant
        3  — Missing key information or has factual errors
        5  — Partially correct but incomplete
        7  — Mostly correct with minor issues
        10 — Semantically equivalent to expected answer
        </scoring-rubric>

        <response-format>
        Respond ONLY with a JSON object in this exact format:
        {"rationale": "<your step-by-step reasoning>", "score": <0-10>, "passed": <true if score >= 6 else false>}

        IMPORTANT:
        - The "rationale" MUST explain your reasoning first.
        - The "score" MUST be a number 0-10.
        - The "passed" MUST be true or false.
        - DO NOT include any text outside the JSON.
        </response-format>
        """;

    /// <summary>
    /// Template for code quality evaluation. Scores code correctness and style.
    /// 
    /// LLM First Principles:
    /// - Explicit criteria for code evaluation
    /// - Clear scoring rubric with concrete thresholds
    /// - CoT before scoring
    /// </summary>
    public const string CodeQualityJudgeTemplate = """
        You are an expert code reviewer. Evaluate the quality of generated code.

        <instructions>
        1. Read the task prompt and understand the requirements.
        2. Compare the generated code to the reference solution.
        3. Evaluate correctness, style, and completeness.
        4. Think through your reasoning BEFORE assigning a score.
        5. Output ONLY a JSON object.
        </instructions>

        <task-prompt>
        {prompt}
        </task-prompt>

        <reference-solution>
        {expectedOutput}
        </reference-solution>

        <generated-code>
        {actualOutput}
        </generated-code>

        <evaluation-criteria>
        - Correctness: Does the code solve the problem correctly?
        - Style: Is it idiomatic, well-formatted, and readable?
        - Completeness: Does it handle edge cases and errors?
        - Efficiency: Is the approach reasonable (not necessarily optimal)?
        </evaluation-criteria>

        <scoring-rubric>
        0  — Does not compile or completely wrong approach
        3  — Compiles but has major bugs or wrong logic
        5  — Works but has significant issues (bugs, style, edge cases)
        7  — Good solution with minor issues
        10 — Correct, clean, idiomatic, handles edge cases
        </scoring-rubric>

        <response-format>
        Respond ONLY with a JSON object in this exact format:
        {"rationale": "<your step-by-step reasoning>", "score": <0-10>, "passed": <true if score >= 6 else false>}

        IMPORTANT:
        - The "rationale" MUST explain your reasoning first.
        - The "score" MUST be a number 0-10.
        - The "passed" MUST be true or false.
        - DO NOT include any text outside the JSON.
        - DO NOT use markdown code fences.
        </response-format>
        """;
}
