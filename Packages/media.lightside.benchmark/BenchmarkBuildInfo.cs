using System;

namespace LightSide.Benchmark
{
    /// <summary>
    /// Git provenance baked into a Resources text asset at build time (<see cref="ResourceName"/>) by the editor
    /// build hook, and read back by the runtime on player builds — the device process cannot run git or read the
    /// CI runner's GITHUB_SHA, so this is how a device run reports the real commit.
    /// </summary>
    [Serializable]
    public class BenchmarkBuildInfo
    {
        public const string ResourceName = "BenchmarkBuildInfo";

        public string commit;
        public string branch;
        public bool dirty;
        public string submoduleCommit;
        public string submoduleBranch;
        public bool submoduleDirty;

        /// <summary>
        /// Launch arguments baked in for players that receive none (device game loops), given to the build as
        /// <c>-ciBenchmarkLaunch</c> with commas between them; <see cref="BenchmarkLaunch"/> reads them before the
        /// real command line.
        /// </summary>
        public string[] launchArguments;
    }
}
