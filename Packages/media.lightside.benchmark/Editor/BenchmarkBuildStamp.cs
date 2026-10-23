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
    /// Writes <see cref="BenchmarkBuildInfo"/> into <c>Assets/Resources/</c> before a player build, capturing
    /// the commit from <c>GITHUB_SHA</c> (CI) or git (local) — so the on-device benchmark JSON carries the
    /// real commit even though the device can neither run git nor see the runner's env.
    /// </summary>
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
                commit = Env("GITHUB_SHA") ?? BenchmarkGit.Run("rev-parse HEAD") ?? "unknown",
                branch = Env("GITHUB_REF_NAME") ?? BenchmarkGit.Run("rev-parse --abbrev-ref HEAD") ?? "unknown",
                dirty = BenchmarkGit.Dirty("diff-index --quiet HEAD"),
                submoduleCommit = Submodule("rev-parse HEAD") ?? "unknown",
                submoduleBranch = Submodule("rev-parse --abbrev-ref HEAD") ?? "unknown",
                submoduleDirty = SubmodulePath.Length != 0 &&
                                 BenchmarkGit.Dirty($"-C {SubmodulePath} diff-index --quiet HEAD"),
            };

            Directory.CreateDirectory(Path.GetDirectoryName(ResourcePath)!);
            File.WriteAllText(ResourcePath, JsonUtility.ToJson(info, true));
            AssetDatabase.ImportAsset(ResourcePath);
            Debug.Log($"[BenchmarkBuildStamp] Baked build info: {info.commit} ({info.branch})");
        }

        static string Env(string key)
        {
            var v = Environment.GetEnvironmentVariable(key);
            return string.IsNullOrEmpty(v) ? null : v;
        }

        static string Submodule(string args) =>
            SubmodulePath.Length == 0 ? null : BenchmarkGit.Run($"-C {SubmodulePath} {args}");
    }
}
#endif
