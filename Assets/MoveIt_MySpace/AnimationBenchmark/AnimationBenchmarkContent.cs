using System;
using UnityEngine;

/// <summary>
/// Generated content of the animation comparison: one prefab per participant for every case owning content, the
/// Mecanim clips and mask the Playables participant builds its graphs from, and the flipbook's sprites. Written by
/// Tools/LightSide/Benchmarks/Generate Animation Content; never edited by hand.
/// </summary>
public sealed class AnimationBenchmarkContent : ScriptableObject
{
    /// <summary>Content of one case; a participant that cannot represent the case has no prefab.</summary>
    [Serializable]
    public struct Entry
    {
        /// <summary>Id of the case owning the content.</summary>
        public string id;

        /// <summary>Instance played by a <c>MoveItPlayer</c> on its root.</summary>
        public GameObject moveit;

        /// <summary>Instance played by an <c>Animator</c> with the case's controller.</summary>
        public GameObject animator;

        /// <summary>Instance played by a legacy <c>Animation</c> holding the case's legacy clips.</summary>
        public GameObject legacy;

        /// <summary>Instance with a controller-less <c>Animator</c> a playable graph drives.</summary>
        public GameObject playables;

        /// <summary>Mecanim clips in the case's clip order.</summary>
        public AnimationClip[] clips;

        /// <summary>Transform mask of the case's override layer, or null.</summary>
        public AvatarMask mask;
    }

    [SerializeField] int version;
    [SerializeField] Entry[] entries = Array.Empty<Entry>();
    [SerializeField] Sprite[] sprites = Array.Empty<Sprite>();

    /// <summary>The <see cref="AnimationBenchmarkSpec.ContentVersion"/> the content was generated for.</summary>
    public int Version => version;

    /// <summary>Frames of the flipbook, in order.</summary>
    public Sprite[] Sprites => sprites;

    /// <summary>The content of the case called <paramref name="id"/>.</summary>
    /// <exception cref="InvalidOperationException">No content was generated for the case.</exception>
    public Entry Find(string id)
    {
        foreach (var entry in entries)
            if (entry.id == id)
                return entry;
        throw new InvalidOperationException(
            $"No animation benchmark content exists for case '{id}'; generate the content again.");
    }

#if UNITY_EDITOR
    /// <summary>Replaces the whole catalog with freshly generated content.</summary>
    public void Assign(int contentVersion, Entry[] generated, Sprite[] frames)
    {
        version = contentVersion;
        entries = generated;
        sprites = frames;
    }
#endif
}
