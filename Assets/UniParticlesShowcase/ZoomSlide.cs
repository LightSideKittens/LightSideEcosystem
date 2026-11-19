using LightSide.Promo;
using UnityEngine;
using static LightSide.ParticleShowcase.ReelKit;

namespace LightSide.ParticleShowcase
{
    /// <summary>Particle shapes under a camera diving a hundred and fifty times into a star's tip: every edge stays one pixel sharp.</summary>
    public sealed class ZoomSlide : WorldSlide
    {
        private const float Start = 2.6f;
        private const float Depth = 150f;
        private static readonly Vector3 Tip = new(0f, 1.03f, 0f);
        private UniParticlesWorld shapes;
        private UniText caption;
        private UniText factor;

        protected override void BuildWorld(Stage stage)
        {
            Eye.backgroundColor = new Color(0.03f, 0.035f, 0.07f);
            Eye.orthographic = true;
            Eye.orthographicSize = Start;
            shapes = Host("Shapes", Vector3.zero);
            var items = new[]
            {
                (new ParticleOutline(ShapeKind.Star) { starPoints = 5, starSharpness = 0.5f }, new Color(1f, 0.8f, 0.25f), new Vector3(0f, 0f, 0f), 2.2f, 0f, 0f),
                (new ParticleOutline(ShapeKind.Heart), new Color(1f, 0.35f, 0.5f), new Vector3(-3f, 0.8f, 0f), 1.3f, 12f, 8f),
                (new ParticleOutline(ShapeKind.RoundedRect) { cornerRadius = 0.4f }, new Color(0.35f, 0.75f, 1f), new Vector3(3f, -0.6f, 0f), 1.2f, -15f, -10f),
                (new ParticleOutline(ShapeKind.Ring) { thickness = 0.2f }, new Color(0.5f, 1f, 0.6f), new Vector3(2.8f, 1.4f, 0f), 0.9f, 0f, 0f),
                (new ParticleOutline(ShapeKind.Star) { starPoints = 4, starSharpness = 0.8f }, Color.white, new Vector3(-2.6f, -1.3f, 0f), 0.8f, 20f, 25f),
                (new ParticleOutline(ShapeKind.Hexagon), new Color(0.75f, 0.5f, 1f), new Vector3(-0.9f, -1.6f, 0f), 0.7f, 0f, -12f),
            };
            for (var i = 0; i < items.Length; i++)
            {
                var (outline, color, at, size, turn, spin) = items[i];
                var emitter = Emitter(shapes.Effect, i, outline.shape.ToString(), (uint)(120 + i), 4);
                Once(emitter, 1f);
                emitter.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Point, Position = at });
                emitter.Modules.Add(new StartModule
                {
                    Lifetime = new ParticleRange(100f), Speed = new ParticleRange(0f), Size = new ParticleRange(size), Rotation = new ParticleRange(turn),
                    Spin = new ParticleRange(spin), Color = color,
                });
                emitter.Modules.Add(new ForceModule { Gravity = Vector3.zero });
                emitter.Look = new ShapeLook { Outline = outline };
            }

            caption = Outline(Text(stage.Root, "Vector particles: sharp at any size.", new Vector2(0f, -440f), new Vector2(1600f, 90f), 60f, Color.white),
                new Color(0.02f, 0.02f, 0.06f), 0.07f);
            factor = Outline(Text(stage.Root, "1×", new Vector2(760f, 440f), new Vector2(300f, 90f), 64f, new Color(1f, 0.85f, 0.35f)), new Color(0.15f, 0.08f, 0f), 0.08f);
        }

        protected override void PoseWorld(float local)
        {
            var k = Smooth(Window(local, 0.5f, 3.6f));
            var zoom = Mathf.Exp(Mathf.Lerp(0f, Mathf.Log(Depth), k));
            Eye.orthographicSize = Start / zoom;
            var focus = Vector3.Lerp(Vector3.zero, Tip, Smooth(Window(local, 0.2f, 2.2f)));
            Eye.transform.localPosition = new Vector3(focus.x, focus.y, -10f);
            Eye.transform.localRotation = Quaternion.identity;
            Show(shapes, local);
            factor.SetText(Mathf.RoundToInt(zoom) + "×");
            caption.color = new Color(1f, 1f, 1f, Smooth(Window(local, 0.3f, 0.9f)));
        }
    }
}
