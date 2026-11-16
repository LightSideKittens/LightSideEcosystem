using System.Collections.Generic;
using System.IO;
using UnityEngine;
using HAlign = LightSide.HorizontalAlignment;
using Ramp = LightSide.Gradient;
using Stop = LightSide.GradientStop;
using Interp = LightSide.GradientInterpolation;

namespace LightSide.Promo
{
    /// <summary>The seven parts of <see cref="GuiShapesScene"/>, each built and posed on its own.</summary>
    public sealed partial class GuiShapesScene
    {
        /// <summary>Any shape: a switch of outline icons under a panel, the panel morphing into each one clicked.</summary>
        private Part BuildShapes(Stage stage)
        {
            var at = new Vector2(0f, contentCenter + contentHeight * 0.1f);
            var side = contentHeight * 0.44f;
            var cell = new Vector2(112f, 80f);
            var switchAt = new Vector2(0f, at.y - side * 0.5f - contentHeight * 0.17f);
            var start = new Vector2(cell.x * 3.4f, switchAt.y - PointerDrop);
            var part = new ShapesPart(stage, start, cell, switchAt);
            part.Build(this, stage, at, side);
            return part;
        }

        private sealed class ShapesPart : DrawnPart
        {
            private static readonly ShapeKind[] Kinds =
            {
                ShapeKind.RoundedRect, ShapeKind.Circle, ShapeKind.Star, ShapeKind.Heart, ShapeKind.Hexagon
            };

            private readonly float[] clicks = new float[Kinds.Length - 1];
            private readonly float[] weights = new float[Kinds.Length];
            private readonly Vector2 cell;
            private readonly Vector2 switchAt;
            private CompositeElement[] morphs;
            private Switch options;
            private UniShape[] lit;
            private RectTransform[][] icons;

            public ShapesPart(Stage stage, Vector2 start, Vector2 cell, Vector2 switchAt)
                : base(stage, "Shapes", start, Script(cell, switchAt), SwitchPace)
            {
                this.cell = cell;
                this.switchAt = switchAt;
            }

            private static Beat[] Script(Vector2 cell, Vector2 at)
            {
                var beats = new List<Beat>();
                for (var i = 1; i < Kinds.Length; i++)
                {
                    beats.Add(Beat.To(at + new Vector2((i - (Kinds.Length - 1) * 0.5f) * cell.x, 0f), null, cell.x * 0.6f));
                    beats.Add(Beat.Click("pick" + i, true));
                }

                beats.Add(Beat.Wait(0.2f));
                return beats.ToArray();
            }

            public void Build(GuiShapesScene scene, Stage stage, Vector2 at, float side)
            {
                var theme = scene.theme;
                var panel = scene.Hero(stage, Root, "Panel", at, Vector2.one * side, side * 0.16f);

                var morph = new CompositeShapeProvider();
                morph.Elements.Clear();
                morph.Elements.Add(new CompositeElement { Shape = Rounded(side * 0.16f, 0.6f) });
                var outlines = new IShapeProvider[]
                {
                    new PrimitiveShapeProvider { Kind = ShapeKind.Circle },
                    new PrimitiveShapeProvider { Kind = ShapeKind.Star, StarPoints = 5, StarSharpness = 0.5f, Rounding = side * 0.03f },
                    new PrimitiveShapeProvider { Kind = ShapeKind.Heart },
                    new PrimitiveShapeProvider { Kind = ShapeKind.Hexagon, Rounding = side * 0.05f }
                };
                morphs = new CompositeElement[outlines.Length];
                for (var i = 0; i < outlines.Length; i++)
                {
                    morphs[i] = new CompositeElement { Shape = outlines[i], Operation = CompositeOp.Morph };
                    morph.Elements.Add(morphs[i]);
                }

                panel.Shape.Shape = morph;

                options = new Switch(stage, Root, theme, switchAt, cell, Kinds.Length);
                lit = new UniShape[Kinds.Length];
                icons = new RectTransform[Kinds.Length][];
                for (var i = 0; i < Kinds.Length; i++)
                {
                    var icon = Icon(stage, Kinds[i], options[i]);
                    Stage.Solid(icon.Fill, theme.TextSoft);

                    var on = Icon(stage, Kinds[i], options[i]);
                    Stage.Ramped(on.Fill, scene.brand, PaintProjectionKind.Linear, 35f);
                    lit[i] = on.Shape;
                    icons[i] = new[] { icon.Rect, on.Rect };
                }
            }

            /// <summary>
            /// An outline icon on a switch cell, the star cut fatter than the primitive's default so its visual weight
            /// matches the solid icons beside it.
            /// </summary>
            private Widget Icon(Stage stage, ShapeKind kind, Vector2 at)
            {
                var icon = stage.Shape("Icon", options.Rect, kind, IconSize * 0.22f);
                if (kind == ShapeKind.Star) ((PrimitiveShapeProvider)icon.Shape.Shape).StarSharpness = IconStarSharpness;
                stage.Box(icon.Rect, at, Vector2.one * IconSide(kind));
                return icon;
            }

            private static float IconSide(ShapeKind kind) => kind == ShapeKind.Star ? IconSize * 1.15f : IconSize;

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                for (var i = 0; i < clicks.Length; i++) clicks[i] = ClickAt("pick" + (i + 1));
                options.Pose(played, clicks, weights);
                for (var i = 0; i < lit.Length; i++)
                {
                    Stage.Alpha(lit[i], weights[i]);
                    var side = IconSide(Kinds[i]) * (i == 0 ? 1f : Kick(played, clicks[i - 1]));
                    foreach (var icon in icons[i]) icon.sizeDelta = Vector2.one * side;
                }
                for (var i = 0; i < morphs.Length; i++)
                    morphs[i].Progress = Ease.Emphasized.Window(played, clicks[i], MorphFor);
            }
        }

        /// <summary>
        /// The panel the corner, outline and shadow beats all edit: one shape that stays on screen through the three
        /// and keeps every edit, as one panel would in a designer's hands.
        /// </summary>
        /// <remarks>
        /// It starts with every layer the three beats edit already in place at its opening values — a stroke of no
        /// width, a tight shadow — so each take finds the layer it edits and none adds structure mid-scene.
        /// </remarks>
        private void BuildLayered(Stage stage)
        {
            layered = stage.Shape("Layered Panel", stage.Root, ShapeKind.RoundedRect, 12f);
            stage.Box(layered.Rect, Vector2.zero, SubjectSize);
            layeredOutline = (PrimitiveShapeProvider)layered.Shape.Shape;
            layeredOutline.Smoothing = 0f;
            Stage.Ramped(layered.Fill, brand, PaintProjectionKind.Linear, 35f);
            layeredShadow = Stage.AddShadow(layered.Shape, Theme.Fade(theme.Magenta, 0.85f), new Vector2(0f, -8f), 10f);
            layeredStroke = Stage.AddStroke(layered.Shape, Color.white, 0f, -1f);
            layeredStroke.Dash = 22f;
            layeredStroke.Gap = 0f;
        }

        /// <summary>Any corner: the real Inspector scrubs Radius and slides Smoothing, the radius written at the corner.</summary>
        private Part BuildCorners(Stage stage)
        {
            var size = SubjectSize;
            var root = stage.Stretch(stage.Node("Corners", stage.Root));
            var outline = layeredOutline;

            var take = Shot("Corners", layered.Shape, new[]
                {
                    TakeBeat.To("Radius", TakePart.Value, 0.25f),
                    TakeBeat.Scrub("Radius", 0.95f, 90f, "radius"),
                    TakeBeat.To("Smoothing", TakePart.Slider, 0f),
                    TakeBeat.Drag("Smoothing", 1.02f, "smoothing"),
                    TakeBeat.Wait(0.35f)
                },
                new[]
                {
                    new TakeChannel("radius", () => outline.Radius, value => outline.Radius = value, 12f),
                    new TakeChannel("smoothing", () => outline.Smoothing, value => outline.Smoothing = value, 0f)
                },
                new TakeCrop("Color", 38f, "Layers", 4f), size, ComponentWindowWidth);

            var note = stage.Label(root, "R 12", NoteSize, theme.Orange, HAlign.Left, stretch: false, face: theme.DisplayFace);
            note.WordWrap = false;
            stage.Box(note.rectTransform, take.SubjectAt + new Vector2(size.x * 0.5f + 150f, size.y * 0.5f + 28f),
                new Vector2(300f, NoteSize * 1.4f));
            take.Build(stage, root, CardRadius, RimWidth);

            var shown = -1;
            return Adopt(stage, root, take, played =>
            {
                var radius = Mathf.RoundToInt(outline.Radius);
                if (radius == shown) return;
                note.Text = "R " + radius;
                shown = radius;
            });
        }

        /// <summary>Any outline: the real Inspector widens the Stroke layer, then opens its dashes.</summary>
        private Part BuildOutline(Stage stage)
        {
            var size = SubjectSize;
            var root = stage.Stretch(stage.Node("Outline", stage.Root));
            var stroke = layeredStroke;

            var take = Shot("Outline", layered.Shape, new[]
                {
                    TakeBeat.To("Width", TakePart.Value, 0.25f),
                    TakeBeat.Scrub("Width", 0.8f, 10f, "width"),
                    TakeBeat.To("Gap", TakePart.Value, 0.25f),
                    TakeBeat.Scrub("Gap", 0.9f, 16f, "gap"),
                    TakeBeat.Wait(0.35f)
                },
                new[]
                {
                    new TakeChannel("width", () => stroke.Width, value => stroke.Width = value, 0f),
                    new TakeChannel("gap", () => stroke.Gap, value => stroke.Gap = value, 0f)
                },
                new TakeCrop("Stroke", 8f, "Gap", 4f, new[] { "Layers", "Stroke" }, LayerLeft, LayerRight), size,
                LayerWindowWidth);

            take.Build(stage, root, CardRadius, RimWidth);
            return Adopt(stage, root, take, null);
        }

        /// <summary>Any shadow: the real Inspector softens a warm Shadow layer, then drops it.</summary>
        private Part BuildShadow(Stage stage)
        {
            var size = SubjectSize;
            var root = stage.Stretch(stage.Node("Shadow", stage.Root));
            var shadow = layeredShadow;

            var take = Shot("Shadow", layered.Shape, new[]
                {
                    TakeBeat.To("Blur", TakePart.Value, 0.25f),
                    TakeBeat.Scrub("Blur", 0.85f, 56f, "blur"),
                    TakeBeat.To("Y", TakePart.Value, 0.9f),
                    TakeBeat.Scrub("Y", 0.15f, -34f, "drop"),
                    TakeBeat.Wait(0.35f)
                },
                new[]
                {
                    new TakeChannel("blur", () => shadow.Blur, value => shadow.Blur = value, 10f),
                    new TakeChannel("y", () => shadow.Offset.y, value => shadow.Offset = new Vector2(shadow.Offset.x, value), -8f)
                },
                new TakeCrop("Shadow", 8f, "Blur", 4f, new[] { "Layers", "Shadow" }, LayerLeft, LayerRight), size,
                LayerWindowWidth);

            take.Build(stage, root, CardRadius, RimWidth);
            return Adopt(stage, root, take, null);
        }

        /// <summary>
        /// A take of an Inspector <paramref name="windowWidth"/> points wide on <paramref name="subject"/>, laid out
        /// beside a subject of <paramref name="size"/>.
        /// </summary>
        private TakeShot Shot(string folder, UniShape subject, TakeBeat[] script, TakeChannel[] channels, TakeCrop crop,
            Vector2 size, float windowWidth)
        {
            var window = new Rect(TakeWindowX, 0f, windowWidth, TakeWindowHeight);
            var take = new TakeShot(Path.Combine(takesFolder, folder), subject, script, channels, crop, window);
            take.Layout(size, TakeGap, contentCenter, contentHeight * CardHeightShare, frameWidth * PairWidthShare);
            takes.Add(take);
            return take;
        }

        /// <summary>Wraps content already built under <paramref name="root"/> into a part that plays <paramref name="take"/>.</summary>
        private static Part Adopt(Stage stage, RectTransform root, TakeShot take, System.Action<float> after)
        {
            var part = new RecordedPart(stage, root.name + " Part", take, after);
            root.SetParent(part.Root, false);
            return part;
        }

        /// <summary>Any gradient: a profile card repainted from a row of swatches.</summary>
        private Part BuildGradient(Stage stage)
        {
            var size = new Vector2(560f, 340f);
            var at = new Vector2(0f, contentCenter + contentHeight * 0.08f);
            var swatchAt = new Vector2(0f, at.y - size.y * 0.5f - contentHeight * 0.14f);
            var start = new Vector2(SwatchStep * 2.6f, swatchAt.y - PointerDrop);
            var part = new GradientPart(stage, start, swatchAt);
            part.Build(this, stage, at, size);
            return part;
        }

        private sealed class GradientPart : DrawnPart
        {
            private readonly float[] clicks = new float[3];
            private readonly Vector2 swatchAt;
            private Ramp[] ramps;
            private ShapePaint[] painted;
            private RectTransform ring;
            private RectTransform[] swatches;

            public GradientPart(Stage stage, Vector2 start, Vector2 swatchAt)
                : base(stage, "Gradient", start, new[]
                {
                    Beat.To(swatchAt + new Vector2(SwatchStep, 0f), null, SwatchSize), Beat.Click("paint1", true),
                    Beat.To(swatchAt + new Vector2(-SwatchStep, 0f), null, SwatchSize), Beat.Click("paint2", true),
                    Beat.To(swatchAt, null, SwatchSize), Beat.Click("paint3", true),
                    Beat.Wait(0.2f)
                })
            {
                this.swatchAt = swatchAt;
            }

            /// <summary>The swatch each click picks, in order; the card starts on <see cref="First"/>.</summary>
            private static readonly int[] Picks = { 2, 0, 1 };

            private const int First = 1;

            public void Build(GuiShapesScene scene, Stage stage, Vector2 at, Vector2 size)
            {
                var theme = scene.theme;
                ramps = new[] { Tri(0x7B5CFF, 0xC56BFF, 0xFF6BB5), scene.brand, Tri(0x00C9A7, 0x2BB5FF, 0x6D6BFF) };

                var card = scene.Panel(stage, Root, "Card", at, size, 30f);
                Stage.AddStroke(card.Shape, Theme.Fade(theme.Line, 0.8f), 1.5f, -1f);

                var top = at.y + size.y * 0.5f;
                var left = at.x - size.x * 0.5f;
                var banner = stage.Shape("Banner", Root, ShapeKind.RoundedRect, 20f);
                stage.Box(banner.Rect, new Vector2(at.x, top - 14f - BannerHeight * 0.5f), new Vector2(size.x - 28f, BannerHeight));

                var avatarAt = new Vector2(left + 44f + AvatarSize * 0.5f, top - 14f - BannerHeight);
                var avatar = stage.Shape("Avatar", Root, ShapeKind.Circle);
                stage.Box(avatar.Rect, avatarAt, Vector2.one * AvatarSize);
                Stage.AddStroke(avatar.Shape, theme.Surface, 7f, 1f);
                var initials = stage.Label(Root, "MC", 36f, Color.white, face: theme.DisplayFace, stretch: false);
                stage.Box(initials.rectTransform, avatarAt, Vector2.one * AvatarSize);

                var textLeft = avatarAt.x + AvatarSize * 0.5f + 22f;
                var name = stage.Label(Root, "Mia Chen", 36f, theme.Text, HAlign.Left, stretch: false, face: theme.DisplayFace);
                stage.Box(name.rectTransform, new Vector2(textLeft + 160f, avatarAt.y - 34f), new Vector2(320f, 50f));
                var role = stage.Label(Root, "Product designer", 24f, theme.TextSoft, HAlign.Left, stretch: false);
                stage.Box(role.rectTransform, new Vector2(textLeft + 160f, avatarAt.y - 74f), new Vector2(320f, 36f));

                var button = stage.Shape("Follow", Root, ShapeKind.Capsule);
                var buttonAt = new Vector2(at.x + size.x * 0.5f - 34f - 80f, at.y - size.y * 0.5f + 34f + 28f);
                stage.Box(button.Rect, buttonAt, new Vector2(160f, 56f));
                var follow = stage.Label(Root, "Follow", 24f, Color.white, face: theme.DisplayFace, stretch: false);
                stage.Box(follow.rectTransform, buttonAt, new Vector2(160f, 56f));

                painted = new[] { banner.Fill, avatar.Fill, button.Fill };
                foreach (var paint in painted) Stage.Ramped(paint, ramps[First], PaintProjectionKind.Linear, 20f);

                var row = stage.Stretch(stage.Node("Swatches", Root));
                swatches = new RectTransform[ramps.Length];
                for (var i = 0; i < ramps.Length; i++)
                {
                    var swatch = stage.Shape("Swatch", row, ShapeKind.Circle);
                    stage.Box(swatch.Rect, Swatch(i), Vector2.one * SwatchSize);
                    Stage.Ramped(swatch.Fill, ramps[i], PaintProjectionKind.Linear, 35f);
                    swatches[i] = swatch.Rect;
                }

                var picked = stage.Shape("Picked", row, ShapeKind.Circle);
                ring = picked.Rect;
                stage.Box(ring, Swatch(First), Vector2.one * (SwatchSize + 16f));
                Stage.Solid(picked.Fill, new Color(1f, 1f, 1f, 0f));
                Stage.AddStroke(picked.Shape, theme.Text, 3f, -1f);
            }

            private Vector2 Swatch(int index) => swatchAt + new Vector2((index - 1) * SwatchStep, 0f);

            private static Ramp Tri(int a, int b, int c) => new Ramp(new[]
            {
                new Stop(0f, Hex(a)), new Stop(0.5f, Hex(b)), new Stop(1f, Hex(c))
            }, Interp.Perceptual);

            private static Color Hex(int rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 0xFF);

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                var active = First;
                var from = Swatch(First);
                var x = from.x;
                for (var i = 0; i < Picks.Length; i++)
                {
                    clicks[i] = ClickAt("paint" + (i + 1));
                    if (played >= clicks[i]) active = Picks[i];
                    var previous = i == 0 ? First : Picks[i - 1];
                    x += (Swatch(Picks[i]).x - Swatch(previous).x) * Motion.Snap.Window(played, clicks[i], SwitchSlide);
                }

                ring.anchoredPosition = new Vector2(x, from.y);
                foreach (var paint in painted) paint.Gradient = ramps[active];
                for (var i = 0; i < swatches.Length; i++)
                {
                    var kick = 1f;
                    for (var c = 0; c < Picks.Length; c++)
                        if (Picks[c] == i) kick = Mathf.Max(kick, Kick(played, clicks[c]));
                    swatches[i].sizeDelta = Vector2.one * (SwatchSize * kick);
                }
            }
        }

        /// <summary>
        /// Any style: a game button assembled from its layers, one layer to a beat while its row joins the layer list
        /// beside it — silhouette, gradient, bevel, inner shadow, stroke, grain, depth, shadow — then pressed, the face
        /// sinking into its own depth.
        /// </summary>
        private sealed class StylePart : DrawnPart
        {
            public StylePart(GuiShapesScene scene, Stage stage) : base(stage, "Style")
            {
                var theme = scene.theme;
                buttonAt = new Vector2(-scene.frameWidth * 0.16f, scene.contentCenter + scene.contentHeight * 0.02f);
                button = stage.Shape("Play Button", Root, ShapeKind.RoundedRect, ButtonSize.y * 0.34f);
                stage.Box(button.Rect, buttonAt, ButtonSize);
                var shape = button.Shape;
                Stage.Solid(button.Fill, theme.Line);

                gradient = new FillLayer();
                Stage.Ramped(gradient.Paint, scene.hot, PaintProjectionKind.Linear, 90f);
                shape.Layers.Add(gradient);
                noise = new NoiseLayer { Frequency = 0.6f, Contrast = 2f };
                Stage.Solid(noise.Paint, new Color(0.62f, 0.36f, 0.46f, 0.45f));
                noise.Paint.Blend = LayerBlend.Multiply;
                shape.Layers.Add(noise);
                inner = Stage.AddInnerShadow(shape, new Color(0.22f, 0f, 0.08f, 0.85f), new Vector2(0f, 22f), InnerBlur);
                lit = Stage.AddBevel(shape, 110f, BevelWidth, 0f);
                shade = Stage.AddBevel(shape, 110f, BevelWidth, 0f, shadowSide: true);
                stroke = Stage.AddStroke(shape, new Color(1f, 0.86f, 0.6f, 1f), 7f, 0f);
                extrude = new ExtrudeLayer
                {
                    Offset = Vector2.zero, NearColor = new Color(0.55f, 0.08f, 0.22f, 1f),
                    FarColor = new Color(0.25f, 0.03f, 0.1f, 1f), Steps = 16
                };
                shape.Layers.Insert(0, extrude);
                shadow = Stage.AddShadow(shape, new Color(0f, 0f, 0f, 0.6f), Vector2.zero, 0f);

                var label = stage.Label(button.Rect, "PLAY", 104f, Color.white, face: theme.DisplayFace);
                label.WordWrap = false;

                var listAt = new Vector2(scene.frameWidth * 0.25f, buttonAt.y);
                var listSize = new Vector2(ListWidth, ListHead + Names.Length * RowStep + ListPad);
                scene.Panel(stage, Root, "Layer List", listAt, listSize, 22f);
                var top = listAt.y + listSize.y * 0.5f;
                var left = listAt.x - listSize.x * 0.5f;
                var title = stage.Label(Root, "Layers", 32f, theme.Text, HAlign.Left, stretch: false, face: theme.DisplayFace);
                stage.Box(title.rectTransform, new Vector2(listAt.x, top - ListHead * 0.5f - 4f), new Vector2(ListWidth - 64f, 44f));

                rows = new RectTransform[Names.Length];
                rowAt = new Vector2[Names.Length];
                glows = new UniShape[Names.Length];
                dots = new UniShape[Names.Length];
                names = new UniText[Names.Length];
                for (var i = 0; i < Names.Length; i++)
                {
                    var row = stage.Node("Row", Root);
                    stage.Box(row, new Vector2(listAt.x, top - ListHead - (i + 0.5f) * RowStep), new Vector2(ListWidth - 28f, RowStep - 6f));
                    var glow = stage.Shape("Glow", row, ShapeKind.RoundedRect, 12f);
                    stage.Stretch(glow.Rect);
                    Stage.Solid(glow.Fill, Theme.Fade(theme.Coral, 0.3f));
                    var dot = stage.Shape("Dot", row, i % 2 == 0 ? ShapeKind.Circle : ShapeKind.RoundedRect, 4f);
                    stage.Box(dot.Rect, new Vector2(-(ListWidth - 28f) * 0.5f + 30f, 0f), Vector2.one * 20f);
                    Stage.Solid(dot.Fill, Dots[i]);
                    var name = stage.Label(row, Names[i], 27f, theme.Text, HAlign.Left, stretch: false);
                    stage.Box(name.rectTransform, new Vector2(36f, 0f), new Vector2(ListWidth - 140f, RowStep - 6f));
                    rows[i] = row;
                    rowAt[i] = row.anchoredPosition;
                    glows[i] = glow.Shape;
                    dots[i] = dot.Shape;
                    names[i] = name;
                }

                var press = buttonAt + new Vector2(ButtonSize.x * 0.12f, -ButtonSize.y * 0.1f);
                Plan(new Vector2(scene.frameWidth * 0.34f, -scene.frameHeight * 0.5f - 90f), new[]
                {
                    Beat.Wait(Arrival(Names.Length - 1) + StyleSettle),
                    Beat.To(press, null, ButtonSize.x),
                    Beat.Click("press", true, true),
                    Beat.Wait(0.5f)
                });
            }

            private readonly Vector2 buttonAt;
            private readonly Widget button;
            private readonly FillLayer gradient;
            private readonly NoiseLayer noise;
            private readonly InnerShadowLayer inner;
            private readonly BevelLayer lit;
            private readonly BevelLayer shade;
            private readonly StrokeLayer stroke;
            private readonly ExtrudeLayer extrude;
            private readonly ShadowLayer shadow;
            private readonly RectTransform[] rows;
            private readonly Vector2[] rowAt;
            private readonly UniShape[] glows;
            private readonly UniShape[] dots;
            private readonly UniText[] names;

            /// <summary>When layer <paramref name="index"/> of <see cref="Names"/> lands, from the action's start.</summary>
            private static float Arrival(int index) => StyleLead + index * StyleStep;

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                button.Rect.gameObject.SetActive(played >= Arrival(0));
                var pulse = 1f;
                for (var i = 0; i < Names.Length; i++)
                {
                    var since = played - Arrival(i);
                    var on = Mathf.Clamp01(since / RowIn);
                    rows[i].anchoredPosition = rowAt[i] + new Vector2(RowSlide * (1f - Motion.Snap.Evaluate(on)), 0f);
                    rows[i].gameObject.SetActive(since >= 0f);
                    Stage.Alpha(dots[i], on);
                    var color = names[i].color;
                    color.a = on;
                    names[i].color = color;
                    Stage.Alpha(glows[i], 1f - Mathf.Clamp01(since / GlowFor));
                    if (since >= 0f) pulse += PulseAmount * Mathf.Sin(Mathf.PI * Mathf.Clamp01(since / PulseFor));
                }

                var grow = 1f - Motion.Back.Evaluate(Mathf.Clamp01((played - Arrival(0)) / RowIn));
                Stage.Scale(button.Rect, pulse * (1f - 0.35f * grow));

                gradient.Disabled = played < Arrival(1);
                var bevel = Ease.Emphasized.Window(played, Arrival(2), LayerGrow);
                lit.Disabled = shade.Disabled = played < Arrival(2);
                lit.Strength = 2f * bevel;
                shade.Strength = 1.2f * bevel;
                inner.Disabled = played < Arrival(3);
                inner.Blur = InnerBlur * Ease.Emphasized.Window(played, Arrival(3), LayerGrow);
                stroke.Disabled = played < Arrival(4);
                stroke.TrimEnd = Ease.Emphasized.Window(played, Arrival(4), StrokeDraw);
                noise.Disabled = played < Arrival(5);

                var down = played < ClickAt("press") ? 0f : Mathf.Clamp01((played - ClickAt("press")) / PressDown);
                var up = Ease.Emphasized.Window(played, Pointer.Timeline.Span("press").y, PressUp);
                var sink = (Depth - PressedDepth) * down * (1f - up);
                var depth = Depth * Ease.Emphasized.Window(played, Arrival(6), LayerGrow) - sink;
                extrude.Disabled = played < Arrival(6);
                extrude.Offset = new Vector2(0f, -depth);
                button.Rect.anchoredPosition = buttonAt - new Vector2(0f, sink);

                var cast = Ease.Emphasized.Window(played, Arrival(7), LayerGrow);
                shadow.Disabled = played < Arrival(7);
                shadow.Offset = new Vector2(0f, -(depth + 26f) * cast);
                shadow.Blur = (56f - 20f * down * (1f - up)) * cast;
            }

            /// <summary>The layers in the order they land, which is also the order the list shows them.</summary>
            private static readonly string[] Names =
            {
                "Shape", "Gradient", "Bevel", "Inner shadow", "Stroke", "Noise", "Extrude", "Shadow"
            };

            private static readonly Color[] Dots =
            {
                new(0.45f, 0.4f, 0.38f), new(1f, 0.42f, 0.42f), new(1f, 0.92f, 0.8f), new(0.55f, 0.1f, 0.28f),
                new(1f, 0.86f, 0.6f), new(0.8f, 0.78f, 0.75f), new(0.6f, 0.1f, 0.25f), new(0.1f, 0.06f, 0.06f)
            };

            private static readonly Vector2 ButtonSize = new(700f, 240f);
            private const float Depth = 30f;
            private const float PressedDepth = 6f;
            private const float PressDown = 0.07f;
            private const float PressUp = 0.24f;
            private const float BevelWidth = 16f;
            private const float InnerBlur = 34f;
            private const float StyleLead = 0.15f;
            private const float StyleStep = 0.42f;
            private const float StyleSettle = 0.45f;
            private const float LayerGrow = 0.3f;
            private const float StrokeDraw = 0.38f;
            private const float RowIn = 0.26f;
            private const float RowSlide = 60f;
            private const float GlowFor = 0.6f;
            private const float PulseAmount = 0.035f;
            private const float PulseFor = 0.22f;
            private const float ListWidth = 500f;
            private const float ListHead = 84f;
            private const float RowStep = 60f;
            private const float ListPad = 16f;
        }

        /// <summary>Any combination: a panel and a circle combined by whichever operation the switch names.</summary>
        private Part BuildBoolean(Stage stage)
        {
            var side = contentHeight * 0.4f;
            var at = new Vector2(0f, contentCenter + contentHeight * 0.1f);
            var cell = new Vector2(176f, 70f);
            var switchAt = new Vector2(0f, at.y - side * 0.5f - contentHeight * 0.2f);
            var start = new Vector2(cell.x * 2.6f, switchAt.y - PointerDrop);
            var part = new BooleanPart(stage, start, cell, switchAt);
            part.Build(this, stage, at, side);
            return part;
        }

        private sealed class BooleanPart : DrawnPart
        {
            private static readonly CompositeOp[] Ops =
            {
                CompositeOp.Union, CompositeOp.Subtract, CompositeOp.Intersect, CompositeOp.Exclude
            };

            private static readonly string[] Names = { "Union", "Subtract", "Intersect", "Exclude" };

            private readonly float[] clicks = new float[Ops.Length - 1];
            private readonly float[] weights = new float[Ops.Length];
            private readonly Vector2 cell;
            private readonly Vector2 switchAt;
            private Switch options;
            private UniText[] labels;
            private CompositeElement bite;
            private PrimitiveShapeProvider body;
            private RectTransform hero;
            private Vector2 heroAt;
            private Color soft;
            private Color lit;
            private float side;
            private float scale;

            public BooleanPart(Stage stage, Vector2 start, Vector2 cell, Vector2 switchAt)
                : base(stage, "Boolean", start, Script(cell, switchAt), SwitchPace)
            {
                this.cell = cell;
                this.switchAt = switchAt;
            }

            private static Beat[] Script(Vector2 cell, Vector2 at)
            {
                var beats = new List<Beat>();
                for (var i = 1; i < Ops.Length; i++)
                {
                    beats.Add(Beat.To(at + new Vector2((i - (Ops.Length - 1) * 0.5f) * cell.x, 0f), null, cell.x * 0.6f));
                    beats.Add(Beat.Click("op" + i, true));
                }

                beats.Add(Beat.Wait(0.3f));
                return beats.ToArray();
            }

            public void Build(GuiShapesScene scene, Stage stage, Vector2 at, float size)
            {
                var theme = scene.theme;
                side = size;
                scale = size;
                heroAt = at;
                soft = theme.TextSoft;
                lit = theme.Text;

                var shape = stage.Shape("Combined", Root, ShapeKind.RoundedRect);
                hero = shape.Rect;
                stage.Box(shape.Rect, at, Vector2.one * side);
                Stage.Ramped(shape.Fill, scene.brand, PaintProjectionKind.Linear, 35f);
                Stage.AddShadow(shape.Shape, Theme.Fade(theme.Coral, 0.35f), new Vector2(0f, -16f), 50f);

                bite = new CompositeElement
                {
                    Shape = new PrimitiveShapeProvider { Kind = ShapeKind.Circle },
                    Operation = CompositeOp.Union,
                    Rect = Placement(0f, 1f)
                };
                var combined = new CompositeShapeProvider();
                combined.Elements.Clear();
                body = Rounded(side * BodyRadius, 0.6f);
                combined.Elements.Add(new CompositeElement { Shape = body });
                combined.Elements.Add(bite);
                shape.Shape.Shape = combined;

                options = new Switch(stage, Root, theme, switchAt, cell, Ops.Length);
                labels = new UniText[Ops.Length];
                for (var i = 0; i < Ops.Length; i++)
                {
                    var label = stage.Label(options.Rect, Names[i], 26f, soft, stretch: false, face: theme.DisplayFace);
                    stage.Box(label.rectTransform, options[i], cell);
                    labels[i] = label;
                }
            }

            /// <summary>
            /// The circle's place <paramref name="seconds"/> into the beat: orbiting the panel's edge, so under every
            /// operation the result keeps changing shape while the viewer watches.
            /// </summary>
            private RectPlacement Placement(float seconds, float presence)
            {
                var angle = (OrbitStart + seconds * OrbitRate) * Mathf.Deg2Rad;
                return new RectPlacement
                {
                    anchorMax = Vector2.one,
                    pivot = new Vector2(0.5f, 0.5f),
                    sizeDelta = Vector2.one * (scale * BiteSize * presence - scale),
                    anchoredPosition = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (scale * OrbitRadius)
                };
            }

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                var step = 0;
                for (var i = 0; i < clicks.Length; i++)
                {
                    clicks[i] = ClickAt("op" + (i + 1));
                    if (played >= clicks[i]) step = i + 1;
                }

                options.Pose(played, clicks, weights);
                for (var i = 0; i < labels.Length; i++)
                {
                    labels[i].color = Color.LerpUnclamped(soft, lit, weights[i]);
                    labels[i].rectTransform.localScale = Vector3.one * (i == 0 ? 1f : Kick(played, clicks[i - 1]));
                }

                var dive = Mathf.Clamp01((played - (Out - ActionAt)) / Leaving);
                var zoom = Mathf.Pow(dive, DiveCurve);
                scale = side * Mathf.Pow(ZoomScale, zoom);
                hero.sizeDelta = Vector2.one * scale;
                hero.anchoredPosition = Vector2.LerpUnclamped(heroAt, Vector2.zero, Motion.Snap.Evaluate(dive));
                body.Radius = scale * BodyRadius;
                var drop = new Vector2(0f, -SwitchDrop * Motion.Swish.Window(played, Out - ActionAt, SwishFor));
                options.Rect.anchoredPosition = drop;
                Pointer.Offset += drop;

                var circle = 1f - Motion.Snap.Window(played, Out - ActionAt, BiteLeave);
                bite.Rect = Placement(Mathf.Max(0f, played), circle);
                bite.Operation = Ops[step];
                bite.Blend = scale * (Ops[step] == CompositeOp.Union ? 0.12f : 0.04f);
            }
        }

        /// <summary>Where the recorded Inspector floats while a take records, and how tall it is, in screen points.</summary>
        /// <remarks>No taller than the screen at the editor scale takes are recorded at.</remarks>
        private const float TakeWindowX = 100f;

        private const float TakeWindowHeight = 350f;

        /// <summary>
        /// The recorded Inspector's width for a take of the whole component and for a take of one layer: as narrow as the
        /// rows allow without truncating, since a value box's empty width only shrinks the card and the subject beside it.
        /// </summary>
        /// <remarks>The component's Shape row holds two pickers side by side and needs the wider window.</remarks>
        private const float ComponentWindowWidth = 480f;

        private const float LayerWindowWidth = 400f;

        /// <summary>The subject beside a take's card: large enough to balance the card it stands next to.</summary>
        private static readonly Vector2 SubjectSize = new Vector2(600f, 430f);


        private const float TakeGap = 120f;
        private const float CardHeightShare = 0.8f;
        private const float PairWidthShare = 0.88f;
        private const float CardRadius = 14f;
        private const float RimWidth = 1.5f;
        private const float NoteSize = 34f;
        private const float IconSize = 40f;
        private const float SwitchSlide = 0.24f;




        private const float MorphFor = 0.35f;
        private const float SwatchSize = 52f;
        private const float SwatchStep = 96f;
        private const float BannerHeight = 128f;
        private const float AvatarSize = 104f;
        private const float BiteSize = 0.64f;
        /// <summary>Where the circle's orbit starts, in degrees counter-clockwise from the right, and how fast it turns.</summary>
        private const float OrbitStart = 40f;

        private const float OrbitRate = 125f;

        /// <summary>The combined panel's corner radius as a fraction of its side.</summary>
        private const float BodyRadius = 0.2f;

        /// <summary>
        /// How many times its own size the combined panel grows to as the camera dives at it at the section's end, before
        /// the next slide's entrance carries the move on. The growth is geometric, which reads as a camera moving at a
        /// steady speed, and starts slowly, <see cref="DiveCurve"/> setting how slowly.
        /// </summary>
        private const float ZoomScale = 2.5f;

        private const float DiveCurve = 1.6f;

        /// <summary>How fast the cutting circle shrinks away as the dive begins, so the camera dives into solid paint.</summary>
        private const float BiteLeave = 0.22f;

        /// <summary>How far the switch falls away under the panel the camera dives into.</summary>
        private const float SwitchDrop = 700f;

        /// <summary>The orbit's radius as a fraction of the panel's side: the circle's center rides the panel's edge.</summary>
        private const float OrbitRadius = 0.5f;

        private const float IconStarSharpness = 0.38f;

        /// <summary>Points trimmed off a layer card's left and right, where the Inspector's section rail and gutter sit.</summary>
        private const float LayerLeft = 40f;

        private const float LayerRight = 16f;
    }
}
