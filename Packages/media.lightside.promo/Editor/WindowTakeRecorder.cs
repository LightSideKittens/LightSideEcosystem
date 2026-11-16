using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide.Promo
{
    /// <summary>
    /// Records every <see cref="TakeShot"/> of the open reel's sections in turn: a floating
    /// Inspector on each take's subject, driven frame by frame through its own pointer events and photographed after
    /// each.
    /// </summary>
    /// <remarks>
    /// The pointer path is the take's script compiled into a <see cref="PointerTimeline"/> in stage coordinates, the
    /// same timeline the scene replays. Each frame samples it, maps the arrow into the window, sends the press, drag or
    /// move the window would get from a mouse there, lets the editor repaint the window, and then captures it and reads
    /// the subject's values. Before a take is measured, the foldouts its crop names are opened by clicks, as a person
    /// would. Recording runs on editor updates and returns at once.
    /// <para>
    /// The events reach the window's panel directly and never the system pointer, so the desktop's own mouse keeps
    /// working while the editor records behind other windows.
    /// </para>
    /// </remarks>
    internal sealed class WindowTakeRecorder
    {
        private const int SettleUpdates = 8;
        private const int StableUpdates = 3;
        private const int UpdatesPerFrame = 2;
        private const string TooltipType = "InspectorTooltip";
        private const string Shut = "▶";
        private const string Open = "▼";

        private static readonly Regex Tags = new Regex("<[^>]+>");
        private static readonly string[] SlideClasses = { "lightside-value-drag", "lightside-fill-slider" };

        private static WindowTakeRecorder active;

        private readonly Reel reel;
        /// <summary>Each take as its section and its index there; the take itself is rebuilt with the reel before each recording.</summary>
        private readonly List<(GuiSection section, int index)> order;
        private readonly int count;

        private int index;
        private TakeShot shot;
        private float[] values;
        private Phase phase;
        private EditorWindow window;
        private WindowCapture capture;
        private PointerTimeline timeline;
        private WindowTake take;
        private RectInt crop;
        /// <summary>
        /// The dragged rows: a scrubbed value box is written in whole numbers in step with the pointer's travel, and
        /// a dragged slider is held to hundredths, so the window never shows the long fractions a drag lands between.
        /// </summary>
        private List<Drag> drags;
        private int frames;
        private int frame;
        private int wait = SettleUpdates;
        private bool held;
        private Vector2 last;
        private string lastLayout;
        private int stable;

        private WindowTakeRecorder(Reel reel, List<(GuiSection section, int index)> order)
        {
            this.reel = reel;
            this.order = order;
            count = order.Count;
        }

        /// <summary>
        /// Where the recording is. Every rebuild of the reel waits a few updates after the selection is cleared, so
        /// the Inspector has let go of the subject before the rebuild destroys it; every take starts from a rebuild,
        /// so it lays out against the takes recorded before it.
        /// </summary>
        private enum Phase
        {
            Rebuilding,
            Settling,
            Recording,
            Finishing
        }

        [MenuItem(PromoMenu.Tools.RecordTake, false, 32)]
        private static void RecordFromMenu() => Debug.Log(Record());

        /// <summary>Starts recording every take of the open reel's sections and answers how it began.</summary>
        /// <exception cref="InvalidOperationException">A recording is running, or the open scene has no reel with a section that has takes.</exception>
        public static string Record()
        {
            if (active != null) throw new InvalidOperationException("[Promo] A take is already recording: " + Progress());

            var reel = PromoSceneCommands.FindReel()
                       ?? throw new InvalidOperationException("[Promo] No Reel in the open scene.");
            var sections = reel.GetComponentsInChildren<GuiSection>(true);
            if (sections.Any(section => !section.IsBuilt)) reel.Rebuild();
            var order = new List<(GuiSection section, int index)>();
            foreach (var section in sections)
                for (var i = 0; i < section.Takes.Count; i++)
                    order.Add((section, i));
            if (order.Count == 0) throw new InvalidOperationException($"[Promo] '{reel.name}' has no section with takes.");

            foreach (var each in order.Select(entry => entry.section.Takes[entry.index]))
            {
                Directory.CreateDirectory(each.Folder);
                foreach (var stale in Directory.GetFiles(each.Folder, "frame_*.png")) File.Delete(stale);
                File.Delete(Path.Combine(each.Folder, WindowTake.FileName));
            }

            reel.Playing = false;
            Selection.activeObject = null;
            active = new WindowTakeRecorder(reel, order);
            EditorApplication.update += active.Tick;
            return $"recording {order.Count} takes of '{reel.name}'";
        }

        /// <summary>Where the running recording is, or that none runs.</summary>
        public static string Progress() => active == null
            ? "idle"
            : $"take {active.index + 1} of {active.count}: " +
              (active.phase == Phase.Recording ? $"frame {active.frame} of {active.frames}" : active.phase.ToString());

        private static EditorWindow FloatingInspector(Rect placement)
        {
            var type = typeof(Editor).Assembly.GetType("UnityEditor.InspectorWindow", true);
            var window = Resources.FindObjectsOfTypeAll(type).Cast<EditorWindow>().FirstOrDefault(w => !w.docked);
            if (!window)
            {
                window = (EditorWindow)ScriptableObject.CreateInstance(type);
                window.Show();
            }

            window.position = placement;
            var refresh = type.GetMethod("RefreshInspectors", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                          ?? throw new MissingMethodException("UnityEditor.InspectorWindow", "RefreshInspectors");
            refresh.Invoke(null, null);
            return window;
        }

        private void Tick()
        {
            try
            {
                if (--wait > 0) return;

                switch (phase)
                {
                    case Phase.Rebuilding:
                        Rebuild();
                        return;
                    case Phase.Settling:
                        if (!Settled()) return;
                        Resolve();
                        Drive();
                        wait = SettleUpdates;
                        return;
                    case Phase.Recording:
                        Capture();
                        if (++frame < frames)
                        {
                            Drive();
                            return;
                        }

                        take.Save(shot.Folder);
                        Debug.Log($"[Promo] Recorded {take.FrameCount} frames at {crop.width}x{crop.height} -> {shot.Folder}");
                        Selection.activeObject = null;
                        phase = ++index < count ? Phase.Rebuilding : Phase.Finishing;
                        wait = SettleUpdates;
                        return;
                    default:
                        Finish();
                        return;
                }
            }
            catch (Exception exception)
            {
                Stop();
                Debug.LogException(exception);
            }
        }

        private void Rebuild()
        {
            reel.Rebuild();
            var (section, at) = order[index];
            shot = section.Takes[at];
            values = new float[shot.ChannelNames.Length];
            held = false;
            frame = 0;
            stable = 0;
            lastLayout = null;
            reel.Seek(reel.StartOf(section) + shot.Start);
            var subject = shot.Subject;
            Selection.activeGameObject = subject.gameObject;
            foreach (var component in subject.GetComponents<Component>())
                InternalEditorUtility.SetIsInspectorExpanded(component, component == subject);

            window = FloatingInspector(shot.Window);
            phase = Phase.Settling;
            wait = SettleUpdates;
        }

        /// <summary>
        /// Whether every row the take measures has stayed where it is for <see cref="StableUpdates"/> updates.
        /// </summary>
        /// <remarks>
        /// An Inspector lays a freshly selected component out over several updates — rows bound to the component's
        /// state appear after the first passes and push everything below them down. A take measured before that
        /// aims its pointer a row away from where the window draws the field.
        /// </remarks>
        private bool Settled()
        {
            RemoveTooltips();
            wait = 1;
            var root = window.rootVisualElement;
            if (!Unfolded(root) || !Scrolled(root)) return false;

            var layout = new System.Text.StringBuilder();
            layout.Append(Row(root, shot.Crop.Top).worldBound).Append(Row(root, shot.Crop.Bottom).worldBound);
            foreach (var step in shot.Script)
                if (step.Row != null) layout.Append(Aim(root, step));

            var measured = layout.ToString();
            stable = measured == lastLayout ? stable + 1 : 0;
            lastLayout = measured;
            return stable >= StableUpdates;
        }

        /// <summary>
        /// Opens the first of the crop's foldouts that is still shut, by a click on its header; answers whether all of
        /// them are open.
        /// </summary>
        /// <exception cref="InvalidOperationException">A named row has no fold arrow beside it.</exception>
        private bool Unfolded(VisualElement root)
        {
            foreach (var name in shot.Crop.Unfold)
            {
                var header = Row(root, name);
                var arrow = header.parent?.Query<TextElement>()
                                .Where(element => element.text == Shut || element.text == Open).First()
                            ?? throw new InvalidOperationException($"[Promo] The Inspector row '{name}' does not fold.");
                if (arrow.text == Open) continue;

                var point = header.worldBound.center;
                last = point;
                Send(EventType.MouseDown, point);
                Send(EventType.MouseUp, point);
                stable = 0;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Scrolls the Inspector until the card's top row sits at the top of its viewport, so the window only has to be
        /// as tall as the part the card shows; answers whether it already does.
        /// </summary>
        /// <exception cref="InvalidOperationException">The window is too short to show that part whole.</exception>
        private bool Scrolled(VisualElement root)
        {
            var scroll = root.Q<ScrollView>();
            if (scroll == null) return true;

            var viewport = scroll.contentViewport.worldBound;
            var top = Row(root, shot.Crop.Top).worldBound.yMin - shot.Crop.Above;
            var shift = top - viewport.yMin;
            if (Mathf.Abs(shift) > 0.5f)
            {
                var before = scroll.scrollOffset;
                scroll.scrollOffset = before + new Vector2(0f, shift);
                if (scroll.scrollOffset != before)
                {
                    stable = 0;
                    return false;
                }
            }

            var bottom = Row(root, shot.Crop.Bottom).worldBound.yMax + shot.Crop.Below;
            if (bottom > viewport.yMax + 0.5f)
                throw new InvalidOperationException(
                    $"[Promo] The Inspector is {bottom - viewport.yMax:0} points too short to show '{shot.Crop.Top}' " +
                    $"through '{shot.Crop.Bottom}' whole. Make the take window of {shot.Folder} taller.");
            return true;
        }

        private void Resolve()
        {
            capture = WindowCapture.Of(window);
            capture.SendToBack();

            var root = window.rootVisualElement;
            var viewport = root.Q<ScrollView>()?.contentViewport.worldBound ?? root.worldBound;
            var top = Row(root, shot.Crop.Top).worldBound.yMin - shot.Crop.Above;
            var bottom = Row(root, shot.Crop.Bottom).worldBound.yMax + shot.Crop.Below;
            var min = capture.Pixel(new Vector2(viewport.xMin + shot.Crop.Left, top));
            var max = capture.Pixel(new Vector2(viewport.xMax - shot.Crop.Right, bottom));
            crop = new RectInt(Mathf.RoundToInt(min.x), Mathf.RoundToInt(min.y),
                Mathf.RoundToInt(max.x - min.x), Mathf.RoundToInt(max.y - min.y));

            var beats = new List<Beat>();
            foreach (var step in shot.Script)
            {
                var point = step.Row == null
                    ? Vector2.zero
                    : shot.CardPoint(capture.Pixel(Aim(root, step)) - crop.position, crop.size);
                beats.Add(step.Resolve(point));
            }

            var regions = new List<WindowTake.CursorRegion>();
            foreach (var step in shot.Script)
            {
                var area = step.Row == null ? null : SlideArea(Row(root, step.Row));
                if (area == null) continue;
                var a = shot.CardPoint(capture.Pixel(area.worldBound.min) - crop.position, crop.size);
                var b = shot.CardPoint(capture.Pixel(area.worldBound.max) - crop.position, crop.size);
                var rect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
                if (!regions.Exists(region => region.Rect == rect))
                    regions.Add(new WindowTake.CursorRegion { Rect = rect, Cursor = CursorType.ResizeHorizontal });
            }

            var start = shot.PointerStartFor(crop.size);
            timeline = new PointerTimeline(start, beats);
            drags = shot.Script.Where(step => step.Kind == BeatKind.Drag)
                .Select(step => new Drag(step, Row(root, step.Row), timeline)).ToList();
            take = new WindowTake(reel.Fps, crop.size, start, beats, regions.ToArray(), shot.ChannelNames);
            frames = Mathf.CeilToInt(timeline.Total * reel.Fps) + 1;
            last = Window(start);
            phase = Phase.Recording;
        }

        private void Drive()
        {
            var t = frame / (float)reel.Fps;
            var pose = timeline.Sample(t);
            var pressed = timeline.Presses.Any(p => t >= p.Down && t < p.Up);
            var type = pressed == held
                ? pressed ? EventType.MouseDrag : EventType.MouseMove
                : pressed ? EventType.MouseDown : EventType.MouseUp;

            var dragged = drags.Find(drag => drag.Holds(t, 0.5f / reel.Fps));
            dragged?.Begin();
            Send(type, Window(pose.Position));
            held = pressed;
            dragged?.Write(pose.Position.x);
            RemoveTooltips();
            window.Repaint();
            wait = UpdatesPerFrame;
        }

        private void Capture()
        {
            var grabbed = capture.Grab(crop);
            try
            {
                File.WriteAllBytes(WindowTake.FramePath(shot.Folder, frame), grabbed.EncodeToPNG());
            }
            finally
            {
                ObjectUtils.SafeDestroy(grabbed);
            }

            shot.ReadChannels(values);
            take.Append(values);
        }

        private void Finish()
        {
            Stop();
            reel.Rebuild();
            Debug.Log($"[Promo] Recorded {count} takes of '{reel.name}'.");
        }

        private void Stop()
        {
            EditorApplication.update -= Tick;
            if (held) Send(EventType.MouseUp, last);
            active = null;
        }

        private Vector2 Window(Vector2 stagePoint) =>
            capture.Point(shot.CapturePixel(stagePoint, crop.size) + crop.position);

        private void Send(EventType type, Vector2 point)
        {
            var system = new Event
            {
                type = type,
                mousePosition = point,
                delta = type == EventType.MouseDown ? Vector2.zero : point - last,
                button = 0,
                clickCount = type == EventType.MouseDown || type == EventType.MouseUp ? 1 : 0
            };
            last = point;

            var root = window.rootVisualElement;
            switch (type)
            {
                case EventType.MouseDown:
                    using (var evt = PointerDownEvent.GetPooled(system)) root.SendEvent(evt);
                    break;
                case EventType.MouseUp:
                    using (var evt = PointerUpEvent.GetPooled(system)) root.SendEvent(evt);
                    break;
                default:
                    using (var evt = PointerMoveEvent.GetPooled(system)) root.SendEvent(evt);
                    break;
            }
        }

        /// <summary>Takes the inspectors' own hover tooltips out of the window: a take shows the edit, not its help.</summary>
        private void RemoveTooltips()
        {
            var tooltips = new List<VisualElement>();
            window.rootVisualElement.panel.visualTree.Query<VisualElement>()
                .Where(element => element.GetType().Name == TooltipType).ToList(tooltips);
            foreach (var tooltip in tooltips) tooltip.RemoveFromHierarchy();
        }

        private static Vector2 Aim(VisualElement root, TakeBeat step)
        {
            var row = Row(root, step.Row);
            if (step.Part == TakePart.Value)
            {
                var box = Part(row, step.Row, "unity-text-input").worldBound;
                return new Vector2(Mathf.Lerp(box.xMin, box.xMax, step.Along), box.center.y);
            }

            var knob = Part(row, step.Row, "unity-dragger").worldBound;
            var track = Part(row, step.Row, "unity-tracker").worldBound;
            return new Vector2(track.xMin + knob.width * 0.5f + step.Along * (track.width - knob.width), knob.center.y);
        }

        /// <summary>
        /// The part of <paramref name="row"/> the LightSide inspector stylesheet dresses in the slide-arrow cursor: a
        /// value box that scrubs and a fill slider. Null when the row has neither.
        /// </summary>
        private static VisualElement SlideArea(VisualElement row)
        {
            foreach (var name in SlideClasses)
            {
                if (row.ClassListContains(name)) return row;
                var found = row.Q(className: name);
                if (found != null) return found;
            }

            return null;
        }

        private static VisualElement Part(VisualElement row, string label, string name) =>
            row.Q(name) ?? throw new InvalidOperationException($"[Promo] The Inspector row '{label}' has no '{name}'.");

        /// <summary>
        /// The shown field whose label reads <paramref name="label"/>, or the shown text element that does once its
        /// rich-text tags are set aside.
        /// </summary>
        /// <remarks>
        /// A folded section keeps its rows in the tree with no layout, so only elements with a laid-out bound count:
        /// the same label in a folded section never wins over the one on screen.
        /// </remarks>
        private static VisualElement Row(VisualElement root, string label)
        {
            var fields = root.Query<VisualElement>().Where(element => Shown(element) && element.GetType().GetProperties()
                .FirstOrDefault(p => p.Name == "label" && p.PropertyType == typeof(string))
                ?.GetValue(element) as string == label).ToList();
            if (fields.Count > 0) return fields[0];

            return root.Query<TextElement>().Where(element => Shown(element) && Plain(element.text) == label).First()
                   ?? throw new InvalidOperationException(
                       $"[Promo] The Inspector shows no row '{label}'. Unfold the section that holds it and record again.");
        }

        private static bool Shown(VisualElement element)
        {
            var bound = element.worldBound;
            return bound.height > 0f && !float.IsNaN(bound.height);
        }

        private static string Plain(string text) => text == null ? null : Tags.Replace(text, string.Empty);

        /// <summary>A dragged row's number, written each frame of the drag after the window has handled the pointer.</summary>
        private sealed class Drag
        {
            private readonly INotifyValueChanged<float> field;
            private readonly TakeBeat step;
            private readonly Vector2 span;
            private readonly float fromX;
            private readonly float toX;
            private float start = float.NaN;

            public Drag(TakeBeat step, VisualElement row, PointerTimeline timeline)
            {
                this.step = step;
                field = row as INotifyValueChanged<float>
                        ?? throw new InvalidOperationException($"[Promo] The Inspector row '{step.Row}' holds no number to drag.");
                var travel = timeline.Span(step.Id);
                span = new Vector2(travel.x - PointerTiming.PressDown, travel.y);
                fromX = timeline.Sample(travel.x).Position.x;
                toX = timeline.Sample(travel.y).Position.x;
            }

            /// <summary>Whether <paramref name="seconds"/> falls from the press through the release, give or take <paramref name="slack"/> after it.</summary>
            public bool Holds(float seconds, float slack) => seconds >= span.x && seconds <= span.y + slack;

            /// <summary>Remembers the number the drag starts from, before the window has seen any of it.</summary>
            public void Begin()
            {
                if (float.IsNaN(start)) start = field.value;
            }

            /// <summary>Writes the number for the pointer at <paramref name="x"/> in stage coordinates.</summary>
            public void Write(float x)
            {
                field.value = step.Part == TakePart.Value
                    ? Mathf.Round(Mathf.Lerp(start, step.Lands, Mathf.InverseLerp(fromX, toX, x)))
                    : Mathf.Round(field.value * 100f) / 100f;
            }
        }
    }
}
