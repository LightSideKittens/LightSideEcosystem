using System;
using System.Diagnostics;
using UnityEngine.LowLevel;

namespace LightSide.Benchmark
{
    /// <summary>
    /// Stopwatch windows over the player loop, refreshed every frame once installed.
    /// The update window runs from the end of <c>Initialization</c> to the end of <c>PreLateUpdate</c>:
    /// the frame's script and engine work, with rendering, present and frame pacing left outside. It must
    /// close before <c>PostLateUpdate</c> — that is where the display wait lives, and a window spanning it
    /// reads the refresh interval instead of the work, which on a paced device hides everything cheaper than
    /// one frame. It covers every participant that ticks in Update or LateUpdate; work scheduled into
    /// <c>PostLateUpdate</c> goes unseen.
    /// The canvas window spans <c>PostLateUpdate.PlayerUpdateCanvases</c>, where <c>Canvas.preWillRenderCanvases</c>
    /// and <c>Canvas.willRenderCanvases</c> run: uGUI layout and every text engine that rebuilds there.
    /// The frame window runs from the end of <c>Initialization</c> to the start of
    /// <c>PostLateUpdate.PresentAfterDraw</c>: all main-thread work of the frame including render submission,
    /// without the wait for the previous present, which the player loop performs before <c>Initialization</c>.
    /// Allocated bytes and collections cover the frame window on the main thread only.
    /// </summary>
    /// <remarks>
    /// Every value describes the most recent frame whose window closed, so a coroutine reading after
    /// <c>yield return null</c> sees the frame in which it last ran.
    /// </remarks>
    public static class BenchmarkFrameProbe
    {
        private struct StartMarker { }
        private struct EndMarker { }
        private struct CanvasStartMarker { }
        private struct CanvasEndMarker { }
        private struct FrameEndMarker { }

        private static long startTimestamp;
        private static long canvasTimestamp;
        private static long startAllocatedBytes;
        private static int startCollections;
        private static bool installed;

        /// <summary>Milliseconds the update window of the most recently completed frame spanned.</summary>
        public static double LastMilliseconds { get; private set; }

        /// <summary>Milliseconds the canvas window of the most recently completed frame spanned.</summary>
        public static double LastCanvasMilliseconds { get; private set; }

        /// <summary>Milliseconds the frame window of the most recently completed frame spanned.</summary>
        public static double LastFrameMilliseconds { get; private set; }

        /// <summary>Managed bytes the main thread allocated inside the frame window of the most recently completed frame.</summary>
        public static long LastFrameAllocatedBytes { get; private set; }

        /// <summary>Garbage collections that completed inside the frame window of the most recently completed frame.</summary>
        public static int LastFrameCollections { get; private set; }

        /// <summary>Adds the probe's markers to the current player loop; a second call while installed does nothing.</summary>
        public static void Install()
        {
            if (installed) return;
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            Insert(ref loop, typeof(UnityEngine.PlayerLoop.Initialization), null, false,
                typeof(StartMarker), BeginFrame);
            Insert(ref loop, typeof(UnityEngine.PlayerLoop.PreLateUpdate), null, false,
                typeof(EndMarker), EndUpdate);
            Insert(ref loop, typeof(UnityEngine.PlayerLoop.PostLateUpdate),
                typeof(UnityEngine.PlayerLoop.PostLateUpdate.PlayerUpdateCanvases), false,
                typeof(CanvasStartMarker), BeginCanvas);
            Insert(ref loop, typeof(UnityEngine.PlayerLoop.PostLateUpdate),
                typeof(UnityEngine.PlayerLoop.PostLateUpdate.PlayerUpdateCanvases), true,
                typeof(CanvasEndMarker), EndCanvas);
            Insert(ref loop, typeof(UnityEngine.PlayerLoop.PostLateUpdate),
                typeof(UnityEngine.PlayerLoop.PostLateUpdate.PresentAfterDraw), false,
                typeof(FrameEndMarker), EndFrame);
            PlayerLoop.SetPlayerLoop(loop);
            installed = true;
        }

        /// <summary>Removes the probe's markers from the current player loop.</summary>
        public static void Uninstall()
        {
            if (!installed) return;
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            Remove(ref loop, typeof(UnityEngine.PlayerLoop.Initialization), typeof(StartMarker));
            Remove(ref loop, typeof(UnityEngine.PlayerLoop.PreLateUpdate), typeof(EndMarker));
            Remove(ref loop, typeof(UnityEngine.PlayerLoop.PostLateUpdate), typeof(CanvasStartMarker));
            Remove(ref loop, typeof(UnityEngine.PlayerLoop.PostLateUpdate), typeof(CanvasEndMarker));
            Remove(ref loop, typeof(UnityEngine.PlayerLoop.PostLateUpdate), typeof(FrameEndMarker));
            PlayerLoop.SetPlayerLoop(loop);
            installed = false;
        }

        private static void BeginFrame()
        {
            startAllocatedBytes = GC.GetAllocatedBytesForCurrentThread();
            startCollections = GC.CollectionCount(0);
            startTimestamp = Stopwatch.GetTimestamp();
        }

        private static void EndUpdate() => LastMilliseconds = Since(startTimestamp);

        private static void BeginCanvas() => canvasTimestamp = Stopwatch.GetTimestamp();

        private static void EndCanvas() => LastCanvasMilliseconds = Since(canvasTimestamp);

        private static void EndFrame()
        {
            LastFrameMilliseconds = Since(startTimestamp);
            LastFrameAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - startAllocatedBytes;
            LastFrameCollections = GC.CollectionCount(0) - startCollections;
        }

        private static double Since(long timestamp) =>
            (Stopwatch.GetTimestamp() - timestamp) * 1000.0 / Stopwatch.Frequency;

        private static void Insert(ref PlayerLoopSystem root, Type phaseType, Type anchorType, bool afterAnchor,
            Type markerType, PlayerLoopSystem.UpdateFunction callback)
        {
            var phases = root.subSystemList;
            for (var i = 0; i < phases.Length; i++)
            {
                if (phases[i].type != phaseType) continue;
                var children = phases[i].subSystemList ?? Array.Empty<PlayerLoopSystem>();
                var at = children.Length;
                if (anchorType != null)
                {
                    at = -1;
                    for (var j = 0; j < children.Length; j++)
                        if (children[j].type == anchorType)
                        {
                            at = afterAnchor ? j + 1 : j;
                            break;
                        }
                    if (at < 0)
                        throw new InvalidOperationException(
                            $"The player loop is missing {phaseType.Name}.{anchorType.Name}.");
                }
                var grown = new PlayerLoopSystem[children.Length + 1];
                Array.Copy(children, grown, at);
                grown[at] = new PlayerLoopSystem { type = markerType, updateDelegate = callback };
                Array.Copy(children, at, grown, at + 1, children.Length - at);
                phases[i].subSystemList = grown;
                return;
            }
            throw new InvalidOperationException($"The player loop is missing its {phaseType.Name} phase.");
        }

        private static void Remove(ref PlayerLoopSystem root, Type phaseType, Type markerType)
        {
            var phases = root.subSystemList;
            for (var i = 0; i < phases.Length; i++)
            {
                if (phases[i].type != phaseType || phases[i].subSystemList == null) continue;
                var children = phases[i].subSystemList;
                for (var j = 0; j < children.Length; j++)
                {
                    if (children[j].type != markerType) continue;
                    var shrunk = new PlayerLoopSystem[children.Length - 1];
                    Array.Copy(children, shrunk, j);
                    Array.Copy(children, j + 1, shrunk, j, children.Length - j - 1);
                    phases[i].subSystemList = shrunk;
                    break;
                }
            }
        }
    }
}
