using System;
using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// The cold open of the UniGUI reel: four real controls cut in on the beat, each whipped into the middle and
    /// changing state as it lands, and then the wordmark slammed in their place.
    /// </summary>
    /// <remarks>
    /// The controls are the kit's prefabs in the scene's own warm palette, set from the slide's time each frame and
    /// easing between states with their own transitions: a switch turned on, a rating filled, a ring run to full, a
    /// pie redrawn.
    /// </remarks>
    [AddComponentMenu(PromoMenu.AddComponent.Scenes + nameof(GuiOpeningScene))]
    public sealed class GuiOpeningScene : Slide
    {
        [SerializeField] private string wordmark = "UniGUI";

        /// <summary>The kit's palette the scene's own copy starts from.</summary>
        [SerializeField] private UniShapePaints basePalette;

        /// <summary>The kit's control prefabs, found by name.</summary>
        [SerializeField] private GameObject[] kit = Array.Empty<GameObject>();

        private static readonly float[] PieFrom = { 25f, 25f, 25f, 25f };
        private static readonly float[] PieTo = { 48f, 27f, 15f, 10f };

        private ScenePalette palette;
        private Impacts impacts;
        private RectTransform[] hits;
        private UniToggle toggle;
        private UniRating rating;
        private UniShapeValue ring;
        private UniChart pie;
        private bool pieDrawn;
        private GlyphReveal markReveal;
        private float frameWidth;

        protected override void OnBuild(Stage stage)
        {
            if (!basePalette) throw new InvalidOperationException($"[Promo] '{name}' needs its palette assigned.");

            var theme = stage.Theme;
            frameWidth = stage.Width;
            stage.Backdrop(stage.Root);
            var glow = stage.Shape("Glow", stage.Root, ShapeKind.Ellipse);
            stage.Box(glow.Rect, Vector2.zero, new Vector2(stage.Width * 0.9f, stage.Height * 1.1f));
            Stage.Radial(glow.Fill, Theme.Fade(theme.Orange, GlowStrength), Theme.Fade(theme.Orange, 0f));
            palette?.Dispose();
            palette = new ScenePalette(basePalette);
            palette.Warm(theme);

            hits = new RectTransform[HitCount];
            var sound = Hit(stage, 0, "Switch", SwitchScale);
            Kit.Relabel(sound, "Switch", "Sound");
            toggle = sound.GetComponent<UniToggle>();
            rating = Hit(stage, 1, "Rating", RatingScale).GetComponent<UniRating>();
            ring = Hit(stage, 2, "Progress Ring", RingScale).GetComponentInChildren<UniShapeValue>(true);
            pie = Hit(stage, 3, "Pie Chart", PieScale).GetComponentInChildren<UniChart>(true);
            pieDrawn = false;
            palette.Present();

            var mark = stage.Stretch(stage.Node("Wordmark", stage.Root));
            markReveal = Slam.Line(stage, mark, wordmark, MarkSize, Vector2.zero, true, theme.Text);

            impacts = new Impacts(stage, stage.Root);
            for (var i = 0; i < HitCount; i++)
            {
                impacts.Hit(Land(i), Vector2.zero, HitBurst);
                Cue("hit", Land(i));
            }

            impacts.Hit(MarkAt + MarkSlam * 0.7f, Vector2.zero, MarkBurst);
            impacts.Shake(MarkAt + MarkSlam * 0.7f, Vector2.down, MarkJolt);
            Cue("mark", MarkAt);
            Seconds = End;
        }

        /// <summary>The control <paramref name="control"/> enlarged <paramref name="scale"/> times, centered on a node of its own for hit <paramref name="index"/>.</summary>
        private GameObject Hit(Stage stage, int index, string control, float scale)
        {
            var holder = stage.Node(control, stage.Root);
            stage.Box(holder, Vector2.zero, Vector2.zero);
            holder.localScale = Vector3.one * scale;
            var instance = Kit.Spawn(Kit.Find(kit, control), holder, palette);
            Kit.Place(instance, Vector2.zero);
            hits[index] = holder;
            return instance;
        }

        /// <summary>When hit <paramref name="index"/> lands in the middle.</summary>
        private static float Land(int index) => FirstHit + index * HitStep + HitWhip;

        protected override void OnRender(float local)
        {
            for (var i = 0; i < HitCount; i++)
            {
                var arrive = FirstHit + i * HitStep;
                var leave = arrive + HitStep;
                var side = i % 2 == 0 ? 1f : -1f;
                var shown = local >= arrive && local <= leave + HitWhip;
                hits[i].gameObject.SetActive(shown);
                if (!shown) continue;
                var x = side * frameWidth * (1f - Motion.Swish.Window(local, arrive, HitWhip)) -
                        side * frameWidth * Motion.Swish.Window(local, leave, HitWhip);
                hits[i].anchoredPosition = new Vector2(x, 0f);
            }

            var on = local >= Land(0) + StateAfter;
            if (toggle.On != on) toggle.On = on;
            rating.Value = local >= Land(1) + StateAfter ? 5 : 0;
            ring.Value = local >= Land(2) + StateAfter ? 1f : 0f;
            var drawn = local >= Land(3) + StateAfter;
            if (drawn != pieDrawn)
            {
                pieDrawn = drawn;
                pie.Series[0].Values.ReplaceAll(drawn ? PieTo : PieFrom);
            }

            markReveal.Fill = GlyphReveal.Frontier.Window(local, MarkAt, MarkSlam);
            Content.anchoredPosition = impacts.Pose(local);
        }

        private void OnDestroy() => palette?.Dispose();

        private const float GlowStrength = 0.09f;
        private const int HitCount = 4;
        private const float FirstHit = 0.1f;

        /// <summary>How long each control holds the middle before the next cuts it away, and how long a whip takes.</summary>
        private const float HitStep = 0.5f;

        private const float HitWhip = 0.2f;

        /// <summary>How long after landing a control changes its state: long enough to be seen standing first.</summary>
        private const float StateAfter = 0.06f;

        private const float SwitchScale = 6f;
        private const float RatingScale = 5f;
        private const float RingScale = 8f;
        private const float PieScale = 3.4f;
        private const float HitBurst = 300f;

        private const float MarkSize = 240f;
        private const float MarkAt = FirstHit + HitCount * HitStep;
        private const float MarkSlam = 0.28f;
        private const float MarkBurst = 620f;
        private const float MarkJolt = 22f;
        private const float End = MarkAt + 1.5f;
    }
}
