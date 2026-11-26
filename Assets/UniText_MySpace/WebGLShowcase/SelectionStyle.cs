using UnityEngine;

namespace LightSide.Showcase
{
    /// <summary>Dresses the selected letters of a field in a style of their own instead of the plain highlight.</summary>
    [RequireComponent(typeof(UniTextEditable), typeof(UniTextSelectable))]
    public sealed class SelectionStyle : MonoBehaviour
    {
        [Tooltip("What the selected letters wear while the field is edited.")]
        [SerializeField] private ModifierGraphPreset style;

        private readonly MutableRangeSource selected = new();
        private UniTextEditable field;
        private UniTextBase text;

        private void Awake()
        {
            field = GetComponent<UniTextEditable>();
            text = GetComponent<UniTextBase>();
            text.Styles.Add(Style.FromSource(selected, new ModifierGraphModifier { Preset = style }));
            GetComponent<UniTextSelectable>().SelectionHighlight.Tint = new Color32(255, 255, 255, 0);
        }

        private void OnEnable()
        {
            field.SelectionChanged += OnSelectionChanged;
            field.Focused += Refresh;
            field.Defocused += OnDefocused;
            text.Committed += OnCommitted;
        }

        private void OnDisable()
        {
            field.SelectionChanged -= OnSelectionChanged;
            field.Focused -= Refresh;
            field.Defocused -= OnDefocused;
            text.Committed -= OnCommitted;
        }

        private void OnSelectionChanged(int start, int end) => Refresh();

        private void OnDefocused(EditingEndReason reason) => Refresh();

        private void OnCommitted(UniTextCommitChanges changes)
        {
            if ((changes & UniTextCommitChanges.Content) != 0) Refresh();
        }

        /// <summary>Dresses the current selection; while the text waits to be parsed, the selection may already belong to the text to come, and the commit of that text refreshes again.</summary>
        private void Refresh()
        {
            if ((text.CurrentDirtyFlags & UniTextDirty.FullRebuild) != 0) return;
            var snapshot = selected.Snapshot;
            var selection = field.Selection;
            if (!snapshot.IsValid || Dresses(selection)) return;
            using var update = selected.BeginUpdate(snapshot.Revision);
            update.Clear();
            if (!selection.IsCollapsed) update.Add(new TextRange(selection.Start, selection.Length));
            update.Commit();
        }

        private bool Dresses(TextSelection selection)
        {
            var entities = selected.CaptureEntities();
            if (entities.Length == 0) return selection.IsCollapsed;
            var range = entities[0].Segments[0].Range;
            return range.start == selection.Start && range.End == selection.End;
        }
    }
}
