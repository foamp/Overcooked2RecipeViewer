using System;

namespace Overcooked2RecipeViewer
{
    internal enum UiTheme { Kitchen, Cream, Ocean, Charcoal }

    // Independent of board zoom and export settings. No Unity native calls.
    internal sealed class UiSettings
    {
        internal float Scale = 1f;
        internal UiTheme Theme = UiTheme.Kitchen;
        internal void Normalize()
        {
            if (float.IsNaN(Scale) || float.IsInfinity(Scale)) Scale = 1f;
            Scale = Math.Max(0.5f, Math.Min(2f, Scale));
            if (!Enum.IsDefined(typeof(UiTheme), Theme)) Theme = UiTheme.Kitchen;
        }
        internal float ViewScale(int width, int height)
        {
            float fit = Math.Max(0.25f, Math.Min(1.2f, Math.Min(width / 1000f, height / 840f)));
            return fit * Scale;
        }
    }

    internal sealed class UiPalette
    {
        internal readonly int Background, Panel, Button, Hover, Selected, Accent, Text, SelectedText, Muted;
        private UiPalette(int background, int panel, int button, int hover, int selected,
            int accent, int text, int selectedText, int muted)
        {
            Background = background; Panel = panel; Button = button; Hover = hover;
            Selected = selected; Accent = accent; Text = text; SelectedText = selectedText; Muted = muted;
        }
        internal static UiPalette Get(UiTheme theme)
        {
            switch (theme)
            {
                case UiTheme.Cream: return new UiPalette(0xEDE4D3,0xF8F2E6,0xE2D3BC,0xD5C09F,0x985026,0x986023,0x382D24,0xFFF8EA,0x6B5847);
                case UiTheme.Ocean: return new UiPalette(0x101F30,0x192C41,0x294661,0x365B7B,0x346A97,0x79C8EF,0xEDF5FC,0xFFFFFF,0xB7D0E4);
                case UiTheme.Charcoal: return new UiPalette(0x181C22,0x242A33,0x353E49,0x465261,0x32675D,0x83DDC6,0xF0F3F7,0xFFFFFF,0xBDC8D4);
                default: return new UiPalette(0x102725,0x193532,0x2B4A44,0x3D6356,0x8D642F,0xE4BF75,0xFFF3D8,0xFFFFFF,0xDACAAB);
            }
        }
    }

    internal struct UiBounds
    {
        internal float X, Y, Width, Height;
        internal UiBounds(float x, float y, float width, float height)
        { X = x; Y = y; Width = width; Height = height; }
    }

    internal sealed class UiToolbarLayout
    {
        internal readonly UiBounds[] Buttons = new UiBounds[6];
        internal float Height;
        internal static UiToolbarLayout Create(float width)
        {
            UiToolbarLayout layout = new UiToolbarLayout();
            float[] sizes = {200,140,124,170,100,90};
            float x = 18, y = 42;
            for (int i = 0; i < sizes.Length; i++)
            {
                float w = Math.Min(sizes[i], Math.Max(1, width - 36));
                if (x > 18 && x + w > width - 18) { x = 18; y += 38; }
                layout.Buttons[i] = new UiBounds(x,y,w,30);
                x += w + 8;
            }
            layout.Height = y + 40;
            return layout;
        }
    }

    internal sealed class UiPreviewLayout
    {
        internal UiBounds Window, Title, Interface, Close, Resolution, Preview, Settings, Status, ZoomLabel;
        internal readonly UiBounds[] Actions = new UiBounds[3], ZoomButtons = new UiBounds[4];
        internal bool CompactZoom;
        internal static UiPreviewLayout Create(float width, float height)
        {
            UiPreviewLayout layout = new UiPreviewLayout();
            layout.Window = new UiBounds(20,20,width - 40,height - 40);
            layout.Title = new UiBounds(38,32,Math.Min(270,width - 250),26);
            layout.Interface = new UiBounds(width - 184,30,100,28);
            layout.Close = new UiBounds(width - 76,30,38,28);
            bool narrow = width < 840;
            layout.Resolution = narrow ? new UiBounds(38,58,width - 76,26) : new UiBounds(320,32,width - 540,26);
            float top = narrow ? 90 : 74;
            layout.Preview = new UiBounds(38,top,width - 320,height - top - 174);
            layout.Settings = new UiBounds(width - 264,top,224,height - top - 108);
            layout.Status = new UiBounds(38,height - 100,width - 76,22);
            layout.CompactZoom = layout.Preview.Width < 250;
            float[] zoomWidths = layout.CompactZoom ? new float[] {46,46,26,26} : new float[] {88,58,34,34};
            float x = 38, y = height - 166;
            for (int i = 0; i < zoomWidths.Length; i++)
            {
                layout.ZoomButtons[i] = new UiBounds(x,y,zoomWidths[i],28);
                x += zoomWidths[i] + (layout.CompactZoom ? 4 : 8);
            }
            layout.ZoomLabel = new UiBounds(38,y + 34,layout.Preview.Width,22);
            float actionScale = Math.Min(1,(width - 92) / 422);
            float[] actionWidths = {174,140,108};
            x = width - 38 - 422 * actionScale - 16;
            for (int i = 0; i < actionWidths.Length; i++)
            {
                layout.Actions[i] = new UiBounds(x,height - 62,actionWidths[i] * actionScale,30);
                x += actionWidths[i] * actionScale + 8;
            }
            return layout;
        }
    }
}
