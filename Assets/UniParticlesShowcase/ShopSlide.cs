using LightSide.Promo;
using UnityEngine;
using static LightSide.ParticleShowcase.ReelKit;

namespace LightSide.ParticleShowcase
{
    /// <summary>A shop: sparks race around the legendary offer's border as it rises and swells under the pointer, the player buys it, rays burst behind it and gems pour into the purse.</summary>
    public sealed class ShopSlide : ShotSlide
    {
        private const float Press = 2f;
        private static readonly Gradient GemColors = Palette(new Color(0.45f, 0.85f, 1f), new Color(0.75f, 0.5f, 1f), new Color(1f, 0.5f, 0.85f), new Color(0.45f, 1f, 0.8f));
        private UniParticles back;
        private UniParticles edges;
        private UniParticles loot;
        private UniParticles burst;
        private UniText gemCount;
        private RectTransform gemIcon;
        private RectTransform stamp;
        private CanvasGroup stampGroup;
        private RectTransform buy;
        private RectTransform[] cards;
        private Pointer pointer;

        protected override void OnBuild(Stage stage)
        {
            var root = stage.Root;
            Backdrop(root, new Color(0.1f, 0.3f, 0.5f), new Color(0.02f, 0.07f, 0.16f));
            Outline(Text(root, "SHOP", new Vector2(-730f, 455f), new Vector2(360f, 110f), 90f, Color.white), new Color(0.02f, 0.12f, 0.25f), 0.09f);

            back = Host(root, "Back", new Vector2(0f, 40f), new Vector2(400f, 400f));
            Rays(back.Effect, 401, Press, Vector3.zero, 1500f, new Color(1f, 0.85f, 0.45f, 0.5f), 5f, 20f);
            Flash(back.Effect, 402, Press, Vector3.zero, 1000f, new Color(1f, 0.9f, 0.6f, 0.9f), 0.55f);

            var offers = new[]
            {
                ("STARTER", "$1.99", new Color(0.32f, 0.7f, 1f), new Color(0.1f, 0.22f, 0.48f), "Coin"),
                ("GEM CHEST", "$9.99", new Color(1f, 0.76f, 0.26f), new Color(0.42f, 0.2f, 0.05f), "Gem"),
                ("STAR PACK", "$4.99", new Color(0.45f, 0.95f, 0.55f), new Color(0.08f, 0.32f, 0.16f), "Star"),
            };
            cards = new RectTransform[3];
            for (var i = 0; i < 3; i++)
            {
                var (label, price, accent, deep, art) = offers[i];
                var legendary = i == 1;
                var size = legendary ? new Vector2(470f, 660f) : new Vector2(400f, 560f);
                var card = Vertical(Shape(root, label, new Vector2((i - 1) * 530f, -10f), size, ShapeKind.RoundedRect, 44f, Color.white),
                    Color.Lerp(deep, Color.white, 0.18f), deep * 0.55f + new Color(0f, 0f, 0f, 0.45f));
                Shadow(card, new Color(0f, 0f, 0f, 0.5f), new Vector2(0f, -16f), 50f, 4f);
                Stroke(card, new Color(accent.r, accent.g, accent.b, 0.8f), 5f);
                cards[i] = (RectTransform)card.transform;
                Glow(card.transform, "Halo", new Vector2(0f, 60f), 360f, new Color(accent.r, accent.g, accent.b, 0.45f));
                var icon = Textured(Shape(card.transform, "Art", new Vector2(0f, 60f), new Vector2(230f, 230f), ShapeKind.RoundedRect, 0f, Color.white), art);
                if (art == "Gem") ((FillLayer)icon.Layers[0]).Paint.Color = new Color(0.55f, 0.85f, 1f);
                Outline(Text(card.transform, label, new Vector2(0f, size.y * 0.5f - 66f), new Vector2(size.x - 20f, 70f), 54f, Color.white), deep * 0.4f, 0.1f);
                var priceButton = Vertical(Shape(card.transform, "Buy", new Vector2(0f, -size.y * 0.5f + 80f), new Vector2(280f, 96f), ShapeKind.Capsule, 0f, Color.white),
                    new Color(0.55f, 1f, 0.45f), new Color(0.15f, 0.68f, 0.2f));
                Shadow(priceButton, new Color(0f, 0f, 0f, 0.4f), new Vector2(0f, -8f), 0f);
                Stroke(priceButton, new Color(0.05f, 0.35f, 0.05f), 6f);
                Outline(Text(priceButton.transform, price, Vector2.zero, new Vector2(270f, 90f), 54f, Color.white), new Color(0.05f, 0.35f, 0.05f), 0.1f);
                if (!legendary) continue;
                buy = (RectTransform)priceButton.transform;
                var ribbon = Vertical(Shape(card.transform, "Best", new Vector2(0f, size.y * 0.5f + 6f), new Vector2(300f, 64f), ShapeKind.Capsule, 0f, Color.white),
                    new Color(1f, 0.45f, 0.6f), new Color(0.85f, 0.15f, 0.4f));
                Stroke(ribbon, Color.white, 4f);
                Outline(Text(ribbon.transform, "BEST VALUE", Vector2.zero, new Vector2(290f, 60f), 38f, Color.white), new Color(0.5f, 0.05f, 0.2f), 0.1f);

                edges = Host(root, "Edge Sparks", Vector2.zero, size);
                var follow = cards[i];
                var halo = Emitter(edges.Effect, 0, "Halo", 702, 200);
                halo.Prewarm = true;
                halo.Modules.Add(new EmissionModule { Rate = 40f });
                halo.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Outline, Follow = follow, Flow = 650f });
                halo.Modules.Add(new StartModule { Lifetime = new ParticleRange(1f, 1.4f), Speed = new ParticleRange(0f), Size = new ParticleRange(70f, 100f), Color = new Color(accent.r, accent.g, accent.b, 0.28f) });
                halo.Modules.Add(new ColorOverLifeModule { Gradient = Fade(0.2f, 0.4f) });
                halo.Look = Glowing(1f);
                var sparks = Emitter(edges.Effect, 1, "Sparks", 701, 600);
                sparks.Prewarm = true;
                sparks.Modules.Add(new EmissionModule { Rate = 150f });
                sparks.Modules.Add(new ShapeModule
                {
                    Kind = ParticleShapeKind.Outline, Follow = follow, Flow = 650f, Lane = -1f, Spread = 30f,
                });
                sparks.Modules.Add(new StartModule
                {
                    Lifetime = new ParticleRange(0.9f, 1.4f), Speed = new ParticleRange(0f, 30f), Size = new ParticleRange(14f, 22f),
                    ColorMode = ParticleColorMode.BetweenTwo, Color = new Color(1f, 0.97f, 0.75f), SecondColor = accent,
                });
                sparks.Modules.Add(new ColorOverLifeModule { Gradient = Fade(0.1f, 0.5f) });
                sparks.Modules.Add(new TrailModule { Length = 0.2f, Segments = 8, Width = 0.5f, TailWidth = 0.1f });
                sparks.Look = Glowing(0.5f);
                var glints = Emitter(edges.Effect, 2, "Glints", 703, 20);
                glints.Prewarm = true;
                glints.Modules.Add(new EmissionModule { Rate = 4f });
                glints.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Outline, Follow = follow, Flow = 650f });
                glints.Modules.Add(new StartModule { Lifetime = new ParticleRange(0.5f, 0.8f), Speed = new ParticleRange(0f), Size = new ParticleRange(90f, 140f), Rotation = new ParticleRange(0f, 45f), Spin = new ParticleRange(-90f, 90f), Color = new Color(1f, 0.95f, 0.75f) });
                glints.Modules.Add(new SizeOverLifeModule { From = 0.2f, To = 1f, Curve = Ease.Of(EasingType.BackOut) });
                glints.Modules.Add(new ColorOverLifeModule { Gradient = Fade(0.15f, 0.45f) });
                glints.Look = Painted("Flare");
            }

            var purse = Shape(root, "Gem Purse", new Vector2(700f, 455f), new Vector2(360f, 96f), ShapeKind.Capsule, 0f, new Color(0.02f, 0.06f, 0.14f, 0.85f));
            Stroke(purse, new Color(0.5f, 0.85f, 1f, 0.6f), 4f);
            var icon2 = Textured(Shape(root, "Gem", new Vector2(560f, 455f), new Vector2(110f, 110f), ShapeKind.RoundedRect, 0f, Color.white), "Gem");
            ((FillLayer)icon2.Layers[0]).Paint.Color = new Color(0.55f, 0.85f, 1f);
            gemIcon = (RectTransform)icon2.transform;
            gemCount = Outline(Text(root, "120", new Vector2(735f, 455f), new Vector2(260f, 90f), 62f, Color.white), new Color(0.02f, 0.1f, 0.25f), 0.08f);

            loot = Host(root, "Loot", new Vector2(0f, 50f), new Vector2(300f, 300f));
            PopAndFly(loot.Effect, 710, Press + 0.05f, Vector3.zero, 24f, "Gem", gemIcon, 96f, GemColors, new Color(0.6f, 0.85f, 1f, 0.55f), new Color(0.8f, 0.92f, 1f));

            burst = Host(root, "Burst", new Vector2(0f, -280f), new Vector2(300f, 300f));
            Shock(burst.Effect, 720, Press, Vector3.zero, 700f, new Color(0.6f, 1f, 0.6f, 0.9f), 0.5f);
            Flares(burst.Effect, 721, Press, Vector3.zero, 6f, 180f, 1200f, new Color(0.9f, 1f, 0.85f));

            var stampShape = Vertical(Shape(cards[1], "Purchased", new Vector2(0f, -30f), new Vector2(420f, 110f), ShapeKind.RoundedRect, 26f, Color.white),
                new Color(0.5f, 1f, 0.45f), new Color(0.15f, 0.65f, 0.2f));
            Stroke(stampShape, Color.white, 6f);
            Outline(Text(stampShape.transform, "PURCHASED!", Vector2.zero, new Vector2(410f, 100f), 58f, Color.white), new Color(0.05f, 0.35f, 0.08f), 0.1f);
            stamp = (RectTransform)stampShape.transform;
            stampGroup = stamp.gameObject.AddComponent<CanvasGroup>();

            pointer = stage.Pointer(new Vector2(-260f, -500f), new[]
            {
                Beat.Wait(0.5f), Beat.To(new Vector2(-520f, -230f)), Beat.Wait(0.3f), Beat.To(new Vector2(14f, -270f), "buy", 280f),
                Beat.Click("buy", accent: true), Beat.Wait(0.3f), Beat.To(new Vector2(380f, -470f)),
            });
            Wake(stage.Root);
        }

        protected override void OnRender(float local)
        {
            for (var i = 0; i < cards.Length; i++)
            {
                var rise = Ease.Of(EasingType.BackOut).Evaluate(Window(local, 0.1f * i, 0.1f * i + 0.55f));
                cards[i].anchoredPosition = new Vector2((i - 1) * 530f, -10f - 800f * (1f - rise));
            }

            var hover = Ease.Of(EasingType.BackOut).Evaluate(Window(local, 1.05f, 1.5f));
            var settle = Ease.Of(EasingType.CubicOut).Evaluate(Window(local, Press + 0.2f, Press + 0.7f));
            cards[1].sizeDelta = new Vector2(470f, 660f) + new Vector2(60f, 80f) * (hover - 0.5f * settle);
            cards[1].anchoredPosition += new Vector2(0f, 30f * (hover - settle));

            pointer.Pose(local);
            Show(back, local);
            Show(edges, local + 2f);
            Show(loot, local);
            Show(burst, local);
            var press = Window(local, Press - 0.08f, Press + 0.3f);
            buy.sizeDelta = new Vector2(280f, 96f) * (1f - 0.08f * Mathf.Sin(press * Mathf.PI));
            var arrive = Window(local, Press + 0.95f, Press + 2.3f);
            gemCount.SetText((120 + Mathf.RoundToInt(Smooth(arrive) * 24f) * 10).ToString());
            var beat = arrive > 0f && arrive < 1f ? Mathf.Abs(Mathf.Sin(arrive * Mathf.PI * 9f)) : 0f;
            gemIcon.sizeDelta = Vector2.one * (110f + 26f * beat);
            var s = Pop(local, Press + 0.3f, 0.4f);
            stampGroup.alpha = Smooth(Window(local, Press + 0.3f, Press + 0.45f));
            stamp.localRotation = Quaternion.Euler(0f, 0f, Mathf.LerpUnclamped(-24f, 8f, s));
            stamp.anchoredPosition = new Vector2(0f, Mathf.LerpUnclamped(80f, -30f, s));
        }
    }
}
