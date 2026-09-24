#if UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace LightSideDiagnostics
{
    /// <summary>
    /// Writes the frame selected in the Profiler window to a text file, so a frame can be shared as
    /// text instead of screenshots. Reads whatever the Profiler currently holds — a live capture or a
    /// loaded .data file — and covers every thread of that frame.
    /// </summary>
    /// <remarks>
    /// Put this file under an <c>Editor</c> folder. The header lists the main-thread time of every
    /// captured frame and marks the dumped one, so a mis-selected frame is visible in the output.
    /// Rows below <see cref="MinMs"/> are omitted and counted; thread enumeration stops at the first
    /// index the capture has no data for.
    /// </remarks>
    public static class ProfilerFrameDump
    {
        private const float MinMs = 0.05f;
        private const int MaxDepth = 64;

        [MenuItem("Tools/Dump Selected Profiler Frame")]
        private static void DumpSelectedFrame()
        {
            var first = ProfilerDriver.firstFrameIndex;
            var last = ProfilerDriver.lastFrameIndex;
            if (first < 0 || last < first)
            {
                Debug.LogError("[ProfilerFrameDump] The Profiler holds no frames. Record or load a capture first.");
                return;
            }

            var windows = Resources.FindObjectsOfTypeAll<ProfilerWindow>();
            if (windows.Length == 0)
            {
                Debug.LogError("[ProfilerFrameDump] Open the Profiler window and click the frame you want dumped.");
                return;
            }

            long selected = windows[0].selectedFrameIndex;
            if (selected < first || selected > last)
            {
                Debug.LogError($"[ProfilerFrameDump] The Profiler's selected frame ({selected}) is outside the " +
                               $"captured range {first}..{last}. Click the frame you want in the Profiler chart.");
                return;
            }

            Dump((int)selected, first, last);
        }

        private static void Dump(int frame, int first, int last)
        {
            var text = new StringBuilder();
            var omitted = 0;

            text.Append("Unity ").Append(Application.unityVersion)
                .Append("  platform=").Append(Application.platform)
                .Append("  dumped frame=").Append(frame)
                .Append("  captured range=").Append(first).Append("..").Append(last)
                .AppendLine();
            text.Append("rows under ").Append(MinMs.ToString("F2", CultureInfo.InvariantCulture))
                .AppendLine(" ms are omitted").AppendLine();

            text.AppendLine("--- main-thread time per captured frame ---");
            for (var f = first; f <= last; f++)
            {
                var ms = MainThreadMs(f);
                if (ms < 0f) continue;
                text.Append("  frame ").Append(f).Append(": ")
                    .Append(ms.ToString("F2", CultureInfo.InvariantCulture))
                    .AppendLine(f == frame ? "   <-- dumped" : string.Empty);
            }
            text.AppendLine();

            for (var thread = 0; ; thread++)
            {
                using var view = ProfilerDriver.GetHierarchyFrameDataView(
                    frame, thread, HierarchyFrameDataView.ViewModes.Default,
                    HierarchyFrameDataView.columnTotalTime, false);
                if (view == null || !view.valid) break;

                text.Append("=== thread ").Append(thread).Append(" '").Append(view.threadName)
                    .Append("'  frameTime=").Append(view.frameTimeMs.ToString("F2", CultureInfo.InvariantCulture))
                    .AppendLine(" ms ===");
                AppendItem(view, view.GetRootItemID(), 0, text, ref omitted);
                text.AppendLine();
            }

            text.Append("omitted rows: ").Append(omitted).AppendLine();

            var path = Path.Combine(
                Path.GetDirectoryName(Application.dataPath) ?? ".",
                $"ProfilerFrameDump_{frame}.txt");
            File.WriteAllText(path, text.ToString());
            Debug.Log($"[ProfilerFrameDump] Frame {frame} written to {path}");
            EditorUtility.RevealInFinder(path);
        }

        private static float MainThreadMs(int frame)
        {
            using var view = ProfilerDriver.GetHierarchyFrameDataView(
                frame, 0, HierarchyFrameDataView.ViewModes.Default,
                HierarchyFrameDataView.columnTotalTime, false);
            return view != null && view.valid ? view.frameTimeMs : -1f;
        }

        private static void AppendItem(HierarchyFrameDataView view, int id, int depth,
            StringBuilder text, ref int omitted)
        {
            var total = view.GetItemColumnDataAsSingle(id, HierarchyFrameDataView.columnTotalTime);
            var self = view.GetItemColumnDataAsSingle(id, HierarchyFrameDataView.columnSelfTime);

            text.Append(' ', depth * 2)
                .Append(view.GetItemName(id))
                .Append("  total=").Append(total.ToString("F2", CultureInfo.InvariantCulture))
                .Append("ms self=").Append(self.ToString("F2", CultureInfo.InvariantCulture))
                .Append("ms calls=").Append(view.GetItemColumnData(id, HierarchyFrameDataView.columnCalls))
                .Append(" gc=").Append(view.GetItemColumnData(id, HierarchyFrameDataView.columnGcMemory))
                .AppendLine();

            if (depth >= MaxDepth || !view.HasItemChildren(id)) return;

            var children = new List<int>();
            view.GetItemChildren(id, children);
            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (view.GetItemColumnDataAsSingle(child, HierarchyFrameDataView.columnTotalTime) < MinMs)
                {
                    omitted++;
                    continue;
                }
                AppendItem(view, child, depth + 1, text, ref omitted);
            }
        }
    }
}
#endif
