using System;
using LightSide;
using UnityEngine;
using static LightSide.EasingType;
using static LightSide.MotionCycle;
using Random = UnityEngine.Random;

/// <summary>Every channel of one transform driven at once, each on its own curve and cycle.</summary>
[Serializable]
public sealed class ChannelChoirScenario : MoveItPlaygroundScenario
{
    public override string Title => "Channel choir";

    public override string Watch =>
        "One cube, four independent claims — position, scale, rotation and color never fight.";

    public override void Enter(MoveItStage stage)
    {
        var cube = stage.Spawn(PrimitiveType.Cube, Vector3.zero, new Color(0.30f, 0.65f, 1f), "Choir");
        var renderer = cube.GetComponent<Renderer>();

        cube.MoveYTo(2.5f, SineInOut.Over(1.4f)).Loop();
        cube.ScaleTo(1.6f, CubicInOut.Over(0.9f)).Loop();
        cube.RotateLocalTo(new Vector3(0f, 360f, 0f), 3f).Loop(Incremental);
        renderer.ColorTo(new Color(1f, 0.45f, 0.2f), QuadraticInOut.Over(2.1f)).Loop();

        stage.Check(MoveIt.Count(cube) >= 3, "three transform claims live on one cube");
        stage.Say("four claims on one target, four different clocks of their own");
    }
}

/// <summary>Every easing the package ships, side by side on the same timing.</summary>
[Serializable]
public sealed class EaseGalleryScenario : MoveItPlaygroundScenario
{
    public override string Title => "Ease gallery";

    public override string Watch => "Each sphere carries a different EasingType on an identical 1.6s ping-pong.";

    public override void Enter(MoveItStage stage)
    {
        const float pitch = 1.54f;
        var eases = (EasingType[])Enum.GetValues(typeof(EasingType));
        var columns = Mathf.CeilToInt(Mathf.Sqrt(eases.Length));
        var rows = Mathf.CeilToInt(eases.Length / (float)columns);
        var top = (rows - 1) * pitch * 0.5f;
        for (var i = 0; i < eases.Length; i++)
        {
            var column = i % columns;
            var row = i / columns;
            var origin = new Vector3(column * pitch - columns * (pitch * 0.5f), top - row * pitch, 0f);
            var hue = i / (float)eases.Length;
            var sphere = stage.Spawn(PrimitiveType.Sphere, origin, hue,
                eases[i].ToString());
            sphere.localScale = Vector3.one * 0.7f;
            sphere.MoveXTo(origin.x + 0.8f, eases[i].Over(1.6f)).Loop();
            stage.Label(origin + new Vector3(0.4f, -0.52f, 0f), eases[i].ToString(), 0.1f);
        }

        stage.Say($"{eases.Length} easing curves running in lock-step");
    }
}

/// <summary>The four repeat modes, so their difference is visible rather than described.</summary>
[Serializable]
public sealed class CycleModeScenario : MoveItPlaygroundScenario
{
    public override string Title => "Cycle modes";

    public override string Watch => "Restart snaps back, PingPong retraces, Yoyo re-eases, Incremental keeps going.";

    public override void Enter(MoveItStage stage)
    {
        var modes = new[] { Restart, PingPong, Yoyo, Incremental };
        for (var i = 0; i < modes.Length; i++)
        {
            var origin = new Vector3(-4f, 2f - i * 1.4f, 0f);
            var cube = stage.Spawn(PrimitiveType.Cube, origin, i / 4f,
                modes[i].ToString());
            cube.localScale = Vector3.one * 0.6f;
            cube.MoveXTo(origin.x + 2f, QuadraticInOut.Over(1.2f))
                .Loop(modes[i]);
            stage.Label(new Vector3(origin.x - 0.8f, origin.y, 0f), modes[i].ToString(), 0.22f,
                TextAnchor.MiddleRight);
        }

        stage.Say("four repeat modes on identical motions");
    }
}

/// <summary>Physical drivers: a spring that is re-aimed mid-flight and a fling that coasts to rest.</summary>
[Serializable]
public sealed class PhysicalDriverScenario : MoveItPlaygroundScenario
{
    private Transform spring;
    private Transform fling;
    private MoveIt springMotion;
    private float nextRetarget;
    private int flings;

    public override string Title => "Springs and flings";

    public override string Watch =>
        "The spring is re-aimed every 1.2s and keeps its velocity through the change; the fling coasts down.";

    public override void Enter(MoveItStage stage)
    {
        spring = stage.Spawn(PrimitiveType.Sphere, new Vector3(-2f, 1f, 0f), new Color(0.4f, 1f, 0.6f), "Spring");
        fling = stage.Spawn(PrimitiveType.Capsule, new Vector3(2f, 1f, 0f), new Color(1f, 0.8f, 0.3f), "Fling");

        springMotion = spring.MoveYTo(3f, new Spring(180f, 12f));
        nextRetarget = 1.2f;
        stage.Say("spring released toward y=3");
    }

    public override void Tick(MoveItStage stage)
    {
        if (stage.Elapsed >= nextRetarget)
        {
            nextRetarget = stage.Elapsed + 1.2f;
            var aim = Random.Range(0.5f, 3.5f);
            if (springMotion.IsAlive)
            {
                springMotion.RetargetTo(aim);
                stage.Say($"spring re-aimed to y={aim:0.00} without losing velocity");
            }
            else
            {
                springMotion = spring.MoveYTo(aim, new Spring(180f, 12f));
                stage.Say("spring settled and was released again");
            }
        }

        if (MoveIt.Count(fling) != 0 || fling == null) return;
        flings++;
        var velocity = new Vector3(Random.Range(-6f, 6f), 0f, 0f);
        fling.Fling(velocity, 0.08f);
        stage.Say($"fling #{flings} released at {velocity.x:0.0} u/s");
    }
}

/// <summary>Chain, group, insert, prepend, delays and cues assembled into one timeline.</summary>
[Serializable]
public sealed class SequenceTheaterScenario : MoveItPlaygroundScenario
{
    private MoveItSequence sequence;
    private int cues;

    public override string Title => "Sequence theater";

    public override string Watch => "Chained, grouped and inserted parts on one playhead, with cues firing between them.";

    public override void Enter(MoveItStage stage)
    {
        var a = stage.Spawn(PrimitiveType.Cube, new Vector3(-3f, 0f, 0f), new Color(1f, 0.4f, 0.4f), "Chain");
        var b = stage.Spawn(PrimitiveType.Sphere, new Vector3(0f, 0f, 0f), new Color(0.4f, 1f, 0.4f), "Group");
        var c = stage.Spawn(PrimitiveType.Capsule, new Vector3(3f, 0f, 0f), new Color(0.4f, 0.6f, 1f), "Insert");
        stage.Label(new Vector3(-3f, -0.9f, 0f), "Chain + Prepend", 0.18f);
        stage.Label(new Vector3(0f, -0.9f, 0f), "Group + Insert", 0.18f);
        stage.Label(new Vector3(3f, -0.9f, 0f), "Chain after delay", 0.18f);

        sequence = MoveItSequence.Create()
            .Chain(a.MoveYTo(2.5f, BackOut.Over(0.8f)))
            .Group(b.ScaleTo(1.8f, ElasticOut.Over(0.8f)))
            .ChainCallback(this, static self => self.cues++)
            .ChainDelay(0.25f)
            .Chain(c.RotateLocalTo(new Vector3(0f, 0f, 180f), 0.6f))
            .Insert(0.2f, b.MoveYTo(1.5f, SineInOut.Over(1.4f)))
            .Prepend(a.ScaleTo(0.6f, 0.3f))
            .InsertCallback(1.5f, this, static self => self.cues++)
            .Loop(Restart);

        stage.Check(sequence.IsAlive, "sequence is alive after assembly");
        stage.Say($"assembled: content {sequence.ContentDuration:0.00}s");
    }

    public override void Tick(MoveItStage stage)
    {
        if (sequence == null || !sequence.IsAlive) return;
        stage.Check(sequence.Playhead >= -0.001f && sequence.Playhead <= sequence.ContentDuration + 0.001f,
            "playhead stays inside the content");
        if (cues > 0 && cues % 20 == 0) stage.Say($"{cues} cues fired so far");
    }

    public override void Exit() => sequence = null;
}

/// <summary>A sequence built out of sequences, so ownership nests more than one level deep.</summary>
[Serializable]
public sealed class NestedSequenceScenario : MoveItPlaygroundScenario
{
    private MoveItSequence outer;

    public override string Title => "Nested sequences";

    public override string Watch => "Three inner sequences chained inside an outer one that loops back and forth.";

    public override void Enter(MoveItStage stage)
    {
        outer = MoveItSequence.Create();
        for (var i = 0; i < 3; i++)
        {
            var origin = new Vector3(i * 2f - 2f, 0f, 0f);
            var cube = stage.Spawn(PrimitiveType.Cube, origin, i / 3f, $"Nest{i}");
            var inner = MoveItSequence.Create()
                .Chain(cube.MoveYTo(2f, QuadraticOut.Over(0.4f)))
                .Chain(cube.ScaleTo(1.4f, 0.3f))
                .Chain(cube.MoveYTo(0f, BounceOut.Over(0.4f)));
            outer.Chain(inner);
        }

        outer.Loop();
        stage.Check(outer.IsAlive, "outer sequence survived nesting");
        stage.Say($"outer content {outer.ContentDuration:0.00}s over three nested sequences");
    }

    public override void Exit() => outer = null;
}

/// <summary>A paused timeline scrubbed forward and backward, the way a timeline editor drives one.</summary>
[Serializable]
public sealed class ScrubbingScenario : MoveItPlaygroundScenario
{
    private MoveItSequence sequence;
    private int cueHits;

    public override string Title => "Scrubbing";

    public override string Watch => "The playhead is driven by a sine, so every part is entered and left in both directions.";

    public override void Enter(MoveItStage stage)
    {
        var a = stage.Spawn(PrimitiveType.Cube, new Vector3(-2f, 0f, 0f), new Color(0.9f, 0.5f, 1f), "ScrubA");
        var b = stage.Spawn(PrimitiveType.Sphere, new Vector3(2f, 0f, 0f), new Color(0.5f, 0.9f, 1f), "ScrubB");

        sequence = MoveItSequence.Create()
            .Chain(a.MoveYTo(2.5f, QuadraticInOut.Over(1f)))
            .InsertCallback(1f, this, static self => self.cueHits++)
            .Chain(b.MoveYTo(2.5f, QuadraticInOut.Over(1f)))
            .Pause();

        stage.Say("timeline paused; the scrubber now owns the playhead");
    }

    public override void Tick(MoveItStage stage)
    {
        if (sequence == null || !sequence.IsAlive) return;
        var span = Mathf.Max(sequence.ContentDuration, 0.001f);
        var t = (Mathf.Sin(stage.Elapsed * 1.3f) * 0.5f + 0.5f) * span;
        stage.CheckSurvives(() => sequence.Seek(t), "seek to an arbitrary playhead");
        stage.Check(sequence.IsPaused, "scrubbing does not resume a paused timeline");
    }

    public override void Exit() => sequence = null;
}

/// <summary>Scaled, unscaled and manual clocks, plus the three update phases, under a moving Time.timeScale.</summary>
[Serializable]
public sealed class ClockAndPhaseScenario : MoveItPlaygroundScenario
{
    private float restoreTimeScale = 1f;

    public override string Title => "Clocks and phases";

    public override string Watch =>
        "Time.timeScale swings between 0 and 1.5: the scaled row stalls, the unscaled row never does.";

    public override void Enter(MoveItStage stage)
    {
        restoreTimeScale = Time.timeScale;

        var clocks = new[] { PlaybackClock.Scaled, PlaybackClock.Unscaled };
        for (var i = 0; i < clocks.Length; i++)
        {
            var origin = new Vector3(-3f, 2f - i * 1.3f, 0f);
            var cube = stage.Spawn(PrimitiveType.Cube, origin, i == 0 ? Color.cyan : Color.yellow,
                clocks[i].ToString());
            cube.localScale = Vector3.one * 0.6f;
            cube.MoveXTo(origin.x + 3f, 1.5f).On(clocks[i]).Loop();
            stage.Label(new Vector3(origin.x - 0.8f, origin.y, 0f), clocks[i].ToString(), 0.2f,
                TextAnchor.MiddleRight);
        }

        var phases = new[] { MoveItUpdatePhase.Update, MoveItUpdatePhase.FixedUpdate, MoveItUpdatePhase.LateUpdate };
        for (var i = 0; i < phases.Length; i++)
        {
            var origin = new Vector3(-3f + i * 2.2f, -1.5f, 0f);
            var sphere = stage.Spawn(PrimitiveType.Sphere, origin, 0.3f + i * 0.2f,
                phases[i].ToString());
            sphere.localScale = Vector3.one * 0.6f;
            sphere.MoveYTo(-0.3f, SineInOut.Over(0.9f))
                .On(phases[i]).Loop();
            stage.Label(new Vector3(origin.x, -2.35f, 0f), phases[i].ToString(), 0.16f);
        }

        stage.Say("two clocks and all three update phases running together");
    }

    public override void Tick(MoveItStage stage) =>
        Time.timeScale = Mathf.Max(0f, Mathf.Sin(stage.Elapsed * 0.9f) * 0.75f + 0.75f);

    public override void Exit() => Time.timeScale = restoreTimeScale;
}

/// <summary>Group time scales and bulk control through labels, which is how a whole layer is governed at once.</summary>
[Serializable]
public sealed class GroupAndLabelScenario : MoveItPlaygroundScenario
{
    private const string slowGroup = "Playground.Slow";
    private const string cullLabel = "playground.cull";
    private float nextCull;

    public override string Title => "Groups and labels";

    public override string Watch =>
        "The lower row sits in a group whose scale oscillates; the labeled row is culled and rebuilt in bulk.";

    public override void Enter(MoveItStage stage)
    {
        for (var i = 0; i < 6; i++)
        {
            var origin = new Vector3(i * 1.2f - 3f, 1.5f, 0f);
            var cube = stage.Spawn(PrimitiveType.Cube, origin, new Color(0.6f, 0.8f, 1f), $"Grouped{i}");
            cube.localScale = Vector3.one * 0.5f;
            cube.MoveYTo(origin.y + 1.2f, SineInOut.Over(1f))
                .InGroup(slowGroup).Loop();
        }

        Rebuild(stage);
        nextCull = 2f;
        stage.Say("group scale drives one row; the other answers to a label");
    }

    public override void Tick(MoveItStage stage)
    {
        MoveItGroups.SetScale(slowGroup, Mathf.Abs(Mathf.Sin(stage.Elapsed * 0.5f)) * 1.8f + 0.05f);

        if (stage.Elapsed < nextCull) return;
        nextCull = stage.Elapsed + 2f;
        var stopped = MoveIt.StopAll(label: cullLabel);
        stage.Check(MoveIt.Count(label: cullLabel) == 0, "bulk stop by label leaves nothing behind");
        stage.Say($"stopped {stopped} labeled motions in one call");
        Rebuild(stage);
    }

    private static void Rebuild(MoveItStage stage)
    {
        for (var i = 0; i < 6; i++)
        {
            var origin = new Vector3(i * 1.2f - 3f, -1.5f, 0f);
            var sphere = stage.Spawn(PrimitiveType.Sphere, origin, new Color(1f, 0.7f, 0.5f), $"Labeled{i}");
            sphere.localScale = Vector3.one * 0.5f;
            sphere.MoveYTo(origin.y - 1.2f, QuadraticInOut.Over(1.4f))
                .Named(cullLabel).Loop();
        }
    }

    public override void Exit() => MoveItGroups.SetScale(slowGroup, 1f);
}

/// <summary>Two motions claiming one channel of one transform, so priority has to pick a winner every frame.</summary>
[Serializable]
public sealed class PriorityDuelScenario : MoveItPlaygroundScenario
{
    private Transform contested;
    private float nextChallenger;
    private int rounds;

    public override string Title => "Priority duel";

    public override string Watch =>
        "Two independent motions drive the same axis; the newer graph wins and the loser never corrupts it.";

    public override void Enter(MoveItStage stage)
    {
        contested = stage.Spawn(PrimitiveType.Cube, Vector3.zero, new Color(1f, 0.35f, 0.6f), "Contested");
        stage.Label(new Vector3(0f, 1.6f, 0f), "one axis, two Replace claims — the newest wins", 0.2f);
        contested.MoveXTo(-3f, SineInOut.Over(2f)).Loop();
        nextChallenger = 1.5f;
        stage.Say("incumbent claims position.x forever");
    }

    public override void Tick(MoveItStage stage)
    {
        if (contested == null || stage.Elapsed < nextChallenger) return;
        nextChallenger = stage.Elapsed + 1.5f;
        rounds++;
        contested.MoveXTo(3f, BackOut.Over(0.8f));
        stage.Check(MoveIt.Count(contested) >= 2, "both claims coexist on the contested channel");
        stage.Say($"round {rounds}: a challenger joined the same channel");
    }
}

/// <summary>
/// Composition modes on every batched sink: an Add bob riding a Replace base, a Multiply pulse on scale,
/// and an additive color flash on a property block — with the Add claim removed live to prove the base
/// comes back untouched.
/// </summary>
[Serializable]
public sealed class CompositionScenario : MoveItPlaygroundScenario
{
    private Transform reference;
    private Transform composed;
    private MoveIt bob;
    private float phaseStart;
    private bool comparing;

    public override string Title => "Composition modes";

    public override string Watch =>
        "The two left cubes share one base motion; the second also carries an Add bob that comes and goes. " +
        "Right: a Multiply pulse on scale and an additive color flash.";

    public override void Enter(MoveItStage stage)
    {
        reference = stage.Spawn(PrimitiveType.Cube, new Vector3(-4.2f, 0f, 0f),
            new Color(0.45f, 0.7f, 1f), "ReplaceOnly");
        composed = stage.Spawn(PrimitiveType.Cube, new Vector3(-2.4f, 0f, 0f),
            new Color(0.45f, 1f, 0.7f), "BasePlusAdd");
        stage.Label(new Vector3(-4.2f, -0.9f, 0f), "Replace only", 0.17f);
        stage.Label(new Vector3(-2.4f, -0.9f, 0f), "base + Add bob", 0.17f);

        reference.MoveYTo(2f, SineInOut.Over(1.8f)).Loop();
        composed.MoveYTo(2f, SineInOut.Over(1.8f)).Loop();
        StartBob(stage);
        phaseStart = 0f;

        var pulse = stage.Spawn(PrimitiveType.Sphere, new Vector3(0.6f, 0.6f, 0f),
            new Color(1f, 0.75f, 0.35f), "MultiplyPulse");
        stage.Label(new Vector3(0.6f, -0.9f, 0f), "scale × Multiply", 0.17f);
        pulse.ScaleTo(1.7f, QuadraticInOut.Over(1.6f)).Loop();
        stage.CheckSurvives(() => pulse.ScaleTo(1.3f, SineInOut.Over(0.35f)).Loop()
                .Compose(MoveItComposition.Multiply),
            "multiplying a scale on top of a Replace base");

        var glow = stage.Spawn(PrimitiveType.Sphere, new Vector3(3f, 0.6f, 0f), Color.black, "AddGlow");
        stage.Label(new Vector3(3f, -0.9f, 0f), "color + Add flash", 0.17f);
        var renderer = glow.GetComponent<Renderer>();
        renderer.ColorTo(new Color(0.15f, 0.35f, 0.9f), QuadraticInOut.Over(2.4f)).Loop();
        stage.CheckSurvives(() => renderer.ColorTo(new Color(0.55f, 0.35f, 0.05f), SineInOut.Over(0.5f))
                .Loop().Compose(MoveItComposition.Add),
            "adding a color flash on top of a property-block base");

        stage.Say("relative claims stack instead of fighting for the channel");
    }

    private void StartBob(MoveItStage stage)
    {
        stage.CheckSurvives(() => bob = composed.MoveYTo(0.45f, SineInOut.Over(0.27f)).Loop()
                .Compose(MoveItComposition.Add),
            "adding a position bob on top of a Replace base");
        stage.Check(MoveIt.Count(composed) == 2, "the base and the Add bob coexist on one channel");
    }

    public override void Tick(MoveItStage stage)
    {
        if (reference == null || composed == null) return;
        var phase = stage.Elapsed - phaseStart;

        if (!comparing && phase >= 4f)
        {
            comparing = true;
            bob.Stop();
            stage.Say("Add bob removed — the composed cube must fall back to the base");
        }

        if (comparing && phase >= 4.5f)
            stage.Check(Mathf.Abs(composed.position.y - reference.position.y) < 0.05f,
                "removing the Add claim returns the target to the pure base value");

        if (phase < 6f) return;
        phaseStart = stage.Elapsed;
        comparing = false;
        StartBob(stage);
        stage.Say("Add bob re-attached");
    }

    public override void Exit()
    {
        reference = null;
        composed = null;
        bob = default;
    }
}

/// <summary>
/// Endless motions living inside sequences: a finite timeline that holds forever while its endless child
/// keeps looping, against a looping timeline that cuts and restarts its endless child every pass.
/// </summary>
[Serializable]
public sealed class EndlessChildrenScenario : MoveItPlaygroundScenario
{
    private MoveItSequence coda;
    private MoveItSequence cutter;
    private Transform slider;
    private Transform spinner;
    private Transform dropper;
    private Quaternion previousSpin;
    private float spunInCoda;
    private float phaseStart;
    private bool completed;

    public override string Title => "Endless children";

    public override string Watch =>
        "Top: the intro ends, the timeline holds, the spinner never stops — until Complete lands it. " +
        "Bottom: a looping timeline restarts its endless child every pass.";

    public override void Enter(MoveItStage stage)
    {
        slider = stage.Spawn(PrimitiveType.Cube, new Vector3(-4.5f, 1.3f, 0f),
            new Color(1f, 0.55f, 0.4f), "Intro");
        spinner = stage.Spawn(PrimitiveType.Cube, new Vector3(0f, 1.3f, 0f),
            new Color(0.4f, 0.9f, 1f), "EndlessSpin");
        dropper = stage.Spawn(PrimitiveType.Sphere, new Vector3(3f, 0.2f, 0f),
            new Color(0.7f, 1f, 0.5f), "Outro");
        stage.Label(new Vector3(-4.5f, 0.4f, 0f), "intro", 0.16f);
        stage.Label(new Vector3(0f, 0.4f, 0f), "endless spinner", 0.16f);
        stage.Label(new Vector3(3f, -0.6f, 0f), "outro", 0.16f);

        var bobHost = stage.Spawn(PrimitiveType.Cube, new Vector3(-4.5f, -2f, 0f),
            new Color(1f, 0.85f, 0.4f), "LoopIntro");
        var bobber = stage.Spawn(PrimitiveType.Sphere, new Vector3(0f, -2f, 0f),
            new Color(1f, 0.5f, 0.8f), "CutBobber");
        stage.Label(new Vector3(3.4f, -2f, 0f), "looping timeline\nrestarts its endless child", 0.15f);

        Build(stage);
        cutter = MoveItSequence.Create()
            .Chain(bobHost.MoveXTo(-2.5f, QuadraticInOut.Over(1.2f)))
            .Group(bobber.MoveYTo(-1.2f, SineInOut.Over(0.3f)).Loop())
            .Loop(MotionCycle.Restart);
    }

    private void Build(MoveItStage stage)
    {
        slider.position = new Vector3(-4.5f, 1.3f, 0f);
        spinner.localRotation = Quaternion.identity;
        dropper.position = new Vector3(3f, 0.2f, 0f);
        phaseStart = stage.Elapsed;
        completed = false;
        spunInCoda = 0f;
        previousSpin = spinner.localRotation;

        stage.CheckSurvives(() => coda = MoveItSequence.Create()
                .Chain(slider.MoveXTo(-2.5f, BackOut.Over(0.8f)))
                .Chain(spinner.RotateLocalTo(new Vector3(0f, 0f, 360f), 0.9f).Loop(Incremental))
                .Chain(dropper.MoveYTo(2.2f, BounceOut.Over(0.7f))),
            "adopting a motion that never ends");
        stage.Check(!float.IsInfinity(coda.ContentDuration),
            "an endless child occupies one cycle of content, not infinity");
        stage.Check(float.IsInfinity(coda.Duration), "a timeline holding an endless child never ends on its own");
        stage.Say($"content {coda.ContentDuration:0.00}s, duration infinite");
    }

    public override void Tick(MoveItStage stage)
    {
        if (coda == null || spinner == null) return;
        var phase = stage.Elapsed - phaseStart;
        var content = coda.ContentDuration;

        if (coda.IsAlive && phase > content + 0.5f && phase < 8f)
        {
            spunInCoda += Quaternion.Angle(previousSpin, spinner.localRotation);
            stage.Check(coda.CyclesDone == 0, "the held timeline never reports a finished cycle");
            stage.Check(Mathf.Abs(coda.Playhead - content) < 0.02f, "the playhead holds at the content end");
        }
        previousSpin = spinner.localRotation;

        if (!completed && phase >= 8f)
        {
            completed = true;
            stage.Check(spunInCoda > 90f, "the endless child keeps spinning after the content ends");
            stage.CheckSurvives(() => coda.Complete(), "completing a timeline that never ends on its own");
        }

        if (completed && phase >= 8.5f)
        {
            stage.Check(!coda.IsAlive, "Complete lands and ends the held timeline");
            stage.Say("completed at the content-end pose; rebuilding");
            Build(stage);
        }

        if (cutter != null && stage.Elapsed > 3f)
            stage.Check(cutter.CyclesDone >= 1, "a looping timeline keeps cycling despite its endless child");
    }

    public override void Exit()
    {
        coda = null;
        cutter = null;
        slider = null;
        spinner = null;
        dropper = null;
    }
}
