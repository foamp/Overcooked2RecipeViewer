using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

namespace Overcooked2RecipeViewer
{
    // Windows clipboard CF_DIB; the exported pixels are already opaque and
    // arranged bottom-up, matching a positive-height BITMAPINFOHEADER.
    internal static class ClipboardImage
    {
        private const uint CfDib = 8;
        private const uint GmemMoveable = 0x0002;

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr owner);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint format, IntPtr handle);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalUnlock(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalFree(IntPtr handle);

        internal static bool Copy(Color32[] pixels, int width, int height, out string error)
        {
            error = string.Empty;
            if (pixels == null || width < 1 || height < 1 || pixels.Length != width * height)
            {
                error = "Invalid bitmap dimensions.";
                return false;
            }

            IntPtr memory = IntPtr.Zero;
            try
            {
                int pixelBytes = checked(width * height * 4);
                byte[] dib = new byte[checked(40 + pixelBytes)];
                WriteInt(dib, 0, 40);                  // BITMAPINFOHEADER size
                WriteInt(dib, 4, width);
                WriteInt(dib, 8, height);              // positive = bottom-up
                dib[12] = 1;                          // planes
                dib[14] = 32;                         // bits per pixel
                WriteInt(dib, 20, pixelBytes);
                for (int i = 0; i < pixels.Length; i++)
                {
                    int at = 40 + i * 4;
                    dib[at] = pixels[i].b;
                    dib[at + 1] = pixels[i].g;
                    dib[at + 2] = pixels[i].r;
                    dib[at + 3] = 255;
                }

                memory = GlobalAlloc(GmemMoveable, new UIntPtr((uint)dib.Length));
                if (memory == IntPtr.Zero)
                {
                    error = "GlobalAlloc failed: " + Marshal.GetLastWin32Error();
                    return false;
                }
                IntPtr pointer = GlobalLock(memory);
                if (pointer == IntPtr.Zero)
                {
                    error = "GlobalLock failed: " + Marshal.GetLastWin32Error();
                    return false;
                }
                try { Marshal.Copy(dib, 0, pointer, dib.Length); }
                finally { GlobalUnlock(memory); }

                IntPtr owner = GetActiveWindow();
                if (owner == IntPtr.Zero) owner = Process.GetCurrentProcess().MainWindowHandle;
                if (owner == IntPtr.Zero) owner = GetForegroundWindow();
                if (owner == IntPtr.Zero)
                {
                    error = "Game window handle was not found.";
                    return false;
                }

                bool opened = false;
                for (int attempt = 0; attempt < 5 && !opened; attempt++)
                {
                    opened = OpenClipboard(owner);
                    if (!opened) Thread.Sleep(25);
                }
                if (!opened)
                {
                    error = "Clipboard is busy: " + Marshal.GetLastWin32Error();
                    return false;
                }
                try
                {
                    if (!EmptyClipboard())
                    {
                        error = "EmptyClipboard failed: " + Marshal.GetLastWin32Error();
                        return false;
                    }
                    if (SetClipboardData(CfDib, memory) == IntPtr.Zero)
                    {
                        error = "SetClipboardData failed: " + Marshal.GetLastWin32Error();
                        return false;
                    }
                    memory = IntPtr.Zero; // Windows owns it after SetClipboardData.
                    return true;
                }
                finally
                {
                    CloseClipboard();
                }
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
            finally
            {
                if (memory != IntPtr.Zero) GlobalFree(memory);
            }
        }

        private static void WriteInt(byte[] bytes, int offset, int value)
        {
            byte[] part = BitConverter.GetBytes(value);
            Buffer.BlockCopy(part, 0, bytes, offset, 4);
        }
    }
}
