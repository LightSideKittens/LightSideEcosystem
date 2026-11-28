using LightSide.Benchmark;
using UnityEngine;

/// <summary>
/// Hands the shared benchmark harness what only this project can supply: UniText's own build
/// configuration for the run's <c>systemInfo</c>, and the screen capture that stores benchmark screenshots as
/// the screen shows them, UI Toolkit panels included.
/// </summary>
public static class UniTextBenchmarkEnvironment
{
    /// <summary>
    /// Whether the launch carries <c>-benchmarkMergedLayers</c>: every UniText text of the text and scenario suites then
    /// draws glyph-major with its layers merged into one quad per glyph, the opt-in mode of <see cref="LightSide.PaintOrderModifier"/>.
    /// </summary>
    public static bool MergedLayers => BenchmarkLaunch.Has("-benchmarkMergedLayers");

    /// <summary>
    /// Whether the launch carries <c>-benchmarkPlain</c>, on the command line or baked into the player: no text of the text
    /// and scenario suites, of any engine, then draws an outline or a shadow, where a launch without it gives every text both.
    /// </summary>
    public static bool Plain => BenchmarkLaunch.Has("-benchmarkPlain");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        BenchmarkScreenshot.Capturer = TestScreenshot.CaptureScreen;

        BenchmarkEnvironment.ExtraSystemInfo["unitextDebugDefine"] =
#if UNITEXT_DEBUG
            true;
#else
            false;
#endif
        BenchmarkEnvironment.ExtraSystemInfo["unitextProfileDefine"] =
#if UNITEXT_PROFILE
            true;
#else
            false;
#endif
    }
}
