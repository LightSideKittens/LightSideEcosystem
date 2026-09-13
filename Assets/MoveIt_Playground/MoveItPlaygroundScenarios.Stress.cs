using System;
using System.Collections.Generic;
using LightSide;
using UnityEngine;
using static LightSide.EasingType;
using static LightSide.MotionCycle;
using Random = UnityEngine.Random;

/// <summary>Targets destroyed while motions are still driving them, by both destruction paths.</summary>
[Serializable]
public sealed class DestroyUnderFootScenario : MoveItPlaygroundScenario
{
    private readonly List<Transform> living = new();
    private float nextWave;
    private int destroyed;
    private int peakLive;

    public override string Title => "Destroy under foot";

    public override bool IsStress => true;

    public override string Watch =>
        "Objects are destroyed mid-flight, half deferred and half immediate; the live count must keep returning.";

    public override void Enter(MoveItStage stage)
    {
        nextWave = 0f;
        destroyed = 0;
        peakLive = 0;
        living.Clear();
    }

    public override void Tick(MoveItStage stage)
    {
        peakLive = Mathf.Max(peakLive, MoveIt.Count());

        if (stage.Elapsed >= nextWave)
        {
            nextWave = stage.Elapsed + 0.25f;
            for (var i = 0; i < 12; i++)
            {
                var origin = new Vector3(Random.Range(-5f, 5f), Random.Range(-2f, 3f), 0f);
                var cube = stage.Spawn(PrimitiveType.Cube, origin, Random.value,
                    "Doomed");
                cube.localScale = Vector3.one * 0.4f;
                cube.MoveYTo(origin.y + 2f, SineInOut.Over(3f)).Loop();
                cube.RotateLocalTo(new Vector3(0f, 360f, 0f), 2f).Loop(Incremental);
                living.Add(cube);
            }
        }

        for (var i = living.Count - 1; i >= 0; i--)
        {
            if (living[i] == null)
            {
                living.RemoveAt(i);
                continue;
            }

            if (living.Count <= 24 || Random.value > 0.25f) continue;
            var immediate = (destroyed & 1) == 0;
            var victim = living[i];
            living.RemoveAt(i);
            stage.CheckSurvives(() => stage.Despawn(victim, immediate), "destroying a target under a live motion");
            destroyed++;
        }

        stage.Check(MoveIt.Count() < 4000, $"live motions stay bounded (peak {peakLive})");
    }

    public override void Exit() => living.Clear();
}

/// <summary>User callbacks that re-enter the engine: stopping, completing, restarting and creating from inside.</summary>
[Serializable]
public sealed class ReentrancyStormScenario : MoveItPlaygroundScenario
{
    private Transform host;
    private float nextReport;
    private int created;
    private int stopped;
    private int completed;
    private int restarted;
    private int reentered;
    private MoveItStage stage;

    public override string Title => "Reentrancy storm";

    public override bool IsStress => true;

    public override string Watch =>
        "Every callback calls back into the engine — Stop, Complete, Restart and fresh creations from inside Update.";

    public override void Enter(MoveItStage stage)
    {
        host = stage.Spawn(PrimitiveType.Sphere, Vector3.zero, new Color(1f, 0.5f, 0.9f), "Reentrant");
        created = stopped = completed = restarted = reentered = 0;
        this.stage = stage;
        nextReport = 1f;
        Seed(stage);
    }

    public override void Tick(MoveItStage stage)
    {
        if (host == null) return;
        if (MoveIt.Count(host) < 6) Seed(stage);
        stage.Check(MoveIt.Count(host) < 512, "reentrant creation does not run away");

        if (stage.Elapsed < nextReport) return;
        nextReport = stage.Elapsed + 1f;
        stage.Say($"from inside callbacks: {stopped} stopped, {completed} completed, " +
                  $"{restarted} finished, {created} created, {reentered} re-entered while ending");
    }

    private void Seed(MoveItStage stage)
    {
        var self = this;

        stage.CheckSurvives(() =>
                host.MoveYTo(Random.Range(-2f, 2f), QuadraticInOut.Over(0.6f))
                    .OnUpdate(self, static (s, motion) =>
                    {
                        if (motion.Progress < 0.5f) return;
                        s.stopped++;
                        s.Reenter(ref motion, stop: true);
                    }),
            "stopping a motion from inside its own update");

        stage.CheckSurvives(() =>
                host.ScaleTo(Random.Range(0.6f, 1.6f), 0.7f)
                    .OnUpdate(self, static (s, motion) =>
                    {
                        if (motion.Progress < 0.8f) return;
                        s.completed++;
                        s.Reenter(ref motion, stop: false);
                    }),
            "completing a motion from inside its own update");

        stage.CheckSurvives(() =>
                host.RotateLocalTo(new Vector3(0f, 180f, 0f), 0.5f)
                    .OnComplete(self, static s =>
                    {
                        s.restarted++;
                        if (s.host == null) return;
                        s.created++;
                        s.host.MoveXTo(Random.Range(-2f, 2f), 0.4f);
                    }),
            "creating a motion from inside a completion callback");
    }

    /// <summary>
    /// Ends the motion from inside its own callback, on purpose, more than once: completing a motion jumps it to
    /// its end, which publishes one more update, so this callback runs again while the motion it just ended is
    /// still terminating. Ending something already ending is the outcome the caller asked for, so it must be
    /// accepted in silence — an exception here would mean the idempotent contract broke.
    /// </summary>
    private void Reenter(ref MoveIt motion, bool stop)
    {
        if (motion.IsEnding) reentered++;
        var ending = motion;
        stage.CheckSurvives(() =>
        {
            if (stop) ending.Stop();
            else ending.Complete();
        }, "ending a motion that is already ending");
    }

    public override void Exit()
    {
        host = null;
        stage = null;
    }
}

/// <summary>A sequence attacked from inside its own children, which is where ownership is easiest to break.</summary>
[Serializable]
public sealed class SequenceSelfSabotageScenario : MoveItPlaygroundScenario
{
    private MoveItSequence sequence;
    private Transform a;
    private Transform b;
    private int generation;
    private int sabotages;

    public override string Title => "Sequence self-sabotage";

    public override bool IsStress => true;

    public override string Watch =>
        "A child's callback stops, completes or restarts the sequence that owns it, mid-placement.";

    public override void Enter(MoveItStage stage)
    {
        a = stage.Spawn(PrimitiveType.Cube, new Vector3(-2f, 0f, 0f), new Color(1f, 0.3f, 0.3f), "SabotageA");
        b = stage.Spawn(PrimitiveType.Cube, new Vector3(2f, 0f, 0f), new Color(0.3f, 0.6f, 1f), "SabotageB");
        Build(stage);
    }

    public override void Tick(MoveItStage stage)
    {
        if (sequence != null && sequence.IsAlive) return;
        Build(stage);
    }

    private void Build(MoveItStage stage)
    {
        if (a == null || b == null) return;
        generation++;
        var self = this;
        var mode = generation % 3;

        stage.CheckSurvives(() =>
        {
            sequence = MoveItSequence.Create()
                .Chain(a.MoveYTo(2f, 0.5f)
                    .OnUpdate(self, static (s, motion) =>
                    {
                        if (motion.Progress < 0.6f || s.sequence == null || !s.sequence.IsAlive ||
                            s.sequence.IsEnding) return;
                        s.sabotages++;
                        var target = s.sequence;
                        switch (s.generation % 3)
                        {
                            case 0:
                                target.Stop();
                                break;
                            case 1:
                                target.Complete();
                                break;
                            default:
                                target.Restart();
                                break;
                        }
                    }))
                .Chain(b.MoveYTo(2f, 0.5f))
                .ChainCallback(self, static s => s.sabotages++);
        }, $"assembling a sequence whose child sabotages it (mode {mode})");

        stage.Check(sequence != null, "sequence survived assembly under sabotage");
        if (generation % 10 == 1)
            stage.Say($"generation {generation}, {sabotages} sabotages absorbed");
    }

    public override void Exit() => sequence = null;
}

/// <summary>Callbacks that throw, because a consumer's exception must not become the engine's problem.</summary>
[Serializable]
public sealed class ThrowingCallbackScenario : MoveItPlaygroundScenario
{
    private Transform victim;
    private int thrown;
    private float nextRound;

    public override string Title => "Throwing callbacks";

    public override bool IsStress => true;

    public override string Watch =>
        "Every third callback throws. Expect red console entries — what matters is that motion keeps running.";

    public override void Enter(MoveItStage stage)
    {
        victim = stage.Spawn(PrimitiveType.Capsule, Vector3.zero, new Color(1f, 0.9f, 0.3f), "Throwing");
        nextRound = 0f;
        thrown = 0;
    }

    public override void Tick(MoveItStage stage)
    {
        if (victim == null || stage.Elapsed < nextRound) return;
        nextRound = stage.Elapsed + 0.6f;
        var self = this;

        stage.CheckSurvives(() =>
                victim.MoveYTo(Random.Range(-1.5f, 1.5f), 0.5f)
                    .OnStart(self, static s => s.MaybeThrow("start"))
                    .OnUpdate(self, static (s, _) => s.MaybeThrow("update"))
                    .OnComplete(self, static s => s.MaybeThrow("complete")),
            "registering callbacks that will throw");

        stage.Check(MoveIt.Count(victim) > 0 || stage.Elapsed < 0.1f,
            "a throwing callback does not stop the engine from accepting new motions");
    }

    private void MaybeThrow(string phase)
    {
        thrown++;
        if (thrown % 3 != 0) return;
        throw new InvalidOperationException($"Deliberate playground failure from the {phase} callback.");
    }

    public override void Exit() => victim = null;
}

/// <summary>Creation and destruction at a rate that crosses every internal capacity boundary repeatedly.</summary>
[Serializable]
public sealed class ChurnBurstScenario : MoveItPlaygroundScenario
{
    private readonly List<MoveIt> handles = new();
    private Transform anchor;
    private float value;
    private int peak;

    public override string Title => "Churn burst";

    public override bool IsStress => true;

    public override string Watch =>
        "Hundreds of motions are created and killed every frame across three sink kinds; watch the live count.";

    public override void Enter(MoveItStage stage)
    {
        anchor = stage.Spawn(PrimitiveType.Cube, Vector3.zero, new Color(0.5f, 1f, 0.8f), "ChurnAnchor");
        anchor.localScale = Vector3.one * 0.5f;
        handles.Clear();
        peak = 0;
    }

    public override void Tick(MoveItStage stage)
    {
        if (anchor == null) return;
        var self = this;
        var renderer = anchor.GetComponent<Renderer>();

        stage.CheckSurvives(() =>
        {
            for (var i = 0; i < 80; i++)
            {
                handles.Add(anchor.MoveXTo(Random.Range(-1f, 1f), 0.4f));
                handles.Add(MoveIt.Value(0f, 1f, 0.3f)
                    .OnUpdate(self, static (s, motion) => s.value = motion.Value<float>()));
                if (renderer != null)
                    handles.Add(renderer.ColorTo(Color.HSVToRGB(Random.value, 0.6f, 1f), 0.35f));
            }
        }, "creating a burst of motions across every sink kind");

        peak = Mathf.Max(peak, MoveIt.Count());

        for (var i = handles.Count - 1; i >= 0; i--)
        {
            if (Random.value > 0.55f) continue;
            var handle = handles[i];
            handles.RemoveAt(i);
            stage.CheckSurvives(() =>
            {
                if (Random.value < 0.5f) handle.Stop();
                else handle.Complete();
            }, "killing a motion out of the middle of the churn");
        }

        if (handles.Count > 4000) handles.RemoveRange(0, handles.Count - 4000);
        stage.Check(MoveIt.Count() < 20000, $"the engine keeps the live set bounded (peak {peak})");
    }

    public override void Exit() => handles.Clear();
}

/// <summary>Every control call aimed at handles that are dead, defaulted, or already finished.</summary>
[Serializable]
public sealed class DeadHandleScenario : MoveItPlaygroundScenario
{
    private float nextRound;
    private int rounds;

    public override string Title => "Dead handle abuse";

    public override bool IsStress => true;

    public override string Watch =>
        "Every method is called on stopped, completed and never-created handles. All of it must be a quiet no-op.";

    public override void Enter(MoveItStage stage)
    {
        nextRound = 0f;
        rounds = 0;
        stage.CheckSurvives(() => Abuse(default), "the full control surface on a handle that was never created");
    }

    public override void Tick(MoveItStage stage)
    {
        if (stage.Elapsed < nextRound) return;
        nextRound = stage.Elapsed + 0.5f;
        rounds++;

        stage.CheckSurvives(() => Abuse(default), "the full control surface on a handle that was never created");

        var stoppedHandle = MoveIt.Value(0f, 1f, 1f);
        stoppedHandle.Stop();
        stage.Probe(() => Abuse(stoppedHandle), "controlling a stopped motion");

        var completedHandle = MoveIt.Value(0f, 1f, 1f);
        completedHandle.Complete();
        stage.Probe(() => Abuse(completedHandle), "controlling a completed motion");

        var doubleKilled = MoveIt.Value(0f, 1f, 1f);
        doubleKilled.Stop();
        stage.CheckSurvives(() => doubleKilled.Stop(), "stopping a motion twice");
        stage.CheckSurvives(() => doubleKilled.Complete(), "completing an already stopped motion");
        stage.Check(!doubleKilled.IsAlive, "a stopped motion never reports itself alive again");

        if (rounds % 8 == 1) stage.Say($"round {rounds}: dead handles absorbed every control call");
    }

    /// <summary>Touches every public member of the handle, so no control path escapes the sweep.</summary>
    private static void Abuse(MoveIt handle)
    {
        _ = handle.IsAlive;
        _ = handle.Id;
        _ = handle.Progress;
        _ = handle.Elapsed;
        _ = handle.Duration;
        _ = handle.Factor;
        _ = handle.CyclesDone;
        _ = handle.IsBackward;
        _ = handle.IsPaused;
        _ = handle.TimeScale;
        _ = handle.Value<float>();
        _ = handle.ToString();
        handle.Pause();
        handle.Resume();
        handle.PlayForward();
        handle.PlayBackward();
        handle.Reverse();
        handle.Restart();
        handle.Seek(0.5f);
        handle.Advance(0.1f);
        handle.RetargetTo(1f);
        handle.Shape(QuadraticOut);
        handle.Cycles(3);
        handle.Loop();
        handle.Delay(0.1f);
        handle.On(PlaybackClock.Unscaled);
        handle.On(MoveItUpdatePhase.LateUpdate);
        handle.InGroup("playground.dead");
        handle.Essential();
        handle.Named("playground.dead");
        handle.OnStart(static () => { });
        handle.OnUpdate(static _ => { });
        handle.OnCycle(static _ => { });
        handle.OnComplete(static () => { });
        handle.OnStop(static () => { });
        handle.Stop();
        handle.Complete();
    }
}

/// <summary>Operations the engine is documented to refuse, checked for the refusal rather than the effect.</summary>
[Serializable]
public sealed class IllegalOperationScenario : MoveItPlaygroundScenario
{
    private Transform target;
    private float nextRound;
    private int rounds;

    public override string Title => "Refused operations";

    public override bool IsStress => true;

    public override string Watch =>
        "Each illegal call must throw its documented exception — a silent acceptance counts as a failure.";

    public override void Enter(MoveItStage stage)
    {
        target = stage.Spawn(PrimitiveType.Cube, Vector3.zero, new Color(0.9f, 0.4f, 0.3f), "Refused");
        nextRound = 0f;
        rounds = 0;
    }

    public override void Tick(MoveItStage stage)
    {
        if (target == null || stage.Elapsed < nextRound) return;
        nextRound = stage.Elapsed + 1f;
        rounds++;

        stage.CheckThrows<ArgumentNullException>(
            static () => ((Transform)null).MoveTo(Vector3.zero, 1f),
            "moving a null transform");

        var endlessZero = MoveItTiming.Of(0f);
        endlessZero.Cycles = MotionCycles.Infinite;
        stage.CheckThrows<ArgumentException>(
            () => MoveIt.Value(0f, 1f, endlessZero),
            "creating a zero-duration motion that repeats without end");

        var owned = MoveIt.Value(0f, 1f, 0.5f);
        var first = MoveItSequence.Create().Chain(owned);
        var rejected = MoveItSequence.Create();
        stage.CheckThrows<ArgumentException>(
            () => rejected.Chain(owned),
            "adopting one motion into a second sequence");

        var spin = target.RotateLocalTo(new Vector3(0f, 180f, 0f), 0.5f);
        stage.CheckThrows<ArgumentException>(
            () => spin.Compose(MoveItComposition.Multiply),
            "multiplying a rotation, which has no multiplication");
        spin.Stop();

        var callbackValue = MoveIt.Value(0f, 1f, 0.5f);
        stage.CheckThrows<InvalidOperationException>(
            () => callbackValue.Compose(MoveItComposition.Add),
            "composing a callback value, which owns no shared value");
        stage.CheckThrows<ArgumentOutOfRangeException>(
            () => callbackValue.Compose((MoveItComposition)9),
            "an undefined composition mode");
        callbackValue.Stop();

        var nested = MoveItSequence.Create().Chain(MoveIt.Value(0f, 1f, 0.4f));
        var parent = MoveItSequence.Create().Chain(nested);
        stage.CheckThrows<InvalidOperationException>(
            () => nested.Stop(),
            "controlling a sequence that another sequence owns");

        first.Stop();
        parent.Stop();
        rejected.Stop();
        stage.Say($"round {rounds}: every documented refusal held");
    }

    public override void Exit() => target = null;
}

/// <summary>Reduced-motion preference flipped underneath running motions.</summary>
[Serializable]
public sealed class ReducedMotionScenario : MoveItPlaygroundScenario
{
    private bool restore;
    private float nextFlip;

    public override string Title => "Reduced motion toggle";

    public override bool IsStress => true;

    public override string Watch =>
        "The accessibility preference flips every 1.5s; essential motions keep running, the rest settle.";

    public override void Enter(MoveItStage stage)
    {
        restore = Accessibility.PrefersReducedMotion;
        nextFlip = 1.5f;

        for (var i = 0; i < 5; i++)
        {
            var origin = new Vector3(i * 1.5f - 3f, 1.5f, 0f);
            var cube = stage.Spawn(PrimitiveType.Cube, origin, new Color(0.8f, 0.8f, 1f), $"Ordinary{i}");
            cube.localScale = Vector3.one * 0.6f;
            cube.MoveYTo(origin.y - 3f, SineInOut.Over(1.2f)).Loop();
        }
        stage.Label(new Vector3(-4.2f, 1.5f, 0f), "ordinary — settles", 0.18f, TextAnchor.MiddleRight);

        for (var i = 0; i < 5; i++)
        {
            var origin = new Vector3(i * 1.5f - 3f, -2.5f, 0f);
            var sphere = stage.Spawn(PrimitiveType.Sphere, origin, new Color(1f, 0.6f, 0.6f), $"Essential{i}");
            sphere.localScale = Vector3.one * 0.6f;
            sphere.MoveYTo(origin.y + 1.5f, SineInOut.Over(1.2f))
                .Essential().Loop();
        }
        stage.Label(new Vector3(-4.2f, -2.5f, 0f), "Essential — keeps playing", 0.18f, TextAnchor.MiddleRight);

        stage.Say("top row is ordinary, bottom row is marked essential");
    }

    public override void Tick(MoveItStage stage)
    {
        if (stage.Elapsed < nextFlip) return;
        nextFlip = stage.Elapsed + 1.5f;
        stage.CheckSurvives(() => Accessibility.PrefersReducedMotion = !Accessibility.PrefersReducedMotion,
            "flipping the reduced-motion preference under live motions");
        stage.Say($"reduced motion is now {Accessibility.PrefersReducedMotion}");
    }

    public override void Exit() => Accessibility.PrefersReducedMotion = restore;
}
