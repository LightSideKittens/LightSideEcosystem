using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>A built element: its <see cref="UniShape"/> and the rect that positions it.</summary>
    /// <remarks>
    /// Converts implicitly to <see cref="RectTransform"/>, so a widget drops straight into the layout vocabulary
    /// without a member access at every call site.
    /// </remarks>
    public readonly struct Widget
    {
        public Widget(UniShape shape)
        {
            Shape = shape;
            Rect = shape.rectTransform;
        }

        public UniShape Shape { get; }

        public RectTransform Rect { get; }

        public GameObject GameObject => Shape.gameObject;

        /// <summary>The fill paint, which is the one a slide almost always animates.</summary>
        public ShapePaint Fill => Stage.FillOf(Shape);

        /// <summary>The outline, for changing a radius or a kind after the fact.</summary>
        public PrimitiveShapeProvider Outline => (PrimitiveShapeProvider)Shape.Shape;

        public static implicit operator RectTransform(Widget widget) => widget.Rect;

        public static implicit operator Transform(Widget widget) => widget.Rect;
    }
}
