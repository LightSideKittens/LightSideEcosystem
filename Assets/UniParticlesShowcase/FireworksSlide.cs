using System.Collections.Generic;
using LightSide.Promo;
using UnityEditor;
using UnityEngine;
using static LightSide.ParticleShowcase.ReelKit;

namespace LightSide.ParticleShowcase
{
    /// <summary>Fireworks over a city at night: three copies of the Fireworks preset, every burst evaluated by the vertex shader.</summary>
    public sealed class FireworksSlide : ShotSlide
    {
        private readonly List<UniParticles> shows = new();
        private UniText caption;

        protected override void OnBuild(Stage stage)
        {
            shows.Clear();
            var root = stage.Root;
            Backdrop(root, new Color(0.1f, 0.06f, 0.22f), new Color(0.01f, 0.01f, 0.04f));
            Glow(root, "Horizon", new Vector2(0f, -560f), 2400f, new Color(0.9f, 0.35f, 0.55f, 0.32f));
            var random = new System.Random(7);
            var x = -980f;
            while (x < 980f)
            {
                var w = 60f + (float)random.NextDouble() * 110f;
                var h = 80f + (float)random.NextDouble() * 260f;
                var building = Shape(root, "Building", new Vector2(x + w * 0.5f, -540f + h * 0.5f), new Vector2(w, h), ShapeKind.RoundedRect, 4f, new Color(0.03f, 0.03f, 0.07f));
                for (var k = 0; k < 6; k++)
                {
                    if (random.NextDouble() > 0.55) continue;
                    var wx = ((float)random.NextDouble() - 0.5f) * (w - 20f);
                    var wy = ((float)random.NextDouble() - 0.5f) * (h - 30f);
                    Shape(building.transform, "Window", new Vector2(wx, wy), new Vector2(8f, 10f), ShapeKind.RoundedRect, 2f, new Color(1f, 0.82f, 0.45f, 0.75f));
                }

                x += w + 6f;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/media.lightside.uniparticles/Defaults/Presets/Fireworks.prefab");
            uint seed = 300;
            foreach (var at in new[] { new Vector2(-560f, 80f), new Vector2(0f, 150f), new Vector2(560f, 80f) })
            {
                var copy = Object.Instantiate(prefab, root, false);
                copy.SetActive(false);
                ((RectTransform)copy.transform).anchoredPosition = at;
                var host = copy.GetComponent<UniParticles>();
                host.Effect.Clock = PlaybackClock.Manual;
                host.Effect.PlayOnEnable = false;
                foreach (var emitter in host.Effect.Emitters)
                {
                    emitter.Seed = seed++;
                    emitter.Looping = true;
                }
                shows.Add(host);
            }

            caption = Outline(Text(root, "A whole fireworks show on the GPU — zero CPU per frame.", new Vector2(0f, 440f), new Vector2(1700f, 80f), 56f, Color.white),
                new Color(0.03f, 0.02f, 0.08f), 0.07f);
            Wake(stage.Root);
        }

        protected override void OnRender(float local)
        {
            for (var i = 0; i < shows.Count; i++) Show(shows[i], 1.2f + local + i * 0.37f);
            caption.color = new Color(1f, 1f, 1f, Smooth(Window(local, 0.5f, 1.1f)));
        }
    }
}
