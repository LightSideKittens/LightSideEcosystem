#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LightSide.Benchmark
{
    /// <summary>
    /// Writes <c>isolated-launches.json</c> beside every player build: each <see cref="IIsolatedLaunchSuite"/> in the built
    /// scenes with its game-loop scenario, its timing cases and its memory participants in order, and per timing case the
    /// memory participants that are its arms, <c>{suiteId: {scenario, timing, memory, arms: {caseId: [...]}}}</c>, from
    /// which a device run derives the scenario numbers of its launches and puts every launch of a case on one unit.
    /// </summary>
    /// <remarks>The lists are read from the scenes as the build processes them, so they match the player exactly.</remarks>
    public sealed class BenchmarkIsolatedLaunches : IPreprocessBuildWithReport, IProcessSceneWithReport, IPostprocessBuildWithReport
    {
        public const string FileName = "isolated-launches.json";

        const int MaxLaunches = 499;

        static readonly SortedDictionary<string, (int scenario, List<string> timing, List<string> memory,
            List<(string caseId, List<string> arms)> arms)> Launches = new();

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) => Launches.Clear();

        /// <exception cref="BuildFailedException">A suite declares more than 499 timing cases or memory participants, or names an arm of a case that is not one of its memory participants.</exception>
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null) return;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component is not IIsolatedLaunchSuite isolated || component is not IBenchmarkSuite suite) continue;
                var timing = new List<string>(isolated.TimingCases);
                var memory = new List<string>(isolated.MemoryParticipants);
                if (timing.Count > MaxLaunches || memory.Count > MaxLaunches)
                    throw new BuildFailedException($"Suite '{suite.SuiteId}' declares {timing.Count} timing cases and {memory.Count} memory participants; game-loop scenario numbers hold {MaxLaunches} of each.");
                var arms = new List<(string, List<string>)>();
                foreach (var caseId in timing)
                {
                    var caseArms = new List<string>(isolated.MemoryParticipantsOf(caseId));
                    foreach (var arm in caseArms)
                        if (!memory.Contains(arm))
                            throw new BuildFailedException($"Suite '{suite.SuiteId}' lists '{arm}' as an arm of case '{caseId}' but not among its memory participants.");
                    if (caseArms.Count > 0) arms.Add((caseId, caseArms));
                }
                Launches[suite.SuiteId] = (suite.Scenario, timing, memory, arms);
            }
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            var output = report.summary.outputPath;
            var directory = Directory.Exists(output) ? output : Path.GetDirectoryName(output);
            var json = new StringBuilder("{");
            foreach (var pair in Launches)
            {
                if (json.Length > 1) json.Append(',');
                json.Append('\n').Append(Quote(pair.Key)).Append(": {\"scenario\": ").Append(pair.Value.scenario);
                AppendList(json, "timing", pair.Value.timing);
                AppendList(json, "memory", pair.Value.memory);
                json.Append(", \"arms\": {");
                for (var i = 0; i < pair.Value.arms.Count; i++)
                {
                    if (i > 0) json.Append(", ");
                    json.Append(Quote(pair.Value.arms[i].caseId)).Append(": [");
                    var caseArms = pair.Value.arms[i].arms;
                    for (var j = 0; j < caseArms.Count; j++)
                        json.Append(j == 0 ? "" : ", ").Append(Quote(caseArms[j]));
                    json.Append(']');
                }
                json.Append("}}");
            }
            json.Append("\n}\n");
            File.WriteAllText(Path.Combine(directory!, FileName), json.ToString());
            foreach (var pair in Launches)
                Debug.Log($"[BenchmarkIsolatedLaunches] {pair.Key} (scenario {pair.Value.scenario}): {pair.Value.timing.Count} timing cases, {pair.Value.memory.Count} memory participants");
        }

        static void AppendList(StringBuilder json, string key, List<string> values)
        {
            json.Append(", ").Append(Quote(key)).Append(": [");
            for (var i = 0; i < values.Count; i++)
                json.Append(i == 0 ? "" : ", ").Append(Quote(values[i]));
            json.Append(']');
        }

        static string Quote(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
#endif
