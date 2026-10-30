using System.Collections;
using System.Collections.Generic;
using LightSide;
using LightSide.Benchmark;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// Text-pipeline comparison across UniText, TextMeshPro and UI Toolkit over the same corpora, object
/// counts and iteration budget. Every text carries an outline and a shadow, the configuration shipped
/// projects use most.
/// </summary>
public sealed class TextBenchmarkSuite : MonoBehaviour, IBenchmarkSuite
{
    /// <summary>Second pass over every engine with the plain-Latin corpus — the apples-to-apples case (result keys get a ".latin" suffix). Creation/destruction is skipped there: it does not depend on text content.</summary>
    public bool runLatinCorpus = true;

    readonly Dictionary<string, TextBenchmarkBase.TestResults> results = new();

    int objectCount;
    int iterations;
    int warmupIterations;
    int memoryProbeRepeats;

    public string SuiteId => "text";

    public string Section => "textBenchmarks";

    public string StreamGlobal => "__unitextTextRuns";

    public int Scenario => 2;

    public IEnumerable<KeyValuePair<string, string>> PhaseNotes => new[]
    {
        new KeyValuePair<string, string>("effects",
            "Every text draws an outline and a shadow. UniText: whole-text StrokeModifier and ShadowModifier at their defaults (0.12 em outside; offset 0.1/-0.1 em, blur 0.1 em, spread 0.1 em, black 50 %). TMP: the material preset NotoSans-Regular SDF Outline Shadow (outline 0.25, underlay offset 0.2/-0.2, dilate 0.2, softness 0.2). UI Toolkit: outline 3.36 px and text-shadow 2.8/2.8 px with blur 2.8 px, black 50 %, on font assets with padding 22 at 90 pt, the smallest that holds the shadow unclamped."),
        new KeyValuePair<string, string>("fullRebuild",
            "Incremental edit of one document — warm caches where an engine has them (UniText reuses unchanged-paragraph shaping)."),
        new KeyValuePair<string, string>("fullRebuildUnique",
            "Every paragraph unique per iteration — the cold path for all engines."),
        new KeyValuePair<string, string>("fullRebuildRichText",
            "Unique variants of the corpus saturated with markup (91 tag pairs: <b> <i> <u> <s> <color=#hex> <size=%> <sub> <sup> <uppercase> <lowercase>) parsed by all three engines with byte-identical syntax (UniText via registered tag styles, TMP/UIToolkit richText). The previous phase measures the same content tag-free, so the delta is the markup cost."),
        new KeyValuePair<string, string>("meshRebuild",
            "Engine-incremental color change: UniText re-emits mesh only, TMP re-runs full layout, UIToolkit repaints tint — different work classes by design."),
        new KeyValuePair<string, string>("corpus.multilingual",
            "Arabic/bidi/emoji showcase. UniText and UIToolkit both shape it (this project enables UITK's Advanced Text Generator: HarfBuzz+ICU, see UIToolkitProjectSettings) — like-for-like on shaping. TMP has no shaper — its output is NOT equivalent."),
        new KeyValuePair<string, string>("corpus.latin",
            "Plain Latin text — every engine performs comparable work; the apples-to-apples case."),
        new KeyValuePair<string, string>("memory",
            "Resident is the OS-reported app footprint. Retained is positive post-GC growth between equal live states across warmup and measured work; phase setup net change is reported separately. GC reclaimed is managed garbage at the state-normalized checkpoint. Repeat growth, measured in development players only, is a leak candidate from an identical state-normalized untimed cycle, not proof of a leak. Deep profiler capture runs separately after these checkpoints."),
        new KeyValuePair<string, string>("memory.process",
            "Settled checkpoints (after GC) also carry the process's own memory: dirty = Private_Dirty + SwapPss of /proc/self/smaps_rollup on Android, PrivateUsage on Windows, phys_footprint on iOS; clean = Private_Clean and pss on Android only. Dirty decides; clean is file-backed memory the system can drop. GPU driver memory is in none of them. afterRetention is read 25 s after cleanup, past the engines' pooled-buffer retention, after GC and UnloadUnusedAssets; keptAfterRetention is its delta to the baseline. " +
            (BenchmarkProcessMemory.Available ? "This player reads them." : "This player reads none: " + BenchmarkProcessMemory.UnavailableReason))
    };

    public IEnumerator Run(BenchmarkContext context)
    {
        var cfg = BenchmarkConfig.Instance;
        var uniTextBench = ObjectUtils.FindAny<UniTextBenchmark>();
        if (uniTextBench != null)
        {
            objectCount = cfg != null ? cfg.objectCount : uniTextBench.objectCount;
            iterations = cfg != null ? cfg.iterations : uniTextBench.iterations;
            warmupIterations = cfg != null ? cfg.warmupIterations : uniTextBench.warmupIterations;
            memoryProbeRepeats = cfg != null ? cfg.memoryProbeRepeats : uniTextBench.memoryProbeRepeats;
            PublishConfig(context);

            Debug.Log("[TextBenchmarkSuite] Running UniText (Single-Threaded)...");
            yield return context.ThermalSettle();
            yield return context.Run("unitextSingleThreaded",
                () => uniTextBench.RunBenchmarkCoroutine(silent: true, parallel: false),
                () => results["unitextSingleThreaded"] = uniTextBench.Results);

            if (!context.Alive) yield break;
            yield return context.Cooldown();

            Debug.Log("[TextBenchmarkSuite] Running UniText (Parallel)...");
            yield return context.ThermalSettle();
            yield return context.Run("unitextParallel",
                () => uniTextBench.RunBenchmarkCoroutine(silent: true, parallel: true),
                () => results["unitextParallel"] = uniTextBench.Results);

            if (!context.Alive) yield break;
            yield return context.Cooldown();
        }
        else
        {
            context.Error("UniTextBenchmark not found on scene");
            Debug.LogWarning("[TextBenchmarkSuite] UniTextBenchmark not found");
        }

        var tmpBench = ObjectUtils.FindAny<TMPBenchmark>();
        if (tmpBench != null)
        {
            ApplyConfig(tmpBench);
            Debug.Log("[TextBenchmarkSuite] Running TMP...");
            yield return context.ThermalSettle();
            yield return context.Run("tmp",
                () => tmpBench.RunBenchmarkCoroutine(silent: true),
                () => results["tmp"] = tmpBench.Results);

            if (!context.Alive) yield break;
            yield return context.Cooldown();
        }
        else
        {
            context.Error("TMPBenchmark not found on scene");
            Debug.LogWarning("[TextBenchmarkSuite] TMPBenchmark not found");
        }

#if LIGHTSIDE_NO_UITOOLKIT
#elif UNITY_WEBGL && !UNITY_EDITOR
        UIToolkitBenchmark uitkBench = null;
        Debug.LogWarning("[TextBenchmarkSuite] UIToolkit skipped on WebGL (dynamic FontAsset rasterization traps the WASM runtime).");
#else
        var uitkBench = ObjectUtils.FindAny<UIToolkitBenchmark>();
        if (uitkBench != null)
        {
            ApplyConfig(uitkBench);

            Debug.Log("[TextBenchmarkSuite] Running UIToolkit...");
            yield return context.ThermalSettle();
            yield return context.Run("uiToolkit",
                () => uitkBench.RunBenchmarkCoroutine(silent: true),
                () => results["uiToolkit"] = uitkBench.Results);

            if (!context.Alive) yield break;
        }
        else
        {
            Debug.LogWarning("[TextBenchmarkSuite] UIToolkitBenchmark not found (optional)");
        }
#endif

        if (runLatinCorpus)
            yield return RunLatinCorpusPass(context, uniTextBench, tmpBench
#if !LIGHTSIDE_NO_UITOOLKIT
                , uitkBench
#endif
                );
    }

    public JObject Serialize() => BenchmarkJsonSerializer.SerializeTextBenchmarks(results);

    public bool Measured(out string reason)
    {
        reason = null;
        return results.Count != 0;
    }

    void PublishConfig(BenchmarkContext context)
    {
        context.Config["objectCount"] = objectCount;
        context.Config["iterations"] = iterations;
        context.Config["warmupIterations"] = warmupIterations;
        context.Config["memoryProbeRepeats"] = TextBenchmarkBase.ProbeCycles(memoryProbeRepeats);
    }

    void ApplyConfig(TextBenchmarkBase bench)
    {
        bench.objectCount = objectCount;
        bench.iterations = iterations;
        bench.warmupIterations = warmupIterations;
        bench.memoryProbeRepeats = memoryProbeRepeats;
    }

    IEnumerator RunLatinCorpusPass(BenchmarkContext context, UniTextBenchmark uniTextBench,
        TMPBenchmark tmpBench
#if !LIGHTSIDE_NO_UITOOLKIT
        , UIToolkitBenchmark uitkBench
#endif
        )
    {
        Debug.Log("[TextBenchmarkSuite] === LATIN CORPUS PASS ===");
        var latin = BenchmarkConfig.Latin;

        if (uniTextBench != null)
        {
            uniTextBench.corpusName = "latin";
            uniTextBench.corpusOverrideText = latin;
            var wasCreation = uniTextBench.runCreationDestructionTest;
            uniTextBench.runCreationDestructionTest = false;

            yield return context.Cooldown();
            yield return context.ThermalSettle();
            yield return context.Run("unitextSingleThreaded.latin",
                () => uniTextBench.RunBenchmarkCoroutine(silent: true, parallel: false),
                () => results["unitextSingleThreaded.latin"] = uniTextBench.Results);
            if (!context.Alive) yield break;

            yield return context.Cooldown();
            yield return context.ThermalSettle();
            yield return context.Run("unitextParallel.latin",
                () => uniTextBench.RunBenchmarkCoroutine(silent: true, parallel: true),
                () => results["unitextParallel.latin"] = uniTextBench.Results);
            if (!context.Alive) yield break;

            uniTextBench.corpusOverrideText = null;
            uniTextBench.corpusName = "multilingual";
            uniTextBench.runCreationDestructionTest = wasCreation;
        }

        if (tmpBench != null)
        {
            tmpBench.corpusName = "latin";
            tmpBench.corpusOverrideText = latin;
            var wasCreation = tmpBench.runCreationDestructionTest;
            tmpBench.runCreationDestructionTest = false;

            yield return context.Cooldown();
            yield return context.ThermalSettle();
            yield return context.Run("tmp.latin",
                () => tmpBench.RunBenchmarkCoroutine(silent: true),
                () => results["tmp.latin"] = tmpBench.Results);
            if (!context.Alive) yield break;

            tmpBench.corpusOverrideText = null;
            tmpBench.corpusName = "multilingual";
            tmpBench.runCreationDestructionTest = wasCreation;
        }

#if !LIGHTSIDE_NO_UITOOLKIT
        if (uitkBench != null)
        {
            uitkBench.corpusName = "latin";
            uitkBench.corpusOverrideText = latin;
            var wasCreation = uitkBench.runCreationDestructionTest;
            uitkBench.runCreationDestructionTest = false;

            yield return context.Cooldown();
            yield return context.ThermalSettle();
            yield return context.Run("uiToolkit.latin",
                () => uitkBench.RunBenchmarkCoroutine(silent: true),
                () => results["uiToolkit.latin"] = uitkBench.Results);
            if (!context.Alive) yield break;

            uitkBench.corpusOverrideText = null;
            uitkBench.corpusName = "multilingual";
            uitkBench.runCreationDestructionTest = wasCreation;
        }
#endif
    }
}
