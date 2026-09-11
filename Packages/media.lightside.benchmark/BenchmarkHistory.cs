#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace LightSide.Benchmark
{
    /// <summary>
    /// Persists benchmark streams for the local history viewer and keeps its generated run index synchronized
    /// with <c>Benchmarks/runs/</c> while the Unity editor is open.
    /// </summary>
    [InitializeOnLoad]
    public static class BenchmarkHistory
    {
        const long rebuildDelayTicks = TimeSpan.TicksPerMillisecond * 250;

        static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        static string BenchmarksDir => Path.Combine(ProjectRoot, "Benchmarks");
        static string RunsDir => Path.Combine(BenchmarksDir, "runs");
        static string IndexPath => Path.Combine(BenchmarksDir, "runs-index.js");

        static readonly FileSystemWatcher watcher;
        static long indexChangeTicks;

        static BenchmarkHistory()
        {
            Directory.CreateDirectory(RunsDir);
            RebuildIndex();

            watcher = new FileSystemWatcher(RunsDir, "run-*.js")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
            };
            watcher.Created += QueueIndexRebuild;
            watcher.Changed += QueueIndexRebuild;
            watcher.Deleted += QueueIndexRebuild;
            watcher.Renamed += QueueIndexRebuild;
            watcher.EnableRaisingEvents = true;

            EditorApplication.update += RebuildChangedIndex;
            LightSide.EditorLifecycle.ManagedCleaning += DisposeWatcher;
        }

        /// <summary>Writes a combined benchmark result into the per-suite files consumed by the history viewer.</summary>
        public static void SaveRun(string json)
        {
            var stamp = BenchmarkStreams.Stamp(Application.platform.ToString(), SystemInfo.deviceName);
            WriteStreams(json, stamp);
            RebuildIndex();
        }

        [MenuItem(LightSideCore.Menu.ToolsRoot + "Benchmarks/Import Run JSON...")]
        static void ImportRunJson()
        {
            var path = EditorUtility.OpenFilePanel("Import benchmark run", "", "json");
            if (string.IsNullOrEmpty(path)) return;

            var stamp = $"imported-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{BenchmarkStreams.Sanitize(Path.GetFileNameWithoutExtension(path))}";
            WriteStreams(File.ReadAllText(path), stamp);
            RebuildIndex();
        }

        static void WriteStreams(string json, string stamp)
        {
            var suites = BenchmarkRunner.DiscoverSuites();
            if (suites.Count == 0)
            {
                Debug.LogWarning("[BenchmarkHistory] No benchmark suite is loaded, so the run cannot be split into viewer streams. Open the benchmark scene first.");
                return;
            }

            Directory.CreateDirectory(RunsDir);
            foreach (var f in BenchmarkStreams.Split(json, stamp, suites))
            {
                File.WriteAllText(Path.Combine(RunsDir, f.fileName), f.contents);
                Debug.Log($"[BenchmarkHistory] {f.suite} run saved to Benchmarks/runs/{f.fileName}");
            }
        }

        /// <summary>Regenerates the viewer index from every top-level <c>run-*.js</c> file in the runs directory.</summary>
        [MenuItem(LightSideCore.Menu.ToolsRoot + "Benchmarks/Rebuild History Index")]
        public static void RebuildIndex()
        {
            Directory.CreateDirectory(RunsDir);

            var files = Directory.GetFiles(RunsDir, "run-*.js", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            var sb = new StringBuilder();
            sb.AppendLine("window.__unitextBenchIndex = [");
            foreach (var f in files)
                sb.Append("  ").Append(JsonConvert.SerializeObject($"runs/{Path.GetFileName(f)}")).AppendLine(",");
            sb.AppendLine("];");

            var contents = sb.ToString();
            if (!File.Exists(IndexPath) || File.ReadAllText(IndexPath) != contents)
                File.WriteAllText(IndexPath, contents, new UTF8Encoding(false));
        }

        [MenuItem(LightSideCore.Menu.ToolsRoot + "Benchmarks/Open History Page")]
        static void OpenHistoryPage()
        {
            RebuildIndex();
            var page = Path.Combine(BenchmarksDir, "index.html");
            if (File.Exists(page)) Application.OpenURL("file:///" + page.Replace('\\', '/'));
            else Debug.LogWarning($"[BenchmarkHistory] Viewer not found at {page}");
        }

        static void QueueIndexRebuild(object sender, FileSystemEventArgs args) =>
            Interlocked.Exchange(ref indexChangeTicks, DateTime.UtcNow.Ticks);

        static void RebuildChangedIndex()
        {
            var changedAt = Interlocked.Read(ref indexChangeTicks);
            if (changedAt == 0 || DateTime.UtcNow.Ticks - changedAt < rebuildDelayTicks) return;
            if (Interlocked.CompareExchange(ref indexChangeTicks, 0, changedAt) != changedAt) return;

            try
            {
                RebuildIndex();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BenchmarkHistory] Failed to rebuild run index: {e.Message}");
            }
        }

        static void DisposeWatcher() => watcher.Dispose();
    }
}
#endif
