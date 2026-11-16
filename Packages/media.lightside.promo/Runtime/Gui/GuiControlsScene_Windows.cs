using System;
using UnityEngine;

namespace LightSide.Promo
{
    public sealed partial class GuiControlsScene
    {
        /// <summary>
        /// Ready-made windows and desktop controls: a level editor worked like a desktop app — File opened on the menu
        /// bar and Export walked into its submenu, a folder unfolded in the project tree, the split view's divider
        /// dragged wider and the floating inspector window dragged by its title and snapped to the right half.
        /// </summary>
        private sealed class WindowsPart : KitPart
        {
            private readonly UniMenuItem file;
            private readonly UniMenuItem export;
            private readonly UniTreeView tree;
            private readonly int scripts;
            private readonly UniShapeValue divider;
            private readonly UniWindow inspector;
            private readonly RectTransform inspectorRect;
            private readonly Vector2 inspectorHome;
            private bool snapped;

            public WindowsPart(GuiControlsScene scene, Stage stage)
                : base(scene, stage, "Windows", Board, Scale)
            {
                var top = Board.y / Scale * 0.5f;
                Label("Level editor", new Vector2(Left, top - 34f), 24f, "Ink", display: true);

                var bar = Add("Menu Bar", new Vector2(Left, MenuY), 0f);
                file = Item(bar, "File");
                export = Item(bar, "Export");

                var split = Add("Split View", new Vector2(Left, SplitY), 0f);
                ((RectTransform)split.transform).sizeDelta = SplitSize;
                var view = split.GetComponentInChildren<UniSplitView>(true);
                divider = view.Dividers[0];
                Banner(view.Panes[1].transform, SceneInset, 35f);
                Kit.Relabel(split, "First pane", "");
                Kit.Relabel(split, "Second pane", "");
                Kit.Relabel(split, "Third pane", "Console · 0 errors");
                var sceneTitle = stage.Label(view.Panes[1].transform, "Scene · Main hall", 14f, Color.white,
                    HorizontalAlignment.Left, VerticalAlignment.Top, face: scene.theme.DisplayFace);
                sceneTitle.Padding = new Vector4(SceneInset + PaneTitleInset, 0f, 0f, SceneInset + PaneTitleInset);
                foreach (var text in split.GetComponentsInChildren<UniText>(true))
                {
                    if (text.Text != "Console · 0 errors") continue;
                    text.HorizontalAlignment = HorizontalAlignment.Left;
                    text.VerticalAlignment = VerticalAlignment.Top;
                    text.Padding = new Vector4(PaneTitleInset, 0f, 0f, PaneTitleInset);
                }

                var project = Add("Tree", new Vector2(Left + TreeInset, SplitY), 0f);
                ((RectTransform)project.transform).sizeDelta = new Vector2(TreeWidth, SplitSize.y - TreeInset * 2f);
                tree = project.GetComponentInChildren<UniTreeView>(true);
                scripts = -1;
                for (var i = 0; i < tree.Nodes.Count; i++)
                    if (tree.Nodes[i].Text == "Scripts")
                        scripts = i;
                if (scripts < 0) throw new InvalidOperationException("[Promo] The tree no longer has a Scripts folder.");

                var window = Add("Window", WindowFrom);
                Kit.Relabel(window, "Window", "Inspector");
                Kit.Relabel(window, "Window content", "Torch · Light 2400 lm");
                inspector = window.GetComponentInChildren<UniWindow>(true);
                inspectorRect = (RectTransform)window.transform;
                inspectorHome = WindowFrom;

                Plan(At(new Vector2(Left + 40f, MenuY - 60f)), new[]
                {
                    Beat.To(At(new Vector2(Left + FileX, MenuY)), null, Wide(50f)),
                    Beat.Click("file", true),
                    Beat.To(At(new Vector2(Left + FileX + ExportDx, MenuY + ExportDy)), "export", Wide(120f)),
                    Beat.Wait(SubmenuOpen),
                    Beat.To(At(new Vector2(Left + FileX + PngDx, MenuY + PngDy)), null, Wide(100f)),
                    Beat.Click("png", true, true),
                    Beat.To(At(new Vector2(Left + TreeInset + FolderDx, SplitY + FolderDy)), null, Wide(120f)),
                    Beat.Click("folder", true),
                    Beat.To(At(new Vector2(Left + SplitSize.x * DividerFrom, SplitY)), null, Wide(12f)),
                    Beat.Drag(At(new Vector2(Left + SplitSize.x * DividerTo, SplitY)), "divider", Wide(12f)),
                    Beat.To(At(WindowFrom + TitleOffset), null, Wide(160f)),
                    Beat.Drag(At(WindowTo + TitleOffset), "window", Wide(160f)),
                    Beat.Wait(SnapRead)
                }, Pace);
                Aim("file", Captioned(bar, "File"));
                Aim("png", Captioned(bar, "PNG"));
                Aim("folder", Captioned(project, "Scripts"));
                Aim("divider", divider);
                Aim("window", Named(window, "Title Bar"));
            }

            private static UniMenuItem Item(GameObject bar, string caption)
            {
                foreach (var text in bar.GetComponentsInChildren<UniText>(true))
                    if (text.Text == caption && text.GetComponentInParent<UniMenuItem>(true) is { } item)
                        return item;
                throw new InvalidOperationException($"[Promo] The menu bar has no '{caption}' item.");
            }

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                var menus = played >= ClickAt("file") && played < ClickAt("png");
                if (menus && !file.IsOpen) file.Open(false);
                var sub = menus && played >= Pointer.Timeline.Span("export").y;
                if (sub && !export.IsOpen) export.Open(false);
                if (!sub && export.IsOpen) export.Close();
                if (!menus && file.IsOpen) file.Close();

                tree.SetExpanded(scripts, played >= ClickAt("folder"));
                var picked = played >= ClickAt("folder") ? scripts : -1;
                if (tree.Selected != picked) tree.Selection.ReplaceAll(picked >= 0 ? new[] { picked } : Array.Empty<int>());

                divider.TrackTo(Mathf.Lerp(DividerFrom, DividerTo, Dragged("divider", played,
                    At(new Vector2(Left + SplitSize.x * DividerFrom, SplitY)), At(new Vector2(Left + SplitSize.x * DividerTo, SplitY)))));

                var drag = Pointer.Timeline.Span("window");
                var released = played >= drag.y;
                if (released != snapped)
                {
                    snapped = released;
                    if (released) inspector.Snap(WindowSnap.Right);
                    else inspector.Restore();
                }

                if (!released)
                    inspectorRect.anchoredPosition = Vector2.Lerp(inspectorHome, WindowTo,
                        Dragged("window", played, At(WindowFrom + TitleOffset), At(WindowTo + TitleOffset)));
            }

            private static readonly Vector2 Board = new(BoardWidth, 750f);
            private static readonly Vector2 SplitSize = new(900f, 340f);
            private static readonly Vector2 WindowFrom = new(Left + SplitSize.x - WindowRest, SplitY - 4f);
            private static readonly Vector2 WindowTo = new(Left + SplitSize.x - WindowSnapped, SplitY - 4f);

            /// <summary>How far the inspector's middle stands from the split view's right end, where it floats and once dragged there.</summary>
            private const float WindowRest = 338f;

            private const float WindowSnapped = 158f;
            private static readonly Vector2 TitleOffset = new(0f, 96f);
            private const float BoardWidth = 1540f;
            private const float Scale = 1.6f;
            private const float Pace = 1.4f;
            private const float Pad = 28f;
            private const float Left = -BoardWidth / Scale * 0.5f + Pad;
            private const float Top = 750f / Scale * 0.5f;
            private const float MenuY = Top - 64f;
            private static float SplitY => MenuY - 32f - SplitSize.y * 0.5f;
            private const float TreeInset = 8f;
            private const float TreeWidth = 250f;
            private const float FileX = 32f;

            /// <summary>Where the File menu's Export row and its submenu's PNG row sit from the File title.</summary>
            private const float ExportDx = 72f;

            private const float ExportDy = -217f;
            private const float PngDx = 218f;
            private const float PngDy = -222f;

            /// <summary>Where the Scripts folder's disclosure arrow sits from the tree's left end and from its top.</summary>
            private const float FolderDx = 84f;

            private const float FolderDepth = 275f;

            /// <summary>Where the Scripts folder's disclosure arrow sits from the split's middle.</summary>
            private static float FolderDy => SplitSize.y * 0.5f - TreeInset - FolderDepth;
            private const float SceneInset = 10f;

            /// <summary>How far a pane's title stands in from the pane's top left corner.</summary>
            private const float PaneTitleInset = 14f;
            private const float DividerFrom = 0.3f;
            private const float DividerTo = 0.4f;
            private const float SubmenuOpen = 0.35f;
            private const float SnapRead = 0.6f;
        }
    }
}
