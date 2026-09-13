using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// Three fields being typed into at once, each wearing a different behaviour off the same field.
    /// </summary>
    /// <remarks>
    /// Every keystroke goes through <see cref="UniTextEditable.InsertText(string)"/> as a real typing edit, so the
    /// mask, the limit and the password behaviour see the input exactly as a keyboard would deliver it. Assigning the
    /// finished string instead would bypass the pipeline and show three fields that merely contain the right result.
    /// <para>
    /// The behaviours are ordinary objects in a list. Nothing here subclasses the field, and nothing here is a mode
    /// the field was built with.
    /// </para>
    /// </remarks>
    [AddComponentMenu(PromoMenu.AddComponent.Slides + nameof(InputKitSlide))]
    public sealed class InputKitSlide : Slide
    {
        [SerializeField] private string headline = "The input field is a kit, not a component.";
        [SerializeField] private string sub = "Masks, limits, passwords — small objects you drop in.";

        [SerializeField] private string phone = "5551234567";
        [SerializeField] private string secret = "correct horse";

        /// <summary>Longer than <see cref="Limit"/> on purpose: the last characters are the ones refused on screen.</summary>
        [SerializeField] private string note = "Only sixteen characters fit";

        private Claim claim;
        private Column[] columns;

        /// <summary>When the last keystroke of the slowest field lands, so the shot can outlive its own typing.</summary>
        private float finish;

        private readonly struct Column
        {
            internal Column(EditableField field, CanvasGroup group)
            {
                Field = field;
                Group = group;
            }

            public EditableField Field { get; }
            public CanvasGroup Group { get; }
        }

        protected override void OnBuild(Stage stage)
        {
            finish = 0f;
            stage.Backdrop(stage.Root);
            claim = stage.Claim(stage.Root, headline, sub);

            var width = stage.Width * 0.27f;
            var gap = stage.Width * 0.022f;
            var height = stage.ContentHeight * 0.52f;

            columns = new[]
            {
                BuildColumn(stage, 0, "Mask", "InputMaskBehavior", "(###) ###-####",
                    new Vector2(-(width + gap), stage.ContentCentre), width, height,
                    new InputMaskBehavior { Pattern = "(###) ###-####" }, phone),

                BuildColumn(stage, 1, "Password", "PasswordBehavior", "masked, copy refused",
                    new Vector2(0f, stage.ContentCentre), width, height,
                    new PasswordBehavior { MaskChar = "•", AllowCopy = false }, secret),

                BuildColumn(stage, 2, "Limit", "LengthLimitBehavior", "stops at " + Limit,
                    new Vector2(width + gap, stage.ContentCentre), width, height,
                    new LengthLimitBehavior { Limit = Limit }, note)
            };

            Script.Rewind(Clear);
            for (var i = 0; i < columns.Length; i++) Cue("focus", First + i * Lane);

            Seconds = Mathf.Max(Seconds, finish + Tail);
        }

        /// <summary>Empties every field, so a backward seek replays the typing from nothing.</summary>
        private void Clear()
        {
            for (var i = 0; i < columns.Length; i++) columns[i].Field.Editable.Text = string.Empty;
        }

        private Column BuildColumn(Stage stage, int index, string name, string title, string caption,
            Vector2 position, float width, float height, InputBehavior behavior, string typed)
        {
            var panel = stage.Panel(name, stage.Root);
            stage.Box(panel.Rect, position, new Vector2(width, height));
            var group = Stage.Group(panel.Rect);

            var head = stage.Label(panel.Rect, title, stage.Theme.Small, stage.Theme.TextSoft,
                HorizontalAlignment.Left, VerticalAlignment.Middle, stretch: false);
            stage.Anchor(head.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -stage.Theme.PadXl), new Vector2(-stage.Theme.PadXl * 2f, stage.Theme.Small * 1.6f));

            var field = stage.EditableField(name + " Field", panel.Rect, string.Empty, stage.Theme.Body);
            stage.Anchor(field.Rect, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 0f), new Vector2(-stage.Theme.PadXl * 2f, stage.Theme.Body * 2.6f));
            field.Text.WordWrap = false;
            field.Editable.Behaviors.Add(behavior);

            var hint = stage.Label(panel.Rect, caption, stage.Theme.Small, stage.Theme.TextSoft,
                HorizontalAlignment.Left, VerticalAlignment.Middle, stretch: false);
            stage.Anchor(hint.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, stage.Theme.PadXl), new Vector2(-stage.Theme.PadXl * 2f, stage.Theme.Small * 1.6f));

            TypeInto(field, typed, First + index * Lane);
            return new Column(field, group);
        }

        /// <summary>Schedules <paramref name="text"/> to be typed one character at a time from <paramref name="at"/>.</summary>
        private void TypeInto(EditableField field, string text, float at)
        {
            Script.At(at, "focus", () =>
            {
                field.Editable.Text = string.Empty;
                field.Editable.Activate(showKeyboard: false);
            });

            for (var i = 0; i < text.Length; i++)
            {
                var character = text[i].ToString();
                Script.At(at + Focus + i * Key, null, () => field.Editable.InsertText(character));
            }

            finish = Mathf.Max(finish, at + Focus + text.Length * Key);
        }

        protected override void OnRender(float local)
        {
            claim.Pose(local);

            for (var i = 0; i < columns.Length; i++)
            {
                var start = First + i * Lane;
                columns[i].Group.alpha = Ease.EmphasizedIn.Window(local, start - 0.35f, 0.45f);
                columns[i].Field.PoseFocus(local - start);
            }
        }

        /// <summary>
        /// Characters the third field accepts.
        /// </summary>
        /// <remarks>
        /// The field is one line tall and about 400 px wide inside its insets, which at
        /// <see cref="Promo.Theme.Body"/> holds sixteen characters of Latin even at a generous advance. Anything the
        /// limit lets through beyond that has nowhere to go.
        /// </remarks>
        private const int Limit = 16;

        private const float First = 0.5f;

        /// <summary>How long after a field is focused the first character lands.</summary>
        private const float Focus = 0.28f;

        /// <summary>Seconds per keystroke, at roughly the rate a person types without hurrying.</summary>
        private const float Key = 0.075f;

        /// <summary>Stagger between the three fields, so the eye follows one at a time.</summary>
        private const float Lane = 1.55f;

        /// <summary>How long the finished form is held after the last key, so the shot does not cut on a keystroke.</summary>
        private const float Tail = 1.1f;
    }
}
