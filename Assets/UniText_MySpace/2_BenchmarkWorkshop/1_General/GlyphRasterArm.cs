using System;
using System.Collections;
using LightSide.Benchmark;
using Newtonsoft.Json.Linq;

/// <summary>One step of a glyph case's ladder: the first <see cref="CodePoints"/> distinct code points of the font's corpus that the font draws.</summary>
public readonly struct GlyphRung
{
    /// <summary>The step's name in arm ids: its code point count, or <c>all</c> for the whole corpus.</summary>
    public readonly string Name;

    /// <summary>The text every engine shows.</summary>
    public readonly string Text;

    public readonly int CodePoints;

    public GlyphRung(string name, string text, int codePoints)
    {
        Name = name;
        Text = text;
        CodePoints = codePoints;
    }
}

/// <summary>
/// One engine rasterizing one rung of a glyph case: its glyph tables emptied under the hidden text, the text shown, and the
/// frames until the engine drew it.
/// </summary>
/// <remarks>Every round must add the same glyphs; a round that adds another count fails the case as a partial rasterization.</remarks>
public sealed class GlyphRasterArm : IBenchmarkOneShotArm
{
    readonly GlyphRasterBenchmarkBase bench;
    readonly bool styled;
    int glyphsBefore;

    public GlyphRasterArm(GlyphRasterBenchmarkBase bench, string engineId, GlyphRung rung, bool styled)
    {
        this.bench = bench;
        this.styled = styled;
        EngineId = engineId;
        Rung = rung;
        Id = $"{engineId}.{rung.Name}";
    }

    public string Id { get; }

    /// <summary>The engine configuration the arm runs.</summary>
    public string EngineId { get; }

    /// <summary>The engine component the arm drives.</summary>
    public GlyphRasterBenchmarkBase Bench => bench;

    public GlyphRung Rung { get; }

    /// <summary>Glyphs every round added, -1 before the first round ended.</summary>
    public int Glyphs { get; private set; } = -1;

    public IEnumerator Prepare()
    {
        bench.SelectEngine(EngineId);
        yield return bench.Clear(Rung.Text);
        glyphsBefore = bench.GlyphCount;
    }

    public void Operate() => bench.Show();

    public bool Committed => bench.Drawn;

    /// <exception cref="InvalidOperationException">The engine drew by another path than its measured one, added no glyph, or added another count than in an earlier round.</exception>
    public IEnumerator Reset()
    {
        try
        {
            bench.ValidateDelivery();
            var added = bench.GlyphCount - glyphsBefore;
            if (added <= 0)
                throw new InvalidOperationException($"Arm '{Id}' drew its text without adding a glyph.");
            if (Glyphs >= 0 && added != Glyphs)
                throw new InvalidOperationException($"Arm '{Id}' added {added} glyphs after {Glyphs} in an earlier round: a partial rasterization.");
            Glyphs = added;
            bench.Hide();
        }
        finally
        {
            bench.ReleaseEngine();
        }
        yield return null;
    }

    /// <summary>Writes the arm descriptor with the rung's code point count as its load, the same for every engine at the rung; the glyphs the engine added are workload.</summary>
    public void Describe(JObject arm)
    {
        BenchmarkEngines.Describe(arm, EngineId, styled, Rung.CodePoints);
        bench.DescribeEngine(EngineId, arm);
    }

    /// <summary>The descriptor of a case arm as the scheduler records it in <c>armInfo</c>.</summary>
    public JObject ArmInfo()
    {
        var info = new JObject();
        Describe(info);
        info["control"] = JValue.CreateNull();
        return info;
    }
}
