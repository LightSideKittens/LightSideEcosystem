using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LightSide;

/// <summary>
/// Conformance test runner for BidiTest.txt (UAX #9).
/// Unlike BidiCharacterTest.txt which uses explicit codepoints,
/// BidiTest.txt uses BiDi class names (L, R, EN, etc.) which are
/// mapped to representative codepoints for testing.
/// </summary>
internal sealed class BidiTestRunner
{
    private readonly BidiEngine bidiEngine;

    /// <summary>
    /// Maps BiDi class names to representative Unicode codepoints.
    /// Uses constants from UnicodeData where available.
    /// </summary>
    private static readonly Dictionary<string, int> BidiClassToCodepoint = new()
    {
        ["L"] = UnicodeData.LatinCapitalA,
        ["R"] = UnicodeData.HebrewAlef,
        ["AL"] = UnicodeData.ArabicAlef,
        ["EN"] = UnicodeData.DigitZero,
        ["ES"] = UnicodeData.PlusSign,
        ["ET"] = UnicodeData.DollarSign,
        ["AN"] = UnicodeData.ArabicIndicDigitZero,
        ["CS"] = UnicodeData.Comma,
        ["NSM"] = UnicodeData.CombiningGraveAccent,
        ["B"] = UnicodeData.LineFeed,
        ["S"] = UnicodeData.Tab,
        ["WS"] = UnicodeData.Space,
        ["ON"] = UnicodeData.ExclamationMark,
        ["BN"] = UnicodeData.ZeroWidthSpace,
        ["LRE"] = UnicodeData.LeftToRightEmbedding,
        ["LRO"] = UnicodeData.LeftToRightOverride,
        ["RLE"] = UnicodeData.RightToLeftEmbedding,
        ["RLO"] = UnicodeData.RightToLeftOverride,
        ["PDF"] = UnicodeData.PopDirectionalFormat,
        ["LRI"] = UnicodeData.LeftToRightIsolate,
        ["RLI"] = UnicodeData.RightToLeftIsolate,
        ["FSI"] = UnicodeData.FirstStrongIsolate,
        ["PDI"] = UnicodeData.PopDirectionalIsolate,
    };

    public BidiTestRunner(BidiEngine bidiEngine)
    {
        this.bidiEngine = bidiEngine ?? throw new ArgumentNullException(nameof(bidiEngine));
    }

    public BidiTestSummary RunTests(string fileContent, int maxFailuresToLog = 20)
    {
        var summary = new BidiTestSummary();

        if (string.IsNullOrEmpty(fileContent))
        {
            summary.sampleFailures = "BidiTest content is empty or null.";
            return summary;
        }

        var failures = new List<BidiTestFailure>();

        int[] currentLevels = null;
        int[] currentReorder = null;

        using var reader = new StringReader(fileContent);
        string line;
        var lineNumber = 0;

        while ((line = reader.ReadLine()) != null)
        {
            lineNumber++;

            var hashIndex = line.IndexOf('#');
            if (hashIndex >= 0)
                line = line.Substring(0, hashIndex);

            line = line.Trim();
            if (line.Length == 0)
                continue;

            if (line.StartsWith("@Levels:"))
            {
                currentLevels = ParseLevels(line.Substring(8).Trim());
                continue;
            }

            if (line.StartsWith("@Reorder:"))
            {
                currentReorder = ParseReorder(line.Substring(9).Trim());
                continue;
            }

            if (line.StartsWith("@"))
                continue;

            var semicolonIndex = line.IndexOf(';');
            if (semicolonIndex < 0)
            {
                summary.skippedTests++;
                continue;
            }

            var inputPart = line.Substring(0, semicolonIndex).Trim();
            var bitsetPart = line.Substring(semicolonIndex + 1).Trim();

            if (!int.TryParse(bitsetPart, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var bitset))
            {
                summary.skippedTests++;
                continue;
            }

            int[] codepoints;
            try
            {
                codepoints = ParseBidiClasses(inputPart);
            }
            catch
            {
                summary.skippedTests++;
                continue;
            }

            if (codepoints.Length == 0)
            {
                summary.skippedTests++;
                continue;
            }

            for (var dirBit = 1; dirBit <= 4; dirBit <<= 1)
            {
                if ((bitset & dirBit) == 0)
                    continue;

                var paragraphDir = dirBit switch
                {
                    1 => 2,
                    2 => 0,
                    4 => 1,
                    _ => 2
                };

                summary.totalTests++;

                BidiResult result;
                try
                {
                    result = bidiEngine.Process(codepoints, (BidiParagraphDirection)paragraphDir);
                }
                catch (Exception ex)
                {
                    summary.failedTests++;
                    AddFailure(failures, lineNumber, line, paragraphDir,
                        $"Exception: {ex.Message}", maxFailuresToLog);
                    continue;
                }

                if (currentLevels != null && !CompareLevels(currentLevels, result.levels, result.levelsLength, out var levelError))
                {
                    summary.failedTests++;
                    AddFailure(failures, lineNumber, line, paragraphDir, levelError, maxFailuresToLog);
                    continue;
                }

                if (currentReorder != null && !CompareReorder(currentLevels, currentReorder, result.levels,
                        out var reorderError))
                {
                    summary.failedTests++;
                    AddFailure(failures, lineNumber, line, paragraphDir, reorderError, maxFailuresToLog);
                    continue;
                }

                summary.passedTests++;
            }
        }

        summary.sampleFailures = BuildFailuresText(failures, maxFailuresToLog);
        return summary;
    }

    private static int[] ParseBidiClasses(string input)
    {
        var tokens = input.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var result = new int[tokens.Length];

        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i].Trim();
            if (!BidiClassToCodepoint.TryGetValue(token, out var cp))
                throw new ArgumentException($"Unknown BiDi class: {token}");
            result[i] = cp;
        }

        return result;
    }

    private static int[] ParseLevels(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Array.Empty<int>();

        var tokens = input.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var result = new int[tokens.Length];

        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i].Trim();
            if (token.Equals("x", StringComparison.OrdinalIgnoreCase))
                result[i] = -1;
            else
                result[i] = int.Parse(token, System.Globalization.CultureInfo.InvariantCulture);
        }

        return result;
    }

    private static int[] ParseReorder(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Array.Empty<int>();

        var tokens = input.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var result = new List<int>(tokens.Length);

        foreach (var token in tokens)
        {
            if (!token.Equals("x", StringComparison.OrdinalIgnoreCase))
                result.Add(int.Parse(token.Trim(), System.Globalization.CultureInfo.InvariantCulture));
        }

        return result.ToArray();
    }

    private static bool CompareLevels(int[] expected, byte[] actual, int actualLength, out string error)
    {
        error = null;

        if (expected.Length != actualLength)
        {
            error = $"Levels length mismatch: expected {expected.Length}, got {actualLength}";
            return false;
        }

        for (var i = 0; i < expected.Length; i++)
        {
            if (expected[i] == -1) continue;

            if (actual[i] != expected[i])
            {
                error = $"Level mismatch at {i}: expected {expected[i]}, got {actual[i]}";
                return false;
            }
        }

        return true;
    }

    private static bool CompareReorder(int[] expectedLevels, int[] expectedReorder, byte[] actualLevels,
        out string error)
    {
        error = null;

        if (expectedReorder == null || expectedReorder.Length == 0)
            return true;

        if (expectedLevels == null || expectedLevels.Length != actualLevels.Length)
            return true;

        var visibleIndices = new List<int>();
        for (var i = 0; i < expectedLevels.Length; i++)
        {
            if (expectedLevels[i] != -1)
                visibleIndices.Add(i);
        }

        if (visibleIndices.Count != expectedReorder.Length)
        {
            error = $"Reorder length mismatch: expected {expectedReorder.Length}, visible {visibleIndices.Count}";
            return false;
        }

        if (visibleIndices.Count == 0)
            return true;

        var filteredLevels = new byte[visibleIndices.Count];
        for (var i = 0; i < visibleIndices.Count; i++)
            filteredLevels[i] = actualLevels[visibleIndices[i]];

        var indexMap = new int[filteredLevels.Length];
        BidiEngine.ReorderLine(filteredLevels, 0, filteredLevels.Length - 1, indexMap);

        var actualReorder = new int[filteredLevels.Length];
        for (var visualIndex = 0; visualIndex < filteredLevels.Length; visualIndex++)
        {
            var filteredLogicalIndex = indexMap[visualIndex];
            if ((uint)filteredLogicalIndex >= (uint)visibleIndices.Count)
            {
                error = $"Invalid reorder index {filteredLogicalIndex}";
                return false;
            }
            actualReorder[visualIndex] = visibleIndices[filteredLogicalIndex];
        }

        for (var i = 0; i < expectedReorder.Length; i++)
        {
            if (actualReorder[i] != expectedReorder[i])
            {
                error = $"Reorder mismatch at {i}: expected {expectedReorder[i]}, got {actualReorder[i]}";
                return false;
            }
        }

        return true;
    }

    private static void AddFailure(List<BidiTestFailure> failures, int lineNumber, string line,
        int paragraphDir, string message, int maxFailures)
    {
        if (failures.Count >= maxFailures)
            return;

        failures.Add(new BidiTestFailure
        {
            lineNumber = lineNumber,
            line = line,
            paragraphDir = paragraphDir,
            message = message
        });
    }

    private static string BuildFailuresText(List<BidiTestFailure> failures, int maxFailures)
    {
        if (failures == null || failures.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        var count = Math.Min(failures.Count, maxFailures);

        for (var i = 0; i < count; i++)
        {
            var f = failures[i];
            var dirName = f.paragraphDir switch { 0 => "LTR", 1 => "RTL", 2 => "Auto", _ => "?" };
            sb.AppendLine($"- Line {f.lineNumber} ({dirName}): {f.message}");
            sb.AppendLine($"  Input: {f.line}");
        }

        return sb.ToString();
    }
}

internal sealed class BidiTestFailure
{
    public int lineNumber;
    public string line;
    public int paragraphDir;
    public string message;
}

public struct BidiTestSummary
{
    public int totalTests;
    public int passedTests;
    public int failedTests;
    public int skippedTests;
    public string sampleFailures;
}
