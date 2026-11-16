using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide.Promo
{
    public sealed partial class GuiControlsScene
    {
        /// <summary>
        /// Ready-made data views: a guild roster table sorted by score and a member picked, then searched down to the one
        /// name typed and cleared back, beside an activity list flung through its entries.
        /// </summary>
        /// <remarks>
        /// The search narrows the table's rows to the names that contain what has been typed so far, as a roster would
        /// wire its own search field; clearing the field brings every row back.
        /// </remarks>
        private sealed class DataPart : KitPart
        {
            private static readonly string[] Activity =
            {
                "Aurora joined the raid", "Cinder found a Legendary bow", "Frost reached level 16",
                "Ember won a duel", "Dune is recruiting healers", "Gale sold 40 iron ore", "Harbor set a new record",
                "Bastion left the party", "Aurora leveled up", "Raid starts in 10 minutes", "Cinder repaired gear",
                "Frost joined the guild", "Ember crafted a shield", "Weekly chest unlocked", "Gale invited 3 friends",
                "Dune completed a quest", "Harbor bought a mount", "Season 4 begins"
            };

            private readonly UniTable table;
            private readonly UniTableHeader header;
            private readonly List<TableRow> roster = new();
            private readonly List<TableRow> shown = new();
            private readonly UniTextEditable search;
            private readonly UniLayoutScroll feed;
            private string filtered;

            public DataPart(GuiControlsScene scene, Stage stage)
                : base(scene, stage, "Data", Board, Scale)
            {
                var top = Board.y / Scale * 0.5f;
                Label("Guild roster", new Vector2(Left, top - 34f), 24f, "Ink", display: true);

                var searchField = Add("Search Field", new Vector2(-Left, top - SearchDrop), 1f);
                search = searchField.GetComponentInChildren<UniTextEditable>(true)
                         ?? throw new InvalidOperationException("[Promo] The search field has no editable text.");

                Label("Members", new Vector2(Left, CaptionY), 13f, "InkDim");
                var grid = Add("Table", new Vector2(Left, TableTop), 0f, 1f);
                var gridRect = (RectTransform)grid.transform;
                gridRect.sizeDelta = new Vector2(gridRect.sizeDelta.x, ViewHeight);
                table = grid.GetComponentInChildren<UniTable>(true);
                header = grid.GetComponentInChildren<UniTableHeader>(true);
                roster.AddRange(table.Rows);

                Label("Activity", new Vector2(ListLeft, CaptionY), 13f, "InkDim");
                var list = Add("List", new Vector2(ListLeft, TableTop), 0f, 1f);
                var listRect = (RectTransform)list.transform;
                listRect.sizeDelta = new Vector2(listRect.sizeDelta.x, ViewHeight);
                list.GetComponentInChildren<UniTextList>(true).Lines.ReplaceAll(Activity);
                feed = list.GetComponentInChildren<UniLayoutScroll>(true);

                Plan(At(new Vector2(Left + ScoreColumn, TableTop - 60f)), new[]
                {
                    Beat.To(At(new Vector2(Left + ScoreColumn, TableTop - HeaderHalf)), null, Wide(72f)),
                    Beat.Click("sort", true),
                    Beat.To(At(new Vector2(Left + 90f, RowY(1))), null, Wide(140f)),
                    Beat.Click("select"),
                    Beat.To(At(new Vector2(-Left - 140f, top - SearchDrop)), null, Wide(200f)),
                    Beat.Click("search"),
                    Beat.Wait(Query.Length * KeySeconds, "type"),
                    Beat.Wait(ReadSeconds),
                    Beat.To(At(new Vector2(-Left - ClearInset, top - SearchDrop)), null, Wide(20f)),
                    Beat.Click("clear", true),
                    Beat.To(At(new Vector2(ListLeft + 140f, ListY - FlingReach * 0.5f)), null, Wide(240f)),
                    Beat.Drag(At(new Vector2(ListLeft + 140f, ListY + FlingReach * 0.5f)), "fling", Wide(240f))
                }, Pace);
                Aim("sort", Captioned(grid, "Score"));
                Aim("select", Captioned(grid, "Harbor"));
                Aim("search", search);
                Aim("clear", Named(searchField, "Clear"));
                Aim("fling", list);
            }

            private static float RowY(int row) => TableTop - HeaderHeight - RowHeight * (row + 0.5f);

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                var sorted = played >= ClickAt("sort");
                if (header.Column != (sorted ? ScoreIndex : 0)) header.Column = sorted ? ScoreIndex : 0;
                if (header.Descending != sorted) header.Descending = sorted;

                var span = Pointer.Timeline.Span("type");
                var typed = played < ClickAt("clear")
                    ? Query.Substring(0, Mathf.Clamp(Mathf.FloorToInt(Mathf.InverseLerp(span.x, span.y, played) * Query.Length), 0, Query.Length))
                    : string.Empty;
                if (search.Text != typed) search.SetText(typed);
                var focused = played >= ClickAt("search") - FocusLead && played < ClickAt("clear");
                if (search.IsActive != focused)
                {
                    if (focused) search.Activate(false);
                    else search.Deactivate();
                }

                if (focused) search.MoveCaretTo(typed.Length);
                Filter(typed);

                var picked = played >= ClickAt("select") && typed.Length == 0 ? table.RowAt(1) : -1;
                if (table.Selected != picked) table.Selection.ReplaceAll(picked >= 0 ? new[] { picked } : Array.Empty<int>());

                var held = FlingReach * Dragged("fling", played, At(new Vector2(ListLeft + 140f, ListY - FlingReach * 0.5f)),
                    At(new Vector2(ListLeft + 140f, ListY + FlingReach * 0.5f)));
                var glide = Mathf.Clamp01((played - Pointer.Timeline.Span("fling").y) / GlideSeconds);
                feed.ScrollTo(new Vector2(0f, Mathf.Lerp(held, FlingTravel, 1f - (1f - glide) * (1f - glide))), false);
            }

            /// <summary>Shows the roster rows whose name contains <paramref name="query"/>, ignoring case.</summary>
            private void Filter(string query)
            {
                if (query == filtered) return;
                filtered = query;
                shown.Clear();
                foreach (var row in roster)
                    if (row[0].IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                        shown.Add(row);
                table.Rows.ReplaceAll(shown);
            }

            private static readonly Vector2 Board = new(BoardWidth, 700f);
            private const float BoardWidth = 1720f;
            private const float Scale = 2.1f;
            private const float Pace = 1.45f;
            private const float Pad = 28f;
            private const float Left = -BoardWidth / Scale * 0.5f + Pad;
            private const float ListLeft = Left + 440f + 40f;
            private const float SearchDrop = 36f;
            private const float CaptionY = 700f / Scale * 0.5f - 76f;
            private const float TableTop = CaptionY - 14f;

            /// <summary>How tall the table and the list stand: the table's header and six rows, the rest scrolled away.</summary>
            private const float ViewHeight = HeaderHeight + RowHeight * 6f;

            private static float ListY => TableTop - ViewHeight * 0.5f;
            private const float HeaderHeight = 32f;
            private const float HeaderHalf = 16f;
            private const float RowHeight = 30f;
            private const float ScoreColumn = 252f;
            private const int ScoreIndex = 2;
            private const float ClearInset = 20f;
            private const float FlingReach = 140f;
            private const float FlingTravel = 330f;
            private const float GlideSeconds = 0.7f;
            private const float KeySeconds = 0.09f;
            private const float ReadSeconds = 0.6f;
            private const float FocusLead = 0.3f;
            private const string Query = "fr";
        }
    }
}
