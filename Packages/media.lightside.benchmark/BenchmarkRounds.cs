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
        /// <remarks>
        /// Called once, before the warm-up, on the built inactive arm. No two arms of one case may write the same
        /// descriptor.
        /// </remarks>
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

        /// <summary>Writes the arm's configuration with the keys of <see cref="IBenchmarkArm.Describe"/>; called once, after round 0, when every case arm has operated once.</summary>
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
    /// camera, canvas and harness. With <c>-benchmarkGates</c> it also gains <c>&lt;first arm&gt;.aa</c>, the first arm
    /// again on the same objects, and <c>&lt;control base&gt;.control</c>, the run's positive control. The control base is
    /// the first arm whose <c>threads</c> is not <c>parallel</c>; the control arm is that arm plus a busy loop per frame,
    /// sized after the base's warm-up to <see cref="ControlFraction"/> of its update and canvas work (at least
    /// <see cref="ControlFloorMilliseconds"/>) at the calibrated clock and timed in every measured frame. A busy loop on a
    /// parallel arm overlaps the main thread's wait for its workers instead of adding to it, so a case of parallel arms
    /// alone has no positive control (<c>controlBase</c> null). Round 0 of a frame case is its warm-up round.
    /// The record carries <c>armInfo</c>: each arm's <see cref="IBenchmarkArm.Describe"/> plus <c>control</c>
    /// (<c>reference</c>, <c>aa</c>, <c>positive</c> or null); a gate arm carries the descriptor of the arm it repeats.
    /// </remarks>
    public static class BenchmarkRounds
    {
        /// <summary>Id of the arm whose slots run with every case arm inactive.</summary>
        public const string ReferenceArmId = "reference";

        /// <summary>Share of the control base's main-thread work that the positive-control arm adds as a busy loop.</summary>
        public const double ControlFraction = 0.15;

        /// <summary>Least busy loop the positive-control arm adds, in milliseconds at the calibrated clock: well above a slot's noise.</summary>
        public const double ControlFloorMilliseconds = 1d;

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
        /// case record into <paramref name="record"/>: kind, seed, settings, arms, armInfo, clockThreads (the threads the
        /// clock's hint session covers, <see cref="BenchmarkClock.CoverProcessThreads"/>), processCpu
        /// (<see cref="BenchmarkProcessCpu.Describe"/>), warmup, thermal (one reading per round boundary) and rounds (order
        /// and, per arm, <c>cpuMs</c>, <c>cpuSource</c>, the sampler's <c>metrics</c> and, where the player reads it,
        /// <c>processCpuMs</c>: the process's CPU time on all threads over the measured frames, per frame). With
        /// <c>-benchmarkGates</c> it adds <c>controlBase</c>, <c>controlFraction</c> and <c>controlMsAtCalibration</c>, and
        /// each control slot adds <c>controlBusyMs</c>, the median time of the busy loop in its measured frames: the
        /// positive control recovers when its <c>cpuMs</c> minus the base's of the same round equals it.
        /// </summary>
        /// <remarks>Every arm of <paramref name="caseArms"/> must be built and inactive; it is inactive again when this returns.</remarks>
        /// <exception cref="ArgumentException">The case has no arms, or two arms share an id.</exception>
        public static IEnumerator RunFrames(string caseId, IReadOnlyList<IBenchmarkArm> caseArms,
            BenchmarkRoundSettings settings, JObject record)
        {
            if (caseArms == null || caseArms.Count == 0)
                throw new ArgumentException($"Case '{caseId}' has no arms.", nameof(caseArms));
            BenchmarkFrameProbe.Install();
            var info = DescribeCase(caseId, caseArms, arm => arm.Id, (arm, description) => arm.Describe(description),
                out var controlBase);
            info[ReferenceArmId] = new JObject { ["control"] = "reference" };
            var arms = new List<IBenchmarkArm>(caseArms.Count + 3) { new ReferenceArm() };
            arms.AddRange(caseArms);
            GateArm control = null;
            if (BenchmarkLaunch.Has("-benchmarkGates"))
            {
                var aa = new GateArm(caseArms[0], caseArms[0].Id + ".aa", GateFrameOffset, 0);
                arms.Add(aa);
                AddGate(info, caseArms[0].Id, aa.Id, "aa");
                if (controlBase >= 0)
                {
                    var baseArm = caseArms[controlBase];
                    control = new GateArm(baseArm, baseArm.Id + ".control", 2 * GateFrameOffset, settings.measuredFrames);
                    arms.Add(control);
                    AddGate(info, baseArm.Id, control.Id, "positive");
                }
                record["controlBase"] = controlBase >= 0 ? new JValue(caseArms[controlBase].Id) : JValue.CreateNull();
            }
            record["kind"] = "frames";
            record["seed"] = settings.seed;
            record["settings"] = settings.Describe();
            record["arms"] = ArmIds(caseId, arms, arm => arm.Id);
            record["armInfo"] = info;
            record["clockThreads"] = BenchmarkClock.CoverProcessThreads();
            record["processCpu"] = BenchmarkProcessCpu.Describe();
            var processCpu = BenchmarkProcessCpu.Available;

            var frames = new int[arms.Count];
            var expectedWork = new double[arms.Count];
            var slotWork = new float[settings.measuredFrames];
            var warmup = new JObject();
            record["warmup"] = warmup;
            for (var i = 0; i < arms.Count; i++)
            {
                var arm = arms[i];
                arm.Activate();
                BenchmarkClock.StopFilling();
                var steady = new BenchmarkSteadyState();
                var stepped = 0;
                var observed = new List<float>(settings.maxWarmupFrames);
                while (!steady.Settled && stepped < settings.maxWarmupFrames)
                {
                    arm.Step(frames[i]++);
                    stepped++;
                    yield return null;
                    var work = (float)FrameWork();
                    BenchmarkClock.FillFor(work);
                    steady.Observe(work);
                    observed.Add(work);
                }
                expectedWork[i] = TailMedian(observed);
                if (control != null && i == controlBase + 1) SizeControl(control, expectedWork[i], record);
                arm.Deactivate();
                warmup[arm.Id] = new JObject
                {
                    ["frames"] = stepped, ["settled"] = steady.Settled, ["signal"] = "updateMs + canvasMs",
                    ["workMs"] = Math.Round(expectedWork[i], 4)
                };
            }

            var random = new System.Random(settings.seed);
            var thermal = new JArray { BenchmarkThermal.Read() };
            var rounds = new JArray();
            record["thermal"] = thermal;
            record["rounds"] = rounds;
            for (var round = 0; round < settings.rounds; round++)
            {
                var order = Shuffled(arms.Count, random);
                var orderIds = new JArray();
                var slots = new JObject();
                foreach (var index in order)
                {
                    var arm = arms[index];
                    orderIds.Add(arm.Id);
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    BenchmarkClock.FillFor(expectedWork[index]);
                    arm.Activate();
                    for (var i = 0; i < settings.settleFrames; i++)
                    {
                        arm.Step(frames[index]++);
                        yield return null;
                    }
                    using (var sampler = new BenchmarkFrameSampler(settings.measuredFrames, arm.Markers))
                    {
                        arm.SlotStarts();
                        var processStart = processCpu ? BenchmarkProcessCpu.Nanoseconds() : 0L;
                        var fillStart = BenchmarkClock.FillTicks;
                        for (var i = 0; i < settings.measuredFrames; i++)
                        {
                            arm.Step(frames[index]++);
                            yield return null;
                            sampler.Sample();
                            slotWork[i] = (float)FrameWork();
                        }
                        var processEnd = processCpu ? BenchmarkProcessCpu.Nanoseconds() : 0L;
                        var fill = BenchmarkClock.FillTicks - fillStart;
                        Array.Sort(slotWork);
                        expectedWork[index] = BenchmarkStatistics.MedianSorted(slotWork);
                        arm.SlotEnds(settings.measuredFrames);
                        BenchmarkClock.ThrowOnFailure();
                        var cpu = sampler.CpuMilliseconds(out var source);
                        var slot = new JObject
                        {
                            ["cpuMs"] = cpu,
                            ["cpuSource"] = source,
                            ["metrics"] = sampler.Serialize(settings.includeSamples)
                        };
                        if (processCpu)
                            slot["processCpuMs"] = Math.Round(((processEnd - processStart) / 1e6 - fill * 1000d / Stopwatch.Frequency) / settings.measuredFrames, 4);
                        if (arm == control) slot["controlBusyMs"] = control.SlotBusyMilliseconds();
                        slots[arm.Id] = slot;
                    }
                    arm.Deactivate();
                }
                thermal.Add(BenchmarkThermal.Read());
                rounds.Add(new JObject { ["round"] = round, ["warmup"] = round == 0, ["order"] = orderIds, ["slots"] = slots });
            }
            BenchmarkClock.StopFilling();
        }

        /// <summary>
        /// Runs <see cref="BenchmarkRoundSettings.rounds"/> rounds of one-shot slots (<see cref="BenchmarkOneShot.Measure"/>)
        /// and writes the case record into <paramref name="record"/>: kind, coldResult, seed, settings, arms, armInfo,
        /// clockThreads, processCpu, thermal and rounds (order and, per arm, the slot). Round 0 holds each arm's first
        /// operation in the process; after it the hint session is extended to the threads those operations created, and
        /// clockThreads counts the threads it covers from round 1 on.
        /// </summary>
        /// <remarks>
        /// With <c>-benchmarkGates</c> the case gains, from round 1 on, <c>&lt;first arm&gt;.aa</c>, the first arm's
        /// operation again, and <c>&lt;control base&gt;.control</c> (the base chosen as in <see cref="RunFrames"/>), the
        /// base's operation followed by a busy loop inside <see cref="IBenchmarkOneShotArm.Operate"/>. Before each of its
        /// slots the loop is sized to <see cref="ControlFraction"/> of the base's median <c>workMs</c> from round 1 on so far
        /// (round 0's while there is none), at least <see cref="ControlFloorMilliseconds"/>; the slot records
        /// <c>controlBusyMs</c>, the loop's time in that slot, and <c>controlMsAtCalibration</c>. The record adds
        /// <c>controlBase</c> and <c>controlFraction</c>. Gate arms never run in round 0, so every case arm's first
        /// operation stays the process's first.
        /// </remarks>
        /// <exception cref="ArgumentException">The case has no arms, or two arms share an id.</exception>
        public static IEnumerator RunOneShots(string caseId, IReadOnlyList<IBenchmarkOneShotArm> caseArms,
            BenchmarkRoundSettings settings, JObject record)
        {
            if (caseArms == null || caseArms.Count == 0)
                throw new ArgumentException($"Case '{caseId}' has no arms.", nameof(caseArms));
            BenchmarkFrameProbe.Install();
            var arms = new List<IBenchmarkOneShotArm>(caseArms);
            record["kind"] = "oneShot";
            record["coldResult"] = settings.coldResult;
            record["seed"] = settings.seed;
            record["settings"] = settings.Describe();
            record["arms"] = ArmIds(caseId, arms, arm => arm.Id);
            record["clockThreads"] = BenchmarkClock.CoverProcessThreads();
            record["processCpu"] = BenchmarkProcessCpu.Describe();

            OneShotGateArm control = null;
            var controlBase = -1;
            var baseFirstWork = 0d;
            var baseWork = new List<float>(settings.rounds);
            var random = new System.Random(settings.seed);
            var thermal = new JArray { BenchmarkThermal.Read() };
            var rounds = new JArray();
            record["thermal"] = thermal;
            record["rounds"] = rounds;
            for (var round = 0; round < settings.rounds; round++)
            {
                var order = Shuffled(arms.Count, random);
                var orderIds = new JArray();
                var slots = new JObject();
                foreach (var index in order)
                {
                    var arm = arms[index];
                    orderIds.Add(arm.Id);
                    if (arm == control) control.BusyIterations = BenchmarkClock.Iterations(OneShotControlTarget(baseWork, baseFirstWork));
                    yield return arm.Prepare();
                    var slot = new JObject();
                    yield return BenchmarkOneShot.Measure(arm, settings.maxCommitFrames, slot);
                    BenchmarkClock.ThrowOnFailure();
                    if (arm == control)
                    {
                        slot["controlBusyMs"] = Math.Round(control.BusyMilliseconds, 4);
                        slot["controlMsAtCalibration"] = Math.Round(BenchmarkClock.Milliseconds(control.BusyIterations), 4);
                    }
                    if (round > 0 && index == controlBase) baseWork.Add((float)(double)slot["workMs"]);
                    slots[arm.Id] = slot;
                    yield return arm.Reset();
                }
                thermal.Add(BenchmarkThermal.Read());
                rounds.Add(new JObject { ["round"] = round, ["order"] = orderIds, ["slots"] = slots });
                if (round != 0) continue;

                record["clockThreads"] = BenchmarkClock.CoverProcessThreads();
                var info = DescribeCase(caseId, caseArms, arm => arm.Id, (arm, description) => arm.Describe(description),
                    out controlBase);
                if (BenchmarkLaunch.Has("-benchmarkGates"))
                {
                    var aa = new OneShotGateArm(caseArms[0], caseArms[0].Id + ".aa");
                    arms.Add(aa);
                    AddGate(info, caseArms[0].Id, aa.Id, "aa");
                    if (controlBase >= 0)
                    {
                        var baseArm = caseArms[controlBase];
                        control = new OneShotGateArm(baseArm, baseArm.Id + ".control");
                        arms.Add(control);
                        AddGate(info, baseArm.Id, control.Id, "positive");
                        baseFirstWork = (double)slots[baseArm.Id]["workMs"];
                        record["controlFraction"] = ControlFraction;
                    }
                    record["controlBase"] = controlBase >= 0 ? new JValue(caseArms[controlBase].Id) : JValue.CreateNull();
                    record["arms"] = ArmIds(caseId, arms, arm => arm.Id);
                }
                record["armInfo"] = info;
            }
            BenchmarkClock.StopFilling();
        }

        static double OneShotControlTarget(List<float> baseWork, double baseFirstWork)
        {
            var work = baseFirstWork;
            if (baseWork.Count > 0)
            {
                var sorted = new List<float>(baseWork);
                sorted.Sort();
                work = BenchmarkStatistics.MedianSorted(sorted);
            }
            return Math.Max(ControlFloorMilliseconds, ControlFraction * work);
        }

        /// <summary>
        /// The descriptors of the case arms by id with <c>control</c> null, failing the case on a missing key or two arms
        /// alike; <paramref name="controlBase"/> is the index of the first arm whose <c>threads</c> is not <c>parallel</c>,
        /// -1 when every arm's is.
        /// </summary>
        static JObject DescribeCase<T>(string caseId, IReadOnlyList<T> arms, Func<T, string> id, Action<T, JObject> describe,
            out int controlBase)
        {
            var info = new JObject();
            controlBase = -1;
            for (var i = 0; i < arms.Count; i++)
            {
                var description = new JObject();
                describe(arms[i], description);
                RequireDescriptor(caseId, id(arms[i]), description);
                description["control"] = JValue.CreateNull();
                info[id(arms[i])] = description;
                if (controlBase < 0 && (string)description["threads"] != "parallel") controlBase = i;
            }
            RequireDistinct(caseId, info);
            return info;
        }

        static void AddGate(JObject info, string innerId, string gateId, string control)
        {
            var description = (JObject)info[innerId].DeepClone();
            description["control"] = control;
            info[gateId] = description;
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
                var key = string.Join("|", description["engine"], description["threads"], description["style"],
                    description["load"], description["baseline"], description["variant"]);
                if (seen.TryGetValue(key, out var other))
                    throw new InvalidOperationException($"Arms '{other}' and '{pair.Key}' of case '{caseId}' describe the same configuration; give one a variant.");
                seen.Add(key, pair.Key);
            }
        }

        /// <summary>Update and canvas work of the most recently completed frame: the frame's own work, without the fill.</summary>
        static double FrameWork() => BenchmarkFrameProbe.LastMilliseconds + BenchmarkFrameProbe.LastCanvasMilliseconds;

        /// <summary>Median of the last frames of a warm-up: the work an arm settled at.</summary>
        static double TailMedian(List<float> observed)
        {
            var tail = observed.GetRange(Math.Max(0, observed.Count - ControlSizingFrames), Math.Min(observed.Count, ControlSizingFrames));
            tail.Sort();
            return BenchmarkStatistics.MedianSorted(tail);
        }

        /// <summary>Sizes the positive control from the control base's settled work.</summary>
        static void SizeControl(GateArm control, double baseWork, JObject record)
        {
            var target = Math.Max(ControlFloorMilliseconds, ControlFraction * baseWork);
            control.BusyIterations = BenchmarkClock.Iterations(target);
            record["controlFraction"] = ControlFraction;
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

        static int[] Shuffled(int count, System.Random random)
        {
            var order = new int[count];
            for (var i = 0; i < count; i++) order[i] = i;
            for (var i = count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
            return order;
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

        /// <summary>An arm again on the same objects, with a busy loop after each step when <see cref="BusyIterations"/> is set; the loop is timed in every measured frame.</summary>
        sealed class GateArm : IBenchmarkArm
        {
            readonly IBenchmarkArm inner;
            readonly int frameOffset;
            readonly float[] busy;
            int busyFrames;
            bool measuring;

            public GateArm(IBenchmarkArm inner, string id, int frameOffset, int measuredFrames)
            {
                this.inner = inner;
                this.frameOffset = frameOffset;
                busy = new float[measuredFrames];
                Id = id;
            }

            public long BusyIterations { get; set; }

            public string Id { get; }
            public string[] Markers => inner.Markers;
            public void Activate() => inner.Activate();
            public void Deactivate() => inner.Deactivate();

            public void Step(int frame)
            {
                inner.Step(frame + frameOffset);
                if (BusyIterations == 0) return;
                var started = Stopwatch.GetTimestamp();
                BenchmarkClock.Busy(BusyIterations);
                if (measuring) busy[busyFrames++] = (float)((Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency);
            }

            public void SlotStarts()
            {
                inner.SlotStarts();
                busyFrames = 0;
                measuring = true;
            }

            public void SlotEnds(int measuredFrames)
            {
                measuring = false;
                inner.SlotEnds(measuredFrames);
            }

            /// <summary>Median milliseconds of the busy loop over the last slot's measured frames.</summary>
            public double SlotBusyMilliseconds()
            {
                Array.Sort(busy, 0, busyFrames);
                return Math.Round(BenchmarkStatistics.MedianSorted(new ArraySegment<float>(busy, 0, busyFrames)), 4);
            }

            public void Describe(JObject arm) => inner.Describe(arm);
        }

        /// <summary>An arm's operation again, followed inside <see cref="Operate"/> by a timed busy loop when <see cref="BusyIterations"/> is set.</summary>
        sealed class OneShotGateArm : IBenchmarkOneShotArm
        {
            readonly IBenchmarkOneShotArm inner;

            public OneShotGateArm(IBenchmarkOneShotArm inner, string id)
            {
                this.inner = inner;
                Id = id;
            }

            public long BusyIterations { get; set; }

            /// <summary>Milliseconds the busy loop took in the last <see cref="Operate"/>.</summary>
            public double BusyMilliseconds { get; private set; }

            public string Id { get; }
            public bool Committed => inner.Committed;
            public IEnumerator Prepare() => inner.Prepare();

            public void Operate()
            {
                inner.Operate();
                if (BusyIterations == 0) return;
                var started = Stopwatch.GetTimestamp();
                BenchmarkClock.Busy(BusyIterations);
                BusyMilliseconds = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
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
        /// Sets the <see cref="BenchmarkClock"/> fill from the last frame's work so the slot's frames carry the load target,
        /// fences the GPU, runs <see cref="IBenchmarkOneShotArm.Operate"/> under a stopwatch and steps frames until the arm
        /// reports its result committed, then writes into <paramref name="slot"/>: <c>syncMs</c>; <c>frames</c>, the
        /// completed frames until committed (frames to pixels); <c>workMs</c>, the update and canvas windows of those frames,
        /// the synchronous call included; <c>processCpuMs</c> where the player reads it, the process's CPU time on all
        /// threads from the call to the committed frame; per-frame <c>updateMs</c> and <c>canvasMs</c>; and
        /// <c>frameTimings</c>, FrameTimingManager's series of those frames and the next 8, in delivery order (its timings
        /// arrive a few frames late).
        /// </summary>
        /// <exception cref="InvalidOperationException">The arm did not commit within <paramref name="maxFrames"/> frames.</exception>
        public static IEnumerator Measure(IBenchmarkOneShotArm arm, int maxFrames, JObject slot)
        {
            BenchmarkClock.FillFor(BenchmarkFrameProbe.LastMilliseconds + BenchmarkFrameProbe.LastCanvasMilliseconds);
            yield return Fence();
            var update = new double[maxFrames];
            var canvas = new double[maxFrames];
            using var sampler = new BenchmarkFrameSampler(maxFrames + FrameTimingLagFrames);
            var processCpu = BenchmarkProcessCpu.Available;
            var processStart = processCpu ? BenchmarkProcessCpu.Nanoseconds() : 0L;
            var fillStart = BenchmarkClock.FillTicks;
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
                frames++;
                if (arm.Committed) break;
                if (frames >= maxFrames)
                    throw new InvalidOperationException($"Arm '{arm.Id}' did not commit within {maxFrames} frames.");
            }
            var processEnd = processCpu ? BenchmarkProcessCpu.Nanoseconds() : 0L;
            var fill = BenchmarkClock.FillTicks - fillStart;
            for (var i = 0; i < FrameTimingLagFrames; i++)
            {
                yield return null;
                sampler.Sample();
            }
            var work = 0d;
            for (var i = 0; i < frames; i++)
                work += update[i] + canvas[i];
            slot["syncMs"] = Math.Round(syncMilliseconds, 4);
            slot["frames"] = frames;
            slot["workMs"] = Math.Round(work, 4);
            if (processCpu) slot["processCpuMs"] = Math.Round((processEnd - processStart) / 1e6 - fill * 1000d / Stopwatch.Frequency, 4);
            slot["updateMs"] = Series(update, frames);
            slot["canvasMs"] = Series(canvas, frames);
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
