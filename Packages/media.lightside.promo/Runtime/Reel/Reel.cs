using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// Drives an ordered list of <see cref="Slide"/> children: builds their hierarchies from code, and composes any
    /// single frame of the film on demand.
    /// </summary>
    /// <remarks>
    /// The reel is seekable, and that is the point. <see cref="Compose"/> is a pure function of a time, so the same
    /// frame is identical in the editor preview, in play mode and in an offline capture, and any frame can be
    /// rendered on its own for inspection.
    /// <para>
    /// A slide's <see cref="Slide.Enter"/> transition overlaps the tail of the slide before it, so the reel's length
    /// is exactly the sum of its slides' durations and there is never an empty frame between two of them.
    /// </para>
    /// </remarks>
    [ExecuteAlways]
    [AddComponentMenu(PromoMenu.AddComponent.Reel)]
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public sealed class Reel : MonoBehaviour
    {
        [SerializeField] private Vector2Int frameSize = new Vector2Int(1920, 1080);
        [SerializeReference, TypeSelector] private Theme theme = new Theme();
        [SerializeField] private int fps = 60;
        [SerializeField] private bool playing = true;
        [SerializeField] private bool loop = true;
        [SerializeField] private float time;

        private readonly List<Slide> slides = new();
        private Stage stage;
        private bool attempted;

        /// <summary>
        /// The slide index last activated, or -1 when the content on screen has never been through a canvas pass.
        /// </summary>
        /// <remarks>
        /// A <see cref="UniText"/> parses on the canvas render callbacks, so text created or re-enabled during a
        /// compose has no snapshot and no bound range source until a canvas pass runs. A slide that measures text or
        /// writes ranges while posing is handed an empty component unless the canvas is flushed between activating it
        /// and posing it — which is why every slide about to be posed is activated before the flush, never after.
        /// </remarks>
        private int composed = -1;

        /// <summary>Whether the slide before <see cref="composed"/> was activated alongside it.</summary>
        private bool blended;

        /// <summary>Frames per second the reel is authored and captured at.</summary>
        public int Fps => Mathf.Max(1, fps);

        /// <summary>
        /// The film's frame, in points, and the exact size of the reel's own rect.
        /// </summary>
        /// <remarks>
        /// The reel is a fixed box, never stretched to the canvas. A canvas under a <c>CanvasScaler</c> is whatever
        /// shape the Game View happens to be, so a slide laid out against it is composed for one aspect and captured
        /// at another — every size in the film would be a guess. Fixing the frame here makes
        /// <see cref="Stage.Width"/> and <see cref="Stage.Height"/> mean exactly this, in every scene, on every
        /// machine, in the editor and in the captured file alike.
        /// </remarks>
        public Vector2Int FrameSize => new Vector2Int(Mathf.Max(16, frameSize.x), Mathf.Max(16, frameSize.y));

        /// <summary>Total length in seconds: the sum of every slide's duration.</summary>
        public float Duration
        {
            get
            {
                Collect();
                return Total();
            }
        }

        /// <summary>Total length in whole frames at <see cref="Fps"/>.</summary>
        public int FrameCount => Mathf.Max(1, Mathf.RoundToInt(Duration * Fps));

        /// <summary>The frame currently composed.</summary>
        public int Frame
        {
            get => Mathf.RoundToInt(time * Fps);
            set => Seek(value / (float)Fps);
        }

        /// <summary>Whether the reel advances with time. Turn it off to scrub.</summary>
        public bool Playing
        {
            get => playing;
            set => playing = value;
        }

        /// <summary>The build context, which slides use to create their content.</summary>
        public Stage Stage => stage ??= new Stage((RectTransform)transform, Theme);

        /// <summary>
        /// The livery every slide is drawn in.
        /// </summary>
        /// <remarks>
        /// Read while a slide builds and baked into the shapes it creates, so content already on screen keeps the
        /// palette it was made with: a livery reaches the frame through <see cref="Rebuild"/>.
        /// </remarks>
        public Theme Theme
        {
            get => theme ??= new Theme();
            set
            {
                theme = value ?? new Theme();
                stage = null;
            }
        }

        /// <summary>
        /// Composes the frame at <paramref name="seconds"/> and holds it, wrapping into the reel's length while it
        /// loops and clamping to the end while it does not.
        /// </summary>
        public void Seek(float seconds)
        {
            Collect();
            var total = Total();
            time = total <= 0f
                ? 0f
                : loop
                    ? Mathf.Repeat(seconds, total)
                    : Mathf.Clamp(seconds, 0f, total);
            Compose(time);
        }

        /// <summary>
        /// Destroys every slide's generated content and rebuilds it. The one authoritative way content comes into
        /// existence; nothing in a slide's hierarchy is authored by hand.
        /// </summary>
        /// <remarks>
        /// Outgoing content is deactivated before it is destroyed: in play mode destruction is deferred to the end
        /// of the frame, so it would otherwise render alongside the incoming content for one frame.
        /// <para>
        /// Slides are independent work: one that throws is reported against its own name and left unbuilt, and the
        /// rest still build. Its exception is re-logged, not swallowed.
        /// </para>
        /// </remarks>
        [ContextMenu("Rebuild")]
        public void Rebuild()
        {
            attempted = true;
            Collect();
            stage = null;
            composed = -1;
            blended = false;
            ApplyFrame();

            for (var i = 0; i < slides.Count; i++)
            {
                var slide = slides[i];
                for (var c = slide.transform.childCount - 1; c >= 0; c--)
                {
                    var child = slide.transform.GetChild(c).gameObject;
                    child.SetActive(false);
                    ObjectUtils.SafeDestroy(child);
                }

                Stage.Fit(slide.Rect);
                try
                {
                    slide.Build(Stage.ForFrame(slide.Rect));
                }
                catch (Exception exception)
                {
                    Debug.LogError(
                        $"[Promo] '{slide.name}' ({slide.GetType().Name}) failed to build and will not render.",
                        slide);
                    Debug.LogException(exception, slide);
                }

                ReportOverrun(slide);
            }

            Seek(time);
        }

        private Action advanceCallback;
        private TickHandle advanceHandle;

        private void OnEnable()
        {
            ApplyFrame();
            CoreLoop.Updating.Toggle(ref advanceHandle, advanceCallback ??= Advance, true);
            Seek(time);
        }

        private void OnDisable() =>
            CoreLoop.Updating.Toggle(ref advanceHandle, advanceCallback, false);

        /// <summary>
        /// Advances the playhead from <see cref="CoreLoop"/>, which is the only clock that ticks in edit mode as
        /// well as play mode. Its edit-mode delta is raw wall clock with no stall clamp, so a domain reload or a
        /// long import would otherwise jump the playhead by seconds.
        /// </summary>
        /// <remarks>
        /// This is also where a stale reel repairs itself: a domain reload keeps the built GameObjects but wipes
        /// every slide's cached references. The rebuild happens on a tick rather than in <c>OnEnable</c> because
        /// destroying a hierarchy while the scene is still loading is not something to do for a convenience.
        /// <para>
        /// Exactly one automatic attempt is made. A slide whose <c>OnBuild</c> throws stays unbuilt, and retrying it
        /// every tick would bury its exception in a log flood instead of showing it once.
        /// </para>
        /// </remarks>
        private void Advance()
        {
            Collect();

            if (!attempted && NeedsBuild())
            {
                Rebuild();
                return;
            }

            if (!playing) return;

            var total = Total();
            if (total <= 0f) return;

            var next = time + PlaybackTime.Clamp(CoreLoop.DeltaTime);
            if (next >= total && !loop)
            {
                next = total;
                playing = false;
            }

            Seek(next);
#if UNITY_EDITOR
            if (!Application.isPlaying) CoreLoop.RequestEditorFrame();
#endif
        }

        private void Compose(float at)
        {
            if (slides.Count == 0) return;

            var index = 0;
            var start = 0f;
            for (; index < slides.Count - 1; index++)
            {
                var end = start + slides[index].Seconds;
                if (at < end) break;
                start = end;
            }

            var current = slides[index];
            var local = at - start;
            var enter = current.Enter;
            var blending = index > 0 && enter.Seconds > 0f && local < enter.Seconds;

            for (var i = 0; i < slides.Count; i++)
            {
                slides[i].Enter.Restore(slides[i]);
                if (i + 1 < slides.Count) slides[i + 1].Enter.Restore(slides[i]);
                slides[i].gameObject.SetActive(i == index || (blending && i == index - 1));
            }

            if (composed != index || blended != blending)
            {
                composed = index;
                blended = blending;
                Canvas.ForceUpdateCanvases();
            }

            current.Render(local);

            if (!blending)
            {
                enter.Apply(null, current, 1f);
                return;
            }

            var previous = slides[index - 1];
            previous.Render(previous.Seconds + local);
            enter.Apply(previous, current, local / enter.Seconds);
        }

        /// <summary>
        /// Every slide's cues, shifted onto the reel's own timeline and ordered by time.
        /// </summary>
        public List<Cue> CueSheet()
        {
            Collect();
            var sheet = new List<Cue>();
            var start = 0f;

            for (var i = 0; i < slides.Count; i++)
            {
                var slide = slides[i];
                for (var c = 0; c < slide.Cues.Count; c++) sheet.Add(slide.Cues[c].Offset(start));
                start += slide.Seconds;
            }

            sheet.Sort((a, b) => a.At.CompareTo(b.At));
            return sheet;
        }

        private float Total()
        {
            var total = 0f;
            for (var i = 0; i < slides.Count; i++) total += slides[i].Seconds;
            return total;
        }

        /// <summary>Pins the reel's rect to <see cref="FrameSize"/>, centered, whatever the canvas is doing.</summary>
        private void ApplyFrame()
        {
            var rect = (RectTransform)transform;
            var half = new Vector2(0.5f, 0.5f);

            rect.anchorMin = half;
            rect.anchorMax = half;
            rect.pivot = half;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = FrameSize;
            rect.localScale = Vector3.one;
        }

        /// <summary>
        /// Reports a slide whose own cues land after it ends.
        /// </summary>
        /// <remarks>
        /// A slide's timings are constants inside it and its duration is authored on the reel, so nothing connects
        /// the two: a demonstration built from a start, a count and a step outgrows its slide silently, and the only
        /// symptom is a cut that arrives before the thing being demonstrated finishes. Cues are where a slide says
        /// when its moments are, which makes them the one place the overrun can be seen without running the film.
        /// <para>
        /// A warning rather than a failure: a shot deliberately cut short is a legitimate thing to author, and a
        /// slide that declares no cues is not reporting a length at all and cannot be checked.
        /// </para>
        /// </remarks>
        private static void ReportOverrun(Slide slide)
        {
            var last = 0f;
            for (var i = 0; i < slide.Cues.Count; i++) last = Mathf.Max(last, slide.Cues[i].At);
            if (last <= slide.Seconds) return;

            Debug.LogWarning(
                $"[Promo] '{slide.name}' has a cue at {last:0.00}s and is {slide.Seconds:0.00}s long, so its last " +
                $"{last - slide.Seconds:0.00}s never plays.", slide);
        }

        private bool NeedsBuild()
        {
            for (var i = 0; i < slides.Count; i++)
                if (!slides[i].IsBuilt)
                    return true;
            return false;
        }

        private void Collect()
        {
            slides.Clear();
            for (var i = 0; i < transform.childCount; i++)
                if (transform.GetChild(i).TryGetComponent<Slide>(out var slide))
                    slides.Add(slide);
        }
    }
}
