using System.Collections.Generic;
using UnityEngine;
using Ramp = LightSide.Gradient;
using Stop = LightSide.GradientStop;
using Interp = LightSide.GradientInterpolation;

namespace LightSide.Promo
{
    public sealed partial class GuiShapesScene
    {
        [SerializeField] private Texture2D photo;

        private Part BuildWarp(Stage stage)
        {
            var side = contentHeight * 0.42f;
            var at = new Vector2(0f, contentCenter + contentHeight * 0.1f);
            var cell = new Vector2(176f, 70f);
            var switchAt = new Vector2(0f, at.y - side * 0.5f - contentHeight * 0.2f);
            var part = new WarpPart(stage, new Vector2(cell.x * 2.6f, switchAt.y - PointerDrop), cell, switchAt);
            part.Build(this, stage, at, side);
            return part;
        }

        private Part BuildMask(Stage stage)
        {
            var size = new Vector2(contentHeight * 0.92f, contentHeight * 0.56f);
            var at = new Vector2(0f, contentCenter + contentHeight * 0.1f);
            var cell = new Vector2(190f, 70f);
            var switchAt = new Vector2(0f, at.y - size.y * 0.5f - contentHeight * 0.16f);
            var part = new MaskPart(stage, new Vector2(cell.x * 2f, switchAt.y - PointerDrop), cell, switchAt);
            part.Build(this, stage, at, size);
            return part;
        }

        /// <summary>
        /// Shapes alive through the field modifiers: a star wrung back and forth by a twist, a blob of jelly whose edge
        /// flows, a banner waving like a flag and a flower of one petal repeated round its middle, each picked on a switch
        /// and popping in as the last one pops out.
        /// </summary>
        private sealed class WarpPart : DrawnPart
        {
            private static readonly string[] Names = { "Twist", "Wobble", "Wave", "Bloom" };

            private readonly float[] clicks = new float[Names.Length - 1];
            private readonly float[] weights = new float[Names.Length];
            private readonly Vector2 cell;
            private readonly Vector2 switchAt;
            private Switch options;
            private UniText[] labels;
            private RectTransform[] heroes;
            private CanvasGroup[] groups;
            private DistortLayer twist;
            private DeformLayer wobble;
            private WarpLayer wave;
            private FillLayer petal;
            private Color soft;
            private Color lit;
            private float side;

            public WarpPart(Stage stage, Vector2 start, Vector2 cell, Vector2 switchAt)
                : base(stage, "Warp", start, Script(cell, switchAt), SwitchPace)
            {
                this.cell = cell;
                this.switchAt = switchAt;
            }

            private static Beat[] Script(Vector2 cell, Vector2 at)
            {
                var beats = new List<Beat> { Beat.Wait(ShowFor) };
                for (var i = 1; i < Names.Length; i++)
                {
                    beats.Add(Beat.To(at + new Vector2((i - (Names.Length - 1) * 0.5f) * cell.x, 0f), null, cell.x * 0.6f));
                    beats.Add(Beat.Click("warp" + i, true));
                    beats.Add(Beat.Wait(ShowFor));
                }

                return beats.ToArray();
            }

            public void Build(GuiShapesScene scene, Stage stage, Vector2 at, float size)
            {
                var theme = scene.theme;
                side = size;
                soft = theme.TextSoft;
                lit = theme.Text;
                heroes = new RectTransform[Names.Length];
                groups = new CanvasGroup[Names.Length];

                var star = Hero(scene, stage, 0, at, ShapeKind.Star, Vector2.one * side, 25f);
                star.Outline.StarPoints = 5;
                star.Outline.StarSharpness = 0.45f;
                twist = new DistortLayer { Mode = DistortMode.Twist };
                star.Shape.Layers.Add(twist);

                var blob = Hero(scene, stage, 1, at, ShapeKind.Circle, Vector2.one * side, 120f);
                wobble = new DeformLayer { Amplitude = side * WobbleDepth, Scale = WobbleCells / side };
                blob.Shape.Layers.Add(wobble);

                var banner = Hero(scene, stage, 2, at, ShapeKind.RoundedRect, new Vector2(side * 1.5f, side * 0.72f), 200f);
                banner.Outline.Radius = side * 0.12f;
                wave = new WarpLayer { Style = WarpStyle.Flag };
                banner.Shape.Layers.Add(wave);

                var flower = Hero(scene, stage, 3, at, ShapeKind.Ellipse, Vector2.one * side, 300f);
                petal = flower.Shape.Layer<FillLayer>();
                petal.Rect = Petal(1f);
                flower.Shape.Layers.Add(new RepeatLayer { Mode = RepeatMode.Radial, Count = Petals });

                options = new Switch(stage, Root, theme, switchAt, cell, Names.Length);
                labels = new UniText[Names.Length];
                for (var i = 0; i < Names.Length; i++)
                {
                    var label = stage.Label(options.Rect, Names[i], 26f, soft, stretch: false, face: theme.DisplayFace);
                    stage.Box(label.rectTransform, options[i], cell);
                    labels[i] = label;
                }
            }

            private Widget Hero(GuiShapesScene scene, Stage stage, int index, Vector2 at, ShapeKind kind, Vector2 size, float angle)
            {
                var hero = stage.Shape(Names[index], Root, kind);
                stage.Box(hero.Rect, at, size);
                Stage.Ramped(hero.Fill, scene.brand, PaintProjectionKind.Linear, angle);
                Stage.AddShadow(hero.Shape, Theme.Fade(scene.theme.Coral, 0.35f), new Vector2(0f, -16f), 50f);
                heroes[index] = hero.Rect;
                groups[index] = hero.Rect.gameObject.AddComponent<CanvasGroup>();
                return hero;
            }

            /// <summary>The flower's one petal, an ellipse hung from the top middle, <paramref name="grown"/> of its full length.</summary>
            private RectPlacement Petal(float grown) => new()
            {
                anchorMin = new Vector2(0.5f, 1f),
                anchorMax = new Vector2(0.5f, 1f),
                pivot = new Vector2(0.5f, 1f),
                sizeDelta = new Vector2(side * 0.2f, side * 0.5f * grown)
            };

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                for (var i = 0; i < clicks.Length; i++) clicks[i] = ClickAt("warp" + (i + 1));
                options.Pose(played, clicks, weights);
                var t = Mathf.Max(0f, played);
                for (var i = 0; i < Names.Length; i++)
                {
                    labels[i].color = Color.LerpUnclamped(soft, lit, weights[i]);
                    labels[i].rectTransform.localScale = Vector3.one * (i == 0 ? 1f : Kick(played, clicks[i - 1]));
                    var shown = weights[i] > 0.001f;
                    if (heroes[i].gameObject.activeSelf != shown) heroes[i].gameObject.SetActive(shown);
                    groups[i].alpha = Mathf.Clamp01(weights[i] * 1.6f);
                    heroes[i].localScale = Vector3.one * Mathf.LerpUnclamped(PopFrom, 1f, Motion.Back.Evaluate(weights[i]));
                }

                twist.Angle = TwistAngle * Mathf.Sin(t * TwistRate);
                wobble.Phase = t * WobbleRate;
                wave.Bend = WaveBend * Mathf.Sin(t * WaveRate);
                heroes[3].localRotation = Quaternion.Euler(0f, 0f, -t * BloomSpin);
                petal.Rect = Petal(1f + BloomBreath * Mathf.Sin(t * BloomRate));

                var drop = new Vector2(0f, -SwitchDrop * Motion.Swish.Window(played, Out - ActionAt, SwishFor));
                options.Rect.anchoredPosition = drop;
                Pointer.Offset += drop;
            }

            /// <summary>How long each shape plays before the pointer picks the next.</summary>
            private const float ShowFor = 0.9f;

            private const float PopFrom = 0.4f;
            private const float TwistAngle = 140f;
            private const float TwistRate = 2.4f;

            /// <summary>How deep the jelly's edge moves, as a share of its size, and how many noise cells cross it.</summary>
            private const float WobbleDepth = 0.06f;

            private const float WobbleCells = 3.2f;
            private const float WobbleRate = 1.8f;
            private const float WaveBend = 0.75f;
            private const float WaveRate = 3.2f;
            private const int Petals = 10;
            private const float BloomSpin = 40f;
            private const float BloomBreath = 0.12f;
            private const float BloomRate = 3f;
        }

        /// <summary>
        /// A photograph through the kit's soft mask: its frame's edge feathering in and out, a gradient wiping it away round
        /// and round, then a spotlight cut out of a dark veil wandering over it, each picked on a switch.
        /// </summary>
        private sealed class MaskPart : DrawnPart
        {
            private static readonly string[] Names = { "Feather", "Gradient", "Spotlight" };

            private readonly float[] clicks = new float[Names.Length - 1];
            private readonly float[] weights = new float[Names.Length];
            private readonly Vector2 cell;
            private readonly Vector2 switchAt;
            private Switch options;
            private UniText[] labels;
            private UniShapeMask frame;
            private UniShapeMask hole;
            private UniShape holeShape;
            private CanvasGroup framed;
            private CanvasGroup lit;
            private Color soft;
            private Color ink;
            private Vector2 size;

            public MaskPart(Stage stage, Vector2 start, Vector2 cell, Vector2 switchAt)
                : base(stage, "Mask", start, Script(cell, switchAt), SwitchPace)
            {
                this.cell = cell;
                this.switchAt = switchAt;
            }

            private static Beat[] Script(Vector2 cell, Vector2 at)
            {
                var beats = new List<Beat> { Beat.Wait(ShowFor) };
                for (var i = 1; i < Names.Length; i++)
                {
                    beats.Add(Beat.To(at + new Vector2((i - (Names.Length - 1) * 0.5f) * cell.x, 0f), null, cell.x * 0.6f));
                    beats.Add(Beat.Click("mask" + i, true));
                    beats.Add(Beat.Wait(WipeFor));
                }

                return beats.ToArray();
            }

            public void Build(GuiShapesScene scene, Stage stage, Vector2 at, Vector2 frameSize)
            {
                var theme = scene.theme;
                size = frameSize;
                soft = theme.TextSoft;
                ink = theme.Text;

                var masked = stage.Shape("Masked", Root, ShapeKind.RoundedRect, frameSize.y * 0.08f);
                stage.Box(masked.Rect, at, frameSize);
                frame = masked.Rect.gameObject.AddComponent<UniShapeMask>();
                Stage.Linear(frame.Paint, Color.white, Color.white, WipeAngle);
                framed = masked.Rect.gameObject.AddComponent<CanvasGroup>();
                Photo(scene, stage, masked.Rect);

                var spot = stage.Node("Spotlit", Root);
                stage.Box(spot, at, frameSize);
                lit = spot.gameObject.AddComponent<CanvasGroup>();
                var card = stage.Shape("Card", spot, ShapeKind.RoundedRect, frameSize.y * 0.08f);
                stage.Stretch(card.Rect);
                card.Rect.gameObject.AddComponent<UniShapeMask>().ShowShape = false;
                Photo(scene, stage, card.Rect);
                var veil = stage.Shape("Veil", card.Rect, ShapeKind.Circle);
                stage.Stretch(veil.Rect);
                hole = veil.Rect.gameObject.AddComponent<UniShapeMask>();
                hole.Invert = true;
                hole.Feather = HoleFeather;
                holeShape = veil.Shape;
                var dark = stage.Shape("Dark", veil.Rect, ShapeKind.RoundedRect, 0f);
                stage.Stretch(dark.Rect);
                Stage.Solid(dark.Fill, new Color(0.04f, 0.02f, 0.02f, VeilStrength));

                options = new Switch(stage, Root, theme, switchAt, cell, Names.Length);
                labels = new UniText[Names.Length];
                for (var i = 0; i < Names.Length; i++)
                {
                    var label = stage.Label(options.Rect, Names[i], 26f, soft, stretch: false, face: theme.DisplayFace);
                    stage.Box(label.rectTransform, options[i], cell);
                    labels[i] = label;
                }
            }

            /// <summary>
            /// The frame's mask ramp with its soft edge at <paramref name="edge"/> along the wipe: the photo kept before it,
            /// gone after it; an edge past the far end keeps all of it.
            /// </summary>
            private static Ramp Wipe(float edge)
            {
                var clear = new Color(1f, 1f, 1f, 0f);
                var start = Mathf.Clamp(edge - WipeBand, 0.001f, 0.998f);
                var end = Mathf.Clamp(edge + WipeBand, start + 0.001f, 0.999f);
                return new Ramp(new[] { new Stop(0f, Color.white), new Stop(start, Color.white), new Stop(end, edge > 1f ? Color.white : clear),
                    new Stop(1f, edge > 1f ? Color.white : clear) }, Interp.Perceptual);
            }

            private static void Photo(GuiShapesScene scene, Stage stage, RectTransform parent)
            {
                var picture = stage.Shape("Photo", parent, ShapeKind.RoundedRect, 0f);
                stage.Stretch(picture.Rect);
                Stage.Textured(picture.Fill, scene.photo, PaintFit.Cover);
            }

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                for (var i = 0; i < clicks.Length; i++) clicks[i] = ClickAt("mask" + (i + 1));
                options.Pose(played, clicks, weights);
                var t = Mathf.Max(0f, played);
                for (var i = 0; i < Names.Length; i++)
                {
                    labels[i].color = Color.LerpUnclamped(soft, ink, weights[i]);
                    labels[i].rectTransform.localScale = Vector3.one * (i == 0 ? 1f : Kick(played, clicks[i - 1]));
                }

                frame.Feather = FeatherMost * (0.5f - 0.5f * Mathf.Cos(t * FeatherRate)) * (1f - weights[1] - weights[2]) + EdgeFeather;
                var wiped = Mathf.Clamp01((played - clicks[0]) / (clicks[1] - clicks[0]));
                var edge = Mathf.LerpUnclamped(1f + WipeBand, WipeMiddle + WipeReach * Mathf.Cos(wiped * Mathf.PI * 2f), weights[1]);
                frame.Paint.Gradient = Wipe(edge);
                framed.alpha = 1f - weights[2];
                lit.alpha = weights[2];
                lit.gameObject.SetActive(weights[2] > 0.001f);

                var wander = new Vector2(Mathf.Sin(t * 1.3f) * size.x * 0.3f, Mathf.Sin(t * 2.1f + 0.6f) * size.y * 0.22f);
                holeShape.Rect = new RectPlacement
                {
                    anchorMin = new Vector2(0.5f, 0.5f),
                    anchorMax = new Vector2(0.5f, 0.5f),
                    pivot = new Vector2(0.5f, 0.5f),
                    sizeDelta = Vector2.one * size.y * HoleSize,
                    anchoredPosition = wander
                };

                var drop = new Vector2(0f, -SwitchDrop * Motion.Swish.Window(played, Out - ActionAt, SwishFor));
                options.Rect.anchoredPosition = drop;
                Pointer.Offset += drop;
            }

            private const float ShowFor = 1.1f;

            /// <summary>How far the frame's edge feathers at most, and the feather it keeps at rest, in its own units.</summary>
            private const float FeatherMost = 90f;

            private const float EdgeFeather = 2f;
            private const float FeatherRate = 3f;
            /// <summary>How long the gradient wipes the photo away and back, and the spotlight wanders, before the pointer moves on.</summary>
            private const float WipeFor = 2.2f;

            /// <summary>The wipe's direction, how wide its soft edge is, and the middle and reach of its travel along the frame: from the whole photo to a quarter of it and back.</summary>
            private const float WipeAngle = 20f;

            private const float WipeBand = 0.12f;
            private const float WipeMiddle = 0.68f;
            private const float WipeReach = 0.42f;
            private const float HoleSize = 0.62f;
            private const float HoleFeather = 46f;
            private const float VeilStrength = 0.82f;
        }
    }
}
