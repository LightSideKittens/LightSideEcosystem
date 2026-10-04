using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LightSide;

internal sealed unsafe class WordBreakConformanceRunner
{
    private readonly UnicodeDataProvider provider;
    private byte[] scratch = Array.Empty<byte>();

    public WordBreakConformanceRunner(UnicodeDataProvider provider)
    {
        this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public WordBreakConformanceSummary RunTests(string testFileContent, int maxFailuresToLog = 20)
    {
        var summary = new WordBreakConformanceSummary();
        if (string.IsNullOrEmpty(testFileContent))
        {
            summary.sampleFailures = "WordBreakTest content is empty or null.";
            return summary;
        }

        var failures = new StringBuilder();
        using var reader = new System.IO.StringReader(testFileContent);
        string line;
        var lineNumber = 0;

        while ((line = reader.ReadLine()) != null)
        {
            lineNumber++;
            var hash = line.IndexOf('#');
            if (hash >= 0) line = line.Substring(0, hash);
            line = line.Trim();
            if (line.Length == 0) continue;

            if (!TryParseTestCase(line, out var codepoints, out var expectedBreaks))
            {
                summary.skippedTests++;
                if (summary.skippedTests <= maxFailuresToLog)
                    failures.AppendLine($"Line {lineNumber}: Failed to parse '{line}'");
                continue;
            }

            summary.totalTests++;
            try
            {
                var actualBreaks = Resolve(codepoints);
                var mismatch = FindMismatch(expectedBreaks, actualBreaks);
                if (mismatch < 0)
                {
                    summary.passedTests++;
                    continue;
                }

                summary.failedTests++;
                if (summary.failedTests <= maxFailuresToLog)
                {
                    var expected = expectedBreaks[mismatch] ? "\u00F7" : "\u00D7";
                    var actual = actualBreaks[mismatch] ? "\u00F7" : "\u00D7";
                    failures.AppendLine(
                        $"Line {lineNumber}: Position {mismatch} - expected {expected}, got {actual}");
                    failures.AppendLine($"  Input: {FormatCodepoints(codepoints)}");
                }
            }
            catch (Exception exception)
            {
                summary.failedTests++;
                if (summary.failedTests <= maxFailuresToLog)
                    failures.AppendLine($"Line {lineNumber}: Exception - {exception.Message}");
            }
        }

        summary.sampleFailures = failures.ToString();
        return summary;
    }

    private bool[] Resolve(int[] codepoints)
    {
        var length = codepoints.Length;
        var result = new bool[length + 1];
        if (length == 0)
        {
            result[0] = true;
            return result;
        }

        if (scratch.Length < length) scratch = new byte[length];
        fixed (int* cp = codepoints)
        fixed (bool* bp = result)
        fixed (byte* ws = scratch)
            UniTextWordBurst.Resolve(cp, length, provider.Tables, ws, (byte*)bp);
        return result;
    }

    private static int FindMismatch(bool[] expected, bool[] actual)
    {
        if (expected.Length != actual.Length) return 0;
        for (var i = 0; i < expected.Length; i++)
            if (expected[i] != actual[i])
                return i;
        return -1;
    }

    private static bool TryParseTestCase(string line, out int[] codepoints, out bool[] breaks)
    {
        var codepointList = new List<int>();
        var breakList = new List<bool>();
        var tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var token in tokens)
        {
            if (token == "\u00F7") breakList.Add(true);
            else if (token == "\u00D7") breakList.Add(false);
            else if (int.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var codepoint))
                codepointList.Add(codepoint);
            else
            {
                codepoints = Array.Empty<int>();
                breaks = Array.Empty<bool>();
                return false;
            }
        }

        codepoints = codepointList.ToArray();
        breaks = breakList.ToArray();
        return breaks.Length == codepoints.Length + 1;
    }

    private static string FormatCodepoints(int[] codepoints)
    {
        var result = new StringBuilder();
        foreach (var codepoint in codepoints)
        {
            if (result.Length > 0) result.Append(' ');
            result.Append("U+").Append(codepoint.ToString("X4", CultureInfo.InvariantCulture));
        }
        return result.ToString();
    }
}

internal struct WordBreakConformanceSummary
{
    public int totalTests;
    public int passedTests;
    public int failedTests;
    public int skippedTests;
    public string sampleFailures;
}
