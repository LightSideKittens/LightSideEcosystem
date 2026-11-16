using System.Collections.Generic;
using UnityEngine;
using Ramp = LightSide.Gradient;

namespace LightSide.Promo
{
    /// <summary>
    /// The weight a hit lands with: a burst at the point of impact, and for the biggest hits a jolt of the whole frame,
    /// both pure functions of a slide's time.
    /// </summary>
    /// <remarks>
    /// A burst is a ring and a spray of sparks in the brand ramp, built once at its point and shown only in its own short
    /// window; a jolt is a damped shake the slide applies to its content. Everything is laid out while the slide builds
    /// and posed from its time, so a slide with impacts still scrubs and renders out of order.
    /// </remarks>
    public sealed class Impacts
    {
        private readonly Stage stage;
        private readonly RectTransform layer;
        private readonly Ramp brand;
        private readonly Color ring;
        private readonly List<Burst> bursts = new();
        private readonly List<Jolt> jolts = new();

        private sealed class Burst
        {
            public float At;
            public RectTransform Ring;
            public StrokeLayer Stroke;
            public GameObject Root;
            public RectTransform[] Sparks;
            public Vector2[] Directions;
            public float Size;
        }

        private readonly struct Jolt
        {
            public Jolt(float at, Vector2 direction, float strength)
            {
                At = at;
                Direction = direction;
                Strength = strength;
            }

            public float At { get; }
            public Vector2 Direction { get; }
            public float Strength { get; }
        }

        /// <summary>Makes the layer the bursts draw in, under <paramref name="parent"/>; what is built after it draws over them.</summary>
        public Impacts(Stage stage, Transform parent)
        {
            this.stage = stage;
            brand = stage.Theme.Brand;
            ring = stage.Theme.Accent;
            layer = stage.Stretch(stage.Node("Bursts", parent));
        }

        /// <summary>The layer the bursts draw in, to order among its siblings.</summary>
        public RectTransform Layer => layer;

        /// <summary>A burst <paramref name="size"/> across at <paramref name="point"/> at time <paramref name="at"/>.</summary>
        public void Hit(float at, Vector2 point, float size)
        {
            var burst = new Burst { At = at, Size = size };
            var root = stage.Node("Burst", layer);
            stage.Box(root, point, Vector2.zero);
            burst.Root = root.gameObject;

            var circle = stage.Shape("Ring", root, ShapeKind.Circle);
            stage.Box(circle.Rect, Vector2.zero, Vector2.one * size);
            Stage.Solid(circle.Fill, new Color(1f, 1f, 1f, 0f));
            burst.Ring = circle.Rect;
            burst.Stroke = Stage.AddStroke(circle.Shape, ring, 0f, 0f);
            Stage.Ramped(burst.Stroke.Paint, brand, PaintProjectionKind.Angular, 0f);

            burst.Sparks = new RectTransform[SparkCount];
            burst.Directions = new Vector2[SparkCount];
            var turn = Mathf.Repeat(at * 7.31f + point.x * 0.013f, 1f) * Mathf.PI * 2f;
            for (var i = 0; i < SparkCount; i++)
            {
                var angle = turn + i * Mathf.PI * 2f / SparkCount;
                burst.Directions[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var spark = stage.Shape("Spark", root, ShapeKind.Capsule);
                spark.Rect.localRotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg);
                Stage.Solid(spark.Fill, brand.Evaluate(i / (SparkCount - 1f)));
                burst.Sparks[i] = spark.Rect;
            }

            burst.Root.SetActive(false);
            bursts.Add(burst);
        }

        /// <summary>A jolt of the frame at time <paramref name="at"/>, thrown along <paramref name="direction"/>.</summary>
        public void Shake(float at, Vector2 direction, float strength) =>
            jolts.Add(new Jolt(at, direction.normalized, strength));

        /// <summary>Poses every burst at <paramref name="local"/> and returns how far the frame is thrown.</summary>
        public Vector2 Pose(float local)
        {
            foreach (var burst in bursts)
            {
                var t = (local - burst.At) / BurstFor;
                var shown = t >= 0f && t <= 1f;
                burst.Root.SetActive(shown);
                if (!shown) continue;

                var grow = 1f - (1f - t) * (1f - t) * (1f - t);
                burst.Ring.sizeDelta = Vector2.one * (burst.Size * Mathf.LerpUnclamped(0.35f, 1.6f, grow));
                burst.Stroke.Width = RingWidth * (1f - t);

                var reach = burst.Size * Mathf.LerpUnclamped(0.3f, 1.15f, grow);
                var length = SparkLength * (1f - t) * Mathf.Clamp01(t * 6f);
                var width = SparkWidth * (1f - t * 0.6f);
                for (var i = 0; i < burst.Sparks.Length; i++)
                {
                    burst.Sparks[i].anchoredPosition = burst.Directions[i] * reach;
                    burst.Sparks[i].sizeDelta = new Vector2(length, width);
                }
            }

            var offset = Vector2.zero;
            foreach (var jolt in jolts)
            {
                var t = local - jolt.At;
                if (t < 0f || t > JoltFor) continue;
                offset += jolt.Direction * (jolt.Strength * Mathf.Exp(-t * JoltDamping) * Mathf.Sin(t * JoltRate));
            }

            return offset;
        }

        private const int SparkCount = 10;
        private const float BurstFor = 0.42f;
        private const float RingWidth = 6f;
        private const float SparkLength = 26f;
        private const float SparkWidth = 7f;
        private const float JoltFor = 0.45f;
        private const float JoltDamping = 9f;
        private const float JoltRate = 42f;
    }
}
