using System;
using System.Text;
using UnityEngine;

/// <summary>A per-frame workload of the text suite: what every frame of a measured slot does to every text.</summary>
public enum TextPhase
{
    /// <summary>Nothing changes; the texts only stay on their pages.</summary>
    Idle,

    /// <summary>One paragraph of the document gets new content every frame; the others keep theirs.</summary>
    FullRebuild,

    /// <summary>Every paragraph gets new content every frame, so no content-addressed cache can hit.</summary>
    FullRebuildUnique,

    /// <summary>As <see cref="FullRebuildUnique"/> over the markup corpus with rich text on.</summary>
    FullRebuildRichText,

    /// <summary>The rect alternates between two sizes; word wrap on, auto-size off.</summary>
    LayoutWrapNoAuto,

    /// <summary>The rect alternates between two sizes; word wrap on, auto-size on.</summary>
    LayoutWrapAuto,

    /// <summary>The rect alternates between two sizes; word wrap off, auto-size off.</summary>
    LayoutNoWrapNoAuto,

    /// <summary>The rect alternates between two sizes; word wrap off, auto-size on.</summary>
    LayoutNoWrapAuto,

    /// <summary>The color alternates between two values.</summary>
    MeshRebuild
}

/// <summary>A text corpus of the text suite.</summary>
public enum TextCorpus
{
    /// <summary>The shaping-heavy showcase of <see cref="BenchmarkConfig.Multilingual"/>; TMP does not shape it.</summary>
    Multilingual,

    /// <summary>The plain Latin document of <see cref="BenchmarkConfig.Latin"/>: equal work for every engine.</summary>
    Latin
}

/// <summary>Names, states and inputs of the text suite's phases and corpora.</summary>
public static class TextPhases
{
    /// <summary>The page every text lays out in, in canvas units: narrower than the narrowest test phone's canvas.</summary>
    public const float PageWidth = 960f, PageHeight = 2048f;

    /// <summary>Distance between the tops of two neighboring pages, more than the tallest test phone's canvas, so only page 0 is on screen.</summary>
    public const float PageStride = 2600f;

    /// <summary>Gap above page 0.</summary>
    public const float TopMargin = 120f;

    /// <summary>Size every engine draws at while auto-size is off.</summary>
    public const float FontSize = 28f;

    /// <summary>How much smaller the second rect of a layout phase is in each axis.</summary>
    public const float LayoutStep = 24f;

    /// <summary>The two colors a mesh-rebuild phase alternates between.</summary>
    public static readonly Color ColorA = Color.white, ColorB = new(1f, 0.92f, 0.62f, 1f);

    public static readonly TextPhase[] All = (TextPhase[])Enum.GetValues(typeof(TextPhase));

    /// <summary>The phase's key in results and case ids.</summary>
    public static string Key(TextPhase phase) => phase switch
    {
        TextPhase.Idle => "idle",
        TextPhase.FullRebuild => "fullRebuild",
        TextPhase.FullRebuildUnique => "fullRebuildUnique",
        TextPhase.FullRebuildRichText => "fullRebuildRichText",
        TextPhase.LayoutWrapNoAuto => "layoutWrapNoAuto",
        TextPhase.LayoutWrapAuto => "layoutWrapAuto",
        TextPhase.LayoutNoWrapNoAuto => "layoutNoWrapNoAuto",
        TextPhase.LayoutNoWrapAuto => "layoutNoWrapAuto",
        _ => "meshRebuild"
    };

    public static string Key(TextCorpus corpus) => corpus == TextCorpus.Latin ? "latin" : "multilingual";

    /// <summary>Whether the corpus has a phase: the Latin corpus carries no markup, so its rich phase would repeat the unique one.</summary>
    public static bool Applies(TextPhase phase, TextCorpus corpus) =>
        phase != TextPhase.FullRebuildRichText || corpus == TextCorpus.Multilingual;

    public static bool WordWrap(TextPhase phase) => phase is not (TextPhase.LayoutNoWrapNoAuto or TextPhase.LayoutNoWrapAuto);

    public static bool AutoSize(TextPhase phase) => phase is TextPhase.LayoutWrapAuto or TextPhase.LayoutNoWrapAuto;

    public static bool RichText(TextPhase phase) => phase == TextPhase.FullRebuildRichText;

    public static bool ChangesText(TextPhase phase) =>
        phase is TextPhase.FullRebuild or TextPhase.FullRebuildUnique or TextPhase.FullRebuildRichText;

    public static bool ChangesLayout(TextPhase phase) =>
        phase is TextPhase.LayoutWrapNoAuto or TextPhase.LayoutWrapAuto or TextPhase.LayoutNoWrapNoAuto or TextPhase.LayoutNoWrapAuto;

    /// <summary>
    /// Whether every text must reprocess in every frame of the phase. Layout phases are exempt: an engine may find that a
    /// rect change leaves a text's output unchanged and skip it.
    /// </summary>
    public static bool RequiresCommits(TextPhase phase) => ChangesText(phase) || phase == TextPhase.MeshRebuild;

    public static string PlainText(TextCorpus corpus) =>
        corpus == TextCorpus.Latin ? BenchmarkConfig.Latin : BenchmarkConfig.MultilingualPlain;

    public static string MarkupText(TextCorpus corpus) =>
        corpus == TextCorpus.Latin ? BenchmarkConfig.Latin : BenchmarkConfig.Multilingual;

    /// <summary>The text a phase starts from before its first step.</summary>
    public static string StartText(TextPhase phase, TextCorpus corpus) =>
        RichText(phase) ? MarkupText(corpus) : PlainText(corpus);

    /// <summary>
    /// The <paramref name="count"/> texts a text phase applies frame after frame for the arm <paramref name="armCode"/>:
    /// each differs from every other text of every arm by a fixed-width numeric token, so frames cost the same and no
    /// two arms share a string. Digits are weak in the bidirectional algorithm, so a token leaves every paragraph's base
    /// direction to its own script. Null for phases that change no text.
    /// </summary>
    public static string[] Inputs(TextPhase phase, TextCorpus corpus, int armCode, int count) => phase switch
    {
        TextPhase.FullRebuild => TokenVariants(PlainText(corpus), count, armCode, false),
        TextPhase.FullRebuildUnique => TokenVariants(PlainText(corpus), count, armCode, true),
        TextPhase.FullRebuildRichText => TokenVariants(MarkupText(corpus), count, armCode, true),
        _ => null
    };

    /// <summary>
    /// Variants of <paramref name="corpus"/> whose tokens start every paragraph (<paramref name="everyParagraph"/>) or
    /// only the middle one.
    /// </summary>
    public static string[] TokenVariants(string corpus, int count, int armCode, bool everyParagraph)
    {
        var lines = corpus.Split('\n');
        var edited = lines.Length / 2;
        var variants = new string[count];
        var sb = new StringBuilder(corpus.Length + lines.Length * 10);
        for (var v = 0; v < count; v++)
        {
            sb.Clear();
            for (var l = 0; l < lines.Length; l++)
            {
                if (l > 0) sb.Append('\n');
                if (everyParagraph || l == edited)
                    sb.Append(armCode % 10).Append((v % 10000).ToString("D4")).Append('.').Append(l % 10).Append(' ');
                sb.Append(lines[l]);
            }
            variants[v] = sb.ToString();
        }
        return variants;
    }

    /// <summary>Whether a paragraph's first strong character belongs to a right-to-left script; markup tags are skipped.</summary>
    public static bool StartsRightToLeft(string paragraph)
    {
        var inTag = false;
        foreach (var c in paragraph)
        {
            if (c == '<') { inTag = true; continue; }
            if (inTag) { if (c == '>') inTag = false; continue; }
            if (c is >= '֐' and <= 'ࣿ' or >= 'יִ' and <= '﷿' or >= 'ﹰ' and <= '﻿') return true;
            if (char.IsLetter(c)) return false;
        }
        return false;
    }
}
