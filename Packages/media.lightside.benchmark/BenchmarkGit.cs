#if UNITY_EDITOR
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace LightSide.Benchmark
{
    /// <summary>Reads the project's git provenance for a run's metadata and a build's baked stamp.</summary>
    /// <remarks>Every call is bounded to three seconds and kills a stalled git, so provenance can never hang the editor.</remarks>
    public static class BenchmarkGit
    {
        const int TimeoutMilliseconds = 3000;

        /// <summary>Standard output of <c>git <paramref name="args"/></c> in the project root, or null when git fails, prints nothing or is unavailable.</summary>
        public static string Run(string args) =>
            Start(args, out var exitCode, out var output) && exitCode == 0 && output.Length > 0 ? output : null;

        /// <summary>
        /// Working-tree dirtiness via <c>git diff-index --quiet HEAD</c> (exit 1 = tracked changes present),
        /// prefixed with <paramref name="args"/>'s <c>-C</c> option when the check targets a submodule. Used
        /// instead of <c>git status --porcelain</c> because status scans the whole tree (~3 s on a large Unity
        /// repo) while diff-index stops at the first change (~40 ms). Untracked-only changes do not count.
        /// </summary>
        public static bool Dirty(string args) => Start(args, out var exitCode, out _) && exitCode == 1;

        static bool Start(string args, out int exitCode, out string output)
        {
            exitCode = -1;
            output = string.Empty;
            try
            {
                var psi = new ProcessStartInfo("git", args)
                {
                    WorkingDirectory = Directory.GetParent(Application.dataPath)!.FullName,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var process = Process.Start(psi);
                var text = process!.StandardOutput.ReadToEndAsync();
                if (!process.WaitForExit(TimeoutMilliseconds))
                {
                    process.Kill();
                    return false;
                }
                exitCode = process.ExitCode;
                output = text.Result.Trim();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
#endif
