using LightSide.Promo;
using UnityEngine;
using static LightSide.ParticleShowcase.ReelKit;

namespace LightSide.ParticleShowcase
{
    /// <summary>A gift box wobbles, the player taps it, the lid flies off in a flash of rays and coins arc out and swoop into the purse.</summary>
    public sealed class RewardSlide : ShotSlide
    {
        private const float Press = 1.2f;
        private UniParticles back;
        private UniParticles loot;
        private UniParticles burst;
        private RectTransform box;
        private RectTransform lid;
        private CanvasGroup lidGroup;
        private RectTransform purseCoin;
        private UniText counter;
        private RectTransform amount;
        private CanvasGroup amountGroup;
        private Pointer pointer;

        protected override void OnBuild(Stage stage)
        {
            var root = stage.Root;
            Backdrop(root, new Color(0.36f, 0.16f, 0.58f), new Color(0.1f, 0.04f, 0.22f));
            Glow(root, "Glow", new Vector2(0f, 0f), 1500f, new Color(1f, 0.6f, 0.9f, 0.28f));
            Outline(Text(root, "DAILY GIFT", new Vector2(0f, 400f), new Vector2(900f, 120f), 100f, new Color(1f, 0.9f, 0.45f)), new Color(0.4f, 0.08f, 0.3f), 0.08f);

            back = Host(root, "Back", new Vector2(0f, -20f), new Vector2(400f, 400f));
            Rays(back.Effect, 101, Press, Vector3.zero, 1500f, new Color(1f, 0.85f, 0.45f, 0.55f), 6f, 18f);
            Flash(back.Effect, 102, Press, Vector3.zero, 1100f, new Color(1f, 0.92f, 0.7f, 1f), 0.6f);

            box = Node(root, "Gift", new Vector2(0f, -120f), new Vector2(340f, 260f));
            var body = Vertical(Shape(box, "Body", new Vector2(0f, -20f), new Vector2(320f, 230f), ShapeKind.RoundedRect, 30f, Color.white),
                new Color(1f, 0.38f, 0.5f), new Color(0.78f, 0.12f, 0.32f));
            Shadow(body, new Color(0.15f, 0f, 0.15f, 0.55f), new Vector2(0f, -16f), 30f);
            Stroke(body, new Color(0.5f, 0.02f, 0.18f), 6f);
            Vertical(Shape(box, "Ribbon", new Vector2(0f, -20f), new Vector2(64f, 230f), ShapeKind.RoundedRect, 6f, Color.white),
                new Color(1f, 0.92f, 0.5f), new Color(1f, 0.68f, 0.15f));

            lid = Node(root, "Lid", new Vector2(0f, 40f), new Vector2(370f, 100f));
            lidGroup = lid.gameObject.AddComponent<CanvasGroup>();
            var lidShape = Vertical(Shape(lid, "Lid", Vector2.zero, new Vector2(370f, 96f), ShapeKind.RoundedRect, 26f, Color.white),
                new Color(1f, 0.48f, 0.6f), new Color(0.85f, 0.18f, 0.38f));
            Stroke(lidShape, new Color(0.5f, 0.02f, 0.18f), 6f);
            Vertical(Shape(lid, "Band", Vector2.zero, new Vector2(70f, 96f), ShapeKind.RoundedRect, 6f, Color.white), new Color(1f, 0.92f, 0.5f), new Color(1f, 0.68f, 0.15f));
            foreach (var side in new[] { -1f, 1f })
            {
                var loop = Vertical(Shape(lid, "Bow", new Vector2(side * 62f, 76f), new Vector2(120f, 80f), ShapeKind.Ellipse, 0f, Color.white),
                    new Color(1f, 0.92f, 0.5f), new Color(1f, 0.62f, 0.12f));
                loop.transform.localRotation = Quaternion.Euler(0f, 0f, side * -22f);
                Stroke(loop, new Color(0.7f, 0.35f, 0.02f), 6f);
            }

            Stroke(Radial(Shape(lid, "Knot", new Vector2(0f, 64f), new Vector2(56f, 56f), ShapeKind.Circle, 0f, Color.white),
                new Color(1f, 0.95f, 0.6f), new Color(1f, 0.65f, 0.15f)), new Color(0.7f, 0.35f, 0.02f), 5f);

            var purse = Shape(root, "Purse", new Vector2(700f, 455f), new Vector2(360f, 96f), ShapeKind.Capsule, 0f, new Color(0.08f, 0.03f, 0.15f, 0.85f));
            Stroke(purse, new Color(1f, 0.85f, 0.45f, 0.6f), 4f);
            var coinIcon = Shape(root, "Purse Coin", new Vector2(560f, 455f), new Vector2(110f, 110f), ShapeKind.RoundedRect, 0f, Color.white);
            Textured(coinIcon, "Coin");
            purseCoin = (RectTransform)coinIcon.transform;
            counter = Outline(Text(root, "1,250", new Vector2(735f, 455f), new Vector2(260f, 90f), 62f, Color.white), new Color(0.15f, 0.05f, 0.25f), 0.08f);

            loot = Host(root, "Loot", new Vector2(0f, -40f), new Vector2(300f, 300f));
            PopAndFly(loot.Effect, 110, Press + 0.05f, Vector3.zero, 26f, "Coin", purseCoin, 104f, Palette(Color.white),
                new Color(1f, 0.85f, 0.4f, 0.55f), new Color(1f, 0.92f, 0.6f));

            burst = Host(root, "Burst", new Vector2(0f, -40f), new Vector2(300f, 300f));
            Shock(burst.Effect, 120, Press, Vector3.zero, 1000f, new Color(1f, 0.9f, 0.6f, 0.9f), 0.6f);
            var idle = Add(burst.Effect, "Idle Twinkles", 121, 16);
            idle.Looping = false;
            idle.Duration = Press;
            idle.Modules.Add(new EmissionModule { Rate = 5f });
            idle.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Rectangle, FitRect = false, Size = new Vector3(380f, 300f, 0f), Direction = ParticleDirection.Random });
            idle.Modules.Add(new StartModule { Lifetime = new ParticleRange(0.6f, 0.9f), Speed = new ParticleRange(0f), Size = new ParticleRange(60f, 100f), Rotation = new ParticleRange(0f, 45f), Spin = new ParticleRange(-60f, 60f), Color = new Color(1f, 0.92f, 0.7f) });
            idle.Modules.Add(new SizeOverLifeModule { From = 0.1f, To = 1f, Curve = Ease.Of(EasingType.BackOut) });
            idle.Modules.Add(new ColorOverLifeModule { Gradient = Fade(0.2f, 0.5f) });
            idle.Look = Painted("Flare");
            Flares(burst.Effect, 122, Press, Vector3.zero, 9f, 220f, 1500f, new Color(1f, 0.93f, 0.7f));

            amount = Node(root, "Amount", new Vector2(0f, 160f), new Vector2(500f, 140f));
            amountGroup = amount.gameObject.AddComponent<CanvasGroup>();
            Outline(Text(amount, "+750", Vector2.zero, new Vector2(500f, 140f), 130f, new Color(1f, 0.92f, 0.45f)), new Color(0.45f, 0.18f, 0.02f), 0.09f);

            pointer = stage.Pointer(new Vector2(520f, -480f), new[]
            {
                Beat.Wait(0.35f), Beat.To(new Vector2(30f, -110f), "gift", 300f), Beat.Click("gift", accent: true), Beat.Wait(0.3f),
                Beat.To(new Vector2(420f, -430f)),
            });
            Wake(stage.Root);
        }

        protected override void OnRender(float local)
        {
            pointer.Pose(local);
            var wobble = local < Press ? Mathf.Sin(local * 18f) * 5f * Smooth(Window(local, 0.2f, Press)) : 0f;
            var squash = local < Press ? 0f : Mathf.Exp(-(local - Press) * 7f) * Mathf.Cos((local - Press) * 26f);
            box.localRotation = Quaternion.Euler(0f, 0f, wobble);
            box.anchoredPosition = new Vector2(0f, -120f - 18f * squash);
            var fly = Window(local, Press, Press + 0.7f);
            lid.anchoredPosition = new Vector2(240f * Ease.Of(EasingType.QuadraticOut).Evaluate(fly), 40f + 520f * Ease.Of(EasingType.CubicOut).Evaluate(fly) - 300f * fly * fly);
            lid.localRotation = Quaternion.Euler(0f, 0f, wobble - 160f * fly);
            lidGroup.alpha = 1f - Smooth(Window(local, Press + 0.35f, Press + 0.7f));

            Show(back, local);
            Show(loot, local);
            Show(burst, local);

            var arrive = Window(local, Press + 0.95f, Press + 2.4f);
            var total = 1250 + Mathf.RoundToInt(Smooth(arrive) * 750f / 25f) * 25;
            counter.SetText(total.ToString("N0", System.Globalization.CultureInfo.InvariantCulture));
            var beat = arrive > 0f && arrive < 1f ? Mathf.Abs(Mathf.Sin(arrive * Mathf.PI * 10f)) : 0f;
            purseCoin.sizeDelta = Vector2.one * (110f + 26f * beat);

            var rise = Window(local, Press + 0.15f, Press + 1.6f);
            amountGroup.alpha = Smooth(Window(local, Press + 0.15f, Press + 0.3f)) * (1f - Smooth(Window(local, Press + 1.3f, Press + 1.7f)));
            amount.anchoredPosition = new Vector2(0f, 120f + 60f * Ease.Of(EasingType.BackOut).Evaluate(Window(local, Press + 0.15f, Press + 0.55f)) + 40f * rise);
        }
    }
}
