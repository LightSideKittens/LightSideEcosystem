using LightSide.Promo;
using UnityEngine;
using static LightSide.ParticleShowcase.ReelKit;

namespace LightSide.ParticleShowcase
{
    /// <summary>A finger draws a glowing ribbon across the screen — births by distance through one connected ribbon — shedding sparkles.</summary>
    public sealed class RibbonSlide : ShotSlide
    {
        private UniParticles brush;
        private RectTransform brushRect;
        private RectTransform tip;

        private static Vector2 Path(float t)
        {
            var a = t * 1.3f;
            return new Vector2(Mathf.Sin(a) * 640f + Mathf.Sin(a * 2.3f) * 120f, Mathf.Sin(a * 2f + 0.6f) * 300f + Mathf.Cos(a * 3.1f) * 60f);
        }

        protected override void OnBuild(Stage stage)
        {
            var root = stage.Root;
            Backdrop(root, new Color(0.04f, 0.08f, 0.17f), new Color(0.01f, 0.01f, 0.03f));
            Outline(Text(root, "Draw with light.", new Vector2(0f, 420f), new Vector2(1200f, 100f), 72f, new Color(1f, 1f, 1f, 0.9f)), new Color(0.02f, 0.05f, 0.15f), 0.06f);

            brush = Host(root, "Brush", Path(0f), new Vector2(10f, 10f));
            brushRect = (RectTransform)brush.transform;
            var line = Emitter(brush.Effect, 0, "Ribbon", 111, 400);
            line.Space = ParticleSpace.World;
            line.Duration = 3f;
            line.Modules.Add(new EmissionModule { Rate = 0f, RateOverDistance = 0.12f });
            line.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Point });
            line.Modules.Add(new StartModule
            {
                Lifetime = new ParticleRange(1.1f), Speed = new ParticleRange(0f), Size = new ParticleRange(46f), ColorMode = ParticleColorMode.OverDuration,
                Gradient = Ramp((0f, new Color(0.3f, 0.85f, 1f)), (0.33f, new Color(0.75f, 0.45f, 1f)), (0.66f, new Color(1f, 0.4f, 0.7f)), (1f, new Color(0.3f, 0.85f, 1f))),
            });
            line.Modules.Add(new SizeOverLifeModule { From = 1f, To = 0f, Curve = Ease.Of(EasingType.QuadraticIn) });
            line.Modules.Add(new ColorOverLifeModule { Gradient = Ramp((0f, Color.white), (1f, new Color(1f, 1f, 1f, 0f))) });
            line.Modules.Add(new RibbonModule { Width = 0.9f, ShowParticles = false });
            line.Look = new ShapeLook { Outline = new ParticleOutline(ShapeKind.Circle), Softness = 0.4f, Blend = LayerBlend.Additive };

            var dust = Emitter(brush.Effect, 1, "Sparkles", 112, 300);
            dust.Space = ParticleSpace.World;
            dust.Modules.Add(new EmissionModule { Rate = 0f, RateOverDistance = 0.05f });
            dust.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Circle, Radius = 14f, Direction = ParticleDirection.Random });
            dust.Modules.Add(new StartModule
            {
                Lifetime = new ParticleRange(0.6f, 1.1f), Speed = new ParticleRange(20f, 120f), Size = new ParticleRange(50f, 90f),
                Rotation = new ParticleRange(0f, 90f), Spin = new ParticleRange(-120f, 120f), Color = new Color(1f, 0.95f, 0.85f),
            });
            dust.Modules.Add(new ForceModule { Gravity = new Vector3(0f, -120f, 0f), Drag = 1f });
            dust.Modules.Add(new SizeOverLifeModule { From = 1f, To = 0f });
            dust.Look = Painted("Flare");

            var dot = Shape(root, "Tip", Path(0f), new Vector2(30f, 30f), ShapeKind.Circle, 0f, Color.white);
            Shadow(dot, new Color(0.4f, 0.8f, 1f, 0.85f), Vector2.zero, 26f, 4f);
            tip = (RectTransform)dot.transform;
            Wake(stage.Root);
        }

        protected override void OnRender(float local)
        {
            Replay(brush, local, t => brushRect.anchoredPosition = Path(t));
            tip.anchoredPosition = Path(local);
        }
    }
}
