using System;
using UnityEngine;

namespace LightSide.Promo
{
    public sealed partial class GuiControlsScene
    {
        /// <summary>
        /// Ready-made text fields: a guild sign-up card filled in by the pointer — a name and an e-mail typed, a password
        /// typed hidden and revealed, an age stepped — and sent on with its button.
        /// </summary>
        /// <remarks>
        /// Typing is the field's own editing: the field is activated while the pointer works in it, so its caret and
        /// focus ring show, and its text grows a character at a time with the caret at the end. The button stays
        /// disabled, in the kit's dimmed look, until the password holds a value; the pointer then hovers and presses it
        /// through the kit's own states.
        /// </remarks>
        private sealed class FieldsPart : KitPart
        {
            private readonly Typed typed;
            private readonly PasswordBehavior password;
            private readonly UniShapeValue age;
            private readonly Interactable next;

            public FieldsPart(GuiControlsScene scene, Stage stage)
                : base(scene, stage, "Fields", Board, Scale)
            {
                Label("Create account", new Vector2(LeftColumn, Top - 34f), 24f, "Ink", display: true);

                var name = Field("Name", "Text Field", LeftColumn, 0);
                var mail = Field("E-mail", "Text Field", RightColumn, 0);
                var secret = Field("Password", "Password Field", LeftColumn, 1);
                password = Editable(secret).GetBehavior<PasswordBehavior>()
                           ?? throw new InvalidOperationException("[Promo] The password field has no password behavior.");
                Label("Age", new Vector2(RightColumn, LabelY(1)), 14f, "InkDim");
                var number = Add("Number Field", new Vector2(RightColumn - NumberCaption, FieldY(1)), 0f);
                foreach (var text in number.GetComponentsInChildren<UniText>(true))
                    if (text.Text == "Amount")
                        text.gameObject.SetActive(false);
                age = number.GetComponent<UniShapeValue>();

                Divider(new Vector2(0f, Bottom + ButtonHalf * 2f + FooterGap), Board.x / Scale - Pad * 2f);
                var create = Add("Button", new Vector2(-LeftColumn, Bottom + ButtonHalf), 1f);
                Kit.Relabel(create, "Button", "Continue");
                next = create.GetComponent<Interactable>();

                typed = new Typed(new[]
                {
                    (Editable(name), "Mia Chen", "type name"),
                    (Editable(mail), "mia@guild.gg", "type mail"),
                    (Editable(secret), "dragon42", "type password")
                });

                Plan(At(new Vector2(LeftColumn + 120f, FieldY(0) - 50f)), new[]
                {
                    Beat.To(At(new Vector2(LeftColumn + 80f, FieldY(0))), null, Wide(240f)),
                    Beat.Click("name"),
                    Typing("type name", "Mia Chen"),
                    Beat.To(At(new Vector2(RightColumn + 80f, FieldY(0))), null, Wide(240f)),
                    Beat.Click("mail"),
                    Typing("type mail", "mia@guild.gg"),
                    Beat.To(At(new Vector2(LeftColumn + 80f, FieldY(1))), null, Wide(240f)),
                    Beat.Click("password"),
                    Typing("type password", "dragon42"),
                    Beat.To(At(new Vector2(LeftColumn + FieldWidth - EyeInset, FieldY(1))), null, Wide(24f)),
                    Beat.Click("reveal"),
                    Beat.To(At(new Vector2(RightColumn - NumberCaption + NumberWidth - ArrowInset, FieldY(1) + ArrowRise)), null, Wide(18f)),
                    Beat.Click("older"),
                    Beat.Click("older again"),
                    Beat.To(At(new Vector2(-LeftColumn - ButtonWidth * 0.5f, Bottom + ButtonHalf)), null, Wide(ButtonWidth)),
                    Beat.Click("continue", true, true)
                }, Pace);
                Aim("name", name);
                Aim("mail", mail);
                Aim("password", secret);
                Aim("reveal", Named(secret, "Reveal"));
                Aim("older", number);
                Aim("older again", number);
                Aim("continue", create);
            }

            private GameObject Field(string label, string control, float column, int row)
            {
                Label(label, new Vector2(column, LabelY(row)), 14f, "InkDim");
                return Add(control, new Vector2(column, FieldY(row)), 0f);
            }

            private static float LabelY(int row) => Top - HeaderHeight - row * RowStep;

            private static float FieldY(int row) => LabelY(row) - LabelGap;

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                typed.Pose(played, Pointer);
                password.Revealed = played >= ClickAt("reveal");
                age.Value = AgeFrom + (played >= ClickAt("older") ? 1 : 0) + (played >= ClickAt("older again") ? 1 : 0);
                var waiting = played < Pointer.Timeline.Span("type password").y;
                if (next.Disabled != waiting) next.Disabled = waiting;
            }

            private static readonly Vector2 Board = new(1400f, 680f);
            private const float Scale = 2.25f;
            private const float Pace = 1.6f;
            private const float Top = 680f / Scale * 0.5f;
            private const float Bottom = -Top + Pad;
            private const float LeftColumn = -1400f / Scale * 0.5f + Pad;
            private const float RightColumn = LeftColumn + FieldWidth + ColumnGap;
            private const float NumberWidth = 220f;
            private const float EyeInset = 20f;
            private const float ArrowInset = 23f;

            /// <summary>How far the number field's own caption, hidden here, pushes its box right of the field's left end.</summary>
            private const float NumberCaption = 72f;

            private const float ArrowRise = 7f;
            private const int AgeFrom = 24;
        }

        /// <summary>
        /// Ready-made text fields, the second card: the new member's profile — a country picked from the combo box's list,
        /// an interest added as a tag and a bio typed until it wraps — sent with its button.
        /// </summary>
        private sealed class ProfilePart : KitPart
        {
            private static readonly string[] Countries = { "Germany", "Japan", "Brazil", "Canada" };

            private readonly Typed typed;
            private readonly UniDropdown country;
            private readonly UniTagInput interests;
            private readonly Interactable send;
            private readonly UniText sendLabel;

            public ProfilePart(GuiControlsScene scene, Stage stage)
                : base(scene, stage, "Profile", Board, Scale)
            {
                Label("Your profile", new Vector2(LeftColumn, Top - 34f), 24f, "Ink", display: true);

                Label("Country", new Vector2(LeftColumn, LabelY(0)), 14f, "InkDim");
                var combo = Add("Combo Box", new Vector2(LeftColumn, FieldY(0)), 0f);
                country = combo.GetComponent<UniDropdown>();
                country.Options.ReplaceAll(Array.ConvertAll(Countries, text => new DropdownOption(text)));
                Label("Interests", new Vector2(RightColumn, LabelY(0)), 14f, "InkDim");
                interests = Add("Tag Input", new Vector2(RightColumn, FieldY(0)), 0f).GetComponent<UniTagInput>();
                Label("Bio", new Vector2(LeftColumn, LabelY(1)), 14f, "InkDim");
                var bio = Add("Text Area", new Vector2(LeftColumn, FieldY(1) - AreaDrop), 0f);

                var create = Add("Button", new Vector2(-LeftColumn, Bottom + ButtonHalf), 1f);
                Kit.Relabel(create, "Button", Join);
                send = create.GetComponent<Interactable>();
                sendLabel = create.GetComponentInChildren<UniText>(true);

                typed = new Typed(new[] { (Editable(bio), Bio, "type bio") });

                Plan(At(new Vector2(LeftColumn + 120f, FieldY(0) - 60f)), new[]
                {
                    Beat.To(At(new Vector2(LeftColumn + 100f, FieldY(0))), null, Wide(200f)),
                    Beat.Click("country"),
                    Beat.To(At(new Vector2(LeftColumn + 100f, FieldY(0) - ListFirst - ListStep)), null, Wide(200f)),
                    Beat.Click("japan"),
                    Beat.To(At(new Vector2(RightColumn + 200f, FieldY(0))), null, Wide(120f)),
                    Beat.Click("tags"),
                    Typing("type tag", Tag),
                    Beat.Key("Enter", "commit tag"),
                    Beat.To(At(new Vector2(LeftColumn + 120f, FieldY(1) - AreaDrop)), null, Wide(240f)),
                    Beat.Click("bio"),
                    Typing("type bio", Bio),
                    Beat.To(At(new Vector2(-LeftColumn - ButtonWidth * 0.5f, Bottom + ButtonHalf)), null, Wide(ButtonWidth)),
                    Beat.Click("create", true, true)
                }, Pace);
                Aim("country", combo);
                Aim("japan", Captioned(combo, "Japan"));
                Aim("tags", interests);
                Aim("bio", bio);
                Aim("create", create);
            }

            private static float LabelY(int row) => Top - HeaderHeight - row * RowStep;

            private static float FieldY(int row) => LabelY(row) - LabelGap;

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                typed.Pose(played, Pointer);

                var listed = played >= ClickAt("country") && played < ClickAt("japan") + ListLinger;
                if (country.IsOpen != listed)
                {
                    if (listed) country.Open();
                    else country.Close();
                }

                country.Value = played >= ClickAt("japan") ? 1 : -1;

                var tagged = played >= Pointer.Timeline.Mark("commit tag");
                var tagSpan = Pointer.Timeline.Span("type tag");
                var tagText = Tag.Substring(0, Mathf.Clamp(Mathf.FloorToInt(Mathf.InverseLerp(tagSpan.x, tagSpan.y, played) * Tag.Length), 0, Tag.Length));
                interests.Remove("design");
                interests.Remove("ui");
                interests.Add(Kept);
                if (tagged) interests.Add(Tag);
                else interests.Remove(Tag);
                var tagField = interests.GetComponentInChildren<UniTextEditable>(true);
                var draft = tagged ? string.Empty : tagText;
                if (tagField.Text != draft) tagField.SetText(draft);

                var waiting = played < ClickAt("japan");
                if (send.Disabled != waiting) send.Disabled = waiting;
                var caption = played >= Pointer.Timeline.Span("create").y ? Joined : Join;
                if (sendLabel.Text != caption) sendLabel.Text = caption;
            }

            private static readonly Vector2 Board = new(1400f, 680f);
            private const float Scale = 2.25f;
            private const float Pace = 1.6f;
            private const float Top = 680f / Scale * 0.5f;
            private const float Bottom = -Top + Pad;
            private const float LeftColumn = -1400f / Scale * 0.5f + Pad;
            private const float RightColumn = LeftColumn + FieldWidth + ColumnGap;
            private const float AreaDrop = 35f;
            private const float ListFirst = 44f;
            private const float ListStep = 36f;
            private const float ListLinger = 0.15f;
            private const string Tag = "coop";
            private const string Kept = "raids";
            private const string Bio = "Healer main. Loves long raids.";
            private const string Join = "Join guild";
            private const string Joined = "Welcome, Mia!";
        }

        /// <summary>
        /// The pointer's typing into a card's fields: each field is activated while the pointer works in it, so its caret
        /// and focus ring show, and its text grows a character at a time over the pause named for it, the caret at the end.
        /// </summary>
        private sealed class Typed
        {
            private readonly (UniTextEditable field, string text, string id)[] fields;

            public Typed((UniTextEditable field, string text, string id)[] fields) => this.fields = fields;

            public void Pose(float played, Pointer pointer)
            {
                UniTextEditable active = null;
                foreach (var (field, text, id) in fields)
                {
                    var span = pointer.Timeline.Span(id);
                    var shown = Mathf.Clamp(Mathf.FloorToInt(Mathf.InverseLerp(span.x, span.y, played) * text.Length), 0, text.Length);
                    var content = text.Substring(0, shown);
                    if (field.Text != content) field.SetText(content);
                    if (played >= span.x - FocusLead && played <= span.y + FocusTail)
                    {
                        active = field;
                        field.MoveCaretTo(shown);
                    }
                }

                foreach (var (field, _, _) in fields)
                {
                    var on = field == active;
                    if (field.IsActive == on) continue;
                    if (on) field.Activate(false);
                    else field.Deactivate();
                }
            }

            private const float FocusLead = 0.3f;
            private const float FocusTail = 0.25f;
        }

        private static UniTextEditable Editable(GameObject control) =>
            control.GetComponentInChildren<UniTextEditable>(true)
            ?? throw new InvalidOperationException($"[Promo] '{control.name}' has no editable text.");

        /// <summary>A pause as long as typing <paramref name="text"/> takes, named <paramref name="id"/>.</summary>
        private static Beat Typing(string id, string text) => Beat.Wait(text.Length * KeySeconds, id);

        private const float KeySeconds = 0.06f;
        private const float Pad = 28f;
        private const float HeaderHeight = 76f;
        private const float RowStep = 76f;
        private const float LabelGap = 30f;
        private const float FieldWidth = 240f;
        private const float ColumnGap = 56f;
        private const float ButtonWidth = 140f;
        private const float ButtonHalf = 20f;
        private const float FooterGap = 14f;
    }
}
