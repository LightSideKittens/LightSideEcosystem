using System.Collections.Generic;
using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// What the pointer is over: rects that claim a cursor shape, tested against the pointer's actual position.
    /// </summary>
    /// <remarks>
    /// The shape is derived, never declared. Tagging a beat with a cursor is a second list beside the layout, and
    /// the two drift the moment either moves — a beam appearing over empty background is that drift made visible.
    /// A widget registers the rect it owns, and the answer follows the rect wherever it goes.
    /// <para>
    /// Later registrations sit on top, matching the order things are built and drawn in, so a field inside a panel
    /// wins over the panel.
    /// </para>
    /// </remarks>
    public sealed class CursorRegions
    {
        private readonly List<(RectTransform rect, CursorType cursor)> regions = new();

        /// <summary>Claims <paramref name="cursor"/> for everything inside <paramref name="rect"/>.</summary>
        public void Add(RectTransform rect, CursorType cursor)
        {
            if (rect) regions.Add((rect, cursor));
        }

        /// <summary>
        /// The shape at the world point <paramref name="world"/>.
        /// </summary>
        /// <remarks>
        /// Regions are tested inset by <see cref="Seam"/>. A point resting exactly on an edge is a coin toss in
        /// floating point — and the slow push-in rescales everything under it every frame, so the toss is thrown
        /// again on each one and the cursor flickers between shapes. Insetting makes the boundary unambiguous
        /// without remembering anything, which matters: an answer that depended on the previous frame could not be
        /// scrubbed backwards.
        /// </remarks>
        public CursorType At(Vector3 world)
        {
            for (var i = regions.Count - 1; i >= 0; i--)
            {
                var (rect, cursor) = regions[i];
                if (!rect || !rect.gameObject.activeInHierarchy) continue;

                var area = rect.rect;
                var local = (Vector2)rect.InverseTransformPoint(world);
                if (local.x > area.xMin + Seam && local.x < area.xMax - Seam &&
                    local.y > area.yMin + Seam && local.y < area.yMax - Seam)
                    return cursor;
            }

            return CursorType.Default;
        }

        private const float Seam = 2f;
    }
}
