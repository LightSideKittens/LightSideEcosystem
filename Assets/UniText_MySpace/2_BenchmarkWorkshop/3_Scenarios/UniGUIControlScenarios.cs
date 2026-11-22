using System;
using System.Collections.Generic;
using System.Diagnostics;
using LightSide;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// UniGUI control workloads of <see cref="UniTextScenarioSuite"/>: a grid of copies of one control prefab, instantiated
/// under an inactive root and then enabled, measured at rest and while every copy changes each frame.
/// </summary>
/// <remarks>
/// <para>
/// Ids read <c>controls.&lt;control&gt;.&lt;model&gt;.&lt;work&gt;</c>: the model is <c>today</c> for the shipped default and
/// <c>proto</c> for the prototype of the next control model — <c>shared</c> for the prototype whose styles a shared set
/// holds — so <c>controls.button</c> selects every model of a control.
/// Work is <c>rest</c>, <c>press</c> (every copy's state host previews Pressed on even frames and nothing on odd ones),
/// <c>dim</c> (the same with Disabled) or <c>month</c> (every calendar shows the next month each frame).
/// </para>
/// <para>
/// The workload records the instantiate and enable times apart — copying the objects, then their Awake and OnEnable —
/// the time to enable them again once disabled, and the objects and components one copy carries. While copies change,
/// it counts the times a shape's vertices are dirtied and a text starts a rebuild per measured frame. The suite records the
/// memory the copies hold and the Canvas profiler markers of every measured frame.
/// </para>
/// </remarks>
static class UniGUIControlScenarios
{
    const int Buttons = 400;
    const int Calendars = 12;

    static readonly string[] CanvasMarkers =
    {
        "PostLateUpdate.PlayerUpdateCanvases", "UIEvents.WillRenderCanvases", "CanvasUpdate.Layout", "CanvasUpdate.PreRender",
        "ClipperRegistry.Cull", "UGUI.Rendering.UpdateBatches", "Canvas.BuildBatch", "Canvas.PrepareBatches", "Canvas.Sort",
        "Canvas.GeometryJob", "Canvas.PrepareBatchesJob", "Canvas.SortJob", "Canvas.SplitInstructionsJob",
        "Canvas.CombineInstructionsJob", "CanvasRenderer.SyncTransform", "UGUI.Rendering.EmitWorldScreenspaceCameraGeometry",
        "UGUI.Rendering.EmitGeometryForCamera", "Canvas.RenderOverlays", "Canvas.RenderSubBatch",
    };

    public static List<UniTextScenario> Create(GameObject button, GameObject buttonPrototype, GameObject buttonShared,
        GameObject calendar, GameObject calendarPrototype)
    {
        var scenarios = new List<UniTextScenario>();
        Add(scenarios, "controls.button.today", button, Buttons, Work.Press, Work.Dim);
        Add(scenarios, "controls.button.proto", buttonPrototype, Buttons, Work.Press, Work.Dim);
        Add(scenarios, "controls.button.shared", buttonShared, Buttons, Work.Press, Work.Dim);
        Add(scenarios, "controls.calendar.today", calendar, Calendars, Work.Month);
        Add(scenarios, "controls.calendar.proto", calendarPrototype, Calendars, Work.Month);
        return scenarios;
    }

    static void Add(List<UniTextScenario> scenarios, string id, GameObject prefab, int count, params Work[] changes)
    {
        if (prefab == null) return;
        scenarios.Add(new Grid(id + ".rest", prefab, count, Work.Rest));
        foreach (var change in changes)
            scenarios.Add(new Grid(id + "." + change.ToString().ToLowerInvariant(), prefab, count, change));
    }

    enum Work
    {
        Rest,
        Press,
        Dim,
        Month,
    }

    sealed class Grid : UniTextScenario
    {
        readonly GameObject prefab;
        readonly int count;
        readonly Work work;
        readonly List<GameObject> copies = new();
        readonly List<InteractionStateHost> hosts = new();
        readonly List<IMonthPaged> calendars = new();
        int changes;
        int steps;
        int measuredFrom;
        long shapeDirties;
        long textRebuilds;
        double instantiateMs;
        double enableMs;
        double reenableMs;
        JObject anatomy;

        public Grid(string id, GameObject prefab, int count, Work work) : base(id, true)
        {
            this.prefab = prefab;
            this.count = count;
            this.work = work;
        }

        public override bool MeasuresFootprint => true;

        public override string[] Markers => CanvasMarkers;

        public override void Setup(ScenarioRig rig)
        {
            var root = rig.CreateRoot(Id);
            root.gameObject.SetActive(false);
            var columns = Mathf.CeilToInt(Mathf.Sqrt(count * ScenarioRig.ReferenceWidth / ScenarioRig.ReferenceHeight));
            var rows = Mathf.CeilToInt(count / (float)columns);

            var started = Stopwatch.GetTimestamp();
            for (var i = 0; i < count; i++)
            {
                var copy = Object.Instantiate(prefab, root, false);
                copies.Add(copy);
            }
            instantiateMs = Elapsed(started);

            for (var i = 0; i < count; i++)
            {
                var rect = (RectTransform)copies[i].transform;
                rect.anchorMin = rect.anchorMax = new Vector2((i % columns + 0.5f) / columns, 1f - (i / columns + 0.5f) / rows);
                rect.anchoredPosition = Vector2.zero;
            }

            started = Stopwatch.GetTimestamp();
            root.gameObject.SetActive(true);
            enableMs = Elapsed(started);

            root.gameObject.SetActive(false);
            started = Stopwatch.GetTimestamp();
            root.gameObject.SetActive(true);
            reenableMs = Elapsed(started);

            anatomy = Anatomy(copies[0]);
            Action dirtied = () => shapeDirties++;
            Action rebuilding = () => textRebuilds++;
            foreach (var copy in copies)
            {
                foreach (var shape in copy.GetComponentsInChildren<UniShape>(true)) shape.Host.VerticesDirtied += dirtied;
                foreach (var text in copy.GetComponentsInChildren<UniText>(true)) text.Rebuilding += rebuilding;
                if (work is Work.Press or Work.Dim)
                {
                    var host = copy.GetComponent<InteractionStateHost>()
                               ?? throw new InvalidOperationException($"'{prefab.name}' has no state host on its root.");
                    host.StatesChanged += _ => changes++;
                    hosts.Add(host);
                }
                else if (work == Work.Month) calendars.Add(MonthPaged(copy));
            }
        }

        public override void Step(int frame)
        {
            steps++;
            if (work is Work.Press or Work.Dim)
            {
                InteractionStateSet? preview = null;
                if ((frame & 1) == 0)
                {
                    var shown = InteractionStateSet.Empty;
                    shown.Set(work == Work.Press ? InteractionStates.Pressed : InteractionStates.Disabled, true);
                    preview = shown;
                }
                foreach (var host in hosts) host.PreviewStates = preview;
            }
            else if (work == Work.Month)
            {
                foreach (var calendar in calendars)
                {
                    calendar.Month = calendar.Month.AddMonths(1);
                    changes++;
                }
            }
        }

        public override void Validate()
        {
            foreach (var copy in copies)
                if (copy == null || !copy.activeInHierarchy)
                    throw new InvalidOperationException($"A copy of '{prefab.name}' is gone or inactive.");
            if (work == Work.Rest || steps == 0) return;
            var expected = (long)steps * (work == Work.Month ? calendars.Count : hosts.Count);
            if (changes * 10L < expected * 9)
                throw new InvalidOperationException($"{changes} changes of {expected} expected over {steps} frames.");
        }

        public override void MeasuringStarts()
        {
            measuredFrom = steps;
            shapeDirties = 0;
            textRebuilds = 0;
        }

        public override void Describe(JObject workload)
        {
            workload["prefab"] = prefab.name;
            workload["copies"] = count;
            workload["work"] = work.ToString().ToLowerInvariant();
            workload["instantiateMs"] = Math.Round(instantiateMs, 3);
            workload["enableMs"] = Math.Round(enableMs, 3);
            workload["reenableMs"] = Math.Round(reenableMs, 3);
            var measured = Math.Max(1, steps - measuredFrom);
            workload["shapeDirtiesPerFrame"] = Math.Round((double)shapeDirties / measured, 2);
            workload["textRebuildsPerFrame"] = Math.Round((double)textRebuilds / measured, 2);
            workload["perCopy"] = anatomy;
        }

        public override void Teardown()
        {
            foreach (var host in hosts)
                if (host != null) host.PreviewStates = null;
            copies.Clear();
            hosts.Clear();
            calendars.Clear();
        }

        static double Elapsed(long started) => (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;

        static JObject Anatomy(GameObject copy)
        {
            var objects = copy.GetComponentsInChildren<Transform>(true).Length;
            var components = copy.GetComponentsInChildren<Component>(true);
            var behaviours = 0;
            var renderers = 0;
            var groups = 0;
            var shapes = 0;
            var texts = 0;
            foreach (var component in components)
            {
                if (component is MonoBehaviour) behaviours++;
                else if (component is CanvasRenderer) renderers++;
                else if (component is CanvasGroup) groups++;
                if (component is UniShape) shapes++;
                else if (component is UniText) texts++;
            }
            return new JObject
            {
                ["gameObjects"] = objects,
                ["components"] = components.Length,
                ["monoBehaviours"] = behaviours,
                ["canvasRenderers"] = renderers,
                ["canvasGroups"] = groups,
                ["shapes"] = shapes,
                ["texts"] = texts,
            };
        }

        static IMonthPaged MonthPaged(GameObject copy)
        {
            if (copy.TryGetComponent<UniCalendar>(out var calendar)) return new UniCalendarMonth(calendar);
            if (copy.TryGetComponent<UniCalendarPrototype>(out var prototype)) return new PrototypeMonth(prototype);
            throw new InvalidOperationException($"'{copy.name}' has no calendar on its root.");
        }
    }

    interface IMonthPaged
    {
        DateTime Month { get; set; }
    }

    sealed class PrototypeMonth : IMonthPaged
    {
        readonly UniCalendarPrototype calendar;

        public PrototypeMonth(UniCalendarPrototype calendar) => this.calendar = calendar;

        public DateTime Month
        {
            get => calendar.Month;
            set => calendar.Month = value;
        }
    }

    sealed class UniCalendarMonth : IMonthPaged
    {
        readonly UniCalendar calendar;

        public UniCalendarMonth(UniCalendar calendar) => this.calendar = calendar;

        public DateTime Month
        {
            get => calendar.Month;
            set => calendar.Month = value;
        }
    }
}
