using UnityEngine;

namespace LightSide.Promo
{
    public sealed partial class GuiStylesScene
    {
        /// <summary>
        /// The icons and items of the games' screens, drawn from shapes: each a node of its parts, centered on its place
        /// and <c>s</c> units across. Solid items are lit from above, a lighter top fading to their color.
        /// </summary>
        internal static class Glyphs
        {
            /// <summary>
            /// Draws the glyphs outside the styles section, on the kit's own surfaces: in no look of the six, with the
            /// warm gold coin, the teal gem and the medal metals the film's kit screens use.
            /// </summary>
            public static Builder Plain(Stage stage) => new(stage, PlainLook);

            private static readonly Look PlainLook = new()
            {
                Radius = 10f, Depth = Depth.Lip, Outline = Hex(0x1A1218),
                Coin = Hex(0xFFC84A), CoinDark = Hex(0xC77700), Gem = Hex(0x16C9A4), GemDark = Hex(0x0B7A63),
                Gold = Hex(0xFFC84A), Silver = Hex(0xD5DCF5), Bronze = Hex(0xE79A5C)
            };

            /// <summary>A part of a glyph lit from above: a lighter top fading to <paramref name="color"/>.</summary>
            private static Widget Lit(Builder k, Transform parent, string name, ShapeKind kind, Vector2 at, Vector2 size, Color color,
                float radius = 0f, float turn = 0f)
            {
                var part = k.Shape(parent, name, kind, at, size, radius);
                Stage.Linear(part.Fill, Color.Lerp(color, Color.white, 0.3f), Color.Lerp(color, Color.black, 0.1f), 270f);
                if (turn != 0f) part.Rect.localRotation = Quaternion.Euler(0f, 0f, turn);
                return part;
            }

            private static Widget Flat(Builder k, Transform parent, string name, ShapeKind kind, Vector2 at, Vector2 size, Color color,
                float radius = 0f, float turn = 0f)
            {
                var part = k.Fill(parent, name, kind, at, size, color, radius);
                if (turn != 0f) part.Rect.localRotation = Quaternion.Euler(0f, 0f, turn);
                return part;
            }

            /// <summary>A part outlined by <paramref name="points"/> on a grid twenty units across, scaled to <paramref name="s"/>.</summary>
            private static Widget Outline(Builder k, Transform parent, string name, Vector2[] points, float s, Vector2 at, Color color,
                bool smooth = false, int[] corners = null, bool lit = true)
            {
                var part = k.Shape(parent, name, ShapeKind.RoundedRect, at, Vector2.one * s, 0f);
                part.Shape.Shape = Stage.Contour(points, s / 20f, true, smooth, corners);
                if (lit) Stage.Linear(part.Fill, Color.Lerp(color, Color.white, 0.3f), Color.Lerp(color, Color.black, 0.1f), 270f);
                else Stage.Solid(part.Fill, color);
                return part;
            }

            /// <summary>A white glint on a lit item's upper left.</summary>
            private static void Glint(Builder k, Transform parent, Vector2 at, Vector2 size, float turn = -30f) =>
                Flat(k, parent, "Glint", ShapeKind.Ellipse, at, size, Theme.Fade(Color.white, 0.65f), 0f, turn);

            /// <summary>Edges every shape under <paramref name="node"/> in <paramref name="ink"/>, the way a cartoon look inks its items.</summary>
            public static void Ink(RectTransform node, Color ink, float width)
            {
                foreach (var shape in node.GetComponentsInChildren<UniShape>())
                    if (shape.name != "Glint")
                        Stage.AddStroke(shape, ink, width, 1f);
            }

            /// <summary>Edges every piece of <paramref name="node"/> in a darker shade of its own fill, as glossy art inks its pieces.</summary>
            public static void Ink(RectTransform node, float width)
            {
                foreach (var shape in node.GetComponentsInChildren<UniShape>())
                {
                    if (shape.name == "Glint") continue;
                    var color = Color.clear;
                    foreach (var layer in shape.Layers)
                        if (layer is FillLayer fill)
                        {
                            var paint = fill.Paint;
                            color = paint.Kind == PaintSourceKind.Gradient ? paint.Gradient.Evaluate(1f) : paint.Color;
                            break;
                        }

                    if (color.a < 0.5f) continue;
                    Color.RGBToHSV(color, out var hue, out var saturation, out var value);
                    Stage.AddStroke(shape, Color.HSVToRGB(hue, Mathf.Min(1f, saturation * 1.1f + 0.15f), value * 0.5f), width, 1f);
                }
            }

            public static RectTransform Coin(Builder k, Transform parent, Vector2 at, float s)
            {
                var node = k.Node(parent, "Coin", at);
                var face = k.Ramp(node, "Face", k.L.Depth == Depth.Pixel ? ShapeKind.Octagon : ShapeKind.Circle, Vector2.zero, Vector2.one * s,
                    Color.Lerp(k.L.Coin, Color.white, 0.35f), k.L.CoinDark, 0f);
                Stage.AddStroke(face.Shape, k.L.CoinDark, s * 0.07f, -1f);
                var rim = k.Fill(node, "Rim", ShapeKind.Circle, Vector2.zero, Vector2.one * s * 0.6f, Color.clear, 0f);
                Stage.AddStroke(rim.Shape, Theme.Fade(k.L.CoinDark, 0.7f), s * 0.06f, 0f);
                Glint(k, node, new Vector2(-s * 0.16f, s * 0.18f), new Vector2(s * 0.22f, s * 0.13f));
                return node;
            }

            /// <summary>A pile of coins: three coins leaning on one another.</summary>
            public static RectTransform Coins(Builder k, Transform parent, Vector2 at, float s)
            {
                var node = k.Node(parent, "Coins", at);
                Coin(k, node, new Vector2(-s * 0.2f, -s * 0.16f), s * 0.56f);
                Coin(k, node, new Vector2(s * 0.22f, -s * 0.12f), s * 0.56f);
                Coin(k, node, new Vector2(0f, s * 0.14f), s * 0.6f);
                return node;
            }

            public static RectTransform Gem(Builder k, Transform parent, Vector2 at, float s)
            {
                var node = k.Node(parent, "Gem", at);
                var face = k.Ramp(node, "Face", ShapeKind.Rhombus, Vector2.zero, new Vector2(s * 0.86f, s), Color.Lerp(k.L.Gem, Color.white, 0.4f), k.L.GemDark, 0f);
                Stage.AddStroke(face.Shape, k.L.GemDark, s * 0.06f, -1f);
                Flat(k, node, "Facet", ShapeKind.Rhombus, new Vector2(0f, s * 0.12f), new Vector2(s * 0.42f, s * 0.44f), Theme.Fade(Color.white, 0.45f));
                return node;
            }

            public static RectTransform Heart(Builder k, Transform parent, Vector2 at, float s, Color color)
            {
                var node = k.Node(parent, "Heart", at);
                Lit(k, node, "Heart", ShapeKind.Heart, Vector2.zero, Vector2.one * s, color);
                Glint(k, node, new Vector2(-s * 0.22f, s * 0.16f), new Vector2(s * 0.2f, s * 0.12f), 35f);
                return node;
            }

            public static RectTransform Star(Builder k, Transform parent, Vector2 at, float s, Color color)
            {
                var node = k.Node(parent, "Star", at);
                var star = k.Fill(node, "Star", ShapeKind.Star, Vector2.zero, Vector2.one * s, color, 0f);
                star.Outline.StarSharpness = 0.42f;
                star.Outline.Rounding = k.L.Depth == Depth.Pixel ? 0f : s * 0.06f;
                return node;
            }

            public static RectTransform Gear(Builder k, Transform parent, Vector2 at, float s, Color color, Color hole)
            {
                var node = k.Node(parent, "Gear", at);
                var teeth = k.Fill(node, "Teeth", ShapeKind.Star, Vector2.zero, Vector2.one * s, color, 0f);
                teeth.Outline.StarPoints = 8;
                teeth.Outline.StarSharpness = 0.18f;
                teeth.Outline.Rounding = s * 0.04f;
                k.Fill(node, "Body", ShapeKind.Circle, Vector2.zero, Vector2.one * s * 0.72f, color, 0f);
                k.Fill(node, "Hole", ShapeKind.Circle, Vector2.zero, Vector2.one * s * 0.3f, hole, 0f);
                return node;
            }

            public static RectTransform Check(Builder k, Transform parent, Vector2 at, float s, Color color)
            {
                var node = k.Node(parent, "Check", at);
                Flat(k, node, "Short", ShapeKind.Capsule, new Vector2(-s * 0.2f, -s * 0.06f), new Vector2(s * 0.42f, s * 0.17f), color, 0f, -45f);
                Flat(k, node, "Long", ShapeKind.Capsule, new Vector2(s * 0.12f, s * 0.06f), new Vector2(s * 0.74f, s * 0.17f), color, 0f, 50f);
                return node;
            }

            public static RectTransform Cross(Builder k, Transform parent, Vector2 at, float s, Color color)
            {
                var node = k.Node(parent, "Cross", at);
                foreach (var turn in new[] { 45f, -45f })
                    Flat(k, node, "Bar", ShapeKind.Capsule, Vector2.zero, new Vector2(s, s * 0.2f), color, 0f, turn);
                return node;
            }

            public static RectTransform Plus(Builder k, Transform parent, Vector2 at, float s, Color color)
            {
                var node = k.Node(parent, "Plus", at);
                Flat(k, node, "Bar", ShapeKind.Capsule, Vector2.zero, new Vector2(s, s * 0.24f), color);
                Flat(k, node, "Bar", ShapeKind.Capsule, Vector2.zero, new Vector2(s * 0.24f, s), color);
                return node;
            }

            /// <summary>A chevron pointing left, or right when <paramref name="right"/>.</summary>
            public static RectTransform Chevron(Builder k, Transform parent, Vector2 at, float s, Color color, bool right = false)
            {
                var node = k.Node(parent, "Chevron", at);
                var x = right ? 1f : -1f;
                Flat(k, node, "Upper", ShapeKind.Capsule, new Vector2(0f, s * 0.17f), new Vector2(s * 0.62f, s * 0.2f), color, 0f, x * -45f);
                Flat(k, node, "Lower", ShapeKind.Capsule, new Vector2(0f, -s * 0.17f), new Vector2(s * 0.62f, s * 0.2f), color, 0f, x * 45f);
                return node;
            }

            public static RectTransform Play(Builder k, Transform parent, Vector2 at, float s, Color color)
            {
                var node = k.Node(parent, "Play", at);
                Flat(k, node, "Arrow", ShapeKind.Triangle, new Vector2(s * 0.06f, 0f), new Vector2(s * 0.8f, s * 0.7f), color, s * 0.06f, -90f);
                return node;
            }

            public static RectTransform Lock(Builder k, Transform parent, Vector2 at, float s, Color color)
            {
                var node = k.Node(parent, "Lock", at);
                var shackle = k.Fill(node, "Shackle", ShapeKind.Arc, new Vector2(0f, s * 0.12f), Vector2.one * s * 0.6f, color, 0f);
                shackle.Outline.Start = 0f;
                shackle.Outline.End = 180f;
                shackle.Outline.Thickness = 0.32f;
                Flat(k, node, "Leg", ShapeKind.RoundedRect, new Vector2(-s * 0.24f, s * 0.04f), new Vector2(s * 0.1f, s * 0.18f), color);
                Flat(k, node, "Leg", ShapeKind.RoundedRect, new Vector2(s * 0.24f, s * 0.04f), new Vector2(s * 0.1f, s * 0.18f), color);
                Flat(k, node, "Body", ShapeKind.RoundedRect, new Vector2(0f, -s * 0.18f), new Vector2(s * 0.72f, s * 0.54f), color, s * 0.1f);
                return node;
            }

            public static RectTransform Clock(Builder k, Transform parent, Vector2 at, float s, Color face, Color hands)
            {
                var node = k.Node(parent, "Clock", at);
                var dial = Flat(k, node, "Dial", ShapeKind.Circle, Vector2.zero, Vector2.one * s, face);
                Stage.AddStroke(dial.Shape, hands, s * 0.1f, -1f);
                var hour = Flat(k, node, "Hour", ShapeKind.Capsule, new Vector2(0f, s * 0.12f), new Vector2(s * 0.1f, s * 0.32f), hands);
                hour.Rect.pivot = new Vector2(0.5f, 0.16f);
                hour.Rect.anchoredPosition = Vector2.zero;
                var minute = Flat(k, node, "Minute", ShapeKind.Capsule, Vector2.zero, new Vector2(s * 0.1f, s * 0.26f), hands, 0f, -90f);
                minute.Rect.pivot = new Vector2(0.5f, 0.2f);
                minute.Rect.anchoredPosition = Vector2.zero;
                return node;
            }

            public static RectTransform Person(Builder k, Transform parent, Vector2 at, float s, Color color)
            {
                var node = k.Node(parent, "Person", at);
                Flat(k, node, "Head", ShapeKind.Circle, new Vector2(0f, s * 0.2f), Vector2.one * s * 0.38f, color);
                var body = Flat(k, node, "Body", ShapeKind.RoundedRect, new Vector2(0f, -s * 0.24f), new Vector2(s * 0.7f, s * 0.36f), color, s * 0.18f);
                body.Outline.UniformRadius = false;
                body.Outline.CornerRadii = new Vector4(s * 0.3f, s * 0.3f, s * 0.06f, s * 0.06f);
                return node;
            }

            public static RectTransform Team(Builder k, Transform parent, Vector2 at, float s, Color front, Color back)
            {
                var node = k.Node(parent, "Team", at);
                Person(k, node, new Vector2(-s * 0.2f, s * 0.06f), s * 0.8f, back);
                Person(k, node, new Vector2(s * 0.14f, -s * 0.04f), s * 0.9f, front);
                return node;
            }

            public static RectTransform Gift(Builder k, Transform parent, Vector2 at, float s, Color box, Color ribbon)
            {
                var node = k.Node(parent, "Gift", at);
                foreach (var side in new[] { -1f, 1f })
                    Lit(k, node, "Bow", ShapeKind.Ellipse, new Vector2(side * s * 0.15f, s * 0.36f), new Vector2(s * 0.32f, s * 0.2f), ribbon, 0f, side * 28f);
                Lit(k, node, "Box", ShapeKind.RoundedRect, new Vector2(0f, -s * 0.14f), new Vector2(s * 0.78f, s * 0.56f), box, s * 0.06f);
                Lit(k, node, "Lid", ShapeKind.RoundedRect, new Vector2(0f, s * 0.18f), new Vector2(s * 0.9f, s * 0.22f), box, s * 0.06f);
                Flat(k, node, "Ribbon", ShapeKind.RoundedRect, new Vector2(0f, -s * 0.04f), new Vector2(s * 0.16f, s * 0.78f), ribbon);
                Flat(k, node, "Knot", ShapeKind.Circle, new Vector2(0f, s * 0.3f), Vector2.one * s * 0.14f, ribbon);
                return node;
            }

            /// <summary>A prize wheel of six wedges under a pointer.</summary>
            public static RectTransform Wheel(Builder k, Transform parent, Vector2 at, float s, Color[] wedges, Color rim)
            {
                var node = k.Node(parent, "Wheel", at);
                Flat(k, node, "Rim", ShapeKind.Circle, Vector2.zero, Vector2.one * s, rim);
                var spokes = k.Node(node, "Wedges", Vector2.zero);
                for (var i = 0; i < 6; i++)
                {
                    var wedge = Flat(k, spokes, "Wedge", ShapeKind.Pie, Vector2.zero, Vector2.one * s * 0.84f, wedges[i % wedges.Length]);
                    wedge.Outline.Start = i * 60f;
                    wedge.Outline.End = i * 60f + 60f;
                }

                Flat(k, node, "Hub", ShapeKind.Circle, Vector2.zero, Vector2.one * s * 0.2f, rim);
                Flat(k, node, "Pointer", ShapeKind.Triangle, new Vector2(0f, s * 0.46f), new Vector2(s * 0.24f, s * 0.22f), rim, s * 0.03f, 180f);
                return node;
            }

            /// <summary>A wrapped candy: a striped sweet between two twists of wrapper.</summary>
            public static RectTransform Candy(Builder k, Transform parent, Vector2 at, float s, Color color)
            {
                var node = k.Node(parent, "Candy", at);
                node.localRotation = Quaternion.Euler(0f, 0f, 20f);
                foreach (var side in new[] { -1f, 1f })
                    Lit(k, node, "Twist", ShapeKind.Triangle, new Vector2(side * s * 0.36f, 0f), new Vector2(s * 0.44f, s * 0.36f), color, s * 0.04f, side * 90f);
                Lit(k, node, "Sweet", ShapeKind.Circle, Vector2.zero, Vector2.one * s * 0.6f, color);
                foreach (var x in new[] { -0.12f, 0.12f })
                    Flat(k, node, "Stripe", ShapeKind.Capsule, new Vector2(s * x, 0f), new Vector2(s * 0.08f, s * 0.5f), Theme.Fade(Color.white, 0.55f), 0f, -20f);
                Glint(k, node, new Vector2(-s * 0.1f, s * 0.15f), new Vector2(s * 0.14f, s * 0.08f));
                return node;
            }

            /// <summary>A jelly bean, glossy and leaning.</summary>
            public static RectTransform Bean(Builder k, Transform parent, Vector2 at, float s, Color color)
            {
                var node = k.Node(parent, "Bean", at);
                Lit(k, node, "Bean", ShapeKind.Capsule, Vector2.zero, new Vector2(s * 0.92f, s * 0.54f), color, 0f, 28f);
                Glint(k, node, new Vector2(-s * 0.12f, s * 0.08f), new Vector2(s * 0.3f, s * 0.1f), 28f);
                return node;
            }

            public static RectTransform Bomb(Builder k, Transform parent, Vector2 at, float s, Color body, Color spark)
            {
                var node = k.Node(parent, "Bomb", at);
                Flat(k, node, "Fuse", ShapeKind.Capsule, new Vector2(s * 0.24f, s * 0.3f), new Vector2(s * 0.1f, s * 0.34f), new Color(0.55f, 0.42f, 0.3f), 0f, -40f);
                Lit(k, node, "Cap", ShapeKind.RoundedRect, new Vector2(s * 0.14f, s * 0.2f), new Vector2(s * 0.24f, s * 0.16f), body, s * 0.03f, -40f);
                Lit(k, node, "Body", ShapeKind.Circle, new Vector2(-s * 0.04f, -s * 0.06f), Vector2.one * s * 0.74f, body);
                var flare = Flat(k, node, "Spark", ShapeKind.Star, new Vector2(s * 0.36f, s * 0.44f), Vector2.one * s * 0.34f, spark);
                flare.Outline.StarPoints = 6;
                flare.Outline.StarSharpness = 0.6f;
                Glint(k, node, new Vector2(-s * 0.2f, s * 0.08f), new Vector2(s * 0.18f, s * 0.11f));
                return node;
            }

            public static RectTransform Hammer(Builder k, Transform parent, Vector2 at, float s, Color head, Color handle)
            {
                var node = k.Node(parent, "Hammer", at);
                node.localRotation = Quaternion.Euler(0f, 0f, 35f);
                Lit(k, node, "Handle", ShapeKind.RoundedRect, new Vector2(0f, -s * 0.12f), new Vector2(s * 0.14f, s * 0.72f), handle, s * 0.05f);
                Lit(k, node, "Head", ShapeKind.RoundedRect, new Vector2(0f, s * 0.26f), new Vector2(s * 0.62f, s * 0.28f), head, s * 0.06f);
                Glint(k, node, new Vector2(-s * 0.12f, s * 0.31f), new Vector2(s * 0.26f, s * 0.06f), 0f);
                return node;
            }

            /// <summary>A pin on a map, its head a disc the caller fills, its point below.</summary>
            public static (RectTransform node, RectTransform head) Pin(Builder k, Transform parent, Vector2 at, float s, Color color)
            {
                var node = k.Node(parent, "Pin", at);
                Flat(k, node, "Point", ShapeKind.Triangle, new Vector2(0f, -s * 0.46f), new Vector2(s * 0.4f, s * 0.34f), color, s * 0.05f, 180f);
                var head = Flat(k, node, "Head", ShapeKind.Circle, Vector2.zero, Vector2.one * s, color);
                return (node, head.Rect);
            }

            public static RectTransform Crown(Builder k, Transform parent, Vector2 at, float s, Color color)
            {
                var node = k.Node(parent, "Crown", at);
                Outline(k, node, "Band", CrownOutline, s, Vector2.zero, color);
                foreach (var x in new[] { -0.5f, 0f, 0.5f })
                    Flat(k, node, "Jewel", ShapeKind.Circle, new Vector2(s * x * 0.5f, x == 0f ? s * 0.4f : s * 0.3f), Vector2.one * s * 0.13f,
                        Color.Lerp(color, Color.white, 0.5f));
                return node;
            }

            public static RectTransform Chest(Builder k, Transform parent, Vector2 at, float s, Color wood, Color trim)
            {
                var node = k.Node(parent, "Chest", at);
                Lit(k, node, "Body", ShapeKind.RoundedRect, new Vector2(0f, -s * 0.15f), new Vector2(s, s * 0.52f), wood, s * 0.08f);
                var lid = Lit(k, node, "Lid", ShapeKind.RoundedRect, new Vector2(0f, s * 0.2f), new Vector2(s, s * 0.34f), wood, s * 0.16f);
                lid.Outline.UniformRadius = false;
                lid.Outline.CornerRadii = new Vector4(s * 0.22f, s * 0.22f, s * 0.03f, s * 0.03f);
                Flat(k, node, "Seam", ShapeKind.RoundedRect, new Vector2(0f, s * 0.04f), new Vector2(s, s * 0.05f), Color.Lerp(wood, Color.black, 0.45f));
                foreach (var x in new[] { -0.33f, 0.33f })
                    Lit(k, node, "Band", ShapeKind.RoundedRect, new Vector2(s * x, s * 0.02f), new Vector2(s * 0.12f, s * 0.86f), trim, s * 0.02f);
                Lit(k, node, "Lock", ShapeKind.RoundedRect, new Vector2(0f, s * 0.04f), new Vector2(s * 0.2f, s * 0.26f), trim, s * 0.05f);
                Flat(k, node, "Keyhole", ShapeKind.Capsule, new Vector2(0f, s * 0.01f), new Vector2(s * 0.05f, s * 0.11f), Color.Lerp(trim, Color.black, 0.6f));
                return node;
            }

            public static RectTransform Hat(Builder k, Transform parent, Vector2 at, float s, Color cloth, Color band, Color star)
            {
                var node = k.Node(parent, "Hat", at);
                Lit(k, node, "Cone", ShapeKind.Triangle, new Vector2(s * 0.04f, s * 0.14f), new Vector2(s * 0.62f, s * 0.78f), cloth, s * 0.08f, -8f);
                Lit(k, node, "Brim", ShapeKind.Ellipse, new Vector2(0f, -s * 0.28f), new Vector2(s, s * 0.28f), cloth);
                Lit(k, node, "Band", ShapeKind.Capsule, new Vector2(0f, -s * 0.16f), new Vector2(s * 0.58f, s * 0.12f), band);
                foreach (var (x, y, z) in new[] { (-0.04f, 0.12f, 0.2f), (0.1f, 0.34f, 0.13f) })
                {
                    var spark = Flat(k, node, "Star", ShapeKind.Star, new Vector2(s * x, s * y), Vector2.one * s * z, star);
                    spark.Outline.StarSharpness = 0.45f;
                }

                return node;
            }

            public static RectTransform Shades(Builder k, Transform parent, Vector2 at, float s, Color lens, Color frame)
            {
                var node = k.Node(parent, "Shades", at);
                Flat(k, node, "Bridge", ShapeKind.Capsule, new Vector2(0f, s * 0.08f), new Vector2(s * 0.2f, s * 0.07f), frame);
                foreach (var side in new[] { -1f, 1f })
                {
                    var glass = Lit(k, node, "Lens", ShapeKind.RoundedRect, new Vector2(side * s * 0.25f, 0f), new Vector2(s * 0.44f, s * 0.32f), lens, s * 0.13f);
                    Stage.AddStroke(glass.Shape, frame, s * 0.05f, 1f);
                    Flat(k, node, "Glint", ShapeKind.Capsule, new Vector2(side * s * 0.25f - s * 0.06f, s * 0.04f), new Vector2(s * 0.26f, s * 0.05f),
                        Theme.Fade(Color.white, 0.7f), 0f, 30f);
                }

                return node;
            }

            public static RectTransform Rocket(Builder k, Transform parent, Vector2 at, float s, Color hull, Color fins, Color flame)
            {
                var node = k.Node(parent, "Rocket", at);
                node.localRotation = Quaternion.Euler(0f, 0f, -38f);
                Lit(k, node, "Flame", ShapeKind.Ellipse, new Vector2(0f, -s * 0.44f), new Vector2(s * 0.2f, s * 0.34f), flame);
                foreach (var side in new[] { -1f, 1f })
                    Lit(k, node, "Fin", ShapeKind.Triangle, new Vector2(side * s * 0.16f, -s * 0.24f), new Vector2(s * 0.22f, s * 0.3f), fins, s * 0.03f);
                Lit(k, node, "Hull", ShapeKind.Ellipse, new Vector2(0f, s * 0.04f), new Vector2(s * 0.32f, s * 0.82f), hull);
                var window = Flat(k, node, "Window", ShapeKind.Circle, new Vector2(0f, s * 0.12f), Vector2.one * s * 0.15f, new Color(0.55f, 0.85f, 1f));
                Stage.AddStroke(window.Shape, fins, s * 0.035f, 1f);
                return node;
            }

            public static RectTransform Wand(Builder k, Transform parent, Vector2 at, float s, Color stick, Color star)
            {
                var node = k.Node(parent, "Wand", at);
                Lit(k, node, "Stick", ShapeKind.Capsule, new Vector2(-s * 0.08f, -s * 0.1f), new Vector2(s * 0.1f, s * 0.74f), stick, 0f, 35f);
                var tip = Lit(k, node, "Star", ShapeKind.Star, new Vector2(s * 0.2f, s * 0.26f), Vector2.one * s * 0.46f, star);
                tip.Outline.StarSharpness = 0.45f;
                tip.Outline.Rounding = s * 0.03f;
                foreach (var (x, y, z) in new[] { (0.38f, -0.02f, 0.12f), (-0.08f, 0.38f, 0.1f) })
                {
                    var spark = Flat(k, node, "Sparkle", ShapeKind.Star, new Vector2(s * x, s * y), Vector2.one * s * z, Theme.Fade(star, 0.9f));
                    spark.Outline.StarPoints = 4;
                    spark.Outline.StarSharpness = 0.8f;
                }

                return node;
            }

            /// <summary>A little slime of a pet: a glossy dome with eyes.</summary>
            public static RectTransform Slime(Builder k, Transform parent, Vector2 at, float s, Color color)
            {
                var node = k.Node(parent, "Slime", at);
                var body = Lit(k, node, "Body", ShapeKind.RoundedRect, new Vector2(0f, -s * 0.06f), new Vector2(s * 0.86f, s * 0.66f), color, s * 0.3f);
                body.Outline.UniformRadius = false;
                body.Outline.CornerRadii = new Vector4(s * 0.4f, s * 0.4f, s * 0.14f, s * 0.14f);
                foreach (var side in new[] { -1f, 1f })
                {
                    Flat(k, node, "Eye", ShapeKind.Circle, new Vector2(side * s * 0.15f, -s * 0.02f), Vector2.one * s * 0.17f, Color.white);
                    Flat(k, node, "Pupil", ShapeKind.Circle, new Vector2(side * s * 0.15f + s * 0.02f, -s * 0.03f), Vector2.one * s * 0.09f, new Color(0.1f, 0.08f, 0.18f));
                }

                Glint(k, node, new Vector2(-s * 0.2f, s * 0.16f), new Vector2(s * 0.2f, s * 0.1f));
                return node;
            }

            public static RectTransform Bolt(Builder k, Transform parent, Vector2 at, float s, Color color, bool lit = true)
            {
                var node = k.Node(parent, "Bolt", at);
                Outline(k, node, "Bolt", BoltOutline, s, Vector2.zero, color, false, null, lit);
                return node;
            }

            public static RectTransform Note(Builder k, Transform parent, Vector2 at, float s, Color color)
            {
                var node = k.Node(parent, "Note", at);
                Flat(k, node, "Head", ShapeKind.Ellipse, new Vector2(-s * 0.14f, -s * 0.28f), new Vector2(s * 0.42f, s * 0.32f), color, 0f, 20f);
                Flat(k, node, "Stem", ShapeKind.RoundedRect, new Vector2(s * 0.05f, s * 0.08f), new Vector2(s * 0.09f, s * 0.68f), color);
                Flat(k, node, "Flag", ShapeKind.RoundedRect, new Vector2(s * 0.2f, s * 0.32f), new Vector2(s * 0.3f, s * 0.12f), color, s * 0.05f, -24f);
                return node;
            }

            /// <summary>A credit: a hexagonal token with a hexagon cut in it.</summary>
            public static RectTransform Credit(Builder k, Transform parent, Vector2 at, float s, Color color)
            {
                var node = k.Node(parent, "Credit", at);
                Lit(k, node, "Token", ShapeKind.Hexagon, Vector2.zero, Vector2.one * s, color);
                var inner = Flat(k, node, "Mark", ShapeKind.Hexagon, Vector2.zero, Vector2.one * s * 0.5f, Color.clear);
                Stage.AddStroke(inner.Shape, Color.Lerp(color, Color.black, 0.5f), s * 0.08f, 0f);
                return node;
            }

            /// <summary>An alloy ingot seen from a corner: three faces of a cube.</summary>
            public static RectTransform Cube(Builder k, Transform parent, Vector2 at, float s, Color color)
            {
                var node = k.Node(parent, "Cube", at);
                Outline(k, node, "Top", CubeTop, s, Vector2.zero, Color.Lerp(color, Color.white, 0.35f), false, null, false);
                Outline(k, node, "Left", CubeLeft, s, Vector2.zero, color, false, null, false);
                Outline(k, node, "Right", CubeRight, s, Vector2.zero, Color.Lerp(color, Color.black, 0.3f), false, null, false);
                return node;
            }

            /// <summary>The fighter seen from above, nose up: a hull, swept wings and a cockpit.</summary>
            public static RectTransform Ship(Builder k, Transform parent, Vector2 at, float s, Color hull, Color glass)
            {
                var node = k.Node(parent, "Ship", at);
                Outline(k, node, "Hull", ShipOutline, s, Vector2.zero, hull, false, null, false);
                Flat(k, node, "Cockpit", ShapeKind.Ellipse, new Vector2(0f, s * 0.2f), new Vector2(s * 0.09f, s * 0.2f), glass);
                return node;
            }

            public static RectTransform Flame(Builder k, Transform parent, Vector2 at, float s, Color outer, Color inner)
            {
                var node = k.Node(parent, "Flame", at);
                Outline(k, node, "Outer", FlameOutline, s, Vector2.zero, outer, true);
                Outline(k, node, "Inner", FlameOutline, s * 0.55f, new Vector2(0f, -s * 0.16f), inner, true, null, false);
                return node;
            }

            public static RectTransform Shield(Builder k, Transform parent, Vector2 at, float s, Color color)
            {
                var node = k.Node(parent, "Shield", at);
                Outline(k, node, "Plate", ShieldOutline, s, Vector2.zero, color, true, new[] { 0, 2 });
                return node;
            }

            public static RectTransform Sword(Builder k, Transform parent, Vector2 at, float s, Color blade, Color hilt, float turn = -45f)
            {
                var node = k.Node(parent, "Sword", at);
                node.localRotation = Quaternion.Euler(0f, 0f, turn);
                Outline(k, node, "Blade", BladeOutline, s, Vector2.zero, blade);
                Flat(k, node, "Fuller", ShapeKind.Capsule, new Vector2(0f, s * 0.12f), new Vector2(s * 0.03f, s * 0.5f), Theme.Fade(Color.white, 0.5f));
                Lit(k, node, "Guard", ShapeKind.Capsule, new Vector2(0f, -s * 0.2f), new Vector2(s * 0.46f, s * 0.1f), hilt);
                Lit(k, node, "Grip", ShapeKind.RoundedRect, new Vector2(0f, -s * 0.32f), new Vector2(s * 0.09f, s * 0.18f), Color.Lerp(hilt, Color.black, 0.4f), s * 0.03f);
                Lit(k, node, "Pommel", ShapeKind.Circle, new Vector2(0f, -s * 0.43f), Vector2.one * s * 0.12f, hilt);
                return node;
            }

            public static RectTransform Flask(Builder k, Transform parent, Vector2 at, float s, Color liquid, Color glass)
            {
                var node = k.Node(parent, "Flask", at);
                Flat(k, node, "Neck", ShapeKind.RoundedRect, new Vector2(0f, s * 0.3f), new Vector2(s * 0.24f, s * 0.3f), glass, s * 0.04f);
                var bowl = Lit(k, node, "Bowl", ShapeKind.Circle, new Vector2(0f, -s * 0.1f), Vector2.one * s * 0.7f, liquid);
                Stage.AddStroke(bowl.Shape, glass, s * 0.06f, 1f);
                Glint(k, node, new Vector2(-s * 0.14f, 0f), new Vector2(s * 0.12f, s * 0.2f), 0f);
                return node;
            }

            public static RectTransform Helm(Builder k, Transform parent, Vector2 at, float s, Color steel, Color plume)
            {
                var node = k.Node(parent, "Helm", at);
                Lit(k, node, "Plume", ShapeKind.Ellipse, new Vector2(s * 0.06f, s * 0.42f), new Vector2(s * 0.5f, s * 0.2f), plume, 0f, -12f);
                var dome = Lit(k, node, "Dome", ShapeKind.RoundedRect, new Vector2(0f, -s * 0.02f), new Vector2(s * 0.7f, s * 0.78f), steel, s * 0.3f);
                dome.Outline.UniformRadius = false;
                dome.Outline.CornerRadii = new Vector4(s * 0.35f, s * 0.35f, s * 0.1f, s * 0.1f);
                Flat(k, node, "Visor", ShapeKind.Capsule, new Vector2(0f, s * 0.02f), new Vector2(s * 0.5f, s * 0.08f), Color.Lerp(steel, Color.black, 0.75f));
                Flat(k, node, "Ridge", ShapeKind.RoundedRect, new Vector2(0f, -s * 0.2f), new Vector2(s * 0.06f, s * 0.3f), Color.Lerp(steel, Color.black, 0.4f));
                Glint(k, node, new Vector2(-s * 0.16f, s * 0.2f), new Vector2(s * 0.1f, s * 0.2f), 0f);
                return node;
            }

            public static RectTransform Amulet(Builder k, Transform parent, Vector2 at, float s, Color chain, Color stone)
            {
                var node = k.Node(parent, "Amulet", at);
                var loop = Flat(k, node, "Chain", ShapeKind.Arc, new Vector2(0f, s * 0.16f), Vector2.one * s * 0.74f, chain);
                loop.Outline.Start = 200f;
                loop.Outline.End = 340f;
                loop.Outline.Thickness = 0.1f;
                loop.Rect.localRotation = Quaternion.Euler(0f, 0f, 180f);
                var setting = Lit(k, node, "Setting", ShapeKind.Circle, new Vector2(0f, -s * 0.16f), Vector2.one * s * 0.46f, chain);
                Lit(k, node, "Stone", ShapeKind.Rhombus, new Vector2(0f, -s * 0.16f), new Vector2(s * 0.26f, s * 0.34f), stone);
                Glint(k, setting.Rect, new Vector2(-s * 0.06f, s * 0.06f), new Vector2(s * 0.06f, s * 0.1f), 0f);
                return node;
            }

            public static RectTransform Chestplate(Builder k, Transform parent, Vector2 at, float s, Color steel, Color trim)
            {
                var node = k.Node(parent, "Chestplate", at);
                Outline(k, node, "Plate", CuirassOutline, s, Vector2.zero, steel, false);
                Flat(k, node, "Keel", ShapeKind.RoundedRect, new Vector2(0f, -s * 0.06f), new Vector2(s * 0.05f, s * 0.6f), Color.Lerp(steel, Color.black, 0.35f));
                Lit(k, node, "Collar", ShapeKind.Capsule, new Vector2(0f, s * 0.3f), new Vector2(s * 0.34f, s * 0.08f), trim);
                Lit(k, node, "Belt", ShapeKind.RoundedRect, new Vector2(0f, -s * 0.3f), new Vector2(s * 0.5f, s * 0.08f), trim, s * 0.02f);
                return node;
            }

            public static RectTransform Boots(Builder k, Transform parent, Vector2 at, float s, Color leather, Color trim)
            {
                var node = k.Node(parent, "Boots", at);
                Outline(k, node, "Back", BootOutline, s * 0.9f, new Vector2(s * 0.14f, s * 0.04f), Color.Lerp(leather, Color.black, 0.25f));
                Outline(k, node, "Front", BootOutline, s * 0.9f, new Vector2(-s * 0.08f, -s * 0.04f), leather);
                Lit(k, node, "Cuff", ShapeKind.RoundedRect, new Vector2(-s * 0.13f, s * 0.31f), new Vector2(s * 0.42f, s * 0.1f), trim, s * 0.02f);
                return node;
            }

            /// <summary>A medal for a top place: a disc in the place's metal with its number on it.</summary>
            public static void Medal(Builder k, Transform parent, Vector2 at, float s, int place)
            {
                var metal = place == 1 ? k.L.Gold : place == 2 ? k.L.Silver : k.L.Bronze;
                var disc = k.Ramp(parent, "Medal", ShapeKind.Circle, at, Vector2.one * s, Color.Lerp(metal, Color.white, 0.35f), Color.Lerp(metal, Color.black, 0.25f), 0f);
                Stage.AddStroke(disc.Shape, Color.Lerp(metal, Color.black, 0.4f), Mathf.Max(1.5f, s * 0.07f), -1f);
                if (k.L.Depth == Depth.Pixel) Stage.AddShadow(disc.Shape, k.L.Outline, new Vector2(3f, -3f), 0f);
                k.Figure(disc.Rect, place.ToString(), s * 0.52f, Color.Lerp(metal, Color.black, 0.6f), new Vector2(0f, 1f), s, LightSide.HorizontalAlignment.Center);
            }

            private static readonly Vector2[] ShieldOutline =
            {
                new(-8f, 9f), new(0f, 10f), new(8f, 9f), new(8f, 0f), new(5f, -6f), new(0f, -10f), new(-5f, -6f), new(-8f, 0f)
            };

            private static readonly Vector2[] CrownOutline =
            {
                new(-10f, -7f), new(-10f, 6f), new(-5f, 1f), new(0f, 8f), new(5f, 1f), new(10f, 6f), new(10f, -7f)
            };

            private static readonly Vector2[] BoltOutline =
            {
                new(2f, 10f), new(-6f, -1f), new(-0.5f, -1f), new(-3f, -10f), new(6f, 2f), new(0.5f, 2f)
            };

            private static readonly Vector2[] CubeTop = { new(0f, 10f), new(9f, 5f), new(0f, 0f), new(-9f, 5f) };
            private static readonly Vector2[] CubeLeft = { new(-9f, 5f), new(0f, 0f), new(0f, -10f), new(-9f, -5f) };
            private static readonly Vector2[] CubeRight = { new(9f, 5f), new(9f, -5f), new(0f, -10f), new(0f, 0f) };

            private static readonly Vector2[] ShipOutline =
            {
                new(0f, 10f), new(1.6f, 4f), new(2.4f, 0.5f), new(9.5f, -4.5f), new(9.5f, -6.5f), new(3f, -5f), new(2.4f, -8.5f), new(-2.4f, -8.5f),
                new(-3f, -5f), new(-9.5f, -6.5f), new(-9.5f, -4.5f), new(-2.4f, 0.5f), new(-1.6f, 4f)
            };

            private static readonly Vector2[] FlameOutline =
            {
                new(0f, 10f), new(4.5f, 2f), new(5f, -4f), new(2f, -9f), new(-2f, -9f), new(-5f, -4f), new(-4.5f, 2f)
            };

            private static readonly Vector2[] BladeOutline =
            {
                new(0f, 10f), new(1.2f, 7.5f), new(1.2f, -3.8f), new(-1.2f, -3.8f), new(-1.2f, 7.5f)
            };

            private static readonly Vector2[] CuirassOutline =
            {
                new(-9f, 7f), new(-4f, 8.5f), new(0f, 5.5f), new(4f, 8.5f), new(9f, 7f), new(7.5f, 1.5f), new(5f, -1f), new(5.5f, -8f),
                new(-5.5f, -8f), new(-5f, -1f), new(-7.5f, 1.5f)
            };

            private static readonly Vector2[] BootOutline =
            {
                new(-5f, 8f), new(1f, 8f), new(1f, -2f), new(6.5f, -4f), new(7.5f, -8f), new(-5f, -8f)
            };
        }
    }
}
