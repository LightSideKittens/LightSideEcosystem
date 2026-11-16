using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// Draws its object through a nested canvas sorted over everything else on its root canvas, popups the kit lifts
    /// as they open included.
    /// </summary>
    /// <remarks>
    /// A canvas inactive in its hierarchy reads as a root canvas and drops its sorting override, so the override is
    /// written each time the object joins a canvas: on enable and whenever its canvas hierarchy changes.
    /// </remarks>
    [RequireComponent(typeof(Canvas))]
    [AddComponentMenu("")]
    public sealed class SortAbove : MonoBehaviour
    {
        private void OnEnable() => Apply();

        private void OnCanvasHierarchyChanged() => Apply();

        private void Apply()
        {
            if (!isActiveAndEnabled || transform.parent == null) return;
            var above = transform.parent.GetComponentInParent<Canvas>();
            if (above == null) return;
            var layer = GetComponent<Canvas>();
            layer.overrideSorting = true;
            layer.sortingLayerID = above.rootCanvas.sortingLayerID;
            layer.sortingOrder = Order;
        }

        /// <summary>The sorting order drawn at, far over any the kit's popups reach.</summary>
        private const int Order = 30000;
    }
}
