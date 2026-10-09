using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Overcooked2RecipeViewer
{
    // PNG + CF_DIBV5 preserve alpha where supported. CF_DIB supplies an opaque
    // white-matte fallback for older applications. All inputs are bottom-up RGBA.
    internal static class ClipboardImage
    {
        private const uint CfDib = 8;
        private const uint CfDibV5 = 17;
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

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint RegisterClipboardFormat(string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalUnlock(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalFree(IntPtr handle);

        internal static bool Copy(byte[] pixels, int width, int height, out string error)
        {
            error = string.Empty;
            if (pixels == null || width < 1 || height < 1 || pixels.Length != (long)width * height * 4)
            {
                error = "Invalid bitmap dimensions.";
                return false;
            }

            try
            {
                byte[] png;
                using (MemoryStream stream = new MemoryStream())
                {
                    using (PngStreamWriter.Encoder encoder = new PngStreamWriter.Encoder(stream, width, height))
                    {
                        byte[] row = new byte[checked(width * 4 + 1)];
                        for (int y = 0; y < height; y++)
                        {
                            Buffer.BlockCopy(pixels, (height - 1 - y) * width * 4, row, 1, width * 4);
                            encoder.WriteRow(row);
                        }
                        encoder.Finish();
                    }
                    png = stream.ToArray();
                }

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
                    uint pngFormat = RegisterClipboardFormat("PNG");
                    bool alpha = pngFormat != 0 && Publish(pngFormat, png);
                    alpha = Publish(CfDibV5, BuildDib(pixels, width, height, true)) || alpha;
                    bool fallback = Publish(CfDib, BuildDib(pixels, width, height, false));
                    if (!alpha || !fallback) error = "Some clipboard formats unavailable: " + Marshal.GetLastWin32Error();
                    return alpha && fallback;
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
        }

        private static bool Publish(uint format, byte[] bytes)
        {
            IntPtr memory = GlobalAlloc(GmemMoveable, new UIntPtr((uint)bytes.Length));
            if (memory == IntPtr.Zero) return false;
            try
            {
                IntPtr pointer = GlobalLock(memory);
                if (pointer == IntPtr.Zero) return false;
                try { Marshal.Copy(bytes, 0, pointer, bytes.Length); }
                finally { GlobalUnlock(memory); }
                if (SetClipboardData(format, memory) == IntPtr.Zero) return false;
                memory = IntPtr.Zero; // Windows owns successful publications.
                return true;
            }
            finally { if (memory != IntPtr.Zero) GlobalFree(memory); }
        }

        internal static byte[] BuildDib(byte[] pixels, int width, int height, bool alpha)
        {
            int header = alpha ? 124 : 40;
            byte[] dib = new byte[checked(header + width * height * 4)];
            WriteInt(dib, 0, header); WriteInt(dib, 4, width); WriteInt(dib, 8, height);
            dib[12] = 1; dib[14] = 32; WriteInt(dib, 20, pixels.Length);
            if (alpha)
            {
                WriteInt(dib, 16, 3); // BI_BITFIELDS, RGBA masks + sRGB.
                WriteInt(dib, 40, 0x00FF0000); WriteInt(dib, 44, 0x0000FF00);
                WriteInt(dib, 48, 0x000000FF); WriteInt(dib, 52, unchecked((int)0xFF000000));
                WriteInt(dib, 56, 0x73524742); WriteInt(dib, 108, 4);
            }
            for (int i = 0; i < pixels.Length; i += 4)
            {
                int a = pixels[i + 3];
                for (int c = 0; c < 3; c++)
                {
                    byte channel = pixels[i + 2 - c];
                    dib[header + i + c] = alpha ? channel : (byte)((channel * a + 255 * (255 - a) + 127) / 255);
                }
                dib[header + i + 3] = alpha ? (byte)a : (byte)255;
            }
            return dib;
        }

        private static void WriteInt(byte[] bytes, int offset, int value)
        {
            byte[] part = BitConverter.GetBytes(value);
            Buffer.BlockCopy(part, 0, bytes, offset, 4);
        }
    }
}
