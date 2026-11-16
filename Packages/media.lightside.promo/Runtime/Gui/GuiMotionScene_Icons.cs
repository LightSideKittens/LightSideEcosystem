using UnityEngine;

namespace LightSide.Promo
{
    public sealed partial class GuiMotionScene
    {
        /// <summary>The animated icons of the icons board's actions, in the order they stand: pin, mute, save, copy, gift and delete.</summary>
        [SerializeField] private LottieAsset[] icons = System.Array.Empty<LottieAsset>();

        /// <summary>The sign-in monkey's animations: idling, covering its eyes and peeking through its fingers.</summary>
        [SerializeField] private LottieAsset[] monkey = System.Array.Empty<LottieAsset>();

        /// <summary>
        /// Lottie icons living in the kit's controls: a monkey above the kit's password field covers its eyes as the
        /// password is typed and peeks through its fingers when the field shows it, and each action the pointer presses
        /// plays its own icon.
        /// </summary>
        private sealed class IconsPart : KitPart
        {
            public IconsPart(GuiMotionScene scene, Stage stage) : base(scene, stage, "Icons", Board, Scale)
            {
                Card(stage, scene, SignAt);
                Label("Welcome back", new Vector2(SignAt.x - CardSize.x * 0.5f + 26f, CardTop), 24f, "Ink", display: true);
                for (var i = 0; i < moods.Length; i++)
                {
                    moods[i] = new Clip(scene.monkey[i], Holder, MonkeyAt, MonkeySize);
                    moods[i].Rect.gameObject.SetActive(i == 0);
                }

                var secret = Add("Password Field", FieldAt);
                ((RectTransform)secret.transform).sizeDelta = new Vector2(FieldWidth, ((RectTransform)secret.transform).sizeDelta.y);
                field = secret.GetComponentInChildren<UniTextEditable>(true);
                password = field.GetBehavior<PasswordBehavior>();
                var signIn = Add("Button", SignInAt);
                Size(signIn, new Vector2(FieldWidth, 48f));
                Kit.Relabel(signIn, "Button", "Sign in");

                Card(stage, scene, ActionsAt);
                Label("Actions", new Vector2(ActionsAt.x - CardSize.x * 0.5f + 26f, CardTop), 24f, "Ink", display: true);
                for (var i = 0; i < actions.Length; i++)
                {
                    var at = Spot(i);
                    var button = Add("Button", at);
                    Size(button, Vector2.one * ActionSize);
                    Kit.Relabel(button, "Button", string.Empty);
                    var icon = new Clip(scene.icons[i], (RectTransform)button.transform, Vector2.zero, IconSize);
                    icon.Rect.gameObject.AddComponent<UniLayout>().IgnoreLayout = true;
                    actions[i] = (button, icon);
                    Label(Captions[i], at + new Vector2(0f, -ActionSize * 0.5f - 18f), 15f, "InkDim", 0.5f);
                }

                var beats = new System.Collections.Generic.List<Beat>
                {
                    Beat.To(At(FieldAt + new Vector2(-40f, 0f)), null, Wide(FieldWidth)),
                    Beat.Click("password"),
                    Beat.Wait(Secret.Length * KeySeconds, "type"),
                    Beat.To(At(FieldAt + new Vector2(FieldWidth * 0.5f - EyeInset, 0f)), null, Wide(24f)),
                    Beat.Click("reveal", true),
                    Beat.Wait(PeekRead)
                };
                foreach (var index in Pressed)
                {
                    beats.Add(Beat.To(At(Spot(index)), null, Wide(ActionSize)));
                    beats.Add(Beat.Click("action" + index, true));
                    beats.Add(Beat.Wait(IconRead));
                }

                Plan(At(new Vector2(SignAt.x + 40f, -Top - 30f)), beats.ToArray(), Pace);
                Aim("password", field);
                foreach (var index in Pressed) Aim("action" + index, actions[index].button);
            }

            private readonly Clip[] moods = new Clip[3];
            private readonly UniTextEditable field;
            private readonly PasswordBehavior password;
            private readonly (GameObject button, Clip icon)[] actions = new (GameObject, Clip)[6];

            private static Vector2 Spot(int index) =>
                ActionsAt + new Vector2((index % 3 - 1) * ActionStep, GridTop - index / 3 * RowStep);

            private void Card(Stage stage, GuiMotionScene scene, Vector2 at)
            {
                var card = stage.Shape("Card", Holder, ShapeKind.RoundedRect, 22f);
                stage.Box(card.Rect, at, CardSize);
                Stage.Solid(card.Fill, scene.palette["Raised"]);
                Stage.AddStroke(card.Shape, scene.palette["Line"], 1f, -1f);
            }

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                var typing = Pointer.Timeline.Span("type");
                var count = Mathf.Clamp(Mathf.FloorToInt(Mathf.InverseLerp(typing.x, typing.y, played) * Secret.Length), 0, Secret.Length);
                var text = played < typing.x ? string.Empty : Secret.Substring(0, count);
                if (field.Text != text) field.SetText(text);
                var shown = played >= ClickAt("reveal");
                if (password.Revealed != shown) password.Revealed = shown;

                var mood = shown ? 2 : played >= typing.x ? 1 : 0;
                for (var i = 0; i < moods.Length; i++)
                    if (moods[i].Rect.gameObject.activeSelf != (i == mood))
                        moods[i].Rect.gameObject.SetActive(i == mood);
                var since = mood == 2 ? played - ClickAt("reveal") : mood == 1 ? played - typing.x : Mathf.Max(0f, played);
                moods[mood].Pose(since, mood == 0);

                for (var i = 0; i < actions.Length; i++)
                {
                    var clicked = System.Array.IndexOf(Pressed, i) >= 0 ? ClickAt("action" + i) : float.MaxValue;
                    actions[i].icon.Pose(played >= clicked ? played - clicked : Rest, false);
                }
            }

            private const string Secret = "dragon42";

            /// <summary>The actions in order and the ones the pointer presses, by their place.</summary>
            private static readonly string[] Captions = { "Pin", "Mute", "Save", "Copy", "Gift", "Delete" };

            private static readonly int[] Pressed = { 0, 1, 4, 5 };

            private static readonly Vector2 Board = new(1500f, 740f);
            private const float Scale = 1.6f;
            private const float Top = 740f / Scale * 0.5f - 28f;
            private static readonly Vector2 CardSize = new(410f, 400f);
            private static readonly Vector2 SignAt = new(-222f, -12f);
            private static readonly Vector2 ActionsAt = new(222f, -12f);
            private const float CardTop = -12f + 200f - 32f;
            private static readonly Vector2 MonkeyAt = new(-222f, 66f);
            private const float MonkeySize = 190f;
            private static readonly Vector2 FieldAt = new(-222f, -60f);
            private const float FieldWidth = 320f;
            private const float EyeInset = 20f;
            private static readonly Vector2 SignInAt = new(-222f, -132f);
            private const float ActionSize = 108f;
            private const float IconSize = 70f;

            /// <summary>Where an action's icon rests before it is pressed: at its end, the icon whole, as a clip that draws itself in starts empty.</summary>
            private const float Rest = 60f;
            private const float ActionStep = 122f;
            private const float GridTop = 64f;
            private const float RowStep = 150f;
            private const float KeySeconds = 0.12f;
            private const float PeekRead = 0.8f;
            private const float IconRead = 0.45f;
            private const float Pace = 1.2f;
        }
    }
}
