using System;
using System.Collections;
using LightSide.Benchmark;
using Newtonsoft.Json.Linq;

/// <summary>The memory record of a text or glyph memory launch, in the shape of the scenario suite's.</summary>
public static class BenchmarkMemoryRecord
{
    const int SettleFrames = 30;

    /// <summary>The <c>reference</c> participant: <see cref="Measure"/> with nothing built, belonging to no case (<c>memoryCase</c> null, <c>armInfo.control</c> reference).</summary>
    public static IEnumerator Reference(JObject record, int frames)
    {
        record["memoryCase"] = JValue.CreateNull();
        record["armInfo"] = new JObject { ["control"] = BenchmarkRounds.ReferenceArmId };
        yield return Measure(record, null, frames);
    }

    /// <summary>
    /// Runs <paramref name="setup"/> (none for the reference floor) and 30 settle frames inside one peak window, written as
    /// <c>setupPeak</c>; collects garbage; samples <paramref name="frames"/> frames into <c>metrics</c>, whose
    /// <c>processPeak</c> and <c>systemFootprint</c> cover the settled state; then sets <c>status</c> to measured.
    /// </summary>
    public static IEnumerator Measure(JObject record, IEnumerator setup, int frames)
    {
        var start = BenchmarkProcessMemory.StartPeak();
        if (setup != null) yield return setup;
        for (var i = 0; i < SettleFrames; i++)
            yield return null;
        var setupPeak = BenchmarkProcessMemory.PeakRecord(start);
        if (setupPeak != null) record["setupPeak"] = setupPeak;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using (var sampler = new BenchmarkFrameSampler(frames))
        {
            for (var i = 0; i < frames; i++)
            {
                yield return null;
                sampler.Sample();
            }
            record["metrics"] = sampler.Serialize(false);
        }
        record["status"] = "measured";
    }
}
