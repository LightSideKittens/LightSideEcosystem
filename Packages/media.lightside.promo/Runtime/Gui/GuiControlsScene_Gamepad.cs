using UnityEngine;
using UnityEngine.EventSystems;

namespace LightSide.Promo
{
    public sealed partial class GuiControlsScene
    {
        /// <summary>
        /// A console game's pause menu worked from a gamepad, with no pointer: the kit's focus ring steps from control to
        /// control on the d-pad, A opens the settings beside the menu, right steps the music slider up, A flips vibration,
        /// right walks the difficulty to Hard and B goes back, the prompt bar lighting each button as it is pressed.
        /// </summary>
        private sealed class GamepadPart : KitPart
        {
            public GamepadPart(GuiControlsScene scene, Stage stage) : base(scene, stage, "Gamepad", Board, Scale)
            {
                this.scene = scene;
                this.stage = stage;
                Scenery(stage, Board / Scale);
                Veil();
                Label("PAUSED", new Vector2(Left, Top - 30f), 40f, "Ink", display: true);
                Label("Ember Gate · Chapter 3", new Vector2(Left, Top - 70f), 16f, "InkDim");

                items = new GameObject[Items.Length];
                for (var i = 0; i < Items.Length; i++)
                {
                    items[i] = Add("Button", MenuAt - new Vector2(0f, MenuStep * i));
                    Size(items[i], ItemSize);
                    Kit.Relabel(items[i], "Button", Items[i]);
                }

                panel = stage.Node("Settings", Holder);
                stage.Box(panel, PanelAt, PanelSize);
                panelGroup = panel.gameObject.AddComponent<CanvasGroup>();
                var card = stage.Shape("Card", panel, ShapeKind.RoundedRect, 22f);
                stage.Stretch(card.Rect);
                Stage.Solid(card.Fill, scene.palette["Surface"]);
                Stage.AddStroke(card.Shape, scene.palette["Line"], 1f, -1f);
                Stage.AddShadow(card.Shape, new Color(0f, 0f, 0f, 0.45f), new Vector2(0f, -12f), 36f);
                Text(panel, "Settings", new Vector2(RowLeft, PanelSize.y * 0.5f - 34f), 26f, "Ink", true);
                Text(panel, "Music", new Vector2(RowLeft, RowY(0)), 18f, "Ink", false);
                Text(panel, "Vibration", new Vector2(RowLeft, RowY(1)), 18f, "Ink", false);
                Text(panel, "Difficulty", new Vector2(RowLeft, RowY(2)), 18f, "Ink", false);
                music = Control("Slider", RowY(0));
                vibration = Control("Switch", RowY(1));
                Kit.Relabel(vibration, "Switch", string.Empty);
                difficulty = Control("Segmented Control", RowY(2));
                Kit.Relabel(difficulty, "Option 1", "Easy");
                Kit.Relabel(difficulty, "Option 2", "Normal");
                Kit.Relabel(difficulty, "Option 3", "Hard");
                volume = music.GetComponentInChildren<UniShapeValue>(true);
                thumb = music.GetComponentInChildren<Interactable>(true).gameObject;
                level = difficulty.GetComponent<UniShapeValue>();
                toggle = vibration.GetComponent<UniToggle>();

                buttons = new (RectTransform rect, UniShape face)[Prompts.Length];
                for (var i = 0; i < Prompts.Length; i++) buttons[i] = Prompt(i);

                var last = Inputs[Inputs.Length - 1].at;
                Plan(At(new Vector2(0f, Bottom - 200f)), new[] { Beat.Wait(last + EndHold) }, 1f);
            }

            private readonly GuiControlsScene scene;
            private readonly Stage stage;
            private readonly GameObject[] items;
            private readonly RectTransform panel;
            private readonly CanvasGroup panelGroup;
            private readonly GameObject music;
            private readonly GameObject vibration;
            private readonly GameObject difficulty;
            private readonly UniShapeValue volume;
            private readonly UniShapeValue level;
            private readonly UniToggle toggle;
            private readonly (RectTransform rect, UniShape face)[] buttons;
            private GameObject focused;
            private readonly GameObject thumb;
            private int sent;

            private static float RowY(int row) => PanelSize.y * 0.5f - 104f - row * RowStep;

            private void Text(RectTransform parent, string text, Vector2 at, float size, string swatch, bool display)
            {
                var label = stage.Label(parent, text, size, scene.palette[swatch], HorizontalAlignment.Left, stretch: false,
                    face: display ? scene.theme.DisplayFace : null);
                label.WordWrap = false;
                var rect = label.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.sizeDelta = new Vector2(PanelSize.x * 0.5f, size * 1.6f);
                rect.anchoredPosition = at;
            }

            /// <summary>The kit's <paramref name="control"/> on the settings panel's row at <paramref name="y"/>, by its right end.</summary>
            private GameObject Control(string control, float y)
            {
                var instance = Add(control, Vector2.zero);
                instance.transform.SetParent(panel, false);
                Kit.Place(instance, new Vector2(PanelSize.x * 0.5f - 28f, y), 1f);
                return instance;
            }

            /// <summary>A button of the prompt bar: the gamepad button's face and what it does, lit while pressed.</summary>
            private (RectTransform rect, UniShape face) Prompt(int index)
            {
                var (glyph, caption, tone) = Prompts[index];
                var at = PromptAt + new Vector2(index * PromptStep, 0f);
                var face = stage.Shape("Prompt " + glyph, Holder, glyph == "✚" ? ShapeKind.RoundedRect : ShapeKind.Circle,
                    glyph == "✚" ? 9f : 0f);
                stage.Box(face.Rect, at, Vector2.one * PromptSize);
                Stage.Solid(face.Fill, tone);
                Stage.AddShadow(face.Shape, new Color(0f, 0f, 0f, 0.4f), new Vector2(0f, -3f), 6f);
                var mark = stage.Label(face.Rect, glyph, PromptSize * 0.55f, Color.white, stretch: true, face: scene.theme.DisplayFace);
                mark.WordWrap = false;
                Label(caption, at + new Vector2(PromptSize * 0.5f + 10f, 0f), 17f, "Ink");
                return (face.Rect, face.Shape);
            }

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                Pointer.Visible = false;
                var slide = Ease.Emphasized.Window(played, OpenAt, PanelFor) - Ease.Emphasized.Window(played, BackAt, PanelFor);
                var open = played >= OpenAt && played < BackAt + PanelFor;
                panel.anchoredPosition = PanelAt + new Vector2(PanelSlide * (1f - slide), 0f);
                panelGroup.alpha = slide;
                if (panel.gameObject.activeSelf != open) panel.gameObject.SetActive(open);

                var target = Focus(played, out var flipped, out var harder);
                if (target != focused && target.activeInHierarchy && EventSystem.current != null)
                {
                    focused = target;
                    EventSystem.current.SetSelectedGameObject(target, new AxisEventData(EventSystem.current));
                }

                if (played < Inputs[0].at && !Mathf.Approximately(volume.Value, MusicFrom)) volume.SnapTo(MusicFrom);
                while (sent < Inputs.Length && played >= Inputs[sent].at)
                {
                    if (Inputs[sent].button == Right && target == thumb && EventSystem.current != null)
                        ExecuteEvents.Execute(thumb, new AxisEventData(EventSystem.current) { moveDir = MoveDirection.Right, moveVector = Vector2.right },
                            ExecuteEvents.moveHandler);
                    sent++;
                }
                if (toggle.On != flipped) toggle.On = flipped;
                if (!Mathf.Approximately(level.Value, harder)) level.Value = harder;

                for (var i = 0; i < buttons.Length; i++)
                {
                    var press = 0f;
                    foreach (var (at, button) in Inputs)
                        if (PromptOf(button) == i) press = Mathf.Max(press, Pressing(played - at));
                    buttons[i].rect.localScale = Vector3.one * (1f - PromptGive * press);
                    Stage.Alpha(buttons[i].face, 0.55f + 0.45f * press);
                }
            }

            /// <summary>How far a button is held in <paramref name="since"/> seconds after its press: down at once, up over a beat.</summary>
            private static float Pressing(float since) => since < 0f || since > PressFor ? 0f : 1f - since / PressFor;

            /// <summary>
            /// What holds the focus at <paramref name="played"/>, following the presses so far, and what the presses have
            /// done by then: whether vibration is on and the difficulty picked; the slider takes its steps from the presses
            /// themselves.
            /// </summary>
            private GameObject Focus(float played, out bool flipped, out int harder)
            {
                var item = 0;
                var row = -1;
                var open = false;
                flipped = false;
                harder = 0;
                foreach (var (at, button) in Inputs)
                {
                    if (played < at) break;
                    switch (button)
                    {
                        case Down when !open:
                            item = Mathf.Min(item + 1, Items.Length - 1);
                            break;
                        case Down:
                            row = Mathf.Min(row + 1, 2);
                            break;
                        case Right when row == 2:
                            harder = Mathf.Min(harder + 1, 2);
                            break;
                        case Confirm when !open:
                            open = true;
                            row = 0;
                            break;
                        case Confirm when row == 1:
                            flipped = !flipped;
                            break;
                        case Back:
                            open = false;
                            row = -1;
                            break;
                    }
                }

                var control = row switch { 0 => music, 1 => vibration, 2 => difficulty, _ => items[item] };
                return control.GetComponentInChildren<Interactable>(true).gameObject;
            }

            private static readonly string[] Items = { "Resume", "Settings", "Controls", "Quit" };

            /// <summary>The presses the board makes, each lighting the prompt of its button: the d-pad's down and right share the d-pad's.</summary>
            private const int Down = 0;

            private const int Confirm = 1;
            private const int Back = 2;
            private const int Right = 3;

            private static int PromptOf(int button) => button == Right ? Down : button;

            /// <summary>The prompt bar's buttons in order: the d-pad, A and B, what each does and its color.</summary>
            private static readonly (string glyph, string caption, Color tone)[] Prompts =
            {
                ("✚", "Navigate", new Color32(0x5A, 0x50, 0x4E, 0xFF)), ("A", "Select", new Color32(0x3C, 0xB8, 0x5A, 0xFF)),
                ("B", "Back", new Color32(0xE0, 0x4A, 0x4A, 0xFF))
            };

            /// <summary>
            /// The presses in order, in seconds from the action's start: the d-pad down to Settings, A, three rights on the
            /// slider, down, A on vibration, down, two rights on the difficulty, B.
            /// </summary>
            private static readonly (float at, int button)[] Inputs =
            {
                (0.5f, Down), (1.0f, Confirm), (1.55f, Right), (1.9f, Right), (2.25f, Right), (2.8f, Down), (3.25f, Confirm),
                (3.8f, Down), (4.25f, Right), (4.65f, Right), (5.2f, Back)
            };

            private const float OpenAt = 1.0f;
            private const float BackAt = 5.2f;
            private const float EndHold = 0.8f;
            private static readonly Vector2 Board = new(1640f, 800f);
            private const float Scale = 1.7f;
            private const float Pad = 28f;
            private const float Left = -1640f / Scale * 0.5f + Pad;
            private const float Top = 800f / Scale * 0.5f - Pad;
            private const float Bottom = -Top;
            private static readonly Vector2 MenuAt = new(Left + 150f, 90f);
            private const float MenuStep = 64f;
            private static readonly Vector2 ItemSize = new(280f, 52f);
            private static readonly Vector2 PanelAt = new(170f, 6f);
            private static readonly Vector2 PanelSize = new(540f, 330f);
            private const float PanelSlide = 60f;
            private const float PanelFor = 0.35f;
            private const float RowLeft = -540f * 0.5f + 28f;
            private const float RowStep = 72f;
            private const float MusicFrom = 0.4f;
            private static readonly Vector2 PromptAt = new(Left + 24f, Bottom + 28f);
            private const float PromptStep = 210f;
            private const float PromptSize = 40f;
            private const float PromptGive = 0.18f;
            private const float PressFor = 0.3f;
        }
    }
}
