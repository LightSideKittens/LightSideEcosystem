using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using LightSide;
using LightSide.Benchmark;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// First use of a font: UniText, TextMesh Pro and UI Toolkit rasterizing a ladder of glyph counts inside one font. A timing
/// case is one font of <see cref="GlyphBenchmarkFonts"/> in one <see cref="GlyphTier"/> (<c>NotoSans-Regular.plain</c>), every
/// engine at every rung its one-shot arm (<c>tmp.512</c>); a memory participant is one engine at a case's whole corpus
/// (<c>NotoSans-Regular.plain.tmp.all</c>), and <c>reference</c> is the scene with nothing shown.
/// </summary>
public sealed class GlyphRasterizationSuite : MonoBehaviour, IBenchmarkSuite, IIsolatedLaunchSuite
{
    const string TopRung = "all";
    static readonly GlyphTier[] Tiers = { GlyphTier.Plain, GlyphTier.Styled };

    readonly Dictionary<string, JObject> records = new();
    List<string> timingCases, memoryParticipants;

    public string SuiteId => "glyph";
    public string Section => "glyphRasterization";
    public string StreamGlobal => "__unitextGlyphRuns";
    public int Scenario => 3;

    /// <summary>Every font of the scene's <see cref="GlyphBenchmarkFonts"/> in each tier.</summary>
    public IReadOnlyList<string> TimingCases => timingCases ??= ListTimingCases();

    /// <summary><c>reference</c>, then every engine at the whole corpus of every case.</summary>
    public IReadOnlyList<string> MemoryParticipants => memoryParticipants ??= ListMemoryParticipants();

    public IReadOnlyList<string> MemoryParticipantsOf(string caseId)
    {
        var ids = new List<string>();
        foreach (var id in TimingCases)
        {
            if (id != caseId) continue;
            foreach (var engine in BenchmarkEngines.Glyph)
                ids.Add(Participant(caseId, engine));
        }
        return ids;
    }

    static string CaseId(GlyphBenchmarkFonts.GlyphFont font, GlyphTier tier) =>
        $"{font.Name}.{(tier == GlyphTier.Plain ? "plain" : "styled")}";

    static string Participant(string caseId, string engineId) => $"{caseId}.{engineId}.{TopRung}";

    static List<string> ListTimingCases()
    {
        var fonts = ObjectUtils.FindAny<GlyphBenchmarkFonts>()
                    ?? throw new InvalidOperationException("The glyph suite needs a GlyphBenchmarkFonts in its scene.");
        var ids = new List<string>();
        foreach (var font in fonts.Fonts)
            foreach (var tier in Tiers)
                ids.Add(CaseId(font, tier));
        return ids;
    }

    List<string> ListMemoryParticipants()
    {
        var ids = new List<string> { BenchmarkRounds.ReferenceArmId };
        foreach (var caseId in TimingCases)
            foreach (var engine in BenchmarkEngines.Glyph)
                ids.Add(Participant(caseId, engine));
        return ids;
    }

    public IEnumerable<KeyValuePair<string, string>> PhaseNotes => new[]
    {
        new KeyValuePair<string, string>("glyphRasterization",
            "A case is one font in one tier; its arms are every engine at every rung of the font's ladder: the first 32, 128, 512 and 2,048 distinct glyphs of the font's corpus that the font draws, below their count, then all of them (rung all). A round empties every engine's glyph tables under its hidden text, keeps the atlas storage already grown, shows the text and counts the frames until the engine drew it: UniText once every text committed its layout after the GPU upload of its glyphs, TMP once every text regenerated its mesh, UI Toolkit once the panel repainted the label and its font gained glyphs. The record holds the synchronous trigger, the frames to pixels and FrameTimingManager's CPU and GPU of those frames. Round 0 is each arm's first rasterization in the process: a warm-up. Every round of an arm must add the same glyphs."),
        new KeyValuePair<string, string>("glyphRasterization.tiers",
            "Rival font assets are made per case from the source font of the pair's TMP asset, at that asset's sampling size and atlas size, SDFAA, multi-atlas, font features on (TMP only where TMP Settings enables them at runtime; the case workload records it per arm). plain: that asset's padding, no effects. styled: padding 0.33 em, which holds the house look (outline 0.12 em, shadow offset (0.05, -0.14) em, spread and blur 0.05 em), every engine drawing the look: UniText its whole-text stroke and shadow, TMP an outline-and-underlay material, UI Toolkit text outline and shadow. UniText's fonts keep their own tile settings."),
        new KeyValuePair<string, string>("glyphRasterization.threads",
            "UniText rasterizes on its own worker threads (unitextParallel) or on the main thread (unitextSingleThreaded); UI Toolkit on Unity's job workers (uiToolkit) or, with Unity's job worker count set to 0 from the arm's preparation to its reset, on the main thread (uiToolkitSingleThreaded), so every Unity job of those frames runs there too; TMP on the main thread. armInfo.threads pairs them; jobWorkers records the UI Toolkit arms' worker count."),
        new KeyValuePair<string, string>("corpus",
            "Shared is the character list set on BenchmarkConfig (Noto Sans, Rubik Storm, Tilt Neon, Cinzel); ArabicForms lists the Arabic base letters and presentation forms apart, so TMP, which does not shape, draws the glyphs the shaping engines draw (Noto Sans Arabic); HanSample is every 14th CJK unified ideograph (Noto Serif SC, CFF outlines); DevanagariLetters lists the Devanagari block apart (Noto Sans Devanagari), where a shaping engine adds one dotted circle under lone signs. Whitespace, control and format characters and code points the font has no glyph for are left out of every ladder."),
        new KeyValuePair<string, string>("memory",
            "A memory launch rasterizes one engine's whole corpus once: setupPeak spans the rasterization and 30 frames after it, metrics the frames that hold the drawn text, with processPeak and systemFootprint. reference is the same without any text."),
        new KeyValuePair<string, string>("fontIsolation",
            "UI Toolkit uses explicit Panel Text Settings with local, global, default, sprite, emoji and Dynamic OS fallbacks disabled and validated; TMP disables local, global, default, sprite and emoji fallbacks; UniText disables system-font and emoji fallback.")
    };

    public IEnumerator Run(BenchmarkContext context)
    {
        var config = BenchmarkConfig.Instance;
        var fonts = ObjectUtils.FindAny<GlyphBenchmarkFonts>();
        if (config == null || fonts == null || fonts.Fonts.Count == 0)
        {
            context.Error("The glyph suite needs a BenchmarkConfig and a GlyphBenchmarkFonts with fonts in its scene.");
            yield break;
        }
        var benches = FindBenches(context);
        if (benches.Count == 0) yield break;
        PublishConfig(context, config);
        RequireSterileScene(context, benches);

        if (context.MeasuresMemory)
        {
            foreach (var participant in MemoryParticipants)
            {
                if (!context.Measures(participant)) continue;
                var record = records[participant] = new JObject { ["status"] = "measuring" };
                yield return context.Cooldown();
                yield return context.Run($"glyph memory {participant}",
                    () => BenchmarkRecord.Guard(MeasureMemory(context, participant, fonts, benches, config, record), record), null);
                if (!context.Alive) yield break;
            }
            yield break;
        }

        foreach (var font in fonts.Fonts)
        foreach (var tier in Tiers)
        {
            var caseId = CaseId(font, tier);
            if (!context.Selects(caseId)) continue;
            var record = records[caseId] = new JObject { ["status"] = "measuring" };
            yield return context.Cooldown();
            yield return context.ThermalSettle();
            yield return context.Run($"glyph {caseId}",
                () => BenchmarkRecord.Guard(RunCase(caseId, font, tier, benches, config, context.Repeat, record), record), null);
            if (!context.Alive) yield break;
        }
    }

    IEnumerator RunCase(string caseId, GlyphBenchmarkFonts.GlyphFont font, GlyphTier tier,
        List<GlyphRasterBenchmarkBase> benches, BenchmarkConfig config, int repeat, JObject record)
    {
        var rungs = Rungs(font, config);
        var opened = new List<GlyphRasterBenchmarkBase>();
        var arms = new List<GlyphRasterArm>();
        var unsupported = new JObject();
        try
        {
            foreach (var bench in benches)
            {
                if (bench.UnsupportedReason != null)
                {
                    foreach (var engineId in bench.EngineIds)
                        unsupported[engineId] = bench.UnsupportedReason;
                    continue;
                }
                bench.BeginCase(font, tier);
                opened.Add(bench);
                foreach (var engineId in bench.EngineIds)
                    foreach (var rung in rungs)
                        arms.Add(new GlyphRasterArm(bench, engineId, rung, tier == GlyphTier.Styled));
            }
            var band = BenchmarkConfig.GlyphBand(font.corpus);
            record["font"] = new JObject
            {
                ["name"] = font.Name,
                ["file"] = font.tmpFont != null && font.tmpFont.sourceFontFile != null ? font.tmpFont.sourceFontFile.name : null,
                ["corpus"] = font.corpus.ToString(),
                ["script"] = band.script,
                ["codePoints"] = rungs[rungs.Count - 1].CodePoints,
                ["band"] = new JObject { ["min"] = band.min, ["max"] = band.max, ["source"] = "estimate" }
            };
            if (unsupported.Count > 0) record["unsupported"] = unsupported;
            yield return BenchmarkRounds.RunOneShots(caseId, arms,
                config.GlyphRoundSettings(BenchmarkRounds.Seed(caseId, repeat)), record);
            var workload = new JObject();
            foreach (var arm in arms)
                workload[arm.Id] = Workload(arm, font);
            record["workload"] = workload;
            yield return CaptureWholeCorpus(caseId, arms, config.maxCommitFrames);
            record["status"] = "measured";
        }
        finally
        {
            for (var i = opened.Count - 1; i >= 0; i--)
                opened[i].EndCase();
        }
    }

    /// <summary>Each engine's whole corpus once more after the rounds: a still of the drawn text and, where its component asks, a Prof capture.</summary>
    static IEnumerator CaptureWholeCorpus(string caseId, List<GlyphRasterArm> arms, int maxFrames)
    {
        foreach (var arm in arms)
        {
            if (arm.Rung.Name != TopRung) continue;
            var bench = arm.Bench;
            yield return arm.Prepare();
            if (bench.captureProfile)
            {
                Prof.SampleAlloc = bench.captureAlloc;
                if (bench.captureAlloc) ProfExactAlloc.Begin();
                Prof.BeginCapture();
            }
            try
            {
                arm.Operate();
                yield return UntilDrawn(arm, maxFrames);
            }
            finally
            {
                if (bench.captureProfile)
                {
                    var capture = Prof.Capturing ? Prof.EndCapture() : null;
                    ProfExactAlloc.End();
                    if (capture != null)
                        ProfTransport.Ship(capture.ToJson(), $"prof/{BenchmarkRun.Id}/profile_glyph_{caseId}_{arm.Id}.json");
                }
            }
            yield return BenchmarkScreenshot.Capture($"glyph-{caseId}-{arm.Id}");
            yield return arm.Reset();
        }
    }

    IEnumerator MeasureMemory(BenchmarkContext context, string participant, GlyphBenchmarkFonts fonts,
        List<GlyphRasterBenchmarkBase> benches, BenchmarkConfig config, JObject record)
    {
        yield return context.ThermalSettle();
        if (participant == BenchmarkRounds.ReferenceArmId)
        {
            yield return BenchmarkMemoryRecord.Reference(record, config.memoryFrames);
            yield break;
        }
        foreach (var font in fonts.Fonts)
        foreach (var tier in Tiers)
        foreach (var engineId in BenchmarkEngines.Glyph)
        {
            if (!string.Equals(Participant(CaseId(font, tier), engineId), participant, StringComparison.OrdinalIgnoreCase)) continue;
            var bench = Owner(benches, engineId)
                        ?? throw new InvalidOperationException($"No glyph benchmark in the scene measures '{engineId}'.");
            var rungs = Rungs(font, config);
            var arm = new GlyphRasterArm(bench, engineId, rungs[rungs.Count - 1], tier == GlyphTier.Styled);
            record["memoryCase"] = CaseId(font, tier);
            record["armInfo"] = arm.ArmInfo();
            if (bench.UnsupportedReason != null)
            {
                record["status"] = "unsupported";
                record["reason"] = bench.UnsupportedReason;
                yield break;
            }
            bench.BeginCase(font, tier);
            try
            {
                yield return arm.Prepare();
                yield return BenchmarkMemoryRecord.Measure(record, Rasterize(arm, config.maxCommitFrames), config.memoryFrames);
                yield return arm.Reset();
                record["workload"] = new JObject { [arm.Id] = Workload(arm, font) };
            }
            finally
            {
                bench.EndCase();
            }
            yield break;
        }
        throw new InvalidOperationException($"No glyph arm answers to memory participant '{participant}'.");
    }

    /// <summary>The load an arm drew: the glyphs its engine added, the rung's code points, the font, its script and the rival font asset's settings.</summary>
    static JObject Workload(GlyphRasterArm arm, GlyphBenchmarkFonts.GlyphFont font)
    {
        var entry = new JObject
        {
            ["load"] = new JObject { ["n"] = arm.Glyphs, ["unit"] = "glyphs", ["changing"] = false },
            ["characters"] = arm.Rung.CodePoints,
            ["font"] = font.Name,
            ["script"] = BenchmarkConfig.GlyphBand(font.corpus).script
        };
        if (arm.Bench.Rival != null) entry["rival"] = arm.Bench.Rival.DeepClone();
        return entry;
    }

    static IEnumerator Rasterize(IBenchmarkOneShotArm arm, int maxFrames)
    {
        arm.Operate();
        yield return UntilDrawn(arm, maxFrames);
    }

    static IEnumerator UntilDrawn(IBenchmarkOneShotArm arm, int maxFrames)
    {
        for (var frames = 1; ; frames++)
        {
            yield return null;
            if (arm.Committed) yield break;
            if (frames >= maxFrames)
                throw new InvalidOperationException($"Arm '{arm.Id}' did not draw within {maxFrames} frames.");
        }
    }

    /// <summary>
    /// The ladder of <paramref name="font"/>: for each count of <see cref="BenchmarkConfig.GlyphLadder"/> below the number of
    /// distinct code points of its corpus that the font draws, the first that many, then all of them; whitespace, control and
    /// format characters are left out.
    /// </summary>
    static List<GlyphRung> Rungs(GlyphBenchmarkFonts.GlyphFont font, BenchmarkConfig config)
    {
        if (font.uniTextFont == null)
            throw new InvalidOperationException($"Font '{font.Name}' has no UniText font to read its glyph coverage from.");
        var corpus = config.GlyphCorpusText(font.corpus);
        var units = new List<string>();
        var seen = new HashSet<int>();
        for (var i = 0; i < corpus.Length; i += char.IsSurrogatePair(corpus, i) ? 2 : 1)
        {
            if (char.IsWhiteSpace(corpus, i) || char.IsControl(corpus, i)
                || char.GetUnicodeCategory(corpus, i) == UnicodeCategory.Format) continue;
            var codePoint = char.ConvertToUtf32(corpus, i);
            if (seen.Add(codePoint) && font.uniTextFont.GetGlyphIndexForUnicode((uint)codePoint) != 0)
                units.Add(char.ConvertFromUtf32(codePoint));
        }
        if (units.Count == 0)
            throw new InvalidOperationException($"Font '{font.Name}' draws none of its {font.corpus} corpus.");
        var separator = BenchmarkConfig.SeparatesGlyphs(font.corpus) ? " " : "";
        var rungs = new List<GlyphRung>();
        foreach (var count in BenchmarkConfig.GlyphLadder)
            if (count < units.Count)
                rungs.Add(new GlyphRung(count.ToString(CultureInfo.InvariantCulture), string.Join(separator, units.GetRange(0, count)), count));
        rungs.Add(new GlyphRung(TopRung, string.Join(separator, units), units.Count));
        return rungs;
    }

    static GlyphRasterBenchmarkBase Owner(List<GlyphRasterBenchmarkBase> benches, string engineId)
    {
        foreach (var bench in benches)
            foreach (var id in bench.EngineIds)
                if (id == engineId)
                    return bench;
        return null;
    }

    static List<GlyphRasterBenchmarkBase> FindBenches(BenchmarkContext context)
    {
        var benches = new List<GlyphRasterBenchmarkBase>();
        var uniText = ObjectUtils.FindAny<UniText_GlyphRasterizationBenchmark>();
        if (uniText != null) benches.Add(uniText);
        else context.Error("UniText_GlyphRasterizationBenchmark not found in the scene.");
        var tmp = ObjectUtils.FindAny<TMP_GlyphRasterizationBenchmark>();
        if (tmp != null) benches.Add(tmp);
        else context.Error("TMP_GlyphRasterizationBenchmark not found in the scene.");
#if !LIGHTSIDE_NO_UITOOLKIT
        var uiToolkit = ObjectUtils.FindAny<UIToolkit_GlyphRasterizationBenchmark>();
        if (uiToolkit != null) benches.Add(uiToolkit);
        else context.Error("UIToolkit_GlyphRasterizationBenchmark not found in the scene.");
#endif
        return benches;
    }

    /// <summary>Records an error when an enabled UniText text outside the benchmark draws from the shared glyph atlas, whose entries the UniText arms count.</summary>
    static void RequireSterileScene(BenchmarkContext context, List<GlyphRasterBenchmarkBase> benches)
    {
        var live = 0;
        foreach (var text in ObjectUtils.FindAll<UniTextBase>())
        {
            if (!text.isActiveAndEnabled) continue;
            var owned = false;
            foreach (var bench in benches)
                owned |= text.transform.IsChildOf(bench.transform);
            if (!owned) live++;
        }
        if (live > 0)
            context.Error($"{live} enabled UniText text(s) outside the glyph benchmark share its atlas; UniText glyph counts would be skewed.");
    }

    static void PublishConfig(BenchmarkContext context, BenchmarkConfig config) =>
        context.Config["glyph"] = new JObject
        {
            ["rounds"] = config.glyphRounds,
            ["maxCommitFrames"] = config.maxCommitFrames,
            ["memoryFrames"] = config.memoryFrames,
            ["ladder"] = new JArray(BenchmarkConfig.GlyphLadder),
            ["styledPaddingEm"] = (float)LookCalibrationProfile.UIToolkitAtlasPadding / LookCalibrationProfile.UIToolkitSamplingPointSize
        };

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
        reason = records.Count == 0 ? "No glyph case or memory participant was measured." : null;
        foreach (var pair in records)
        {
            var status = (string)pair.Value["status"];
            if (status is "measured" or "unsupported") continue;
            reason = $"Glyph record '{pair.Key}' ended with status '{status}'.";
            break;
        }
        return reason == null;
    }
}
