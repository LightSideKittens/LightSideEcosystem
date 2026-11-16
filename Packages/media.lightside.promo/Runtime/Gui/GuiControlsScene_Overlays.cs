using System;
using UnityEngine;

namespace LightSide.Promo
{
    public sealed partial class GuiControlsScene
    {
        /// <summary>
        /// Ready-made overlays: an inbox worked through everything that floats over it — a tooltip on hover, a popover
        /// on a click, a context menu on a message whose Delete asks a modal dialog, the message leaving the list while
        /// the one below moves up and a toast confirms it, and a share sheet risen from the bottom and swiped back down.
        /// </summary>
        /// <remarks>
        /// The inbox is a column of the kit's layout that moves its children, so a dismissed message leaves the flow and
        /// its neighbor closes up as the layout's own motion. The full-screen overlays — the dialog and the sheet —
        /// stretch over the card, so their scrims dim the inbox they belong to rather than the whole frame.
        /// </remarks>
        private sealed class OverlaysPart : KitPart
        {
            private readonly Surface tip;
            private readonly Surface sort;
            private readonly Surface menu;
            private readonly UniMessages dialogs;
            private readonly Surface sheet;
            private readonly Grip swipe;
            private readonly UniMessages toasts;
            private readonly RectTransform pressPoint;
            private readonly UniLayout inbox;
            private readonly RectTransform thread;
            private readonly RectTransform restPoint;
            private readonly UniShape veil;
            private bool shared;
            private bool toasted;
            private bool asked;
            private bool answered;
            private bool dismissed;

            public OverlaysPart(GuiControlsScene scene, Stage stage)
                : base(scene, stage, "Overlays", Board, Scale)
            {
                Label("Messages", new Vector2(Left, Top - 34f), 24f, "Ink", display: true);

                var filter = Add("Tooltip", new Vector2(ToolX(0), ToolbarY), 0f);
                Kit.Relabel(filter, "Hover me", "Filters");
                Kit.Relabel(filter, "A helpful hint", "Show unread only");
                tip = Surfaces(filter)[0];

                var order = Add("Popover", new Vector2(ToolX(1), ToolbarY), 0f);
                Kit.Relabel(order, "Open", "Sort");
                Kit.Relabel(order, "Popover", "Sort by");
                Kit.Relabel(order, "Anything goes here.", "Newest first");
                sort = Surfaces(order)[0];

                var share = Add("Button", new Vector2(ToolX(2), ToolbarY), 0f);
                Kit.Relabel(share, "Button", "Share");

                var column = stage.Node("Inbox", Holder);
                column.anchorMin = column.anchorMax = new Vector2(0.5f, 0.5f);
                column.pivot = new Vector2(0f, 1f);
                column.anchoredPosition = new Vector2(Left, InboxTop);
                column.sizeDelta = new Vector2(InboxWidth, RowHeight * 3f + RowSpacing * 2f);
                inbox = column.gameObject.AddComponent<UniLayout>();
                inbox.Mode = LayoutMode.Column;
                inbox.Gap = new Vector2(0f, RowSpacing);
                inbox.Width = LayoutSize.Fixed;
                inbox.Height = LayoutSize.Fixed;
                inbox.Motion = LayoutMotion.Gentle;

                Message(stage, scene, Card(stage, scene, column), "Aurora", "Raid tonight at 8? We need a healer.", "A", 30f);
                var control = Add("Context Menu", Vector2.zero, 0f);
                thread = (RectTransform)control.transform;
                thread.SetParent(column, false);
                thread.sizeDelta = new Vector2(InboxWidth, RowHeight);
                Fixed(thread);
                Kit.Relabel(control, "Right-click or hold", "");
                Message(stage, scene, thread, "Cinder", "Selling a Legendary bow, cheap.", "C", 90f);
                menu = Surfaces(control)[0];
                Message(stage, scene, Card(stage, scene, column), "Frost", "Thanks for the carry yesterday!", "F", 150f);

                veil = Veil();

                pressPoint = stage.Node("Press Point", Holder);
                stage.Box(pressPoint, new Vector2(MenuAimX, ThreadY), Vector2.zero);
                restPoint = stage.Node("Rest Point", Holder);
                stage.Box(restPoint, TipRest, Vector2.zero);

                var asking = Add("Dialogs", Vector2.zero);
                Stretch(asking);
                dialogs = asking.GetComponent<UniMessages>();

                var shareSheet = Add("Sheet", Vector2.zero);
                Stretch(shareSheet);
                Kit.Relabel(shareSheet, "Sheet", "Share with your guild");
                Kit.Relabel(shareSheet, "Slides up from the bottom edge; drag it down to put it away.",
                    "Copy link · Send to Silver Knights · Post to Discord");
                sheet = Surfaces(shareSheet)[0];
                var handle = Named(shareSheet, "Handle");
                swipe = new Grip(handle()?.gameObject ?? throw new InvalidOperationException("[Promo] The sheet has no handle."), "swipe");

                toasts = Add("Toasts", new Vector2(0f, -Top + ToastLift)).GetComponentInChildren<UniMessages>(true);

                Plan(At(new Vector2(ToolX(0) + ButtonWidth * 0.5f, ToolbarY - 70f)), new[]
                {
                    Beat.To(At(TipRest), "hover", Wide(ButtonWidth)),
                    Beat.Wait(TipRead),
                    Beat.To(At(new Vector2(ToolX(1) + ButtonWidth * 0.5f, ToolbarY)), null, Wide(ButtonWidth)),
                    Beat.Click("sort", true),
                    Beat.To(At(new Vector2(MenuAimX, ThreadY)), null, Wide(240f)),
                    Beat.Click("menu"),
                    Beat.To(At(new Vector2(MenuAimX + DeleteDx, ThreadY + DeleteDy)), null, Wide(120f)),
                    Beat.Click("delete", true),
                    Beat.To(At(new Vector2(ConfirmX, ConfirmY)), null, Wide(90f)),
                    Beat.Click("confirm", true, true),
                    Beat.Wait(RemoveRead),
                    Beat.To(At(new Vector2(ToolX(2) + ButtonWidth * 0.5f, ToolbarY)), null, Wide(ButtonWidth)),
                    Beat.Click("share", true),
                    Beat.To(At(new Vector2(HandleX, HandleY)), null, Wide(60f)),
                    Beat.Drag(At(new Vector2(HandleX, HandleY - SwipeReach)), "swipe", Wide(60f))
                }, Pace);
                Aim("sort", Captioned(order, "Sort"));
                Aim("menu", control);
                Aim("delete", Captioned(control, "Delete"));
                Aim("confirm", Captioned(asking, ConfirmCaption));
                Aim("share", share);
                Aim("swipe", handle);
            }

            /// <summary>A message card in the inbox, drawn as the kit's raised surface, its size fixed for the column.</summary>
            private static RectTransform Card(Stage stage, GuiControlsScene scene, RectTransform column)
            {
                var card = stage.Shape("Message", column, ShapeKind.RoundedRect, CardRadius);
                card.Rect.sizeDelta = new Vector2(InboxWidth, RowHeight);
                Stage.Solid(card.Fill, scene.palette["Raised"]);
                Stage.AddStroke(card.Shape, scene.palette["Line"], 1f, -1f);
                Fixed(card.Rect);
                return card.Rect;
            }

            private static void Fixed(RectTransform row)
            {
                var size = row.TryGetComponent<UniLayout>(out var own) ? own : row.gameObject.AddComponent<UniLayout>();
                size.Width = LayoutSize.Fixed;
                size.Height = LayoutSize.Fixed;
            }

            /// <summary>The sender's avatar, name and line on a message row, from its left end.</summary>
            private void Message(Stage stage, GuiControlsScene scene, RectTransform row, string sender, string text, string initial,
                float angle)
            {
                var face = Avatar(Vector2.zero, AvatarSize, initial, angle);
                face.SetParent(row, false);
                face.anchoredPosition = new Vector2(RowLeft + AvatarSize * 0.5f, 0f);
                var name = stage.Label(row, sender, 16f, scene.palette["Ink"], HorizontalAlignment.Left, stretch: false,
                    face: scene.theme.DisplayFace);
                Pin(name.rectTransform, new Vector2(TextLeft, NameRise));
                var line = stage.Label(row, text, 14f, scene.palette["InkDim"], HorizontalAlignment.Left, stretch: false);
                Pin(line.rectTransform, new Vector2(TextLeft, -LineDrop));
            }

            private static void Pin(RectTransform rect, Vector2 at)
            {
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.sizeDelta = new Vector2(InboxWidth - AvatarSize - RowInset * 3f, 24f);
                rect.anchoredPosition = at;
            }

            /// <summary>Where toolbar button <paramref name="index"/>'s left end stands, the three right-aligned with gaps.</summary>
            private static float ToolX(int index) => -Left - (3 - index) * ButtonWidth - (2 - index) * ButtonGap;

            private static Surface[] Surfaces(GameObject control)
            {
                var found = control.GetComponentsInChildren<Surface>(true);
                if (found.Length == 0) throw new InvalidOperationException($"[Promo] '{control.name}' has no surface.");
                return found;
            }

            private static void Stretch(GameObject overlay)
            {
                var rect = (RectTransform)overlay.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }

            private static void Show(Surface surface, bool open)
            {
                if (surface.IsOpen == open) return;
                if (open) surface.Open();
                else surface.Close();
            }

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                if (tip.Anchor != restPoint) tip.Anchor = restPoint;
                Show(tip, played >= Pointer.Timeline.Span("hover").y && played < ClickAt("sort") - TipLead);
                Show(sort, played >= ClickAt("sort") && played < ClickAt("menu"));
                if (menu.Anchor != pressPoint) menu.Anchor = pressPoint;
                Show(menu, played >= ClickAt("menu") && played < ClickAt("delete"));
                if (played >= ClickAt("delete") && !asked)
                {
                    asked = true;
                    _ = dialogs.Confirm(Question, Warning, ConfirmCaption, destructive: true);
                }

                if (played >= ClickAt("confirm") && !answered)
                {
                    answered = true;
                    Click(Action(dialogs, ConfirmCaption).gameObject);
                }

                if (played >= ClickAt("confirm") + RemoveLead && !dismissed)
                {
                    dismissed = true;
                    inbox.Dismiss(thread);
                }

                var removed = played >= ClickAt("confirm");
                if (removed && !toasted) toasts.Show(new MessageRequest { Template = "Success", Title = "Deleted", Text = "Message removed", Timeout = ToastStay });
                if (!removed && toasted) toasts.Clear();
                toasted = removed;

                if (played >= ClickAt("share") && !shared)
                {
                    shared = true;
                    sheet.Open();
                }

                Pose(swipe, played);
                Stage.Alpha(veil, Mathf.Max(Openness(dialogs), sheet.Openness));
            }

            /// <summary>The shown action of <paramref name="presenter"/> captioned <paramref name="caption"/>.</summary>
            private static UniButton Action(UniMessages presenter, string caption)
            {
                foreach (var button in presenter.GetComponentsInChildren<UniButton>())
                    if (button.GetComponentInChildren<UniText>(true).Text == caption)
                        return button;
                throw new InvalidOperationException($"[Promo] '{presenter.name}' shows no '{caption}' action.");
            }

            /// <summary>How far open the most open message of <paramref name="presenter"/> stands.</summary>
            private static float Openness(UniMessages presenter)
            {
                var open = 0f;
                foreach (var surface in presenter.GetComponentsInChildren<Surface>(true))
                    open = Mathf.Max(open, surface.Openness);
                return open;
            }

            private static readonly Vector2 Board = new(1600f, 600f);
            private const float Scale = 2f;
            private const float Pace = 1.4f;
            private const float Pad = 28f;
            private const float Top = 600f / Scale * 0.5f;
            private const float Left = -1600f / Scale * 0.5f + Pad;
            private const float ButtonWidth = 140f;
            private const float ButtonGap = 10f;
            private const float ToolbarY = Top - 36f;
            private const float InboxTop = Top - 76f;
            private const float InboxWidth = -Left * 2f;
            private const float RowHeight = 58f;
            private const float RowSpacing = 10f;
            private const float CardRadius = 8f;
            private const float RowInset = 14f;
            private const float RowLeft = -InboxWidth * 0.5f + RowInset;
            private const float TextLeft = RowLeft + AvatarSize + RowInset;
            private const float NameRise = 10f;
            private const float LineDrop = 12f;
            private const float AvatarSize = 38f;

            /// <summary>The middle of the second message, the one the context menu opens on.</summary>
            private const float ThreadY = InboxTop - RowHeight * 1.5f - RowSpacing;

            private const float MenuAimX = TextLeft + 160f;

            /// <summary>Where the context menu's Delete row sits from the press it opened at, and the dialog's confirm button.</summary>
            private const float DeleteDx = 80f;

            private const float DeleteDy = -88f;
            private const float ConfirmX = 105f;
            private const float ConfirmY = -33f;
            private const float HandleX = 6f;

            /// <summary>The sheet's handle, a fixed height above the card's bottom edge.</summary>
            private const float HandleY = -Top + 92.7f;

            private const float SwipeReach = 160f;
            private const float ToastLift = 40f;

            /// <summary>Where the pointer rests on the filter button, the point its tooltip opens beside.</summary>
            private static readonly Vector2 TipRest = new(ToolX(0) + ButtonWidth * 0.5f + 10f, ToolbarY);

            private const float TipRead = 0.6f;
            private const string Question = "Delete message?";
            private const string Warning = "Cinder's message will be gone for good.";
            private const string ConfirmCaption = "Delete";

            /// <summary>How long after the confirmation the message leaves the inbox, and how long the closing is watched.</summary>
            private const float RemoveLead = 0.15f;

            private const float RemoveRead = 0.5f;
            private const float TipLead = 0.25f;
            private const float ToastStay = 30f;
        }
    }
}
