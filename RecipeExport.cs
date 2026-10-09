using System;
using System.Collections.Generic;
using System.IO;

namespace Overcooked2RecipeViewer
{
    internal enum ExportBackground { Default, Transparent, White, Dark, Custom }
    internal enum ExportMargin { None, Compact, Standard, Wide }
    internal enum ExportLayout { Current, Compact, Standard, Wide }

    // ConfigurationManager reads this optional tag by field name. No dependency
    // on the configuration UI plugin, and settings still persist in the same file.
    internal sealed class ConfigurationManagerAttributes
    {
        public bool? Browsable;
#pragma warning disable 0649
        public string DispName;
        public string Category;
        public string Description;
#pragma warning restore 0649
    }

    internal static class ExportConfiguration
    {
        internal static BepInEx.Configuration.ConfigDescription Hidden(string description)
        {
            return new BepInEx.Configuration.ConfigDescription(description, null,
                new ConfigurationManagerAttributes { Browsable = false });
        }
    }

    // Export-only values; independent of board zoom, viewport and saved layouts.
    internal sealed class ExportSettings
    {
        private static readonly int[] Margins = { 0, 24, 64, 128 };
        internal ExportBackground Background = ExportBackground.Default;
        internal ExportMargin Margin = ExportMargin.Standard;
        internal ExportLayout Layout = ExportLayout.Standard;
        internal byte Red = 235, Green = 227, Blue = 209;
        internal int Padding { get { return Margins[(int)Margin]; } }
        internal ExportSettings Clone() { return (ExportSettings)MemberwiseClone(); }
        internal void Normalize()
        {
            if (!Enum.IsDefined(typeof(ExportBackground), Background)) Background = ExportBackground.Default;
            if (!Enum.IsDefined(typeof(ExportMargin), Margin)) Margin = ExportMargin.Standard;
            // Current remains a compatibility identity for older configs only.
            if (!Enum.IsDefined(typeof(ExportLayout), Layout) || Layout == ExportLayout.Current) Layout = ExportLayout.Standard;
        }
        internal void BackgroundPixel(byte[] row, int at)
        {
            byte r = 235, g = 227, b = 209, a = 255;
            switch (Background)
            {
                case ExportBackground.Transparent: r = g = b = a = 0; break;
                case ExportBackground.White: r = g = b = 255; break;
                case ExportBackground.Dark: r = 25; g = 37; b = 39; break;
                case ExportBackground.Custom: r = Red; g = Green; b = Blue; break;
            }
            row[at] = r; row[at + 1] = g; row[at + 2] = b; row[at + 3] = a;
        }
    }

    internal sealed class ExportCard
    {
        internal int Left, Top, Width, Height, Row;
        internal string Path;
    }

    internal sealed class ExportLayoutPlan
    {
        internal readonly List<ExportCard> Cards = new List<ExportCard>();
        internal int Width, Height;
    }

    // Full-quality cards live on disk, never in one full-board GPU texture.
    internal sealed class RecipeExport : IDisposable
    {
        internal const int MaxWidth = 12000, MaxHeight = 100000;
        internal const long MaxPixels = 200000000L, MaxCacheBytes = 512L * 1024 * 1024;
        internal readonly List<ExportCard> Cards = new List<ExportCard>();
        private readonly string _directory;
        private long _cacheBytes;
        private bool _disposed;

        internal RecipeExport()
        {
            _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "Overcooked2RecipeViewer-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        internal static bool SafeDimensions(double width, double height)
        {
            return width >= 1 && height >= 1 && width <= MaxWidth && height <= MaxHeight &&
                width * height <= MaxPixels;
        }

        internal bool Dimensions(ExportSettings settings, out int width, out int height)
        {
            width = height = 0;
            if (_disposed || Cards.Count == 0) return false;
            ExportLayoutPlan plan = CreatePlan(settings);
            long w = (long)plan.Width + settings.Padding * 2, h = (long)plan.Height + settings.Padding * 2;
            if (!SafeDimensions(w, h)) return false;
            width = (int)w; height = (int)h;
            return true;
        }

        internal void Add(int left, int top, int width, int height, byte[] pixels)
        {
            Add(left, top, width, height, pixels, 0);
        }

        internal void Add(int left, int top, int width, int height, byte[] pixels, int row)
        {
            if (_disposed) throw new ObjectDisposedException("RecipeExport");
            long bytes = (long)width * height * 4;
            if (width < 1 || height < 1 || pixels == null || pixels.Length != bytes ||
                _cacheBytes + bytes > MaxCacheBytes) throw new InvalidOperationException("Export cache exceeds safe limits.");
            // Find every nonzero alpha pixel, including the faintest shadows.
            // Input/cache are bottom-up; placement coordinates are top-down.
            int minX = width, minY = height, maxX = -1, maxY = -1;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (pixels[(y * width + x) * 4 + 3] != 0)
                    {
                        minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                        minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                    }
            if (maxX < 0) throw new InvalidOperationException("Empty recipe capture; export cancelled to avoid missing content.");
            int croppedWidth = maxX - minX + 1, croppedHeight = maxY - minY + 1;
            if (croppedWidth != width || croppedHeight != height)
            {
                byte[] cropped = new byte[checked(croppedWidth * croppedHeight * 4)];
                for (int y = 0; y < croppedHeight; y++)
                    Buffer.BlockCopy(pixels, ((minY + y) * width + minX) * 4,
                        cropped, y * croppedWidth * 4, croppedWidth * 4);
                pixels = cropped;
            }
            left += minX; top += height - 1 - maxY;
            width = croppedWidth; height = croppedHeight;
            ExportCard card = new ExportCard();
            card.Left = left; card.Top = top; card.Width = width; card.Height = height;
            card.Row = row;
            card.Path = System.IO.Path.Combine(_directory, Cards.Count + ".rgba");
            File.WriteAllBytes(card.Path, pixels);
            Cards.Add(card); _cacheBytes += pixels.Length;
        }

        // Positions are copies. Changing export layout never mutates captured
        // positions, viewer rows, transforms or saved layouts. Card order is the
        // viewer's current reading order and explicit row membership. Alpha
        // cropping can change Top, so it cannot be used to infer manual rows.
        internal ExportLayoutPlan CreatePlan(ExportSettings settings)
        {
            ExportLayoutPlan plan = new ExportLayoutPlan();
            if (Cards.Count == 0) return plan;
            int left = int.MaxValue, top = int.MaxValue;
            int gap = settings.Layout == ExportLayout.Compact ? 12 : settings.Layout == ExportLayout.Wide ? 64 : 32;
            int x = 0, y = 0, rowHeight = 0;
            for (int i = 0; i < Cards.Count; i++)
            {
                ExportCard source = Cards[i], card = new ExportCard();
                card.Path = source.Path; card.Width = source.Width; card.Height = source.Height; card.Row = source.Row;
                if (settings.Layout == ExportLayout.Current)
                {
                    card.Left = source.Left; card.Top = source.Top;
                    left = Math.Min(left, card.Left); top = Math.Min(top, card.Top);
                }
                else
                {
                    // Spacing presets preserve the player's manual line breaks,
                    // including short rows. Never merge or wrap their groups.
                    if (i > 0 && source.Row != Cards[i - 1].Row)
                    {
                        plan.Width = Math.Max(plan.Width, x);
                        y += rowHeight + gap; x = 0; rowHeight = 0;
                    }
                    if (x > 0) x += gap;
                    card.Left = x; card.Top = y; x += card.Width;
                    rowHeight = Math.Max(rowHeight, card.Height);
                }
                plan.Cards.Add(card);
            }
            if (settings.Layout == ExportLayout.Current)
            {
                foreach (ExportCard card in plan.Cards)
                {
                    card.Left -= left; card.Top -= top;
                    plan.Width = Math.Max(plan.Width, card.Left + card.Width);
                    plan.Height = Math.Max(plan.Height, card.Top + card.Height);
                }
            }
            else
            {
                plan.Width = Math.Max(plan.Width, x); plan.Height = y + rowHeight;
            }
            return plan;
        }

        internal ExportScanlines OpenScanlines(ExportSettings settings)
        {
            int width, height;
            if (!Dimensions(settings, out width, out height)) throw new InvalidOperationException("Unsafe export dimensions or stale capture.");
            return new ExportScanlines(CreatePlan(settings).Cards, settings.Clone(), width, height);
        }

        // Bottom-up RGBA for Texture2D / clipboard. Same scanline compositor as PNG.
        internal byte[] Thumbnail(ExportSettings settings, int maxSide, int maxPixels,
            out int width, out int height)
        {
            using (ExportThumbnail thumbnail = new ExportThumbnail(this, settings, maxSide, maxPixels))
            {
                while (thumbnail.NextRow()) { }
                width = thumbnail.Width; height = thumbnail.Height;
                return thumbnail.Pixels;
            }
        }

        internal static void ThumbnailSize(int fullWidth, int fullHeight, int maxSide, int maxPixels,
            out int width, out int height)
        {
            if (maxSide < 1 || maxPixels < 1) throw new ArgumentOutOfRangeException("maxSide");
            double factor = Math.Min(1, Math.Min((double)maxSide / fullWidth, (double)maxSide / fullHeight));
            factor = Math.Min(factor, Math.Sqrt((double)maxPixels / ((double)fullWidth * fullHeight)));
            width = Math.Max(1, (int)Math.Floor(fullWidth * factor));
            height = Math.Max(1, (int)Math.Floor(fullHeight * factor));
        }

        // Default Unity UI blends RGB onto the matte. Recover straight alpha,
        // rather than exporting the shader's squared alpha or premultiplied RGB.
        internal static void RecoverAlpha(byte[] black, byte[] white, bool linear)
        {
            if (black.Length != white.Length || black.Length % 4 != 0) throw new ArgumentException("Invalid matte pixels.");
            for (int i = 0; i < black.Length; i += 4)
            {
                double r = Decode(black[i], linear), g = Decode(black[i + 1], linear), b = Decode(black[i + 2], linear);
                double difference = (Decode(white[i], linear) - r + Decode(white[i + 1], linear) - g + Decode(white[i + 2], linear) - b) / 3;
                double alpha = Math.Max(0, Math.Min(1, 1 - difference));
                byte a = Byte(alpha);
                black[i + 3] = a;
                if (a == 0) { black[i] = black[i + 1] = black[i + 2] = 0; continue; }
                black[i] = Encode(r / alpha, linear);
                black[i + 1] = Encode(g / alpha, linear);
                black[i + 2] = Encode(b / alpha, linear);
            }
        }
        private static double Decode(byte value, bool linear)
        {
            double v = value / 255.0;
            return !linear ? v : v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        private static byte Encode(double v, bool linear)
        {
            v = Math.Max(0, Math.Min(1, v));
            return Byte(!linear ? v : v <= 0.0031308 ? v * 12.92 : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055);
        }
        private static byte Byte(double v) { return (byte)Math.Max(0, Math.Min(255, (int)Math.Round(v * 255))); }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Cards.Clear();
            // Only this session's freshly-created directory; never user exports.
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }
    }

    internal sealed class ExportThumbnail : IDisposable
    {
        internal readonly int Width, Height;
        internal readonly byte[] Pixels;
        private readonly int _fullWidth, _fullHeight;
        private readonly int _left, _top, _spanWidth, _spanHeight;
        private readonly byte[] _row;
        private readonly ExportScanlines _source;
        private int _y;
        internal ExportThumbnail(RecipeExport cache, ExportSettings settings, int maxSide, int maxPixels)
        {
            if (!cache.Dimensions(settings, out _fullWidth, out _fullHeight)) throw new InvalidOperationException("Unsafe thumbnail dimensions.");
            _spanWidth = _fullWidth; _spanHeight = _fullHeight;
            RecipeExport.ThumbnailSize(_fullWidth, _fullHeight, maxSide, maxPixels, out Width, out Height);
            Pixels = new byte[checked(Width * Height * 4)];
            _row = new byte[checked(_fullWidth * 4 + 1)];
            _source = cache.OpenScanlines(settings);
        }
        internal ExportThumbnail(RecipeExport cache, ExportSettings settings, int left, int top,
            int spanWidth, int spanHeight, int textureWidth, int textureHeight)
        {
            if (!cache.Dimensions(settings, out _fullWidth, out _fullHeight) || left < 0 || top < 0 ||
                spanWidth < 1 || spanHeight < 1 || left + spanWidth > _fullWidth || top + spanHeight > _fullHeight ||
                textureWidth < 1 || textureHeight < 1 || (long)textureWidth * textureHeight > 2000000)
                throw new InvalidOperationException("Unsafe preview region.");
            _left = left; _top = top; _spanWidth = spanWidth; _spanHeight = spanHeight;
            Width = textureWidth; Height = textureHeight;
            Pixels = new byte[checked(Width * Height * 4)];
            _row = new byte[checked(_fullWidth * 4 + 1)];
            _source = cache.OpenScanlines(settings);
        }
        internal bool NextRow()
        {
            if (_y >= Height) return false;
            int sourceY = Math.Min(_fullHeight - 1, _top + (int)((_y + 0.5) * _spanHeight / Height));
            _source.Fill(sourceY, _row);
            for (int x = 0; x < Width; x++)
            {
                int sourceX = Math.Min(_fullWidth - 1, _left + (int)((x + 0.5) * _spanWidth / Width));
                Buffer.BlockCopy(_row, 1 + sourceX * 4, Pixels, ((Height - 1 - _y) * Width + x) * 4, 4);
            }
            _y++;
            return true;
        }
        public void Dispose() { _source.Dispose(); }
    }

    internal sealed class ExportScanlines : IDisposable
    {
        private readonly List<ExportCard> _cards;
        private readonly Dictionary<ExportCard, FileStream> _streams = new Dictionary<ExportCard, FileStream>();
        private readonly ExportSettings _settings;
        private readonly int _width, _pad;
        private byte[] _source = new byte[0];
        internal ExportScanlines(List<ExportCard> cards, ExportSettings settings, int width, int height)
        { _cards = cards; _settings = settings; _width = width; _pad = settings.Padding; }

        internal void Fill(int y, byte[] row)
        {
            row[0] = 0;
            for (int x = 0; x < _width; x++) _settings.BackgroundPixel(row, 1 + x * 4);
            for (int i = 0; i < _cards.Count; i++)
            {
                ExportCard card = _cards[i];
                int localY = y - card.Top - _pad;
                FileStream stream;
                if (localY < 0 || localY >= card.Height)
                {
                    if (_streams.TryGetValue(card, out stream)) { stream.Dispose(); _streams.Remove(card); }
                    continue;
                }
                if (!_streams.TryGetValue(card, out stream))
                {
                    stream = new FileStream(card.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    _streams.Add(card, stream);
                }
                int length = checked(card.Width * 4);
                if (_source.Length < length) _source = new byte[length];
                stream.Position = (long)(card.Height - 1 - localY) * length;
                int read = 0;
                while (read < length)
                {
                    int n = stream.Read(_source, read, length - read);
                    if (n == 0) throw new EndOfStreamException("Incomplete recipe capture.");
                    read += n;
                }
                for (int x = 0; x < card.Width; x++)
                {
                    int target = card.Left + _pad + x;
                    if (target < 0 || target >= _width) continue;
                    Blend(_source, x * 4, row, 1 + target * 4);
                }
            }
        }

        internal static void Blend(byte[] source, int from, byte[] target, int at)
        {
            int a = source[from + 3], d = target[at + 3];
            if (a == 0) return;
            int alpha = a * 255 + d * (255 - a);
            for (int c = 0; c < 3; c++)
                target[at + c] = (byte)((source[from + c] * a * 255 + target[at + c] * d * (255 - a) + alpha / 2) / alpha);
            target[at + 3] = (byte)((alpha + 127) / 255);
        }
        public void Dispose()
        {
            foreach (FileStream stream in _streams.Values) stream.Dispose();
            _streams.Clear();
        }
    }
}
