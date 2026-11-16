using System.Collections.Generic;
using UnityEngine;
using HAlign = LightSide.HorizontalAlignment;

namespace LightSide.Promo
{
    /// <summary>The settings card and the two beats of <see cref="GuiThemeScene"/>.</summary>
    public sealed partial class GuiThemeScene
    {
        /// <summary>
        /// A game's settings card: a title and six real controls — a switch, a checkbox, a slider, a segmented control,
        /// a progress bar and a button — each set to a believable state and labelled for it.
        /// </summary>
        /// <remarks>
        /// The controls are built at their own size inside a holder scaled up as one, so the card reads at film size
        /// while every control keeps the proportions it ships with. Their states are set when the card is posed, not
        /// here: the section is built inactive, and a control initializes only once it is enabled.
        /// </remarks>
        private void BuildCard(Stage stage)
        {
            labels.Clear();
            settled.Clear();
            var at = new Vector2(0f, contentCenter + contentHeight * CardRise);
            card = stage.Shape("Settings Card", stage.Root, ShapeKind.RoundedRect, CardRadius);
            stage.Box(card.Rect, at, CardSize);
            Stage.AddShadow(card.Shape, new Color(0f, 0f, 0f, 0.5f), new Vector2(0f, -18f), 56f);

            var holder = stage.Node("Controls", card.Rect);
            stage.Box(holder, Vector2.zero, CardSize / ControlScale);
            holder.localScale = Vector3.one * ControlScale;
            var half = CardSize / ControlScale * 0.5f;
            var left = -half.x + Inset;
            var right = half.x - Inset;

            Label(stage, holder, "Settings", 26f, "Ink", new Vector2(left, half.y - 30f), 0f, theme.DisplayFace);
            Label(stage, holder, "Audio & display", 13f, "InkDim", new Vector2(left, half.y - 54f), 0f);

            var row = half.y - 100f;
            var music = Kit.Spawn(switchPrefab, holder, palette);
            Kit.Relabel(music, "Switch", "Music");
            Kit.Place(music, new Vector2(left, row), 0f);
            var subtitles = Kit.Spawn(checkboxPrefab, holder, palette);
            Kit.Relabel(subtitles, "Checkbox", "Subtitles");
            Kit.Place(subtitles, new Vector2(right, row), 1f);

            row -= RowStep;
            Label(stage, holder, "Volume", 15f, "Ink", new Vector2(left, row), 0f);
            var slider = Kit.Spawn(sliderPrefab, holder, palette);
            Kit.Place(slider, new Vector2(right, row), 1f);
            settled.Add((slider.GetComponentInChildren<UniShapeValue>(true), 0.72f));

            row -= RowStep;
            Label(stage, holder, "Difficulty", 15f, "Ink", new Vector2(left, row), 0f);
            var segmented = Kit.Spawn(segmentedPrefab, holder, palette);
            Kit.Relabel(segmented, "Option 1", "Easy");
            Kit.Relabel(segmented, "Option 2", "Normal");
            Kit.Relabel(segmented, "Option 3", "Hard");
            Kit.Place(segmented, new Vector2(right, row), 1f);
            settled.Add((segmented.GetComponentInChildren<UniShapeValue>(true), 1f));

            row -= RowStep;
            Label(stage, holder, "Update", 15f, "Ink", new Vector2(left, row), 0f);
            var bar = Kit.Spawn(progressPrefab, holder, palette);
            Kit.Place(bar, new Vector2(right, row), 1f);
            progress = bar.GetComponentInChildren<UniShapeValue>(true);

            var save = Kit.Spawn(buttonPrefab, holder, palette);
            Kit.Relabel(save, "Button", "Save");
            Kit.Place(save, new Vector2(right, half.y - 42f), 1f);

            toggles.Clear();
            toggles.AddRange(card.Rect.GetComponentsInChildren<UniToggle>(true));
        }

        /// <summary>A label of the card colored from <paramref name="swatch"/> of the scene's palette.</summary>
        private UniText Label(Stage stage, Transform parent, string text, float size, string swatch, Vector2 at, float edge,
            UniTextFont face = null)
        {
            var label = stage.Label(parent, text, size, Color.white, edge == 0f ? HAlign.Left : HAlign.Right, stretch: false,
                face: face);
            label.WordWrap = false;
            var rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(edge, 0.5f);
            rect.sizeDelta = new Vector2(320f, size * 1.6f);
            rect.anchoredPosition = at;
            labels.Add((label, swatch));
            return label;
        }

        /// <summary>
        /// Writes the palette for <paramref name="local"/> and poses the card: present from the first beat's arrival to
        /// the last beat's departure, its surface and labels in the palette's colors, its download running.
        /// </summary>
        protected override void PoseScene(float local)
        {
            var first = Beats[0];
            var last = Beats[Beats.Count - 1];
            var shown = local >= first.In && local <= last.Out + last.Leaving;
            card.Rect.gameObject.SetActive(shown);
            if (!shown) return;

            var accents = (AccentsPart)Beats[0];
            var modes = (ModesPart)Beats[1];
            WritePalette(modes.Mode(local - modes.ActionAt), accents.Accent(local - accents.ActionAt));

            foreach (var toggle in toggles)
                if (!toggle.On) toggle.On = true;
            foreach (var (value, at) in settled)
                if (value.Value != at) value.SnapTo(at);

            Stage.Solid(card.Fill, palette["Surface"]);
            foreach (var (text, swatch) in labels) text.color = palette[swatch];
            progress.SnapTo(Mathf.Lerp(0.35f, 0.92f, Mathf.InverseLerp(first.In, last.Out, local)));
            card.Rect.anchoredPosition = cardAt + first.Arrival(local) + last.Departure(local);
        }

        private Vector2 cardAt => new Vector2(0f, contentCenter + contentHeight * CardRise);

        /// <summary>New accent: a row of accent dots under the card; each click repaints every control's accent.</summary>
        private Part BuildAccents(Stage stage)
        {
            var rowAt = new Vector2(0f, cardAt.y - CardSize.y * 0.5f - contentHeight * 0.12f);
            var part = new AccentsPart(stage, rowAt);
            part.Build(this, stage);
            return part;
        }

        private sealed class AccentsPart : DrawnPart
        {
            /// <summary>The accents in the order the dots show them; the palette starts on the first.</summary>
            private static readonly Color[] Hues =
            {
                new Color32(0x4D, 0x8D, 0xFF, 0xFF), new Color32(0xFF, 0x8A, 0x1F, 0xFF),
                new Color32(0xFF, 0x1F, 0x8E, 0xFF), new Color32(0x16, 0xC9, 0xA4, 0xFF)
            };

            private readonly float[] clicks = new float[Hues.Length - 1];
            private readonly Vector2 rowAt;
            private RectTransform[] dots;
            private RectTransform ring;

            public AccentsPart(Stage stage, Vector2 rowAt)
                : base(stage, "Accents", new Vector2(DotStep * 2.6f, rowAt.y - PointerDrop), Script(rowAt), SwitchPace)
            {
                this.rowAt = rowAt;
            }

            private static Vector2 Dot(Vector2 rowAt, int index) =>
                rowAt + new Vector2((index - (Hues.Length - 1) * 0.5f) * DotStep, 0f);

            private static Beat[] Script(Vector2 rowAt)
            {
                var beats = new List<Beat>();
                for (var i = 1; i < Hues.Length; i++)
                {
                    beats.Add(Beat.To(Dot(rowAt, i), null, DotSize));
                    beats.Add(Beat.Click("accent" + i, true));
                    beats.Add(Beat.Wait(AccentRead));
                }

                return beats.ToArray();
            }

            public void Build(GuiThemeScene scene, Stage stage)
            {
                dots = new RectTransform[Hues.Length];
                for (var i = 0; i < Hues.Length; i++)
                {
                    var dot = stage.Shape("Accent", Root, ShapeKind.Circle);
                    stage.Box(dot.Rect, Dot(rowAt, i), Vector2.one * DotSize);
                    Stage.Solid(dot.Fill, Hues[i]);
                    dots[i] = dot.Rect;
                }

                var picked = stage.Shape("Picked", Root, ShapeKind.Circle);
                ring = picked.Rect;
                stage.Box(ring, Dot(rowAt, 0), Vector2.one * (DotSize + 18f));
                Stage.Solid(picked.Fill, new Color(1f, 1f, 1f, 0f));
                Stage.AddStroke(picked.Shape, scene.theme.Text, 3f, -1f);
            }

            /// <summary>The accent <paramref name="played"/> seconds into the beat: each click blends to the dot clicked.</summary>
            public Color Accent(float played)
            {
                for (var i = 0; i < clicks.Length; i++) clicks[i] = ClickAt("accent" + (i + 1));
                var color = Hues[0];
                for (var i = 0; i < clicks.Length; i++)
                    color = Turn(color, Hues[i + 1], Ease.Emphasized.Window(played, clicks[i], AccentBlend));
                return color;
            }

            /// <summary>Blends <paramref name="from"/> to <paramref name="to"/> around the hue wheel the short way, so the blend stays as vivid as its ends.</summary>
            private static Color Turn(Color from, Color to, float t)
            {
                t = Mathf.Clamp01(t);
                Color.RGBToHSV(from, out var h1, out var s1, out var v1);
                Color.RGBToHSV(to, out var h2, out var s2, out var v2);
                var hue = Mathf.Repeat(h1 + Mathf.DeltaAngle(h1 * 360f, h2 * 360f) / 360f * t, 1f);
                return Color.HSVToRGB(hue, Mathf.Lerp(s1, s2, t), Mathf.Lerp(v1, v2, t));
            }

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                var x = Dot(rowAt, 0).x;
                for (var i = 0; i < clicks.Length; i++)
                    x += DotStep * Motion.Snap.Window(played, clicks[i], SwitchSlide);
                ring.anchoredPosition = new Vector2(x, rowAt.y);
                for (var i = 0; i < dots.Length; i++)
                    dots[i].sizeDelta = Vector2.one * (DotSize * (i == 0 ? 1f : Kick(played, clicks[i - 1])));
            }
        }

        /// <summary>New theme: a dark and light switch under the card; the click to light restyles the whole card.</summary>
        private Part BuildModes(Stage stage)
        {
            var switchAt = new Vector2(0f, cardAt.y - CardSize.y * 0.5f - contentHeight * 0.12f);
            var part = new ModesPart(stage, switchAt);
            part.Build(this, stage);
            return part;
        }

        private sealed class ModesPart : DrawnPart
        {
            private static readonly string[] Names = { "Dark", "Light" };
            private static readonly Vector2 Cell = new Vector2(170f, 68f);

            private readonly float[] clicks = new float[1];
            private readonly float[] weights = new float[2];
            private readonly Vector2 switchAt;
            private Switch options;
            private UniText[] names;
            private Color soft;
            private Color lit;

            public ModesPart(Stage stage, Vector2 switchAt)
                : base(stage, "Modes", new Vector2(Cell.x * 1.6f, switchAt.y - PointerDrop), new[]
                {
                    Beat.Wait(ModeLead),
                    Beat.To(switchAt + new Vector2(Cell.x * 0.5f, 0f), null, Cell.x * 0.6f),
                    Beat.Click("light", true, true),
                    Beat.Wait(ModeRead)
                })
            {
                this.switchAt = switchAt;
            }

            public void Build(GuiThemeScene scene, Stage stage)
            {
                soft = scene.theme.TextSoft;
                lit = scene.theme.Text;
                options = new Switch(stage, Root, scene.theme, switchAt, Cell, Names.Length);
                names = new UniText[Names.Length];
                for (var i = 0; i < Names.Length; i++)
                {
                    names[i] = stage.Label(options.Rect, Names[i], 28f, soft, stretch: false, face: scene.theme.DisplayFace);
                    stage.Box(names[i].rectTransform, options[i], Cell);
                }
            }

            /// <summary>How far the card has gone from dark to light <paramref name="played"/> seconds into the beat.</summary>
            public float Mode(float played) => Ease.Emphasized.Window(played, ClickAt("light"), ModeBlend);

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                clicks[0] = ClickAt("light");
                options.Pose(played, clicks, weights);
                for (var i = 0; i < names.Length; i++)
                {
                    names[i].color = Color.LerpUnclamped(soft, lit, weights[i]);
                    names[i].rectTransform.localScale = Vector3.one * (i == 0 ? 1f : Kick(played, clicks[0]));
                }
            }
        }

        private static readonly Vector2 CardSize = new Vector2(1029f, 634f);
        private const float CardRadius = 28f;

        /// <summary>How far above the content band's middle the card sits, as a share of the band's height.</summary>
        private const float CardRise = 0.09f;

        /// <summary>How much larger than shipped the controls are drawn, and their inset and row pitch at shipped size.</summary>
        private const float ControlScale = 2.1f;

        private const float Inset = 26f;
        private const float RowStep = 52f;

        private const float DotSize = 56f;
        private const float DotStep = 96f;
        private const float SwitchSlide = 0.24f;
        private const float AccentRead = 0.55f;
        private const float AccentBlend = 0.35f;
        private const float ModeLead = 0.2f;
        private const float ModeRead = 1.1f;
        private const float ModeBlend = 0.7f;
    }
}
