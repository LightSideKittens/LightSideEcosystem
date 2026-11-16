using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// One timed unit of a <see cref="Reel"/>: builds its own hierarchy from code and reports what it looks like at
    /// any point inside its own duration.
    /// </summary>
    /// <remarks>
    /// <see cref="OnRender"/> must be a pure function of its argument. A slide that accumulates state across calls
    /// cannot be scrubbed, cannot be re-rendered at a single frame for inspection, and will not match between the
    /// editor preview and an offline capture — the reel seeks backwards freely.
    /// </remarks>
    [RequireComponent(typeof(RectTransform), typeof(CanvasGroup))]
    [DisallowMultipleComponent]
    public abstract class Slide : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float seconds = 3f;
        [SerializeField] private float pushIn = 0.035f;
        [SerializeReference] private SlideTransition enter = new Cut();

        private readonly List<Cue> cues = new();

        private Script script;
        private RectTransform rect;
        [NonSerialized] private RectTransform content;
        private CanvasGroup group;

        /// <summary>How long the slide occupies the reel, before any transition overlap. Never negative.</summary>
        /// <remarks>
        /// Clamped on read as well as on write: the inspector writes the serialized field directly, and a negative
        /// duration reaching the reel would shift every later slide and every cue behind it.
        /// </remarks>
        public float Seconds
        {
            get => Mathf.Max(0f, seconds);
            set => seconds = Mathf.Max(0f, value);
        }

        /// <summary>How this slide arrives. Its duration overlaps the tail of the slide before it.</summary>
        public SlideTransition Enter
        {
            get => enter ??= new Cut();
            set => enter = value ?? new Cut();
        }

        /// <summary>The slide's own rect, which fills the stage and which transitions move and fade.</summary>
        public RectTransform Rect => rect ? rect : rect = (RectTransform)transform;

        /// <summary>Opacity for the whole slide; owned by the reel and its transitions.</summary>
        public CanvasGroup Group => group ? group : group = GetComponent<CanvasGroup>();

        /// <summary>
        /// The rect every piece of the slide's content lives under, and the only one the slow push-in scales.
        /// </summary>
        /// <remarks>
        /// Separating it from <see cref="Rect"/> is what stops framing and transitions from fighting: a transition
        /// poses <see cref="Rect"/>, the push-in poses this, and the slide's own code poses this rect's children.
        /// The three are disjoint, so their scales compose instead of overwriting each other.
        /// </remarks>
        public RectTransform Content => content;

        /// <summary>
        /// Whether this slide's content exists and its references are live.
        /// </summary>
        /// <remarks>
        /// A slide caches what it animates in ordinary fields, which do not survive a domain reload or a scene load
        /// even though the built GameObjects do. The reel rebuilds any slide that reports false, and a slide that
        /// reports false draws nothing until it does.
        /// <para>
        /// It also reports false when <c>OnBuild</c> threw. A half-built slide must not be posed through its own
        /// half-assigned references: the resulting <see cref="System.NullReferenceException"/> fires every frame and
        /// buries the exception that actually mattered.
        /// </para>
        /// </remarks>
        public bool IsBuilt => content;

        /// <summary>
        /// How far the frame scales up across the slide, from unity on its first frame.
        /// </summary>
        /// <remarks>
        /// Default-on, because a frame with no motion at all reads as a screenshot and the viewer stops watching
        /// between beats. The rate is far below the threshold at which the move itself is noticed.
        /// <para>
        /// It moves the whole frame, never one element. Anything the eye or a pointer is aiming at must enter with
        /// opacity alone: a target that is still moving is a target the pointer disagrees with about where it is.
        /// </para>
        /// </remarks>
        public float PushIn
        {
            get => pushIn;
            set => pushIn = value;
        }

        /// <summary>Sounds and markers this slide declared, in the slide's own time.</summary>
        public IReadOnlyList<Cue> Cues => cues;

        /// <summary>
        /// Timed mutations of real product state, replayed to the current moment before every render.
        /// </summary>
        /// <remarks>
        /// Declare steps in <see cref="OnBuild"/>. Anything that changes a live document — focusing a field,
        /// selecting a range, copying, pasting — belongs here rather than in <see cref="OnRender"/>, which must stay
        /// a pure function of its argument. A fresh script is created for every build.
        /// </remarks>
        protected Script Script => script ??= new Script();

        /// <summary>
        /// Declares a named moment at <paramref name="atLocal"/> seconds into this slide. Call from
        /// <see cref="OnBuild"/>; the list is cleared on every build.
        /// </summary>
        protected void Cue(string name, float atLocal) => cues.Add(new Cue(name, atLocal));

        /// <summary>Declares every cue of <paramref name="source"/>, shifted by <paramref name="offset"/> seconds.</summary>
        protected void Cue(IEnumerable<Cue> source, float offset = 0f)
        {
            foreach (var cue in source) cues.Add(cue.Offset(offset));
        }

        /// <summary>Creates the slide's content. Called once per build, on an empty hierarchy.</summary>
        protected abstract void OnBuild(Stage stage);

        /// <summary>
        /// Poses the slide for <paramref name="local"/> seconds since its own start. Called every rendered frame,
        /// in any order, including backwards.
        /// </summary>
        protected abstract void OnRender(float local);

        internal void Build(Stage stage)
        {
            Group.alpha = 1f;
            content = null;
            cues.Clear();
            script = null;

            var node = stage.Node("Content", Rect);
            stage.Fit(node);
            try
            {
                OnBuild(stage.For(node));
            }
            catch
            {
                node.gameObject.SetActive(false);
                ObjectUtils.SafeDestroy(node.gameObject);
                throw;
            }

            content = node;
        }

        internal void Render(float local)
        {
            if (!IsBuilt) return;

            var k = Seconds > 0f
                ? Mathf.LerpUnclamped(1f, 1f + pushIn, Mathf.Clamp01(local / Seconds))
                : 1f;
            content.localScale = new Vector3(k, k, 1f);

            try
            {
                script?.Seek(local);
                OnRender(local);
            }
            catch (Exception exception)
            {
                content.gameObject.SetActive(false);
                content = null;
                Debug.LogError(
                    $"[Promo] '{name}' ({GetType().Name}) threw while posing frame {local:0.000}s and has been " +
                    "taken out of the reel until the next rebuild. The exception below is the diagnosis.", this);
                Debug.LogException(exception, this);
            }
        }
    }
}
