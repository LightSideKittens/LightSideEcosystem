using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LightSide.Promo
{
    /// <summary>The boards a <see cref="GuiKitSection"/>'s beats are built on.</summary>
    /// <remarks>
    /// Pointer targets are written in a control's own units from its center, as its layout places its parts: a
    /// control lays itself out only once enabled, after the pointer's path has been compiled.
    /// </remarks>
    public abstract partial class GuiKitSection
    {
        /// <summary>
        /// A beat on a board of its own: a card in the middle of the content band, painted the kit's surface the controls
        /// are drawn to sit on, and the controls on it, built at their shipped size inside a holder scaled up as one. The
        /// board is the app's window: what lies beyond it — a sheet below its edge — is cut off, as a screen's edge would.
        /// </summary>
        /// <remarks>
        /// Every press of the pointer must land on a control: posed past a press, the part logs an error if nothing on
        /// the board that takes the pointer holds the point pressed, and for a press with an <see cref="Aim"/> unless its
        /// own target holds it. It logs rather than throws so one render lists every miss; a reel drops a slide that throws.
        /// <para>
        /// A board in depth cuts at its window through the stencil, where the window is drawn. A rect mask takes its clip in
        /// the canvas plane and cuts content turned out of that plane where it would lie in the plane, which the perspective
        /// lens draws past the board's edge on the near side and short of it on the far side.
        /// </para>
        /// </remarks>
        protected abstract class KitPart : DrawnPart
        {
            private readonly GuiKitSection scene;
            private readonly Stage stage;
            private readonly Vector2 center;
            private readonly float scale;
            private readonly Vector2 board;
            private readonly bool inDepth;
            private int pressesChecked;
            private readonly List<(string id, Func<RectTransform> target)> aims = new();
            private readonly List<(string id, Func<RectTransform> target)> squashes = new();

            protected KitPart(GuiKitSection scene, Stage stage, string name, Vector2 board, float scale, bool inDepth = false)
                : base(stage, name)
            {
                this.scene = scene;
                this.stage = stage;
                this.scale = scale;
                this.board = board;
                this.inDepth = inDepth;
                center = new Vector2(0f, scene.contentCenter);
                Zoom = stage.Stretch(stage.Node("Zoom", Root));
                var surface = scene.Panel(stage, Zoom, "Board", center, board, BoardRadius);
                Stage.Solid(surface.Fill, scene.palette["Surface"]);
                Holder = stage.Node("Controls", Zoom);
                stage.Box(Holder, center, board / scale);
                Holder.localScale = Vector3.one * scale;
                if (inDepth)
                {
                    Holder.gameObject.AddComponent<UnityEngine.UI.Image>().raycastTarget = false;
                    Holder.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;
                }
                else Holder.gameObject.AddComponent<LightSideRectMask>();
            }

            public override bool InDepth => inDepth;

            /// <summary>
            /// Whether the camera takes close shots of what the board's pointer works on, for a board whose action is a
            /// small detail; a board without it holds the whole frame still.
            /// </summary>
            protected bool Closeup { get; set; }

            protected override float CameraZoom => Closeup ? CloseupPush : base.CameraZoom;

            protected override Rect? Composition => new Rect(center - board * 0.5f, board);

            /// <summary>The node the board and its controls hang under, which the closing dive scales.</summary>
            protected RectTransform Zoom { get; }

            protected RectTransform Holder { get; }

            /// <summary>Where the point <paramref name="native"/> of the holder stands on the stage.</summary>
            protected Vector2 At(Vector2 native) => center + native * scale;

            /// <summary>How wide <paramref name="native"/> units of the holder stand on the stage.</summary>
            protected float Wide(float native) => native * scale;

            /// <summary>Where the stage point <paramref name="point"/> stands in the holder.</summary>
            protected Vector2 Native(Vector2 point) => (point - center) / scale;

            /// <summary>Where a pointer starts: under the board's right half, inside the frame.</summary>
            protected Vector2 Start => center + new Vector2(board.x * 0.3f, -board.y * 0.5f - PointerDrop);

            /// <summary>
            /// The kit's <paramref name="control"/> on the board, hung at <paramref name="native"/> by the point
            /// <paramref name="edge"/> of its width, the pointer taking over its parts the cursors they ask the kit for.
            /// </summary>
            protected GameObject Add(string control, Vector2 native, float edge = 0.5f, float rise = 0.5f)
            {
                var instance = Kit.Spawn(Kit.Find(scene.kit, control), Holder, scene.palette);
                Kit.Place(instance, native, edge, rise);
                Claim(instance);
                return instance;
            }

            /// <summary>Gives <paramref name="control"/> <paramref name="size"/>, held by its own layout rather than the size it hugs.</summary>
            protected static void Size(GameObject control, Vector2 size)
            {
                var layout = control.GetComponent<UniLayout>();
                layout.Width = LayoutSize.Fixed;
                layout.Height = LayoutSize.Fixed;
                ((RectTransform)control.transform).sizeDelta = size;
            }

            /// <summary>
            /// Registers the cursor each part of <paramref name="control"/> asks the kit for over its own rect: a value
            /// gesture's declared cursor, the text beam over editable text, the column cursor over a column's resize edge,
            /// a window's resize cursors over its edges and corners.
            /// A control whose root takes the pointer first claims the plain cursor over all of itself, hiding what earlier
            /// controls claim beneath it, as it hides them from the pointer.
            /// </summary>
            private void Claim(GameObject control)
            {
                if (control.TryGetComponent<UniShapeBase>(out var face) && face.RaycastTarget)
                    stage.Cursors.Add((RectTransform)control.transform, CursorType.Default);
                foreach (var value in control.GetComponentsInChildren<UniShapeValue>(true))
                    if (value.Cursor != CursorType.Default)
                        stage.Cursors.Add((RectTransform)value.transform, value.Cursor);
                foreach (var editable in control.GetComponentsInChildren<UniTextEditable>(true))
                    stage.Cursors.Add((RectTransform)editable.transform, CursorType.Text);
                foreach (var edge in control.GetComponentsInChildren<UniColumnResize>(true))
                    stage.Cursors.Add((RectTransform)edge.transform, CursorType.ColResize);
                foreach (var handle in control.GetComponentsInChildren<UniWindowHandle>(true))
                    if (handle.Cursor != CursorType.Default)
                        stage.Cursors.Add((RectTransform)handle.transform, handle.Cursor);
            }

            /// <summary>
            /// A line of text on the board in the kit's <paramref name="swatch"/> color, hung at <paramref name="native"/>
            /// by the point <paramref name="edge"/> of its width; the display face when <paramref name="display"/>.
            /// </summary>
            protected UniText Label(string text, Vector2 native, float size, string swatch, float edge = 0f,
                bool display = false)
            {
                var label = stage.Label(Holder, text, size, scene.palette[swatch],
                    edge <= 0f ? HorizontalAlignment.Left : edge >= 1f ? HorizontalAlignment.Right : HorizontalAlignment.Center,
                    stretch: false, face: display ? scene.theme.DisplayFace : null);
                label.WordWrap = false;
                var rect = label.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(edge, 0.5f);
                rect.sizeDelta = new Vector2(LabelWidth, size * 1.6f);
                rect.anchoredPosition = native;
                return label;
            }

            /// <summary>
            /// A rounded banner in the brand ramp behind everything else under <paramref name="parent"/>, filling it inset
            /// by <paramref name="inset"/>, its ramp turned <paramref name="angle"/> degrees.
            /// </summary>
            protected void Banner(Transform parent, float inset, float angle)
            {
                var banner = stage.Shape("Banner", parent, ShapeKind.RoundedRect, BannerRadius);
                stage.Stretch(banner.Rect, inset, inset, inset, inset);
                Stage.Ramped(banner.Fill, scene.brand, PaintProjectionKind.Linear, angle);
                banner.Rect.SetAsFirstSibling();
            }

            /// <summary>A round avatar in the brand ramp with <paramref name="initial"/> on it, <paramref name="size"/> across.</summary>
            protected RectTransform Avatar(Vector2 native, float size, string initial, float angle)
            {
                var face = stage.Shape("Avatar", Holder, ShapeKind.Circle);
                stage.Box(face.Rect, native, Vector2.one * size);
                Stage.Ramped(face.Fill, scene.brand, PaintProjectionKind.Linear, angle);
                var letter = stage.Label(face.Rect, initial, size * 0.48f, Color.white, stretch: true, face: scene.theme.DisplayFace);
                letter.WordWrap = false;
                return face.Rect;
            }

            /// <summary>
            /// A row the kit's layout lays out, hung at <paramref name="native"/> by its left end, holding
            /// <paramref name="controls"/> side by side <paramref name="gap"/> apart, so one growing moves the rest along.
            /// </summary>
            protected RectTransform Strip(Vector2 native, float gap, params GameObject[] controls)
            {
                var row = stage.Node("Row", Holder);
                row.anchorMin = row.anchorMax = new Vector2(0.5f, 0.5f);
                row.pivot = new Vector2(0f, 0.5f);
                row.anchoredPosition = native;
                var layout = row.gameObject.AddComponent<UniLayout>();
                layout.Mode = LayoutMode.Row;
                layout.Gap = new Vector2(gap, gap);
                layout.AlignY = LayoutAlign.Center;
                layout.Width = LayoutSize.Hug;
                layout.Height = LayoutSize.Hug;
                foreach (var control in controls) control.transform.SetParent(row, false);
                return row;
            }

            /// <summary>
            /// A game for the board's controls to stand over, <paramref name="size"/> across in the holder's units: a dusk
            /// sky, a low sun and two ridges of hills, cut to the board's rounded corners by the sky's own shape.
            /// </summary>
            protected void Scenery(Stage stage, Vector2 size)
            {
                var sky = stage.Shape("Sky", Holder, ShapeKind.RoundedRect, BoardRadius / scale);
                stage.Box(sky.Rect, Vector2.zero, size);
                Stage.Linear(sky.Fill, Dusk, Sky, 270f);
                sky.Rect.gameObject.AddComponent<UniShapeMask>().ShowShape = true;
                var sun = stage.Shape("Sun", sky.Rect, ShapeKind.Circle);
                stage.Box(sun.Rect, new Vector2(size.x * 0.16f, -size.y * 0.04f), Vector2.one * 170f);
                Stage.Radial(sun.Fill, Sun, Theme.Fade(Sun, 0f));
                Ridge(stage, sky.Rect, "Far Hills", new Vector2(-size.x * 0.22f, -size.y * 0.34f), new Vector2(size.x * 1.3f, size.y * 0.5f), FarHills);
                Ridge(stage, sky.Rect, "Near Hills", new Vector2(size.x * 0.3f, -size.y * 0.48f), new Vector2(size.x * 1.2f, size.y * 0.46f), NearHills);
            }

            private static void Ridge(Stage stage, RectTransform sky, string name, Vector2 at, Vector2 size, Color color)
            {
                var hill = stage.Shape(name, sky, ShapeKind.Ellipse);
                stage.Box(hill.Rect, at, size);
                Stage.Solid(hill.Fill, color);
            }


            private static readonly Color Sky = new Color32(0xF2, 0x8C, 0x4B, 0xFF);
            private static readonly Color Dusk = new Color32(0x3A, 0x22, 0x2E, 0xFF);
            private static readonly Color Sun = new Color32(0xFF, 0xD2, 0x7A, 0xFF);
            private static readonly Color FarHills = new Color32(0x5A, 0x2E, 0x3A, 0xFF);
            private static readonly Color NearHills = new Color32(0x2E, 0x1C, 0x22, 0xFF);

            /// <summary>
            /// Dives the camera at the stage point <paramref name="target"/> while the section ends on this board: the board
            /// grows about it, faster and faster, the pointer riding along, as the next slide's entrance carries the move on.
            /// </summary>
            protected void Dive(float played, Vector2 target)
            {
                if (!Dives) return;
                var dive = Mathf.Clamp01((played - (Out - ActionAt)) / Leaving);
                var zoom = Mathf.Pow(DiveScale, Mathf.Pow(dive, DiveCurve));
                Zoom.localScale = Vector3.one * zoom;
                Zoom.anchoredPosition = target * (1f - Motion.Snap.Evaluate(dive)) - target * zoom;
                var pointer = Pointer.Timeline.Sample(played).Position;
                Pointer.Offset += pointer * zoom + Zoom.anchoredPosition - pointer;
            }

            /// <summary>How many times its size a board grows to as the camera dives at it, and how slowly the dive starts.</summary>
            private const float DiveScale = 2.5f;

            private const float DiveCurve = 1.6f;

            /// <summary>
            /// A dark veil over the whole board, under whatever is added after it; returns it so its opacity can follow
            /// a modal surface.
            /// </summary>
            protected UniShape Veil()
            {
                var veil = stage.Shape("Veil", Holder, ShapeKind.RoundedRect, BoardRadius / scale);
                stage.Box(veil.Rect, Vector2.zero, board / scale);
                Stage.Solid(veil.Fill, new Color(0f, 0f, 0f, VeilStrength));
                return veil.Shape;
            }

            /// <summary>A hairline across the board in the kit's line color, <paramref name="width"/> long, centered on <paramref name="native"/>.</summary>
            protected void Divider(Vector2 native, float width)
            {
                var line = stage.Shape("Divider", Holder, ShapeKind.RoundedRect);
                stage.Box(line.Rect, native, new Vector2(width, DividerWidth));
                Stage.Solid(line.Fill, scene.palette["Line"]);
            }

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                var presses = Pointer.Timeline.Presses;
                while (pressesChecked < presses.Count && played >= presses[pressesChecked].Down)
                {
                    var press = presses[pressesChecked++];
                    if (played - press.Down <= CheckWindow) Land(press.Point);
                }
                for (var i = aims.Count - 1; i >= 0; i--)
                {
                    var (id, target) = aims[i];
                    var at = Pointer.Timeline.Span(id).x;
                    if (played < at) continue;
                    aims.RemoveAt(i);
                    if (played - at <= CheckWindow) Land(id, Pointer.Timeline.Sample(at).Position, target());
                }

                foreach (var (id, target) in squashes) Squash(id, target, played);
            }

            /// <summary>
            /// Presses the control clicked as <paramref name="id"/> in and lets it spring back: it gives under the press and
            /// rings out on release, about its middle. A drag, and a control hung by any other point, stand as they are.
            /// </summary>
            private void Squash(string id, Func<RectTransform> target, float played)
            {
                var span = Pointer.Timeline.Span(id);
                var held = span.y - span.x;
                var since = played - span.x;
                if (held > ClickLength || since < 0f || since > held + RingOut + SquashTail) return;
                var rect = target();
                if (rect == null || rect.pivot != new Vector2(0.5f, 0.5f)) return;
                var give = since < held
                    ? Mathf.Lerp(1f, Pressed, Ease.Emphasized.Evaluate(since / held))
                    : 1f + (Pressed - 1f) * Mathf.Exp(-RingDecay * (since - held)) * Mathf.Cos(RingRate * (since - held));
                rect.localScale = Vector3.one * (since > held + RingOut ? 1f : give);
            }

            /// <summary>
            /// Clicks <paramref name="control"/> through its own pointer handlers — down, up, click — as a press of the left
            /// button would, so it takes the press the way the kit takes a person's: focus as from a pointer, and whatever
            /// its click does.
            /// </summary>
            protected static void Click(GameObject control)
            {
                var press = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
                ExecuteEvents.Execute(control, press, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute(control, press, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.Execute(control, press, ExecuteEvents.pointerClickHandler);
            }

            /// <summary>Requires the press named <paramref name="id"/> to land inside what <paramref name="target"/> answers as it presses.</summary>
            protected void Aim(string id, Func<RectTransform> target)
            {
                aims.Add((id, target));
                squashes.Add((id, target));
            }

            /// <summary>Requires the press named <paramref name="id"/> to land inside <paramref name="target"/>.</summary>
            protected void Aim(string id, Component target) => Aim(id, () => (RectTransform)target.transform);

            /// <summary>Requires the press named <paramref name="id"/> to land inside <paramref name="target"/>.</summary>
            protected void Aim(string id, GameObject target) => Aim(id, () => (RectTransform)target.transform);

            /// <summary>The first part of <paramref name="control"/> named <paramref name="name"/>, looked up as a press needs it; null when it has none.</summary>
            protected static Func<RectTransform> Named(GameObject control, string name) => () =>
            {
                foreach (var part in control.GetComponentsInChildren<RectTransform>(true))
                    if (part.name == name)
                        return part;
                return null;
            };

            /// <summary>
            /// The interactable showing <paramref name="caption"/> under <paramref name="control"/>, looked up as a press
            /// needs it, so rows and copies built after the plan count; null while it shows none.
            /// </summary>
            protected static Func<RectTransform> Captioned(GameObject control, string caption) => () =>
            {
                foreach (var text in control.GetComponentsInChildren<UniText>())
                    if (text.Text == caption && text.GetComponentInParent<Interactable>() is { } owner)
                        return (RectTransform)owner.transform;
                return null;
            };

            /// <summary>Logs an error unless <paramref name="target"/> exists and holds the stage point <paramref name="point"/> the press named <paramref name="id"/> lands on.</summary>
            private void Land(string id, Vector2 point, RectTransform target)
            {
                if (target == null)
                {
                    Debug.LogError($"[Promo] Press '{id}' of '{Root.name}' finds no target as it presses.");
                    return;
                }

                var world = Holder.TransformPoint(Native(point));
                if (target.rect.Contains(target.InverseTransformPoint(world))) return;
                var center = (Vector2)Holder.InverseTransformPoint(target.TransformPoint(target.rect.center));
                Debug.LogError(
                    $"[Promo] Press '{id}' of '{Root.name}' lands at {Native(point)} in its holder, outside '{target.name}' " +
                    $"centered at {center}, {target.rect.size} large.");
            }

            /// <summary>Logs an error unless something on the board that takes the pointer holds the stage point <paramref name="point"/>.</summary>
            private void Land(Vector2 point)
            {
                var native = Native(point);
                var world = Holder.TransformPoint(native);
                foreach (var receiver in Holder.GetComponentsInChildren<UnityEngine.EventSystems.IEventSystemHandler>())
                {
                    if (receiver is not Behaviour { enabled: true } behaviour) continue;
                    var rect = (RectTransform)behaviour.transform;
                    if (rect.rect.Contains(rect.InverseTransformPoint(world))) return;
                }

                Debug.LogError($"[Promo] A press of '{Root.name}' at {native} in its holder lands on no control.");
            }

            /// <summary>
            /// A wheel flicked by the drag named <see cref="Id"/>: its scroll follows the pointer while the drag holds it,
            /// then coasts on to rest <see cref="Cells"/> cells past where the press found it, where the wheel takes the
            /// number in its middle as its value.
            /// </summary>
            /// <remarks>
            /// The scroll is moved by each pose's step rather than to an offset: a looping wheel moves its cells round and
            /// takes their length back off its offset as it does, so an offset written from the start would undo that.
            /// </remarks>
            protected sealed class Flick
            {
                public Flick(UniShapeValue wheel, string id, int cells)
                {
                    Scroll = wheel.GetComponentInChildren<UniLayoutScroll>(true);
                    Id = id;
                    Cells = cells;
                }

                public UniLayoutScroll Scroll { get; }
                public string Id { get; }
                public int Cells { get; }

                /// <summary>How far the wheel has been turned so far, in its own units; each pose turns it by the rest.</summary>
                public float Turned { get; set; }

                public bool Rested { get; set; }
            }

            /// <summary>Turns <paramref name="flick"/>'s wheel for <paramref name="played"/>: under the pointer, then coasting to rest.</summary>
            protected void Pose(Flick flick, float played)
            {
                var span = Pointer.Timeline.Span(flick.Id);
                if (played < span.x || flick.Rested) return;
                var pressed = Native(Pointer.Timeline.Sample(span.x).Position).y;
                var held = Native(Pointer.Timeline.Sample(Mathf.Min(played, span.y)).Position).y;
                var cell = ((RectTransform)flick.Scroll.transform.GetChild(0)).rect.height;
                var coast = Mathf.Clamp01((played - span.y) / CoastSeconds);
                var settled = 1f - (1f - coast) * (1f - coast) * (1f - coast);
                var travel = Mathf.Lerp(held - pressed, flick.Cells * cell, settled);
                flick.Scroll.ScrollBy(new Vector2(0f, travel - flick.Turned), false);
                flick.Turned = travel;
                flick.Rested = coast >= 1f;
            }

            /// <summary>The pointer's drag named <paramref name="id"/>, handed to a control's own pointer and drag handlers.</summary>
            protected sealed class Grip
            {
                public Grip(GameObject control, string id)
                {
                    Control = control;
                    Id = id;
                }

                public GameObject Control { get; }
                public string Id { get; }

                /// <summary>The press the drag runs on, from the moment it went down.</summary>
                public PointerEventData Press { get; set; }

                /// <summary>What takes the drag: the nearest drag handler at or above the control.</summary>
                public GameObject Dragged { get; set; }

                public bool Dragging { get; set; }
                public bool Released { get; set; }
            }

            /// <summary>
            /// Delivers <paramref name="grip"/>'s drag up to <paramref name="played"/> as an input module delivers a hand's:
            /// the press, the drag starting once the pointer moves, a move every pose, then the release — so the control
            /// answers the drag with its own behavior.
            /// </summary>
            protected void Pose(Grip grip, float played)
            {
                var span = Pointer.Timeline.Span(grip.Id);
                if (played < span.x || grip.Released) return;
                var at = Screen(Pointer.Timeline.Sample(Mathf.Min(played, span.y)).Position);
                if (grip.Press == null)
                {
                    var camera = Holder.GetComponentInParent<BaseRaycaster>()
                                 ?? throw new InvalidOperationException($"[Promo] '{Root.name}' stands on no canvas a pointer reaches.");
                    var hit = Hit(grip.Control, Holder.TransformPoint(Native(Pointer.Timeline.Sample(span.x).Position)));
                    grip.Press = new PointerEventData(EventSystem.current)
                    {
                        button = PointerEventData.InputButton.Left,
                        position = at,
                        pressPosition = at,
                        pointerPressRaycast = new RaycastResult { module = camera, gameObject = hit },
                        pointerCurrentRaycast = new RaycastResult { module = camera, gameObject = hit },
                        eligibleForClick = true
                    };
                    ExecuteEvents.ExecuteHierarchy(hit, grip.Press, ExecuteEvents.pointerDownHandler);
                    grip.Dragged = ExecuteEvents.GetEventHandler<IDragHandler>(hit);
                    if (grip.Dragged != null)
                        ExecuteEvents.Execute(grip.Dragged, grip.Press, ExecuteEvents.initializePotentialDrag);
                }

                if (at != grip.Press.position)
                {
                    grip.Press.delta = at - grip.Press.position;
                    grip.Press.position = at;
                    if (!grip.Dragging && grip.Dragged != null)
                    {
                        grip.Dragging = true;
                        grip.Press.dragging = true;
                        ExecuteEvents.Execute(grip.Dragged, grip.Press, ExecuteEvents.beginDragHandler);
                    }
                    if (grip.Dragging) ExecuteEvents.Execute(grip.Dragged, grip.Press, ExecuteEvents.dragHandler);
                }

                if (played < span.y) return;
                grip.Released = true;
                ExecuteEvents.ExecuteHierarchy(grip.Press.pointerPressRaycast.gameObject, grip.Press, ExecuteEvents.pointerUpHandler);
                if (grip.Dragging) ExecuteEvents.Execute(grip.Dragged, grip.Press, ExecuteEvents.endDragHandler);
                grip.Press.dragging = false;
            }

            /// <summary>
            /// What a press at <paramref name="world"/> lands on within <paramref name="control"/>, as a raycast finds it:
            /// the deepest part taking pointer events whose rect holds the point, or the control itself.
            /// </summary>
            private static GameObject Hit(GameObject control, Vector3 world)
            {
                var found = control;
                var depth = -1;
                foreach (var part in control.GetComponentsInChildren<RectTransform>())
                {
                    if (!part.TryGetComponent<IEventSystemHandler>(out _)) continue;
                    if (!part.rect.Contains(part.InverseTransformPoint(world))) continue;
                    var level = 0;
                    for (var node = part.parent; node != null; node = node.parent) level++;
                    if (level <= depth) continue;
                    depth = level;
                    found = part.gameObject;
                }

                return found;
            }

            /// <summary>Where the stage point <paramref name="point"/> stands on the screen the canvas draws to.</summary>
            private Vector2 Screen(Vector2 point)
            {
                var canvas = Holder.GetComponentInParent<Canvas>().rootCanvas;
                var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                return RectTransformUtility.WorldToScreenPoint(camera, Holder.TransformPoint(Native(point)));
            }

            /// <summary>
            /// How far the pointer pressing <paramref name="id"/> has dragged at <paramref name="played"/>, as a share of
            /// the way from <paramref name="from"/> to <paramref name="to"/> along that line, held at the release.
            /// </summary>
            protected float Dragged(string id, float played, Vector2 from, Vector2 to)
            {
                var span = Pointer.Timeline.Span(id);
                var at = Pointer.Timeline.Sample(Mathf.Clamp(played, span.x, span.y)).Position;
                var line = to - from;
                return Mathf.Clamp01(Vector2.Dot(at - from, line) / line.sqrMagnitude);
            }
        }


        protected const float BoardRadius = 32f;

        /// <summary>
        /// How long after a press a pose may still check where it landed: a render that starts past a press skips its
        /// check, since what it pressed may have closed since.
        /// </summary>
        protected const float CheckWindow = 0.1f;

        /// <summary>
        /// How a clicked control gives and rings out: pressed in to <see cref="Pressed"/> of its size, then a decaying
        /// swing over <see cref="RingOut"/>; a press held longer than <see cref="ClickLength"/> is a drag.
        /// </summary>
        private const float Pressed = 0.93f;

        private const float RingOut = 0.45f;
        private const float RingDecay = 5.5f;
        private const float RingRate = 16f;
        private const float ClickLength = 0.3f;

        /// <summary>How long past its swing a squash still writes its rest size, so a pose a frame late lands on it.</summary>
        private const float SquashTail = 0.05f;


        /// <summary>How long a flicked wheel coasts from the release to rest.</summary>
        protected const float CoastSeconds = 0.6f;

        /// <summary>How long a spinner takes to grow in as its wait begins.</summary>
        protected const float SpinnerGrow = 0.3f;

        /// <summary>The widest a board label lays out, and how thick a divider is drawn, in the holder's units.</summary>
        protected const float LabelWidth = 420f;

        protected const float DividerWidth = 1f;
        protected const float BannerRadius = 14f;
        protected const float VeilStrength = 0.55f;

        /// <summary>How far the camera pushes in on what a close-up board's pointer works on.</summary>
        private const float CloseupPush = 1.25f;
    }
}
