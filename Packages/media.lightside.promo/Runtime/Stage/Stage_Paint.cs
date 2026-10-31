using UnityEngine;
using Ramp = LightSide.Gradient;
using Stop = LightSide.GradientStop;
using Interp = LightSide.GradientInterpolation;

namespace LightSide.Promo
{
    /// <summary>Paint and layer assembly for <see cref="UniShape"/>.</summary>
    /// <remarks>
    /// Two of the projection conversions below are not derivable from their parameter names and will read as bugs to
    /// anyone who has not met them: a radial paint's <c>Scale</c> is an inverse zoom, and an angular sweep needs its
    /// first color repeated as a third stop or the ramp shows a seam where it wraps.
    /// </remarks>
    public sealed partial class Stage
    {
        /// <summary>
        /// The shape's fill paint, adding a <see cref="FillLayer"/> beneath everything else if it has none.
        /// </summary>
        public static ShapePaint FillOf(UniShape shape)
        {
            var layers = shape.Layers;
            for (var i = 0; i < layers.Count; i++)
                if (layers[i] is FillLayer fill)
                    return fill.Paint;

            var added = new FillLayer();
            shape.Layers.Insert(0, added);
            return added.Paint;
        }

        public static void Solid(ShapePaint paint, Color color)
        {
            paint.Kind = PaintSourceKind.Solid;
            paint.Color = color;
        }

        /// <summary>A two-stop linear ramp at <paramref name="angleDeg"/> degrees.</summary>
        public static void Linear(ShapePaint paint, Color from, Color to, float angleDeg = 90f,
            Interp interpolation = Interp.Perceptual)
        {
            Project(paint, PaintProjectionKind.Linear, angleDeg, 1f);
            paint.Gradient = new Ramp(new[] { new Stop(0f, from), new Stop(1f, to) }, interpolation);
        }

        /// <summary>
        /// A two-stop radial ramp. <paramref name="scale"/> is a zoom in the intuitive direction — larger spreads the
        /// ramp further — and is inverted into the paint's own scale.
        /// </summary>
        /// <remarks>
        /// To fade a glow out, give the outer stop the inner stop's color at zero alpha. Fading to transparent
        /// black instead darkens as it fades: the shader premultiplies the ramp's texels, so the color curve and
        /// the alpha curve multiply and the falloff collapses far faster than it looks like it should.
        /// </remarks>
        public static void Radial(ShapePaint paint, Color inner, Color outer, float scale = 1f,
            Interp interpolation = Interp.Perceptual)
        {
            Project(paint, PaintProjectionKind.Radial, 0f, 1f / Mathf.Max(scale, 0.001f));
            paint.Gradient = new Ramp(new[] { new Stop(0f, inner), new Stop(1f, outer) }, interpolation);
        }

        /// <summary>
        /// A conic sweep starting at <paramref name="angleDeg"/>. The ramp is <c>from - to - from</c> so the wrap
        /// point has no seam, and the angle is mirrored because the sweep runs clockwise from the paint's own zero.
        /// </summary>
        public static void Angular(ShapePaint paint, Color from, Color to, float angleDeg = 0f,
            Interp interpolation = Interp.Perceptual)
        {
            Project(paint, PaintProjectionKind.Angular, Mathf.Repeat(180f - angleDeg, 360f), 1f);
            paint.Gradient = new Ramp(
                new[] { new Stop(0f, from), new Stop(0.5f, to), new Stop(1f, from) }, interpolation);
        }

        /// <summary>Paints <paramref name="gradient"/> as given, without rebuilding its stops.</summary>
        /// <remarks>
        /// Assigning a gradient of equal value costs nothing, but a gradient built fresh with different stops every
        /// frame consumes one atlas row per frame. Animate <see cref="ShapePaint.Angle"/>,
        /// <see cref="ShapePaint.Offset"/> or <see cref="ShapePaint.Scale"/> instead; it is cheaper and it sweeps
        /// rather than mutates, which is what the eye wants anyway.
        /// </remarks>
        public static void Ramped(ShapePaint paint, Ramp gradient,
            PaintProjectionKind projection = PaintProjectionKind.Linear, float angleDeg = 90f, float scale = 1f)
        {
            Project(paint, projection, angleDeg, scale);
            paint.Gradient = gradient;
        }

        /// <summary>Paints an imported texture into the shape. A null texture renders magenta by design.</summary>
        public static void Textured(ShapePaint paint, Texture2D texture, PaintFit fit = PaintFit.Contain)
        {
            paint.Kind = PaintSourceKind.Texture;
            paint.Color = Color.white;
            paint.Texture = texture;
            paint.Fit = fit;
            paint.ProjectionKind = PaintProjectionKind.Linear;
            paint.Angle = 0f;
            paint.Scale = 1f;
            paint.Offset = Vector2.zero;
        }

        public static StrokeLayer AddStroke(UniShape shape, Color color, float width, float align = 0f)
        {
            var layer = new StrokeLayer { Width = width, Align = align };
            Solid(layer.Paint, color);
            shape.Layers.Add(layer);
            return layer;
        }

        /// <summary>Adds a drop shadow beneath every existing layer, which is the only place it reads as depth.</summary>
        public static ShadowLayer AddShadow(UniShape shape, Color color, Vector2 offset, float blur, float spread = 0f)
        {
            var layer = new ShadowLayer { Offset = offset, Blur = blur, Spread = spread };
            Solid(layer.Paint, color);
            shape.Layers.Insert(0, layer);
            return layer;
        }

        public static InnerShadowLayer AddInnerShadow(UniShape shape, Color color, Vector2 offset, float blur)
        {
            var layer = new InnerShadowLayer { Offset = offset, Blur = blur };
            Solid(layer.Paint, color);
            shape.Layers.Add(layer);
            return layer;
        }

        /// <summary>
        /// Adds a rim light. <paramref name="shadowSide"/> lights the edge facing away from
        /// <paramref name="lightAngle"/> and multiplies instead of adding, so a lit and a shadowed bevel together
        /// read as one embossed edge.
        /// </summary>
        public static BevelLayer AddBevel(UniShape shape, float lightAngle, float width, float strength,
            bool shadowSide = false)
        {
            var layer = new BevelLayer
            {
                LightAngle = lightAngle,
                Width = width,
                Strength = strength,
                ShadowSide = shadowSide
            };
            layer.Paint.Blend = shadowSide ? LayerBlend.Multiply : LayerBlend.Additive;
            Solid(layer.Paint, shadowSide ? Color.black : Color.white);
            shape.Layers.Add(layer);
            return layer;
        }

        private static void Project(ShapePaint paint, PaintProjectionKind kind, float angleDeg, float scale)
        {
            paint.Kind = PaintSourceKind.Gradient;
            paint.Color = Color.white;
            paint.FollowShape = false;
            paint.ProjectionKind = kind;
            paint.Angle = angleDeg;
            paint.Scale = scale;
            paint.Offset = Vector2.zero;
        }
    }
}
