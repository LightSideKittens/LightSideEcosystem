using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace LightSide.Benchmark
{
    /// <summary>
    /// Everything a run carries that is not a suite's own measurement: when it ran, which revision it ran
    /// against, and what went wrong. Suite results are never stored here — the runner asks each suite for
    /// its section at serialization time.
    /// </summary>
    public sealed class BenchmarkRunData
    {
        public string timestamp;
        public string utc;
        public string commit = "unknown";
        public string branch = "unknown";
        public bool dirty;
        public string submoduleCommit = "unknown";
        public string submoduleBranch = "unknown";
        public bool submoduleDirty;
        public string source = "unknown";

        /// <summary>
        /// The single participant this process measured, or null when it measured every one. Present so a
        /// merge can tell isolated runs apart from a shared-process run and refuse to mix them.
        /// </summary>
        public string participant;

        /// <summary>Index of this process among the repeats of the same measurement.</summary>
        public int repeat;

        /// <summary>Launch arguments baked into the player at build time (<see cref="BenchmarkBuildInfo.launchArguments"/>).</summary>
        public string[] launchArguments = System.Array.Empty<string>();

        /// <summary>Run parameters a suite wants published beside its results; emitted as the document's <c>config</c>.</summary>
        public readonly JObject config = new();

        public readonly List<string> errors = new();
    }
}
