using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using LightSide.Benchmark;
using Newtonsoft.Json.Linq;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// MoveIt's keyframe players against Unity's animation systems — Mecanim's Animator, the legacy Animation component
/// and Playables — on content generated from one specification: single objects, a large skeleton, crowds,
/// cross-fades, layers, UI, sprites, activation, seeking and churn, plus the memory a playing instance occupies.
/// </summary>
/// <remarks>
/// Every case and participant is measured the way the UniText scenarios are: thermal settle, setup, adaptive warmup
/// until the frame cost stops drifting, one garbage collection outside the window, then
/// <see cref="measuredFrames"/> frames sampled by <see cref="BenchmarkFrameSampler"/>. Outside the window every
/// participant must move every sampled track — or hold them, for a finished clip — and must render the
/// specification's values at fixed times, so a participant that skips work fails instead of winning.
/// </remarks>
public sealed class AnimationBenchmarkSuite : MonoBehaviour, IBenchmarkSuite
{
    const int SetupFrames = 3;
    const int SettleFrames = 6;
    const int SampledInstances = 6;
    const int ObservedFrames = 45;
    const float ObservedSeconds = 0.75f;
    const int MemoryFrames = 30;
    const float HoldMargin = 0.25f;
    const int CanvasColumns = 20;
    const int SpriteColumns = 40;
    const int WarmUpFrames = 180;

    /// <summary>Profiler markers whose time development players record per measured frame.</summary>
    static readonly string[] Markers =
    {
        "LightSide.MoveIt.Total", "LightSide.MoveIt.Advance", "LightSide.MoveIt.AdvanceWait",
        "LightSide.MoveIt.Apply", "LightSide.MoveIt.SinkFrame", "LightSide.MoveIt.Graphs", "LightSide.MoveIt.Write",
        "LightSide.MoveIt.WriteWait", "LightSide.MoveIt.Retire", "MoveItClipJob (Burst)", "MoveItAdvanceJob (Burst)",
        "MoveItPersistentTransformJob (Burst)", "PreLateUpdate.DirectorUpdateAnimationBegin",
        "PreLateUpdate.DirectorUpdateAnimationEnd", "PreLateUpdate.LegacyAnimationUpdate",
        "PostLateUpdate.PlayerUpdateCanvases", "Instantiate"
    };

    /// <summary>Profiler markers whose time in the setup frame development players record.</summary>
    static readonly string[] SetupMarkers =
    {
        "Instantiate", "Instantiate.Copy", "Instantiate.Produce", "Instantiate.Awake", "GC.Collect",
        "LightSide.MoveIt.ClipBind", "LightSide.MoveIt.ClipBuild", "LightSide.MoveIt.ClipClaim",
        "LightSide.MoveIt.ClipPresent", "LightSide.MoveIt.ClipSeek"
    };

    [Tooltip("Generated content: Tools/LightSide/Benchmarks/Generate Animation Content.")]
    [SerializeField] AnimationBenchmarkContent content;

    [Min(30), Tooltip("Frames sampled per case and participant.")]
    [SerializeField] int measuredFrames = 120;

    [Min(24), Tooltip("Upper bound of the adaptive warmup; a measurement whose frame cost still drifts after it is reported as unsettled.")]
    [SerializeField] int maxWarmupFrames = 240;

    [Tooltip("Keep every frame's sample beside the summaries.")]
    [SerializeField] bool includeSamples = true;

    readonly Dictionary<string, JObject> results = new();

    public string SuiteId => "animation";

    public string Section => "moveitAnimation";

    public string StreamGlobal => "__moveitAnimationRuns";

    public int Scenario => 6;

    public IEnumerable<KeyValuePair<string, string>> PhaseNotes => new[]
    {
        new KeyValuePair<string, string>("animation.windows",
            "Per frame: frameMs = main-thread work from the end of Initialization to the start of PresentAfterDraw; updateMs = end of Initialization to end of PreLateUpdate, which holds MoveIt's update, the scripted drive and the Mecanim, legacy Animation and Playables evaluation; canvasMs = PostLateUpdate.PlayerUpdateCanvases, where uGUI rebuilds what animation dirtied. Main-thread figures include waits for animation jobs and exclude worker-thread time."),
        new KeyValuePair<string, string>("animation.method",
            "Each case and participant: thermal settle, setup, adaptive warmup until the mean frameMs of consecutive 12-frame windows agrees within 5% (or 0.05 ms), one forced GC outside the window, settle frames, then the measured frames. Participants rotate per case and the whole order reverses on odd repeats; BENCHMARK_PARTICIPANT selects a case id, a participant key or case.participant."),
        new KeyValuePair<string, string>("animation.content",
            "Every participant plays content generated from one specification: identical hierarchies, bindings, key times, values and slopes (12 Hermite keys per curve, seamless loops), scaled game time, Animator culling off and no root motion, and the same seeded start offset per instance."),
        new KeyValuePair<string, string>("animation.validation",
            "Outside the window every participant must change every sampled track (or none, for ui-hold) and must render the specification's values at fixed times within tolerance; validation.maxError holds the largest error per property."),
        new KeyValuePair<string, string>("animation.memory",
            "memory: Memory profiler counters after settled GCs before creating, while playing and after destroying the instances; perInstanceBytes = (playing - before) / instances. Run one participant per process for figures free of the previous participant's pools.")
    };

    public IEnumerator Run(BenchmarkContext context)
    {
        if (content == null)
            throw new InvalidOperationException(
                $"{nameof(AnimationBenchmarkSuite)} needs its generated content: Tools/LightSide/Benchmarks/Generate Animation Content.");
        if (content.Version != AnimationBenchmarkSpec.ContentVersion)
            throw new InvalidOperationException(
                $"The animation benchmark content is version {content.Version}, the specification {AnimationBenchmarkSpec.ContentVersion}: generate the content again.");
        BenchmarkFrameProbe.Install();
        results.Clear();

        var plan = Plan(context, AnimationBenchmarkSpec.Cases, AnimationBenchmarkParticipant.All());
        if (plan.Count == 0)
            throw new InvalidOperationException(
                $"No animation benchmark case or participant answers to '{context.Participant}'.");
        PublishConfig(context, plan);
        var (firstCase, firstParticipant) = plan[0];
        if (firstParticipant.Unsupported(firstCase) == null)
            yield return WarmUp(firstCase, firstParticipant);

        foreach (var (benchmarkCase, participant) in plan)
        {
            var record = Record(benchmarkCase, participant);
            var unsupported = participant.Unsupported(benchmarkCase);
            if (unsupported != null)
            {
                record["status"] = "unsupported";
                record["reason"] = unsupported;
                continue;
            }
            yield return context.Run($"animation {benchmarkCase.Id}.{participant.Key}",
                () => BenchmarkRecord.Guard(benchmarkCase.Drive == AnimationBenchmarkDrive.Memory
                    ? MeasureMemory(context, benchmarkCase, participant, record)
                    : Measure(context, benchmarkCase, participant, record), record), null);
            if (!context.Alive) yield break;
        }
    }

    public JObject Serialize()
    {
        if (results.Count == 0) return null;
        var section = new JObject();
        foreach (var pair in results)
            section[pair.Key] = pair.Value;
        return section;
    }

    public bool Measured(out string reason)
    {
        reason = null;
        var measured = false;
        foreach (var pair in results)
            foreach (var participant in (JObject)pair.Value["participants"])
            {
                var status = (string)participant.Value["status"];
                if (status == "measured") measured = true;
                else if (status != "unsupported")
                {
                    reason = $"Animation case '{pair.Key}' ended with status '{status}' for '{participant.Key}'.";
                    return false;
                }
            }
        return measured;
    }

    static List<(AnimationBenchmarkCase, AnimationBenchmarkParticipant)> Plan(BenchmarkContext context,
        IReadOnlyList<AnimationBenchmarkCase> cases, AnimationBenchmarkParticipant[] participants)
    {
        var plan = new List<(AnimationBenchmarkCase, AnimationBenchmarkParticipant)>();
        var reversed = (context.Repeat & 1) != 0;
        for (var c = 0; c < cases.Count; c++)
        {
            var benchmarkCase = cases[reversed ? cases.Count - 1 - c : c];
            for (var p = 0; p < participants.Length; p++)
            {
                var rotated = (p + c) % participants.Length;
                var participant = participants[reversed ? participants.Length - 1 - rotated : rotated];
                if (Selected(context.Participant, benchmarkCase.Id, participant.Key))
                    plan.Add((benchmarkCase, participant));
            }
        }
        return plan;
    }

    static bool Selected(string selection, string caseId, string participant) =>
        selection == null ||
        string.Equals(selection, caseId, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(selection, participant, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(selection, caseId + "." + participant, StringComparison.OrdinalIgnoreCase);

    void PublishConfig(BenchmarkContext context, List<(AnimationBenchmarkCase, AnimationBenchmarkParticipant)> plan)
    {
        var order = new JArray();
        foreach (var (benchmarkCase, participant) in plan)
            order.Add(benchmarkCase.Id + "." + participant.Key);
        context.Config["animation"] = new JObject
        {
            ["measuredFrames"] = measuredFrames,
            ["maxWarmupFrames"] = maxWarmupFrames,
            ["setupFrames"] = SetupFrames,
            ["settleFrames"] = SettleFrames,
            ["sampledInstances"] = SampledInstances,
            ["observedFrames"] = ObservedFrames,
            ["observedSeconds"] = ObservedSeconds,
            ["memoryFrames"] = MemoryFrames,
            ["warmUpFrames"] = WarmUpFrames,
            ["contentVersion"] = content.Version,
            ["reversed"] = (context.Repeat & 1) != 0,
            ["order"] = order
        };
    }

    JObject Record(AnimationBenchmarkCase benchmarkCase, AnimationBenchmarkParticipant participant)
    {
        if (!results.TryGetValue(benchmarkCase.Id, out var caseRecord))
            results[benchmarkCase.Id] = caseRecord = new JObject
            {
                ["description"] = benchmarkCase.Description,
                ["workload"] = Describe(benchmarkCase),
                ["participants"] = new JObject()
            };
        var record = new JObject { ["status"] = "measuring" };
        caseRecord["participants"][participant.Key] = record;
        return record;
    }

    static JObject Describe(AnimationBenchmarkCase benchmarkCase)
    {
        var clips = new JArray();
        var curves = 0;
        foreach (var clip in benchmarkCase.Clips)
        {
            clips.Add(new JObject
            {
                ["name"] = clip.Name,
                ["length"] = clip.Length,
                ["loop"] = clip.Loop,
                ["curves"] = clip.CurveCount
            });
            curves = Math.Max(curves, clip.CurveCount);
        }
        if (benchmarkCase.LayerClip >= 0) curves = benchmarkCase.Clips[0].CurveCount +
                                                   benchmarkCase.Clips[benchmarkCase.LayerClip].CurveCount;
        return new JObject
        {
            ["instances"] = benchmarkCase.Instances,
            ["rig"] = benchmarkCase.Rig.ToString(),
            ["objectsPerInstance"] = benchmarkCase.Paths.Length,
            ["drive"] = benchmarkCase.Drive.ToString(),
            ["curvesPerInstance"] = curves,
            ["keysPerCurve"] = AnimationBenchmarkSpec.KeyCount,
            ["content"] = benchmarkCase.ContentId,
            ["clips"] = clips,
            ["validationTimes"] = new JArray(benchmarkCase.ValidationTimes)
        };
    }

    IEnumerator Measure(BenchmarkContext context, AnimationBenchmarkCase benchmarkCase,
        AnimationBenchmarkParticipant participant, JObject record)
    {
        yield return context.ThermalSettle();
        var stage = new Stage(benchmarkCase, participant, content);
        try
        {
            var setupRecorders = StartSetupRecorders();
            var collections = GC.CollectionCount(0);
            var started = Stopwatch.GetTimestamp();
            stage.Setup();
            record["setupMs"] = Round(Milliseconds(started));
            record["setupInstantiateMs"] = Round(stage.InstantiateMilliseconds);
            record["setupStartMs"] = Round(stage.StartMilliseconds);
            record["setupCollections"] = GC.CollectionCount(0) - collections;

            for (var i = 0; i < SetupFrames; i++)
            {
                stage.Step();
                yield return null;
                if (i == 0 && setupRecorders != null) record["setupMarkers"] = ReadSetupRecorders(setupRecorders);
            }
            if (benchmarkCase.Drive == AnimationBenchmarkDrive.Hold)
            {
                var finished = Time.time + benchmarkCase.Clips[0].Length + HoldMargin;
                while (Time.time < finished)
                    yield return null;
            }

            var steady = new BenchmarkSteadyState();
            while (!steady.Settled && steady.Frames < maxWarmupFrames)
            {
                stage.Step();
                yield return null;
                steady.Observe((float)BenchmarkFrameProbe.LastFrameMilliseconds);
            }
            record["warmup"] = new JObject { ["frames"] = steady.Frames, ["settled"] = steady.Settled };

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            using (var sampler = new BenchmarkFrameSampler(measuredFrames, Markers))
            {
                for (var i = 0; i < SettleFrames; i++)
                {
                    stage.Step();
                    yield return null;
                }
                for (var i = 0; i < measuredFrames; i++)
                {
                    stage.Step();
                    yield return null;
                    sampler.Sample();
                }
                record["metrics"] = sampler.Serialize(includeSamples);
            }

            yield return stage.Observe(record);
            yield return stage.Validate(record);
            record["status"] = "measured";
            yield return BenchmarkScreenshot.Capture($"animation-{benchmarkCase.Id}-{participant.Key}");
        }
        finally
        {
            var started = Stopwatch.GetTimestamp();
            stage.Teardown();
            record["teardownMs"] = Round(Milliseconds(started));
        }
    }

    IEnumerator MeasureMemory(BenchmarkContext context, AnimationBenchmarkCase benchmarkCase,
        AnimationBenchmarkParticipant participant, JObject record)
    {
        yield return context.ThermalSettle();
        using var memory = new BenchmarkMemoryCounters();
        var stage = new Stage(benchmarkCase, participant, content);
        try
        {
            LightSide.ArrayPool.Clear();
            yield return BenchmarkMemoryCounters.Settle();
            var before = memory.Read();
            stage.Setup();
            for (var i = 0; i < MemoryFrames; i++)
                yield return null;
            yield return BenchmarkMemoryCounters.Settle();
            var playing = memory.Read();
            stage.Teardown();
            yield return BenchmarkMemoryCounters.Settle();
            var after = memory.Read();
            LightSide.ArrayPool.Clear();
            yield return BenchmarkMemoryCounters.Settle();
            var cleared = memory.Read();

            var perInstance = new JObject();
            var retained = new JObject();
            var kept = new JObject();
            foreach (var pair in playing)
            {
                perInstance[pair.Key] = Math.Round(((long)pair.Value - (long)before[pair.Key]) /
                                                   (double)benchmarkCase.Instances);
                retained[pair.Key] = (long)after[pair.Key] - (long)before[pair.Key];
                kept[pair.Key] = (long)cleared[pair.Key] - (long)before[pair.Key];
            }
            record["before"] = before;
            record["playing"] = playing;
            record["afterTeardown"] = after;
            record["afterPoolClear"] = cleared;
            record["perInstanceBytes"] = perInstance;
            record["retainedBytes"] = retained;
            record["retainedWithoutPoolCacheBytes"] = kept;
            record["status"] = "measured";
        }
        finally
        {
            stage.Teardown();
        }
    }

    /// <summary>
    /// Plays the first planned case and participant unrecorded for <see cref="WarmUpFrames"/> frames, so the first
    /// measurement after launch does not also pay for the player's start: clock ramp, first loads and first draws.
    /// </summary>
    IEnumerator WarmUp(AnimationBenchmarkCase benchmarkCase, AnimationBenchmarkParticipant participant)
    {
        var stage = new Stage(benchmarkCase, participant, content);
        try
        {
            stage.Setup();
            for (var i = 0; i < WarmUpFrames; i++)
            {
                stage.Step();
                yield return null;
            }
        }
        finally
        {
            stage.Teardown();
        }
        yield return null;
    }

    /// <summary>
    /// Recorders of <see cref="SetupMarkers"/> in a development player or the editor, else null: markers exist only
    /// where the profiler is compiled in.
    /// </summary>
    static ProfilerRecorder[] StartSetupRecorders()
    {
        if (!UnityEngine.Debug.isDebugBuild) return null;
        var recorders = new ProfilerRecorder[SetupMarkers.Length];
        for (var i = 0; i < recorders.Length; i++)
            recorders[i] = new ProfilerRecorder(SetupMarkers[i], 1,
                ProfilerRecorderOptions.Default | ProfilerRecorderOptions.StartImmediately);
        return recorders;
    }

    /// <summary>Milliseconds each setup marker took in the frame that just completed; disposes the recorders.</summary>
    static JObject ReadSetupRecorders(ProfilerRecorder[] recorders)
    {
        var result = new JObject();
        for (var i = 0; i < recorders.Length; i++)
        {
            if (recorders[i].Valid) result[SetupMarkers[i]] = Round(recorders[i].LastValue * 1e-6);
            recorders[i].Dispose();
        }
        return result;
    }

    static double Milliseconds(long since) =>
        (Stopwatch.GetTimestamp() - since) * 1000d / Stopwatch.Frequency;

    static double Round(double value) => Math.Round(value, 3);

    /// <summary>
    /// The instances of one case played by one participant: their creation and placement, the per-frame drive, and
    /// the checks that they move and show the specification's values.
    /// </summary>
    sealed class Stage
    {
        readonly AnimationBenchmarkCase benchmarkCase;
        readonly AnimationBenchmarkCase owner;
        readonly AnimationBenchmarkParticipant participant;
        readonly AnimationBenchmarkContent content;
        readonly AnimationBenchmarkContent.Entry entry;
        readonly GameObject prefab;
        readonly uint seed;
        readonly AnimationBenchmarkInstance[] instances;
        GameObject root;
        RectTransform[] cells;
        float startTime;
        int head;
        int spawned;
        bool tornDown;
        long instantiateTicks;
        long startTicks;

        public Stage(AnimationBenchmarkCase benchmarkCase, AnimationBenchmarkParticipant participant,
            AnimationBenchmarkContent content)
        {
            this.benchmarkCase = benchmarkCase;
            this.participant = participant;
            this.content = content;
            owner = AnimationBenchmarkSpec.Find(benchmarkCase.ContentId);
            entry = content.Find(owner.Id);
            prefab = participant.Prefab(entry);
            if (prefab == null)
                throw new InvalidOperationException(
                    $"The animation benchmark content of '{owner.Id}' has no {participant.Key} prefab; generate the content again.");
            seed = AnimationBenchmarkSpec.Hash(benchmarkCase.Id);
            instances = new AnimationBenchmarkInstance[benchmarkCase.Instances];
        }

        /// <summary>Time <see cref="Setup"/> spent instantiating and binding the instances.</summary>
        public double InstantiateMilliseconds => instantiateTicks * 1000d / Stopwatch.Frequency;

        /// <summary>Time <see cref="Setup"/> spent starting their playback.</summary>
        public double StartMilliseconds => startTicks * 1000d / Stopwatch.Frequency;

        public void Setup()
        {
            participant.Begin(benchmarkCase, entry);
            root = new GameObject(benchmarkCase.Id + "." + participant.Key);
            if (benchmarkCase.Rig == AnimationBenchmarkRig.Image) BuildCanvas();
            startTime = Time.time;
            for (var i = 0; i < instances.Length; i++)
                instances[i] = Spawn(new AnimationBenchmarkInstance(), i, i);
            spawned = instances.Length;
        }

        public void Step()
        {
            switch (benchmarkCase.Drive)
            {
                case AnimationBenchmarkDrive.CrossFade:
                {
                    var now = Time.time;
                    foreach (var instance in instances)
                    {
                        if (now >= instance.NextFade)
                        {
                            instance.Current ^= 1;
                            participant.CrossFade(instance, instance.Current, AnimationBenchmarkSpec.CrossFadeDuration);
                            instance.NextFade += AnimationBenchmarkSpec.CrossFadeInterval;
                        }
                        participant.Advance(instance, now);
                    }
                    break;
                }
                case AnimationBenchmarkDrive.Seek:
                {
                    var advanced = (Time.time - startTime) * AnimationBenchmarkSpec.SeekRate;
                    var length = benchmarkCase.Clips[0].Length;
                    foreach (var instance in instances)
                        participant.Seek(instance, Mathf.Repeat(instance.Phase + advanced, length));
                    break;
                }
                case AnimationBenchmarkDrive.Spawn:
                    for (var i = 0; i < AnimationBenchmarkSpec.SpawnPerFrame; i++)
                    {
                        var instance = instances[head];
                        participant.Release(instance);
                        Object.Destroy(instance.Root);
                        Spawn(instance, head, spawned++);
                        head = (head + 1) % instances.Length;
                    }
                    break;
            }
        }

        /// <summary>
        /// Watches the sampled tracks for at least <see cref="ObservedFrames"/> frames and
        /// <see cref="ObservedSeconds"/> seconds while the drive goes on: every track must change, or none for a
        /// finished clip.
        /// </summary>
        public IEnumerator Observe(JObject record)
        {
            var probes = Probes();
            var previous = new float[probes.Count * 4];
            var current = new float[probes.Count * 4];
            var changed = new bool[probes.Count];
            Read(probes, previous);
            var started = Time.time;
            var frames = 0;
            while (frames < ObservedFrames || Time.time - started < ObservedSeconds)
            {
                if (benchmarkCase.Drive != AnimationBenchmarkDrive.Spawn) Step();
                yield return null;
                frames++;
                Read(probes, current);
                for (var i = 0; i < current.Length; i++)
                    if (Mathf.Abs(current[i] - previous[i]) > 1e-5f)
                        changed[i / 4] = true;
                (previous, current) = (current, previous);
            }

            var moved = 0;
            foreach (var flag in changed)
                if (flag) moved++;
            record["motion"] = new JObject
            {
                ["frames"] = frames,
                ["seconds"] = Math.Round(Time.time - started, 3),
                ["tracks"] = probes.Count,
                ["changed"] = moved
            };
            if (benchmarkCase.Drive == AnimationBenchmarkDrive.Hold)
            {
                if (moved != 0)
                    throw new InvalidOperationException(
                        $"{moved} of {probes.Count} sampled tracks changed while the finished clip should hold them.");
            }
            else if (moved != probes.Count)
            {
                throw new InvalidOperationException(
                    $"{probes.Count - moved} of {probes.Count} sampled tracks never changed in {frames} frames.");
            }
        }

        /// <summary>
        /// Holds the sampled instances at each validation time and compares every track with the specification;
        /// fails on the first value beyond its tolerance after recording the largest error per property.
        /// </summary>
        public IEnumerator Validate(JObject record)
        {
            var sampled = SampledIndices();
            var objects = new Transform[sampled.Count][];
            for (var s = 0; s < sampled.Count; s++)
                objects[s] = Objects(instances[sampled[s]]);
            var curves = Curves();
            var maxError = new Dictionary<AnimationBenchmarkProperty, float>();
            string failure = null;

            foreach (var time in benchmarkCase.ValidationTimes)
            {
                foreach (var index in sampled)
                    participant.Hold(instances[index], time);
                yield return null;
                yield return null;

                for (var s = 0; s < sampled.Count; s++)
                {
                    var tracks = benchmarkCase.Clips[0].Tracks;
                    for (var t = 0; t < tracks.Length; t++)
                    {
                        var track = tracks[t];
                        var clip = 0;
                        var source = t;
                        if (benchmarkCase.LayerClip >= 0)
                        {
                            var layered = benchmarkCase.Clips[benchmarkCase.LayerClip].TrackOf(track.Object, track.Property);
                            if (layered >= 0)
                            {
                                clip = benchmarkCase.LayerClip;
                                source = layered;
                            }
                        }
                        var spec = benchmarkCase.Clips[clip];
                        var expected = new Vector3();
                        for (var c = 0; c < track.Components; c++)
                            expected[c] = AnimationBenchmarkSpec.Sample(spec, curves[clip][source][c], time);
                        var target = objects[s][track.Object];
                        var error = Error(track.Property, target, expected);
                        maxError[track.Property] = Math.Max(maxError.TryGetValue(track.Property, out var worst) ? worst : 0f, error);
                        if (failure == null && error > Tolerance(track.Property))
                            failure = $"{track.Property} of '{benchmarkCase.Paths[track.Object]}' on instance {sampled[s]} " +
                                      $"is off by {error:G4} at {time:0.###} s (tolerance {Tolerance(track.Property):G3}); expected {expected}.";
                    }
                }
            }

            var errors = new JObject();
            var tolerances = new JObject();
            foreach (var pair in maxError)
            {
                errors[pair.Key.ToString()] = pair.Value;
                tolerances[pair.Key.ToString()] = Tolerance(pair.Key);
            }
            record["validation"] = new JObject
            {
                ["instances"] = sampled.Count,
                ["times"] = new JArray(benchmarkCase.ValidationTimes),
                ["maxError"] = errors,
                ["tolerance"] = tolerances
            };
            if (failure != null) throw new InvalidOperationException(failure);
        }

        /// <summary>
        /// Releases and destroys every instance and drops every reference to them, so memory read afterwards holds
        /// only what the participant itself keeps.
        /// </summary>
        public void Teardown()
        {
            if (tornDown) return;
            tornDown = true;
            foreach (var instance in instances)
                if (instance != null)
                    participant.Release(instance);
            Array.Clear(instances, 0, instances.Length);
            cells = null;
            if (root != null) Object.Destroy(root);
            root = null;
        }

        AnimationBenchmarkInstance Spawn(AnimationBenchmarkInstance instance, int slot, int index)
        {
            var instantiating = Stopwatch.GetTimestamp();
            var parent = cells != null ? cells[slot] : root.transform;
            var created = Object.Instantiate(prefab, parent, false);
            if (benchmarkCase.Rig == AnimationBenchmarkRig.Sprite)
                created.transform.localPosition = new Vector3((slot % SpriteColumns - (SpriteColumns - 1) * 0.5f) * 0.42f,
                    (slot / SpriteColumns - 12f) * 0.4f, 0f);
            instance.Root = created;
            instance.Seed = index;
            instance.Current = 0;
            participant.Bind(instance);
            var starting = Stopwatch.GetTimestamp();
            instantiateTicks += starting - instantiating;

            var length = benchmarkCase.Clips[0].Length;
            var offset = AnimationBenchmarkSpec.Unit(seed, index) * length;
            switch (benchmarkCase.Drive)
            {
                case AnimationBenchmarkDrive.Seek:
                    participant.Start(instance, 0f);
                    participant.Freeze(instance);
                    instance.Phase = offset;
                    participant.Seek(instance, offset);
                    break;
                case AnimationBenchmarkDrive.CrossFade:
                    participant.Start(instance, offset);
                    instance.NextFade = startTime + AnimationBenchmarkSpec.Unit(seed ^ 0x5BD1E995u, index) *
                        AnimationBenchmarkSpec.CrossFadeInterval;
                    break;
                default:
                    participant.Start(instance, offset);
                    break;
            }
            startTicks += Stopwatch.GetTimestamp() - starting;
            return instance;
        }

        void BuildCanvas()
        {
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var rows = (instances.Length + CanvasColumns - 1) / CanvasColumns;
            var size = new Vector2(1920f / CanvasColumns, 1080f / rows);
            cells = new RectTransform[instances.Length];
            for (var i = 0; i < cells.Length; i++)
            {
                var cell = new GameObject("Cell", typeof(RectTransform)).GetComponent<RectTransform>();
                cell.SetParent(root.transform, false);
                cell.sizeDelta = size;
                cell.anchoredPosition = new Vector2((i % CanvasColumns + 0.5f) * size.x - 960f,
                    540f - (i / CanvasColumns + 0.5f) * size.y);
                cells[i] = cell;
            }
        }

        List<int> SampledIndices()
        {
            var count = Math.Min(SampledInstances, instances.Length);
            var sampled = new List<int>(count);
            for (var k = 0; k < count; k++)
                sampled.Add(k * instances.Length / count);
            return sampled;
        }

        Transform[] Objects(AnimationBenchmarkInstance instance)
        {
            var paths = benchmarkCase.Paths;
            var objects = new Transform[paths.Length];
            var rootTransform = instance.Root.transform;
            for (var i = 0; i < paths.Length; i++)
                objects[i] = paths[i].Length == 0
                    ? rootTransform
                    : rootTransform.Find(paths[i]) ??
                      throw new InvalidOperationException($"'{instance.Root.name}' has no object at '{paths[i]}'.");
            return objects;
        }

        List<(Transform target, AnimationBenchmarkProperty property)> Probes()
        {
            var probes = new List<(Transform, AnimationBenchmarkProperty)>();
            foreach (var index in SampledIndices())
            {
                var objects = Objects(instances[index]);
                foreach (var track in benchmarkCase.Clips[0].Tracks)
                    probes.Add((objects[track.Object], track.Property));
            }
            return probes;
        }

        void Read(List<(Transform target, AnimationBenchmarkProperty property)> probes, float[] values)
        {
            for (var i = 0; i < probes.Count; i++)
            {
                var (target, property) = probes[i];
                var value = property == AnimationBenchmarkProperty.LocalEuler
                    ? (Vector4)new Vector4(target.localRotation.x, target.localRotation.y, target.localRotation.z,
                        target.localRotation.w)
                    : (Vector4)Value(property, target);
                for (var c = 0; c < 4; c++)
                    values[i * 4 + c] = value[c];
            }
        }

        Vector3 Value(AnimationBenchmarkProperty property, Transform target) => property switch
        {
            AnimationBenchmarkProperty.LocalPosition => target.localPosition,
            AnimationBenchmarkProperty.LocalEuler => target.localEulerAngles,
            AnimationBenchmarkProperty.LocalScale => target.localScale,
            AnimationBenchmarkProperty.AnchoredPosition => ((RectTransform)target).anchoredPosition,
            AnimationBenchmarkProperty.GraphicAlpha => new Vector3(target.GetComponent<Graphic>().color.a, 0f),
            AnimationBenchmarkProperty.SpriteAlpha => new Vector3(target.GetComponent<SpriteRenderer>().color.a, 0f),
            AnimationBenchmarkProperty.Sprite =>
                new Vector3(Array.IndexOf(content.Sprites, target.GetComponent<SpriteRenderer>().sprite), 0f),
            AnimationBenchmarkProperty.Active => new Vector3(target.gameObject.activeSelf ? 1f : 0f, 0f),
            _ => throw new ArgumentOutOfRangeException(nameof(property), property, null),
        };

        float Error(AnimationBenchmarkProperty property, Transform target, Vector3 expected)
        {
            if (property == AnimationBenchmarkProperty.LocalEuler)
                return Degrees(Quaternion.Euler(expected), target.localRotation);
            var actual = Value(property, target);
            var error = 0f;
            for (var c = 0; c < AnimationBenchmarkSpec.Components(property); c++)
                error = Math.Max(error, Mathf.Abs(actual[c] - expected[c]));
            return error;
        }

        /// <summary>
        /// Degrees one rotation turns from the other, down to float precision; <c>Quaternion.Angle</c> reads every turn
        /// under about 0.16° as none.
        /// </summary>
        static float Degrees(Quaternion a, Quaternion b)
        {
            var from = new Vector4(a.x, a.y, a.z, a.w);
            var to = new Vector4(b.x, b.y, b.z, b.w);
            if (Vector4.Dot(from, to) < 0f) to = -to;
            return 4f * Mathf.Atan2((from - to).magnitude, (from + to).magnitude) * Mathf.Rad2Deg;
        }

        AnimationBenchmarkCurve[][][] Curves()
        {
            var curves = new AnimationBenchmarkCurve[benchmarkCase.Clips.Length][][];
            for (var clip = 0; clip < curves.Length; clip++)
            {
                var tracks = benchmarkCase.Clips[clip].Tracks;
                curves[clip] = new AnimationBenchmarkCurve[tracks.Length][];
                for (var t = 0; t < tracks.Length; t++)
                {
                    curves[clip][t] = new AnimationBenchmarkCurve[tracks[t].Components];
                    for (var c = 0; c < tracks[t].Components; c++)
                        curves[clip][t][c] = AnimationBenchmarkSpec.Curve(owner, clip, t, c);
                }
            }
            return curves;
        }

        static float Tolerance(AnimationBenchmarkProperty property) => property switch
        {
            AnimationBenchmarkProperty.LocalPosition or AnimationBenchmarkProperty.LocalScale => 1e-3f,
            AnimationBenchmarkProperty.AnchoredPosition => 2e-2f,
            AnimationBenchmarkProperty.LocalEuler => 0.1f,
            AnimationBenchmarkProperty.GraphicAlpha or AnimationBenchmarkProperty.SpriteAlpha => 2e-3f,
            _ => 0f,
        };
    }
}
