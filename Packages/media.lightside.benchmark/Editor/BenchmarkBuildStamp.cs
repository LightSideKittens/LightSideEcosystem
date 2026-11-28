#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LightSide.Benchmark
{
    /// <summary>
    /// Writes <see cref="BenchmarkBuildInfo"/> into <c>Assets/Resources/</c> before a player build, so the on-device
    /// benchmark JSON carries the commits that were built even though the device can neither run git nor see the
    /// runner's env.
    /// </summary>
    /// <remarks>
    /// CI passes the built commits as <c>-ciCommit</c>, <c>-ciBranch</c> and <c>-ciSubmoduleCommit</c>: git does not
    /// run inside the build container, and <c>GITHUB_SHA</c> names the dispatching branch rather than the checked-out
    /// ref. Without those arguments the values come from local git.
    /// </remarks>
    public class BenchmarkBuildStamp : IPreprocessBuildWithReport
    {
        /// <summary>
        /// Repository-relative path of the submodule whose revision is stamped beside the project's own.
        /// Empty leaves the submodule fields unknown. Set it from an <c>[InitializeOnLoadMethod]</c>; the
        /// build callback runs in the same domain.
        /// </summary>
        public static string SubmodulePath = "";

        static string ResourcePath => $"Assets/Resources/{BenchmarkBuildInfo.ResourceName}.json";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var info = new BenchmarkBuildInfo
            {
                commit = Argument("-ciCommit") ?? BenchmarkGit.Run("rev-parse HEAD") ?? "unknown",
                branch = Argument("-ciBranch") ?? BenchmarkGit.Run("rev-parse --abbrev-ref HEAD") ?? "unknown",
                dirty = BenchmarkGit.Dirty("diff-index --quiet HEAD"),
                submoduleCommit = Argument("-ciSubmoduleCommit") ?? Submodule("rev-parse HEAD") ?? "unknown",
                submoduleBranch = Submodule("rev-parse --abbrev-ref HEAD") ?? "unknown",
                submoduleDirty = SubmodulePath.Length != 0 &&
                                 BenchmarkGit.Dirty($"-C {SubmodulePath} diff-index --quiet HEAD"),
                launchArguments = BakedLaunchArguments(),
            };

            Directory.CreateDirectory(Path.GetDirectoryName(ResourcePath)!);
            File.WriteAllText(ResourcePath, JsonUtility.ToJson(info, true));
            AssetDatabase.ImportAsset(ResourcePath);
            Debug.Log($"[BenchmarkBuildStamp] Baked build info: {info.commit} ({info.branch})");
        }

        /// <summary>The comma-separated value of this editor's <c>-ciBenchmarkLaunch</c>, split; empty without it.</summary>
        static string[] BakedLaunchArguments() =>
            Argument("-ciBenchmarkLaunch")?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            ?? Array.Empty<string>();

        static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            var at = Array.IndexOf(args, name);
            return at < 0 || at + 1 >= args.Length || string.IsNullOrEmpty(args[at + 1]) ? null : args[at + 1];
        }

        static string Submodule(string args) =>
            SubmodulePath.Length == 0 ? null : BenchmarkGit.Run($"-C {SubmodulePath} {args}");
    }
}
#endif
