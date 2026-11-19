using System.Collections.Generic;
using LightSide.Promo;
using UnityEngine;
using static LightSide.ParticleShowcase.ReelKit;

namespace LightSide.ParticleShowcase
{
    /// <summary>Level complete: three stars slam into their slots with rings and flares, rays turn behind, and cannons fire confetti that piles on the stars and buttons.</summary>
    public sealed class LevelSlide : ShotSlide
    {
        private static readonly float[] StarTimes = { 0.7f, 1.05f, 1.4f };
        private static readonly Vector2[] StarSpots = { new(-200f, 60f), new(0f, 105f), new(200f, 60f) };
        private static readonly float[] StarSides = { 170f, 220f, 170f };
        private static readonly float[] StarTurns = { 12f, 0f, -12f };
        private const float Fire = 1.75f;
        private readonly List<RectTransform> stars = new();
        private UniParticles back;
        private UniParticles impacts;
        private UniParticles confetti;
        private RectTransform panel;
        private UniText title;
        private UniText score;

        protected override void OnBuild(Stage stage)
        {
            stars.Clear();
            var root = stage.Root;
            Backdrop(root, new Color(0.12f, 0.36f, 0.75f), new Color(0.04f, 0.1f, 0.3f));
            back = Host(root, "Back", new Vector2(0f, 120f), new Vector2(400f, 400f));
            Rays(back.Effect, 201, 0.5f, Vector3.zero, 1700f, new Color(0.7f, 0.9f, 1f, 0.4f), 7f, 14f);
            Flash(back.Effect, 202, StarTimes[1], new Vector3(0f, -15f, 0f), 900f, new Color(1f, 0.95f, 0.7f, 0.9f), 0.5f);

            var panelShape = Vertical(Shape(root, "Panel", new Vector2(0f, -40f), new Vector2(940f, 700f), ShapeKind.RoundedRect, 60f, Color.white),
                new Color(0.26f, 0.5f, 0.95f), new Color(0.12f, 0.24f, 0.62f));
            Shadow(panelShape, new Color(0f, 0.02f, 0.12f, 0.6f), new Vector2(0f, -24f), 70f, 8f);
            Stroke(panelShape, new Color(0.04f, 0.14f, 0.4f), 8f);
            panel = (RectTransform)panelShape.transform;
            var banner = Vertical(Shape(panel, "Banner", new Vector2(0f, 330f), new Vector2(760f, 130f), ShapeKind.RoundedRect, 40f, Color.white),
                new Color(1f, 0.72f, 0.25f), new Color(0.95f, 0.42f, 0.1f));
            Stroke(banner, new Color(0.55f, 0.2f, 0.02f), 7f);
            title = Outline(Text(banner.transform, "LEVEL COMPLETE", Vector2.zero, new Vector2(740f, 120f), 84f, Color.white), new Color(0.55f, 0.2f, 0.02f), 0.09f);

            var obstacles = new List<ParticleShapeObstacle>();
            for (var i = 0; i < 3; i++)
            {
                var slot = Shape(panel, "Slot", StarSpots[i], new Vector2(StarSides[i], StarSides[i]), ShapeKind.Star, 0f, new Color(0.05f, 0.1f, 0.32f, 0.85f));
                var provider = (PrimitiveShapeProvider)slot.Shape;
                provider.StarPoints = 5;
                provider.StarSharpness = 0.5f;
                slot.transform.localRotation = Quaternion.Euler(0f, 0f, StarTurns[i]);
                var star = Node(panel, "Star", StarSpots[i], new Vector2(StarSides[i], StarSides[i]) * 1.12f);
                star.localRotation = Quaternion.Euler(0f, 0f, StarTurns[i]);
                star.gameObject.AddComponent<UnityEngine.UI.RawImage>().texture = Tex("Star");
                stars.Add(star);
                obstacles.Add(new ParticleShapeObstacle((RectTransform)slot.transform, new ParticleOutline(ShapeKind.Star) { starPoints = 5, starSharpness = 0.5f }));
            }

            score = Outline(Text(panel, "0", new Vector2(0f, -85f), new Vector2(700f, 90f), 76f, Color.white), new Color(0.04f, 0.12f, 0.38f), 0.1f);
            foreach (var (at, label, top, bottom) in new[]
                     {
                         (new Vector2(-205f, -240f), "REPLAY", new Color(0.45f, 0.8f, 1f), new Color(0.15f, 0.45f, 0.95f)),
                         (new Vector2(205f, -240f), "NEXT", new Color(0.55f, 1f, 0.45f), new Color(0.15f, 0.7f, 0.2f)),
                     })
            {
                var button = Vertical(Shape(panel, label, at, new Vector2(330f, 110f), ShapeKind.Capsule, 0f, Color.white), top, bottom);
                Shadow(button, new Color(0f, 0.05f, 0.2f, 0.5f), new Vector2(0f, -9f), 0f);
                Stroke(button, bottom * 0.55f + new Color(0f, 0f, 0f, 0.45f), 6f);
                Outline(Text(button.transform, label, Vector2.zero, new Vector2(320f, 100f), 60f, Color.white), bottom * 0.5f + new Color(0f, 0f, 0f, 0.5f), 0.1f);
                obstacles.Add(new ParticleShapeObstacle((RectTransform)button.transform, new ParticleOutline(ShapeKind.Capsule)));
            }

            impacts = Host(panel, "Impacts", Vector2.zero, new Vector2(900f, 680f));
            for (var i = 0; i < 3; i++) Shock(impacts.Effect, (uint)(210 + i), StarTimes[i], StarSpots[i], StarSides[i] * 3.2f, new Color(1f, 0.9f, 0.5f, 0.95f), 0.5f);
            for (var i = 0; i < 3; i++) Flares(impacts.Effect, (uint)(220 + i), StarTimes[i], StarSpots[i], 6f, 150f, 1100f, new Color(1f, 0.95f, 0.7f));
            for (var i = 0; i < 3; i++)
            {
                var bits = Add(impacts.Effect, "Star Bits", (uint)(230 + i), 24);
                Once(bits, 9f, StarTimes[i]);
                bits.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Circle, Radius = 30f, Position = StarSpots[i], Direction = ParticleDirection.Shape });
                bits.Modules.Add(new StartModule
                {
                    Lifetime = new ParticleRange(0.7f, 1.1f), Speed = new ParticleRange(500f, 950f), Size = new ParticleRange(36f, 56f),
                    Rotation = new ParticleRange(0f, 360f), Spin = new ParticleRange(-360f, 360f),
                });
                bits.Modules.Add(new ForceModule { Gravity = new Vector3(0f, -1800f, 0f), Drag = 1.2f });
                bits.Modules.Add(new SizeOverLifeModule { From = 1f, To = 0.2f, Curve = Ease.Of(EasingType.QuadraticIn) });
                bits.Look = Painted("Star", LayerBlend.Normal);
            }

            confetti = Host(root, "Confetti", Vector2.zero, new Vector2(1920f, 1080f));
            for (var side = 0; side < 2; side++)
            {
                var sign = side == 0 ? -1f : 1f;
                var cannon = Emitter(confetti.Effect, side * 2, side == 0 ? "Left Cannon" : "Right Cannon", (uint)(41 + side), 500);
                var emission = Once(cannon, 150f, Fire);
                emission.Bursts.Add(new ParticleBurst(Fire + 0.8f, 110f));
                Cannon(cannon, sign, new ParticleRange(26f, 38f), new ParticleRange(0.4f, 0.65f), new ParticleOutline(ShapeKind.RoundedRect) { cornerRadius = 0.25f }, obstacles);
                var streamers = Emitter(confetti.Effect, side * 2 + 1, side == 0 ? "Left Streamers" : "Right Streamers", (uint)(51 + side), 120);
                Once(streamers, 26f, Fire);
                Cannon(streamers, sign, new ParticleRange(60f, 86f), new ParticleRange(0.12f, 0.16f), new ParticleOutline(ShapeKind.Capsule), obstacles);
            }

            Wake(stage.Root);
        }

        private static void Cannon(ParticleEmitter cannon, float sign, ParticleRange size, ParticleRange aspect, ParticleOutline outline, List<ParticleShapeObstacle> obstacles)
        {
            cannon.Modules.Add(new ShapeModule
            {
                Kind = ParticleShapeKind.Point, Position = new Vector3(sign * 930f, -560f, 0f),
                Direction = ParticleDirection.Fixed, DirectionVector = new Vector3(-sign * 0.42f, 1f, 0f), Spread = 16f,
            });
            cannon.Modules.Add(new StartModule
            {
                Lifetime = new ParticleRange(6f, 7f), Speed = new ParticleRange(1600f, 2600f), Size = size, Aspect = aspect,
                Spin = new ParticleRange(-280f, 280f), Rotation = new ParticleRange(0f, 360f), ColorMode = ParticleColorMode.RandomFromGradient, Gradient = Festive(),
            });
            cannon.Modules.Add(new ForceModule { Gravity = new Vector3(0f, -520f, 0f), Drag = 1.7f });
            var collision = new CollisionModule { RectWalls = ParticleRectWalls.Bottom, Bounce = 0.3f, Dampen = 0.1f };
            foreach (var obstacle in obstacles) collision.Shapes.Add(obstacle);
            cannon.Modules.Add(collision);
            cannon.Modules.Add(new FlipModule { Turns = new ParticleRange(0.7f, 2f), BackShade = 0.45f });
            cannon.Modules.Add(new SpinBySpeedModule { From = 0f, To = 1f, MinSpeed = 20f, MaxSpeed = 300f });
            cannon.Look = new ShapeLook { Outline = outline };
        }

        protected override void OnRender(float local)
        {
            panel.anchoredPosition = new Vector2(0f, -40f - 1000f * (1f - Ease.Of(EasingType.BackOut).Evaluate(Window(local, 0f, 0.5f))));
            for (var i = 0; i < stars.Count; i++)
            {
                var t = Window(local, StarTimes[i] - 0.28f, StarTimes[i]);
                var s = Mathf.Lerp(3.2f, 1f, Ease.Of(EasingType.QuadraticIn).Evaluate(t));
                var settle = local < StarTimes[i] ? 0f : Mathf.Exp(-(local - StarTimes[i]) * 9f) * Mathf.Sin((local - StarTimes[i]) * 30f) * 0.12f;
                stars[i].sizeDelta = Vector2.one * StarSides[i] * 1.12f * (s - settle);
                stars[i].localRotation = Quaternion.Euler(0f, 0f, StarTurns[i] - 60f * (1f - t));
                stars[i].gameObject.SetActive(local >= StarTimes[i] - 0.28f);
            }

            var tl = Window(local, 0.3f, 0.7f);
            ((RectTransform)title.transform).anchoredPosition = new Vector2(0f, 40f * (1f - Ease.Of(EasingType.BackOut).Evaluate(tl)));
            title.color = new Color(1f, 1f, 1f, Smooth(tl));
            score.SetText(Mathf.RoundToInt(48250f * Smooth(Window(local, 1.5f, 2.6f))).ToString("N0", System.Globalization.CultureInfo.InvariantCulture));
            Show(back, local);
            Show(impacts, local);
            Show(confetti, local);
        }
    }
}
