using System;
using UnityEditor;
using UnityEngine;
using Gradient = LightSide.Gradient;

namespace LightSide.ParticleShowcase
{
    /// <summary>The building blocks of the showcase's shots: game-styled shapes and text, and particle hosts posed by time.</summary>
    public static class ReelKit
    {
        /// <summary>The festive palette of every confetti shot, one color per piece.</summary>
        public static Gradient Festive() => Palette(
            new Color(1f, 0.32f, 0.56f), new Color(1f, 0.8f, 0.22f), new Color(0.2f, 0.82f, 1f),
            new Color(0.62f, 0.42f, 1f), new Color(0.32f, 0.92f, 0.62f), new Color(1f, 0.5f, 0.32f));

        public static Gradient Palette(params Color[] colors)
        {
            var stops = new GradientStop[colors.Length];
            for (var i = 0; i < colors.Length; i++) stops[i] = new GradientStop(i / (float)colors.Length, colors[i]);
            return new Gradient(stops, GradientInterpolation.Stepped);
        }

        public static Gradient Ramp(params (float at, Color color)[] stops)
            => new(Array.ConvertAll(stops, stop => new GradientStop(stop.at, stop.color)), GradientInterpolation.Perceptual);

        /// <summary>White fading in over the first <paramref name="fadeIn"/> of a life and out after <paramref name="fadeOut"/>.</summary>
        public static Gradient Fade(float fadeIn, float fadeOut)
            => Ramp((0f, new Color(1f, 1f, 1f, fadeIn > 0f ? 0f : 1f)), (Mathf.Max(fadeIn, 0.001f), Color.white), (fadeOut, Color.white), (1f, new Color(1f, 1f, 1f, 0f)));

        public static UniTextFont Font(string name)
            => AssetDatabase.LoadAssetAtPath<UniTextFont>(name switch
               {
                   "lilita" => "Assets/Fonts/Promo/lilitaone/LilitaOne-Regular.asset",
                   _ => "Assets/Fonts/Promo/FiraSans-ExtraBold.asset",
               }) ?? throw new InvalidOperationException($"Font {name} missing.");

        public static RectTransform Node(Transform parent, string name, Vector2 at, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = 5 };
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchoredPosition = at;
            rect.sizeDelta = size;
            return rect;
        }

        public static UniShape Shape(Transform parent, string name, Vector2 at, Vector2 size, ShapeKind kind, float radius, Color color)
        {
            var shape = Node(parent, name, at, size).gameObject.AddComponent<UniShape>();
            shape.Shape = new PrimitiveShapeProvider { Kind = kind, Radius = radius, Smoothing = 0.6f };
            shape.RaycastTarget = false;
            shape.Layers.Clear();
            var fill = new FillLayer();
            fill.Paint.Kind = PaintSourceKind.Solid;
            fill.Paint.Color = color;
            shape.Layers.Add(fill);
            return shape;
        }

        private static ShapePaint Fill(UniShape shape)
        {
            foreach (var layer in shape.Layers)
                if (layer is FillLayer fill)
                    return fill.Paint;
            throw new InvalidOperationException($"{shape.name} has no fill.");
        }

        public static UniShape Vertical(UniShape shape, Color top, Color bottom)
        {
            var paint = Fill(shape);
            paint.Kind = PaintSourceKind.Gradient;
            paint.Dither = true;
            paint.Color = Color.white;
            paint.ProjectionKind = PaintProjectionKind.Linear;
            paint.Angle = 90f;
            paint.Scale = 1f;
            paint.Gradient = new Gradient(new[] { new GradientStop(0f, bottom), new GradientStop(1f, top) }, GradientInterpolation.Perceptual);
            return shape;
        }

        public static UniShape Radial(UniShape shape, Color inner, Color outer, float scale = 1f)
        {
            var paint = Fill(shape);
            paint.Kind = PaintSourceKind.Gradient;
            paint.Dither = true;
            paint.Color = Color.white;
            paint.ProjectionKind = PaintProjectionKind.Radial;
            paint.Angle = 0f;
            paint.Scale = 1f / scale;
            paint.Gradient = new Gradient(new[] { new GradientStop(0f, inner), new GradientStop(1f, outer) }, GradientInterpolation.Perceptual);
            return shape;
        }

        public static StrokeLayer Stroke(UniShape shape, Color color, float width, float align = -1f)
        {
            var layer = new StrokeLayer { Width = width, Align = align };
            layer.Paint.Kind = PaintSourceKind.Solid;
            layer.Paint.Color = color;
            shape.Layers.Add(layer);
            return layer;
        }

        public static ShadowLayer Shadow(UniShape shape, Color color, Vector2 offset, float blur, float spread = 0f)
        {
            var layer = new ShadowLayer { Offset = offset, Blur = blur, Spread = spread };
            layer.Paint.Kind = PaintSourceKind.Solid;
            layer.Paint.Color = color;
            shape.Layers.Insert(0, layer);
            return layer;
        }

        /// <summary>A soft radial glow: a circle whose color fades to nothing at its edge.</summary>
        public static UniShape Glow(Transform parent, string name, Vector2 at, float diameter, Color color)
            => Radial(Shape(parent, name, at, new Vector2(diameter, diameter), ShapeKind.Circle, 0f, Color.white),
                color, new Color(color.r, color.g, color.b, 0f));

        /// <summary>A full-frame backdrop of 1920 × 1080 units with a vertical gradient.</summary>
        public static UniShape Backdrop(Transform parent, Color top, Color bottom)
            => Vertical(Shape(parent, "Backdrop", Vector2.zero, new Vector2(1920f, 1080f), ShapeKind.RoundedRect, 0f, bottom), top, bottom);

        public static UniText Text(Transform parent, string text, Vector2 at, Vector2 size, float fontSize, Color color, string face = "lilita")
        {
            var label = Node(parent, "Text " + text, at, size).gameObject.AddComponent<UniText>();
            label.raycastTarget = false;
            label.Font = Font(face);
            label.FontSize = fontSize;
            label.color = color;
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Middle;
            label.Text = text;
            return label;
        }

        public static UniText Outline(UniText label, Color color, float em)
        {
            label.Styles.Add(Style.WholeText(new StrokeModifier { Paint = PaintRef.Solid(color), Width = UnitValue.Em(em), Align = 1f }));
            return label;
        }

        /// <summary>
        /// A Canvas particle host on the manual clock, so only <see cref="UniParticles.Seek"/> and
        /// <see cref="UniParticles.Simulate"/> move it; its first emitter emptied of modules.
        /// </summary>
        public static UniParticles Host(Transform parent, string name, Vector2 at, Vector2 size)
        {
            var rect = Node(parent, name, at, size);
            rect.gameObject.SetActive(false);
            var host = rect.gameObject.AddComponent<UniParticles>();
            host.Effect.Clock = PlaybackClock.Manual;
            host.Effect.PlayOnEnable = false;
            host.Effect.Emitters[0].Modules.Clear();
            return host;
        }

        public static ParticleEmitter Emitter(ParticleEffect effect, int index, string name, uint seed, int capacity)
        {
            ParticleEmitter emitter;
            if (index < effect.Emitters.Count) emitter = effect.Emitters[index];
            else
            {
                emitter = new ParticleEmitter();
                effect.Emitters.Add(emitter);
            }

            emitter.Modules.Clear();
            emitter.Name = name;
            emitter.Seed = seed;
            emitter.Capacity = capacity;
            return emitter;
        }

        /// <summary>A one-shot emitter: <paramref name="count"/> particles at <paramref name="at"/> seconds, no loop.</summary>
        public static EmissionModule Once(ParticleEmitter emitter, float count, float at = 0f, float duration = 30f)
        {
            emitter.Looping = false;
            emitter.Duration = duration;
            var emission = new EmissionModule { Rate = 0f };
            emission.Bursts.Add(new ParticleBurst(at, count));
            emitter.Modules.Add(emission);
            return emission;
        }

        private const string Kit = "Packages/media.lightside.uniparticles/Defaults/Textures/";

        public static Texture2D Tex(string name)
            => AssetDatabase.LoadAssetAtPath<Texture2D>(Kit + name + ".png") ?? throw new InvalidOperationException($"Texture {name} missing.");

        /// <summary>A look painting the whole of a kit texture: additive light for glows and flares, normal for objects.</summary>
        public static ShapeLook Painted(string texture, LayerBlend blend = LayerBlend.Additive) => new()
        {
            Outline = new ParticleOutline(ShapeKind.RoundedRect) { cornerRadius = 0f }, Paint = ParticlePaint.Texture, Texture = Tex(texture), Blend = blend,
        };

        /// <summary>Appends an emitter emptied of modules to <paramref name="effect"/>.</summary>
        public static ParticleEmitter Add(ParticleEffect effect, string name, uint seed, int capacity)
            => Emitter(effect, effect.Emitters.Count, name, seed, capacity);

        /// <summary>One soft light at <paramref name="at"/> seconds that swells and fades: the flash an impact opens with.</summary>
        public static ParticleEmitter Flash(ParticleEffect effect, uint seed, float at, Vector3 position, float size, Color color, float life = 0.45f)
        {
            var flash = Add(effect, "Flash", seed, 4);
            Once(flash, 1f, at);
            flash.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Point, Position = position });
            flash.Modules.Add(new StartModule { Lifetime = new ParticleRange(life), Speed = new ParticleRange(0f), Size = new ParticleRange(size), Color = color });
            flash.Modules.Add(new SizeOverLifeModule { From = 0.35f, To = 1f, Curve = Ease.Of(EasingType.CubicOut) });
            flash.Modules.Add(new ColorOverLifeModule { Gradient = Ramp((0f, Color.white), (1f, new Color(1f, 1f, 1f, 0f))) });
            flash.Look = Glowing(1f);
            return flash;
        }

        /// <summary>A shock ring racing out from <paramref name="position"/> at <paramref name="at"/> seconds, thinning as it grows.</summary>
        public static ParticleEmitter Shock(ParticleEffect effect, uint seed, float at, Vector3 position, float size, Color color, float life = 0.55f)
        {
            var ring = Add(effect, "Shock", seed, 4);
            Once(ring, 1f, at);
            ring.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Point, Position = position });
            ring.Modules.Add(new StartModule { Lifetime = new ParticleRange(life), Speed = new ParticleRange(0f), Size = new ParticleRange(size), Color = color });
            ring.Modules.Add(new SizeOverLifeModule { From = 0.12f, To = 1f, Curve = Ease.Of(EasingType.CubicOut) });
            ring.Modules.Add(new ColorOverLifeModule { Gradient = Ramp((0f, Color.white), (0.4f, new Color(1f, 1f, 1f, 0.7f)), (1f, new Color(1f, 1f, 1f, 0f))) });
            ring.Look = new ShapeLook
            {
                Outline = new ParticleOutline(ShapeKind.Ring) { thickness = 0.2f }, MorphTarget = new ParticleOutline(ShapeKind.Ring) { thickness = 0.02f },
                MorphTo = 1f, MorphCurve = Ease.Of(EasingType.CubicOut), Softness = 0.02f, Blend = LayerBlend.Additive,
            };
            return ring;
        }

        /// <summary>A look of candy-colored dots that bloom into five-pointed stars over their life.</summary>
        public static ShapeLook Popcorn() => new()
        {
            Outline = new ParticleOutline(ShapeKind.Circle), MorphTarget = new ParticleOutline(ShapeKind.Star) { starPoints = 5, starSharpness = 0.5f },
            MorphTo = 1f, MorphCurve = Ease.Of(EasingType.CubicOut), Softness = 0.03f,
        };

        /// <summary>A size over life that pops in, holds and shrinks away: the casual twinkle's curve.</summary>
        public static SizeOverLifeModule Plateau() => new() { From = 0f, To = 1f, Curve = EaseProfiles.Plateau };

        /// <summary>Big four-pointed flares thrown out of <paramref name="position"/> at <paramref name="at"/> seconds, spinning down as they fade.</summary>
        public static ParticleEmitter Flares(ParticleEffect effect, uint seed, float at, Vector3 position, float count, float size, float speed, Color color)
        {
            var flares = Add(effect, "Flares", seed, 64);
            Once(flares, count, at);
            flares.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Circle, Radius = 20f, Position = position, Direction = ParticleDirection.Shape });
            flares.Modules.Add(new StartModule
            {
                Lifetime = new ParticleRange(0.5f, 0.85f), Speed = new ParticleRange(speed * 0.4f, speed), Size = new ParticleRange(size * 0.6f, size),
                Rotation = new ParticleRange(0f, 45f), Spin = new ParticleRange(-140f, 140f), Color = color,
            });
            flares.Modules.Add(new ForceModule { Gravity = Vector3.zero, Drag = 3.5f });
            flares.Modules.Add(new SizeOverLifeModule { From = 1f, To = 0f, Curve = Ease.Of(EasingType.QuadraticIn) });
            flares.Look = Painted("Flare");
            return flares;
        }

        /// <summary>Sunburst rays fading in at <paramref name="at"/> seconds behind something precious, turning slowly and holding for <paramref name="hold"/> seconds.</summary>
        public static ParticleEmitter Rays(ParticleEffect effect, uint seed, float at, Vector3 position, float size, Color color, float hold, float spin = 22f)
        {
            var rays = Add(effect, "Rays", seed, 4);
            Once(rays, 1f, at);
            rays.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Point, Position = position });
            rays.Modules.Add(new StartModule { Lifetime = new ParticleRange(hold), Speed = new ParticleRange(0f), Size = new ParticleRange(size), Spin = new ParticleRange(spin), Color = color });
            rays.Modules.Add(new SizeOverLifeModule { From = 0.3f, To = 1f, Curve = Ease.Of(EasingType.BackOut) });
            var fadeIn = Mathf.Min(0.15f, 0.4f / hold);
            var fadeOut = 1f - Mathf.Min(0.2f, 0.6f / hold);
            rays.Modules.Add(new ColorOverLifeModule { Gradient = Ramp((0f, new Color(1f, 1f, 1f, 0f)), (fadeIn, Color.white), (fadeOut, Color.white), (1f, new Color(1f, 1f, 1f, 0f))) });
            rays.Look = Painted("Rays");
            return rays;
        }

        /// <summary>
        /// Objects popping out of <paramref name="position"/> at <paramref name="at"/> seconds in an arc, then — a death
        /// sub-emitter each — swooping to <paramref name="target"/> with a glowing trail, a flare blooming on each arrival.
        /// Returns the flying emitter, whose arrivals the host reports through <c>Notified</c>.
        /// </summary>
        public static ParticleEmitter PopAndFly(ParticleEffect effect, uint seed, float at, Vector3 position, float count, string sprite, Transform target,
            float size, Gradient tint, Color trail, Color shine)
        {
            var pop = Add(effect, "Pop", seed, 96);
            Once(pop, count, at);
            pop.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Point, Position = position, Direction = ParticleDirection.Fixed, DirectionVector = Vector3.up, Spread = 75f });
            pop.Modules.Add(new StartModule
            {
                Lifetime = new ParticleRange(0.65f, 0.95f), Speed = new ParticleRange(750f, 1250f), Size = new ParticleRange(size * 0.85f, size),
                Spin = new ParticleRange(-200f, 200f), ColorMode = ParticleColorMode.RandomFromGradient, Gradient = tint,
            });
            pop.Modules.Add(new ForceModule { Gravity = new Vector3(0f, -2600f, 0f), Drag = 0.6f });
            pop.Modules.Add(new FlipModule { Turns = new ParticleRange(1.5f, 2.5f), BackShade = 0.15f });
            pop.Look = Painted(sprite, LayerBlend.Normal);

            var fly = Add(effect, "Fly", seed + 1, 96);
            fly.Modules.Add(new SubEmitterModule { Source = pop.NodeId, Trigger = ParticleSubEmitterTrigger.Death, Count = new ParticleRange(1f), InheritVelocity = 0.45f, InheritColor = true });
            fly.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Point });
            fly.Modules.Add(new StartModule { Lifetime = new ParticleRange(2.5f), Speed = new ParticleRange(0f), Size = new ParticleRange(size * 0.85f, size), Spin = new ParticleRange(-120f, 120f) });
            fly.Modules.Add(new ForceModule { Gravity = Vector3.zero, Drag = 1f });
            fly.Modules.Add(new AttractorModule { Target = target, Strength = 9500f, ArrivalRadius = 34f, Steering = 6f });
            fly.Modules.Add(new SizeBySpeedModule { From = 1f, To = 0.75f, MinSpeed = 200f, MaxSpeed = 1800f });
            fly.Modules.Add(new FlipModule { Turns = new ParticleRange(1.5f, 2.5f), BackShade = 0.15f });
            fly.Modules.Add(new TrailModule
            {
                Length = 0.12f, Segments = 6, Width = 0.3f, TailWidth = 0f, TailAlpha = 0f,
                Color = Ramp((0f, trail), (1f, new Color(trail.r, trail.g, trail.b, 0f))),
            });
            fly.Look = Painted(sprite, LayerBlend.Normal);

            var glint = Add(effect, "Arrival", seed + 2, 96);
            glint.Modules.Add(new SubEmitterModule { Source = fly.NodeId, Trigger = ParticleSubEmitterTrigger.Death, Count = new ParticleRange(1f) });
            glint.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Point });
            glint.Modules.Add(new StartModule
            {
                Lifetime = new ParticleRange(0.35f, 0.45f), Speed = new ParticleRange(0f), Size = new ParticleRange(size * 2f, size * 2.6f),
                Rotation = new ParticleRange(0f, 45f), Spin = new ParticleRange(-90f, 90f), Color = shine,
            });
            glint.Modules.Add(new SizeOverLifeModule { From = 0.3f, To = 1f, Curve = Ease.Of(EasingType.BackOut) });
            glint.Modules.Add(new ColorOverLifeModule { Gradient = Fade(0f, 0.4f) });
            glint.Look = Painted("Flare");
            return fly;
        }

        /// <summary>Fills <paramref name="shape"/> with a whole kit texture instead of its color.</summary>
        public static UniShape Textured(UniShape shape, string texture)
        {
            var paint = Fill(shape);
            paint.Kind = PaintSourceKind.Texture;
            paint.Color = Color.white;
            paint.Texture = Tex(texture);
            paint.Fit = PaintFit.Contain;
            paint.ProjectionKind = PaintProjectionKind.Linear;
            paint.Angle = 0f;
            paint.Scale = 1f;
            paint.Offset = Vector2.zero;
            return shape;
        }

        public static ShapeLook Glowing(float softness = 0.55f) => new() { Outline = new ParticleOutline(ShapeKind.Circle), Softness = softness, Blend = LayerBlend.Additive };

        public static ShapeLook Twinkle() => new()
        {
            Outline = new ParticleOutline(ShapeKind.Star) { starPoints = 4, starSharpness = 0.82f }, Softness = 0.08f, Blend = LayerBlend.Additive,
        };

        public static ShapeLook Piece() => new() { Outline = new ParticleOutline(ShapeKind.RoundedRect) { cornerRadius = 0.25f } };

        public static float Smooth(float t) => t * t * (3f - 2f * t);

        /// <summary>0 before <paramref name="from"/>, 1 after <paramref name="to"/>, linear between.</summary>
        public static float Window(float t, float from, float to) => Mathf.Clamp01((t - from) / (to - from));

        /// <summary>A back-out pop from 0 to 1 over <paramref name="seconds"/> starting at <paramref name="at"/>.</summary>
        public static float Pop(float t, float at, float seconds = 0.45f) => Ease.Of(EasingType.BackOut).Evaluate(Window(t, at, at + seconds));
    }
}
