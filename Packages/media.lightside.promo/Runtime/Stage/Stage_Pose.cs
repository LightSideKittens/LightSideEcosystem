using System;
using UnityEngine;
using UnityEngine.UI;

namespace LightSide.Promo
{
    /// <summary>Poses a slide already built — the vocabulary of <see cref="Slide.OnRender"/>.</summary>
    public sealed partial class Stage
    {
        /// <summary>Sets one graphic's opacity, leaving its color alone.</summary>
        /// <remarks>
        /// Reaches that visual only — a uGUI graphic or a shape. Its color never propagates to its children, so a
        /// panel faded this way keeps every child it owns at full opacity. To fade a subtree, build it a
        /// <see cref="CanvasGroup"/> with <see cref="Stage.Group"/> and set that instead.
        /// </remarks>
        /// <exception cref="ArgumentException"><paramref name="visual"/> is neither a graphic nor a shape.</exception>
        public static void Alpha(Component visual, float alpha)
        {
            switch (visual)
            {
                case Graphic graphic:
                {
                    var color = graphic.color;
                    color.a = Mathf.Clamp01(alpha);
                    graphic.color = color;
                    break;
                }
                case UniShapeBase shape:
                {
                    var color = shape.Color;
                    color.a = Mathf.Clamp01(alpha);
                    shape.Color = color;
                    break;
                }
                default:
                    throw new ArgumentException($"{visual.GetType().Name} has no color to fade.", nameof(visual));
            }
        }

        /// <summary>
        /// Drives the fill rect returned by <see cref="Bar"/> to <paramref name="progress"/> of its track.
        /// </summary>
        /// <remarks>
        /// The fill's width is entirely anchor-derived, so this is a real resize: the mesh re-populates on every
        /// call, and under an ancestor layout group a layout pass is queued as well. It is cheap enough to drive
        /// per frame, but it is not free.
        /// </remarks>
        public static void Progress(RectTransform fill, float progress)
        {
            var max = fill.anchorMax;
            max.x = Mathf.Clamp01(progress);
            fill.anchorMax = max;
        }

        /// <summary>Scales <paramref name="rect"/> uniformly in the plane.</summary>
        public static void Scale(RectTransform rect, float scale) =>
            rect.localScale = new Vector3(scale, scale, 1f);

        /// <summary>Scales <paramref name="rect"/> independently along each axis, for squash and stretch.</summary>
        public static void Scale(RectTransform rect, float x, float y) =>
            rect.localScale = new Vector3(x, y, 1f);

        /// <summary>Rotates <paramref name="rect"/> in the plane of the frame.</summary>
        public static void Spin(RectTransform rect, float degrees) =>
            rect.localRotation = Quaternion.Euler(0f, 0f, degrees);
    }
}
