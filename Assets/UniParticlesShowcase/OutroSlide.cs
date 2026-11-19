using System.Collections.Generic;
using LightSide.Promo;
using UnityEditor;
using UnityEngine;
using static LightSide.ParticleShowcase.ReelKit;

namespace LightSide.ParticleShowcase
{
    /// <summary>The end card: the wordmark breathes sparkles from its own pixels while the capabilities line up beneath it.</summary>
    public sealed class OutroSlide : ShotSlide
    {
        private static readonly string[] Points =
        {
            "Canvas and world", "GPU closed form", "Burst simulation", "Vector shapes", "Forms and trails", "16 presets",
        };

        private readonly List<CanvasGroup> chips = new();
        private readonly List<RectTransform> chipRects = new();
        private UniParticles aura;
        private UniParticles back;
        private UnityEngine.UI.Image mark;
        private UniText site;

        protected override void OnBuild(Stage stage)
        {
            chips.Clear();
            chipRects.Clear();
            var root = stage.Root;
            Backdrop(root, new Color(0.08f, 0.05f, 0.15f), new Color(0.01f, 0.01f, 0.03f));
            Glow(root, "Halo", new Vector2(0f, 120f), 1700f, new Color(0.6f, 0.4f, 1f, 0.3f));
            back = Host(root, "Back", new Vector2(0f, 130f), new Vector2(400f, 400f));
            Rays(back.Effect, 1003, 0f, Vector3.zero, 2100f, new Color(0.85f, 0.6f, 1f, 0.38f), 12f, 10f);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ReelArt.Wordmark);
            var size = new Vector2(1300f, 1300f * sprite.rect.height / sprite.rect.width);
            var markRect = Node(root, "Wordmark", new Vector2(0f, 130f), size);
            mark = markRect.gameObject.AddComponent<UnityEngine.UI.Image>();
            mark.sprite = sprite;
            mark.raycastTarget = false;

            aura = Host(root, "Aura", new Vector2(0f, 130f), size);
            var breath = Emitter(aura.Effect, 0, "Breath", 1001, 600);
            breath.Prewarm = true;
            breath.Modules.Add(new EmissionModule { Rate = 140f });
            breath.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Image, Image = sprite, Size = new Vector3(size.x, size.y, 0f), Direction = ParticleDirection.Fixed, DirectionVector = Vector3.up, Spread = 40f });
            breath.Modules.Add(new StartModule { Lifetime = new ParticleRange(1.2f, 2f), Speed = new ParticleRange(30f, 90f), Size = new ParticleRange(12f, 20f) });
            breath.Modules.Add(new ForceModule { Gravity = new Vector3(0f, 30f, 0f), Drag = 0.6f });
            breath.Modules.Add(new ColorOverLifeModule { Gradient = Fade(0.15f, 0.5f) });
            breath.Look = Glowing(0.7f);

            var twinkles = Emitter(aura.Effect, 1, "Twinkles", 1002, 100);
            twinkles.Prewarm = true;
            twinkles.Modules.Add(new EmissionModule { Rate = 14f });
            twinkles.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Image, Image = sprite, Size = new Vector3(size.x, size.y, 0f), Direction = ParticleDirection.Random });
            twinkles.Modules.Add(new StartModule
            {
                Lifetime = new ParticleRange(0.5f, 0.9f), Speed = new ParticleRange(0f, 8f), Size = new ParticleRange(90f, 160f),
                Rotation = new ParticleRange(0f, 90f), Spin = new ParticleRange(-90f, 90f), ColorMode = ParticleColorMode.Solid, Color = Color.white,
            });
            twinkles.Modules.Add(new SizeOverLifeModule { From = 1f, To = 0.1f, Curve = Ease.Of(EasingType.QuadraticIn) });
            twinkles.Look = Painted("Flare");

            const float gap = 18f;
            var widths = new float[Points.Length];
            var total = -gap;
            for (var i = 0; i < Points.Length; i++)
            {
                widths[i] = 64f + Points[i].Length * 13.5f;
                total += widths[i] + gap;
            }

            var x = -total * 0.5f;
            var colors = new[] { new Color(0.3f, 0.75f, 1f), new Color(0.75f, 0.5f, 1f), new Color(1f, 0.45f, 0.7f), new Color(1f, 0.65f, 0.3f), new Color(0.45f, 0.95f, 0.6f), new Color(1f, 0.85f, 0.35f) };
            for (var i = 0; i < Points.Length; i++)
            {
                var chip = Shape(root, "Chip " + Points[i], new Vector2(x + widths[i] * 0.5f, -230f), new Vector2(widths[i], 66f), ShapeKind.Capsule, 0f,
                    new Color(colors[i].r * 0.25f, colors[i].g * 0.25f, colors[i].b * 0.3f, 0.9f));
                Stroke(chip, colors[i], 3f);
                Text(chip.transform, Points[i], Vector2.zero, new Vector2(widths[i], 60f), 29f, Color.Lerp(colors[i], Color.white, 0.6f), "fira");
                chips.Add(chip.gameObject.AddComponent<CanvasGroup>());
                chipRects.Add((RectTransform)chip.transform);
                x += widths[i] + gap;
            }

            site = Text(root, "unity.lightside.media", new Vector2(0f, -380f), new Vector2(900f, 70f), 44f, new Color(1f, 1f, 1f, 0.7f), "fira");
            Wake(stage.Root);
        }

        protected override void OnRender(float local)
        {
            Show(aura, 2f + local);
            Show(back, local);
            mark.color = new Color(1f, 1f, 1f, Smooth(Window(local, 0f, 0.4f)));
            for (var i = 0; i < chips.Count; i++)
            {
                var t = Window(local, 0.5f + i * 0.14f, 0.85f + i * 0.14f);
                chips[i].alpha = Smooth(t);
                var p = chipRects[i].anchoredPosition;
                chipRects[i].anchoredPosition = new Vector2(p.x, -230f - 40f * (1f - Ease.Of(EasingType.BackOut).Evaluate(t)));
            }

            site.color = new Color(1f, 1f, 1f, 0.7f * Smooth(Window(local, 1.6f, 2.2f)));
        }
    }
}
