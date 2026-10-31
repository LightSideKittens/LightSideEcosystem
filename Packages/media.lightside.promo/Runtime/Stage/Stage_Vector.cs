using System;
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
        /// <see cref="BezierPath.Replace"/>, since Auto reads neighbors the path must already hold.
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

            return new VectorShapeProvider { Path = path, FlattenTolerance = Budget(path) };
        }

        /// <summary>
        /// The coarsest-but-one tolerance at which <paramref name="path"/> flattens inside the shape atlas's vertex
        /// budget.
        /// </summary>
        /// <remarks>
        /// A polygon shape is baked into a shared vertex atlas whose rows hold <see cref="AtlasVertices"/> points,
        /// and a flattened contour longer than that is <em>truncated</em> — silently, by a <c>Min</c>, with the
        /// surviving head closed back to its start by a straight chord. The tail of the path is what disappears, so
        /// the damage lands wherever the author happened to stop authoring, and it looks like a modeling mistake
        /// rather than a budget.
        /// <para>
        /// Tolerance is in path units, and the path is already scaled, so it is not comparable between contours —
        /// which is why it is solved for rather than typed. Adding anchors to chase a rounder outline spends the
        /// same budget twice over: once on the anchors, again on the curve between them.
        /// </para>
        /// </remarks>
        public static float Budget(BezierPath path)
        {
            var flat = new List<Vector2>();
            var tolerance = FinestFlatten;

            for (var i = 0; i < BudgetSteps; i++)
            {
                path.Flatten(flat, tolerance);
                if (flat.Count <= AtlasVertices) return tolerance;
                tolerance *= 1.6f;
            }

            throw new InvalidOperationException(
                $"[Promo] A contour of {path.Count} knots flattens to {flat.Count} points at tolerance " +
                $"{tolerance:0.###}, past the {AtlasVertices} a shape atlas row holds. It would be truncated and " +
                "closed with a straight chord across whatever was left. Author it with fewer anchors.");
        }

        /// <summary>Points one row of the shape vertex atlas holds.</summary>
        private static int AtlasVertices => VectorShapeProvider.MaxVertices;

        /// <summary>Where the search for a tolerance starts: fine enough that a simple contour keeps every curve.</summary>
        private const float FinestFlatten = 0.25f;

        private const int BudgetSteps = 12;
    }
}
