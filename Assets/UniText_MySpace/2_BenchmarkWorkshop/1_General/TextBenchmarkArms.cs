using System;
using System.Collections;
using LightSide.Benchmark;
using Newtonsoft.Json.Linq;

/// <summary>
/// One engine in a per-frame text case: activation builds the engine's texts on their pages in the phase's start state,
/// every step applies the next input, deactivation destroys them.
/// </summary>
/// <remarks>
/// Inputs come from the arm's own cursor, never from the scheduler's frame index, so a gate arm that reuses this object
/// still never repeats a text; repeating one would turn the engines' content caches warm.
/// </remarks>
public sealed class TextPhaseArm : IBenchmarkArm
{
    readonly TextBenchmarkBase bench;
    readonly string engineId;
    readonly TextPhase phase;
    readonly string[] inputs;
    int cursor;
    long commitsAtSlotStart;

    /// <param name="armCode">Digit that keeps this arm's input texts apart from every other arm's of the case.</param>
    /// <param name="frames">Frames the arm can step at most, gate arms that step it included; its input sequence repeats beyond that.</param>
    public TextPhaseArm(TextBenchmarkBase bench, string engineId, TextPhase phase, int armCode, int frames)
    {
        this.bench = bench;
        this.engineId = engineId;
        this.phase = phase;
        inputs = TextPhases.Inputs(phase, bench.Corpus, armCode, frames);
        bench.PrepareInputs(inputs);
    }

    public string Id => engineId;

    public string[] Markers => Array.Empty<string>();

    public void Activate()
    {
        bench.SelectEngine(engineId);
        bench.SetStructure(phase);
        bench.CreateAll(TextPhases.PlainText(bench.Corpus));
        bench.ApplyPhase(phase);
        bench.Visible = true;
    }

    public void Deactivate()
    {
        bench.Visible = false;
        bench.DestroyAll();
    }

    public void Step(int frame) => bench.Step(phase, inputs, cursor++);

    public void SlotStarts() => commitsAtSlotStart = bench.Commits;

    public void SlotEnds(int measuredFrames)
    {
        var commits = bench.Commits;
        bench.RequireCommits(engineId, phase, commits >= 0 ? commits - commitsAtSlotStart : -1, measuredFrames);
    }

    public void Describe(JObject arm) => bench.Describe(engineId, arm);
}

/// <summary>One engine creating every text of a page set with its own text: the synchronous creation, then the frames until every text is processed.</summary>
public sealed class TextCreationArm : IBenchmarkOneShotArm
{
    readonly TextBenchmarkBase bench;
    readonly string engineId;
    readonly string text;
    long commitsBefore;

    /// <param name="armCode">Digit that keeps this arm's text apart from every other arm's, so its first round is cold.</param>
    public TextCreationArm(TextBenchmarkBase bench, string engineId, int armCode)
    {
        this.bench = bench;
        this.engineId = engineId;
        text = TextPhases.TokenVariants(TextPhases.PlainText(bench.Corpus), 1, armCode, true)[0];
        bench.PrepareInputs(new[] { text });
    }

    public string Id => engineId;

    public IEnumerator Prepare()
    {
        bench.SelectEngine(engineId);
        bench.SetStructure(TextPhase.Idle);
        bench.Visible = true;
        yield return null;
    }

    public void Operate()
    {
        commitsBefore = bench.Commits;
        bench.CreateAll(text);
    }

    public bool Committed => bench.AllProcessed(commitsBefore);

    public IEnumerator Reset()
    {
        bench.DestroyAll();
        bench.Visible = false;
        yield return null;
        yield return null;
    }

    public void Describe(JObject arm) => bench.Describe(engineId, arm);
}

/// <summary>One engine destroying every text of a processed page set: the synchronous destruction and the frame that follows.</summary>
public sealed class TextDestructionArm : IBenchmarkOneShotArm
{
    readonly TextBenchmarkBase bench;
    readonly string engineId;
    readonly string text;
    readonly int maxCommitFrames;

    public TextDestructionArm(TextBenchmarkBase bench, string engineId, int armCode, int maxCommitFrames)
    {
        this.bench = bench;
        this.engineId = engineId;
        this.maxCommitFrames = maxCommitFrames;
        text = TextPhases.TokenVariants(TextPhases.PlainText(bench.Corpus), 1, armCode, true)[0];
        bench.PrepareInputs(new[] { text });
    }

    public string Id => engineId;

    public IEnumerator Prepare()
    {
        bench.SelectEngine(engineId);
        bench.SetStructure(TextPhase.Idle);
        bench.Visible = true;
        var before = bench.Commits;
        bench.CreateAll(text);
        for (var frames = 0; ; frames++)
        {
            yield return null;
            if (bench.AllProcessed(before)) break;
            if (frames >= maxCommitFrames)
                throw new InvalidOperationException($"{engineId} texts did not process within {maxCommitFrames} frames.");
        }
        yield return null;
    }

    public void Operate() => bench.DestroyAll();

    public bool Committed => true;

    public IEnumerator Reset()
    {
        bench.Visible = false;
        yield return null;
    }

    public void Describe(JObject arm) => bench.Describe(engineId, arm);
}
