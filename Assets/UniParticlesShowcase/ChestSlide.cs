using System.Globalization;
using LightSide.Promo;
using UnityEngine;
using static LightSide.ParticleShowcase.ReelKit;

namespace LightSide.ParticleShowcase
{
    /// <summary>
    /// Victory: the player taps the reward chest, its lid bursts open in rays, a ring and blooming stars, three
    /// reward orbs float out, and each tap pops one into sparks while its reward flies to its counter in the top bar.
    /// </summary>
    public sealed class ChestSlide : ShotSlide
    {
        private const float Drag = 4f;
        private static readonly Vector2 Mouth = new(0f, -150f);
        private static readonly Color GemTint = new(0.55f, 0.85f, 1f);
        private static readonly (string texture, Color tint, Vector2 rest, Color spark, Vector2 chip, int from, int amount)[] Rewards =
        {
            ("Heart", Color.white, new Vector2(-420f, 70f), new Color(1f, 0.4f, 0.6f), new Vector2(-600f, 470f), 4, 1),
            ("Coin", Color.white, new Vector2(0f, 140f), new Color(1f, 0.82f, 0.3f), new Vector2(-30f, 470f), 1250, 500),
            ("Gem", GemTint, new Vector2(420f, 70f), new Color(0.4f, 0.85f, 1f), new Vector2(560f, 470f), 120, 25),
        };

        private readonly UniText[] counters = new UniText[3];
        private readonly RectTransform[] icons = new RectTransform[3];
        private readonly int[] arrivals = new int[3];
        private readonly float[] arrivedAt = new float[3];
        private readonly float[] pops = new float[3];
        private readonly Vector2[] popPoints = new Vector2[3];
        private UniParticles back;
        private UniParticles air;
        private UniParticles burst;
        private UniParticles orbs;
        private RectTransform chest;
        private RectTransform lid;
        private CanvasGroup inner;
        private RectTransform banner;
        private Pointer pointer;
        private float press;
        private float replayTime;

        protected override void OnBuild(Stage stage)
        {
            var root = stage.Root;
            Backdrop(root, new Color(0.42f, 0.2f, 0.62f), new Color(0.1f, 0.04f, 0.24f));
            Glow(root, "Room Glow", new Vector2(0f, -120f), 1600f, new Color(1f, 0.55f, 0.85f, 0.22f));

            pointer = stage.Pointer(new Vector2(700f, -520f), new[]
            {
                Beat.Wait(0.35f), Beat.To(new Vector2(40f, -250f), "chest", 300f), Beat.Click("chest", accent: true), Beat.Wait(1.15f),
                Beat.To(Rewards[0].rest, "heart", 150f), Beat.Click("heart"), Beat.Wait(0.12f),
                Beat.To(Rewards[1].rest, "coin", 150f), Beat.Click("coin"), Beat.Wait(0.12f),
                Beat.To(Rewards[2].rest, "gem", 150f), Beat.Click("gem"), Beat.Wait(0.3f),
                Beat.To(new Vector2(720f, -480f)),
            });
            var presses = pointer.Timeline.Presses;
            press = presses[0].Down;
            for (var i = 0; i < 3; i++) pops[i] = presses[i + 1].Down;

            back = Host(root, "Back", Mouth, new Vector2(400f, 400f));
            Rays(back.Effect, 301, press, Vector3.zero, 1700f, new Color(1f, 0.85f, 0.45f, 0.5f), 6f, 16f);
            Flash(back.Effect, 302, press, Vector3.zero, 1100f, new Color(1f, 0.9f, 0.65f, 1f), 0.55f);

            BuildBar(root);
            BuildBanner(root);
            BuildChest(root);

            air = Host(root, "Air", new Vector2(0f, -40f), new Vector2(980f, 620f));
            var twinkles = Add(air.Effect, "Twinkles", 311, 48);
            twinkles.Modules.Add(new EmissionModule { Rate = 8f });
            twinkles.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Rectangle, FitRect = true, Direction = ParticleDirection.Random });
            twinkles.Modules.Add(new StartModule
            {
                Lifetime = new ParticleRange(0.9f, 1.4f), Speed = new ParticleRange(0f, 8f), Size = new ParticleRange(44f, 100f),
                Rotation = new ParticleRange(-10f, 10f), Spin = new ParticleRange(-40f, 40f),
                ColorMode = ParticleColorMode.RandomFromGradient, Gradient = Palette(Color.white, new Color(1f, 0.88f, 0.5f), new Color(1f, 0.7f, 0.92f)),
            });
            twinkles.Modules.Add(Plateau());
            twinkles.Look = Twinkle();
            var dust = Add(air.Effect, "Dust", 312, 160);
            dust.StartDelay = press;
            dust.Modules.Add(new EmissionModule { Rate = 34f });
            dust.Modules.Add(new ShapeModule
            {
                Kind = ParticleShapeKind.Line, Size = new Vector3(360f, 0f, 0f), Position = new Vector3(0f, Mouth.y + 40f, 0f),
                Direction = ParticleDirection.Fixed, DirectionVector = Vector3.up, Spread = 18f,
            });
            dust.Modules.Add(new StartModule
            {
                Lifetime = new ParticleRange(1.2f, 1.9f), Speed = new ParticleRange(120f, 260f), Size = new ParticleRange(18f, 34f),
                ColorMode = ParticleColorMode.BetweenTwo, Color = new Color(1f, 0.92f, 0.6f), SecondColor = new Color(1f, 0.7f, 0.35f),
            });
            dust.Modules.Add(new ForceModule { Gravity = Vector3.zero, Drag = 0.6f });
            dust.Modules.Add(Plateau());
            dust.Look = Glowing(0.8f);

            burst = Host(root, "Burst", Mouth, new Vector2(300f, 300f));
            Shock(burst.Effect, 321, press, Vector3.zero, 1150f, new Color(1f, 0.9f, 0.6f, 1f), 0.6f);
            Flares(burst.Effect, 322, press, Vector3.zero, 10f, 240f, 1600f, new Color(1f, 0.93f, 0.7f));
            var popcorn = Add(burst.Effect, "Popcorn", 323, 96);
            Once(popcorn, 46f, press);
            popcorn.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Line, Size = new Vector3(300f, 0f, 0f), Direction = ParticleDirection.Fixed, DirectionVector = Vector3.up, Spread = 50f });
            popcorn.Modules.Add(new StartModule
            {
                Lifetime = new ParticleRange(1.1f, 1.6f), Speed = new ParticleRange(900f, 1500f), Size = new ParticleRange(44f, 76f),
                Rotation = new ParticleRange(0f, 72f), Spin = new ParticleRange(-240f, 240f), ColorMode = ParticleColorMode.RandomFromGradient, Gradient = Festive(),
            });
            popcorn.Modules.Add(new ForceModule { Gravity = new Vector3(0f, -2200f, 0f), Drag = 1.2f });
            popcorn.Modules.Add(Plateau());
            popcorn.Look = Popcorn();

            orbs = Host(root, "Orbs", Vector2.zero, new Vector2(300f, 300f));
            for (var i = 0; i < 3; i++) Orb(orbs.Effect, 330u + 10u * (uint)i, i, press + 0.22f + 0.08f * i);

            Wake(stage.Root);
        }

        private void BuildBar(Transform root)
        {
            for (var i = 0; i < 3; i++)
            {
                var reward = Rewards[i];
                var chip = Shape(root, "Chip", reward.chip + new Vector2(120f, 0f), new Vector2(330f, 92f), ShapeKind.Capsule, 0f, new Color(0.08f, 0.03f, 0.16f, 0.85f));
                Stroke(chip, new Color(1f, 0.85f, 0.5f, 0.55f), 4f);
                var icon = Shape(root, "Icon", reward.chip, new Vector2(108f, 108f), ShapeKind.RoundedRect, 0f, Color.white);
                Textured(icon, reward.texture);
                ((FillLayer)icon.Layers[0]).Paint.Color = reward.tint;
                icons[i] = (RectTransform)icon.transform;
                counters[i] = Outline(Text(root, Format(reward.from), reward.chip + new Vector2(150f, 0f), new Vector2(230f, 84f), 58f, Color.white),
                    new Color(0.15f, 0.05f, 0.25f), 0.08f);
            }
        }

        private void BuildBanner(Transform root)
        {
            banner = Node(root, "Banner", new Vector2(0f, 352f), new Vector2(900f, 150f));
            var ribbon = Vertical(Shape(banner, "Ribbon", Vector2.zero, new Vector2(860f, 128f), ShapeKind.RoundedRect, 44f, Color.white),
                new Color(1f, 0.74f, 0.26f), new Color(0.96f, 0.42f, 0.12f));
            Shadow(ribbon, new Color(0.2f, 0.02f, 0.15f, 0.55f), new Vector2(0f, -14f), 30f);
            Stroke(ribbon, new Color(0.55f, 0.2f, 0.02f), 7f);
            Outline(Text(banner, "VICTORY!", Vector2.zero, new Vector2(840f, 120f), 96f, Color.white), new Color(0.55f, 0.2f, 0.02f), 0.09f);
        }

        private void BuildChest(Transform root)
        {
            var wood = (top: new Color(0.7f, 0.4f, 0.2f), bottom: new Color(0.4f, 0.19f, 0.08f));
            var gold = (top: new Color(1f, 0.92f, 0.5f), bottom: new Color(0.95f, 0.6f, 0.12f));
            var edge = new Color(0.3f, 0.12f, 0.04f);
            chest = Node(root, "Chest", new Vector2(0f, -280f), new Vector2(480f, 260f));
            var body = Vertical(Shape(chest, "Body", Vector2.zero, new Vector2(470f, 250f), ShapeKind.RoundedRect, 28f, Color.white), wood.top, wood.bottom);
            Shadow(body, new Color(0.1f, 0f, 0.12f, 0.6f), new Vector2(0f, -20f), 36f);
            Stroke(body, edge, 7f);
            foreach (var x in new[] { -160f, 160f })
                Stroke(Vertical(Shape(chest, "Band", new Vector2(x, 0f), new Vector2(50f, 250f), ShapeKind.RoundedRect, 8f, Color.white), gold.top, gold.bottom), edge, 5f);

            inner = Node(chest, "Inner", new Vector2(0f, 120f), new Vector2(440f, 300f)).gameObject.AddComponent<CanvasGroup>();
            Glow(inner.transform, "Light", new Vector2(0f, 40f), 520f, new Color(1f, 0.85f, 0.4f, 0.95f));
            Shape(inner.transform, "Mouth", new Vector2(0f, 6f), new Vector2(430f, 46f), ShapeKind.Capsule, 0f, new Color(1f, 0.93f, 0.6f));

            var plate = Vertical(Shape(chest, "Lock", new Vector2(0f, 70f), new Vector2(96f, 110f), ShapeKind.RoundedRect, 22f, Color.white), gold.top, gold.bottom);
            Stroke(plate, edge, 5f);
            Shape(plate.transform, "Keyhole", new Vector2(0f, 8f), new Vector2(26f, 26f), ShapeKind.Circle, 0f, edge);
            Shape(plate.transform, "Slot", new Vector2(0f, -14f), new Vector2(12f, 30f), ShapeKind.RoundedRect, 4f, edge);

            lid = Node(chest, "Lid", new Vector2(0f, 175f), new Vector2(500f, 150f));
            var top = Vertical(Shape(lid, "Top", Vector2.zero, new Vector2(496f, 140f), ShapeKind.RoundedRect, 60f, Color.white),
                new Color(0.8f, 0.5f, 0.26f), wood.top);
            Stroke(top, edge, 7f);
            foreach (var x in new[] { -160f, 160f })
                Stroke(Vertical(Shape(lid, "Band", new Vector2(x, 0f), new Vector2(50f, 140f), ShapeKind.RoundedRect, 8f, Color.white), gold.top, gold.bottom), edge, 5f);
            Stroke(Vertical(Shape(lid, "Rim", new Vector2(0f, -58f), new Vector2(500f, 30f), ShapeKind.RoundedRect, 10f, Color.white), gold.top, gold.bottom), edge, 5f);
        }

        /// <summary>
        /// A reward orb rising out of the chest inside a glowing bubble to rest at its spot. A tap pops the orb into
        /// blooming stars and a flare while its reward flies to its counter, flaring as it lands; the bubble, timed to
        /// the tap, bursts into a ring.
        /// </summary>
        private void Orb(ParticleEffect effect, uint seed, int index, float at)
        {
            var reward = Rewards[index];
            var path = reward.rest - Mouth;
            var bubble = Rising(effect, "Bubble Glow " + reward.texture, seed + 5, at, path, pops[index] - at, 300f, new Color(reward.spark.r, reward.spark.g, reward.spark.b, 0.5f));
            bubble.Look = Glowing(1f);
            var orb = Rising(effect, "Orb " + reward.texture, seed, at, path, 20f, 170f, reward.tint);
            orb.Look = Painted(reward.texture, LayerBlend.Normal);
            var skin = Rising(effect, "Bubble " + reward.texture, seed + 6, at, path, pops[index] - at, 250f, new Color(1f, 1f, 1f, 0.75f));
            skin.Look = new ShapeLook { Outline = new ParticleOutline(ShapeKind.Ring) { thickness = 0.07f }, Softness = 0.05f, Blend = LayerBlend.Additive };
            var burstRing = Add(effect, "Bubble Burst " + reward.texture, seed + 7, 4);
            burstRing.Modules.Add(new SubEmitterModule { Source = skin.NodeId, Trigger = ParticleSubEmitterTrigger.Death, Count = new ParticleRange(1f) });
            burstRing.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Point });
            burstRing.Modules.Add(new StartModule { Lifetime = new ParticleRange(0.4f), Speed = new ParticleRange(0f), Size = new ParticleRange(420f), Color = Color.Lerp(reward.spark, Color.white, 0.5f) });
            burstRing.Modules.Add(new SizeOverLifeModule { From = 0.55f, To = 1f, Curve = Ease.Of(EasingType.CubicOut) });
            burstRing.Modules.Add(new ColorOverLifeModule { Gradient = Ramp((0f, Color.white), (1f, new Color(1f, 1f, 1f, 0f))) });
            burstRing.Look = new ShapeLook
            {
                Outline = new ParticleOutline(ShapeKind.Ring) { thickness = 0.12f }, MorphTarget = new ParticleOutline(ShapeKind.Ring) { thickness = 0.02f },
                MorphTo = 1f, MorphCurve = Ease.Of(EasingType.CubicOut), Softness = 0.02f, Blend = LayerBlend.Additive,
            };

            var sparks = Add(effect, "Pop " + reward.texture, seed + 1, 32);
            sparks.Modules.Add(new SubEmitterModule { Source = orb.NodeId, Trigger = ParticleSubEmitterTrigger.Death, Count = new ParticleRange(18f) });
            sparks.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Circle, Radius = 40f, Thickness = 0f, Direction = ParticleDirection.Shape });
            sparks.Modules.Add(new StartModule
            {
                Lifetime = new ParticleRange(0.55f, 0.85f), Speed = new ParticleRange(420f, 780f), Size = new ParticleRange(40f, 66f),
                Rotation = new ParticleRange(0f, 72f), Spin = new ParticleRange(-240f, 240f), ColorMode = ParticleColorMode.RandomFromGradient,
                Gradient = Palette(reward.spark, Color.Lerp(reward.spark, Color.white, 0.55f)),
            });
            sparks.Modules.Add(new ForceModule { Gravity = new Vector3(0f, -900f, 0f), Drag = 3f });
            sparks.Modules.Add(Plateau());
            sparks.Look = Popcorn();

            var flare = Add(effect, "Pop Flare " + reward.texture, seed + 2, 4);
            flare.Modules.Add(new SubEmitterModule { Source = orb.NodeId, Trigger = ParticleSubEmitterTrigger.Death, Count = new ParticleRange(1f) });
            flare.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Point });
            flare.Modules.Add(new StartModule
            {
                Lifetime = new ParticleRange(0.4f), Speed = new ParticleRange(0f), Size = new ParticleRange(380f), Rotation = new ParticleRange(0f, 45f),
                Color = Color.Lerp(reward.spark, Color.white, 0.6f),
            });
            flare.Modules.Add(new SizeOverLifeModule { From = 0.3f, To = 1f, Curve = Ease.Of(EasingType.BackOut) });
            flare.Modules.Add(new ColorOverLifeModule { Gradient = Fade(0f, 0.35f) });
            flare.Look = Painted("Flare");

            var fly = Add(effect, "Fly " + reward.texture, seed + 3, 4);
            fly.Modules.Add(new SubEmitterModule { Source = orb.NodeId, Trigger = ParticleSubEmitterTrigger.Death, Count = new ParticleRange(1f) });
            fly.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Point, Direction = ParticleDirection.Fixed, DirectionVector = Vector3.down });
            fly.Modules.Add(new StartModule { Lifetime = new ParticleRange(3f), Speed = new ParticleRange(500f), Size = new ParticleRange(130f), Spin = new ParticleRange(-120f, 120f), Color = reward.tint });
            fly.Modules.Add(new ForceModule { Gravity = Vector3.zero, Drag = 1f });
            var attractor = new AttractorModule { Target = icons[index], Strength = 9000f, ArrivalRadius = 40f, Steering = 6f };
            var flying = effect.Emitters.IndexOf(fly);
            orbs.Notified += notice =>
            {
                if (notice.Kind != ParticleNoticeKind.Arrived || notice.Emitter != flying) return;
                arrivals[index] += notice.Count;
                arrivedAt[index] = replayTime;
            };
            fly.Modules.Add(attractor);
            fly.Modules.Add(new SizeBySpeedModule { From = 1f, To = 0.7f, MinSpeed = 200f, MaxSpeed = 1800f });
            fly.Modules.Add(new TrailModule
            {
                Length = 0.12f, Segments = 6, Width = 0.32f, TailWidth = 0f, TailAlpha = 0f,
                Color = Ramp((0f, new Color(reward.spark.r, reward.spark.g, reward.spark.b, 0.7f)), (1f, new Color(reward.spark.r, reward.spark.g, reward.spark.b, 0f))),
            });
            fly.Look = Painted(reward.texture, LayerBlend.Normal);

            var land = Add(effect, "Land " + reward.texture, seed + 4, 4);
            land.Modules.Add(new SubEmitterModule { Source = fly.NodeId, Trigger = ParticleSubEmitterTrigger.Death, Count = new ParticleRange(1f) });
            land.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Point });
            land.Modules.Add(new StartModule
            {
                Lifetime = new ParticleRange(0.4f), Speed = new ParticleRange(0f), Size = new ParticleRange(260f), Rotation = new ParticleRange(0f, 45f),
                Color = Color.Lerp(reward.spark, Color.white, 0.6f),
            });
            land.Modules.Add(new SizeOverLifeModule { From = 0.3f, To = 1f, Curve = Ease.Of(EasingType.BackOut) });
            land.Modules.Add(new ColorOverLifeModule { Gradient = Fade(0f, 0.35f) });
            land.Look = Painted("Flare");
        }

        /// <summary>
        /// One particle leaving the chest's mouth at <paramref name="at"/> seconds and coasting to rest at the end of
        /// <paramref name="path"/> under drag, swelling as it slows; every emitter made this way travels the same path.
        /// </summary>
        private static ParticleEmitter Rising(ParticleEffect effect, string name, uint seed, float at, Vector2 path, float life, float size, Color color)
        {
            var emitter = Add(effect, name, seed, 4);
            Once(emitter, 1f, at);
            emitter.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Point, Position = Mouth, Direction = ParticleDirection.Fixed, DirectionVector = path.normalized, Spread = 0f });
            emitter.Modules.Add(new StartModule { Lifetime = new ParticleRange(life), Speed = new ParticleRange(path.magnitude * Drag), Size = new ParticleRange(size), Spin = new ParticleRange(-14f, 14f), Color = color });
            emitter.Modules.Add(new ForceModule { Gravity = Vector3.zero, Drag = Drag });
            emitter.Modules.Add(new SizeBySpeedModule { From = 1f, To = 0.45f, MinSpeed = 0f, MaxSpeed = path.magnitude * Drag });
            return emitter;
        }

        protected override void OnRender(float local)
        {
            pointer.Pose(local);
            var camera = CanvasUtil.GetCanvasCamera(orbs.GetComponentInParent<Canvas>().rootCanvas);
            for (var i = 0; i < 3; i++)
                popPoints[i] = RectTransformUtility.WorldToScreenPoint(camera, pointer.On(pops[i]));

            var wobble = local < press ? Mathf.Sin(local * 20f) * 4f * Smooth(Window(local, 0.3f, press)) : 0f;
            var squash = local < press ? 0f : Mathf.Exp(-(local - press) * 7f) * Mathf.Cos((local - press) * 26f);
            chest.localRotation = Quaternion.Euler(0f, 0f, wobble);
            chest.localScale = new Vector3(1f + 0.06f * squash, 1f - 0.06f * squash, 1f);
            var open = Ease.Of(EasingType.BackOut).Evaluate(Window(local, press, press + 0.45f));
            lid.anchoredPosition = new Vector2(0f, 175f + 110f * open);
            lid.localRotation = Quaternion.Euler(0f, 0f, wobble - 10f * open);
            lid.localScale = new Vector3(1f, 1f - 0.42f * open, 1f);
            inner.alpha = Smooth(Window(local, press, press + 0.2f));
            banner.localScale = Vector3.one * Pop(local, 0.1f, 0.5f);

            Show(back, local);
            Show(air, local);
            Show(burst, local);
            System.Array.Clear(arrivals, 0, arrivals.Length);
            Replay(orbs, local, t =>
            {
                replayTime = t;
                for (var i = 0; i < 3; i++)
                    if (t >= pops[i] && t - Step < pops[i])
                        orbs.Pop(popPoints[i]);
            });

            for (var i = 0; i < 3; i++)
            {
                var reward = Rewards[i];
                var since = arrivals[i] > 0 ? local - arrivedAt[i] : -1f;
                var shown = since < 0f ? reward.from : reward.from + Mathf.RoundToInt(reward.amount * Smooth(Mathf.Clamp01(since / 0.35f)));
                counters[i].SetText(Format(shown));
                var punch = since < 0f ? 0f : Mathf.Exp(-since * 9f) * Mathf.Cos(since * 30f);
                icons[i].localScale = Vector3.one * (1f + 0.3f * punch);
            }
        }

        private static string Format(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
    }
}
