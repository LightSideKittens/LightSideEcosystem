using LightSide.Promo;
using UnityEngine;
using static LightSide.ParticleShowcase.ReelKit;

namespace LightSide.ParticleShowcase
{
    /// <summary>A season pass: the bar surges across three tiers with a blazing tip and a spray of sparks, each tier rings and flares, and the chest at the fifth bursts into loot.</summary>
    public sealed class PassSlide : ShotSlide
    {
        private const float FillFrom = 0.9f;
        private const float FillTo = 3f;
        private const float TrackWidth = 1560f;
        private const float Open = 3.25f;
        private static readonly Color Lit = new(1f, 0.82f, 0.3f);
        private static readonly string[] Rewards = { "Coin", "Gem", "Star", "Heart", "Coin", "Gem" };
        private UniParticles lead;
        private UniParticles tiers;
        private UniParticles chest;
        private RectTransform fill;
        private RectTransform leadRect;
        private RectTransform tip;
        private UniShape[] tiles;
        private RectTransform chestRect;
        private RectTransform banner;
        private CanvasGroup bannerGroup;

        private static float TileX(int i) => -TrackWidth * 0.5f + TrackWidth * (i + 0.5f) / 6f;

        protected override void OnBuild(Stage stage)
        {
            var root = stage.Root;
            Backdrop(root, new Color(0.5f, 0.16f, 0.55f), new Color(0.12f, 0.03f, 0.2f));
            Outline(Text(root, "SEASON PASS", new Vector2(0f, 410f), new Vector2(1000f, 130f), 110f, new Color(1f, 0.86f, 0.45f)), new Color(0.35f, 0.04f, 0.3f), 0.09f);

            chest = Host(root, "Chest Burst", new Vector2(TileX(4), 80f), new Vector2(200f, 200f));
            Rays(chest.Effect, 841, Open, Vector3.zero, 1100f, new Color(1f, 0.85f, 0.45f, 0.6f), 3f, 30f);
            Flash(chest.Effect, 842, Open, Vector3.zero, 900f, new Color(1f, 0.92f, 0.65f, 1f), 0.5f);
            var loot = Add(chest.Effect, "Loot", 843, 120);
            Once(loot, 34f, Open);
            loot.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Point, Direction = ParticleDirection.Fixed, DirectionVector = Vector3.up, Spread = 50f });
            loot.Modules.Add(new StartModule
            {
                Lifetime = new ParticleRange(1.6f, 2.2f), Speed = new ParticleRange(900f, 1450f), Size = new ParticleRange(70f, 92f),
                Spin = new ParticleRange(-200f, 200f), Rotation = new ParticleRange(-30f, 30f),
            });
            loot.Modules.Add(new ForceModule { Gravity = new Vector3(0f, -2200f, 0f), Drag = 0.4f });
            loot.Modules.Add(new FlipModule { Turns = new ParticleRange(1.5f, 2.5f), BackShade = 0.15f });
            loot.Modules.Add(new ColorOverLifeModule { Gradient = Fade(0f, 0.85f) });
            loot.Look = Painted("Coin", LayerBlend.Normal);

            tiles = new UniShape[6];
            for (var i = 0; i < 6; i++)
            {
                var tile = Vertical(Shape(root, "Tier " + (i + 1), new Vector2(TileX(i), 70f), new Vector2(220f, 250f), ShapeKind.RoundedRect, 34f, Color.white),
                    new Color(0.45f, 0.25f, 0.62f), new Color(0.26f, 0.12f, 0.38f));
                Shadow(tile, new Color(0f, 0f, 0f, 0.45f), new Vector2(0f, -12f), 30f);
                Stroke(tile, new Color(1f, 1f, 1f, 0.18f), 4f);
                tiles[i] = tile;
                if (i == 4)
                {
                    Stroke(Vertical(Shape(tile.transform, "Chest", new Vector2(0f, 14f), new Vector2(150f, 110f), ShapeKind.RoundedRect, 18f, Color.white),
                        new Color(1f, 0.7f, 0.3f), new Color(0.75f, 0.35f, 0.08f)), new Color(0.45f, 0.18f, 0.02f), 6f);
                    chestRect = (RectTransform)tile.transform.Find("Chest");
                    Vertical(Shape(chestRect, "Band", Vector2.zero, new Vector2(30f, 110f), ShapeKind.RoundedRect, 4f, Color.white),
                        new Color(1f, 0.95f, 0.6f), new Color(1f, 0.7f, 0.2f));
                }
                else
                {
                    var art = Textured(Shape(tile.transform, "Reward", new Vector2(0f, 22f), new Vector2(140f, 140f), ShapeKind.RoundedRect, 0f, Color.white), Rewards[i]);
                    if (Rewards[i] == "Gem") ((FillLayer)art.Layers[0]).Paint.Color = new Color(0.75f, 0.55f, 1f);
                }

                Outline(Text(tile.transform, (i + 1).ToString(), new Vector2(0f, -90f), new Vector2(120f, 60f), 52f, Color.white), new Color(0.15f, 0.03f, 0.2f), 0.12f);
            }

            var track = Shape(root, "Track", new Vector2(0f, -150f), new Vector2(TrackWidth, 44f), ShapeKind.Capsule, 0f, new Color(0.12f, 0.04f, 0.18f));
            Stroke(track, new Color(1f, 1f, 1f, 0.18f), 4f);
            fill = (RectTransform)Vertical(Shape(root, "Fill", new Vector2(0f, -150f), new Vector2(30f, 34f), ShapeKind.Capsule, 0f, Color.white),
                new Color(1f, 0.92f, 0.5f), new Color(1f, 0.55f, 0.15f)).transform;
            tip = (RectTransform)Glow(root, "Tip", new Vector2(0f, -150f), 180f, new Color(1f, 0.85f, 0.45f, 0.8f)).transform;

            lead = Host(root, "Lead Sparks", new Vector2(0f, -150f), new Vector2(10f, 10f));
            leadRect = (RectTransform)lead.transform;
            var sparks = Emitter(lead.Effect, 0, "Sparks", 801, 600);
            sparks.Space = ParticleSpace.World;
            sparks.Looping = false;
            sparks.StartDelay = FillFrom;
            sparks.Duration = FillTo - FillFrom;
            sparks.Modules.Add(new EmissionModule { Rate = 170f });
            sparks.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Circle, Radius = 14f, Direction = ParticleDirection.Fixed, DirectionVector = new Vector3(-0.3f, 1f, 0f), Spread = 70f });
            sparks.Modules.Add(new StartModule
            {
                Lifetime = new ParticleRange(0.4f, 0.8f), Speed = new ParticleRange(150f, 520f), Size = new ParticleRange(14f, 22f),
                ColorMode = ParticleColorMode.BetweenTwo, Color = new Color(1f, 0.97f, 0.8f), SecondColor = Lit,
            });
            sparks.Modules.Add(new ForceModule { Gravity = new Vector3(0f, -1100f, 0f), Drag = 1f });
            sparks.Modules.Add(new ColorOverLifeModule { Gradient = Fade(0f, 0.5f) });
            sparks.Modules.Add(new TrailModule { Length = 0.1f, Segments = 4, Width = 0.45f, TailWidth = 0.1f });
            sparks.Look = Glowing(0.5f);
            var flicker = Emitter(lead.Effect, 1, "Tip Flare", 802, 16);
            flicker.Looping = false;
            flicker.StartDelay = FillFrom;
            flicker.Duration = FillTo - FillFrom;
            flicker.Modules.Add(new EmissionModule { Rate = 16f });
            flicker.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Point });
            flicker.Modules.Add(new StartModule { Lifetime = new ParticleRange(0.12f, 0.2f), Speed = new ParticleRange(0f), Size = new ParticleRange(170f, 230f), Rotation = new ParticleRange(0f, 45f), Color = new Color(1f, 0.95f, 0.75f) });
            flicker.Look = Painted("Flare");

            tiers = Host(root, "Tier Bursts", new Vector2(0f, 70f), new Vector2(TrackWidth, 250f));
            for (var k = 0; k < 3; k++) Shock(tiers.Effect, (uint)(820 + k), TierTime(2 + k), new Vector3(TileX(2 + k), 0f, 0f), 560f, Lit, 0.5f);
            for (var k = 0; k < 3; k++) Flares(tiers.Effect, (uint)(830 + k), TierTime(2 + k), new Vector3(TileX(2 + k), 0f, 0f), 5f, 170f, 900f, new Color(1f, 0.95f, 0.75f));

            var bannerShape = Vertical(Shape(root, "Banner", new Vector2(0f, -320f), new Vector2(820f, 124f), ShapeKind.Capsule, 0f, Color.white),
                new Color(1f, 0.55f, 0.78f), new Color(0.78f, 0.18f, 0.52f));
            Stroke(bannerShape, Color.white, 6f);
            Outline(Text(bannerShape.transform, "TIER 5 UNLOCKED!", Vector2.zero, new Vector2(800f, 110f), 68f, Color.white), new Color(0.45f, 0.05f, 0.28f), 0.1f);
            banner = (RectTransform)bannerShape.transform;
            bannerGroup = banner.gameObject.AddComponent<CanvasGroup>();
            Wake(stage.Root);
        }

        private static float Progress(float t) => Mathf.Lerp(1.5f / 6f, 4.6f / 6f, Ease.Of(EasingType.CubicInOut).Evaluate(Window(t, FillFrom, FillTo)));

        private static float TierTime(int tier)
        {
            var target = (tier + 0.5f) / 6f;
            for (var t = FillFrom; t < FillTo; t += 0.005f)
                if (Progress(t) >= target) return t;
            return FillTo;
        }

        private void PoseFill(float t)
        {
            var width = TrackWidth * Progress(t);
            fill.sizeDelta = new Vector2(Mathf.Max(34f, width - 10f), 34f);
            fill.anchoredPosition = new Vector2(-TrackWidth * 0.5f + width * 0.5f, -150f);
            leadRect.anchoredPosition = new Vector2(-TrackWidth * 0.5f + width - 12f, -150f);
            tip.anchoredPosition = leadRect.anchoredPosition;
        }

        protected override void OnRender(float local)
        {
            Replay(lead, local, PoseFill);
            PoseFill(local);
            tip.GetComponent<UniShape>().Color = new Color(1f, 1f, 1f, Smooth(Window(local, FillFrom - 0.1f, FillFrom + 0.1f)) * (1f - Smooth(Window(local, FillTo, FillTo + 0.3f))));
            for (var i = 0; i < tiles.Length; i++)
            {
                var lit = i < 2 ? 1f : i <= 4 ? Smooth(Window(local, TierTime(i), TierTime(i) + 0.2f)) : 0f;
                var stroke = (StrokeLayer)tiles[i].Layers[2];
                stroke.Paint.Color = Color.Lerp(new Color(1f, 1f, 1f, 0.18f), Lit, lit);
                stroke.Width = 4f + 6f * lit;
            }

            Show(tiers, local);
            Show(chest, local);
            var jolt = local < Open - 0.5f || local >= Open ? 0f : Mathf.Sin((local - Open + 0.5f) * 44f) * 7f;
            chestRect.localRotation = Quaternion.Euler(0f, 0f, jolt);
            chestRect.gameObject.SetActive(local < Open + 0.05f);
            var b = Window(local, Open + 0.3f, Open + 0.75f);
            bannerGroup.alpha = Smooth(b);
            banner.anchoredPosition = new Vector2(0f, -330f - 60f * (1f - Ease.Of(EasingType.BackOut).Evaluate(b)));
        }
    }
}
