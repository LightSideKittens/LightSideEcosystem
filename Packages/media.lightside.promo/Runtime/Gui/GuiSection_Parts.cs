using System.Collections.Generic;
using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>The building blocks every section is made of: its beats, and the controls the beats operate.</summary>
    public abstract partial class GuiSection
    {
        /// <summary>One beat of a section: its content, the pointer that drives it, and when it plays.</summary>
        /// <remarks>
        /// Content lives under <see cref="Root"/>. It is thrown in from <see cref="From"/> at <see cref="In"/>, holds
        /// still while the action plays from <see cref="ActionAt"/>, and is thrown out toward <see cref="To"/> at
        /// <see cref="Out"/> — the moment the next part is thrown in, so consecutive parts travel as one strip, a frame
        /// apart, like a camera moving between them. The pointer rides with its content. Shapes are never scaled
        /// through a transform — uGUI carries their parameters in the tangent stream, which a scale distorts — so a
        /// part that grows does it by resizing its shapes.
        /// </remarks>
        protected abstract class Part
        {
            protected Part(Stage stage, string name)
            {
                Root = stage.Stretch(stage.Node(name, stage.Root));
                group = Root.gameObject.AddComponent<CanvasGroup>();
                frame = new Vector2(stage.Width, stage.Height) * 0.5f;
            }

            private readonly CanvasGroup group;

            /// <summary>Half the frame, which the camera's view never leaves.</summary>
            private readonly Vector2 frame;

            /// <summary>How far the camera's close shots zoom in on what the pointer works on; one keeps it at rest.</summary>
            protected virtual float CameraZoom => 1f;

            /// <summary>
            /// The composition's extent in stage points: the camera never pushes in so far that its width leaves the view,
            /// keeps all of it in view along an axis it fits, and its view inside it along one it does not; null for a
            /// composition the camera only keeps the frame around.
            /// </summary>
            protected virtual Rect? Composition => null;

            /// <summary>Whether the part turns content out of the canvas plane, shown through the reel's depth lens while the part is on screen.</summary>
            public virtual bool InDepth => false;

            /// <summary>How the part arrives and how it leaves.</summary>
            public Link Enter { get; set; }

            public Link Exit { get; set; }

            public RectTransform Root { get; }

            public float In { get; set; }

            public float ActionAt { get; set; }

            public float Out { get; set; }

            /// <summary>Where the content starts its arrival from, as an offset from its resting place.</summary>
            public Vector2 From { get; set; }

            /// <summary>Where the content ends its departure, as an offset from its resting place.</summary>
            public Vector2 To { get; set; }

            /// <summary>The recorded window the part plays, or null for a part the scene draws.</summary>
            public virtual TakeShot Take => null;

            /// <summary>How long the action runs once the content has landed.</summary>
            public abstract float ActionSeconds { get; }

            /// <summary>
            /// How much larger than it is built the part's content stands, grown about <see cref="MagnifyAbout"/>: a drawn
            /// part's pointer aims at the grown content. Set before the part's pointer is built.
            /// </summary>
            public float Magnify { get; set; } = 1f;

            /// <summary>The stage point <see cref="Magnify"/> grows the content about.</summary>
            public Vector2 MagnifyAbout { get; set; }

            /// <summary>Where the content's own point <paramref name="point"/> stands once grown by <see cref="Magnify"/>.</summary>
            protected Vector2 Magnified(Vector2 point) => MagnifyAbout + (point - MagnifyAbout) * Magnify;

            /// <summary>
            /// Whether the action ends on a press that brings the next part in, as a menu item opens its screen: the part
            /// leaves as the press ends, holding nothing.
            /// </summary>
            public virtual bool Navigates => false;

            /// <summary>The part's pointer, or null before it has been built.</summary>
            protected Pointer Pointer { get; set; }

            /// <summary>Builds the part's pointer above everything; call once every part's content exists.</summary>
            public abstract void BuildPointer(Stage stage);

            /// <summary>The pointer's clicks and drags, relative to <see cref="ActionAt"/>.</summary>
            public IEnumerable<Cue> Cues() => Pointer == null ? System.Array.Empty<Cue>() : Pointer.Timeline.Cues();

            /// <summary>Every accented press of the pointer: when, in section time, and where, in stage coordinates.</summary>
            public IEnumerable<(float at, Vector2 point)> Accents()
            {
                if (Pointer == null) yield break;
                foreach (var press in Pointer.Timeline.Presses)
                    if (press.Accent)
                        yield return (ActionAt + press.Down, press.Point);
            }

            /// <summary>Whether the section ends by diving into this part, which then poses the dive itself.</summary>
            public bool Dives { get; set; }

            /// <summary>How long the part stays on screen after <see cref="Out"/>.</summary>
            public float Leaving { get; set; } = SwishFor;

            /// <summary>How far the arriving content still is from its resting place at <paramref name="local"/>.</summary>
            public Vector2 Arrival(float local) => Enter == Link.Drop
                ? From * (1f - Motion.Back.Window(local, In, DropFor))
                : From * (1f - Motion.Swish.Window(local, In, SwishFor));

            /// <summary>How far the leaving content has travelled from its resting place at <paramref name="local"/>.</summary>
            public Vector2 Departure(float local) => Exit == Link.Drop
                ? To * Ease.EmphasizedIn.Window(local, Out, SwishFor)
                : To * Motion.Swish.Window(local, Out, SwishFor);

            public void Pose(float local)
            {
                var shown = local >= In && local <= Out + Leaving;
                Root.gameObject.SetActive(shown);
                var leaving = Mathf.Clamp01((local - Out) / SwishFor);
                var receding = Exit is Link.Push or Link.Zoom && local > Out;
                if (Pointer != null) Pointer.Visible = shown && !receding;
                if (!shown) return;

                var offset = Arrival(local) + Departure(local);

                var arriving = Motion.Swish.Window(local, In, SwishFor);
                var scale = 1f;
                var alpha = 1f;
                if (Enter == Link.Zoom)
                {
                    scale *= Mathf.LerpUnclamped(ZoomFrom, 1f, arriving);
                    alpha *= arriving;
                }

                var left = Motion.Swish.Evaluate(leaving);
                if (Exit == Link.Zoom)
                {
                    scale *= Mathf.LerpUnclamped(1f, ZoomTo, left);
                    alpha *= 1f - left;
                }
                else if (Exit == Link.Push)
                {
                    scale *= Mathf.LerpUnclamped(1f, PushTo, left);
                    alpha *= 1f - left;
                }

                Root.anchoredPosition = offset + MagnifyAbout * ((1f - Magnify) * scale);
                Root.localScale = Vector3.one * (scale * Magnify);
                if (Pointer != null) Pointer.Offset = offset;
                group.alpha = alpha;
                PoseAction(local - ActionAt);
            }

            /// <summary>
            /// Where the section's camera stands while this part plays, at <paramref name="local"/>: the stage point it
            /// looks at and how far it is zoomed; false while it stands at rest on the whole frame. A part with a close-up
            /// holds one still shot for each run of the pointer's presses that fits a view, snaps from shot to shot as the
            /// pointer heads for the next run, and snaps back to the whole frame before the part leaves; its view never
            /// leaves the frame.
            /// </summary>
            public bool Films(float local, out Vector2 look, out float zoom)
            {
                look = Vector2.zero;
                zoom = 1f;
                if (CameraZoom <= 1f || Pointer == null) return false;
                shots ??= PlanShots();
                var played = local - ActionAt;
                var current = -1;
                for (var i = 0; i < shots.Count && played >= shots[i].At; i++) current = i;
                if (current < 0) return false;

                var from = current > 0 ? shots[current - 1] : new Shot(0f, Vector2.zero, 1f);
                var to = shots[current];
                var snap = Motion.Snap.Evaluate(Mathf.Clamp01((played - to.At) / SnapFor));
                zoom = Mathf.LerpUnclamped(from.Zoom, to.Zoom, snap);
                look = Vector2.LerpUnclamped(from.Look, to.Look, snap);
                return true;
            }

            /// <summary>
            /// The part's shots in order, from the action's start: each the view of a run of presses — clicks and the whole
            /// of each drag — that all fit inside the middle of one close view, taken just before the run's first press,
            /// and last the whole frame again.
            /// </summary>
            private List<Shot> PlanShots()
            {
                var zoom = CameraZoom;
                if (Composition is { } wide) zoom = Mathf.Min(zoom, frame.x * 2f / (wide.width * WidthRoom));
                var inner = frame / zoom * ShotMiddle;
                var presses = Pointer.Timeline.Presses;
                var planned = new List<Shot>();
                var points = new List<Vector2>();
                var ready = ShotEarliest;
                var first = 0;
                while (first < presses.Count)
                {
                    points.Clear();
                    var next = first;
                    var center = Vector2.zero;
                    while (next < presses.Count)
                    {
                        points.Add(presses[next].Point);
                        points.Add(Pointer.Timeline.Sample(presses[next].Up).Position);
                        var bounds = Bounds(points);
                        if (next > first && (bounds.width > inner.x * 2f || bounds.height > inner.y * 2f)) break;
                        center = bounds.center;
                        next++;
                    }

                    var at = Mathf.Max(presses[first].Down - SnapLead, ready);
                    planned.Add(new Shot(at, View(center, zoom), zoom));
                    ready = at + SnapFor + ShotHold;
                    first = next;
                }

                if (planned.Count > 0) planned.Add(new Shot(Mathf.Max(Out - ActionAt - SnapBack, ready), Vector2.zero, 1f));
                return planned;
            }

            private static Rect Bounds(List<Vector2> points)
            {
                var min = points[0];
                var max = points[0];
                foreach (var point in points)
                {
                    min = Vector2.Min(min, point);
                    max = Vector2.Max(max, point);
                }

                return UnityEngine.Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            }

            /// <summary>Where a view zoomed by <paramref name="zoom"/> looks to center <paramref name="target"/>, kept on the composition and inside the frame.</summary>
            private Vector2 View(Vector2 target, float zoom)
            {
                var half = frame / zoom;
                var look = target;
                if (Composition is { } kept)
                {
                    var bounds = UnityEngine.Rect.MinMaxRect(Mathf.Max(kept.xMin, -frame.x), Mathf.Max(kept.yMin, -frame.y),
                        Mathf.Min(kept.xMax, frame.x), Mathf.Min(kept.yMax, frame.y));
                    look.x = Keep(look.x, bounds.xMin, bounds.xMax, half.x);
                    look.y = Keep(look.y, bounds.yMin, bounds.yMax, half.y);
                }

                look.x = Mathf.Clamp(look.x, half.x - frame.x, frame.x - half.x);
                look.y = Mathf.Clamp(look.y, half.y - frame.y, frame.y - half.y);
                return look;
            }

            /// <summary>
            /// Where a view <paramref name="half"/> wide either side may look along an axis holding a composition from
            /// <paramref name="min"/> to <paramref name="max"/>: so the whole of it stays in view when it fits, and so the
            /// view stays inside it when it does not.
            /// </summary>
            private static float Keep(float look, float min, float max, float half) =>
                Mathf.Clamp(look, Mathf.Min(max - half, min + half), Mathf.Max(max - half, min + half));

            /// <summary>Poses the action <paramref name="played"/> seconds after it started; negative before it.</summary>
            protected abstract void PoseAction(float played);

            private List<Shot> shots;

            /// <summary>A still camera shot: from when, relative to the action's start, where it looks and how far it is zoomed.</summary>
            private readonly struct Shot
            {
                public Shot(float at, Vector2 look, float zoom)
                {
                    At = at;
                    Look = look;
                    Zoom = zoom;
                }

                public float At { get; }
                public Vector2 Look { get; }
                public float Zoom { get; }
            }

            /// <summary>How long the camera takes to snap to a new shot, and how long before a run's first press it starts.</summary>
            private const float SnapFor = 0.35f;

            private const float SnapLead = 0.45f;

            /// <summary>How long before the part leaves the camera snaps back to the whole frame.</summary>
            private const float SnapBack = 0.7f;

            /// <summary>How long a shot holds at least once it has landed, and the earliest a first shot starts.</summary>
            private const float ShotHold = 0.6f;

            private const float ShotEarliest = 0.15f;

            /// <summary>The share of a close view, either side of its middle, that a run of presses must fit inside.</summary>
            private const float ShotMiddle = 0.6f;

            /// <summary>How much wider than a composition the camera's view stays, so the composition reads whole across.</summary>
            private const float WidthRoom = 1.06f;
        }

        /// <summary>A part the scene draws: a subject and a labelled control the pointer clicks.</summary>
        /// <remarks>
        /// The kit control the pointer is on shows the kit's own hover look, and its press look while the pointer holds
        /// down, over the states it holds, as a real pointer would raise them.
        /// </remarks>
        protected abstract class DrawnPart : Part
        {
            private Vector2 pointerStart;
            private Beat[] beats;
            private float tempo = 1f;

            protected DrawnPart(Stage stage, string name, Vector2 pointerStart, Beat[] beats, float pace = 1f)
                : base(stage, name) => Plan(pointerStart, beats, pace);

            /// <summary>The pace of a part whose pointer picks one option after another along a row: brisker than a hand's.</summary>
            protected const float SwitchPace = 1.4f;

            /// <summary>A part whose pointer is planned once its content is built, with <see cref="Plan"/>.</summary>
            protected DrawnPart(Stage stage, string name) : base(stage, name)
            {
            }

            /// <summary>
            /// Sets where the pointer starts, what it does and how briskly; called before the pointer is built.
            /// </summary>
            protected void Plan(Vector2 start, Beat[] script, float pace = 1f)
            {
                pointerStart = start;
                beats = script;
                tempo = pace;
            }

            public override float ActionSeconds => Pointer.Timeline.Total;

            public override void BuildPointer(Stage stage) => Pointer = stage.Pointer(Magnified(pointerStart),
                System.Array.ConvertAll(beats ?? throw new System.InvalidOperationException($"[Promo] Part '{Root.name}' was never planned."),
                    beat => beat.Kind is BeatKind.To or BeatKind.Drag ? beat.Retargeted(Magnified(beat.Point), beat.TargetWidth * Magnify) : beat),
                tempo: tempo);

            /// <summary>When the click named <paramref name="id"/> presses, relative to the action's start.</summary>
            protected float ClickAt(string id) => Pointer.Timeline.Span(id).x;


            protected override void PoseAction(float played)
            {
                Pointer.Pose(Mathf.Max(0f, played));
                Touch(Mathf.Max(0f, played));
            }

            private InteractionStateHost touched;

            /// <summary>
            /// Shows the kit's hover look, and its press look while the pointer holds down, on the control the pointer is
            /// on at <paramref name="played"/>, over the states that control holds, as a real pointer would raise them;
            /// the control it leaves shows its own states again.
            /// </summary>
            private void Touch(float played)
            {
                var under = Pointer.Visible ? Under(Pointer.On(played)) : null;
                if (under != touched)
                {
                    Show(touched, null);
                    touched = under;
                }

                if (touched == null) return;
                var look = touched.States;
                look.Set(InteractionStates.Hovered, true);
                look.Set(InteractionStates.Pressed, Pointer.Timeline.Sample(played).Press > PressedFrom);
                Show(touched, look);
            }

            /// <summary>
            /// The interactable control of the part a pointer at <paramref name="world"/> would be over: the one holding the
            /// frontmost graphic the canvas raycasters hit there, as the input module finds it, so a control's own shape and
            /// what draws above it decide, not its rect.
            /// </summary>
            private Interactable Under(Vector3 world)
            {
                var events = UnityEngine.EventSystems.EventSystem.current;
                if (events == null) return null;
                var canvas = Root.GetComponentInParent<Canvas>().rootCanvas;
                var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                raycast ??= new UnityEngine.EventSystems.PointerEventData(events);
                raycast.position = RectTransformUtility.WorldToScreenPoint(camera, world);
                events.RaycastAll(raycast, hits);
                var front = hits.Count > 0 ? hits[0].gameObject : null;
                if (front != null) raycast.pointerCurrentRaycast = hits[0];
                hits.Clear();
                if (front == null || !front.transform.IsChildOf(Root)) return null;
                var control = Handle(front) ?? front.GetComponentInParent<Interactable>();
                return control != null && control.enabled && control.IsInteractable ? control : null;
            }

            /// <summary>
            /// The handle a value under the pointer passes its hover on to, as the kit's value does: the one riding the end
            /// a press there would take; null when nothing at <paramref name="front"/> is such a value.
            /// </summary>
            private Interactable Handle(GameObject front)
            {
                foreach (var value in front.GetComponents<UniShapeValue>())
                {
                    if (value.Gesture == null) continue;
                    var end = value.Gesture.EndAt(value, raycast);
                    return end.Keys != null ? end.Keys : end.GetComponent<Interactable>();
                }

                return null;
            }

            private UnityEngine.EventSystems.PointerEventData raycast;
            private static readonly List<UnityEngine.EventSystems.RaycastResult> hits = new();

            /// <summary>Previews <paramref name="look"/> on every shape <paramref name="host"/> drives, or hands them back to its own states.</summary>
            private static void Show(InteractionStateHost host, InteractionStateSet? look)
            {
                if (host == null) return;
                foreach (var states in host.GetComponentsInChildren<UniShapeStates>(true))
                {
                    var driver = states.Host != null ? states.Host : states.GetComponentInParent<InteractionStateHost>(true);
                    if (driver == host && !Equals(states.PreviewStates, look)) states.PreviewStates = look;
                }
            }

            /// <summary>How far down the pointer's button must be for the control under it to look pressed.</summary>
            private const float PressedFrom = 0.5f;
        }

        /// <summary>A part that plays a recorded Inspector window beside its subject.</summary>
        protected sealed class RecordedPart : Part
        {
            private readonly TakeShot take;
            private readonly System.Action<float> after;

            public RecordedPart(Stage stage, string name, TakeShot take, System.Action<float> after) : base(stage, name)
            {
                this.take = take;
                this.after = after;
            }

            public override TakeShot Take => take;

            public override float ActionSeconds => take.Seconds(UnrecordedTake);

            public override void BuildPointer(Stage stage) => Pointer = take.BuildPointer(stage);

            protected override void PoseAction(float played)
            {
                take.Pose(played);
                after?.Invoke(played);
            }

            /// <summary>Writes the take's values into its subject; called every frame, on screen or not.</summary>
            public void Apply(float local) => take.Apply(local - take.Start);
        }

        /// <summary>
        /// A switch of options in a capsule. Its highlight runs to whichever option was clicked last like a drop of
        /// liquid: the leading edge darts ahead and the trailing edge catches up, the highlight stretched thin between
        /// them.
        /// </summary>
        protected sealed class Switch
        {
            private readonly RectTransform highlight;
            private readonly Vector2[] cells;

            public Switch(Stage stage, Transform parent, Theme theme, Vector2 center, Vector2 cell, int count)
            {
                Cell = cell;
                cells = new Vector2[count];
                for (var i = 0; i < count; i++)
                    cells[i] = center + new Vector2((i - (count - 1) * 0.5f) * cell.x, 0f);

                Rect = stage.Stretch(stage.Node("Switch", parent));
                var size = new Vector2(cell.x * count + SwitchInset * 2f, cell.y + SwitchInset * 2f);

                var track = stage.Shape("Track", Rect, ShapeKind.Capsule);
                stage.Box(track.Rect, center, size);
                Stage.Solid(track.Fill, Theme.Sink(theme.Surface, 0.3f));
                Stage.AddStroke(track.Shape, Theme.Fade(theme.Line, 0.6f), 1.5f, -1f);

                var light = stage.Shape("Selected", Rect, ShapeKind.Capsule);
                highlight = light.Rect;
                stage.Box(highlight, cells[0], cell);
                Stage.Solid(light.Fill, Theme.Mix(theme.Surface, theme.Orange, 0.08f));
                Stage.AddShadow(light.Shape, new Color(0f, 0f, 0f, 0.45f), new Vector2(0f, -3f), 10f);
            }

            /// <summary>The node everything on the switch is built under, so it moves as one surface.</summary>
            public RectTransform Rect { get; }

            public Vector2 Cell { get; }

            public Vector2 this[int index] => cells[index];

            /// <summary>
            /// How selected each option is at <paramref name="played"/>, given the moments <paramref name="clicks"/>
            /// pressed options 1, 2, … in turn after option 0, and runs the highlight to match.
            /// </summary>
            public void Pose(float played, IReadOnlyList<float> clicks, float[] weights)
            {
                var half = Cell.x * 0.5f;
                var left = cells[0].x - half;
                var right = cells[0].x + half;
                for (var i = 0; i < weights.Length; i++) weights[i] = 0f;
                weights[0] = 1f;
                for (var i = 0; i < clicks.Count; i++)
                {
                    var step = cells[i + 1].x - cells[i].x;
                    var lead = Motion.Snap.Window(played, clicks[i], LiquidLead);
                    var trail = Motion.Back.Window(played, clicks[i] + LiquidLag, LiquidTrail);
                    left += step * (step > 0f ? trail : lead);
                    right += step * (step > 0f ? lead : trail);
                    weights[i] -= trail;
                    weights[i + 1] += trail;
                }

                var stretch = Mathf.Clamp01((right - left - Cell.x) / Cell.x);
                highlight.anchoredPosition = new Vector2((left + right) * 0.5f, cells[0].y);
                highlight.sizeDelta = new Vector2(right - left, Cell.y * (1f - LiquidThin * stretch));
            }
        }

        /// <summary>
        /// How much larger an option's icon or label stands for a moment after it is clicked: a kick that rises and
        /// falls back within <see cref="KickFor"/>.
        /// </summary>
        protected static float Kick(float played, float click) =>
            1f + KickAmount * Mathf.Sin(Mathf.PI * Mathf.Clamp01((played - click) / KickFor));

        /// <summary>A card surface on the theme's warm dark, lifted off the frame by a soft shadow.</summary>
        /// <remarks>
        /// The surface is used as it is. <see cref="Theme.Lift"/> mixes in linear light, where even a few percent of
        /// white swamps a dark color and washes its hue out to gray.
        /// </remarks>
        protected Widget Panel(Stage stage, Transform parent, string name, Vector2 at, Vector2 size, float radius)
        {
            var panel = stage.Shape(name, parent, ShapeKind.RoundedRect, radius);
            stage.Box(panel.Rect, at, size);
            Stage.Solid(panel.Fill, theme.Surface);
            Stage.AddShadow(panel.Shape, new Color(0f, 0f, 0f, 0.55f), new Vector2(0f, -16f), 48f);
            return panel;
        }

        /// <summary>A beat's subject: the brand ramp, glowing over the frame.</summary>
        protected Widget Hero(Stage stage, Transform parent, string name, Vector2 at, Vector2 size, float radius)
        {
            var hero = stage.Shape(name, parent, ShapeKind.RoundedRect, radius);
            stage.Box(hero.Rect, at, size);
            Stage.Ramped(hero.Fill, brand, PaintProjectionKind.Linear, 35f);
            Stage.AddShadow(hero.Shape, Theme.Fade(theme.Coral, 0.35f), new Vector2(0f, -16f), 50f);
            return hero;
        }

        protected static PrimitiveShapeProvider Rounded(float radius, float smoothing) =>
            new PrimitiveShapeProvider { Kind = ShapeKind.RoundedRect, Radius = radius, Smoothing = smoothing };

        /// <summary>How long a dropped composition takes to land, its bounce included.</summary>
        private const float DropFor = 0.42f;

        /// <summary>
        /// How small a zoomed composition starts and how large a zoomed-through one ends; how far a pushed card sinks
        /// back under the next.
        /// </summary>
        private const float ZoomFrom = 0.82f;

        private const float ZoomTo = 1.3f;
        private const float PushTo = 0.9f;


        /// <summary>How far under its control a drawn part's pointer starts, inside the frame.</summary>
        protected const float PointerDrop = 110f;

        private const float SwitchInset = 6f;

        /// <summary>
        /// The liquid highlight: how fast its leading edge reaches the new option, how long the trailing edge waits and
        /// takes to follow, and how much thinner the highlight runs while stretched to twice its width.
        /// </summary>
        private const float LiquidLead = 0.16f;

        private const float LiquidLag = 0.05f;
        private const float LiquidTrail = 0.32f;
        private const float LiquidThin = 0.22f;

        /// <summary>A clicked option's kick: how much larger it grows, and how long it takes to grow and settle back.</summary>
        private const float KickAmount = 0.28f;

        private const float KickFor = 0.28f;
        private const float UnrecordedTake = 3.5f;
    }
}
