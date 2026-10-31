using UnityEngine;
using UnityEngine.UI;

namespace LightSide.Promo
{
    /// <summary>Rect creation and the anchor vocabulary every widget is positioned with.</summary>
    public sealed partial class Stage
    {
        private const int UiLayer = 5;

        /// <summary>An empty rect parented to <see cref="Root"/>.</summary>
        public RectTransform Node(string name) => Node(name, Root);

        /// <summary>
        /// An empty rect parented to <paramref name="parent"/>, on the UI layer, at unit scale.
        /// </summary>
        /// <remarks>
        /// Named for the node rather than its component so it cannot shadow <see cref="UnityEngine.Rect"/> inside
        /// this class, which owns members of that type.
        /// </remarks>
        public RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = UiLayer };
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            return rect;
        }

        /// <summary>Stretches <paramref name="rect"/> across its parent, inset by the given edges.</summary>
        public RectTransform Stretch(RectTransform rect, float left = 0f, float bottom = 0f, float right = 0f, float top = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
            return rect;
        }

        /// <summary>Full anchor control, for elements that must stretch along one axis and size along the other.</summary>
        public RectTransform Anchor(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        /// <summary>A fixed-size element pinned to one anchor point.</summary>
        public RectTransform Box(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        /// <summary>A fixed-size element centered on the frame, offset by <paramref name="position"/>.</summary>
        public RectTransform Box(RectTransform rect, Vector2 position, Vector2 size) =>
            Box(rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, size);

        public HorizontalLayoutGroup Row(GameObject go, float spacing, RectOffset padding, TextAnchor align,
            bool controlWidth = true, bool controlHeight = true, bool expandWidth = false, bool expandHeight = false) =>
            Configure(go.AddComponent<HorizontalLayoutGroup>(), spacing, padding, align,
                controlWidth, controlHeight, expandWidth, expandHeight);

        public VerticalLayoutGroup Column(GameObject go, float spacing, RectOffset padding, TextAnchor align,
            bool controlWidth = true, bool controlHeight = true, bool expandWidth = false, bool expandHeight = false) =>
            Configure(go.AddComponent<VerticalLayoutGroup>(), spacing, padding, align,
                controlWidth, controlHeight, expandWidth, expandHeight);

        /// <summary>Uniform padding, in the order a layout group expects.</summary>
        public static RectOffset Pad(int all) => new RectOffset(all, all, all, all);

        public static RectOffset Pad(int left, int right, int top, int bottom) =>
            new RectOffset(left, right, top, bottom);

        /// <summary>
        /// Sets only the layout values given: any of <paramref name="preferredWidth"/>,
        /// <paramref name="preferredHeight"/>, <paramref name="flexibleWidth"/> and <paramref name="flexibleHeight"/>
        /// left negative is not written, so one call can size an element without disturbing what another already set.
        /// <paramref name="ignore"/> is how an overlay escapes its parent's layout group entirely.
        /// </summary>
        public LayoutElement Size(GameObject go, float preferredWidth = -1f, float preferredHeight = -1f,
            float flexibleWidth = -1f, float flexibleHeight = -1f, bool ignore = false)
        {
            if (!go.TryGetComponent<LayoutElement>(out var element)) element = go.AddComponent<LayoutElement>();
            element.ignoreLayout = ignore;
            if (preferredWidth >= 0f) element.preferredWidth = preferredWidth;
            if (preferredHeight >= 0f) element.preferredHeight = preferredHeight;
            if (flexibleWidth >= 0f) element.flexibleWidth = flexibleWidth;
            if (flexibleHeight >= 0f) element.flexibleHeight = flexibleHeight;
            return element;
        }

        /// <summary>
        /// The <see cref="CanvasGroup"/> on <paramref name="rect"/>, adding one if it has none.
        /// </summary>
        /// <remarks>
        /// The only way to fade a subtree: a graphic's color reaches that graphic and nothing beneath it. Call at
        /// build time and cache the result — a slide must not hunt for components while posing a frame.
        /// </remarks>
        public static CanvasGroup Group(RectTransform rect) =>
            rect.TryGetComponent<CanvasGroup>(out var group) ? group : rect.gameObject.AddComponent<CanvasGroup>();

        /// <summary>Distance between two stage points.</summary>
        public static float Dist(Vector2 a, Vector2 b) => Vector2.Distance(a, b);

        private static T Configure<T>(T group, float spacing, RectOffset padding, TextAnchor align,
            bool controlWidth, bool controlHeight, bool expandWidth, bool expandHeight)
            where T : HorizontalOrVerticalLayoutGroup
        {
            group.spacing = spacing;
            group.padding = padding;
            group.childAlignment = align;
            group.childControlWidth = controlWidth;
            group.childControlHeight = controlHeight;
            group.childForceExpandWidth = expandWidth;
            group.childForceExpandHeight = expandHeight;
            return group;
        }
    }
}
