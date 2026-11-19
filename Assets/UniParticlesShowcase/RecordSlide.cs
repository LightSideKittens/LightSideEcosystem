using LightSide.Promo;
using UnityEditor;
using UnityEngine;
using static LightSide.ParticleShowcase.ReelKit;

namespace LightSide.ParticleShowcase
{
    /// <summary>NEW RECORD! slams in, then shatters into sparks of its own colors that rain down and bounce on the floor.</summary>
    public sealed class RecordSlide : ShotSlide
    {
        private const float Shatter = 1.9f;
        private UniParticles shards;
        private UniParticles back;
        private UnityEngine.UI.Image word;
        private RectTransform wordRect;
        private UniShape flash;
        private Vector2 size;

        protected override void OnBuild(Stage stage)
        {
            var root = stage.Root;
            Backdrop(root, new Color(0.3f, 0.08f, 0.22f), new Color(0.06f, 0.01f, 0.06f));
            back = Host(root, "Back", new Vector2(0f, 60f), new Vector2(400f, 400f));
            Rays(back.Effect, 903, 0.45f, Vector3.zero, 2000f, new Color(1f, 0.7f, 0.4f, 0.5f), 1.6f, 30f);
            Shock(back.Effect, 904, 0.45f, Vector3.zero, 1600f, new Color(1f, 0.85f, 0.6f, 1f), 0.5f);
            flash = Glow(root, "Flash", new Vector2(0f, 60f), 1800f, new Color(1f, 0.6f, 0.35f, 0.5f));
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ReelArt.Record);
            size = new Vector2(1500f, 1500f * sprite.rect.height / sprite.rect.width);
            wordRect = Node(root, "New Record", new Vector2(0f, 60f), size);
            word = wordRect.gameObject.AddComponent<UnityEngine.UI.Image>();
            word.sprite = sprite;
            word.raycastTarget = false;

            shards = Host(root, "Shards", new Vector2(0f, 60f), new Vector2(1920f, 1080f));
            ((RectTransform)shards.transform).anchoredPosition = Vector2.zero;
            var burst = Emitter(shards.Effect, 0, "Shards", 901, 1200);
            Once(burst, 1100f, Shatter);
            burst.Modules.Add(new ShapeModule
            {
                Kind = ParticleShapeKind.Image, Image = sprite, Size = new Vector3(size.x, size.y, 0f), Position = new Vector3(0f, 60f, 0f),
                Direction = ParticleDirection.Random,
            });
            burst.Modules.Add(new StartModule
            {
                Lifetime = new ParticleRange(4f, 5f), Speed = new ParticleRange(150f, 1100f), Size = new ParticleRange(22f, 34f),
                Spin = new ParticleRange(-400f, 400f), Rotation = new ParticleRange(0f, 360f),
            });
            burst.Modules.Add(new ForceModule { Gravity = new Vector3(0f, -1400f, 0f), Drag = 0.9f });
            burst.Modules.Add(new CollisionModule { RectWalls = ParticleRectWalls.Bottom, Bounce = 0.45f, Dampen = 0.2f });
            burst.Modules.Add(new SpinBySpeedModule { From = 0f, To = 1f, MinSpeed = 20f, MaxSpeed = 300f });
            burst.Modules.Add(new ColorOverLifeModule { Gradient = Fade(0f, 0.85f) });
            burst.Look = new ShapeLook { Outline = new ParticleOutline(ShapeKind.Triangle), Softness = 0.05f };

            var glints = Add(shards.Effect, "Glints", 902, 200);
            Once(glints, 120f, Shatter);
            glints.Modules.Add(new ShapeModule
            {
                Kind = ParticleShapeKind.Image, Image = sprite, Size = new Vector3(size.x, size.y, 0f), Position = new Vector3(0f, 60f, 0f),
                Direction = ParticleDirection.Random,
            });
            glints.Modules.Add(new StartModule
            {
                Lifetime = new ParticleRange(0.5f, 1f), Speed = new ParticleRange(300f, 1300f), Size = new ParticleRange(90f, 170f),
                Rotation = new ParticleRange(0f, 90f), ColorMode = ParticleColorMode.Solid, Color = Color.white,
            });
            glints.Modules.Add(new ForceModule { Gravity = Vector3.zero, Drag = 2.5f });
            glints.Modules.Add(new SizeOverLifeModule { From = 1f, To = 0f, Curve = Ease.Of(EasingType.QuadraticIn) });
            glints.Look = Painted("Flare");
            Flash(shards.Effect, 905, Shatter, new Vector3(0f, 60f, 0f), 1800f, new Color(1f, 0.85f, 0.6f, 1f), 0.5f);
            Wake(stage.Root);
        }

        protected override void OnRender(float local)
        {
            var slam = Ease.Of(EasingType.BackOut).Evaluate(Window(local, 0.1f, 0.5f));
            var shake = local > 0.5f && local < 0.9f ? Mathf.Sin(local * 90f) * 14f * (0.9f - local) : 0f;
            var tremble = local > Shatter - 0.6f && local < Shatter ? Mathf.Sin(local * 120f) * 5f * Window(local, Shatter - 0.6f, Shatter) : 0f;
            wordRect.sizeDelta = size * Mathf.Lerp(2.2f, 1f, slam);
            wordRect.anchoredPosition = new Vector2(shake + tremble, 60f);
            word.color = new Color(1f, 1f, 1f, local < Shatter ? Smooth(Window(local, 0.1f, 0.25f)) : 0f);
            flash.Color = new Color(1f, 1f, 1f, Mathf.Clamp01(Window(local, 0.4f, 0.5f) * (1f - Window(local, 0.5f, 1.4f)) + Window(local, Shatter - 0.05f, Shatter) * (1f - Window(local, Shatter, Shatter + 0.9f))));
            Show(shards, local);
            Show(back, local);
        }
    }
}
