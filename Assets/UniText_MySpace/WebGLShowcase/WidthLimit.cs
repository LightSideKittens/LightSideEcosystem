using UnityEngine;
using UnityEngine.EventSystems;

namespace LightSide.Showcase
{
    /// <summary>Keeps a centered panel as wide as its parent allows, up to a limit.</summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public sealed class WidthLimit : UIBehaviour
    {
        [SerializeField, Min(0f)] private float limit = 1000f;
        [SerializeField, Min(0f)] private float margin = 24f;

        private DrivenRectTransformTracker tracker;

        protected override void OnEnable() => Fit();

        protected override void OnDisable() => tracker.Clear();

        protected override void OnRectTransformDimensionsChange() => Fit();

        private void Fit()
        {
            if (!isActiveAndEnabled || transform.parent is not RectTransform parent) return;
            var rect = (RectTransform)transform;
            tracker.Clear();
            tracker.Add(this, rect, DrivenTransformProperties.SizeDeltaX);
            var width = Mathf.Min(limit, parent.rect.width - margin * 2f);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        }
    }
}
