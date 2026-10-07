using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace LightSide.DeviceTests
{
    /// <summary>Logs the selected text under <c>[MenuTest]</c>: a command of the test assembly's own, so menus show a custom entry.</summary>
    [Serializable]
    public sealed partial class LogSelectionCommand : TextCommand
    {
        /// <inheritdoc />
        protected override string DefaultTitle => "Log selection";

        /// <inheritdoc />
        public override bool IsApplicable(in TextCommandContext context) => !context.Selectable.Selection.IsCollapsed;

        /// <inheritdoc />
        public override void Execute(in TextCommandContext context)
            => Debug.Log($"[MenuTest] custom command on {context.Selectable.name}: '{context.Selectable.GetSelectedText()}'");
    }

    /// <summary>
    /// Reports what the context menu device test scene does — selection, text, menu visibility, clipboard writes — to
    /// the log under <c>[MenuTest]</c> and on screen, switches the project's menu presentation, and runs the commands
    /// a tester appends to <c>menutest-commands.txt</c> in <see cref="Application.persistentDataPath"/>, one
    /// <c>&lt;id&gt; &lt;verb&gt; [arguments]</c> per line with ids that only grow.
    /// </summary>
    public sealed class ContextMenuDeviceTest : MonoBehaviour
    {
        private const string Tag = "[MenuTest] ";
        private const int ShownLines = 9;

        [SerializeField] private UniTextSelectable[] fields;
        [SerializeField] private Button modeButton;
        [SerializeField] private UniText modeLabel;
        [SerializeField] private UniText logView;

        private readonly List<string> lines = new();
        private readonly List<Action> unsubscribe = new();
        private bool[] menuShown;
        private string commandsPath;
        private int lastCommand;
        private float nextPoll;

        private void OnEnable()
        {
            commandsPath = Path.Combine(Application.persistentDataPath, "menutest-commands.txt");
            if (File.Exists(commandsPath)) File.Delete(commandsPath);
            menuShown = new bool[fields.Length];

            Application.logMessageReceived += OnLogMessage;
            UniTextClipboard.Written += OnClipboardWritten;
            modeButton.onClick.AddListener(ToggleMode);
            foreach (var field in fields) Watch(field);

            ShowMode();
            Report($"ready platform={Application.platform} os={SystemInfo.operatingSystem} commands={commandsPath}");
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= OnLogMessage;
            UniTextClipboard.Written -= OnClipboardWritten;
            modeButton.onClick.RemoveListener(ToggleMode);
            foreach (var action in unsubscribe) action();
            unsubscribe.Clear();
        }

        private void Watch(UniTextSelectable field)
        {
            Action<SelectionChangedArgs> selection = args =>
                Report($"selection {field.name} {args.Current.Start}..{args.Current.End} ({args.UserEvent})");
            field.SelectionChanged += selection;
            unsubscribe.Add(() => field.SelectionChanged -= selection);

            var editable = field.GetComponent<UniTextEditable>();
            if (editable == null) return;
            Action text = () => Report($"text {field.name} '{Shorten(editable.Text)}'");
            editable.TextChanged += text;
            unsubscribe.Add(() => editable.TextChanged -= text);
        }

        private void Update()
        {
            for (var i = 0; i < fields.Length; i++)
            {
                var shown = fields[i].IsContextMenuVisible;
                if (shown == menuShown[i]) continue;
                menuShown[i] = shown;
                Report($"menu {(shown ? "shown" : "hidden")} {fields[i].name}");
            }

            if (Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + 0.5f;
            PollCommands();
        }

        private void ToggleMode()
        {
            var next = UniTextSettings.AndroidMenu == TextMenuPresentation.SystemMenu
                ? TextMenuPresentation.UnityUI
                : TextMenuPresentation.SystemMenu;
            SetMode(next);
        }

        private void SetMode(TextMenuPresentation mode)
        {
            UniTextSettings.AndroidMenu = mode;
            UniTextSettings.IOSMenu = mode;
            ShowMode();
            Report("mode " + mode);
        }

        private void ShowMode() => modeLabel.Text = "Menu: " + (UniTextSettings.AndroidMenu == TextMenuPresentation.SystemMenu
            ? "System"
            : "Unity UI");

        private void OnClipboardWritten() => Report("clipboard written");

        private void PollCommands()
        {
            if (!File.Exists(commandsPath)) return;
            foreach (var line in File.ReadAllLines(commandsPath))
            {
                var parts = line.Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || !int.TryParse(parts[0], out var id) || id <= lastCommand) continue;
                lastCommand = id;
                try
                {
                    Run(parts[1], parts.Length > 2 ? parts[2] : string.Empty);
                }
                catch (Exception error)
                {
                    Report($"command {id} {parts[1]} failed: {error.Message}");
                }
            }
        }

        private void Run(string verb, string arguments)
        {
            var args = arguments.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            switch (verb)
            {
                case "select":
                {
                    var field = fields[int.Parse(args[0])];
                    var editable = field.GetComponent<UniTextEditable>();
                    if (editable != null)
                    {
                        editable.Activate(false);
                        editable.Select(int.Parse(args[1]), int.Parse(args[2]));
                    }
                    else field.SetSelection(int.Parse(args[1]), int.Parse(args[2]));
                    break;
                }
                case "keyboard":
                    fields[int.Parse(args[0])].OpenContextMenuFromKeyboard();
                    break;
                case "menu":
                {
                    var field = fields[int.Parse(args[0])];
                    var point = field.TryGetContextMenuScreenRect(out var box) ? box.center : (Vector2)field.transform.position;
                    field.RequestContextMenu(point);
                    break;
                }
                case "mode":
                    SetMode(args[0] == "system" ? TextMenuPresentation.SystemMenu : TextMenuPresentation.UnityUI);
                    break;
                case "text":
                {
                    var separator = arguments.IndexOf(' ');
                    var field = fields[int.Parse(separator < 0 ? arguments : arguments.Substring(0, separator))];
                    var text = separator < 0 ? string.Empty : arguments.Substring(separator + 1);
                    var editable = field.GetComponent<UniTextEditable>();
                    if (editable != null) editable.Text = text;
                    else field.TextComponent.Text = text;
                    break;
                }
                case "clipboard":
                    Report($"clipboard '{Shorten(UniTextClipboard.GetText())}'");
                    break;
                case "dump":
                    Dump();
                    break;
                default:
                    Report("unknown command " + verb);
                    break;
            }
        }

        private void Dump()
        {
            var entries = new TextMenuEntries();
            for (var i = 0; i < fields.Length; i++)
            {
                var field = fields[i];
                entries.Clear();
                field.CollectMenuEntries(entries);
                var text = new StringBuilder();
                text.Append($"dump {i} {field.name} selection={field.Selection.Start}..{field.Selection.End} " +
                            $"menu={field.IsContextMenuVisible} system@{entries.SystemActionsAt} entries:");
                foreach (var entry in entries.Items)
                    text.Append($" [{entry.Title}{(entry.IsEnabled ? "" : " off")}{(entry.Standard.HasValue ? " =" + entry.Standard.Value : "")}]");
                Report(text.ToString());
            }
        }

        private void OnLogMessage(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Log && !message.StartsWith(Tag, StringComparison.Ordinal)) return;
            var line = type == LogType.Log ? message.Substring(Tag.Length) : type + ": " + message;
            lines.Add(Shorten(line, 140));
            if (lines.Count > ShownLines) lines.RemoveAt(0);
            logView.Text = string.Join("\n", lines);
        }

        private static void Report(string message) => Debug.Log(Tag + message);

        private static string Shorten(string text, int length = 60)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            text = text.Replace('\n', '¶');
            return text.Length <= length ? text : text.Substring(0, length) + "…";
        }
    }
}
