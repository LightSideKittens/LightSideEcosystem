using LightSide.Promo;
using UnityEngine;
using static LightSide.ParticleShowcase.ReelKit;

namespace LightSide.ParticleShowcase
{
    /// <summary>A campfire at night: flames, embers whose lights flicker over the stones, smoke drifting off; the camera circles slowly.</summary>
    public sealed class CampfireSlide : WorldSlide
    {
        private UniParticlesWorld fire;
        private UniText caption;

        protected override void BuildWorld(Stage stage)
        {
            Eye.backgroundColor = new Color(0.012f, 0.014f, 0.03f);
            Eye.fieldOfView = 40f;
            Primitive(PrimitiveType.Plane, Vector3.zero, new Vector3(3f, 1f, 3f), Lit(new Color(0.3f, 0.24f, 0.2f), 0.1f));
            var stone = Lit(new Color(0.46f, 0.45f, 0.47f), 0.35f);
            for (var i = 0; i < 9; i++)
            {
                var angle = i / 9f * Mathf.PI * 2f;
                Primitive(PrimitiveType.Sphere, new Vector3(Mathf.Cos(angle) * 0.62f, 0.05f, Mathf.Sin(angle) * 0.62f),
                    new Vector3(0.28f, 0.18f + 0.04f * Mathf.Sin(i * 1.7f), 0.24f), stone, Quaternion.Euler(0f, i * 40f, 0f));
            }

            var wood = Lit(new Color(0.34f, 0.2f, 0.11f), 0.15f);
            foreach (var turn in new[] { 20f, 140f, 260f })
                Primitive(PrimitiveType.Cylinder, new Vector3(0f, 0.1f, 0f), new Vector3(0.1f, 0.42f, 0.1f), wood, Quaternion.Euler(0f, turn, 75f));

            var lightObject = new GameObject("Ember Light", typeof(Light));
            lightObject.transform.SetParent(World, false);
            var template = lightObject.GetComponent<Light>();
            template.type = LightType.Point;
            template.range = 1.7f;
            template.intensity = 2.4f;
            template.enabled = false;

            fire = Host("Fire", Vector3.zero);
            var flames = Emitter(fire.Effect, 0, "Flames", 131, 300);
            flames.Prewarm = true;
            flames.Modules.Add(new EmissionModule { Rate = 70f });
            flames.Modules.Add(new ShapeModule
            {
                Kind = ParticleShapeKind.Circle, Radius = 0.22f, Rotation = new Vector3(90f, 0f, 0f), Position = new Vector3(0f, 0.12f, 0f),
                Direction = ParticleDirection.Fixed, DirectionVector = Vector3.up, Spread = 10f,
            });
            flames.Modules.Add(new StartModule
            {
                Lifetime = new ParticleRange(0.5f, 0.9f), Speed = new ParticleRange(0.6f, 1.2f), Size = new ParticleRange(0.3f, 0.45f),
                Rotation = new ParticleRange(0f, 360f), Spin = new ParticleRange(-90f, 90f), Color = new Color(1f, 0.75f, 0.35f, 0.9f),
            });
            flames.Modules.Add(new ForceModule { Gravity = new Vector3(0f, 0.8f, 0f), Drag = 0.8f });
            flames.Modules.Add(new SizeOverLifeModule { From = 1f, To = 0.15f, Curve = Ease.Of(EasingType.QuadraticIn) });
            flames.Modules.Add(new ColorOverLifeModule
            {
                Gradient = Ramp((0f, new Color(1f, 1f, 0.85f, 0f)), (0.12f, new Color(1f, 0.9f, 0.6f, 1f)), (0.5f, new Color(1f, 0.45f, 0.12f, 0.8f)), (1f, new Color(0.7f, 0.1f, 0.05f, 0f))),
            });
            flames.Look = new ShapeLook { Outline = new ParticleOutline(ShapeKind.Circle), Softness = 1f, Blend = LayerBlend.Additive, Facing = ParticleFacing.Camera };

            var embers = Emitter(fire.Effect, 1, "Embers", 132, 300);
            embers.Prewarm = true;
            embers.Modules.Add(new EmissionModule { Rate = 36f });
            embers.Modules.Add(new ShapeModule
            {
                Kind = ParticleShapeKind.Circle, Radius = 0.3f, Rotation = new Vector3(90f, 0f, 0f), Position = new Vector3(0f, 0.15f, 0f),
                Direction = ParticleDirection.Fixed, DirectionVector = Vector3.up, Spread = 20f,
            });
            embers.Modules.Add(new StartModule
            {
                Lifetime = new ParticleRange(1.6f, 3f), Speed = new ParticleRange(0.9f, 2f), Size = new ParticleRange(0.05f, 0.09f),
                ColorMode = ParticleColorMode.BetweenTwo, Color = new Color(1f, 0.9f, 0.45f), SecondColor = new Color(1f, 0.5f, 0.12f),
            });
            embers.Modules.Add(new ForceModule { Gravity = new Vector3(0f, 0.4f, 0f), Drag = 0.7f });
            embers.Modules.Add(new NoiseModule { Strength = 2f, Scale = 0.8f, Drift = 0.9f, Octaves = 2 });
            embers.Modules.Add(new ColorOverLifeModule { Gradient = Ramp((0f, Color.white), (0.6f, new Color(1f, 0.6f, 0.35f)), (1f, new Color(0.9f, 0.2f, 0.1f, 0f))) });
            embers.Modules.Add(new LightsModule { Light = template, MaxLights = 6, Ratio = 0.3f, SizeAffectsRange = false });
            embers.Look = new ShapeLook { Outline = new ParticleOutline(ShapeKind.Circle), Softness = 0.5f, Blend = LayerBlend.Additive, Facing = ParticleFacing.Camera };

            var smoke = Emitter(fire.Effect, 2, "Smoke", 133, 120);
            smoke.Prewarm = true;
            smoke.Modules.Add(new EmissionModule { Rate = 9f });
            smoke.Modules.Add(new ShapeModule
            {
                Kind = ParticleShapeKind.Circle, Radius = 0.15f, Rotation = new Vector3(90f, 0f, 0f), Position = new Vector3(0f, 0.75f, 0f),
                Direction = ParticleDirection.Fixed, DirectionVector = Vector3.up, Spread = 12f,
            });
            smoke.Modules.Add(new StartModule
            {
                Lifetime = new ParticleRange(4f, 5.5f), Speed = new ParticleRange(0.35f, 0.6f), Size = new ParticleRange(0.45f, 0.7f),
                Rotation = new ParticleRange(0f, 360f), Spin = new ParticleRange(-15f, 15f), Color = new Color(0.55f, 0.57f, 0.63f, 0.5f),
            });
            smoke.Modules.Add(new VelocityModule { Velocity = new Vector3(0.18f, 0f, 0.05f) });
            smoke.Modules.Add(new SizeOverLifeModule { From = 0.6f, To = 2.6f, Curve = Ease.Of(EasingType.QuadraticOut) });
            smoke.Modules.Add(new ColorOverLifeModule { Gradient = Fade(0.2f, 0.4f) });
            smoke.Look = Painted("Smoke", LayerBlend.Normal);
            ((ShapeLook)smoke.Look).Facing = ParticleFacing.Camera;

            var glow = Emitter(fire.Effect, 3, "Glow", 134, 16);
            glow.Prewarm = true;
            glow.Modules.Add(new EmissionModule { Rate = 6f });
            glow.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Sphere, Radius = 0.05f, Position = new Vector3(0f, 0.3f, 0f) });
            glow.Modules.Add(new StartModule { Lifetime = new ParticleRange(0.8f, 1.2f), Speed = new ParticleRange(0f, 0.1f), Size = new ParticleRange(1.5f, 1.9f), Color = new Color(1f, 0.5f, 0.15f, 0.16f) });
            glow.Modules.Add(new ForceModule { Gravity = Vector3.zero });
            glow.Modules.Add(new ColorOverLifeModule { Gradient = Fade(0.3f, 0.6f) });
            glow.Look = new ShapeLook { Outline = new ParticleOutline(ShapeKind.Circle), Softness = 1f, Blend = LayerBlend.Additive, Facing = ParticleFacing.Camera };

            caption = Outline(Text(stage.Root, "And in the world — lit by its own sparks.", new Vector2(0f, 420f), new Vector2(1600f, 90f), 62f, Color.white),
                new Color(0.05f, 0.02f, 0.02f), 0.06f);
        }

        protected override void PoseWorld(float local)
        {
            var a = -0.3f + local * 0.07f;
            Eye.transform.localPosition = new Vector3(Mathf.Sin(a) * 4.3f, 1.5f, -Mathf.Cos(a) * 4.3f);
            Eye.transform.LookAt(World.TransformPoint(new Vector3(0f, 0.7f, 0f)));
            Show(fire, 2f + local);
            caption.color = new Color(1f, 1f, 1f, Smooth(Window(local, 0.4f, 1f)));
        }
    }
}
