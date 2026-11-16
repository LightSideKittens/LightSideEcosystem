using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// Pixels of a floating editor window, read from the window system rather than from the editor's renderer.
    /// </summary>
    /// <remarks>
    /// <c>PrintWindow</c> with full-content rendering copies what the compositor holds for the window, so the window
    /// can sit behind every other window on the desktop and still be photographed, without focus or input. The
    /// editor's own pixel grab is not an alternative: on D3D12 it crashes the editor on its first call.
    /// </remarks>
    internal sealed class WindowCapture
    {
        private readonly IntPtr handle;
        private byte[] bits;

        private WindowCapture(IntPtr handle, Vector2 clientOrigin, float pixelsPerPoint)
        {
            this.handle = handle;
            ClientOrigin = clientOrigin;
            PixelsPerPoint = pixelsPerPoint;
        }

        /// <summary>Where the window's panel origin lands in a capture, in pixels from its top-left corner.</summary>
        public Vector2 ClientOrigin { get; }

        /// <summary>Capture pixels per panel point.</summary>
        public float PixelsPerPoint { get; }

        /// <summary>The capture pixel under panel <paramref name="point"/>.</summary>
        public Vector2 Pixel(Vector2 point) => ClientOrigin + point * PixelsPerPoint;

        /// <summary>The panel point under capture <paramref name="pixel"/>.</summary>
        public Vector2 Point(Vector2 pixel) => (pixel - ClientOrigin) / PixelsPerPoint;

        /// <summary>Finds the window system's window that hosts <paramref name="window"/>.</summary>
        /// <exception cref="InvalidOperationException">No visible window of this process hosts a panel of that size.</exception>
        public static WindowCapture Of(EditorWindow window)
        {
#if UNITY_EDITOR_WIN
            var panel = window.rootVisualElement.panel.visualTree.worldBound.size;
            var process = (uint)Process.GetCurrentProcess().Id;
            var best = IntPtr.Zero;
            var bestError = float.MaxValue;
            var bestOrigin = Vector2.zero;
            var bestScale = 1f;

            EnumWindows((hwnd, _) =>
            {
                GetWindowThreadProcessId(hwnd, out var owner);
                if (owner != process || !IsWindowVisible(hwnd) || IsIconic(hwnd)) return true;
                if (!GetClientRect(hwnd, out var client) || client.Right <= 0) return true;

                var scale = client.Right / panel.x;
                var error = Mathf.Abs(client.Bottom - panel.y * scale);
                if (scale < 0.9f || scale > 4.1f || error > client.Bottom * 0.03f || error >= bestError) return true;

                GetWindowRect(hwnd, out var frame);
                var corner = new Point32();
                ClientToScreen(hwnd, ref corner);

                bestError = error;
                best = hwnd;
                bestScale = scale;
                bestOrigin = new Vector2(corner.X - frame.Left, corner.Y - frame.Top);
                return true;
            }, IntPtr.Zero);

            if (best == IntPtr.Zero)
                throw new InvalidOperationException(
                    $"[Promo] No visible window of this editor hosts the {panel.x:0}x{panel.y:0} panel of " +
                    $"'{window.titleContent.text}'. A window can be captured only while it floats and is not minimized.");

            return new WindowCapture(best, bestOrigin, bestScale);
#else
            throw new PlatformNotSupportedException("[Promo] Window capture reads the Windows compositor.");
#endif
        }

        /// <summary>Puts the window under every other window on the desktop without activating it.</summary>
        /// <remarks>Capture keeps working there, so a recording never covers what the person at the desktop is doing.</remarks>
        public void SendToBack()
        {
#if UNITY_EDITOR_WIN
            SetWindowPos(handle, new IntPtr(1), 0, 0, 0, 0, NoSize | NoMove | NoActivate);
#endif
        }

        /// <summary>The window's current pixels inside <paramref name="crop"/>, measured from its top-left corner.</summary>
        /// <exception cref="InvalidOperationException">The window system returned no image.</exception>
        public Texture2D Grab(RectInt crop)
        {
#if UNITY_EDITOR_WIN
            GetWindowRect(handle, out var frame);
            var width = frame.Right - frame.Left;
            var height = frame.Bottom - frame.Top;
            var screen = GetDC(IntPtr.Zero);
            var memory = CreateCompatibleDC(screen);
            var bitmap = CreateCompatibleBitmap(screen, width, height);
            var previous = SelectObject(memory, bitmap);
            try
            {
                if (!PrintWindow(handle, memory, RenderFullContent))
                    throw new InvalidOperationException("[Promo] PrintWindow returned no image of the captured window.");

                var header = new BitmapInfoHeader
                {
                    Size = Marshal.SizeOf<BitmapInfoHeader>(), Width = width, Height = -height, Planes = 1, BitCount = 32
                };
                var length = width * height * 4;
                if (bits == null || bits.Length != length) bits = new byte[length];
                if (GetDIBits(memory, bitmap, 0, (uint)height, bits, ref header, 0) != height)
                    throw new InvalidOperationException("[Promo] The captured window's pixels came back partial.");

                var x0 = Mathf.Clamp(crop.x, 0, width);
                var y0 = Mathf.Clamp(crop.y, 0, height);
                var w = Mathf.Clamp(crop.width, 0, width - x0);
                var h = Mathf.Clamp(crop.height, 0, height - y0);
                var pixels = new Color32[w * h];
                for (var row = 0; row < h; row++)
                {
                    var source = ((y0 + row) * width + x0) * 4;
                    var target = (h - 1 - row) * w;
                    for (var column = 0; column < w; column++, source += 4)
                        pixels[target + column] = new Color32(bits[source + 2], bits[source + 1], bits[source], 255);
                }

                var texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
                texture.SetPixels32(pixels);
                texture.Apply(false);
                return texture;
            }
            finally
            {
                SelectObject(memory, previous);
                DeleteObject(bitmap);
                DeleteDC(memory);
                ReleaseDC(IntPtr.Zero, screen);
            }
#else
            throw new PlatformNotSupportedException("[Promo] Window capture reads the Windows compositor.");
#endif
        }

#if UNITY_EDITOR_WIN
        private const uint RenderFullContent = 2;
        private const uint NoSize = 0x0001;
        private const uint NoMove = 0x0002;
        private const uint NoActivate = 0x0010;

        [StructLayout(LayoutKind.Sequential)] private struct Rect32 { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)] private struct Point32 { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfoHeader
        {
            public int Size, Width, Height;
            public short Planes, BitCount;
            public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
        }

        private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect32 rect);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out Rect32 rect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref Point32 point);
        [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width,
            int height, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr hdc, IntPtr bitmap, uint start, uint lines,
            byte[] bits, ref BitmapInfoHeader info, uint usage);
#endif
    }
}
