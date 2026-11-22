#if LIGHTSIDE_HAS_UNIGUI
using System;
using LightSide;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// Shape workloads of <see cref="UniTextScenarioSuite"/>: a grid of UniShapes on the rig's Canvas, each a rounded
/// rectangle with a shadow, a fill and a stroke, resized every frame so every shape rebuilds and reloads its mesh.
/// </summary>
/// <remarks>
/// Validation throws when a shape's vertices were not dirtied in at least 90 % of the stepped frames — each dirtying
/// schedules the rebuild the next Canvas update performs — or when a shape is culled.
/// </remarks>
static class UniShapesScenarios
{
    const int Columns = 24;
    const int Rows = 25;
    const int Shapes = Columns * Rows;

    public static UniTextScenario[] Create() => new UniTextScenario[] { new Rebuild("shapes.rebuild") };

    sealed class Rebuild : UniTextScenario
    {
        readonly UniShape[] shapes = new UniShape[Shapes];
        readonly RectTransform[] rects = new RectTransform[Shapes];
        readonly int[] dirtied = new int[Shapes];
        int steps;

        public Rebuild(string id) : base(id, true) { }

        public override void Setup(ScenarioRig rig)
        {
            var root = rig.CreateRoot(Id);
            for (var i = 0; i < Shapes; i++)
            {
                var go = new GameObject("Shape " + i, typeof(RectTransform)) { layer = 5 };
                go.SetActive(false);
                go.transform.SetParent(root, false);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = new Vector2((i % Columns + 0.5f) / Columns, (i / Columns + 0.5f) / Rows);
                rect.sizeDelta = new Vector2(29f, 49f);
                var shape = go.AddComponent<UniShape>();
                shape.Layers.Clear();
                var shadow = shape.AddLayer<ShadowLayer>();
                shadow.Blur = 6f;
                shadow.Offset = new Vector2(3f, -3f);
                shape.AddLayer<FillLayer>();
                shape.AddLayer<StrokeLayer>().Width = 2f;
                go.SetActive(true);
                shapes[i] = shape;
                rects[i] = rect;
                var index = i;
                shape.Host.VerticesDirtied += () => dirtied[index]++;
            }
        }

        public override void Step(int frame)
        {
            if (steps == 0) Array.Clear(dirtied, 0, Shapes);
            steps++;
            var grow = (frame & 1) * 2f;
            for (var i = 0; i < Shapes; i++)
                rects[i].sizeDelta = new Vector2(30f + grow + i % 3, 50f + grow);
        }

        public override void Validate()
        {
            for (var i = 0; i < Shapes; i++)
            {
                if (shapes[i].GetComponent<CanvasRenderer>().cull) throw new InvalidOperationException($"Shape {i} is culled.");
                if (dirtied[i] * 10 < steps * 9)
                    throw new InvalidOperationException($"Shape {i} was dirtied in {dirtied[i]} of {steps} frames.");
            }
        }

        public override void Describe(JObject workload)
        {
            workload["shapes"] = Shapes;
            workload["layers"] = "shadow, fill, stroke";
            workload["change"] = "size every frame";
        }
    }
}
#endif
