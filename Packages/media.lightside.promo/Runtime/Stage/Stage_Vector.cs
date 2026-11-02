using System.Collections.Generic;
using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>Vector outlines built from authored points.</summary>
    public sealed partial class Stage
    {
        /// <summary>
        /// A contour through <paramref name="points"/> scaled by <paramref name="scale"/>, rendered at its authored
        /// position: path space is the rect's local space with the origin on its pivot, so a point authored at zero
        /// lands exactly there. Closed, it is filled; open, it is stroked to a band by the provider's <c>Width</c>.
        /// </summary>
        /// <remarks>
        /// <paramref name="smooth"/> puts every knot in <see cref="TangentMode.Auto"/>, which derives its handles
        /// from its neighbors; left off, every knot is a corner. <paramref name="corners"/> exempts the knots that
        /// must stay sharp on a smooth contour: Auto takes its direction from the two neighboring anchors alone, so
        /// a knot with a near neighbor on one side and a distant one on the other tilts toward the near one, and a
        /// straight edge needs both its ends kept as corners. Modes are applied after
        /// <see cref="BezierPath.Replace(IReadOnlyList{BezierKnot}, bool)"/>, since Auto reads neighbors the path must
        /// already hold.
        /// </remarks>
        public static VectorShapeProvider Contour(Vector2[] points, float scale, bool closed = true,
            bool smooth = false, int[] corners = null)
        {
            var path = new BezierPath();
            var knots = new BezierKnot[points.Length];
            for (var i = 0; i < points.Length; i++) knots[i] = new BezierKnot(points[i] * scale);

            path.Replace(knots, closed);
            if (smooth)
            {
                for (var i = 0; i < points.Length; i++) path.SetMode(i, TangentMode.Auto);
                if (corners != null)
                    for (var i = 0; i < corners.Length; i++) path.SetMode(corners[i], TangentMode.Vector);
            }

            return new VectorShapeProvider { Path = path, FlattenTolerance = Flatten };
        }

        /// <summary>How far a contour's flattened curve may stray from its Bézier, in path units: fine enough that every curve stays round.</summary>
        private const float Flatten = 0.25f;
    }
}
