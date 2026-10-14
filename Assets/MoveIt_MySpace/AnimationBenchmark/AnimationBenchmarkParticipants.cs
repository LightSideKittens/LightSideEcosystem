using System;
using LightSide;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>One live copy of a case's rig and what its participant keeps about it between frames.</summary>
public sealed class AnimationBenchmarkInstance
{
    /// <summary>The instantiated prefab.</summary>
    public GameObject Root;

    /// <summary>Index the instance's seeded time offset derives from.</summary>
    public int Seed;

    /// <summary>Clip the base layer plays or fades toward.</summary>
    public int Current;

    /// <summary>Game time of the instance's next cross-fade.</summary>
    public float NextFade;

    /// <summary>Clip time a seeking script starts the instance at.</summary>
    public float Phase;

    internal MoveItPlayer Player;
    internal MoveItPlayback Base;
    internal MoveItPlayback Layer;
    internal Animator Animator;
    internal Animation Animation;
    internal AnimationState[] States;
    internal PlayableGraph Graph;
    internal AnimationClipPlayable[] Clips;
    internal AnimationMixerPlayable Mixer;
    internal float FadeStart;
    internal bool Fading;
}

/// <summary>
/// One animation system under comparison: how it finds its component on an instance, starts the case's clips at an
/// offset, cross-fades, seeks, and holds a fixed time for the result check. Every operation uses the system's own
/// public API the way a project would.
/// </summary>
public abstract class AnimationBenchmarkParticipant
{
    /// <summary>The case being measured.</summary>
    protected AnimationBenchmarkCase Case { get; private set; }

    /// <summary>The case's generated content.</summary>
    protected AnimationBenchmarkContent.Entry Content { get; private set; }

    /// <summary>Token the participant answers to in results and in a participant selection.</summary>
    public abstract string Key { get; }

    /// <summary>Every participant, in their default order.</summary>
    public static AnimationBenchmarkParticipant[] All() => new AnimationBenchmarkParticipant[]
    {
        new MoveItParticipant(), new MoveItSharedParticipant(), new AnimatorParticipant(), new LegacyParticipant(),
        new PlayablesParticipant(),
    };

    /// <summary>The participant's prefab for the case's content.</summary>
    public abstract GameObject Prefab(in AnimationBenchmarkContent.Entry entry);

    /// <summary>Why the participant cannot represent <paramref name="benchmarkCase"/>, or null when it can.</summary>
    public virtual string Unsupported(AnimationBenchmarkCase benchmarkCase) => null;

    /// <summary>Prepares for instances of <paramref name="benchmarkCase"/> playing <paramref name="entry"/>.</summary>
    public virtual void Begin(AnimationBenchmarkCase benchmarkCase, in AnimationBenchmarkContent.Entry entry)
    {
        Case = benchmarkCase;
        Content = entry;
    }

    /// <summary>Finds what the participant drives on a freshly instantiated instance.</summary>
    public abstract void Bind(AnimationBenchmarkInstance instance);

    /// <summary>
    /// Plays the base clip from <paramref name="offset"/> seconds, and the layer clip over it when the case has one,
    /// replacing whatever played.
    /// </summary>
    public abstract void Start(AnimationBenchmarkInstance instance, float offset);

    /// <summary>Fades the base layer from what it plays to clip <paramref name="clip"/> started from its beginning.</summary>
    public abstract void CrossFade(AnimationBenchmarkInstance instance, int clip, float duration);

    /// <summary>Stops the base clip's time from advancing on its own, ahead of <see cref="Seek"/> calls.</summary>
    public abstract void Freeze(AnimationBenchmarkInstance instance);

    /// <summary>Shows the frozen base clip at <paramref name="time"/> seconds.</summary>
    public abstract void Seek(AnimationBenchmarkInstance instance, float time);

    /// <summary>
    /// Replaces everything with the base clip, and the layer clip over it, both held at <paramref name="time"/>
    /// seconds; values reach the objects by the end of the next frame at the latest.
    /// </summary>
    public abstract void Hold(AnimationBenchmarkInstance instance, float time);

    /// <summary>Per-frame work the system leaves to the script, such as the weights of a cross-fade.</summary>
    public virtual void Advance(AnimationBenchmarkInstance instance, float now) { }

    /// <summary>Frees what <see cref="Bind"/> created beside the instance's objects.</summary>
    public virtual void Release(AnimationBenchmarkInstance instance) { }

    /// <summary>The <typeparamref name="T"/> on the instance's root.</summary>
    /// <exception cref="InvalidOperationException">The prefab carries none.</exception>
    protected static T Require<T>(AnimationBenchmarkInstance instance) where T : Component =>
        instance.Root.TryGetComponent<T>(out var component)
            ? component
            : throw new InvalidOperationException($"'{instance.Root.name}' has no {typeof(T).Name}.");

    /// <summary>Name of clip <paramref name="clip"/>.</summary>
    protected string NameOf(int clip) => Case.Clips[clip].Name;

    /// <summary>Length of clip <paramref name="clip"/> in seconds.</summary>
    protected float LengthOf(int clip) => Case.Clips[clip].Length;

    /// <summary>Time of the layer clip when the base clip is <paramref name="time"/> seconds in.</summary>
    protected float LayerTime(float time) =>
        Case.Clips[Case.LayerClip].Loop ? Mathf.Repeat(time, LengthOf(Case.LayerClip)) : time;
}

class MoveItParticipant : AnimationBenchmarkParticipant
{
    public override string Key => "moveit";

    public override GameObject Prefab(in AnimationBenchmarkContent.Entry entry) => entry.moveit;

    public override void Bind(AnimationBenchmarkInstance instance) => instance.Player = Require<MoveItPlayer>(instance);

    public override void Start(AnimationBenchmarkInstance instance, float offset)
    {
        instance.Base = instance.Player.Play(NameOf(0));
        if (offset > 0f) instance.Base.Time = offset;
        if (Case.LayerClip < 0) return;
        instance.Layer = instance.Player.Layer(1).Play(NameOf(Case.LayerClip));
        if (offset > 0f) instance.Layer.Time = LayerTime(offset);
    }

    public override void CrossFade(AnimationBenchmarkInstance instance, int clip, float duration) =>
        instance.Base = instance.Player.CrossFade(NameOf(clip), duration);

    public override void Freeze(AnimationBenchmarkInstance instance) => instance.Base.Pause();

    public override void Seek(AnimationBenchmarkInstance instance, float time) => instance.Base.Time = time;

    public override void Hold(AnimationBenchmarkInstance instance, float time)
    {
        instance.Base = instance.Player.Play(NameOf(0));
        instance.Base.Pause();
        instance.Base.Time = time;
        if (Case.LayerClip < 0) return;
        instance.Layer.Pause();
        instance.Layer.Time = LayerTime(time);
    }
}

sealed class MoveItSharedParticipant : MoveItParticipant
{
    public override string Key => "moveit-shared";

    public override GameObject Prefab(in AnimationBenchmarkContent.Entry entry) => entry.moveitShared;
}

sealed class AnimatorParticipant : AnimationBenchmarkParticipant
{
    int[] states = Array.Empty<int>();

    public override string Key => "animator";

    public override GameObject Prefab(in AnimationBenchmarkContent.Entry entry) => entry.animator;

    public override void Begin(AnimationBenchmarkCase benchmarkCase, in AnimationBenchmarkContent.Entry entry)
    {
        base.Begin(benchmarkCase, entry);
        states = new int[benchmarkCase.Clips.Length];
        for (var i = 0; i < states.Length; i++)
            states[i] = Animator.StringToHash(benchmarkCase.Clips[i].Name);
    }

    public override void Bind(AnimationBenchmarkInstance instance) => instance.Animator = Require<Animator>(instance);

    public override void Start(AnimationBenchmarkInstance instance, float offset)
    {
        instance.Animator.speed = 1f;
        instance.Animator.Play(states[0], 0, offset / LengthOf(0));
        if (Case.LayerClip >= 0)
            instance.Animator.Play(states[Case.LayerClip], 1, LayerTime(offset) / LengthOf(Case.LayerClip));
    }

    public override void CrossFade(AnimationBenchmarkInstance instance, int clip, float duration) =>
        instance.Animator.CrossFadeInFixedTime(states[clip], duration, 0, 0f);

    public override void Freeze(AnimationBenchmarkInstance instance) => instance.Animator.speed = 0f;

    public override void Seek(AnimationBenchmarkInstance instance, float time) =>
        instance.Animator.Play(states[0], 0, time / LengthOf(0));

    public override void Hold(AnimationBenchmarkInstance instance, float time)
    {
        instance.Animator.speed = 0f;
        instance.Animator.Play(states[0], 0, time / LengthOf(0));
        if (Case.LayerClip >= 0)
            instance.Animator.Play(states[Case.LayerClip], 1, LayerTime(time) / LengthOf(Case.LayerClip));
    }
}

sealed class LegacyParticipant : AnimationBenchmarkParticipant
{
    string layerRoot;

    public override string Key => "legacy";

    public override GameObject Prefab(in AnimationBenchmarkContent.Entry entry) => entry.legacy;

    public override string Unsupported(AnimationBenchmarkCase benchmarkCase)
    {
        foreach (var clip in benchmarkCase.Clips)
            foreach (var track in clip.Tracks)
                if (track.Property == AnimationBenchmarkProperty.Sprite)
                    return "Legacy Animation plays no object reference curves such as sprites; Unity's AnimationClip " +
                           "reference names the Animator as the only component that does.";
        return null;
    }

    public override void Begin(AnimationBenchmarkCase benchmarkCase, in AnimationBenchmarkContent.Entry entry)
    {
        base.Begin(benchmarkCase, entry);
        layerRoot = benchmarkCase.LayerClip >= 0 ? benchmarkCase.Paths[benchmarkCase.LayerRoot] : null;
    }

    public override void Bind(AnimationBenchmarkInstance instance)
    {
        instance.Animation = Require<Animation>(instance);
        if (instance.States == null || instance.States.Length != Case.Clips.Length)
            instance.States = new AnimationState[Case.Clips.Length];
        for (var i = 0; i < instance.States.Length; i++)
            instance.States[i] = instance.Animation[NameOf(i)] ??
                                 throw new InvalidOperationException($"'{instance.Root.name}' has no clip '{NameOf(i)}'.");
        if (Case.LayerClip < 0) return;
        var upper = instance.States[Case.LayerClip];
        upper.layer = 1;
        upper.AddMixingTransform(instance.Root.transform.Find(layerRoot), true);
    }

    public override void Start(AnimationBenchmarkInstance instance, float offset)
    {
        var state = instance.States[0];
        instance.Animation.Play(NameOf(0));
        state.speed = 1f;
        state.time = offset;
        if (Case.LayerClip < 0) return;
        instance.Animation.Play(NameOf(Case.LayerClip));
        instance.States[Case.LayerClip].time = LayerTime(offset);
    }

    public override void CrossFade(AnimationBenchmarkInstance instance, int clip, float duration)
    {
        instance.States[clip].time = 0f;
        instance.Animation.CrossFade(NameOf(clip), duration);
    }

    public override void Freeze(AnimationBenchmarkInstance instance) => instance.States[0].speed = 0f;

    public override void Seek(AnimationBenchmarkInstance instance, float time) => instance.States[0].time = time;

    public override void Hold(AnimationBenchmarkInstance instance, float time)
    {
        var state = instance.States[0];
        instance.Animation.Play(NameOf(0));
        state.speed = 0f;
        state.time = time;
        if (Case.LayerClip < 0) return;
        var upper = instance.States[Case.LayerClip];
        upper.speed = 0f;
        upper.time = LayerTime(time);
    }
}

sealed class PlayablesParticipant : AnimationBenchmarkParticipant
{
    public override string Key => "playables";

    public override GameObject Prefab(in AnimationBenchmarkContent.Entry entry) => entry.playables;

    public override void Bind(AnimationBenchmarkInstance instance)
    {
        var animator = Require<Animator>(instance);
        var graph = PlayableGraph.Create("AnimationBenchmark");
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        var output = AnimationPlayableOutput.Create(graph, "Animation", animator);
        var clips = Content.clips;
        if (instance.Clips == null || instance.Clips.Length != clips.Length)
            instance.Clips = new AnimationClipPlayable[clips.Length];
        for (var i = 0; i < clips.Length; i++)
            instance.Clips[i] = AnimationClipPlayable.Create(graph, clips[i]);

        if (Case.Drive == AnimationBenchmarkDrive.CrossFade)
        {
            var mixer = AnimationMixerPlayable.Create(graph, 2);
            graph.Connect(instance.Clips[0], 0, mixer, 0);
            graph.Connect(instance.Clips[1], 0, mixer, 1);
            mixer.SetInputWeight(0, 1f);
            mixer.SetInputWeight(1, 0f);
            instance.Mixer = mixer;
            output.SetSourcePlayable(mixer);
        }
        else if (Case.LayerClip >= 0)
        {
            var layers = AnimationLayerMixerPlayable.Create(graph, 2);
            graph.Connect(instance.Clips[0], 0, layers, 0);
            graph.Connect(instance.Clips[Case.LayerClip], 0, layers, 1);
            layers.SetInputWeight(0, 1f);
            layers.SetInputWeight(1, 1f);
            layers.SetLayerMaskFromAvatarMask(1, Content.mask);
            output.SetSourcePlayable(layers);
        }
        else
        {
            output.SetSourcePlayable(instance.Clips[0]);
        }
        instance.Graph = graph;
        instance.Fading = false;
        graph.Play();
    }

    public override void Start(AnimationBenchmarkInstance instance, float offset)
    {
        var clip = instance.Clips[0];
        clip.SetTime(offset);
        clip.SetSpeed(1d);
        if (Case.LayerClip >= 0) instance.Clips[Case.LayerClip].SetTime(LayerTime(offset));
    }

    public override void CrossFade(AnimationBenchmarkInstance instance, int clip, float duration)
    {
        instance.Clips[clip].SetTime(0d);
        instance.FadeStart = Time.time;
        instance.Fading = true;
    }

    public override void Advance(AnimationBenchmarkInstance instance, float now)
    {
        if (!instance.Fading) return;
        var weight = Mathf.Clamp01((now - instance.FadeStart) / AnimationBenchmarkSpec.CrossFadeDuration);
        instance.Mixer.SetInputWeight(instance.Current, weight);
        instance.Mixer.SetInputWeight(instance.Current ^ 1, 1f - weight);
        if (weight >= 1f) instance.Fading = false;
    }

    public override void Freeze(AnimationBenchmarkInstance instance) => instance.Clips[0].SetSpeed(0d);

    public override void Seek(AnimationBenchmarkInstance instance, float time) => instance.Clips[0].SetTime(time);

    public override void Hold(AnimationBenchmarkInstance instance, float time)
    {
        instance.Fading = false;
        if (instance.Mixer.IsValid())
        {
            instance.Mixer.SetInputWeight(0, 1f);
            instance.Mixer.SetInputWeight(1, 0f);
        }
        var clip = instance.Clips[0];
        clip.SetSpeed(0d);
        clip.SetTime(time);
        if (Case.LayerClip < 0) return;
        var upper = instance.Clips[Case.LayerClip];
        upper.SetSpeed(0d);
        upper.SetTime(LayerTime(time));
    }

    public override void Release(AnimationBenchmarkInstance instance)
    {
        if (instance.Graph.IsValid()) instance.Graph.Destroy();
        instance.Mixer = default;
    }
}
