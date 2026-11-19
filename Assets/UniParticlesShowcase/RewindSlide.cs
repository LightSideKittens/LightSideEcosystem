using LightSide.Promo;
using UnityEngine;
using static LightSide.ParticleShowcase.ReelKit;

namespace LightSide.ParticleShowcase
{
    /// <summary>An explosion plays, pauses, rewinds to its first frame and plays again faster: exact seeking both ways.</summary>
    public sealed class RewindSlide : ShotSlide
    {
        private const float Span = 1.6f;
        private UniParticles blast;
        private UniParticles debrisHost;
        private RectTransform played;
        private RectTransform knob;
        private UniText mode;

        protected override void OnBuild(Stage stage)
        {
            var root = stage.Root;
            Backdrop(root, new Color(0.06f, 0.1f, 0.2f), new Color(0.01f, 0.02f, 0.05f));
            blast = Host(root, "Explosion", new Vector2(0f, 60f), new Vector2(400f, 400f));
            var sparks = Emitter(blast.Effect, 0, "Sparks", 91, 800);
            Once(sparks, 520f);
            sparks.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Circle, Radius = 10f, Direction = ParticleDirection.Random });
            sparks.Modules.Add(new StartModule
            {
                Lifetime = new ParticleRange(1.6f, 3f), Speed = new ParticleRange(250f, 1300f), Size = new ParticleRange(16f, 26f),
                ColorMode = ParticleColorMode.RandomFromGradient,
                Gradient = Ramp((0f, new Color(1f, 0.95f, 0.6f)), (0.5f, new Color(1f, 0.55f, 0.2f)), (1f, new Color(1f, 0.25f, 0.4f))),
            });
            sparks.Modules.Add(new ForceModule { Gravity = new Vector3(0f, -500f, 0f), Drag = 1.6f });
            sparks.Modules.Add(new SizeOverLifeModule { From = 1f, To = 0.3f });
            sparks.Modules.Add(new ColorOverLifeModule { Gradient = Fade(0f, 0.6f) });
            sparks.Modules.Add(new TrailModule { Length = 0.15f, Segments = 6, Width = 0.6f, TailWidth = 0.1f });
            sparks.Look = Glowing(0.5f);

            var ring = Emitter(blast.Effect, 1, "Glow", 95, 4);
            Once(ring, 1f);
            ring.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Point });
            ring.Modules.Add(new StartModule { Lifetime = new ParticleRange(0.6f), Speed = new ParticleRange(0f), Size = new ParticleRange(1100f), Color = new Color(1f, 0.8f, 0.5f) });
            ring.Modules.Add(new SizeOverLifeModule { From = 0.05f, To = 1f, Curve = Ease.Of(EasingType.CubicOut) });
            ring.Modules.Add(new ColorOverLifeModule { Gradient = Ramp((0f, Color.white), (1f, new Color(1f, 1f, 1f, 0f))) });
            ring.Look = Glowing(1f);
            Shock(blast.Effect, 92, 0f, Vector3.zero, 1300f, new Color(1f, 0.85f, 0.6f, 1f), 0.8f);
            Flares(blast.Effect, 94, 0f, Vector3.zero, 8f, 240f, 1500f, new Color(1f, 0.92f, 0.7f));

            debrisHost = Host(root, "Debris", new Vector2(0f, 60f), new Vector2(400f, 400f));
            var debris = Emitter(debrisHost.Effect, 0, "Debris", 93, 200);
            Once(debris, 70f);
            debris.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Circle, Radius = 20f, Direction = ParticleDirection.Shape });
            debris.Modules.Add(new StartModule
            {
                Lifetime = new ParticleRange(3f, 3.4f), Speed = new ParticleRange(350f, 1000f), Size = new ParticleRange(40f, 64f),
                Spin = new ParticleRange(-500f, 500f), Rotation = new ParticleRange(0f, 360f), Aspect = new ParticleRange(0.5f, 1f),
                Color = Color.white,
            });
            debris.Modules.Add(new ForceModule { Gravity = new Vector3(0f, -900f, 0f), Drag = 0.5f });
            debris.Look = Painted("Star", LayerBlend.Normal);

            Shape(root, "Track", new Vector2(0f, -430f), new Vector2(1200f, 10f), ShapeKind.Capsule, 0f, new Color(1f, 1f, 1f, 0.15f));
            played = (RectTransform)Shape(root, "Played", new Vector2(-600f, -430f), new Vector2(10f, 10f), ShapeKind.Capsule, 0f, new Color(1f, 0.7f, 0.3f)).transform;
            knob = (RectTransform)Shape(root, "Knob", new Vector2(-600f, -430f), new Vector2(30f, 30f), ShapeKind.Circle, 0f, Color.white).transform;
            mode = Text(root, "PLAY", new Vector2(0f, -370f), new Vector2(600f, 60f), 42f, new Color(1f, 1f, 1f, 0.85f), "fira");
            Outline(Text(root, "Seek anywhere. Play backward.", new Vector2(0f, 430f), new Vector2(1400f, 90f), 64f, Color.white), new Color(0.02f, 0.03f, 0.08f), 0.07f);
            Wake(stage.Root);
        }

        protected override void OnRender(float local)
        {
            float head;
            string label;
            if (local < Span) { head = local; label = "▶  PLAY"; }
            else if (local < Span + 0.4f) { head = Span; label = "❚❚  PAUSE"; }
            else if (local < Span + 2.4f) { head = Span * (1f - Smooth(Window(local, Span + 0.4f, Span + 2.4f))); label = "◀◀  REWIND"; }
            else if (local < Span + 2.7f) { head = 0f; label = "❚❚  PAUSE"; }
            else { head = Mathf.Min(Span, (local - Span - 2.7f) * 1.5f); label = "▶  PLAY  ×1.5"; }
            Show(blast, head);
            Show(debrisHost, head);
            var x = -600f + 1200f * head / Span;
            played.sizeDelta = new Vector2(Mathf.Max(10f, x + 600f), 10f);
            played.anchoredPosition = new Vector2(-600f + Mathf.Max(10f, x + 600f) * 0.5f, -430f);
            knob.anchoredPosition = new Vector2(x, -430f);
            mode.SetText(label);
        }
    }
}
