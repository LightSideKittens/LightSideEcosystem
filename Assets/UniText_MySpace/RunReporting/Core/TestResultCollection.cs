
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

[Serializable]
public class TestResult
{
    public string ClassName;
    public string MethodName;
    public bool Passed;
    public string ErrorMessage;
    public string StackTrace;
    /// <summary>Text recorded for the case whatever its outcome; written as the JUnit system-out element.</summary>
    public string Output;
    public DateTime StartTime;
    public DateTime EndTime;

    public double Duration => (EndTime - StartTime).TotalSeconds;
}

[Serializable]
public class TestResultCollection
{
    public List<TestResult> Results = new();

    public int Total => Results.Count;
    public int Passed => Results.Count(r => r.Passed);
    public int Failed => Results.Count(r => !r.Passed);
    public bool AllPassed => Results.All(r => r.Passed);

    public void Add(TestResult result) => Results.Add(result);

    public string ToJUnitXml()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<testsuite name=\"UniText\" tests=\"{Total}\" failures=\"{Failed}\" time=\"{Results.Sum(r => r.Duration):F2}\">");

        foreach (var result in Results)
        {
            sb.Append($"  <testcase classname=\"{EscapeXml(result.ClassName)}\" ");
            sb.Append($"name=\"{EscapeXml(result.MethodName)}\" ");
            sb.AppendLine($"time=\"{result.Duration:F2}\">");

            if (!result.Passed)
            {
                sb.AppendLine($"    <failure message=\"{EscapeXml(result.ErrorMessage)}\">");
                if (!string.IsNullOrEmpty(result.StackTrace))
                {
                    sb.AppendLine($"      {EscapeXml(result.StackTrace)}");
                }
                sb.AppendLine("    </failure>");
            }

            if (!string.IsNullOrEmpty(result.Output))
            {
                sb.AppendLine($"    <system-out>{EscapeXml(result.Output)}</system-out>");
            }

            sb.AppendLine("  </testcase>");
        }

        sb.AppendLine("</testsuite>");
        return sb.ToString();
    }

    private static string EscapeXml(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

    public string ToSummary()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Test Results ===");
        sb.AppendLine($"Total: {Total}, Passed: {Passed}, Failed: {Failed}");
        sb.AppendLine();

        foreach (var result in Results)
        {
            var status = result.Passed ? "✓" : "✗";
            sb.AppendLine($"{status} {result.ClassName}.{result.MethodName} ({result.Duration:F2}s)");
            if (!result.Passed && !string.IsNullOrEmpty(result.ErrorMessage))
            {
                sb.AppendLine($"  Error: {result.ErrorMessage}");
            }
        }

        return sb.ToString();
    }
}
