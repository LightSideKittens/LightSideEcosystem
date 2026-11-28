using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightSide.Benchmark
{
    /// <summary>One configuration of a frame case, measured against the case's other arms in the same process.</summary>
    /// <remarks>Every arm of a case is built before its rounds start and is inactive between its slots.</remarks>
    public interface IBenchmarkArm
    {
        /// <summary>Name of the arm in the case record, unique within the case.</summary>
        string Id { get; }

        /// <summary>Profiler markers whose time in each measured frame the slot records; development players and the editor only.</summary>
        string[] Markers { get; }

        /// <summary>Shows the arm's objects and sets the global modes it runs under.</summary>
        void Activate();

        /// <summary>Hides the arm's objects; an inactive arm updates, rebuilds and draws nothing.</summary>
        void Deactivate();

        /// <summary>Advances the workload by one frame while the arm is active.</summary>
        /// <remarks>
        /// Runs inside measured windows and allocates nothing beyond the engine work it exercises. <paramref name="frame"/>
        /// counts up per arm over the whole case and never repeats.
        /// </remarks>
        void Step(int frame);

        /// <summary>Runs before the first measured frame of a slot.</summary>
        void SlotStarts();

        /// <summary>Runs after the last measured frame of a slot.</summary>
        /// <exception cref="InvalidOperationException">The workload did not run in the slot's measured frames.</exception>
        void SlotEnds(int measuredFrames);

        /// <summary>
        /// Writes the arm's configuration into <paramref name="arm"/>: <c>engine</c> (the measured implementation in
        /// lowercase: <c>unitext</c>, <c>tmp</c> or <c>uitk</c> for text; other workloads name theirs, e.g.
        /// <c>uniparticles</c>, <c>shuriken</c>, <c>unishapes</c>, <c>unigui</c>), <c>threads</c> (<c>parallel</c>, <c>single</c>, or null for an engine without the choice),
        /// <c>style</c> (<c>styled</c> or <c>plain</c>), <c>load</c> (the workload's size in the case's own unit) and
        /// <c>baseline</c> (true for a twin drawn with the default material, the GPU counter's control); optionally
        /// <c>variant</c> (a name that tells apart arms the other keys describe alike, e.g. <c>tmpTwoPass</c>) and
        /// <c>comparable</c> (false where the arm's output differs from the case's first arm, e.g. TMP on unshaped
        /// scripts; absent means true).
        /// </summary>
        /// <remarks>No two arms of one case may write the same descriptor.</remarks>
        void Describe(JObject arm);
    }

    /// <summary>One configuration of a one-shot case: an operation measured once per round, its state rebuilt between rounds.</summary>
    public interface IBenchmarkOneShotArm
    {
        /// <summary>Name of the arm in the case record, unique within the case.</summary>
        string Id { get; }

        /// <summary>Builds what <see cref="Operate"/> acts on; untimed.</summary>
        IEnumerator Prepare();

        /// <summary>The measured synchronous call; it runs inside a frame's update window.</summary>
        void Operate();

        /// <summary>Whether the engine has processed the result of <see cref="Operate"/>; read after every frame that follows it.</summary>
        bool Committed { get; }

        /// <summary>Returns to the state before <see cref="Prepare"/>; untimed.</summary>
        IEnumerator Reset();

        /// <summary>Writes the arm's configuration with the keys of <see cref="IBenchmarkArm.Describe"/>; called after the rounds.</summary>
        void Describe(JObject arm);
    }

    /// <summary>How the rounds of one case run.</summary>
    public sealed class BenchmarkRoundSettings
    {
        /// <summary>Seed of every round's slot order; <see cref="BenchmarkRounds.Seed"/> gives the stable one of a case.</summary>
        public int seed;

        /// <summary>Rounds per case; every round visits every arm once.</summary>
        public int rounds = 8;

        /// <summary>Stepped frames before a frame slot's measured frames.</summary>
        public int settleFrames = 30;

        /// <summary>Measured frames of a frame slot.</summary>
        public int measuredFrames = 60;

        /// <summary>Upper bound of an arm's warm-up before the first round; an arm still drifting after it is recorded as unsettled.</summary>
        public int maxWarmupFrames = 180;

        /// <summary>Frames a one-shot operation may take to commit before its slot fails.</summary>
        public int maxCommitFrames = 30;

        /// <summary>Whether frame slots keep every frame's sample beside the summaries.</summary>
        public bool includeSamples;

        /// <summary>
        /// Whether round 0 of a one-shot case is its result, an operation a process pays once (the first screen, the
        /// first rasterization of a font), rather than the warm-up of a repeated operation.
        /// </summary>
        public bool coldResult;

        internal JObject Describe() => new()
        {
            ["rounds"] = rounds,
            ["settleFrames"] = settleFrames,
            ["measuredFrames"] = measuredFrames,
            ["maxWarmupFrames"] = maxWarmupFrames,
            ["maxCommitFrames"] = maxCommitFrames
        };
    }

    /// <summary>
    /// Runs the arms of one case in rounds inside one process: every round visits every arm once, in an order drawn from
    /// the case's seed, so drift, heat and governor phases fall on all arms alike instead of on whichever ran later.
    /// </summary>
    /// <remarks>
    /// A frame case gains the arm <see cref="ReferenceArmId"/>, in whose slots no case arm is active: the in-launch floor of
    /// camera, canvas, harness and clock hold. With <c>-benchmarkGates</c> it also gains <c>&lt;first arm&gt;.aa</c>, the
    /// first arm again on the same objects, and <c>&lt;first arm&gt;.control</c>, the same plus fixed work per frame sized
    /// after the first arm's warm-up to <see cref="ControlFraction"/> of its own main-thread work (at least
    /// <see cref="ControlFloorMilliseconds"/>) at the calibrated clock: the run's own A/A and positive control. Round 0 of
    /// a frame case is its warm-up round.
    /// After the rounds the record carries <c>armInfo</c>: each arm's <see cref="IBenchmarkArm.Describe"/> plus
    /// <c>control</c> (<c>reference</c>, <c>aa</c>, <c>positive</c> or null).
    /// </remarks>
    public static class BenchmarkRounds
    {
        /// <summary>Id of the arm whose slots run with every case arm inactive.</summary>
        public const string ReferenceArmId = "reference";

        /// <summary>Share of the first arm's warm-up main-thread work, hold excluded, that the positive-control arm adds per frame.</summary>
        public const double ControlFraction = 0.15;

        /// <summary>Least fixed work per frame the positive-control arm adds, in milliseconds at the calibrated clock.</summary>
        public const double ControlFloorMilliseconds = 0.3;

        const int ControlSizingFrames = 24;

        const int GateFrameOffset = 1 << 20;

        static readonly string[] DescriptorKeys = { "engine", "threads", "style", "load", "baseline" };

        /// <summary>
        /// The slot-order seed of <paramref name="caseId"/> in repeat <paramref name="repeat"/>: the same in every build and
        /// on every device; <c>-benchmarkSeed &lt;int&gt;</c> shifts it for the whole launch.
        /// </summary>
        public static int Seed(string caseId, int repeat)
        {
            var hash = 2166136261u;
            foreach (var character in caseId)
            {
                hash ^= character;
                hash *= 16777619u;
            }
            var shift = int.TryParse(BenchmarkLaunch.Value("-benchmarkSeed", "BENCHMARK_SEED"), out var value) ? value : 0;
            return unchecked((int)hash + repeat * 7919 + shift * 104729);
        }

        /// <summary>
        /// Warms every arm up, then runs <see cref="BenchmarkRoundSettings.rounds"/> rounds of frame slots and writes the
        /// case record into <paramref name="record"/>: kind, seed, settings, arms, warmup, thermal (one reading per round
        /// boundary) and rounds (order and, per arm, <c>cpuMs</c>, <c>cpuSource</c> and the sampler's <c>metrics</c>).
        /// </summary>
        /// <remarks>Every arm of <paramref name="caseArms"/> must be built and inactive; it is inactive again when this returns.</remarks>
        /// <exception cref="ArgumentException">The case has no arms, or two arms share an id.</exception>
        public static IEnumerator RunFrames(string caseId, IReadOnlyList<IBenchmarkArm> caseArms,
            BenchmarkRoundSettings settings, JObject record)
        {
            if (caseArms == null || caseArms.Count == 0)
                throw new ArgumentException($"Case '{caseId}' has no arms.", nameof(caseArms));
            BenchmarkFrameProbe.Install();
            var arms = new List<IBenchmarkArm>(caseArms.Count + 3) { new ReferenceArm() };
            arms.AddRange(caseArms);
            GateArm control = null;
            if (BenchmarkLaunch.Has("-benchmarkGates"))
            {
                arms.Add(new GateArm(caseArms[0], caseArms[0].Id + ".aa", "aa", GateFrameOffset));
                control = new GateArm(caseArms[0], caseArms[0].Id + ".control", "positive", 2 * GateFrameOffset);
                arms.Add(control);
            }
            record["kind"] = "frames";
            record["seed"] = settings.seed;
            record["settings"] = settings.Describe();
            record["arms"] = ArmIds(caseId, arms, arm => arm.Id);

            var frames = new int[arms.Count];
            var warmup = new JObject();
            record["warmup"] = warmup;
            for (var i = 1; i < arms.Count; i++)
            {
                var arm = arms[i];
                arm.Activate();
                var steady = new BenchmarkSteadyState();
                var signal = new MainThreadSignal();
                var stepped = 0;
                var observed = new List<float>(settings.maxWarmupFrames);
                while (!steady.Settled && stepped < settings.maxWarmupFrames)
                {
                    arm.Step(frames[i]++);
                    stepped++;
                    yield return null;
                    if (!signal.TryRead(out var milliseconds)) continue;
                    steady.Observe(milliseconds);
                    observed.Add(milliseconds);
                }
                if (i == 1 && control != null) SizeControl(caseId, control, observed, record);
                arm.Deactivate();
                warmup[arm.Id] = new JObject { ["frames"] = stepped, ["settled"] = steady.Settled, ["signal"] = signal.Name };
            }

            var random = new System.Random(settings.seed);
            var order = Identity(arms.Count);
            var thermal = new JArray { BenchmarkThermal.Read() };
            var rounds = new JArray();
            record["thermal"] = thermal;
            record["rounds"] = rounds;
            for (var round = 0; round < settings.rounds; round++)
            {
                Shuffle(order, random);
                var orderIds = new JArray();
                var slots = new JObject();
                foreach (var index in order)
                {
                    var arm = arms[index];
                    orderIds.Add(arm.Id);
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    arm.Activate();
                    for (var i = 0; i < settings.settleFrames; i++)
                    {
                        arm.Step(frames[index]++);
                        yield return null;
                    }
                    using (var sampler = new BenchmarkFrameSampler(settings.measuredFrames, arm.Markers))
                    {
                        arm.SlotStarts();
                        for (var i = 0; i < settings.measuredFrames; i++)
                        {
                            arm.Step(frames[index]++);
                            yield return null;
                            sampler.Sample();
                        }
                        arm.SlotEnds(settings.measuredFrames);
                        BenchmarkClock.ThrowOnFailure();
                        var cpu = sampler.CpuMilliseconds(out var source);
                        slots[arm.Id] = new JObject
                        {
                            ["cpuMs"] = cpu,
                            ["cpuSource"] = source,
                            ["metrics"] = sampler.Serialize(settings.includeSamples)
                        };
                    }
                    arm.Deactivate();
                }
                thermal.Add(BenchmarkThermal.Read());
                rounds.Add(new JObject { ["round"] = round, ["warmup"] = round == 0, ["order"] = orderIds, ["slots"] = slots });
            }
            var info = new JObject();
            foreach (var arm in arms)
            {
                var description = new JObject();
                switch (arm)
                {
                    case ReferenceArm:
                        description["control"] = "reference";
                        break;
                    case GateArm gate:
                        gate.Inner.Describe(description);
                        RequireDescriptor(caseId, arm.Id, description);
                        description["control"] = gate.Control;
                        break;
                    default:
                        arm.Describe(description);
                        RequireDescriptor(caseId, arm.Id, description);
                        description["control"] = JValue.CreateNull();
                        break;
                }
                info[arm.Id] = description;
            }
            RequireDistinct(caseId, info);
            record["armInfo"] = info;
        }

        /// <summary>
        /// Runs <see cref="BenchmarkRoundSettings.rounds"/> rounds of one-shot slots (<see cref="BenchmarkOneShot.Measure"/>)
        /// and writes the case record into <paramref name="record"/>: kind, coldResult, seed, settings, arms, thermal,
        /// rounds (order and, per arm, the slot) and armInfo. Round 0 holds each arm's first operation in the process.
        /// </summary>
        /// <remarks>
        /// With <c>-benchmarkGates</c> the case gains <c>&lt;first arm&gt;.aa</c>, the first arm's operation again, and
        /// <c>&lt;first arm&gt;.control</c>, the same with fixed work inside <see cref="IBenchmarkOneShotArm.Operate"/>: before
        /// each of its slots it is sized to <see cref="ControlFraction"/> of the median <c>workMs</c> of the first arm's slots
        /// from round 1 on so far (at least <see cref="ControlFloorMilliseconds"/>), and the slot records <c>controlMs</c>
        /// and <c>controlMsAtCalibration</c>. Gate arms skip round 0 when <see cref="BenchmarkRoundSettings.coldResult"/> is
        /// set, so the first arm's first operation stays the process's first.
        /// </remarks>
        /// <exception cref="ArgumentException">The case has no arms, or two arms share an id.</exception>
        public static IEnumerator RunOneShots(string caseId, IReadOnlyList<IBenchmarkOneShotArm> caseArms,
            BenchmarkRoundSettings settings, JObject record)
        {
            if (caseArms == null || caseArms.Count == 0)
                throw new ArgumentException($"Case '{caseId}' has no arms.", nameof(caseArms));
            BenchmarkFrameProbe.Install();
            var arms = new List<IBenchmarkOneShotArm>(caseArms);
            OneShotGateArm control = null;
            if (BenchmarkLaunch.Has("-benchmarkGates"))
            {
                arms.Add(new OneShotGateArm(caseArms[0], caseArms[0].Id + ".aa", "aa"));
                control = new OneShotGateArm(caseArms[0], caseArms[0].Id + ".control", "positive");
                arms.Add(control);
                record["controlFraction"] = ControlFraction;
            }
            record["kind"] = "oneShot";
            record["coldResult"] = settings.coldResult;
            record["seed"] = settings.seed;
            record["settings"] = settings.Describe();
            record["arms"] = ArmIds(caseId, arms, arm => arm.Id);

            var firstWork = new List<float>(settings.rounds);
            var random = new System.Random(settings.seed);
            var order = Identity(arms.Count);
            var thermal = new JArray { BenchmarkThermal.Read() };
            var rounds = new JArray();
            record["thermal"] = thermal;
            record["rounds"] = rounds;
            for (var round = 0; round < settings.rounds; round++)
            {
                Shuffle(order, random);
                var orderIds = new JArray();
                var slots = new JObject();
                foreach (var index in order)
                {
                    var arm = arms[index];
                    if (arm is OneShotGateArm && round == 0 && settings.coldResult) continue;
                    orderIds.Add(arm.Id);
                    if (arm == control) control.BusyIterations = BenchmarkClock.Iterations(OneShotControlTarget(firstWork));
                    yield return arm.Prepare();
                    var slot = new JObject();
                    yield return BenchmarkOneShot.Measure(arm, settings.maxCommitFrames, slot);
                    BenchmarkClock.ThrowOnFailure();
                    if (arm == control)
                    {
                        var calibrated = Math.Round(BenchmarkClock.Milliseconds(control.BusyIterations), 4);
                        slot["controlMs"] = Math.Round(OneShotControlTarget(firstWork), 4);
                        slot["controlMsAtCalibration"] = calibrated;
                        record["controlMsAtCalibration"] = calibrated;
                    }
                    if (index == 0 && round > 0) firstWork.Add((float)(double)slot["workMs"]);
                    slots[arm.Id] = slot;
                    yield return arm.Reset();
                }
                thermal.Add(BenchmarkThermal.Read());
                rounds.Add(new JObject { ["round"] = round, ["order"] = orderIds, ["slots"] = slots });
            }
            var info = new JObject();
            foreach (var arm in arms)
            {
                var description = new JObject();
                arm.Describe(description);
                RequireDescriptor(caseId, arm.Id, description);
                description["control"] = arm is OneShotGateArm gate ? new JValue(gate.Control) : JValue.CreateNull();
                info[arm.Id] = description;
            }
            RequireDistinct(caseId, info);
            record["armInfo"] = info;
        }

        static double OneShotControlTarget(List<float> firstWork)
        {
            if (firstWork.Count == 0) return ControlFloorMilliseconds;
            var sorted = new List<float>(firstWork);
            sorted.Sort();
            return Math.Max(ControlFloorMilliseconds, ControlFraction * BenchmarkStatistics.MedianSorted(sorted));
        }

        static void RequireDescriptor(string caseId, string armId, JObject description)
        {
            foreach (var key in DescriptorKeys)
                if (!description.ContainsKey(key))
                    throw new InvalidOperationException($"Arm '{armId}' of case '{caseId}' describes no '{key}'.");
        }

        /// <summary>Fails the case when two of its own arms write the same descriptor, which no reduction could tell apart.</summary>
        static void RequireDistinct(string caseId, JObject info)
        {
            var seen = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in info)
            {
                var description = (JObject)pair.Value;
                if (description["control"]?.Type != JTokenType.Null) continue;
                var key = string.Join("|", description["engine"], description["threads"], description["style"],
                    description["load"], description["baseline"], description["variant"]);
                if (seen.TryGetValue(key, out var other))
                    throw new InvalidOperationException($"Arms '{other}' and '{pair.Key}' of case '{caseId}' describe the same configuration; give one a variant.");
                seen.Add(key, pair.Key);
            }
        }

        /// <summary>Sizes the positive control from the first arm's warm-up: its median main-thread CPU of the last frames, clock hold excluded.</summary>
        /// <exception cref="InvalidOperationException">The warm-up delivered no main-thread CPU.</exception>
        static void SizeControl(string caseId, GateArm control, List<float> observed, JObject record)
        {
            if (observed.Count == 0)
                throw new InvalidOperationException($"Case '{caseId}': the first arm's warm-up delivered no main-thread CPU, so the positive control cannot be sized.");
            var tail = observed.GetRange(Math.Max(0, observed.Count - ControlSizingFrames), Math.Min(observed.Count, ControlSizingFrames));
            tail.Sort();
            var work = Math.Max(0d, BenchmarkStatistics.MedianSorted(tail) - BenchmarkClock.HoldMilliseconds);
            var target = Math.Max(ControlFloorMilliseconds, ControlFraction * work);
            control.BusyIterations = BenchmarkClock.Iterations(target);
            record["controlFraction"] = ControlFraction;
            record["controlMs"] = Math.Round(target, 4);
            record["controlMsAtCalibration"] = Math.Round(BenchmarkClock.Milliseconds(control.BusyIterations), 4);
        }

        static JArray ArmIds<T>(string caseId, IReadOnlyList<T> arms, Func<T, string> id)
        {
            var ids = new JArray();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var arm in arms)
            {
                var name = id(arm);
                if (!seen.Add(name))
                    throw new ArgumentException($"Case '{caseId}' has two arms named '{name}'.");
                ids.Add(name);
            }
            return ids;
        }

        static int[] Identity(int count)
        {
            var order = new int[count];
            for (var i = 0; i < count; i++) order[i] = i;
            return order;
        }

        static void Shuffle(int[] order, System.Random random)
        {
            for (var i = order.Length - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
        }

        /// <summary>Main-thread CPU of each new frame for the warm-up: FrameTimingManager's where it is on, else the frame window.</summary>
        sealed class MainThreadSignal
        {
            readonly FrameTiming[] latest = new FrameTiming[1];
            readonly bool frameTiming = FrameTimingManager.IsFeatureEnabled();
            ulong lastFrame;

            public string Name => frameTiming ? "cpuMainMs" : "frameMs";

            public bool TryRead(out float milliseconds)
            {
                milliseconds = 0f;
                if (!frameTiming)
                {
                    milliseconds = (float)BenchmarkFrameProbe.LastFrameMilliseconds;
                    return true;
                }
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, latest) == 0) return false;
                var timing = latest[0];
                if (timing.frameStartTimestamp == lastFrame || timing.cpuMainThreadFrameTime <= 0) return false;
                lastFrame = timing.frameStartTimestamp;
                milliseconds = (float)timing.cpuMainThreadFrameTime;
                return true;
            }
        }

        sealed class ReferenceArm : IBenchmarkArm
        {
            public string Id => ReferenceArmId;
            public string[] Markers => Array.Empty<string>();
            public void Activate() { }
            public void Deactivate() { }
            public void Step(int frame) { }
            public void SlotStarts() { }
            public void SlotEnds(int measuredFrames) { }
            public void Describe(JObject arm) { }
        }

        sealed class GateArm : IBenchmarkArm
        {
            readonly IBenchmarkArm inner;
            readonly int frameOffset;

            public GateArm(IBenchmarkArm inner, string id, string control, int frameOffset)
            {
                this.inner = inner;
                this.frameOffset = frameOffset;
                Id = id;
                Control = control;
            }

            public long BusyIterations { get; set; }

            public string Id { get; }
            public string Control { get; }
            public IBenchmarkArm Inner => inner;
            public string[] Markers => inner.Markers;
            public void Activate() => inner.Activate();
            public void Deactivate() => inner.Deactivate();

            public void Step(int frame)
            {
                inner.Step(frame + frameOffset);
                if (BusyIterations > 0) BenchmarkClock.Busy(BusyIterations);
            }

            public void SlotStarts() => inner.SlotStarts();
            public void SlotEnds(int measuredFrames) => inner.SlotEnds(measuredFrames);
            public void Describe(JObject arm) => inner.Describe(arm);
        }

        sealed class OneShotGateArm : IBenchmarkOneShotArm
        {
            readonly IBenchmarkOneShotArm inner;

            public OneShotGateArm(IBenchmarkOneShotArm inner, string id, string control)
            {
                this.inner = inner;
                Id = id;
                Control = control;
            }

            public long BusyIterations { get; set; }

            public string Id { get; }
            public string Control { get; }
            public bool Committed => inner.Committed;
            public IEnumerator Prepare() => inner.Prepare();

            public void Operate()
            {
                inner.Operate();
                if (BusyIterations > 0) BenchmarkClock.Busy(BusyIterations);
            }

            public IEnumerator Reset() => inner.Reset();
            public void Describe(JObject arm) => inner.Describe(arm);
        }
    }

    /// <summary>Measures one synchronous operation and the frames until the engine has processed it.</summary>
    public static class BenchmarkOneShot
    {
        const int FrameTimingLagFrames = 8;

        static RenderTexture fenceTarget;

        /// <summary>Returns once the GPU has finished every command submitted before the call.</summary>
        /// <exception cref="NotSupportedException">The player has no AsyncGPUReadback.</exception>
        /// <exception cref="InvalidOperationException">The fence's readback failed.</exception>
        public static IEnumerator Fence()
        {
            if (!SystemInfo.supportsAsyncGPUReadback)
                throw new NotSupportedException("The GPU fence needs AsyncGPUReadback, which this player lacks.");
            if (fenceTarget == null)
            {
                fenceTarget = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave };
                fenceTarget.Create();
            }
            var request = AsyncGPUReadback.Request(fenceTarget);
            while (!request.done)
                yield return null;
            if (request.hasError) throw new InvalidOperationException("The GPU fence's readback failed.");
        }

        /// <summary>
        /// Fences the GPU, runs <see cref="IBenchmarkOneShotArm.Operate"/> under a stopwatch and steps frames until the arm
        /// reports its result committed, then writes into <paramref name="slot"/>: <c>syncMs</c>; <c>frames</c>, the
        /// completed frames until committed (frames to pixels); <c>workMs</c>, the update and canvas windows of those frames
        /// minus the clock hold, the synchronous call included; per-frame <c>updateMs</c>, <c>canvasMs</c> and
        /// <c>holdMs</c>; and <c>frameTimings</c>, FrameTimingManager's series of those frames and the next 8, in delivery
        /// order (its timings arrive a few frames late).
        /// </summary>
        /// <exception cref="InvalidOperationException">The arm did not commit within <paramref name="maxFrames"/> frames.</exception>
        public static IEnumerator Measure(IBenchmarkOneShotArm arm, int maxFrames, JObject slot)
        {
            yield return Fence();
            var update = new double[maxFrames];
            var canvas = new double[maxFrames];
            var hold = new double[maxFrames];
            using var sampler = new BenchmarkFrameSampler(maxFrames + FrameTimingLagFrames);
            var started = Stopwatch.GetTimestamp();
            arm.Operate();
            var syncMilliseconds = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
            var frames = 0;
            while (true)
            {
                yield return null;
                sampler.Sample();
                update[frames] = BenchmarkFrameProbe.LastMilliseconds;
                canvas[frames] = BenchmarkFrameProbe.LastCanvasMilliseconds;
                hold[frames] = BenchmarkFrameProbe.LastHoldMilliseconds;
                frames++;
                if (arm.Committed) break;
                if (frames >= maxFrames)
                    throw new InvalidOperationException($"Arm '{arm.Id}' did not commit within {maxFrames} frames.");
            }
            for (var i = 0; i < FrameTimingLagFrames; i++)
            {
                yield return null;
                sampler.Sample();
            }
            var work = 0d;
            for (var i = 0; i < frames; i++)
                work += update[i] + canvas[i] - hold[i];
            slot["syncMs"] = Math.Round(syncMilliseconds, 4);
            slot["frames"] = frames;
            slot["workMs"] = Math.Round(work, 4);
            slot["updateMs"] = Series(update, frames);
            slot["canvasMs"] = Series(canvas, frames);
            slot["holdMs"] = Series(hold, frames);
            slot["frameTimings"] = sampler.Serialize(true);
        }

        static JArray Series(double[] values, int count)
        {
            var series = new JArray();
            for (var i = 0; i < count; i++)
                series.Add(Math.Round(values[i], 4));
            return series;
        }
    }
}
