using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LightSide.Benchmark
{
    /// <summary>
    /// Splits a run's combined result document into per-suite site streams for the benchmark viewer
    /// (<c>run-&lt;suite&gt;-&lt;stamp&gt;.js</c>, each pushing into the suite's own window global). Runtime —
    /// not editor-gated — so a device build writes drop-in site files next to <c>benchmarkResults.json</c>,
    /// carrying the full meta/systemInfo into each stream. A suite with no data is omitted.
    /// </summary>
    public static class BenchmarkStreams
    {
        /// <summary>
        /// Per non-empty suite: its site file name and contents — the combined document trimmed to that suite, wrapped
        /// as a push into the suite's global. Values pass through verbatim; timestamps keep their original text.
        /// </summary>
        public static List<(string suite, string fileName, string contents)> Split(string combinedJson,
            string stamp, IReadOnlyList<IBenchmarkSuite> suites)
        {
            JObject root;
            using (var reader = new JsonTextReader(new StringReader(combinedJson)) { DateParseHandling = DateParseHandling.None })
                root = JObject.Load(reader);
            var files = new List<(string, string, string)>();

            foreach (var suite in suites)
            {
                if (root[suite.Section] is not JObject section || section.Count == 0) continue;

                var clone = (JObject)root.DeepClone();
                foreach (var other in suites)
                    if (other.Section != suite.Section)
                        clone.Remove(other.Section);
                clone["suite"] = suite.SuiteId;

                var contents = $"window.{suite.StreamGlobal} = window.{suite.StreamGlobal} || [];\n" +
                               $"window.{suite.StreamGlobal}.push(\n" + clone.ToString(Formatting.Indented) + "\n);\n";
                files.Add((suite.SuiteId, $"run-{suite.SuiteId}-{stamp}.js", contents));
            }

            return files;
        }

        /// <summary>Timestamp+platform+device stem shared by every suite stream from one run.</summary>
        public static string Stamp(string platform, string deviceName) =>
            $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{platform}-{Sanitize(deviceName)}";

        /// <summary>Reduces <paramref name="s"/> to letters, digits, dash and underscore; empty input becomes <c>unknown</c>.</summary>
        public static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "unknown";
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-');
            return sb.ToString();
        }
    }
}
