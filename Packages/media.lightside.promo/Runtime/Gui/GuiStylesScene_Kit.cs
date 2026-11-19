using System;
using UnityEngine;
using HAlign = LightSide.HorizontalAlignment;
using VAlign = LightSide.VerticalAlignment;

namespace LightSide.Promo
{
    /// <summary>The design tokens of <see cref="GuiStylesScene"/>'s looks and the parts every game is built from.</summary>
    public sealed partial class GuiStylesScene
    {
        /// <summary>How a look gives its surfaces depth.</summary>
        internal enum Depth
        {
            /// <summary>Lit from above, every part standing on a lip of its own darker color.</summary>
            Lip,

            /// <summary>
            /// Polished candy plastic: every raised part lit from above with a shine across its top, edged and lipped in a
            /// darker shade of its own color; light trays on a light rim with soft shadows; wells sunk in a darker shade.
            /// </summary>
            Gloss,

            /// <summary>Dark glass edged in light that glows.</summary>
            Glow,

            /// <summary>Hairlines, brackets and an inner light: a head-up display.</summary>
            Hud,

            /// <summary>Flat fills, black outlines and hard shadows a pixel grid would draw.</summary>
            Pixel,

            /// <summary>Parchment and velvet in bronze and gold trim.</summary>
            Ornate
        }

        /// <summary>How a colored part stands on what holds it.</summary>
        internal enum Relief
        {
            /// <summary>Flush in the well it fills: a bar's or a switch's fill.</summary>
            Flush,

            /// <summary>Raised on its lip: a button, a band, a tag, a chip.</summary>
            Raised
        }

        /// <summary>A two-tone color: the lit face of a part and the darker one beneath it.</summary>
        internal readonly struct Tone
        {
            public Tone(Color light, Color dark, Color lip, Color ink = default)
            {
                Light = light;
                Dark = dark;
                Lip = lip;
                Ink = ink;
            }

            public Color Light { get; }
            public Color Dark { get; }
            public Color Lip { get; }

            /// <summary>The caption color a light tone needs to read; clear for a tone the look's own caption treatment reads on.</summary>
            public Color Ink { get; }
        }

        /// <summary>One art direction: its colors, its type, its shape language and how it gives depth.</summary>
        internal sealed class Look
        {
            public string Name;
            public UniTextFont Display;
            public UniTextFont Body;
            public float DisplayWeight;
            public float BodyWeight;
            public bool Caps;
            public float Tracking;
            public Depth Depth;
            public float Radius;
            public float Smoothing = 0.6f;
            public CornerStyle Corners = CornerStyle.Round;

            public Color ScreenTop;
            public Color ScreenBottom;
            public Color Panel;
            public Color PanelHigh;
            public Color Inset;
            public Color Line;
            public Color Outline;
            public Color Text;
            public Color TextDim;
            public Color TextOnTone;
            public Tone Primary;
            public Tone Secondary;
            public Tone Accent;
            public Tone Danger;
            public Color Gold;
            public Color Silver;
            public Color Bronze;
            public Color Coin;
            public Color CoinDark;
            public Color Gem;
            public Color GemDark;

            /// <summary>The look's item rarities in rising order: common, rare, epic, legendary.</summary>
            public Tone[] Rarity;

            /// <summary>
            /// The look's own chrome on its screen behind every panel — glows, grids, motes, rays — and how it lives: the
            /// returned pose moves it at a time in the beat.
            /// </summary>
            public Func<Stage, RectTransform, Action<float>> Ambience;

            /// <summary>The look's extra trim on a panel of the given size: brackets, ornaments, scanlines.</summary>
            public Action<Stage, RectTransform, Vector2> Trim;

            /// <summary>The look's treatment of a heading: strokes, shadows, glows, ramps.</summary>
            public Action<UniText> Heading;

            /// <summary>The look's treatment of a caption on a colored part of the given tone.</summary>
            public Action<UniText, Tone> OnTone;

            /// <summary>How large the look sets its display type against the screens' sizes: below 1 for a wide face.</summary>
            public float Type = 1f;

            /// <summary>How large the look sets its body type, for a body face wider or narrower than the display's.</summary>
            public float BodyType = 1f;

            public string Cased(string text) => Caps ? text.ToUpperInvariant() : text;
        }

        /// <summary>
        /// The parts of one game's screen, in its look: the painters that give a surface the look's depth, and every
        /// control and piece of chrome the games share, each handing back what its moves pose.
        /// </summary>
        /// <remarks>
        /// A part's size is the room a layout gives it: its face shrinks until everything it draws with a hard edge — its
        /// lip, a hard shadow, an outline outside it — fits that box, and the rect a part hands back is its face. Soft
        /// shadows and glows fall outside the box.
        /// </remarks>
        internal sealed class Builder
        {
            public Builder(Stage stage, Look look)
            {
                Stage = stage;
                L = look;
            }

            public Stage Stage { get; }
            public Look L { get; }

            /// <summary>How the look's chrome on the screen lives, posed at a time in the beat; null for a still screen.</summary>
            public Action<float> Live { get; private set; }

            public RectTransform Node(Transform parent, string name, Vector2 at)
            {
                var node = Stage.Node(name, parent);
                Stage.Box(node, at, Vector2.zero);
                return node;
            }

            /// <summary>A node whose whole subtree fades as one, for parts that come and go.</summary>
            public (RectTransform node, CanvasGroup group) Group(Transform parent, string name, Vector2 at)
            {
                var node = Node(parent, name, at);
                return (node, node.gameObject.AddComponent<CanvasGroup>());
            }

            public Widget Shape(Transform parent, string name, ShapeKind kind, Vector2 at, Vector2 size, float radius = -1f)
            {
                var shape = Stage.Shape(name, parent, kind, radius < 0f ? L.Radius : radius);
                Stage.Box(shape.Rect, at, size);
                var outline = shape.Outline;
                outline.Smoothing = L.Smoothing;
                outline.CornerStyle = kind == ShapeKind.RoundedRect ? L.Corners : CornerStyle.Round;
                return shape;
            }

            public Widget Fill(Transform parent, string name, ShapeKind kind, Vector2 at, Vector2 size, Color color, float radius = -1f)
            {
                var shape = Shape(parent, name, kind, at, size, radius);
                Stage.Solid(shape.Fill, color);
                return shape;
            }

            public Widget Ramp(Transform parent, string name, ShapeKind kind, Vector2 at, Vector2 size, Color top, Color bottom,
                float radius = -1f)
            {
                var shape = Shape(parent, name, kind, at, size, radius);
                Stage.Linear(shape.Fill, top, bottom, 270f);
                return shape;
            }

            /// <summary>A soft round glow of <paramref name="color"/> fading out to its edge.</summary>
            public Widget Halo(Transform parent, Vector2 at, float size, Color color)
            {
                var halo = Shape(parent, "Glow", ShapeKind.Circle, at, Vector2.one * size, 0f);
                Stage.Radial(halo.Fill, color, Theme.Fade(color, 0f));
                return halo;
            }

            /// <summary>
            /// The device the game runs on: a dark bezel round a display of <see cref="ScreenSize"/> painted the look's
            /// screen colors, which clips everything built on it; returns the display.
            /// </summary>
            public RectTransform Screen(RectTransform device)
            {
                var bezel = Fill(device, "Bezel", ShapeKind.RoundedRect, Vector2.zero, ScreenSize + Vector2.one * BezelWidth * 2f, BezelColor,
                    ScreenRadius + BezelWidth);
                bezel.Outline.CornerStyle = CornerStyle.Round;
                bezel.Outline.Smoothing = 0.6f;
                Stage.AddStroke(bezel.Shape, BezelEdge, 2f, -1f);
                Stage.AddShadow(bezel.Shape, Theme.Fade(Color.black, 0.55f), new Vector2(0f, -22f), 46f);
                Fill(device, "Camera", ShapeKind.Circle, new Vector2(-ScreenSize.x * 0.5f - BezelWidth * 0.5f, 0f), Vector2.one * 9f, BezelLens, 0f);
                var display = Ramp(device, "Display", ShapeKind.RoundedRect, Vector2.zero, ScreenSize, L.ScreenTop, L.ScreenBottom, ScreenRadius);
                display.Outline.CornerStyle = CornerStyle.Round;
                display.Outline.Smoothing = 0.6f;
                display.Rect.gameObject.AddComponent<UniShapeMask>().ShowShape = true;
                Live = L.Ambience?.Invoke(Stage, display.Rect);
                return display.Rect;
            }

            /// <summary>A panel of the look: a raised surface content stands on.</summary>
            public Widget Panel(Transform parent, string name, Vector2 at, Vector2 size, float radius = -1f)
            {
                var panel = Ramp(parent, name, ShapeKind.RoundedRect, at, size, L.PanelHigh, L.Panel, radius < 0f ? L.Radius * 1.4f : radius);
                var shape = panel.Shape;
                switch (L.Depth)
                {
                    case Depth.Lip:
                        Stage.AddBevel(shape, 90f, 3f, 0.35f);
                        Stage.AddShadow(shape, Theme.Fade(L.Outline, 0.7f), new Vector2(0f, -10f), 0f);
                        Stage.AddShadow(shape, Theme.Fade(Color.black, 0.3f), new Vector2(0f, -24f), 40f);
                        break;
                    case Depth.Gloss:
                        Stage.AddStroke(shape, Theme.Fade(Color.white, 0.9f), 4f, -1f);
                        Stage.AddInnerShadow(shape, Theme.Fade(L.Line, 0.4f), new Vector2(0f, 8f), 18f);
                        Stage.AddStroke(shape, L.Line, 3f, 1f);
                        Stage.AddShadow(shape, L.Line, new Vector2(0f, -8f), 0f);
                        Stage.AddShadow(shape, Theme.Fade(L.Outline, 0.3f), new Vector2(0f, -16f), 26f);
                        break;
                    case Depth.Glow:
                        Stage.AddStroke(shape, Theme.Fade(L.Line, 0.85f), 2f, -1f);
                        Stage.AddShadow(shape, Theme.Fade(L.Line, 0.35f), Vector2.zero, 24f, 1f);
                        break;
                    case Depth.Hud:
                        Stage.AddStroke(shape, Theme.Fade(L.Line, 0.55f), 1.5f, -1f);
                        Stage.AddInnerShadow(shape, Theme.Fade(L.Line, 0.18f), Vector2.zero, 30f);
                        break;
                    case Depth.Pixel:
                        Stage.AddStroke(shape, L.Line, 5f, -1f);
                        Stage.AddStroke(shape, L.Outline, 5f, 1f);
                        Stage.AddShadow(shape, Theme.Fade(L.Outline, 0.8f), new Vector2(10f, -10f), 0f);
                        break;
                    case Depth.Ornate:
                        Stage.AddInnerShadow(shape, Theme.Fade(L.Outline, 0.45f), Vector2.zero, 34f);
                        Stage.AddStroke(shape, L.Gold, 2.5f, -1f);
                        Stage.AddStroke(shape, L.Outline, 6f, 1f);
                        Stage.AddShadow(shape, Theme.Fade(Color.black, 0.45f), new Vector2(0f, -16f), 30f);
                        break;
                }

                Seat(panel, at, size, ShapeKind.RoundedRect);
                L.Trim?.Invoke(Stage, panel.Rect, panel.Rect.sizeDelta);
                return panel;
            }

            /// <summary>A well set into a panel: darker, shaded on its inner top edge.</summary>
            public Widget Inset(Transform parent, string name, ShapeKind kind, Vector2 at, Vector2 size, float radius = -1f)
            {
                var well = Fill(parent, name, kind, at, size, L.Inset, radius);
                switch (L.Depth)
                {
                    case Depth.Lip:
                    case Depth.Ornate:
                        Stage.AddInnerShadow(well.Shape, Theme.Fade(Color.black, 0.4f), new Vector2(0f, -3f), 6f);
                        break;
                    case Depth.Gloss:
                        Stage.AddInnerShadow(well.Shape, Theme.Fade(L.TextDim, 0.45f), new Vector2(0f, -4f), 7f);
                        Stage.AddInnerShadow(well.Shape, Theme.Fade(Color.white, 0.7f), new Vector2(0f, 3f), 3f);
                        break;
                    case Depth.Glow:
                    case Depth.Hud:
                        Stage.AddStroke(well.Shape, Theme.Fade(L.Line, 0.3f), 1.5f, -1f);
                        break;
                    case Depth.Pixel:
                        Stage.AddStroke(well.Shape, L.Outline, 3f, 1f);
                        break;
                }

                Seat(well, at, size, kind);
                return well;
            }

            /// <summary>A colored surface of <paramref name="tone"/>: a button's face, a lit chip, a filled bar.</summary>
            public Widget Toned(Transform parent, string name, ShapeKind kind, Vector2 at, Vector2 size, Tone tone, float radius = -1f,
                bool shine = true, Relief relief = Relief.Raised)
            {
                var raised = relief != Relief.Flush;
                var lip = new Vector2(0f, -Mathf.Min(LipDepth, size.y * LipShare));
                Widget face;
                switch (L.Depth)
                {
                    case Depth.Lip:
                        face = Ramp(parent, name, kind, at, size, tone.Light, tone.Dark, radius);
                        Stage.AddBevel(face.Shape, 90f, 3f, 0.45f);
                        if (raised) Stage.AddShadow(face.Shape, tone.Lip, lip, 0f);
                        break;
                    case Depth.Gloss:
                        face = Ramp(parent, name, kind, at, size, tone.Light, tone.Dark, radius);
                        if (raised)
                        {
                            Stage.AddStroke(face.Shape, tone.Lip, 3f, 1f);
                            Stage.AddShadow(face.Shape, tone.Lip, lip, 0f);
                            Stage.AddShadow(face.Shape, Theme.Fade(tone.Lip, 0.3f), lip + new Vector2(0f, -3f), 8f);
                        }

                        break;
                    case Depth.Glow:
                        face = Fill(parent, name, kind, at, size, tone.Light, radius);
                        Stage.AddShadow(face.Shape, Theme.Fade(tone.Light, 0.75f), Vector2.zero, 20f, 2f);
                        break;
                    case Depth.Hud:
                        face = Fill(parent, name, kind, at, size, Theme.Fade(tone.Light, 0.2f), radius);
                        Stage.AddStroke(face.Shape, tone.Light, 2f, -1f);
                        Stage.AddShadow(face.Shape, Theme.Fade(tone.Light, 0.4f), Vector2.zero, 12f);
                        break;
                    case Depth.Pixel:
                        face = Fill(parent, name, kind, at, size, tone.Light, radius);
                        if (raised)
                        {
                            Stage.AddStroke(face.Shape, L.Outline, 4f, 1f);
                            Stage.AddShadow(face.Shape, L.Outline, lip, 0f);
                        }

                        break;
                    default:
                        face = Ramp(parent, name, kind, at, size, tone.Light, tone.Dark, radius);
                        if (raised)
                        {
                            Stage.AddStroke(face.Shape, L.Gold, 2.5f, 1f);
                            Stage.AddShadow(face.Shape, tone.Lip, lip, 0f);
                        }

                        break;
                }

                Seat(face, at, size, kind);
                var lit = face.Rect.sizeDelta;
                if (shine && lit.x > 30f)
                {
                    if (L.Depth == Depth.Gloss)
                        Fill(face.Rect, "Shine", ShapeKind.Capsule, new Vector2(0f, lit.y * 0.24f),
                            new Vector2(kind == ShapeKind.Circle ? lit.x * 0.56f : lit.x - Mathf.Min(22f, lit.x * 0.24f), lit.y * 0.24f),
                            Theme.Fade(Color.white, 0.45f), 0f);
                    else if (L.Depth == Depth.Pixel)
                        Fill(face.Rect, "Gleam", ShapeKind.RoundedRect, new Vector2(0f, lit.y * 0.5f - 7f), new Vector2(lit.x - 10f, 5f),
                            Theme.Fade(Color.white, 0.45f), 0f);
                }

                return face;
            }

            /// <summary>
            /// Shrinks <paramref name="part"/>'s face until everything it draws with a hard edge fits the box
            /// <paramref name="size"/> large at <paramref name="at"/>; a circle stays round.
            /// </summary>
            private void Seat(Widget part, Vector2 at, Vector2 size, ShapeKind kind)
            {
                var reach = Vector4.zero;
                foreach (var layer in part.Shape.Layers)
                {
                    if (layer is StrokeLayer stroke)
                    {
                        var outset = stroke.Width * 0.5f * (stroke.Align + 1f);
                        reach = Vector4.Max(reach, new Vector4(outset, outset, outset, outset));
                    }
                    else if (layer is ShadowLayer { Blur: 0f } hard)
                    {
                        var (offset, spread) = (hard.Offset, hard.Spread);
                        reach = Vector4.Max(reach, new Vector4(spread - offset.x, spread - offset.y, spread + offset.x, spread + offset.y));
                    }
                }

                var face = new Vector2(size.x - reach.x - reach.z, size.y - reach.y - reach.w);
                if (kind == ShapeKind.Circle) face = Vector2.one * Mathf.Min(face.x, face.y);
                Stage.Box(part.Rect, at + new Vector2(reach.x - reach.z, reach.y - reach.w) * 0.5f, face);
            }

            /// <summary>
            /// Moves <paramref name="node"/> onto its seated <paramref name="face"/>, so what is built on the node centers on
            /// the face; returns where the node rests.
            /// </summary>
            private static Vector2 Center(RectTransform node, Widget face)
            {
                node.anchoredPosition += face.Rect.anchoredPosition;
                face.Rect.anchoredPosition = Vector2.zero;
                return node.anchoredPosition;
            }

            /// <summary>The hard shadow a raised face stands on; null for a look without lips.</summary>
            private static ShadowLayer Lip(Widget face)
            {
                foreach (var layer in face.Shape.Layers)
                    if (layer is ShadowLayer { Blur: 0f } hard)
                        return hard;
                return null;
            }

            /// <summary>A heading in the look's display face and its heading treatment.</summary>
            public UniText Heading(Transform parent, string text, float size, Vector2 at, float width, HAlign align = HAlign.Left)
            {
                var label = Words(parent, L.Cased(text), size * L.Type, L.Text, L.Display, L.DisplayWeight, at, width, align);
                L.Heading?.Invoke(label);
                return label;
            }

            /// <summary>A line in the look's body face.</summary>
            public UniText Caption(Transform parent, string text, float size, Color color, Vector2 at, float width, HAlign align = HAlign.Left) =>
                Words(parent, L.Cased(text), size * L.BodyType, color, L.Body, L.BodyWeight, at, width, align);

            /// <summary>A figure or a name in the look's display face, without the heading's treatment.</summary>
            public UniText Figure(Transform parent, string text, float size, Color color, Vector2 at, float width, HAlign align = HAlign.Left) =>
                Words(parent, L.Cased(text), size * L.Type, color, L.Display, L.DisplayWeight, at, width, align);

            /// <summary>A caption on a part of <paramref name="tone"/>: in its ink when it is a light tone, else as the look treats tones.</summary>
            public UniText Toned(Transform parent, string text, float size, Vector2 at, float width, Tone tone, HAlign align = HAlign.Center)
            {
                var inked = tone.Ink.a > 0f;
                var label = Words(parent, L.Cased(text), size * L.Type, inked ? tone.Ink : L.TextOnTone, L.Display, L.DisplayWeight, at, width, align);
                if (inked) Drop(label, Theme.Fade(Color.white, 0.35f), new Vector2(0f, -0.05f));
                else L.OnTone?.Invoke(label, tone);
                return label;
            }

            /// <summary>An initial on a colored disc: white, lifted by a shadow whatever the look.</summary>
            public UniText Initial(Transform parent, string text, float size, float width)
            {
                var label = Words(parent, text, size * L.Type, Color.white, L.Display, L.DisplayWeight, new Vector2(0f, 1f), width, HAlign.Center);
                Drop(label, Theme.Fade(Color.black, 0.4f), new Vector2(0f, -0.07f));
                return label;
            }

            private UniText Words(Transform parent, string text, float size, Color color, UniTextFont face, float weight, Vector2 at, float width,
                HAlign align)
            {
                var label = Stage.Label(parent, text, size, color, align, VAlign.Middle, stretch: false, face: face);
                label.WordWrap = false;
                var rect = label.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(align == HAlign.Left ? 0f : align == HAlign.Right ? 1f : 0.5f, 0.5f);
                rect.sizeDelta = new Vector2(width, size * 1.6f);
                rect.anchoredPosition = at;
                if (weight > 0f) label.Styles.Add(Style.WholeText(new VariationModifier { Weight = UnitValue.Absolute(weight) }));
                if (L.Tracking != 0f) label.Styles.Add(Style.WholeText(new LetterSpacingModifier { Spacing = UnitValue.Em(L.Tracking) }));
                return label;
            }

            /// <summary>A paragraph in the look's body face, wrapped to <paramref name="width"/> and hung from its top.</summary>
            public UniText Paragraph(Transform parent, string text, float size, Color color, Vector2 topLeft, Vector2 box)
            {
                var label = Words(parent, L.Cased(text), size * L.BodyType, color, L.Body, L.BodyWeight, topLeft, box.x, HAlign.Left);
                label.WordWrap = true;
                label.VerticalAlignment = VAlign.Top;
                label.rectTransform.pivot = new Vector2(0f, 1f);
                label.rectTransform.sizeDelta = box;
                return label;
            }

            /// <summary>A button of <paramref name="tone"/> with its caption; it sinks onto its lip, or shrinks, as it is pressed.</summary>
            public Button Button(Transform parent, string caption, Vector2 at, Vector2 size, Tone tone, float text = 30f, float radius = -1f)
            {
                var node = Node(parent, "Button", at);
                var face = Toned(node, "Face", ShapeKind.RoundedRect, Vector2.zero, size, tone, radius < 0f ? L.Radius : radius);
                var home = Center(node, face);
                var label = Toned(node, caption, text, new Vector2(0f, 2f), face.Rect.sizeDelta.x - 16f, tone);
                return new Button(node, home, label, Lip(face));
            }

            /// <summary>A square button of <paramref name="tone"/> for an icon the caller draws on the returned node, which centers on its face.</summary>
            public Button IconButton(Transform parent, Vector2 at, float size, Tone tone)
            {
                var node = Node(parent, "Icon Button", at);
                var face = Toned(node, "Face", ShapeKind.RoundedRect, Vector2.zero, Vector2.one * size, tone, L.Radius * 0.8f);
                return new Button(node, Center(node, face), null, Lip(face));
            }

            /// <summary>
            /// A purse: a dark capsule holding an amount, its icon's place at the left end left to the caller, and a plus
            /// for buying more at the right end when <paramref name="plus"/>.
            /// </summary>
            public UniText Pill(Transform parent, Vector2 at, float width, string amount, bool plus = true)
            {
                var node = Node(parent, "Purse", at);
                Inset(node, "Well", ShapeKind.Capsule, Vector2.zero, new Vector2(width, PillHeight), 0f);
                if (plus)
                {
                    var button = Toned(node, "Plus", ShapeKind.Circle, new Vector2(width * 0.5f - PillHeight * 0.5f, 0f), Vector2.one * (PillHeight - 8f),
                        L.Primary, 0f);
                    Glyphs.Plus(this, button.Rect, Vector2.zero, PillHeight * 0.42f, L.TextOnTone);
                }

                return Figure(node, amount, 28f, L.Text, new Vector2(-width * 0.5f + PillHeight + 6f, 1f), width - PillHeight * 2f, HAlign.Left);
            }

            /// <summary>Where a purse <paramref name="width"/> wide holds its icon, from its middle.</summary>
            public static Vector2 PillIcon(float width) => new(-width * 0.5f + PillHeight * 0.5f + 2f, 0f);

            /// <summary>A modal surface's veil over everything beneath it.</summary>
            public CanvasGroup Veil(Transform parent, float strength = 0.6f)
            {
                var (node, group) = Group(parent, "Veil", Vector2.zero);
                Fill(node, "Shade", ShapeKind.RoundedRect, Vector2.zero, ScreenSize, Theme.Fade(L.Outline, strength), 0f);
                group.alpha = 0f;
                return group;
            }

            /// <summary>
            /// A banner across a part in <paramref name="tone"/>, its band and the folded tails behind either end filling a
            /// box <paramref name="width"/> by <paramref name="height"/>, with <paramref name="text"/> on the band.
            /// </summary>
            public UniText Ribbon(Transform parent, Vector2 at, float width, float height, Tone tone, string text, float size)
            {
                var node = Node(parent, "Ribbon", at);
                var band = height * BandShare;
                var tail = new Vector2(band * 0.9f, band * 0.78f);
                var length = width - (tail.x + 8f);
                foreach (var side in new[] { -1f, 1f })
                {
                    var fold = Fill(node, "Tail", ShapeKind.Parallelogram, new Vector2(side * (length * 0.5f + 4f), (tail.y - height) * 0.5f), tail, tone.Lip, 0f);
                    fold.Outline.Skew = side * 18f;
                }

                var face = Toned(node, "Band", ShapeKind.RoundedRect, new Vector2(0f, (height - band) * 0.5f), new Vector2(length, band), tone,
                    Mathf.Min(L.Radius, band * 0.3f));
                return Toned(face.Rect, text, size, new Vector2(0f, 2f), length - 30f, tone);
            }

            /// <summary>
            /// A window of the look, <paramref name="size"/> large: a panel titled <paramref name="title"/> in the look's way
            /// of heading a window, with a close button on its corner when <paramref name="closable"/>.
            /// </summary>
            public Window Window(Transform parent, string name, Vector2 at, Vector2 size, string title, bool closable = false)
            {
                var (node, group) = Group(parent, name, at);
                Panel(node, "Panel", Vector2.zero, size);
                var top = size.y * 0.5f;
                switch (L.Depth)
                {
                    case Depth.Lip:
                    case Depth.Ornate:
                        Ribbon(node, new Vector2(0f, top - 6f), Mathf.Min(size.x - 70f, 460f), 78f, L.Depth == Depth.Lip ? L.Secondary : L.Primary, title, 40f);
                        break;
                    case Depth.Gloss:
                        var band = Toned(node, "Header", ShapeKind.RoundedRect, new Vector2(0f, top - 50f), new Vector2(size.x - 36f, 74f), L.Secondary,
                            L.Radius * 0.8f);
                        Toned(band.Rect, title, 38f, new Vector2(0f, 2f), size.x - 120f, L.Secondary);
                        break;
                    case Depth.Pixel:
                        var tab = Panel(node, "Title", new Vector2(-size.x * 0.5f + 150f, top), new Vector2(254f, 70f), L.Radius);
                        Heading(tab.Rect, title, 26f, Vector2.zero, 220f, HAlign.Center);
                        break;
                    default:
                        Heading(node, title, 34f, new Vector2(-size.x * 0.5f + 30f, top - 40f), size.x - 120f);
                        Fill(node, "Rule", ShapeKind.RoundedRect, new Vector2(0f, top - 72f), new Vector2(size.x - 60f, 1.5f), Theme.Fade(L.Line, 0.5f), 0f);
                        break;
                }

                RectTransform close = null;
                if (closable)
                {
                    var corner = new Vector2(size.x * 0.5f - 14f, top - 14f);
                    if (L.Depth is Depth.Glow or Depth.Hud) corner -= new Vector2(26f, 26f);
                    var button = Toned(node, "Close", ShapeKind.Circle, corner, Vector2.one * 62f, L.Danger, 0f);
                    Glyphs.Cross(this, button.Rect, Vector2.zero, 30f, L.TextOnTone);
                    close = button.Rect;
                }

                return new Window(node, group, close);
            }

            /// <summary>
            /// A ring marking a chosen part <paramref name="size"/> large: a line of <paramref name="color"/> round it and
            /// a fainter band outside, so it reads lit without covering what it marks.
            /// </summary>
            public Widget Ring(Transform parent, Vector2 at, Vector2 size, Color color, float radius, float width = 5f)
            {
                var ring = Fill(parent, "Chosen", ShapeKind.RoundedRect, at, size, Color.clear, radius);
                Stage.AddStroke(ring.Shape, Theme.Fade(color, 0.3f), width * 1.6f, 1f);
                Stage.AddStroke(ring.Shape, color, width, 0f);
                return ring;
            }

            /// <summary>An item's frame of rarity <paramref name="rarity"/>, the item drawn on it by the caller.</summary>
            public Widget Slot(Transform parent, Vector2 at, float size, int rarity)
            {
                var tone = L.Rarity[Mathf.Clamp(rarity, 0, L.Rarity.Length - 1)];
                var slot = Inset(parent, "Slot", ShapeKind.RoundedRect, at, Vector2.one * size, L.Radius * 0.7f);
                switch (L.Depth)
                {
                    case Depth.Glow:
                    case Depth.Hud:
                        Stage.AddStroke(slot.Shape, tone.Light, 2f, -1f);
                        Stage.AddInnerShadow(slot.Shape, Theme.Fade(tone.Light, 0.45f), Vector2.zero, size * 0.22f);
                        break;
                    case Depth.Pixel:
                        Stage.AddStroke(slot.Shape, tone.Light, 4f, -1f);
                        break;
                    default:
                        Stage.AddInnerShadow(slot.Shape, Theme.Fade(tone.Light, 0.75f), Vector2.zero, size * 0.24f);
                        Stage.AddStroke(slot.Shape, tone.Dark, 3f, -1f);
                        break;
                }

                return slot;
            }

            /// <summary>
            /// Three stars across <paramref name="width"/>, on an arc rising to the middle one, <paramref name="earned"/>
            /// of them lit; returns them in order.
            /// </summary>
            public RectTransform[] Stars(Transform parent, Vector2 at, float size, float width, int earned, float rise)
            {
                var stars = new RectTransform[3];
                for (var i = 0; i < 3; i++)
                {
                    var middle = i == 1;
                    var s = size * (middle ? 1.18f : 1f);
                    var place = at + new Vector2((i - 1) * width * 0.5f, middle ? rise : 0f);
                    var lit = i < earned;
                    var star = Glyphs.Star(this, parent, place, s, lit ? L.Gold : Color.Lerp(L.Inset, L.Line, 0.55f));
                    var face = star.GetComponentInChildren<UniShape>();
                    Stage.AddStroke(face, lit ? Color.Lerp(L.Gold, Color.black, 0.35f) : L.Outline, Mathf.Max(2f, s * 0.06f), 1f);
                    stars[i] = star;
                }

                return stars;
            }

            /// <summary>A small capsule label of <paramref name="tone"/>: a rarity, a discount, a state.</summary>
            public UniText Tag(Transform parent, Vector2 at, string text, Tone tone, float size = 20f, float width = 0f)
            {
                var w = width > 0f ? width : text.Length * size * (0.66f + L.Tracking) * L.Type + size * 1.6f;
                var face = Toned(parent, "Tag", ShapeKind.Capsule, at, new Vector2(w, size * 1.7f), tone, 0f);
                return Toned(face.Rect, text, size, new Vector2(0f, 1f), w, tone);
            }

            /// <summary>An on/off switch, its knob sliding to the lit side as it turns on.</summary>
            public Toggle Switch(Transform parent, Vector2 at)
            {
                var node = Node(parent, "Switch", at);
                Inset(node, "Track", ShapeKind.Capsule, Vector2.zero, SwitchSize, 0f);
                var lit = Toned(node, "Lit", ShapeKind.Capsule, Vector2.zero, SwitchSize, L.Primary, 0f, false, Relief.Flush);
                var knob = Fill(node, "Knob", ShapeKind.Circle, Vector2.zero, Vector2.one * (SwitchSize.y - 8f), L.Depth == Depth.Glow ? L.Text : Color.white, 0f);
                if (L.Depth is Depth.Lip or Depth.Gloss or Depth.Ornate)
                    Stage.AddShadow(knob.Shape, Theme.Fade(Color.black, 0.35f), new Vector2(0f, -3f), 4f);
                if (L.Depth == Depth.Pixel) Stage.AddStroke(knob.Shape, L.Outline, 3f, 1f);
                return new Toggle(lit.Rect, knob.Rect);
            }

            /// <summary>A slider <paramref name="width"/> wide: a well, its filled part and a knob at the value.</summary>
            public Slider Slider(Transform parent, Vector2 at, float width, Tone tone)
            {
                var node = Node(parent, "Slider", at);
                Inset(node, "Track", ShapeKind.Capsule, Vector2.zero, new Vector2(width, TrackHeight), 0f);
                var fill = Toned(node, "Fill", ShapeKind.Capsule, Vector2.zero, new Vector2(width, TrackHeight), tone, 0f, false, Relief.Flush);
                var knob = Fill(node, "Knob", ShapeKind.Circle, Vector2.zero, Vector2.one * KnobSize, Color.white, 0f);
                switch (L.Depth)
                {
                    case Depth.Glow:
                        Stage.AddShadow(knob.Shape, Theme.Fade(tone.Light, 0.9f), Vector2.zero, 14f, 2f);
                        break;
                    case Depth.Hud:
                        Stage.Solid(knob.Fill, tone.Light);
                        Stage.AddShadow(knob.Shape, Theme.Fade(tone.Light, 0.7f), Vector2.zero, 10f);
                        break;
                    case Depth.Pixel:
                        Stage.AddStroke(knob.Shape, L.Outline, 3f, 1f);
                        Stage.AddShadow(knob.Shape, L.Outline, new Vector2(0f, -3f), 0f);
                        break;
                    default:
                        Stage.AddShadow(knob.Shape, Theme.Fade(Color.black, 0.4f), new Vector2(0f, -3f), 5f);
                        break;
                }

                return new Slider(node, fill.Rect, knob.Rect, width);
            }

            /// <summary>A segmented choice: a well with the options' captions and a thumb under the chosen one.</summary>
            public Segments Segmented(Transform parent, Vector2 at, string[] options, float cell, Tone tone, float height = SegmentHeight, float text = 22f)
            {
                var node = Node(parent, "Segmented", at);
                var width = cell * options.Length + 8f;
                Inset(node, "Track", ShapeKind.RoundedRect, Vector2.zero, new Vector2(width, height), L.Radius * 0.7f);
                var thumb = Toned(node, "Thumb", ShapeKind.RoundedRect, Vector2.zero, new Vector2(cell, height - 8f), tone, L.Radius * 0.55f);
                var middle = thumb.Rect.anchoredPosition.y + 1f;
                var captions = new UniText[options.Length];
                for (var i = 0; i < options.Length; i++)
                    captions[i] = Figure(node, options[i], text, L.Text, new Vector2(Segments.Center(i, options.Length, cell), middle), cell, HAlign.Center);
                return new Segments(thumb.Rect, captions, options.Length, cell);
            }

            /// <summary>A badge counting <paramref name="count"/>, to sit on a part's corner.</summary>
            public UniText Badge(Transform parent, Vector2 at, string count, float size = 32f)
            {
                var badge = Toned(parent, "Badge", ShapeKind.Circle, at, Vector2.one * size, L.Danger, 0f, false);
                return Words(badge.Rect, count, size * 0.55f, Color.white, L.Display, L.DisplayWeight, new Vector2(0f, 1f), size, HAlign.Center);
            }

            /// <summary>An avatar: a ring of <paramref name="ring"/> round an initial on a colored disc.</summary>
            public Widget Avatar(Transform parent, Vector2 at, float size, string initial, Color disc, Color ring)
            {
                var face = Ramp(parent, "Avatar", ShapeKind.Circle, at, Vector2.one * size, Color.Lerp(disc, Color.white, 0.25f), disc, 0f);
                Stage.AddStroke(face.Shape, ring, Mathf.Max(2f, size * 0.07f), 1f);
                Initial(face.Rect, initial, size * 0.46f, size);
                return face;
            }

            /// <summary>A bar: a well <paramref name="width"/> long filled to <paramref name="value"/> in <paramref name="tone"/>; returns the fill, hung from its left end.</summary>
            public RectTransform Progress(Transform parent, Vector2 at, float width, float height, float value, Tone tone)
            {
                var node = Node(parent, "Progress", at);
                Inset(node, "Track", ShapeKind.Capsule, Vector2.zero, new Vector2(width, height), 0f);
                var fill = Toned(node, "Fill", ShapeKind.Capsule, new Vector2(-width * 0.5f, 0f), new Vector2(width * value, height), tone, 0f, false,
                    Relief.Flush);
                fill.Rect.pivot = new Vector2(0f, 0.5f);
                fill.Rect.anchoredPosition = new Vector2(-width * 0.5f, 0f);
                return fill.Rect;
            }
        }

        /// <summary>A window's parts: the node it pops from and fades by, and its close button if it has one.</summary>
        internal sealed class Window
        {
            public Window(RectTransform node, CanvasGroup group, RectTransform close)
            {
                Node = node;
                Group = group;
                Close = close;
            }

            public RectTransform Node { get; }
            public CanvasGroup Group { get; }
            public RectTransform Close { get; }

            /// <summary>Poses the window opened <paramref name="open"/> seconds ago and closed <paramref name="closed"/> seconds ago; negative for not yet.</summary>
            public void Pose(float open, float closed = -1f)
            {
                var shown = open >= 0f && (closed < 0f || closed < CloseFor);
                if (Node.gameObject.activeSelf != shown) Node.gameObject.SetActive(shown);
                if (!shown) return;
                var grow = Pop.Evaluate(Mathf.Clamp01(open / OpenFor));
                var shrink = closed < 0f ? 0f : Ease.EmphasizedIn.Evaluate(Mathf.Clamp01(closed / CloseFor));
                Node.localScale = Vector3.one * Mathf.LerpUnclamped(0.7f, 1f, grow) * (1f - 0.25f * shrink);
                Group.alpha = Mathf.Clamp01(open / 0.12f) * (1f - shrink);
            }

            public const float OpenFor = 0.36f;
            public const float CloseFor = 0.24f;
        }

        /// <summary>A pressable button's parts: where it rests, its caption, how deep it sinks.</summary>
        internal sealed class Button
        {
            public Button(RectTransform node, Vector2 home, UniText caption, ShadowLayer lip)
            {
                Node = node;
                Home = home;
                Caption = caption;
                Rest = caption != null ? caption.Text : null;
                this.lip = lip;
                depth = lip != null ? -lip.Offset.y : 0f;
            }

            /// <summary>The caption the button shows at rest, before a move relabels it.</summary>
            public string Rest { get; }

            private readonly ShadowLayer lip;
            private readonly float depth;

            public RectTransform Node { get; }

            /// <summary>Where the node rests, on the button's face in its parent's units.</summary>
            public Vector2 Home { get; }

            public UniText Caption { get; }

            /// <summary>Poses the press, 0 at rest and 1 held down: sunk onto its lip, or shrunk when the look has none.</summary>
            public void Press(float pressed)
            {
                if (lip != null)
                {
                    Node.anchoredPosition = Home + new Vector2(0f, -depth * pressed);
                    lip.Offset = new Vector2(lip.Offset.x, -depth * (1f - pressed));
                }
                else
                {
                    Node.localScale = Vector3.one * (1f - 0.06f * pressed);
                }
            }

            public void Say(string text)
            {
                if (Caption.Text != text) Caption.Text = text;
            }
        }

        internal sealed class Toggle
        {
            public Toggle(RectTransform lit, RectTransform knob)
            {
                this.lit = lit;
                this.knob = knob;
                litShape = lit.GetComponent<UniShape>();
            }

            private readonly RectTransform lit;
            private readonly RectTransform knob;
            private readonly UniShape litShape;

            public void Set(float on)
            {
                var travel = (SwitchSize.x - SwitchSize.y) * 0.5f;
                knob.anchoredPosition = new Vector2(Mathf.Lerp(-travel, travel, on), 0f);
                lit.gameObject.SetActive(on > 0.001f);
                Stage.Alpha(litShape, on);
            }
        }

        internal sealed class Slider
        {
            public Slider(RectTransform node, RectTransform fill, RectTransform knob, float width)
            {
                Node = node;
                this.fill = fill;
                this.knob = knob;
                this.width = width;
                fill.pivot = new Vector2(0f, 0.5f);
                fill.anchoredPosition = new Vector2(-width * 0.5f, 0f);
            }

            private readonly RectTransform fill;
            private readonly RectTransform knob;
            private readonly float width;

            public RectTransform Node { get; }

            /// <summary>Where the knob stands at <paramref name="value"/>, from the slider's middle.</summary>
            public float KnobX(float value) => -width * 0.5f + KnobSize * 0.5f + (width - KnobSize) * value;

            public void Set(float value)
            {
                var x = KnobX(value);
                knob.anchoredPosition = new Vector2(x, 0f);
                fill.sizeDelta = new Vector2(x + width * 0.5f + KnobSize * 0.25f, fill.sizeDelta.y);
            }
        }

        internal sealed class Segments
        {
            public Segments(RectTransform thumb, UniText[] captions, int count, float cell)
            {
                this.thumb = thumb;
                Captions = captions;
                this.count = count;
                this.cell = cell;
            }

            private readonly RectTransform thumb;
            private readonly int count;
            private readonly float cell;

            public UniText[] Captions { get; }

            public static float Center(int index, int count, float cell) => (index - (count - 1) * 0.5f) * cell;

            public void Set(float index, Color on, Color off)
            {
                thumb.anchoredPosition = new Vector2(Mathf.Lerp(Center(0, count, cell), Center(count - 1, count, cell), index / (count - 1f)),
                    thumb.anchoredPosition.y);
                for (var i = 0; i < Captions.Length; i++)
                    Captions[i].color = Color.Lerp(off, on, Mathf.Clamp01(1f - Mathf.Abs(index - i)));
            }
        }

        /// <summary>Sets <paramref name="text"/> to <paramref name="value"/> unless it already reads so.</summary>
        private static void Say(UniText text, string value)
        {
            if (text.Text != value) text.Text = value;
        }

        /// <summary>Shows or hides <paramref name="part"/> unless it already is.</summary>
        private static void Show(Component part, bool shown)
        {
            if (part.gameObject.activeSelf != shown) part.gameObject.SetActive(shown);
        }

        /// <summary>How deep a raised part's lip is, at most, and as a share of a smaller part's height.</summary>
        private const float LipDepth = 7f;

        private const float LipShare = 0.15f;

        /// <summary>How much of a ribbon's height its band takes, the tails hanging below it taking the rest.</summary>
        private const float BandShare = 0.9f;
        private static readonly Vector2 SwitchSize = new(84f, 44f);
        private const float TrackHeight = 16f;
        private const float KnobSize = 40f;
        private const float SegmentHeight = 62f;
        private const float PillHeight = 58f;
        private const float ScreenRadius = 40f;
        private const float BezelWidth = 22f;
        private static readonly Color BezelColor = new Color32(0x0D, 0x0C, 0x12, 0xFF);
        private static readonly Color BezelEdge = new Color32(0x3A, 0x36, 0x44, 0xFF);
        private static readonly Color BezelLens = new Color32(0x22, 0x20, 0x2C, 0xFF);
    }
}
