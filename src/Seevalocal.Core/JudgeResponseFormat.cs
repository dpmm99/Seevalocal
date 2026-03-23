namespace Seevalocal.Core;

public enum JudgeResponseFormat
{
    /// <summary>Judge outputs a number (e.g., "7.5" or "Score: 7.5/10").</summary>
    NumericScore,

    /// <summary>Judge outputs "PASS" or "FAIL" (case-insensitive).</summary>
    PassFail,

    /// <summary>
    /// Judge outputs JSON: {"rationale": "...", "score": 7.5 }
    /// This is the most robust format and is recommended over NumericScore/PassFail.
    /// </summary>
    StructuredJson,
}
