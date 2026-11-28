using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

/// <summary>The engine configurations the text and glyph suites compare, by the ids their arms and memory participants carry.</summary>
public static class BenchmarkEngines
{
    public const string UniTextSingleThreaded = "unitextSingleThreaded";
    public const string UniTextParallel = "unitextParallel";
    public const string Tmp = "tmp";
    public const string UIToolkit = "uiToolkit";

    /// <summary>UI Toolkit with no Unity job worker, the single-thread rival of <see cref="UniTextSingleThreaded"/>.</summary>
    public const string UIToolkitSingleThreaded = "uiToolkitSingleThreaded";

    /// <summary>Every engine configuration of the text suite this build compiles, UniText single-threaded first.</summary>
    public static readonly IReadOnlyList<string> Text = new[]
    {
        UniTextSingleThreaded, UniTextParallel, Tmp,
#if !LIGHTSIDE_NO_UITOOLKIT
        UIToolkit
#endif
    };

    /// <summary>Every engine configuration of the glyph suite this build compiles, UniText single-threaded first.</summary>
    public static readonly IReadOnlyList<string> Glyph = new[]
    {
        UniTextSingleThreaded, UniTextParallel, Tmp,
#if !LIGHTSIDE_NO_UITOOLKIT
        UIToolkit, UIToolkitSingleThreaded
#endif
    };

    /// <summary>
    /// Writes the arm descriptor of the benchmark package for <paramref name="engineId"/> drawing <paramref name="load"/>
    /// units; <paramref name="comparable"/> false marks an arm whose output differs from UniText's, such as TMP on a script
    /// it does not shape. <c>threads</c> pairs the rivals: <c>single</c> for UniText and UI Toolkit on one thread,
    /// <c>parallel</c> for their default configurations, null for TMP, which works on the main thread alone.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="engineId"/> is none of this class's ids.</exception>
    public static void Describe(JObject arm, string engineId, bool styled, long load, bool comparable = true)
    {
        if (!comparable) arm["comparable"] = false;
        arm["engine"] = engineId switch
        {
            UniTextSingleThreaded or UniTextParallel => "unitext",
            Tmp => "tmp",
            UIToolkit or UIToolkitSingleThreaded => "uitk",
            _ => throw new ArgumentException($"No benchmark engine is named '{engineId}'.", nameof(engineId))
        };
        arm["threads"] = engineId switch
        {
            UniTextParallel or UIToolkit => "parallel",
            UniTextSingleThreaded or UIToolkitSingleThreaded => "single",
            _ => JValue.CreateNull()
        };
        arm["style"] = styled ? "styled" : "plain";
        arm["load"] = load;
        arm["baseline"] = false;
    }
}
