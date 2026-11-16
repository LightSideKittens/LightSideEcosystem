using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using LightSide;

internal sealed class BidiConformanceRunner
{
    private readonly BidiEngine bidiEngine;

    public BidiConformanceRunner(BidiEngine bidiEngine)
    {
        this.bidiEngine = bidiEngine ?? throw new ArgumentNullException(nameof(bidiEngine));
    }

    public BidiConformanceSummary RunBidiCharacterTests(string fileContent, int maxFailuresToLog = 20)
    {
        var summary = new BidiConformanceSummary
        {
            totalEvaluatedTests = 0,
            passedTests = 0,
            failedTests = 0,
            skippedTests = 0,
            sampleFailures = string.Empty
        };

        if (string.IsNullOrEmpty(fileContent))
        {
            summary.sampleFailures = "BidiCharacterTest content is empty or null.";
            return summary;
        }

        var totalEvaluated = 0;
        var passed = 0;
        var failed = 0;
        var skipped = 0;

        var failures = new List<BidiConformanceFailure>();

        using (var reader = new StringReader(fileContent))
        {
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

                var fields = line.Split(';');
                if (fields.Length < 5)
                {
                    skipped++;
                    continue;
                }

                var codePointsField = fields[0].Trim();
                var paragraphDirField = fields[1].Trim();
                var paragraphLevelField = fields[2].Trim();
                var expectedLevelsField = fields[3].Trim();
                var expectedReorderField = fields[4].Trim();

                if (!int.TryParse(paragraphDirField, NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out var paragraphDir))
                {
                    skipped++;
                    continue;
                }

                if (paragraphDir != 0 && paragraphDir != 1 && paragraphDir != 2)
                {
                    skipped++;
                    continue;
                }

                int[] codePoints;
                try
                {
                    codePoints = ParseHexCodePoints(codePointsField);
                }
                catch (Exception ex)
                {
                    skipped++;
                    AddFailure(failures, lineNumber, line,
                        $"Failed to parse code points: {ex.Message}", maxFailuresToLog);
                    continue;
                }

                int?[] expectedLevels;
                try
                {
                    expectedLevels = ParseExpectedLevels(expectedLevelsField);
                }
                catch (Exception ex)
                {
                    skipped++;
                    AddFailure(failures, lineNumber, line,
                        $"Failed to parse expected levels: {ex.Message}", maxFailuresToLog);
                    continue;
                }

                bool hasReorderExpectations;
                int[] expectedReorder;
                try
                {
                    expectedReorder = ParseExpectedReorder(expectedReorderField, out hasReorderExpectations);
                }
                catch (Exception ex)
                {
                    skipped++;
                    AddFailure(failures, lineNumber, line,
                        $"Failed to parse expected reorder: {ex.Message}", maxFailuresToLog);
                    continue;
                }

                if (expectedLevels.Length != codePoints.Length)
                {
                    skipped++;
                    AddFailure(
                        failures,
                        lineNumber,
                        line,
                        $"Expected levels length mismatch: expected {codePoints.Length}, got {expectedLevels.Length}.",
                        maxFailuresToLog);
                }

                totalEvaluated++;

                BidiResult bidiResult;
                try
                {
                    bidiResult = bidiEngine.Process(codePoints, (BidiParagraphDirection)paragraphDir);
                }
                catch (Exception ex)
                {
                    failed++;
                    AddFailure(failures, lineNumber, line,
                        $"BidiEngine.Process threw an exception: {ex.Message}", maxFailuresToLog);
                    continue;
                }

                if (bidiResult.levels == null || bidiResult.levelsLength != codePoints.Length)
                {
                    failed++;
                    AddFailure(
                        failures,
                        lineNumber,
                        line,
                        $"BidiEngine returned levels length {bidiResult.levelsLength}, expected {codePoints.Length}.",
                        maxFailuresToLog);
                    continue;
                }

                if (!CompareLevels(expectedLevels, bidiResult.levels, bidiResult.levelsLength, out var levelsErrorMessage))
                {
                    failed++;
                    AddFailure(failures, lineNumber, line,
                        $"Levels mismatch: {levelsErrorMessage}", maxFailuresToLog);
                    continue;
                }

                if (hasReorderExpectations)
                    if (!CompareReorder(expectedLevels, expectedReorder, bidiResult.levels, bidiResult.levelsLength,
                            out var reorderErrorMessage))
                    {
                        failed++;
                        AddFailure(failures, lineNumber, line,
                            $"Reorder mismatch: {reorderErrorMessage}", maxFailuresToLog);
                        continue;
                    }

                passed++;
            }
        }

        summary.totalEvaluatedTests = totalEvaluated;
        summary.passedTests = passed;
        summary.failedTests = failed;
        summary.skippedTests = skipped;
        summary.sampleFailures = BuildSampleFailuresText(failures, maxFailuresToLog);

        return summary;
    }

    

    private static int[] ParseHexCodePoints(string codePointsField)
    {
        if (string.IsNullOrWhiteSpace(codePointsField))
            return Array.Empty<int>();

        var tokens = codePointsField.Split(
            new[] { ' ' },
            StringSplitOptions.RemoveEmptyEntries);

        var result = new int[tokens.Length];

        for (var i = 0; i < tokens.Length; i++)
            result[i] = int.Parse(tokens[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        return result;
    }


    private static int?[] ParseExpectedLevels(string expectedLevelsField)
    {
        if (string.IsNullOrWhiteSpace(expectedLevelsField))
            return Array.Empty<int?>();

        var tokens = expectedLevelsField.Split(
            new[] { ' ' },
            StringSplitOptions.RemoveEmptyEntries);

        var result = new int?[tokens.Length];

        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];

            if (token.Equals("x", StringComparison.OrdinalIgnoreCase))
                result[i] = null;
            else
                result[i] = int.Parse(token, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        return result;
    }


    private static int[] ParseExpectedReorder(string expectedReorderField, out bool hasReorderExpectations)
    {
        hasReorderExpectations = false;

        if (string.IsNullOrWhiteSpace(expectedReorderField))
            return Array.Empty<int>();

        var tokens = expectedReorderField.Split(
            new[] { ' ' },
            StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length == 0)
            return Array.Empty<int>();

        var indices = new List<int>(tokens.Length);
        var anyIndex = false;

        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];

            if (token.Equals("x", StringComparison.OrdinalIgnoreCase)) continue;

            var logicalIndex = int.Parse(token, NumberStyles.Integer, CultureInfo.InvariantCulture);
            indices.Add(logicalIndex);
            anyIndex = true;
        }

        hasReorderExpectations = anyIndex;
        return indices.ToArray();
    }


    private static bool CompareLevels(int?[] expectedLevels, byte[] actualLevels, int actualLength, out string errorMessage)
    {
        errorMessage = string.Empty;

        if (expectedLevels.Length != actualLength)
        {
            errorMessage =
                $"Levels length mismatch: expected {expectedLevels.Length}, actual {actualLength}.";
            return false;
        }

        for (var i = 0; i < expectedLevels.Length; i++)
        {
            var expected = expectedLevels[i];

            if (!expected.HasValue)
                continue;

            int actual = actualLevels[i];

            if (actual != expected.Value)
            {
                errorMessage =
                    $"Level mismatch at index {i}: expected {expected.Value}, actual {actual}.";
                return false;
            }
        }

        return true;
    }


    private static bool CompareReorder(
        int?[] expectedLevels,
        int[] expectedReorder,
        byte[] actualLevels,
        int actualLength,
        out string errorMessage)
    {
        errorMessage = string.Empty;

        if (expectedReorder == null || expectedReorder.Length == 0) return true;

        if (expectedLevels.Length != actualLength)
        {
            errorMessage =
                $"Reorder check: levels length mismatch: expectedLevels={expectedLevels.Length}, actualLevels={actualLength}.";
            return false;
        }

        var length = expectedLevels.Length;

        var logicalIndices = new List<int>(length);

        for (var i = 0; i < length; i++)
            if (expectedLevels[i].HasValue)
                logicalIndices.Add(i);

        var filteredLength = logicalIndices.Count;

        if (filteredLength != expectedReorder.Length)
        {
            errorMessage =
                $"Reorder length mismatch: expected {expectedReorder.Length}, actual {filteredLength}.";
            return false;
        }

        if (filteredLength == 0) return true;

        var filteredLevels = new byte[filteredLength];

        for (var i = 0; i < filteredLength; i++)
        {
            var logicalIndex = logicalIndices[i];
            filteredLevels[i] = actualLevels[logicalIndex];
        }

        var indexMap = new int[filteredLength];
        BidiEngine.ReorderLine(filteredLevels, 0, filteredLength - 1, indexMap);

        var actualReorder = new int[filteredLength];

        for (var visualIndex = 0; visualIndex < filteredLength; visualIndex++)
        {
            var filteredLogicalIndex = indexMap[visualIndex];

            if ((uint)filteredLogicalIndex >= (uint)filteredLength)
            {
                errorMessage =
                    $"Reorder produced out-of-range filtered logical index {filteredLogicalIndex} for filtered length {filteredLength}.";
                return false;
            }

            actualReorder[visualIndex] = logicalIndices[filteredLogicalIndex];
        }

        for (var i = 0; i < filteredLength; i++)
        {
            var expectedLogical = expectedReorder[i];
            var actualLogical = actualReorder[i];

            if (actualLogical != expectedLogical)
            {
                errorMessage =
                    $"Reorder mismatch at visual index {i}: expected logical index {expectedLogical}, actual {actualLogical}.";
                return false;
            }
        }

        return true;
    }

    private static void AddFailure(
        List<BidiConformanceFailure> failures,
        int lineNumber,
        string rawInput,
        string message,
        int maxFailuresToLog)
    {
        if (failures.Count >= maxFailuresToLog)
            return;

        failures.Add(new BidiConformanceFailure
        {
            lineNumber = lineNumber,
            rawInput = rawInput,
            message = message
        });
    }

    private static string BuildSampleFailuresText(
        List<BidiConformanceFailure> failures,
        int maxFailuresToLog)
    {
        if (failures == null || failures.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();

        var count = Math.Min(failures.Count, maxFailuresToLog);

        for (var i = 0; i < count; i++)
        {
            var failure = failures[i];

            sb.Append("- Line ")
                .Append(failure.lineNumber)
                .Append(": ")
                .Append(failure.message)
                .AppendLine();

            sb.Append("  Input: ")
                .Append(failure.rawInput)
                .AppendLine();
        }

        return sb.ToString();
    }
}

internal sealed class BidiConformanceFailure
{
    public int lineNumber;
    public string rawInput;
    public string message;
}

public struct BidiConformanceSummary
{
    public int totalEvaluatedTests;
    public int passedTests;
    public int failedTests;
    public int skippedTests;
    public string sampleFailures;
}