#if UNITEXT_SLIDESHOW
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using LightSide;
using UnityEngine;
using Core = LightSide.UniTextFont.Core;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

/// <summary>
/// Checks the system-font cut behind every script, weight, slant, language and named family of the device, draws the
/// answers as slides and measures what they cost. A platform tells its own cuts only through its answers, so a cut is
/// wrong when the platform gives a closer one of the same family and slant for the same cluster at another weight, and
/// a cluster is lost when one weight resolves it and another does not.
/// </summary>
internal sealed class SystemFontCutSlideshow
{
    private const string className = "SystemFontCuts";
    private const int reportedViolations = 60;
    private const int threadCount = 4;
    private const int settleFrames = 12;
    private const double frameBudgetMs = 100;

    private static readonly int[] Weights = { 1, 100, 200, 300, 350, 400, 500, 600, 650, 700, 800, 900, 950, 1000 };
    private static readonly int[] PageWeights = { 100, 200, 300, 400, 500, 600, 700, 800, 900 };
    private static readonly int[] MeasuredWeights = { 400, 600, 900 };

    private static readonly (string name, string[] clusters)[] Scripts =
    {
        ("Latin", new[] { "A", "g", "é", "ß", "ŋ", "ə" }),
        ("Vietnamese", new[] { "ậ", "ậ", "ữ" }),
        ("Greek", new[] { "Ω", "ά" }),
        ("Cyrillic", new[] { "Ж", "й", "й" }),
        ("Armenian", new[] { "Ա" }),
        ("Georgian", new[] { "გ", "Ⴂ" }),
        ("Hebrew", new[] { "ש", "שָׁ" }),
        ("Arabic", new[] { "ع", "بَّ", "ﷺ" }),
        ("Persian", new[] { "گ", "هٔ" }),
        ("Syriac", new[] { "ܫ" }),
        ("N'Ko", new[] { "ߒ" }),
        ("Devanagari", new[] { "न", "क्ष", "श्री", "हि" }),
        ("Bengali", new[] { "ক", "ক্ষ" }),
        ("Gurmukhi", new[] { "ਪ", "ਖ਼" }),
        ("Gujarati", new[] { "ગ", "ક્ષ" }),
        ("Odia", new[] { "ଓ", "କ୍ଷ" }),
        ("Tamil", new[] { "த", "க்ஷ" }),
        ("Telugu", new[] { "త", "శ్రీ" }),
        ("Kannada", new[] { "ಕ", "ಕ್ಷ" }),
        ("Malayalam", new[] { "മ", "ക്ഷ" }),
        ("Sinhala", new[] { "ස", "ක්‍ෂ" }),
        ("Thai", new[] { "ก", "ก้", "ปี่" }),
        ("Lao", new[] { "ສ", "ກໍ່" }),
        ("Khmer", new[] { "ក", "ខ្ញុំ" }),
        ("Myanmar", new[] { "မ", "မြ" }),
        ("Tibetan", new[] { "བ", "སྐ" }),
        ("Ethiopic", new[] { "አ" }),
        ("Cherokee", new[] { "Ꭰ" }),
        ("Canadian Syllabics", new[] { "ᐃ" }),
        ("Mongolian", new[] { "ᠮ" }),
        ("Hangul", new[] { "가", "한", "한" }),
        ("Hiragana", new[] { "あ", "が", "が" }),
        ("Katakana", new[] { "ア", "ー", "ｱ" }),
        ("Han", new[] { "中", "骨", "直", "龍", "体" }),
        ("Han extension B", new[] { "\U00020000", "\U00020BB7" }),
        ("Han variation", new[] { "葛\U000E0100" }),
        ("CJK punctuation", new[] { "、", "「", "Ａ" }),
        ("Currency", new[] { "$", "€", "₹", "₽", "₾" }),
        ("Mathematics", new[] { "∑", "√", "∞", "𝒜" }),
        ("Arrows", new[] { "→", "⇌" }),
        ("Geometric", new[] { "■", "○", "╔" }),
        ("Music", new[] { "♪", "𝄞" }),
        ("Phonetic", new[] { "ʃ", "t͡ʃ" }),
        ("Combining", new[] { "q̣̇", "x҉" }),
        ("Joiners", new[] { "A‍", "ب‍", "क्‍", "B͏" }),
        ("Runic", new[] { "ᚠ" }),
        ("Hieroglyphs", new[] { "𓂀" }),
        ("Gothic", new[] { "𐌰" }),
    };

    private static readonly (string tag, string[] clusters)[] Languages =
    {
        ("ja", new[] { "骨", "直", "誤", "あ" }),
        ("zh-Hans", new[] { "骨", "直", "语" }),
        ("zh-Hant", new[] { "骨", "直", "語" }),
        ("ko", new[] { "骨", "直", "한" }),
        ("ar", new[] { "ع" }),
        ("ur", new[] { "ع" }),
        ("fa", new[] { "ک" }),
        ("hi", new[] { "न" }),
        ("mr", new[] { "न" }),
        ("th", new[] { "ก" }),
        ("ka", new[] { "გ" }),
        ("he", new[] { "ש" }),
        ("sr", new[] { "б" }),
        ("und", new[] { "骨" }),
    };

    /// <summary>Families outside the package's catalog that systems ship, variable ones first.</summary>
    private static readonly string[] MoreFamilies =
    {
        "Bahnschrift", "Segoe UI Variable Text", "Cantarell", "Inter Variable", "Ubuntu", "Ubuntu Sans", "Adwaita Sans",
        "Avenir Next", "PingFang SC", "Hiragino Sans", "Apple SD Gothic Neo", "Thonburi", "Geeza Pro", "Yu Gothic UI",
        "Microsoft YaHei UI", "Microsoft JhengHei UI", "Malgun Gothic", "Nirmala UI", "Leelawadee UI", "Noto Sans CJK JP",
        "Noto Sans Thai", "Noto Sans Devanagari", "sans-serif-condensed", "sans-serif-medium", "serif", "monospace",
    };

    private static readonly string[] BatchTexts =
    {
        "Hamburg Живая ไทย हिन्दी 日本你好 あカ 한국어 العربية עברית ქართული",
        "đ中đ中đ Ωж中가ไ中न中ع中ש中გ中",
        "ภาษา한국 日本ไทย 你好नमस्ते ქართული中 עברית中 العربية中",
        "Việt Ậ ữ ﷺ ₹₽₾ ∑√∞ →⇌ ♪ ʃ",
    };

    private static readonly int[][] OddSequences =
    {
        new[] { 0x378 }, new[] { 0xFFFF }, new[] { 0xFFFE }, new[] { 0xFDD0 }, new[] { 0xE000 }, new[] { 0xF8FF },
        new[] { 0x10FFFF }, new[] { 0x0 }, new[] { 0x9 }, new[] { 0xA }, new[] { 0xAD }, new[] { 0x200B },
        new[] { 0x200D }, new[] { 0x200D, 0x200D }, new[] { 0x301 }, new[] { 0x301, 0x301, 0x301 }, new[] { 0xFE0F },
        new[] { 0xE0001 }, new[] { 0xFEFF }, new[] { 0x2028 }, new[] { 0x202E, 0x5E2 }, new[] { 0x41, 0x0, 0x42 },
        new[] { 0x1F600, 0xFE0E }, new[] { 0x1F468, 0x200D, 0x1F469 }, new[] { 0x2603, 0x639, 0x4E2D, 0xE01, 0x928 },
    };

    private static readonly (string name, string text)[] PageScripts =
    {
        ("Latin", "Ag"), ("Cyrillic", "Жя"), ("Greek", "Ωβ"), ("Thai", "ไทย"), ("Devanagari", "हिन्"), ("Han", "日骨"),
        ("Kana", "あア"), ("Hangul", "한국"), ("Arabic", "عرب"), ("Hebrew", "עבר"), ("Georgian", "ქარ"), ("Tamil", "தமி"),
    };

    private static readonly (string name, string language, string text)[] PageLanguages =
    {
        ("ja", "ja", "骨直誤"), ("zh-Hans", "zh-Hans", "骨直语"), ("zh-Hant", "zh-Hant", "骨直語"), ("ko", "ko", "骨直한"),
        ("no language", null, "骨直"), ("ur", "ur", "عرب"), ("ar", "ar", "عرب"),
    };

    private readonly struct Answer
    {
        public readonly Core font;
        public readonly string family;
        public readonly string style;
        public readonly int weight;
        public readonly bool slanted;
        /// <summary>Whether <see cref="weight"/> is a CSS weight: the weight axis of an old variable font runs on a scale of its own.</summary>
        public readonly bool cssWeight;
        private readonly int lightest;
        private readonly int heaviest;

        public Answer(Core resolved)
        {
            font = resolved != null ? resolved.SystemFontSource ?? resolved : null;
            family = null;
            style = null;
            weight = 0;
            slanted = false;
            cssWeight = true;
            lightest = heaviest = 0;
            if (font == null) return;
            family = font.FaceInfo.familyName;
            style = font.FaceInfo.styleName;
            font.GetDefaultStyle(out weight, out slanted);
            foreach (var axis in font.VariableAxes ?? Array.Empty<HB.hb_ot_var_axis_info_t>())
            {
                if (axis.tag != FontVariation.axisTags[0]) continue;
                cssWeight = axis.maxValue >= 400f;
                lightest = (int)axis.minValue;
                heaviest = (int)axis.maxValue;
            }
        }

        /// <summary>The distance between a requested weight and the one drawn: a weight axis draws any weight it spans.</summary>
        public int Distance(int requested)
            => Math.Abs((heaviest > 0 ? Math.Clamp(requested, lightest, heaviest) : weight) - requested);

        public bool Found => font != null;

        /// <summary>
        /// Whether the face is told to be what its style says: a bold or slanted face told to be a regular one is
        /// emboldened or slanted once more.
        /// </summary>
        public bool MatchesStyle
            => string.IsNullOrEmpty(style)
               || (!style.StartsWith("Bold", StringComparison.Ordinal) || weight >= 600)
               && (slanted || style.IndexOf("Italic", StringComparison.Ordinal) < 0
                   && style.IndexOf("Oblique", StringComparison.Ordinal) < 0);

        public bool SameCut(in Answer other)
            => Found == other.Found && family == other.family && style == other.style && weight == other.weight
               && slanted == other.slanted;

        public override string ToString()
            => Found ? $"{family}/{style}/{weight}{(slanted ? "i" : "")}" : "none";
    }

    private sealed class Check
    {
        private readonly TestResult result;
        private readonly StringBuilder output = new();
        private readonly List<string> violations = new();
        private int violationCount;

        public Check(string name)
        {
            result = new TestResult { ClassName = className, MethodName = name, StartTime = DateTime.UtcNow };
        }

        public void Report(string line) => output.Append(line).Append('\n');

        public void Violate(string line)
        {
            violationCount++;
            if (violations.Count < reportedViolations) violations.Add(line);
        }

        public void Finish(TestResultCollection results)
        {
            result.EndTime = DateTime.UtcNow;
            result.Passed = violationCount == 0;
            result.Output = output.ToString();
            if (violationCount > 0)
                result.ErrorMessage = $"{violationCount} violations\n{string.Join("\n", violations)}";
            results.Add(result);
            Debug.Log($"[SystemFontCuts] {result.MethodName}: {(result.Passed ? "passed" : violationCount + " violations")} in {result.Duration:F1} s");
        }
    }

    private readonly string name;
    private readonly Canvas canvas;
    private readonly TestResultCollection results;
    private readonly Stopwatch frame = Stopwatch.StartNew();

    private SystemFontCutSlideshow(Canvas canvas, string name, TestResultCollection results)
    {
        this.canvas = canvas;
        this.name = name;
        this.results = results;
    }

    /// <summary>
    /// Runs every check in <paramref name="canvas"/>, then captures the pages. The measurements reset the system-font
    /// caches, so texts already on screen rebuild afterwards.
    /// </summary>
    internal static IEnumerator Run(Canvas canvas, string name, TestResultCollection results)
    {
        var disabled = SystemFont.Disabled;
        SystemFont.Disabled = false;
        try
        {
            var slideshow = new SystemFontCutSlideshow(canvas, name, results);
            foreach (var (phase, steps) in slideshow.Phases())
                while (slideshow.Advance(phase, steps, out var wait))
                    yield return wait;
        }
        finally
        {
            SystemFont.Disabled = disabled;
        }
    }

    private IEnumerable<(string phase, IEnumerator steps)> Phases()
    {
        yield return ("environment", Environment());
        yield return ("cuts", Cuts());
        yield return ("languages", LanguageCuts());
        yield return ("families", NamedFamilies());
        yield return ("batches", Batches());
        yield return ("odd", Odd());
        if (WorkerPool.IsParallelSupported) yield return ("threads", Threads());
        yield return ("reset", Reset());
        yield return ("performance", Performance());
        yield return ("pages", CapturePages());
    }

    /// <summary>Takes the next step of a phase; a phase that throws is recorded as failed and the next one runs.</summary>
    private bool Advance(string phase, IEnumerator steps, out object wait)
    {
        wait = null;
        try
        {
            if (!steps.MoveNext()) return false;
            wait = steps.Current;
            return true;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            results.Add(new TestResult
            {
                ClassName = className, MethodName = phase + "-exception", ErrorMessage = e.Message,
                StackTrace = e.ToString(), StartTime = DateTime.UtcNow, EndTime = DateTime.UtcNow,
            });
            return false;
        }
    }

    private bool FrameSpent => frame.Elapsed.TotalMilliseconds > frameBudgetMs;

    private object NextFrame()
    {
        frame.Restart();
        return null;
    }

    private static int[] Codepoints(string text)
    {
        var list = new List<int>(text.Length);
        for (var i = 0; i < text.Length; i += char.IsSurrogatePair(text, i) ? 2 : 1)
            list.Add(char.ConvertToUtf32(text, i));
        return list.ToArray();
    }

    private static string Hex(int[] sequence)
        => string.Join(" ", Array.ConvertAll(sequence, value => value.ToString("X4", CultureInfo.InvariantCulture)));

    /// <summary>The face a text whose font is the system font gets for <paramref name="sequence"/>, asked alone.</summary>
    private static Answer Ask(int[] sequence, string language, int weight, bool italic, string family = null)
    {
        family ??= SystemFont.Default?.FaceInfo.familyName;
        return new Answer(SystemFont.TryResolveSequence(sequence, language, weight, italic, null, family));
    }

    private IEnumerator Environment()
    {
        var check = new Check("environment");
        check.Report($"platform\t{Application.platform}");
        check.Report($"device\t{SystemInfo.deviceModel}");
        check.Report($"os\t{SystemInfo.operatingSystem}");
        check.Report($"unity\t{Application.unityVersion}");
        check.Report($"processor\t{SystemInfo.processorType} x{SystemInfo.processorCount}");
        check.Report($"debug\t{Debug.isDebugBuild}");
        var start = Stopwatch.StartNew();
        var font = new Answer(SystemFont.Default);
        check.Report($"default\t{font}");
        check.Report($"defaultMs\t{Ms(start)}");
        if (!font.Found) check.Violate("The platform gave no default system font");
        check.Finish(results);
        yield return NextFrame();
    }

    /// <summary>Every cluster at every weight and slant, with the violations of <see cref="Ladder"/>.</summary>
    private IEnumerator Cuts()
    {
        var upright = new Check("cuts-upright");
        var italic = new Check("cuts-italic");
        foreach (var (script, clusters) in Scripts)
            foreach (var cluster in clusters)
            {
                var sequence = Codepoints(cluster);
                Ladder(upright, $"{script}\t{Hex(sequence)}", sequence, string.Empty, false);
                Ladder(italic, $"{script}\t{Hex(sequence)}", sequence, string.Empty, true);
                if (FrameSpent) yield return NextFrame();
            }
        upright.Finish(results);
        italic.Finish(results);
    }

    private IEnumerator LanguageCuts()
    {
        var check = new Check("languages");
        foreach (var (tag, clusters) in Languages)
            foreach (var cluster in clusters)
            {
                var sequence = Codepoints(cluster);
                Ladder(check, $"{tag}\t{Hex(sequence)}", sequence, tag, false);
                if (FrameSpent) yield return NextFrame();
            }
        check.Finish(results);
    }

    /// <summary>Every family of the package's catalog and <see cref="MoreFamilies"/> the device has: its name at every weight, and the violations of <see cref="Ladder"/>.</summary>
    private IEnumerator NamedFamilies()
    {
        var check = new Check("families");
        var entries = new List<SystemFontCatalog.Entry>(SystemFontCatalog.EntriesFor(SystemFontCatalog.CurrentPlatform()));
        foreach (var family in MoreFamilies) entries.Add(new SystemFontCatalog.Entry { displayName = family });
        foreach (var entry in entries)
        {
            var family = SystemFontCatalog.FamilyOf(entry);
            var sample = Sample(family);
            foreach (var italic in new[] { false, true })
            {
                var regular = Ask(sample, string.Empty, 400, italic, family);
                if (!regular.Found || !SystemFontCatalog.Satisfies(entry, regular.family))
                {
                    if (!italic) check.Report($"{entry.displayName}	not installed, the platform answers {regular}");
                    continue;
                }
                var answers = Ladder(check, $"{entry.displayName}	{(italic ? "italic" : "upright")}",
                    sample, string.Empty, italic, family);
                for (var i = 0; i < answers.Length; i++)
                    if (answers[i].Found && !SystemFontCatalog.Satisfies(entry, answers[i].family))
                        check.Violate($"{entry.displayName} {Weights[i]}: another family {answers[i]}");
                if (!entry.isRole && SystemFont.TryResolveStyledFamily(family, (uint)sample[0], 700, italic, null) == null)
                    check.Violate($"{entry.displayName}: TryResolveStyledFamily gives nothing at 700");
                if (!italic && sample[0] == 'A') CompareWithAsset(check, entry.displayName, family);
                if (FrameSpent) yield return NextFrame();
            }
        }
        check.Finish(results);
    }

    /// <summary>A letter the family is made for: a script family need not have Latin.</summary>
    private static int[] Sample(string family)
        => new[]
        {
            family.Contains("Thai") || family == "Thonburi" ? 0xE01
            : family.Contains("Devanagari") ? 0x928
            : family == "Geeza Pro" ? 0x639
            : family.Contains("CJK") || family.Contains("PingFang") || family.Contains("Hiragino") ? 0x4E2D
            : family.Contains("Gothic Neo") ? 0xAC00
            : 'A',
        };

    /// <summary>A font asset that names the family draws the cut a text asks the family for.</summary>
    private static void CompareWithAsset(Check check, string label, string family)
    {
        foreach (var weight in PageWeights)
        {
            var answer = Ask(new[] { (int)'A' }, string.Empty, weight, false, family);
            if (!SystemFont.TryMaterializeFamily(family, weight, out var source, out _, out _))
            {
                if (answer.Found) check.Violate($"{label} {weight}: an asset gets nothing, a text {answer}");
                continue;
            }
            if (source.faceInfo.familyName != answer.family || source.faceInfo.styleName != answer.style)
                check.Violate($"{label} {weight}: an asset gets {source.faceInfo.familyName}/{source.faceInfo.styleName}, a text {answer}");
            source.ReleaseFaces();
        }
    }

    /// <summary>
    /// Asks one cluster at every weight. Violations: a weight that loses a cluster another weight resolves, a cut farther
    /// from the request than a cut of the same family and slant another weight gets, and a face whose weight or slant
    /// contradicts its style. A family that changes with the weight, or a slant that comes and goes, is reported and left
    /// to the reader: both can be the platform's own.
    /// </summary>
    private static Answer[] Ladder(Check check, string label, int[] sequence, string language, bool italic,
        string family = null)
    {
        var answers = new Answer[Weights.Length];
        var found = -1;
        for (var i = 0; i < Weights.Length; i++)
        {
            answers[i] = Ask(sequence, language, Weights[i], italic, family);
            if (answers[i].Found && found < 0) found = i;
        }
        if (found < 0)
        {
            check.Report($"{label}\tnone");
            return answers;
        }

        var line = new StringBuilder(label);
        var note = "";
        for (var i = 0; i < Weights.Length; i++)
        {
            ref readonly var answer = ref answers[i];
            if (i == 0 || !answer.SameCut(in answers[i - 1])) line.Append('\t').Append(Weights[i]).Append('=').Append(answer);
            if (!answer.Found)
            {
                check.Violate($"{label} {Weights[i]}: lost, {Weights[found]} gets {answers[found]}");
                continue;
            }
            if (answer.family != answers[found].family) note = "\tFAMILY CHANGES";
            else if (answer.slanted != answers[found].slanted) note = "\tSLANT CHANGES";
            if (!answer.MatchesStyle) check.Violate($"{label} {Weights[i]}: {answer} contradicts its style");
            if (!answer.cssWeight) note = "\tNOT CSS WEIGHTS";
            if (!answer.cssWeight || !RanksWeight(Weights[i])) continue;
            var distance = answer.Distance(Weights[i]);
            for (var other = 0; other < Weights.Length; other++)
            {
                ref readonly var candidate = ref answers[other];
                if (!candidate.Found || candidate.family != answer.family || candidate.slanted != answer.slanted
                    || candidate.Distance(Weights[i]) >= distance) continue;
                check.Violate($"{label} {Weights[i]}: {answer}, {Weights[other]} gets the closer {candidate}");
                break;
            }
        }
        check.Report(line.Append(note).ToString());
        return answers;
    }

    /// <summary>
    /// Whether the platform ranks cuts by this exact weight: Android ranks them by whole hundreds, and a browser does not
    /// tell which cut it draws.
    /// </summary>
    private static bool RanksWeight(int weight)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return false;
#elif UNITY_ANDROID && !UNITY_EDITOR
        return weight % 100 == 0;
#else
        return true;
#endif
    }

    /// <summary>Mixed-script texts laid out by a component: every cluster gets the face it gets asked alone, whole.</summary>
    private IEnumerator Batches()
    {
        var check = new Check("batches");
        var root = new GameObject("System font batches", typeof(RectTransform));
        root.transform.SetParent(canvas.transform, false);
        ((RectTransform)root.transform).sizeDelta = new Vector2(1000, 400);
        var compared = 0;
        foreach (var body in BatchTexts)
            foreach (var weight in new[] { 300, 400, 600, 900 })
                foreach (var italic in new[] { false, true })
                {
                    var text = root.AddComponent<UniText>();
                    text.Styles.Add(Style.Tag(new BoldModifier(), "b"));
                    text.Styles.Add(Style.Tag(new ItalicModifier(), "i"));
                    text.FontSize = 20;
                    text.Text = italic ? $"<i><b={weight}>{body}</b></i>" : $"<b={weight}>{body}</b>";
                    Canvas.ForceUpdateCanvases();
                    yield return NextFrame();
                    compared += CompareWithAlone(check, text, body, weight, italic);
                    Object.DestroyImmediate(text);
                    var renderer = root.GetComponent<CanvasRenderer>();
                    if (renderer != null) Object.DestroyImmediate(renderer);
                }
        Object.Destroy(root);
        check.Report($"clusters compared\t{compared}");
        if (compared == 0) check.Violate("No cluster was compared");
        check.Finish(results);
    }

    private static int CompareWithAlone(Check check, UniText text, string body, int weight, bool italic)
    {
        var processor = text.TextProcessor;
        if (processor == null)
        {
            check.Violate($"{weight}{(italic ? "i" : "")}: the component was not processed: {body}");
            return 0;
        }
        var runs = processor.buf.shapedRuns;
        var codepoints = processor.buf.codepoints;
        var compared = 0;
        for (var start = 0; start < codepoints.count;)
        {
            var end = start + 1;
            while (end < codepoints.count && Extends(codepoints[end])) end++;
            var sequence = new int[end - start];
            for (var i = start; i < end; i++) sequence[i - start] = codepoints[i];
            var first = start;
            start = end;
            if (sequence[0] <= ' ') continue;

            Answer drawn = default;
            for (var i = 0; i < runs.count; i++)
            {
                var range = runs[i].range;
                if (range.start > first || range.End <= first) continue;
                if (range.End < end) check.Violate($"{weight}{(italic ? "i" : "")} {Hex(sequence)}: cut between two faces in {body}");
                if (text.FontProvider.TryGetFont(runs[i].fontId, out var font)) drawn = new Answer(font);
                break;
            }
            var alone = Ask(sequence, string.Empty, weight, italic);
            compared++;
            if (alone.Found && !drawn.SameCut(in alone))
                check.Violate($"{weight}{(italic ? "i" : "")} {Hex(sequence)}: {drawn} in a text, {alone} alone, in {body}");
        }
        return compared;
    }

    private static bool Extends(int codepoint)
        => codepoint is >= 0x300 and <= 0x36F or >= 0x5B0 and <= 0x5C7 or >= 0x64B and <= 0x670
               or >= 0x93A and <= 0x93C or >= 0x93E and <= 0x94F or 0xE31 or >= 0xE34 and <= 0xE3A
               or >= 0xE47 and <= 0xE4E or 0xE33 or 0x200C or 0x200D;

    /// <summary>Sequences no font is expected to draw resolve to nothing or to a face that covers them, at any weight, without an exception.</summary>
    private IEnumerator Odd()
    {
        var check = new Check("odd");
        foreach (var sequence in OddSequences)
            foreach (var weight in new[] { 0, 1, 400, 1000, 5000, -5 })
            {
                try
                {
                    var answer = Ask(sequence, string.Empty, weight, weight == 1000);
                    if (weight == 400) check.Report($"{Hex(sequence)}\t{answer}");
                }
                catch (Exception e)
                {
                    check.Violate($"{Hex(sequence)} {weight}: {e.GetType().Name} {e.Message}");
                }
                if (FrameSpent) yield return NextFrame();
            }
        foreach (var family in new[] { "", " ", "No Such Family 12345", new string('F', 2000), "微軟正黑體", "@Arial" })
            foreach (var language in new[] { "", "xx", "en-US-x-private", "zh_Hant_TW", "abcdefgh-abcdefgh" })
                try
                {
                    var answer = new Answer(SystemFont.TryResolveSequence(new[] { 0x4E2D }, language, 600, false, null, family));
                    if (!answer.Found) check.Report($"family '{family.Substring(0, Math.Min(24, family.Length))}' language '{language}'\tnone");
                }
                catch (Exception e)
                {
                    check.Violate($"family '{family.Substring(0, Math.Min(24, family.Length))}' language '{language}': {e.GetType().Name} {e.Message}");
                }
        check.Finish(results);
        yield return NextFrame();
    }

    /// <summary>
    /// Every answer asked on many threads at once, while few faces are kept so that the threads evict each other's. The
    /// main thread, which owns what the evicted faces drew, catches up in a frame. The threads stop by their own clock: a
    /// layout on the main thread starts over whenever another thread evicts a face.
    /// </summary>
    private IEnumerator Threads()
    {
        var check = new Check("threads");
        var questions = new List<(int[] sequence, int weight, bool italic, Answer expected)>();
        foreach (var (_, clusters) in Scripts)
            foreach (var weight in MeasuredWeights)
            {
                var sequence = Codepoints(clusters[0]);
                questions.Add((sequence, weight, weight == 600, Ask(sequence, string.Empty, weight, weight == 600)));
                if (FrameSpent) yield return NextFrame();
            }

        var capacity = SystemFont.InactiveSourceCapacity;
        SystemFont.InactiveSourceCapacity = threadCount;
        long asked = 0;
        var clock = Stopwatch.StartNew();
        var failures = new List<string>();
        var threads = new Thread[threadCount];
        for (var t = 0; t < threadCount; t++)
        {
            var seed = t;
            threads[t] = new Thread(() =>
            {
                var random = new System.Random(seed);
                while (clock.Elapsed.TotalSeconds < 4)
                {
                    var (sequence, weight, italic, expected) = questions[random.Next(questions.Count)];
                    string failure = null;
                    try
                    {
                        var answer = Ask(sequence, string.Empty, weight, italic);
                        if (!answer.SameCut(in expected)) failure = $"{Hex(sequence)} {weight}: {answer}, expected {expected}";
                    }
                    catch (Exception e)
                    {
                        failure = $"{Hex(sequence)} {weight}: {e}";
                    }
                    Interlocked.Increment(ref asked);
                    if (failure == null) continue;
                    lock (failures) failures.Add(failure);
                }
            });
            threads[t].Start();
        }
        double longest = 0;
        while (Array.Exists(threads, thread => thread.IsAlive))
        {
            var started = clock.Elapsed.TotalMilliseconds;
            yield return NextFrame();
            longest = Math.Max(longest, clock.Elapsed.TotalMilliseconds - started);
        }
        var catchUp = Stopwatch.StartNew();
        yield return NextFrame();
        yield return NextFrame();
        SystemFont.InactiveSourceCapacity = capacity;
        foreach (var failure in failures) check.Violate(failure);
        if (catchUp.Elapsed.TotalSeconds > 2) check.Violate($"the main thread caught up in {Ms(catchUp)} ms");
        check.Report($"asked\t{asked}");
        check.Report($"longestFrameMs\t{longest.ToString("F1", CultureInfo.InvariantCulture)}");
        check.Report($"catchUpMs\t{Ms(catchUp)}");
        check.Finish(results);
    }

    /// <summary>The answers of the first pass, asked again after the caches and the platform resolver were dropped.</summary>
    private IEnumerator Reset()
    {
        var check = new Check("reset");
        var expected = new List<(string label, int[] sequence, int weight, Answer answer)>();
        foreach (var (script, clusters) in Scripts)
            foreach (var weight in MeasuredWeights)
            {
                var sequence = Codepoints(clusters[0]);
                expected.Add((script, sequence, weight, Ask(sequence, string.Empty, weight, false)));
                if (FrameSpent) yield return NextFrame();
            }
        SystemFont.Reset();
        yield return NextFrame();
        foreach (var (label, sequence, weight, answer) in expected)
        {
            var again = Ask(sequence, string.Empty, weight, false);
            if (!again.SameCut(in answer)) check.Violate($"{label} {weight}: {again} after a reset, {answer} before");
            if (FrameSpent) yield return NextFrame();
        }
        check.Finish(results);
    }

    private static string Ms(Stopwatch stopwatch) => stopwatch.Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture);

    private static List<int[]> Range(int first, int count, int step = 1)
    {
        var list = new List<int[]>(count);
        for (var i = 0; i < count; i++) list.Add(new[] { first + i * step });
        return list;
    }

    /// <summary>
    /// Cold and cached cost of resolving distinct clusters, per script and weight: the first cluster (which also loads
    /// the face), the mean of the others, the mean of a cached repeat, and one component laying the clusters out, cold
    /// and again with the faces known. Weight 400 is the control of the other weights: it never searches a family for
    /// another cut.
    /// </summary>
    private IEnumerator Performance()
    {
        var check = new Check("performance");
        var sets = new (string name, List<int[]> clusters)[]
        {
            ("Latin", Range(0xC0, 48)), ("Cyrillic", Range(0x410, 48)), ("Han", Range(0x4E00, 48, 7)),
            ("Hangul", Range(0xAC00, 48, 29)), ("Thai", Range(0xE01, 40)), ("Devanagari", Range(0x915, 36)),
            ("Arabic", Range(0x627, 24)), ("Hebrew", Range(0x5D0, 26)), ("Georgian", Range(0x10D0, 32)),
        };
        check.Report("set\tweight\titalic\tclusters\tfirstMs\trestMeanMs\tcachedMeanUs\ttextColdMs\ttextWarmMs");
        foreach (var (set, clusters) in sets)
            foreach (var weight in MeasuredWeights)
            {
                var first = double.MaxValue;
                var rest = double.MaxValue;
                var cached = double.MaxValue;
                for (var repeat = 0; repeat < 3; repeat++)
                {
                    SystemFont.Reset();
                    _ = SystemFont.Default;
                    yield return NextFrame();
                    var watch = Stopwatch.StartNew();
                    Ask(clusters[0], string.Empty, weight, false);
                    first = Math.Min(first, watch.Elapsed.TotalMilliseconds);
                    watch.Restart();
                    for (var i = 1; i < clusters.Count; i++) Ask(clusters[i], string.Empty, weight, false);
                    rest = Math.Min(rest, watch.Elapsed.TotalMilliseconds / (clusters.Count - 1));
                    watch.Restart();
                    for (var pass = 0; pass < 20; pass++)
                        for (var i = 0; i < clusters.Count; i++) Ask(clusters[i], string.Empty, weight, false);
                    cached = Math.Min(cached, watch.Elapsed.TotalMilliseconds * 1000 / (20 * clusters.Count));
                    yield return NextFrame();
                }

                var body = new StringBuilder();
                foreach (var cluster in clusters) body.Append(char.ConvertFromUtf32(cluster[0]));
                var cold = double.MaxValue;
                var warm = double.MaxValue;
                for (var repeat = 0; repeat < 3; repeat++)
                {
                    SystemFont.Reset();
                    _ = SystemFont.Default;
                    yield return NextFrame();
                    cold = Math.Min(cold, MeasureText($"<b={weight}>{body}</b>"));
                    yield return NextFrame();
                    warm = Math.Min(warm, MeasureText($"<b={weight}>{body}</b>"));
                    yield return NextFrame();
                }
                check.Report(string.Format(CultureInfo.InvariantCulture, "{0}\t{1}\tupright\t{2}\t{3:F3}\t{4:F4}\t{5:F3}\t{6:F3}\t{7:F3}",
                    set, weight, clusters.Count, first, rest, cached, cold, warm));
            }
        check.Finish(results);
    }

    private double MeasureText(string content)
    {
        var target = new GameObject("System font text", typeof(RectTransform));
        target.SetActive(false);
        target.transform.SetParent(canvas.transform, false);
        ((RectTransform)target.transform).sizeDelta = new Vector2(1000, 400);
        var text = target.AddComponent<UniText>();
        text.Styles.Add(Style.Tag(new BoldModifier(), "b"));
        text.FontSize = 20;
        text.Text = content;
        Canvas.ForceUpdateCanvases();
        var watch = Stopwatch.StartNew();
        target.SetActive(true);
        Canvas.ForceUpdateCanvases();
        var elapsed = watch.Elapsed.TotalMilliseconds;
        Object.DestroyImmediate(target);
        return elapsed;
    }

    /// <summary>
    /// Draws the scripts over the weights, upright and italic, and the languages over the weights. The cell rectangles
    /// go into the results in viewport coordinates, so the ink of every cell can be read off the images.
    /// </summary>
    private IEnumerator CapturePages()
    {
        var check = new Check("pages");
        foreach (var italic in new[] { false, true })
        {
            var rows = new (string name, string language, string text)[PageScripts.Length];
            for (var i = 0; i < rows.Length; i++) rows[i] = (PageScripts[i].name, null, PageScripts[i].text);
            for (var page = CapturePage(check, italic ? "italic" : "upright", rows, italic); page.MoveNext();)
                yield return page.Current;
        }
        for (var page = CapturePage(check, "languages", PageLanguages, false); page.MoveNext();)
            yield return page.Current;
        check.Finish(results);
        SystemFont.TrimUnused();
    }

    /// <summary>
    /// A page has a camera and an unscaled canvas of its own: the canvas of the slideshow is scaled by the screen, which a
    /// capture can be larger than, and what it draws under a page shows through where it is not a number.
    /// </summary>
    private IEnumerator CapturePage(Check check, string page, (string name, string language, string text)[] rows, bool italic)
    {
        var camera = new GameObject("System font cuts", typeof(Camera)).GetComponent<Camera>();
        camera.transform.position = new Vector3(0f, 0f, -10000f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.white;
        camera.depth = 100f;
        var root = new GameObject("Page", typeof(RectTransform), typeof(Canvas));
        root.transform.SetParent(camera.transform, false);
        var pageCanvas = root.GetComponent<Canvas>();
        pageCanvas.renderMode = RenderMode.ScreenSpaceCamera;
        pageCanvas.worldCamera = camera;
        pageCanvas.planeDistance = 1f;
        var rect = (RectTransform)root.transform;

        const float labelWidth = 0.1f;
        var cellWidth = (1f - labelWidth) / PageWeights.Length;
        var cellHeight = 1f / (rows.Length + 1);
        TestScreenshot.GetCaptureSize(out var captureWidth, out var captureHeight);
        var size = Mathf.Min(captureWidth * cellWidth * 0.3f, captureHeight * cellHeight * 0.45f);
        for (var column = 0; column < PageWeights.Length; column++)
            Cell(rect, PageWeights[column].ToString(CultureInfo.InvariantCulture), null,
                new Rect(labelWidth + column * cellWidth, 1f - cellHeight, cellWidth, cellHeight), size * 0.8f);
        for (var row = 0; row < rows.Length; row++)
        {
            var bottom = 1f - (row + 2) * cellHeight;
            Cell(rect, rows[row].name, null, new Rect(0, bottom, labelWidth, cellHeight), size * 0.4f);
            for (var column = 0; column < PageWeights.Length; column++)
            {
                var area = new Rect(labelWidth + column * cellWidth, bottom, cellWidth, cellHeight);
                var body = $"<b={PageWeights[column]}>{rows[row].text}</b>";
                Cell(rect, italic ? $"<i>{body}</i>" : body, rows[row].language, area, size);
                check.Report(string.Format(CultureInfo.InvariantCulture, "{0}\t{1}\t{2}\t{3:F5}\t{4:F5}\t{5:F5}\t{6:F5}",
                    page, rows[row].name, PageWeights[column], area.xMin, area.yMin, area.xMax, area.yMax));
            }
        }
        for (var i = 0; i < settleFrames; i++) yield return NextFrame();
        TestScreenshot.Capture($"{name}-{page}", camera);
        Object.Destroy(camera.gameObject);
        yield return NextFrame();
    }

    private static void Cell(RectTransform parent, string content, string language, Rect area, float size)
    {
        var cell = new GameObject("Cell", typeof(RectTransform));
        var rect = (RectTransform)cell.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = area.min;
        rect.anchorMax = area.max;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var text = cell.AddComponent<UniText>();
        text.Styles.Add(Style.Tag(new BoldModifier(), "b"));
        text.Styles.Add(Style.Tag(new ItalicModifier(), "i"));
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.VerticalAlignment = VerticalAlignment.Middle;
        text.WordWrap = false;
        text.color = Color.black;
        text.FontSize = size;
        if (language != null) text.Language = language;
        text.Text = content;
    }
}
#endif
