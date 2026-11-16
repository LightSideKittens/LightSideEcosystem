using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// A surface that leans toward a pointer moving over it and leans further while the pointer presses, as a panel
    /// does under a finger.
    /// </summary>
    /// <remarks>
    /// The surface turns about an axis through the point under the pointer, so that point stays exactly where it was
    /// and the pointer never drifts off what it is operating; the rest of the surface swings around it. Only a
    /// perspective camera shows the turn as depth; an orthographic one shows it as a slight foreshortening.
    /// </remarks>
    public sealed class Lean
    {
        private readonly RectTransform surface;
        private readonly Vector3 rest;
        private readonly Rect area;
        private readonly float hover;
        private readonly float press;
        private readonly float reach;

        /// <summary>
        /// Leans <paramref name="surface"/>, which covers <paramref name="area"/> of its parent, by up to
        /// <paramref name="hover"/> degrees under a pointer and <paramref name="press"/> more while it presses, the lean
        /// fading out as the pointer moves <paramref name="reach"/> points outside the area.
        /// </summary>
        public Lean(RectTransform surface, Rect area, float hover, float press, float reach)
        {
            this.surface = surface;
            this.area = area;
            this.hover = hover;
            this.press = press;
            this.reach = reach;
            rest = surface.localPosition;
        }

        /// <summary>Leans the surface for the pointer at <paramref name="pose"/>, in the surface's parent coordinates.</summary>
        public void Pose(PointerTimeline.Pose pose)
        {
            var point = pose.Position;
            var outside = Vector2.Distance(point, new Vector2(
                Mathf.Clamp(point.x, area.xMin, area.xMax), Mathf.Clamp(point.y, area.yMin, area.yMax)));
            var near = 1f - Mathf.SmoothStep(0f, 1f, outside / reach);
            var toward = (point - area.center) / (area.size * 0.5f);
            toward = new Vector2(Mathf.Clamp(toward.x, -1f, 1f), Mathf.Clamp(toward.y, -1f, 1f));
            var degrees = (hover + press * pose.Press) * near;
            var turn = Quaternion.Euler(toward.y * degrees, -toward.x * degrees, 0f);
            var pivot = new Vector3(point.x, point.y, 0f);
            surface.localRotation = turn;
            surface.localPosition = pivot + turn * (rest - pivot);
        }
    }
}
