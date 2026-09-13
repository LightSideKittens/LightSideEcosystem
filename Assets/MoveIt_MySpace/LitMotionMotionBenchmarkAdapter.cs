using System;
using System.Collections.Generic;
using LightSide.Benchmark;
using LitMotion;
using LitMotion.Adapters;
using LitMotion.Extensions;
using UnityEngine;

/// <summary>Direct adapter for LitMotion 2.0.2.</summary>
[Serializable]
public sealed class LitMotionMotionBenchmarkAdapter : MotionBenchmarkAdapter
{
    const string engine = "LitMotion";
    static readonly Action<float, KeyedState> setKeyedValue =
        static (value, state) => state.Values[state.Index] = value;
    static readonly Action<MotionBuilder<double, NoOptions, DoubleMotionAdapter>> configureSequence =
        ConfigureSequenceRoot;

    /// <inheritdoc/>
    public override string Name => "litMotion";

    /// <inheritdoc/>
    public override MotionBenchmarkAdapterMetadata Metadata => new(
        "2.0.2",
        "git:annulusgames/LitMotion@ab6e92bfe78ff911def2fd3c4e9c79bb5a186946",
        "directPublicApi",
        "EnsureStorageCapacityPerValueAdapterAcrossSchedulers",
        "preallocatedMotionHandleAndSequenceRootArrays");

    /// <inheritdoc/>
    public override IReadOnlyList<string> ProfilerMarkers => Array.Empty<string>();

    /// <inheritdoc/>
    public override string UnsupportedReason(in MotionBenchmarkSpec spec) => spec.UnsupportedBaselineTimingReason();

    /// <inheritdoc/>
    public override MotionBenchmarkContext Start(in MotionBenchmarkRequest request)
    {
        EnsureCapacity(request.Spec);
        return request.Spec.Workload switch
        {
            MotionBenchmarkWorkload.EmptyFrame => LitMotionContext.ForMotions(request),
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
        MotionDispatcher.EnsureStorageCapacity<Vector3, NoOptions, Vector3MotionAdapter>(
            request.Spec.MotionCount);
        return new LitMotionCreationContext(request.SharedTransform, request.Spec);
    }

    static MotionBenchmarkContext StartShared(in MotionBenchmarkRequest request)
    {
        var context = LitMotionContext.ForMotions(request);
        try
        {
            for (int i = 0; i < request.Spec.MotionCount; i++)
                context.Add(CreatePosition(request.SharedTransform, request.Spec));
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
        var context = LitMotionContext.ForMotions(request, states);
        try
        {
            for (int i = 0; i < request.Spec.MotionCount; i++)
                context.Add(CreateFloat(states[i], request.Spec));
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
        var context = LitMotionContext.ForMotions(request);
        try
        {
            for (int i = 0; i < request.Spec.MotionCount; i++)
                context.Add(CreatePosition(request.DistinctTransforms[i], request.Spec));
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
        var context = LitMotionContext.ForSequences(request, states);
        try
        {
            int key = 0;
            for (int i = 0; i < request.Spec.SequenceCount; i++)
            {
                var sequence = LSequence.Create();
                bool rootOwned = false;
                try
                {
                    for (int child = 0; child < request.Spec.SequenceLength; child++, key++)
                    {
                        var motion = CreateFloat(states[key], request.Spec);
                        context.Add(motion);
                        sequence.Append(motion);
                    }
                    context.AddSequence(sequence.Run(configureSequence));
                    rootOwned = true;
                }
                catch (Exception primary)
                {
                    if (!rootOwned)
                    {
                        try
                        {
                            context.AddSequence(sequence.Run(configureSequence));
                        }
                        catch (Exception cleanup)
                        {
                            throw new BenchmarkCleanupException(
                                "LitMotion sequence adoption cleanup failed.",
                                new AggregateException(primary, cleanup));
                        }
                    }
                    throw;
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

    static MotionHandle CreatePosition(Transform target, in MotionBenchmarkSpec spec) =>
        LMotion.Create(Vector3.one * spec.From, Vector3.one * spec.To, spec.DurationSeconds)
            .WithEase(Ease.Linear)
            .WithLoops(spec.Cycles, LoopType.Restart)
            .WithScheduler(MotionScheduler.Update)
            .BindToPosition(target);

    static MotionHandle CreateFloat(KeyedState state, in MotionBenchmarkSpec spec) =>
        LMotion.Create(spec.From, spec.To, spec.DurationSeconds)
            .WithEase(Ease.Linear)
            .WithLoops(spec.Cycles, LoopType.Restart)
            .WithScheduler(MotionScheduler.Update)
            .Bind(state, setKeyedValue);

    static void ConfigureSequenceRoot(MotionBuilder<double, NoOptions, DoubleMotionAdapter> builder) =>
        builder.WithEase(Ease.Linear)
            .WithLoops(1, LoopType.Restart)
            .WithScheduler(MotionScheduler.Update);

    static void EnsureCapacity(in MotionBenchmarkSpec spec)
    {
        switch (spec.Workload)
        {
            case MotionBenchmarkWorkload.EmptyFrame:
                return;
            case MotionBenchmarkWorkload.SingleTransformPosition:
            case MotionBenchmarkWorkload.SharedTransformPosition:
            case MotionBenchmarkWorkload.DistinctTransformPositions:
                MotionDispatcher.EnsureStorageCapacity<Vector3, NoOptions, Vector3MotionAdapter>(
                    spec.MotionCount);
                return;
            case MotionBenchmarkWorkload.KeyedManagedFloats:
                MotionDispatcher.EnsureStorageCapacity<float, NoOptions, FloatMotionAdapter>(spec.MotionCount);
                return;
            case MotionBenchmarkWorkload.Sequences:
                MotionDispatcher.EnsureStorageCapacity<float, NoOptions, FloatMotionAdapter>(spec.MotionCount);
                MotionDispatcher.EnsureStorageCapacity<double, NoOptions, DoubleMotionAdapter>(
                    spec.SequenceCount);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(spec), spec.Workload, null);
        }
    }

    static KeyedState[] CreateStates(float[] values, int count)
    {
        var states = new KeyedState[count];
        for (int i = 0; i < count; i++)
            states[i] = new KeyedState(values, i);
        return states;
    }

    static void DisposeAfterStartFailure(MotionBenchmarkContext context, Exception primary)
    {
        try
        {
            context.Dispose();
        }
        catch (Exception cleanup)
        {
            throw new BenchmarkCleanupException("LitMotion adapter setup cleanup failed.",
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

    sealed class LitMotionContext : MotionBenchmarkContext
    {
        readonly MotionBenchmarkRequest request;
        readonly MotionHandle[] motions;
        readonly MotionHandle[] sequences;
        readonly KeyedState[] keyedStates;
        readonly MotionBenchmarkOutputProbe output;
        int motionCount;
        int sequenceCount;

        LitMotionContext(in MotionBenchmarkRequest request, MotionHandle[] motions,
            MotionHandle[] sequences, KeyedState[] keyedStates)
        {
            this.request = request;
            this.motions = motions;
            this.sequences = sequences;
            this.keyedStates = keyedStates;
            output = new MotionBenchmarkOutputProbe(request, engine);
        }

        internal static LitMotionContext ForMotions(in MotionBenchmarkRequest request,
            KeyedState[] keyedStates = null) =>
            new(request, new MotionHandle[request.Spec.MotionCount], null, keyedStates);

        internal static LitMotionContext ForSequences(in MotionBenchmarkRequest request,
            KeyedState[] keyedStates) =>
            new(request, new MotionHandle[request.Spec.MotionCount],
                new MotionHandle[request.Spec.SequenceCount], keyedStates);

        internal void Add(MotionHandle motion) => motions[motionCount++] = motion;

        internal void AddSequence(MotionHandle sequence) => sequences[sequenceCount++] = sequence;

        public override void CaptureOutputBaseline() => output.CaptureBaseline(motionCount);

        public override void Validate()
        {
            if (motionCount != request.Spec.MotionCount)
                throw new InvalidOperationException($"{engine} created {motionCount} of {request.Spec.MotionCount} requested motions.");
            if (sequenceCount != request.Spec.SequenceCount)
                throw new InvalidOperationException($"{engine} created {sequenceCount} of {request.Spec.SequenceCount} requested sequences.");
            if (keyedStates != null && keyedStates.Length != motionCount)
                throw new InvalidOperationException($"{engine} retained {keyedStates.Length} keyed states for {motionCount} motions.");

            for (int i = 0; i < motionCount; i++)
            {
                if (!motions[i].IsActive())
                    throw new InvalidOperationException($"{engine} motion {i} ended before the validation boundary.");
                MotionBenchmarkValidation.RequireEqual(motions[i].Duration,
                    request.Spec.DurationSeconds, engine, "motion duration", i);
                MotionBenchmarkValidation.RequireEqual(motions[i].TotalDuration,
                    request.Spec.DurationSeconds, engine, "motion total duration", i);
            }
            for (int i = 0; i < sequenceCount; i++)
                if (!sequences[i].IsActive())
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
                MotionBenchmarkValidation.RequireEqual(sequences[i].Duration, expectedDuration,
                    engine, "sequence duration", i);
                MotionBenchmarkValidation.RequireEqual(sequences[i].TotalDuration, expectedDuration,
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
                    failure = CaptureCancel(failure, sequence, "sequence", i);
                }
            }
            for (int i = motionCount - 1; i >= 0; i--)
            {
                var motion = motions[i];
                motions[i] = default;
                failure = CaptureCancel(failure, motion, "motion", i);
            }
            motionCount = 0;
            sequenceCount = 0;
            if (keyedStates != null)
                Array.Clear(keyedStates, 0, keyedStates.Length);
            if (failure != null)
                throw failure;
        }
    }

    sealed class LitMotionCreationContext : MotionBenchmarkCreationContext
    {
        public override int LiveCount => count;

        readonly Transform target;
        readonly MotionHandle[] motions;
        readonly MotionBenchmarkSpec spec;
        int count;

        internal LitMotionCreationContext(Transform target, in MotionBenchmarkSpec spec)
        {
            this.target = target;
            motions = new MotionHandle[spec.MotionCount];
            this.spec = spec;
        }

        public override void CreateBatch()
        {
            if (count != 0)
                throw new InvalidOperationException("The previous creation batch is still live.");
            try
            {
                for (; count < motions.Length; count++)
                    motions[count] = CreatePosition(target, spec);
            }
            catch (Exception primary)
            {
                try
                {
                    ClearBatch();
                }
                catch (Exception cleanup)
                {
                    throw new BenchmarkCleanupException("LitMotion creation setup cleanup failed.",
                        new AggregateException(primary, cleanup));
                }
                throw;
            }
        }

        public override void ValidateBatch()
        {
            if (count != spec.MotionCount)
                throw new InvalidOperationException($"{engine} created {count} of {spec.MotionCount} requested motions.");
            for (int i = 0; i < count; i++)
            {
                if (!motions[i].IsActive())
                    throw new InvalidOperationException($"{engine} creation motion {i} ended before cleanup.");
                MotionBenchmarkValidation.RequireEqual(motions[i].Duration, spec.DurationSeconds,
                    engine, "creation motion duration", i);
                MotionBenchmarkValidation.RequireEqual(motions[i].TotalDuration, spec.DurationSeconds,
                    engine, "creation motion total duration", i);
            }
        }

        public override void ClearBatch()
        {
            Exception failure = null;
            for (int i = count - 1; i >= 0; i--)
            {
                var motion = motions[i];
                motions[i] = default;
                failure = CaptureCancel(failure, motion, "creation motion", i);
            }
            count = 0;
            if (failure != null)
                throw failure;
        }

        public override void Dispose() => ClearBatch();
    }

    static Exception CaptureCancel(Exception failure, MotionHandle handle, string role, int index)
    {
        try
        {
            if (handle.IsActive())
                handle.Cancel();
            if (handle.IsActive())
                throw new InvalidOperationException($"{engine} {role} {index} remained active after cancellation.");
        }
        catch (Exception exception)
        {
            return failure == null ? exception : new AggregateException(failure, exception);
        }
        return failure;
    }
}
