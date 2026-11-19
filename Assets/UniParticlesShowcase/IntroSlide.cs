using LightSide.Promo;
using UnityEditor;
using UnityEngine;
using static LightSide.ParticleShowcase.ReelKit;

namespace LightSide.ParticleShowcase
{
    /// <summary>Sparks swirl in from every side and settle into the UniParticles wordmark in its own colors; the mark takes over and the promise follows.</summary>
    public sealed class IntroSlide : ShotSlide
    {
        private UniParticles sparks;
        private UniParticles back;
        private UnityEngine.UI.Image mark;
        private UniText promise;
        private UniShape halo;

        protected override void OnBuild(Stage stage)
        {
            var root = stage.Root;
            Backdrop(root, new Color(0.07f, 0.05f, 0.14f), new Color(0.01f, 0.01f, 0.03f));
            halo = Glow(root, "Halo", new Vector2(0f, 40f), 1700f, new Color(0.55f, 0.35f, 1f, 0.32f));
            back = Host(root, "Back", new Vector2(0f, 40f), new Vector2(400f, 400f));
            Rays(back.Effect, 503, 2.45f, Vector3.zero, 2000f, new Color(0.85f, 0.6f, 1f, 0.45f), 4f, 12f);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ReelArt.Wordmark);
            var size = new Vector2(1500f, 1500f * sprite.rect.height / sprite.rect.width);

            var markRect = Node(root, "Wordmark", new Vector2(0f, 40f), size);
            mark = markRect.gameObject.AddComponent<UnityEngine.UI.Image>();
            mark.sprite = sprite;
            mark.raycastTarget = false;

            sparks = Host(root, "Sparks", new Vector2(0f, 40f), new Vector2(1920f, 1080f));
            var swirl = Emitter(sparks.Effect, 0, "Swirl", 501, 1200);
            Once(swirl, 1100f);
            swirl.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Circle, Radius = 1150f, Thickness = 0.35f, Direction = ParticleDirection.Random });
            swirl.Modules.Add(new StartModule { Lifetime = new ParticleRange(6f), Speed = new ParticleRange(60f, 220f), Size = new ParticleRange(20f, 26f) });
            swirl.Modules.Add(new ForceModule { Gravity = Vector3.zero, Orbital = 0.9f, Drag = 0.2f });
            swirl.Modules.Add(new FormModule
            {
                Target = ParticleFormTarget.Image, Image = sprite, Size = size,
                Gather = new Vector2(0.03f, 0.42f), Release = new Vector2(1f, 1f), Curve = Ease.Of(EasingType.CubicInOut),
            });
            swirl.Modules.Add(new ColorOverLifeModule { Gradient = Ramp((0f, new Color(1f, 1f, 1f, 0f)), (0.08f, Color.white), (1f, Color.white)) });
            swirl.Look = new ShapeLook { Outline = new ParticleOutline(ShapeKind.Circle), Softness = 0.25f };

            Flash(sparks.Effect, 504, 2.5f, Vector3.zero, 1600f, new Color(1f, 0.9f, 1f, 0.9f), 0.6f);
            var glints = Add(sparks.Effect, "Glints", 502, 200);
            glints.Looping = false;
            glints.Duration = 3f;
            glints.StartDelay = 2.4f;
            glints.Modules.Add(new EmissionModule { Rate = 40f });
            glints.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Image, Image = sprite, Size = new Vector3(size.x, size.y, 0f), Direction = ParticleDirection.Random });
            glints.Modules.Add(new StartModule
            {
                Lifetime = new ParticleRange(0.5f, 0.9f), Speed = new ParticleRange(0f, 10f), Size = new ParticleRange(80f, 150f),
                Rotation = new ParticleRange(0f, 90f), Spin = new ParticleRange(-90f, 90f), ColorMode = ParticleColorMode.Solid, Color = Color.white,
            });
            glints.Modules.Add(new SizeOverLifeModule { From = 1f, To = 0.1f, Curve = Ease.Of(EasingType.QuadraticIn) });
            glints.Modules.Add(new ColorOverLifeModule { Gradient = Fade(0.2f, 0.4f) });
            glints.Look = Painted("Flare");

            promise = Text(root, "Particles that belong in your UI — and in your world", new Vector2(0f, -300f), new Vector2(1700f, 80f), 52f,
                new Color(1f, 1f, 1f, 0f), "fira");
            Wake(stage.Root);
        }

        protected override void OnRender(float local)
        {
            Show(sparks, local);
            Show(back, local);
            var takeOver = Smooth(Window(local, 2.5f, 3.1f));
            mark.color = new Color(1f, 1f, 1f, takeOver);
            sparks.Color = new Color(1f, 1f, 1f, 1f - Smooth(Window(local, 2.9f, 3.4f)));
            halo.Color = new Color(1f, 1f, 1f, 0.35f + 0.65f * takeOver);
            promise.color = new Color(1f, 1f, 1f, 0.85f * Smooth(Window(local, 3.3f, 3.9f)));
            ((RectTransform)promise.transform).anchoredPosition = new Vector2(0f, -300f - 24f * (1f - Smooth(Window(local, 3.3f, 3.9f))));
        }
    }
}
