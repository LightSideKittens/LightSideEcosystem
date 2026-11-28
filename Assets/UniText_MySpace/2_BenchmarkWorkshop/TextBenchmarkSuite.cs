using System;
using System.Collections;
using System.Collections.Generic;
using LightSide;
using LightSide.Benchmark;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// Large-text throughput comparison of UniText, TextMeshPro and UI Toolkit. A timing case is one phase of one corpus
/// (<c>multilingual.fullRebuild</c>) with every engine as an arm, interleaved in rounds by the benchmark package's
/// scheduler; creation and destruction are one-shot cases. A memory participant is one engine over one corpus
/// (<c>latin.tmp</c>) in a process of its own, and <c>reference</c> is the scene with no text built.
/// </summary>
public sealed class TextBenchmarkSuite : MonoBehaviour, IBenchmarkSuite, IIsolatedLaunchSuite
{
    readonly Dictionary<string, JObject> records = new();

    static readonly TextCorpus[] Corpora = { TextCorpus.Multilingual, TextCorpus.Latin };
    const string Creation = "creation", Destruction = "destruction";

    public string SuiteId => "text";
    public string Section => "textBenchmarks";
    public string StreamGlobal => "__unitextTextRuns";
    public int Scenario => 2;

    /// <summary>Creation and destruction, then every phase, of each corpus.</summary>
    public IReadOnlyList<string> TimingCases => timingCases ??= ListTimingCases();

    /// <summary><c>reference</c>, then every engine over each corpus.</summary>
    public IReadOnlyList<string> MemoryParticipants => memoryParticipants ??= ListMemoryParticipants();

    /// <summary>Every engine over the case's corpus: one memory launch of an engine covers all cases of its corpus.</summary>
    public IReadOnlyList<string> MemoryParticipantsOf(string caseId)
    {
        var ids = new List<string>();
        foreach (var id in TimingCases)
        {
            if (id != caseId) continue;
            var corpus = caseId.Substring(0, caseId.IndexOf('.'));
            foreach (var engine in BenchmarkEngines.Text)
                ids.Add($"{corpus}.{engine}");
        }
        return ids;
    }

    List<string> timingCases, memoryParticipants;

    static List<string> ListTimingCases()
    {
        var ids = new List<string>();
        foreach (var corpus in Corpora)
        {
            var key = TextPhases.Key(corpus);
            ids.Add($"{key}.{Creation}");
            ids.Add($"{key}.{Destruction}");
            foreach (var phase in TextPhases.All)
                if (TextPhases.Applies(phase, corpus))
                    ids.Add($"{key}.{TextPhases.Key(phase)}");
        }
        return ids;
    }

    static List<string> ListMemoryParticipants()
    {
        var ids = new List<string> { BenchmarkRounds.ReferenceArmId };
        foreach (var corpus in Corpora)
            foreach (var engine in BenchmarkEngines.Text)
                ids.Add($"{TextPhases.Key(corpus)}.{engine}");
        return ids;
    }

    public IEnumerable<KeyValuePair<string, string>> PhaseNotes => new[]
    {
        new KeyValuePair<string, string>("effects", UniTextBenchmarkEnvironment.Plain
            ? "No engine draws an outline or a shadow: the launch carries -benchmarkPlain."
            : "Every text draws the house look of LookCalibrationProfile: a 0.12 em outline outside an unfattened face and a shadow offset (0.05, -0.14) em with 0.05 em spread and blur, black 60 %. UniText: whole-text stroke and shadow with the profile's parameters; TMP: a material the profile derives from the outline-and-underlay preset; UI Toolkit: font assets at 90 pt, padding 30, with the profile's synthetic bold, outline and shadow restyled at every font size."),
        new KeyValuePair<string, string>("pages",
            $"Each engine lays out {BenchmarkConfigCount()} texts of the whole corpus at 28 px in {TextPhases.PageWidth}x{TextPhases.PageHeight}-unit pages {TextPhases.PageStride} units apart: the CPU lays out every text, the GPU draws page 0 only. An arm builds its texts when it activates and destroys them when it deactivates, so engines never hold their texts at once."),
        new KeyValuePair<string, string>("frames",
            "Per-frame phases are frame cases: every arm in rounds of settle and measured frames, interleaved with a reference arm that shows nothing; a slot's CPU is FrameTimingManager's main-thread time (the frame window on players without it), GPU and render thread beside it. Every frame of a slot applies an input of the same cost: a new token in one paragraph (fullRebuild), in every paragraph (fullRebuildUnique, fullRebuildRichText), one of two rect sizes 24 units apart (layouts) or one of two colors (meshRebuild). Tokens are digits, so no paragraph's base direction changes, and differ between arms, so no arm meets another's cached content. A slot fails when a text of a changing phase was not reprocessed in every measured frame, or any text was while idle."),
        new KeyValuePair<string, string>("oneShots",
            "creation: every text instantiated from its template with the arm's own document, then the frames until every text is processed; destruction: every processed text destroyed, then the frame that follows. Round 0 is each arm's first operation in the process."),
        new KeyValuePair<string, string>("meshRebuild",
            "Color change: UniText re-emits its mesh, TMP re-runs its layout, UI Toolkit repaints tint — different work by design."),
        new KeyValuePair<string, string>("corpus.multilingual",
            "Arabic, Persian, Urdu, Hebrew, Devanagari, Thai, Latin, Cyrillic and Greek. UniText and UI Toolkit (Advanced Text Generator) shape it; TMP does not, so its output is not equivalent. UI Toolkit builds a text as a column of one label per paragraph with that paragraph's direction, except in auto-size phases, where one label lays every paragraph out left to right."),
        new KeyValuePair<string, string>("corpus.latin",
            "Plain Latin text: every engine performs comparable work. It has no markup, so it has no rich-text phase."),
        new KeyValuePair<string, string>("memory",
            "Memory launches run one engine over one corpus: settled checkpoints and peak windows around repeated creation and around every phase's stepped frames (each phase on freshly built texts), then cleanup and a checkpoint 25 s later. They record no time. setupPeak is the creation window, metrics.processPeak the idle window with every text alive, metrics.systemFootprint the system's read before cleanup. reference samples the scene with no text built.")
    };

    static int BenchmarkConfigCount() => BenchmarkConfig.Instance != null ? BenchmarkConfig.Instance.objectCount : 0;

    public IEnumerator Run(BenchmarkContext context)
    {
        var config = BenchmarkConfig.Instance;
        if (config == null)
        {
            context.Error("The text suite needs a BenchmarkConfig in the scene.");
            yield break;
        }
        var benches = FindBenches(context);
        if (benches.Count == 0) yield break;
        PublishConfig(context, config);

        if (context.MeasuresMemory)
        {
            foreach (var participant in MemoryParticipants)
            {
                if (!context.Measures(participant)) continue;
                var record = records[participant] = new JObject { ["status"] = "measuring" };
                yield return context.Cooldown();
                yield return context.Run($"text memory {participant}",
                    () => BenchmarkRecord.Guard(MeasureMemory(participant, benches, config, record), record), null);
                if (!context.Alive) yield break;
            }
            yield break;
        }

        foreach (var caseId in TimingCases)
        {
            if (!context.Selects(caseId)) continue;
            var record = records[caseId] = new JObject { ["status"] = "measuring" };
            yield return context.Cooldown();
            yield return context.ThermalSettle();
            yield return context.Run($"text {caseId}",
                () => BenchmarkRecord.Guard(RunCase(caseId, benches, config, context.Repeat, record), record), null);
            if (!context.Alive) yield break;
        }
    }

    IEnumerator RunCase(string caseId, List<TextBenchmarkBase> benches, BenchmarkConfig config, int repeat, JObject record)
    {
        var separator = caseId.IndexOf('.');
        var corpus = caseId.Substring(0, separator) == TextPhases.Key(TextCorpus.Latin) ? TextCorpus.Latin : TextCorpus.Multilingual;
        var operation = caseId.Substring(separator + 1);
        var settings = config.TextRoundSettings(BenchmarkRounds.Seed(caseId, repeat));
        foreach (var bench in benches)
            bench.BeginSession(corpus);
        try
        {
            var armCode = 0;
            if (operation == Creation || operation == Destruction)
            {
                var arms = new List<IBenchmarkOneShotArm>();
                foreach (var bench in benches)
                    foreach (var engineId in bench.EngineIds)
                        arms.Add(operation == Creation
                            ? new TextCreationArm(bench, engineId, ++armCode)
                            : new TextDestructionArm(bench, engineId, ++armCode, config.maxCommitFrames));
                yield return BenchmarkRounds.RunOneShots(caseId, arms, settings, record);
                record["workload"] = Workload(benches, corpus, false, config);
                record["status"] = "measured";
                yield break;
            }

            var phase = ParsePhase(operation);
            var phaseArms = new List<TextPhaseArm>();
            foreach (var bench in benches)
                foreach (var engineId in bench.EngineIds)
                    phaseArms.Add(new TextPhaseArm(bench, engineId, phase, ++armCode, config.TextFramesPerArm));
            yield return BenchmarkRounds.RunFrames(caseId, phaseArms, settings, record);
            record["workload"] = Workload(benches, corpus, phase != TextPhase.Idle, config);
            if (phase == TextPhase.Idle)
                yield return CaptureStills(TextPhases.Key(corpus), phaseArms);
            record["status"] = "measured";
        }
        finally
        {
            for (var i = benches.Count - 1; i >= 0; i--)
                benches[i].EndSession();
        }
    }

    /// <summary>Every engine arm's load: the texts it lays out, whether they change every frame, and the corpus they hold.</summary>
    static JObject Workload(List<TextBenchmarkBase> benches, TextCorpus corpus, bool changing, BenchmarkConfig config)
    {
        var workload = new JObject();
        foreach (var bench in benches)
            foreach (var engineId in bench.EngineIds)
                workload[engineId] = new JObject
                {
                    ["load"] = new JObject { ["n"] = config.objectCount, ["unit"] = "texts", ["changing"] = changing },
                    ["corpus"] = TextPhases.Key(corpus),
                    ["characters"] = TextPhases.PlainText(corpus).Length
                };
        return workload;
    }

    /// <summary>One still per arm in the idle state, page 0 on screen, after the case's measurement.</summary>
    static IEnumerator CaptureStills(string corpus, List<TextPhaseArm> arms)
    {
        foreach (var arm in arms)
        {
            arm.Activate();
            yield return null;
            yield return null;
            yield return BenchmarkScreenshot.Capture($"text-{corpus}-{arm.Id}");
            arm.Deactivate();
            yield return null;
        }
    }

    static TextPhase ParsePhase(string key)
    {
        foreach (var phase in TextPhases.All)
            if (TextPhases.Key(phase) == key)
                return phase;
        throw new ArgumentException($"The text suite has no phase '{key}'.", nameof(key));
    }

    /// <summary>The memory launch of <paramref name="participant"/>: the reference floor, or one engine's memory run over one corpus.</summary>
    static IEnumerator MeasureMemory(string participant, List<TextBenchmarkBase> benches, BenchmarkConfig config, JObject record)
    {
        if (participant == BenchmarkRounds.ReferenceArmId)
        {
            yield return BenchmarkMemoryRecord.Reference(record, config.memoryFrames);
            yield break;
        }
        var separator = participant.IndexOf('.');
        var corpus = participant.Substring(0, separator) == TextPhases.Key(TextCorpus.Latin) ? TextCorpus.Latin : TextCorpus.Multilingual;
        var engineId = participant.Substring(separator + 1);
        var bench = Owner(benches, engineId)
                    ?? throw new InvalidOperationException($"No text benchmark in the scene measures '{engineId}'.");
        var results = new TextBenchmarkBase.TestResults();
        yield return bench.RunMemoryCoroutine(engineId, corpus, results);
        BenchmarkJsonSerializer.WriteTextMemory(record, results);
        record["memoryCase"] = $"{TextPhases.Key(corpus)}.{TextPhases.Key(TextPhase.Idle)}";
        var info = new JObject();
        bench.Describe(engineId, info);
        info["control"] = JValue.CreateNull();
        record["armInfo"] = info;
    }

    static TextBenchmarkBase Owner(List<TextBenchmarkBase> benches, string engineId)
    {
        foreach (var bench in benches)
            foreach (var id in bench.EngineIds)
                if (id == engineId)
                    return bench;
        return null;
    }

    static List<TextBenchmarkBase> FindBenches(BenchmarkContext context)
    {
        var benches = new List<TextBenchmarkBase>();
        var uniText = ObjectUtils.FindAny<UniTextBenchmark>();
        if (uniText != null) benches.Add(uniText);
        else context.Error("UniTextBenchmark not found on scene");
        var tmp = ObjectUtils.FindAny<TMPBenchmark>();
        if (tmp != null) benches.Add(tmp);
        else context.Error("TMPBenchmark not found on scene");
#if !LIGHTSIDE_NO_UITOOLKIT && !(UNITY_WEBGL && !UNITY_EDITOR)
        var uiToolkit = ObjectUtils.FindAny<UIToolkitBenchmark>();
        if (uiToolkit != null) benches.Add(uiToolkit);
        else Debug.LogWarning("[TextBenchmarkSuite] UIToolkitBenchmark not found (optional)");
#elif UNITY_WEBGL && !UNITY_EDITOR
        Debug.LogWarning("[TextBenchmarkSuite] UIToolkit skipped on WebGL (dynamic FontAsset rasterization traps the WASM runtime).");
#endif
        return benches;
    }

    public JObject Serialize()
    {
        if (records.Count == 0) return null;
        var section = new JObject();
        foreach (var pair in records)
            section[pair.Key] = pair.Value;
        return section;
    }

    public bool Measured(out string reason)
    {
        reason = records.Count == 0 ? "No text case or memory participant was measured." : null;
        foreach (var pair in records)
        {
            var status = (string)pair.Value["status"];
            if (status == "measured") continue;
            reason = $"Text record '{pair.Key}' ended with status '{status}'.";
            break;
        }
        return reason == null;
    }

    static void PublishConfig(BenchmarkContext context, BenchmarkConfig config)
    {
        context.Config["text"] = new JObject
        {
            ["objectCount"] = config.objectCount,
            ["rounds"] = config.textRounds,
            ["settleFrames"] = config.textSettleFrames,
            ["measuredFrames"] = config.textMeasuredFrames,
            ["maxWarmupFrames"] = config.textMaxWarmupFrames,
            ["maxCommitFrames"] = config.maxCommitFrames,
            ["memoryProbeRepeats"] = TextBenchmarkBase.ProbeCycles(config.memoryProbeRepeats),
            ["page"] = new JObject
            {
                ["width"] = TextPhases.PageWidth, ["height"] = TextPhases.PageHeight,
                ["stride"] = TextPhases.PageStride, ["fontSize"] = TextPhases.FontSize
            }
        };
    }
}
