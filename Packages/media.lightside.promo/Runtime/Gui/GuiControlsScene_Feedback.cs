using System;
using UnityEngine;

namespace LightSide.Promo
{
    public sealed partial class GuiControlsScene
    {
        /// <summary>
        /// Ready-made feedback: a game update started by its button and reported by every progress control at once — a
        /// bar, a ring counting percent, a segmented strip of steps, the patch art revealed by a radial fill, a spinner
        /// while it runs and a badge counting the notes — then a glass panel dragged over the art, blurring what it covers.
        /// </summary>
        private sealed class FeedbackPart : KitPart
        {
            private readonly GameObject update;
            private readonly UniShapeValue bar;
            private readonly UniShapeValue ring;
            private readonly UniShapeValue steps;
            private readonly UniImage art;
            private readonly GameObject spinner;
            private readonly UniText status;
            private readonly UniText install;
            private readonly UniText badge;
            private readonly RectTransform glass;

            public FeedbackPart(GuiControlsScene scene, Stage stage)
                : base(scene, stage, "Feedback", Board, Scale)
            {
                var top = Board.y / Scale * 0.5f;
                Label("Update 4.2", new Vector2(Left, top - 34f), 24f, "Ink", display: true);

                art = Add("Image", new Vector2(Left, ArtY), 0f).GetComponentInChildren<UniImage>(true);
                ((RectTransform)art.transform).sizeDelta = ArtSize;
                art.Sprite = null;
                art.Mode = ImageMode.Simple;
                art.Texture = scene.Art();
                art.FillMethod = ImageFillMethod.Radial360;
                art.FillOrigin = ImageFillOrigin.Top;

                status = Label("Ready to update", new Vector2(Left, BarLabel), 13f, "InkDim");
                bar = Add("Progress Bar", new Vector2(Left, BarY), 0f).GetComponentInChildren<UniShapeValue>(true);
                Label("Download · Verify · Install", new Vector2(Left, StepsLabel), 13f, "InkDim");
                steps = Add("Segmented Progress", new Vector2(Left, StepsY), 0f).GetComponent<UniShapeValue>();

                update = Add("Button", new Vector2(Left, ButtonY), 0f);
                Kit.Relabel(update, "Button", "Update");

                ring = Add("Progress Ring", new Vector2(Right + RingHalf, RingY)).GetComponentInChildren<UniShapeValue>(true);
                Label("Overall", new Vector2(Right + RingHalf * 2f + 16f, RingY), 15f, "Ink");

                spinner = Add("Spinner", new Vector2(Right + SpinnerHalf, SpinnerY));
                install = Label("Waiting to install", new Vector2(Right + SpinnerHalf * 2f + 16f, SpinnerY), 13f, "InkDim");

                Label("Patch notes", new Vector2(Right, BadgeY), 15f, "Ink");
                badge = Add("Badge", new Vector2(Right + BadgeOffset, BadgeY), 0f).GetComponentInChildren<UniText>(true);

                glass = (RectTransform)Add("Glass Panel", GlassFrom).transform;
                glass.sizeDelta = GlassSize;
                glass.gameObject.AddComponent<UniWindow>().Snapping = false;
                glass.gameObject.AddComponent<UniWindowHandle>().Part = WindowPart.Move;

                Plan(At(new Vector2(Left + 70f, ButtonY - 70f)), new[]
                {
                    Beat.To(At(new Vector2(Left + 70f, ButtonY)), null, Wide(140f)),
                    Beat.Click("update", true),
                    Beat.To(At(GlassFrom), null, Wide(320f)),
                    Beat.Drag(At(GlassTo), "glass", Wide(320f)),
                    Beat.Wait(Settle)
                }, Pace);
                Aim("update", update);
                Aim("glass", glass);
            }

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                var started = ClickAt("update");
                var done = Mathf.Clamp01((played - started) / FillSeconds);
                var eased = Ease.Emphasized.Evaluate(done);
                bar.TrackTo(eased);
                ring.TrackTo(eased);
                steps.TrackTo(eased);
                art.FillAmount = played < started ? 0.12f : Mathf.Lerp(0.12f, 1f, eased);

                var running = played >= started && done < 1f;
                Stage.Appear((RectTransform)spinner.transform, running ? played - started : -1f, Motion.Back, SpinnerGrow);
                var line = played < started ? "Ready to update" : done < 1f ? "Downloading…" : "Up to date";
                if (status.Text != line) status.Text = line;
                var step = played < started ? "Waiting to install" : done < 1f ? "Installing in the background" : "Installed";
                if (install.Text != step) install.Text = step;
                var notes = (Mathf.FloorToInt(eased * 3f) + 1).ToString();
                if (badge.Text != notes) badge.Text = notes;

                glass.anchoredPosition = Vector2.Lerp(GlassFrom, GlassTo, Dragged("glass", played, At(GlassFrom), At(GlassTo)));
            }

            private static readonly Vector2 Board = new(BoardWidth, 740f);
            private static readonly Vector2 ArtSize = new(280f, 170f);
            private static readonly Vector2 GlassFrom = new(Right + 150f, -130f);
            private static readonly Vector2 GlassSize = new(260f, 150f);
            private static readonly Vector2 GlassTo = new(Left + 140f, ArtY);
            private const float BoardWidth = 1400f;
            private const float Scale = 1.85f;
            private const float Pace = 1.35f;
            private const float Pad = 28f;
            private const float Left = -BoardWidth / Scale * 0.5f + Pad;
            private const float Right = Left + 280f + 56f;
            private const float Top = 740f / Scale * 0.5f;
            private const float ArtY = Top - HeaderHeight - 85f;
            private const float BarLabel = ArtY - 85f - 26f;
            private const float BarY = BarLabel - 20f;
            private const float StepsLabel = BarY - 26f;
            private const float StepsY = StepsLabel - 20f;
            private const float ButtonY = StepsY - 44f;
            private const float RingHalf = 32f;
            private const float RingY = Top - HeaderHeight - 32f;
            private const float HeaderHeight = 64f;
            private const float SpinnerHalf = 16f;
            private const float SpinnerY = RingY - 64f;
            private const float BadgeY = SpinnerY - 50f;
            private const float BadgeOffset = 96f;
            private const float FillSeconds = 2.6f;
            private const float Settle = 1.3f;
        }

        private Texture2D art;

        /// <summary>The patch art: a warm dusk over two ridges, drawn once and kept for the scene's life.</summary>
        private Texture2D Art()
        {
            if (art) return art;
            const int width = 320;
            const int height = 200;
            art = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = "Patch Art", hideFlags = HideFlags.DontSave };
            var pixels = new Color32[width * height];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var v = y / (float)height;
                Color sky = brand.Evaluate(1f - v);
                var sun = Vector2.Distance(new Vector2(x, y), new Vector2(width * 0.68f, height * 0.5f)) < height * 0.17f;
                var far = y < height * (0.36f + 0.1f * Mathf.Sin(x * 0.03f));
                var near = y < height * (0.22f + 0.08f * Mathf.Sin(x * 0.05f + 1.3f));
                var color = sun ? Theme.Mix(sky, Color.white, 0.55f) : sky;
                if (far) color = Theme.Mix(color, theme.Violet, 0.6f);
                if (near) color = Theme.Mix(color, theme.Background, 0.75f);
                pixels[y * width + x] = color;
            }

            art.SetPixels32(pixels);
            art.Apply(false, true);
            return art;
        }
    }
}
