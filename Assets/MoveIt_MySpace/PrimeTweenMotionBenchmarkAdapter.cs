using System;
using System.Collections.Generic;
using LightSide.Benchmark;
using PrimeTween;
using UnityEngine;

/// <summary>Direct adapter for PrimeTween 1.4.11.</summary>
[Serializable]
public sealed class PrimeTweenMotionBenchmarkAdapter : MotionBenchmarkAdapter
{
    const string engine = "PrimeTween";
    static readonly string[] markers = { "UpdateAndCheckIfRunning" };
    static readonly Action<KeyedState, float> setKeyedValue =
        static (state, value) => state.Values[state.Index] = value;

    /// <inheritdoc/>
    public override string Name => "primeTween";

    /// <inheritdoc/>
    public override MotionBenchmarkAdapterMetadata Metadata => new(
        "1.4.11",
        "npm:com.kyrylokuzyk.primetween@1.4.11",
        "directPublicApi",
        "SetTweensCapacityExactIsolatedLiveCount",
        "preallocatedTweenAndSequenceHandleArrays");

    /// <inheritdoc/>
    public override IReadOnlyList<string> ProfilerMarkers => markers;

    static bool engineConfigured;

    /// <summary>
    /// Silences the convenience diagnostics PrimeTween runs by default. Each one inspects every tween
    /// as it starts, so leaving them on measures the diagnostics as much as the engine; the vendor
    /// directs benchmarks to turn them off. They cover only the warnings — the assertions themselves
    /// are compiled in unless <c>PRIME_TWEEN_DISABLE_ASSERTIONS</c> is defined for the build, and
    /// without that define this engine is measured with a handicap the others do not carry.
    /// </summary>
    static void ConfigureEngine()
    {
        if (engineConfigured) return;
        engineConfigured = true;
        PrimeTweenConfig.warnZeroDuration = false;
        PrimeTweenConfig.warnTweenOnDisabledTarget = false;
        PrimeTweenConfig.warnBenchmarkWithAsserts = false;
    }

    /// <inheritdoc/>
    public override string UnsupportedReason(in MotionBenchmarkSpec spec) => spec.UnsupportedBaselineTimingReason();

    /// <inheritdoc/>
    public override MotionBenchmarkContext Start(in MotionBenchmarkRequest request)
    {
        RequireIsolated();
        ConfigureEngine();
        if (request.Spec.TotalLiveCoreCount > 0)
            PrimeTweenConfig.SetTweensCapacity(request.Spec.TotalLiveCoreCount);

        return request.Spec.Workload switch
        {
            MotionBenchmarkWorkload.EmptyFrame => PrimeTweenContext.ForMotions(request),
            MotionBenchmarkWorkload.SingleTransformPosition => StartShared(request),
            MotionBenchmarkWorkload.SharedTransformPosition => StartShared(request),
            MotionBenchmarkWorkload.KeyedManagedFloats => StartKeyed(request),
            MotionBenchmarkWorkload.DistinctTransformPositions => StartDistinct(request),
            MotionBenchmarkWorkload.Sequences => StartSequences(request),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Spec.Workload, null)
        };
    }

    /// <inheritdoc/>
    public override MotionBenchmarkCreationContext PrepareCreation(in MotionBenchmarkRequest request)
    {
        if (request.Spec.Workload != MotionBenchmarkWorkload.Creation)
            throw new ArgumentException("A creation context requires the creation workload.", nameof(request));
        RequireIsolated();
        PrimeTweenConfig.SetTweensCapacity(request.Spec.MotionCount);
        return new PrimeTweenCreationContext(request.SharedTransform, request.Spec,
            PositionSettings(request.Spec));
    }

    static MotionBenchmarkContext StartShared(in MotionBenchmarkRequest request)
    {
        var context = PrimeTweenContext.ForMotions(request);
        var settings = PositionSettings(request.Spec);
        try
        {
            for (int i = 0; i < request.Spec.MotionCount; i++)
                context.Add(Tween.Position(request.SharedTransform, settings));
            return context;
        }
        catch (Exception exception)
        {
            DisposeAfterStartFailure(context, exception);
            throw;
        }
    }

    static MotionBenchmarkContext StartKeyed(in MotionBenchmarkRequest request)
    {
        var states = CreateStates(request.ManagedValues, request.Spec.MotionCount);
        var context = PrimeTweenContext.ForMotions(request, states);
        var settings = FloatSettings(request.Spec);
        try
        {
            for (int i = 0; i < request.Spec.MotionCount; i++)
                context.Add(Tween.Custom(states[i], settings, setKeyedValue));
            return context;
        }
        catch (Exception exception)
        {
            DisposeAfterStartFailure(context, exception);
            throw;
        }
    }

    static MotionBenchmarkContext StartDistinct(in MotionBenchmarkRequest request)
    {
        var context = PrimeTweenContext.ForMotions(request);
        var settings = PositionSettings(request.Spec);
        try
        {
            for (int i = 0; i < request.Spec.MotionCount; i++)
                context.Add(Tween.Position(request.DistinctTransforms[i], settings));
            return context;
        }
        catch (Exception exception)
        {
            DisposeAfterStartFailure(context, exception);
            throw;
        }
    }

    static MotionBenchmarkContext StartSequences(in MotionBenchmarkRequest request)
    {
        var states = CreateStates(request.ManagedValues, request.Spec.MotionCount);
        var context = PrimeTweenContext.ForSequences(request, states);
        var settings = FloatSettings(request.Spec);
        try
        {
            int key = 0;
            for (int i = 0; i < request.Spec.SequenceCount; i++)
            {
                var sequence = Sequence.Create(request.Spec.Cycles,
                    Sequence.SequenceCycleMode.Restart, Ease.Linear, false, UpdateType.Update);
                context.Add(sequence);
                for (int child = 0; child < request.Spec.SequenceLength; child++, key++)
                {
                    var motion = Tween.Custom(states[key], settings, setKeyedValue);
                    context.Add(motion);
                    sequence.Chain(motion);
                }
            }
            return context;
        }
        catch (Exception exception)
        {
            DisposeAfterStartFailure(context, exception);
            throw;
        }
    }

    static TweenSettings<Vector3> PositionSettings(in MotionBenchmarkSpec spec) => new(
        Vector3.one * spec.From,
        Vector3.one * spec.To,
        spec.DurationSeconds,
        Ease.Linear,
        spec.Cycles,
        CycleMode.Restart,
        0f,
        0f,
        false,
        UpdateType.Update);

    static TweenSettings<float> FloatSettings(in MotionBenchmarkSpec spec) => new(
        spec.From,
        spec.To,
        spec.DurationSeconds,
        Ease.Linear,
        spec.Cycles,
        CycleMode.Restart,
        0f,
        0f,
        false,
        UpdateType.Update);

    static KeyedState[] CreateStates(float[] values, int count)
    {
        var states = new KeyedState[count];
        for (int i = 0; i < count; i++)
            states[i] = new KeyedState(values, i);
        return states;
    }

    static void RequireIsolated()
    {
        int count = Tween.GetTweensCount();
        if (count != 0)
            throw new InvalidOperationException($"{engine} benchmark requires no pre-existing live tweens, but found {count}.");
    }

    static void DisposeAfterStartFailure(MotionBenchmarkContext context, Exception primary)
    {
        try
        {
            context.Dispose();
        }
        catch (Exception cleanup)
        {
            throw new BenchmarkCleanupException("PrimeTween adapter setup cleanup failed.",
                new AggregateException(primary, cleanup));
        }
    }

    sealed class KeyedState
    {
        internal KeyedState(float[] values, int index)
        {
            Values = values;
            Index = index;
        }

        internal float[] Values { get; }
        internal int Index { get; }
    }

    sealed class PrimeTweenContext : MotionBenchmarkContext
    {
        readonly MotionBenchmarkRequest request;
        readonly Tween[] motions;
        readonly Sequence[] sequences;
        readonly KeyedState[] keyedStates;
        readonly MotionBenchmarkOutputProbe output;
        int motionCount;
        int sequenceCount;

        PrimeTweenContext(in MotionBenchmarkRequest request, Tween[] motions,
            Sequence[] sequences, KeyedState[] keyedStates)
        {
            this.request = request;
            this.motions = motions;
            this.sequences = sequences;
            this.keyedStates = keyedStates;
            output = new MotionBenchmarkOutputProbe(request, engine);
        }

        internal static PrimeTweenContext ForMotions(in MotionBenchmarkRequest request,
            KeyedState[] keyedStates = null) =>
            new(request, new Tween[request.Spec.MotionCount], null, keyedStates);

        internal static PrimeTweenContext ForSequences(in MotionBenchmarkRequest request,
            KeyedState[] keyedStates) =>
            new(request, new Tween[request.Spec.MotionCount],
                new Sequence[request.Spec.SequenceCount], keyedStates);

        internal void Add(Tween motion) => motions[motionCount++] = motion;

        internal void Add(Sequence sequence) => sequences[sequenceCount++] = sequence;

        public override void CaptureOutputBaseline() => output.CaptureBaseline(motionCount);

        public override void Validate()
        {
            if (motionCount != request.Spec.MotionCount)
                throw new InvalidOperationException($"{engine} created {motionCount} of {request.Spec.MotionCount} requested tweens.");
            if (sequenceCount != request.Spec.SequenceCount)
                throw new InvalidOperationException($"{engine} created {sequenceCount} of {request.Spec.SequenceCount} requested sequences.");
            if (keyedStates != null && keyedStates.Length != motionCount)
                throw new InvalidOperationException($"{engine} retained {keyedStates.Length} keyed states for {motionCount} tweens.");

            int globalCount = Tween.GetTweensCount();
            if (globalCount != request.Spec.TotalLiveCoreCount)
                throw new InvalidOperationException($"{engine} has {globalCount} live tweens instead of {request.Spec.TotalLiveCoreCount}.");

            for (int i = 0; i < motionCount; i++)
            {
                if (!motions[i].isAlive)
                    throw new InvalidOperationException($"{engine} tween {i} ended before the validation boundary.");
                MotionBenchmarkValidation.RequireEqual(motions[i].duration,
                    request.Spec.DurationSeconds, engine, "tween duration", i);
                MotionBenchmarkValidation.RequireEqual(motions[i].durationTotal,
                    request.Spec.DurationSeconds, engine, "tween total duration", i);
            }
            for (int i = 0; i < sequenceCount; i++)
                if (!sequences[i].isAlive)
                    throw new InvalidOperationException($"{engine} sequence {i} ended before the validation boundary.");

            output.ValidateCurrent(motionCount);
            if (request.Spec.Workload == MotionBenchmarkWorkload.Sequences)
                ValidateSequences();
        }

        public override void ValidateAdvancement() =>
            output.ValidateAdvancement(motionCount, sequenceCount);

        void ValidateSequences()
        {
            float expectedDuration = request.Spec.DurationSeconds * request.Spec.SequenceLength;
            for (int i = 0; i < sequenceCount; i++)
            {
                MotionBenchmarkValidation.RequireEqual(sequences[i].duration, expectedDuration,
                    engine, "sequence duration", i);
                MotionBenchmarkValidation.RequireEqual(sequences[i].durationTotal, expectedDuration,
                    engine, "sequence total duration", i);
            }
        }

        public override void Dispose()
        {
            Exception failure = null;
            if (sequences != null)
            {
                for (int i = sequenceCount - 1; i >= 0; i--)
                {
                    var sequence = sequences[i];
                    sequences[i] = default;
                    failure = CaptureStop(failure, sequence);
                }
            }
            for (int i = motionCount - 1; i >= 0; i--)
            {
                var motion = motions[i];
                motions[i] = default;
                failure = CaptureStop(failure, motion);
            }
            motionCount = 0;
            sequenceCount = 0;
            if (keyedStates != null)
                Array.Clear(keyedStates, 0, keyedStates.Length);
            if (failure != null)
                throw failure;
        }
    }

    sealed class PrimeTweenCreationContext : MotionBenchmarkCreationContext
    {
        public override int LiveCount => count;

        readonly Transform target;
        readonly Tween[] motions;
        readonly TweenSettings<Vector3> settings;
        readonly MotionBenchmarkSpec spec;
        int count;

        internal PrimeTweenCreationContext(Transform target, in MotionBenchmarkSpec spec,
            in TweenSettings<Vector3> settings)
        {
            this.target = target;
            motions = new Tween[spec.MotionCount];
            this.settings = settings;
            this.spec = spec;
        }

        public override void CreateBatch()
        {
            if (count != 0)
                throw new InvalidOperationException("The previous creation batch is still live.");
            try
            {
                for (; count < motions.Length; count++)
                    motions[count] = Tween.Position(target, settings);
            }
            catch (Exception primary)
            {
                try
                {
                    ClearBatch();
                }
                catch (Exception cleanup)
                {
                    throw new BenchmarkCleanupException("PrimeTween creation setup cleanup failed.",
                        new AggregateException(primary, cleanup));
                }
                throw;
            }
        }

        public override void ValidateBatch()
        {
            if (count != spec.MotionCount)
                throw new InvalidOperationException($"{engine} created {count} of {spec.MotionCount} requested tweens.");
            int globalCount = Tween.GetTweensCount();
            if (globalCount != spec.MotionCount)
                throw new InvalidOperationException($"{engine} has {globalCount} live creation tweens instead of {spec.MotionCount}.");
            for (int i = 0; i < count; i++)
            {
                if (!motions[i].isAlive)
                    throw new InvalidOperationException($"{engine} creation tween {i} ended before cleanup.");
                MotionBenchmarkValidation.RequireEqual(motions[i].duration, spec.DurationSeconds,
                    engine, "creation tween duration", i);
                MotionBenchmarkValidation.RequireEqual(motions[i].durationTotal, spec.DurationSeconds,
                    engine, "creation tween total duration", i);
            }
        }

        public override void ClearBatch()
        {
            Exception failure = null;
            for (int i = count - 1; i >= 0; i--)
            {
                var motion = motions[i];
                motions[i] = default;
                failure = CaptureStop(failure, motion);
            }
            count = 0;
            if (failure != null)
                throw failure;
        }

        public override void Dispose() => ClearBatch();
    }

    static Exception CaptureStop(Exception failure, Sequence sequence)
    {
        try
        {
            if (sequence.isAlive)
                sequence.Stop();
            if (sequence.isAlive)
                throw new InvalidOperationException($"{engine} sequence remained alive after stopping.");
        }
        catch (Exception exception)
        {
            return failure == null ? exception : new AggregateException(failure, exception);
        }
        return failure;
    }

    static Exception CaptureStop(Exception failure, Tween motion)
    {
        try
        {
            if (motion.isAlive)
                motion.Stop();
            if (motion.isAlive)
                throw new InvalidOperationException($"{engine} tween remained alive after stopping.");
        }
        catch (Exception exception)
        {
            return failure == null ? exception : new AggregateException(failure, exception);
        }
        return failure;
    }

}
