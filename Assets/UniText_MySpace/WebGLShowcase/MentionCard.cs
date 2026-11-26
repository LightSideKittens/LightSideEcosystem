using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LightSide.Showcase
{
    /// <summary>The card of a mentioned person, shown beside the mention until anything else is pressed.</summary>
    public sealed class MentionCard : MonoBehaviour
    {
        private const float Gap = 14f;
        private const float OpenSeconds = 0.22f;

        /// <summary>A person the card knows.</summary>
        [Serializable]
        public struct Person
        {
            public string handle;
            public string avatar;
            public string name;
            public string about;
        }

        [SerializeField] private Person[] people;

        [Tooltip("Shown for a handle nobody in the list has; its name is the handle.")]
        [SerializeField] private Person stranger;

        [SerializeField] private CanvasGroup group;
        [SerializeField] private UniText avatar;
        [SerializeField] private UniText title;
        [SerializeField] private UniText about;
        [SerializeField] private Button reply;
        [SerializeField] private UniTextEditable composer;

        private string handle;
        private float opened;
        private int openedFrame;

        private void OnEnable() => reply.onClick.AddListener(Reply);

        private void OnDisable() => reply.onClick.RemoveListener(Reply);

        /// <summary>Opens the card of the person named <paramref name="mentioned"/> beside <paramref name="anchor"/>, a rectangle in the space of <paramref name="text"/>.</summary>
        public void Open(string mentioned, RectTransform text, Rect anchor)
        {
            handle = mentioned;
            var index = Array.FindIndex(people, candidate => candidate.handle == mentioned);
            var person = index < 0 ? stranger : people[index];
            avatar.Text = person.avatar;
            title.Text = index < 0 ? mentioned : person.name;
            about.Text = person.about;
            gameObject.SetActive(true);
            Place(text, anchor);
            opened = 0f;
            openedFrame = Time.frameCount;
            Present();
        }

        private void Update()
        {
            opened = Mathf.Min(1f, opened + Time.unscaledDeltaTime / OpenSeconds);
            Present();
            if (Time.frameCount > openedFrame && InputUtils.GetPointerDown() && !Covers(InputUtils.MousePosition))
                gameObject.SetActive(false);
        }

        private void Place(RectTransform text, Rect anchor)
        {
            var card = (RectTransform)transform;
            var parent = (RectTransform)card.parent;
            Vector2 top = parent.InverseTransformPoint(text.TransformPoint(new Vector3(anchor.center.x, anchor.yMax)));
            Vector2 bottom = parent.InverseTransformPoint(text.TransformPoint(new Vector3(anchor.center.x, anchor.yMin)));
            var above = top.y + Gap + card.rect.height <= parent.rect.yMax;
            card.pivot = new Vector2(0.5f, above ? 0f : 1f);
            var reach = (parent.rect.width - card.rect.width) * 0.5f;
            var x = Mathf.Clamp(top.x - parent.rect.center.x, -reach, reach);
            card.anchoredPosition = new Vector2(x, (above ? top.y + Gap : bottom.y - Gap) - parent.rect.center.y);
        }

        private bool Covers(Vector2 screen)
            => RectTransformUtility.RectangleContainsScreenPoint((RectTransform)transform, screen, avatar.canvas.worldCamera);

        private void Present()
        {
            var t = opened - 1f;
            group.alpha = Mathf.Clamp01(opened * 2f);
            transform.localScale = Vector3.one * (1f + 2.7f * t * t * t + 1.7f * t * t);
        }

        private void Reply()
        {
            var draft = composer.Text;
            composer.SetText(draft.Length == 0 || draft.EndsWith(" ") ? $"{draft}@{handle} " : $"{draft} @{handle} ");
            gameObject.SetActive(false);
            EventSystem.current.SetSelectedGameObject(composer.gameObject);
            composer.Activate();
        }
    }
}
