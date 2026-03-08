namespace Seevalocal.DataSources.Internal;

internal static class IdGenerator
{
    /// <summary>
    /// Generates an id in the format "{sourceName}-{index:D6}".
    /// </summary>
    public static string Generate(string sourceName, int zeroBasedIndex)
        => $"{sourceName}-{zeroBasedIndex:D6}";
}
