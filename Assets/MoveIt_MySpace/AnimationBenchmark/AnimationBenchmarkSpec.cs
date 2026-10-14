using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Value a benchmark track animates; every participant binds it to the same Unity property.</summary>
public enum AnimationBenchmarkProperty : byte
{
    /// <summary>Transform position relative to the parent: three components.</summary>
    LocalPosition,

    /// <summary>Transform rotation keyed as Euler degrees and applied in Unity's Z, X, Y order: three components.</summary>
    LocalEuler,

    /// <summary>Transform scale: three components.</summary>
    LocalScale,

    /// <summary>Rect transform anchored position: two components.</summary>
    AnchoredPosition,

    /// <summary>Opacity of a uGUI graphic's colour.</summary>
    GraphicAlpha,

    /// <summary>Opacity of a sprite renderer's colour.</summary>
    SpriteAlpha,

    /// <summary>Sprite a sprite renderer draws, switched frame by frame; the value indexes the content's sprites.</summary>
    Sprite,

    /// <summary>Whether the game object is active itself: one for active, zero for inactive.</summary>
    Active,
}

/// <summary>Hierarchy one benchmark instance consists of.</summary>
public enum AnimationBenchmarkRig : byte
{
    /// <summary>One empty game object.</summary>
    Single,

    /// <summary>A binary tree of empty bones under the root bone.</summary>
    Skeleton,

    /// <summary>One uGUI image.</summary>
    Image,

    /// <summary>One sprite renderer.</summary>
    Sprite,

    /// <summary>An empty parent with one empty child called <c>Child</c>.</summary>
    Toggle,
}

/// <summary>What the script does to the instances of a case while it is measured.</summary>
public enum AnimationBenchmarkDrive : byte
{
    /// <summary>Nothing: looping clips play on their own.</summary>
    Playback,

    /// <summary>Every instance cross-fades between its two clips once a second.</summary>
    CrossFade,

    /// <summary>Every instance is held at a time the script sets each frame.</summary>
    Seek,

    /// <summary>Instances are destroyed and created every frame.</summary>
    Spawn,

    /// <summary>A clip played once has finished and holds its last frame.</summary>
    Hold,

    /// <summary>Not timed: the memory a set of playing instances occupies.</summary>
    Memory,
}

/// <summary>One animated value of one object of a rig.</summary>
public readonly struct AnimationBenchmarkTrack
{
    /// <summary>Index of the object in <see cref="AnimationBenchmarkSpec.Paths"/> of the case's rig.</summary>
    public readonly int Object;

    /// <summary>The animated value.</summary>
    public readonly AnimationBenchmarkProperty Property;

    /// <summary>A track of <paramref name="property"/> on object <paramref name="objectIndex"/>.</summary>
    public AnimationBenchmarkTrack(int objectIndex, AnimationBenchmarkProperty property)
    {
        Object = objectIndex;
        Property = property;
    }

    /// <summary>Number of scalar curves the track keys.</summary>
    public int Components => AnimationBenchmarkSpec.Components(Property);
}

/// <summary>A named clip of a case: its length, whether it loops, and the tracks it keys.</summary>
public sealed class AnimationBenchmarkClip
{
    /// <summary>Name every participant plays the clip by.</summary>
    public readonly string Name;

    /// <summary>Seconds one pass lasts; the time of the clip's last key.</summary>
    public readonly float Length;

    /// <summary>Whether the clip repeats until stopped; a clip that does not loop holds its last frame.</summary>
    public readonly bool Loop;

    /// <summary>The tracks the clip keys.</summary>
    public readonly AnimationBenchmarkTrack[] Tracks;

    /// <summary>A clip called <paramref name="name"/> keying <paramref name="tracks"/>.</summary>
    public AnimationBenchmarkClip(string name, float length, bool loop, AnimationBenchmarkTrack[] tracks)
    {
        Name = name;
        Length = length;
        Loop = loop;
        Tracks = tracks;
    }

    /// <summary>Index of the track keying <paramref name="property"/> of object <paramref name="objectIndex"/>, or -1.</summary>
    public int TrackOf(int objectIndex, AnimationBenchmarkProperty property)
    {
        for (var i = 0; i < Tracks.Length; i++)
            if (Tracks[i].Object == objectIndex && Tracks[i].Property == property)
                return i;
        return -1;
    }

    /// <summary>Number of scalar curves the clip keys.</summary>
    public int CurveCount
    {
        get
        {
            var count = 0;
            foreach (var track in Tracks)
                count += track.Components;
            return count;
        }
    }
}

/// <summary>
/// Keys of one scalar curve: Hermite segments for continuous values, steps for discrete ones. Both clip formats
/// evaluate a key's slope identically — Unity's Hermite tangent and a MoveIt Bézier handle spanning a third of its
/// segment describe the same cubic — so this evaluation is the value every participant must produce.
/// </summary>
public sealed class AnimationBenchmarkCurve
{
    /// <summary>
    /// Key times, ascending from zero. A continuous curve's last key lies at the clip's length; a stepped curve's
    /// last key holds until the clip ends.
    /// </summary>
    public readonly float[] Times;

    /// <summary>Key values.</summary>
    public readonly float[] Values;

    /// <summary>Slope at each key in value units per second, the same on both sides; null for a stepped curve.</summary>
    public readonly float[] Slopes;

    /// <summary>A curve over the given keys.</summary>
    public AnimationBenchmarkCurve(float[] times, float[] values, float[] slopes)
    {
        Times = times;
        Values = values;
        Slopes = slopes;
    }

    /// <summary>Whether the curve holds each key's value until the next key.</summary>
    public bool Stepped => Slopes == null;

    /// <summary>Value at <paramref name="time"/> seconds, holding the end values outside the keys.</summary>
    public float Evaluate(float time)
    {
        var last = Times.Length - 1;
        if (!(time > Times[0])) return Values[0];
        if (time >= Times[last]) return Values[last];
        var k = 0;
        while (Times[k + 1] <= time) k++;
        if (Slopes == null) return Values[k];
        var start = Times[k];
        var span = Times[k + 1] - start;
        var s = (time - start) / span;
        var s2 = s * s;
        var s3 = s2 * s;
        return (2f * s3 - 3f * s2 + 1f) * Values[k] + (s3 - 2f * s2 + s) * Slopes[k] * span +
               (-2f * s3 + 3f * s2) * Values[k + 1] + (s3 - s2) * Slopes[k + 1] * span;
    }
}

/// <summary>
/// One measured situation: a rig, how many instances of it play, the clips they play, what the script does each
/// frame, and the times its result is checked at.
/// </summary>
public sealed class AnimationBenchmarkCase
{
    /// <summary>Token the case answers to in results and in a participant selection.</summary>
    public readonly string Id;

    /// <summary>What the case shows, for the viewer.</summary>
    public readonly string Description;

    /// <summary>Id of the case whose generated content this case plays; its own id when it has its own.</summary>
    public readonly string ContentId;

    /// <summary>Hierarchy of one instance.</summary>
    public readonly AnimationBenchmarkRig Rig;

    /// <summary>Bones of a skeleton rig; one otherwise.</summary>
    public readonly int Bones;

    /// <summary>Instances alive at once.</summary>
    public readonly int Instances;

    /// <summary>What the script does each frame.</summary>
    public readonly AnimationBenchmarkDrive Drive;

    /// <summary>The clips; the first plays on the base layer.</summary>
    public readonly AnimationBenchmarkClip[] Clips;

    /// <summary>Index of the clip an override layer plays over the base clip, or -1.</summary>
    public readonly int LayerClip;

    /// <summary>Bone whose subtree the layer clip drives.</summary>
    public readonly int LayerRoot;

    /// <summary>Seconds into the base clip the result is checked at.</summary>
    public readonly float[] ValidationTimes;

    internal AnimationBenchmarkCase(string id, string description, string contentId, AnimationBenchmarkRig rig,
        int bones, int instances, AnimationBenchmarkDrive drive, AnimationBenchmarkClip[] clips, int layerClip,
        int layerRoot, float[] validationTimes)
    {
        Id = id;
        Description = description;
        ContentId = contentId;
        Rig = rig;
        Bones = bones;
        Instances = instances;
        Drive = drive;
        Clips = clips;
        LayerClip = layerClip;
        LayerRoot = layerRoot;
        ValidationTimes = validationTimes;
    }

    /// <summary>Paths of the rig's objects relative to the instance root, the root first.</summary>
    public string[] Paths => AnimationBenchmarkSpec.Paths(Rig, Bones);

    /// <summary>Whether the case generates content of its own rather than playing another case's.</summary>
    public bool OwnsContent => ContentId == Id;
}

/// <summary>
/// The comparison's single source of truth: the cases, their rigs, and every key every participant's content is
/// generated from. The generated content and the result check both derive from it, so a participant either plays
/// exactly these curves or fails its check.
/// </summary>
public static class AnimationBenchmarkSpec
{
    /// <summary>Version of the generated content; content stamped with another one must be generated again.</summary>
    public const int ContentVersion = 1;

    /// <summary>Keys of every continuous curve.</summary>
    public const int KeyCount = 12;

    /// <summary>Frames of the sprite flipbook.</summary>
    public const int SpriteFrames = 8;

    /// <summary>Frames per second of the sprite flipbook.</summary>
    public const float SpriteRate = 12f;

    /// <summary>Seconds between two cross-fades of one instance.</summary>
    public const float CrossFadeInterval = 1f;

    /// <summary>Seconds one cross-fade lasts.</summary>
    public const float CrossFadeDuration = 0.3f;

    /// <summary>Clip seconds per second a seeking script advances each instance's time by.</summary>
    public const float SeekRate = 0.75f;

    /// <summary>Instances a spawning case destroys and creates every frame.</summary>
    public const int SpawnPerFrame = 8;

    /// <summary>Bones of the skeleton most cases use.</summary>
    public const int CrowdBones = 40;

    /// <summary>Bones of the single large skeleton.</summary>
    public const int RigBones = 300;

    /// <summary>Id of the case measuring memory rather than frame time.</summary>
    public const string MemoryId = "memory";

    static List<AnimationBenchmarkCase> cases;

    /// <summary>Every case in measurement order.</summary>
    public static IReadOnlyList<AnimationBenchmarkCase> Cases => cases ??= CreateCases();

    /// <summary>The case called <paramref name="id"/>.</summary>
    /// <exception cref="ArgumentException">No case has the id.</exception>
    public static AnimationBenchmarkCase Find(string id)
    {
        foreach (var candidate in Cases)
            if (candidate.Id == id)
                return candidate;
        throw new ArgumentException($"No animation benchmark case is called '{id}'.", nameof(id));
    }

    /// <summary>Scalar components of <paramref name="property"/>.</summary>
    public static int Components(AnimationBenchmarkProperty property) => property switch
    {
        AnimationBenchmarkProperty.LocalPosition or AnimationBenchmarkProperty.LocalEuler or
            AnimationBenchmarkProperty.LocalScale => 3,
        AnimationBenchmarkProperty.AnchoredPosition => 2,
        _ => 1,
    };

    /// <summary>Whether <paramref name="property"/> switches between values instead of interpolating.</summary>
    public static bool IsDiscrete(AnimationBenchmarkProperty property) =>
        property is AnimationBenchmarkProperty.Sprite or AnimationBenchmarkProperty.Active;

    /// <summary>Paths of a rig's objects relative to its root, the root's path empty.</summary>
    public static string[] Paths(AnimationBenchmarkRig rig, int bones)
    {
        switch (rig)
        {
            case AnimationBenchmarkRig.Skeleton:
                var paths = new string[bones];
                paths[0] = string.Empty;
                for (var bone = 1; bone < bones; bone++)
                {
                    var parent = Parent(bone);
                    paths[bone] = parent == 0 ? BoneName(bone) : paths[parent] + "/" + BoneName(bone);
                }
                return paths;
            case AnimationBenchmarkRig.Toggle:
                return new[] { string.Empty, "Child" };
            default:
                return new[] { string.Empty };
        }
    }

    /// <summary>Parent of a skeleton bone; the root bone is zero.</summary>
    public static int Parent(int bone) => (bone - 1) / 2;

    /// <summary>Name of a skeleton bone's game object.</summary>
    public static string BoneName(int bone) => "B" + bone;

    /// <summary>Position of a skeleton bone relative to its parent at rest.</summary>
    public static Vector3 RestPosition(int bone) => new((bone & 1) == 1 ? -0.15f : 0.15f, 0.3f, 0f);

    /// <summary>Whether <paramref name="bone"/> lies in the subtree of <paramref name="root"/>, the root included.</summary>
    public static bool InSubtree(int bone, int root)
    {
        while (bone > root) bone = Parent(bone);
        return bone == root;
    }

    /// <summary>
    /// The keys of one scalar curve of a clip of <paramref name="owner"/>, which must own its content. Values derive
    /// from the case, clip, object, property and component alone, so every participant's content holds the same keys.
    /// </summary>
    public static AnimationBenchmarkCurve Curve(AnimationBenchmarkCase owner, int clip, int track, int component)
    {
        var spec = owner.Clips[clip];
        var target = spec.Tracks[track];
        if (target.Property == AnimationBenchmarkProperty.Sprite) return SpriteCurve();
        if (target.Property == AnimationBenchmarkProperty.Active) return ActiveCurve(spec.Length);

        var seed = Mix(Hash(owner.Id) ^ Mix(Hash(spec.Name) + (uint)target.Object * 64u +
                                            (uint)target.Property * 4u + (uint)component));
        var (rest, amplitude) = Range(target.Property);
        var last = KeyCount - 1;
        var step = spec.Length / last;
        var times = new float[KeyCount];
        var values = new float[KeyCount];
        var slopes = new float[KeyCount];
        for (var k = 0; k < KeyCount; k++)
        {
            times[k] = k == last ? spec.Length : k * step;
            values[k] = rest + amplitude * (2f * Unit(seed, k) - 1f);
        }
        if (spec.Loop)
        {
            values[last] = values[0];
            for (var k = 0; k < last; k++)
                slopes[k] = (values[k + 1] - values[(k + last - 1) % last]) / (2f * step);
            slopes[last] = slopes[0];
        }
        else
        {
            for (var k = 1; k < last; k++)
                slopes[k] = (values[k + 1] - values[k - 1]) / (2f * step);
        }
        return new AnimationBenchmarkCurve(times, values, slopes);
    }

    /// <summary>Value a clip's curve takes <paramref name="time"/> seconds after it started.</summary>
    public static float Sample(AnimationBenchmarkClip clip, AnimationBenchmarkCurve curve, float time) =>
        curve.Evaluate(clip.Loop ? Mathf.Repeat(time, clip.Length) : Mathf.Clamp(time, 0f, clip.Length));

    /// <summary>A number in [0, 1) derived from <paramref name="seed"/> and <paramref name="index"/>.</summary>
    public static float Unit(uint seed, int index) =>
        (Mix(seed ^ Mix((uint)index * 0x9E3779B9u + 0x7F4A7C15u)) >> 8) / 16777216f;

    /// <summary>A stable hash of <paramref name="text"/>, the same on every platform and runtime.</summary>
    public static uint Hash(string text)
    {
        var hash = 2166136261u;
        foreach (var character in text)
        {
            hash ^= character;
            hash *= 16777619u;
        }
        return hash;
    }

    static uint Mix(uint value)
    {
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        value ^= value >> 16;
        return value;
    }

    static (float rest, float amplitude) Range(AnimationBenchmarkProperty property) => property switch
    {
        AnimationBenchmarkProperty.LocalPosition => (0f, 0.25f),
        AnimationBenchmarkProperty.LocalEuler => (0f, 35f),
        AnimationBenchmarkProperty.LocalScale => (1f, 0.25f),
        AnimationBenchmarkProperty.AnchoredPosition => (0f, 24f),
        _ => (0.65f, 0.3f),
    };

    static AnimationBenchmarkCurve SpriteCurve()
    {
        var times = new float[SpriteFrames];
        var values = new float[SpriteFrames];
        for (var frame = 0; frame < SpriteFrames; frame++)
        {
            times[frame] = frame / SpriteRate;
            values[frame] = frame;
        }
        return new AnimationBenchmarkCurve(times, values, null);
    }

    static AnimationBenchmarkCurve ActiveCurve(float length) =>
        new(new[] { 0f, length * 0.5f, length }, new[] { 1f, 0f, 1f }, null);

    static List<AnimationBenchmarkCase> CreateCases()
    {
        var transformTracks = new[]
        {
            new AnimationBenchmarkTrack(0, AnimationBenchmarkProperty.LocalPosition),
            new AnimationBenchmarkTrack(0, AnimationBenchmarkProperty.LocalEuler),
            new AnimationBenchmarkTrack(0, AnimationBenchmarkProperty.LocalScale),
        };
        var imageTracks = new[]
        {
            new AnimationBenchmarkTrack(0, AnimationBenchmarkProperty.AnchoredPosition),
            new AnimationBenchmarkTrack(0, AnimationBenchmarkProperty.LocalScale),
            new AnimationBenchmarkTrack(0, AnimationBenchmarkProperty.GraphicAlpha),
        };
        var transforms = new[] { new AnimationBenchmarkClip("A", 2f, true, transformTracks) };
        var crowd = new[] { new AnimationBenchmarkClip("A", 2f, true, SkeletonTracks(CrowdBones)) };
        var images = new[] { new AnimationBenchmarkClip("A", 1.5f, true, imageTracks) };
        var standard = new[] { 0.37f, 0.81f };

        return new List<AnimationBenchmarkCase>
        {
            new("transforms",
                "1000 objects, each with its own player: local position, Euler rotation and scale (9 curves), looping.",
                "transforms", AnimationBenchmarkRig.Single, 1, 1000, AnimationBenchmarkDrive.Playback, transforms, -1,
                0, Scaled(standard, 2f)),
            new("rig",
                "One 300-bone skeleton: Euler rotation of every bone plus the root position (903 curves), looping.",
                "rig", AnimationBenchmarkRig.Skeleton, RigBones, 1, AnimationBenchmarkDrive.Playback,
                new[] { new AnimationBenchmarkClip("A", 3f, true, SkeletonTracks(RigBones)) }, -1, 0,
                Scaled(standard, 3f)),
            new("crowd",
                "150 skeletons of 40 bones (123 curves each), every one at its own time, looping.",
                "crowd", AnimationBenchmarkRig.Skeleton, CrowdBones, 150, AnimationBenchmarkDrive.Playback, crowd, -1,
                0, Scaled(standard, 2f)),
            new("blend",
                "100 skeletons of 40 bones alternating two clips by a 0.3 s cross-fade once a second, staggered.",
                "blend", AnimationBenchmarkRig.Skeleton, CrowdBones, 100, AnimationBenchmarkDrive.CrossFade,
                new[]
                {
                    new AnimationBenchmarkClip("A", 2f, true, SkeletonTracks(CrowdBones)),
                    new AnimationBenchmarkClip("B", 1.5f, true, SkeletonTracks(CrowdBones)),
                }, -1, 0, Scaled(standard, 2f)),
            new("layers",
                "100 skeletons of 40 bones: a base clip on every bone and an override layer on half of them.",
                "layers", AnimationBenchmarkRig.Skeleton, CrowdBones, 100, AnimationBenchmarkDrive.Playback,
                new[]
                {
                    new AnimationBenchmarkClip("A", 2f, true, SkeletonTracks(CrowdBones)),
                    new AnimationBenchmarkClip("Upper", 2f, true, SubtreeTracks(CrowdBones, 1)),
                }, 1, 1, Scaled(standard, 2f)),
            new("ui",
                "300 images on one canvas: anchored position, scale and alpha (6 curves), looping.",
                "ui", AnimationBenchmarkRig.Image, 1, 300, AnimationBenchmarkDrive.Playback, images, -1, 0,
                Scaled(standard, 1.5f)),
            new("ui-hold",
                "The same images after a 0.5 s clip finished: every value holds the clip's last frame.",
                "ui-hold", AnimationBenchmarkRig.Image, 1, 300, AnimationBenchmarkDrive.Hold,
                new[] { new AnimationBenchmarkClip("A", 0.5f, false, imageTracks) }, -1, 0, new[] { 0.5f }),
            new("sprites",
                "1000 sprite renderers: an 8-frame flipbook at 12 frames per second and alpha, looping.",
                "sprites", AnimationBenchmarkRig.Sprite, 1, 1000, AnimationBenchmarkDrive.Playback,
                new[]
                {
                    new AnimationBenchmarkClip("A", SpriteFrames / SpriteRate, true, new[]
                    {
                        new AnimationBenchmarkTrack(0, AnimationBenchmarkProperty.Sprite),
                        new AnimationBenchmarkTrack(0, AnimationBenchmarkProperty.SpriteAlpha),
                    }),
                }, -1, 0, new[] { 2.5f / SpriteRate, 6.5f / SpriteRate }),
            new("active",
                "1000 parents switching a child on and off four times a second.",
                "active", AnimationBenchmarkRig.Toggle, 1, 1000, AnimationBenchmarkDrive.Playback,
                new[]
                {
                    new AnimationBenchmarkClip("A", 0.5f, true, new[]
                    {
                        new AnimationBenchmarkTrack(1, AnimationBenchmarkProperty.Active),
                    }),
                }, -1, 0, new[] { 0.125f, 0.375f }),
            new("seek",
                "The ui images held at a time the script sets every frame, as a progress or scroll position drives them.",
                "ui", AnimationBenchmarkRig.Image, 1, 300, AnimationBenchmarkDrive.Seek, images, -1, 0,
                Scaled(standard, 1.5f)),
            new("spawn",
                "200 transforms objects alive, 8 destroyed and 8 created and started every frame.",
                "transforms", AnimationBenchmarkRig.Single, 1, 200, AnimationBenchmarkDrive.Spawn, transforms, -1, 0,
                Scaled(standard, 2f)),
            new(MemoryId,
                "Memory of 200 crowd skeletons created and playing, per instance.",
                "crowd", AnimationBenchmarkRig.Skeleton, CrowdBones, 200, AnimationBenchmarkDrive.Memory, crowd, -1,
                0, Array.Empty<float>()),
        };
    }

    static float[] Scaled(float[] fractions, float length)
    {
        var times = new float[fractions.Length];
        for (var i = 0; i < times.Length; i++)
            times[i] = fractions[i] * length;
        return times;
    }

    static AnimationBenchmarkTrack[] SkeletonTracks(int bones)
    {
        var tracks = new AnimationBenchmarkTrack[bones + 1];
        tracks[0] = new AnimationBenchmarkTrack(0, AnimationBenchmarkProperty.LocalPosition);
        for (var bone = 0; bone < bones; bone++)
            tracks[bone + 1] = new AnimationBenchmarkTrack(bone, AnimationBenchmarkProperty.LocalEuler);
        return tracks;
    }

    static AnimationBenchmarkTrack[] SubtreeTracks(int bones, int root)
    {
        var tracks = new List<AnimationBenchmarkTrack>();
        for (var bone = root; bone < bones; bone++)
            if (InSubtree(bone, root))
                tracks.Add(new AnimationBenchmarkTrack(bone, AnimationBenchmarkProperty.LocalEuler));
        return tracks.ToArray();
    }
}
