using System;
using System.Globalization;
using BepInEx.Configuration;
using UnityEngine;

namespace Overcooked2RecipeViewer
{
    public sealed partial class RecipeViewerPlugin
    {
        private ConfigEntry<float> _interfaceScale;
        private ConfigEntry<UiTheme> _interfaceTheme;
        private UiSettings _interfaceSettings;
        private UiPalette _palette;
        private UiTheme _styledTheme = (UiTheme)(-1);
        private bool _appearanceOpen, _interfaceDirty, _interfaceSaveFailed;
        private float _interfaceSaveAt;
        private string _interfaceScaleText = "100";
        private Vector2 _appearanceScroll;
        private Texture2D _normalUiTexture, _hoverUiTexture, _selectedUiTexture,
            _frameUiTexture, _panelUiTexture, _scrimUiTexture;
        private GUIStyle _inputStyle, _scrimStyle, _compactButtonStyle;

        private void BindInterfaceSettings()
        {
            _interfaceScale = Config.Bind("Interface", "Scale", 1f,
                ExportConfiguration.Hidden("Interface scale only / 仅影响界面缩放: 0.5 to 2.0."));
            _interfaceTheme = Config.Bind("Interface", "Theme", UiTheme.Kitchen,
                ExportConfiguration.Hidden("Interface palette only / 仅影响界面配色: Kitchen, Cream, Ocean, Charcoal."));
            _interfaceSettings = new UiSettings();
            _interfaceSettings.Scale = _interfaceScale.Value; _interfaceSettings.Theme = _interfaceTheme.Value;
            _interfaceSettings.Normalize();
            _interfaceScaleText = Math.Round(_interfaceSettings.Scale * 100).ToString(CultureInfo.InvariantCulture);
            _palette = UiPalette.Get(_interfaceSettings.Theme);
        }
        private float InterfaceViewScale()
        { return _interfaceSettings.ViewScale(Screen.width, Screen.height); }
        internal static Color UiColor(int rgb, float alpha)
        { return new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, alpha); }
        private static Rect UiRect(UiBounds bounds)
        { return new Rect(bounds.X,bounds.Y,bounds.Width,bounds.Height); }

        private void InterfaceSettingsChanged()
        {
            _interfaceSettings.Normalize();
            _interfaceDirty = true; _interfaceSaveAt = Time.realtimeSinceStartup + 0.35f;
        }
        private void PersistInterfaceSettings()
        {
            if (!_interfaceDirty) return;
            bool autoSave = Config.SaveOnConfigSet;
            try
            {
                Config.SaveOnConfigSet = false;
                _interfaceScale.Value = _interfaceSettings.Scale;
                _interfaceTheme.Value = _interfaceSettings.Theme;
                Config.Save(); _interfaceSaveFailed = false;
            }
            catch (Exception exception)
            {
                _interfaceSaveFailed = true;
                Logger.LogWarning("Could not save interface settings: " + exception);
            }
            finally { Config.SaveOnConfigSet = autoSave; _interfaceDirty = false; }
        }
        private void OpenAppearance()
        {
            _presetOpen = false; _appearanceOpen = true; _appearanceScroll = Vector2.zero;
            _interfaceScaleText = Math.Round(_interfaceSettings.Scale * 100).ToString(CultureInfo.InvariantCulture);
        }
        private void CloseAppearance()
        { _appearanceOpen = false; PersistInterfaceSettings(); }

        // Small reusable rounded textures. Recolor in place on theme changes;
        // changing scale allocates no textures and never touches export caches.
        private Texture2D RoundedSwatch(Color color)
        {
            Texture2D texture = new Texture2D(24,24,TextureFormat.RGBA32,false);
            texture.hideFlags = HideFlags.HideAndDontSave; texture.wrapMode = TextureWrapMode.Clamp;
            PaintSwatch(texture,color); _uiTextures.Add(texture); return texture;
        }
        private static void PaintSwatch(Texture2D texture, Color color)
        {
            Color[] pixels = new Color[24 * 24];
            for (int y = 0; y < 24; y++) for (int x = 0; x < 24; x++)
            {
                float dx = Math.Max(0, Math.Max(6.5f - (x + 0.5f), x + 0.5f - 17.5f));
                float dy = Math.Max(0, Math.Max(6.5f - (y + 0.5f), y + 0.5f - 17.5f));
                Color pixel = color; pixel.a *= Mathf.Clamp01(6.5f - Mathf.Sqrt(dx * dx + dy * dy));
                pixels[y * 24 + x] = pixel;
            }
            texture.SetPixels(pixels); texture.Apply(false);
        }
        private void ApplyInterfacePalette()
        {
            if (_styledTheme == _interfaceSettings.Theme) return;
            _styledTheme = _interfaceSettings.Theme; _palette = UiPalette.Get(_styledTheme);
            PaintSwatch(_normalUiTexture,UiColor(_palette.Button,1));
            PaintSwatch(_hoverUiTexture,UiColor(_palette.Hover,1));
            PaintSwatch(_selectedUiTexture,UiColor(_palette.Selected,1));
            PaintSwatch(_frameUiTexture,UiColor(_palette.Accent,1));
            PaintSwatch(_panelUiTexture,UiColor(_palette.Panel,1));
            _buttonStyle.normal.textColor = UiColor(_palette.Text,1);
            _buttonStyle.hover.textColor = UiColor(_palette.Text,1);
            _buttonStyle.active.textColor = UiColor(_palette.SelectedText,1);
            _buttonStyle.focused.textColor = UiColor(_palette.Text,1);
            _compactButtonStyle.normal.textColor = UiColor(_palette.Text,1);
            _compactButtonStyle.hover.textColor = UiColor(_palette.Text,1);
            _compactButtonStyle.active.textColor = UiColor(_palette.SelectedText,1);
            _compactButtonStyle.focused.textColor = UiColor(_palette.Text,1);
            _selectedStyle.normal.textColor = UiColor(_palette.SelectedText,1);
            _selectedStyle.hover.textColor = UiColor(_palette.SelectedText,1);
            _selectedStyle.active.textColor = UiColor(_palette.SelectedText,1);
            _selectedStyle.focused.textColor = UiColor(_palette.SelectedText,1);
            _headingStyle.normal.textColor = UiColor(_palette.Text,1);
            _statusStyle.normal.textColor = UiColor(_palette.Muted,1);
            _inputStyle.normal.textColor = UiColor(_palette.Text,1);
            _inputStyle.focused.textColor = UiColor(_palette.Text,1);
            _inputStyle.hover.textColor = UiColor(_palette.Text,1);
        }

        private void DrawAppearance(float width, float height)
        {
            GUI.enabled = true; GUI.color = Color.white;
            GUI.Box(new Rect(0,0,width,height),"",_scrimStyle);
            float w = Math.Min(440,width - 32), h = Math.Min(310,height - 32);
            Rect window = new Rect((width - w) / 2,(height - h) / 2,w,h);
            GUI.Box(window,"",_menuFrameStyle);
            GUI.Box(new Rect(window.x + 2,window.y + 2,w - 4,h - 4),"",_menuBoxStyle);
            GUI.Label(new Rect(window.x + 20,window.y + 16,w - 82,26),RecipeUiText.Text(UiTextKey.InterfaceSettings),_headingStyle);
            if (GUI.Button(new Rect(window.xMax - 54,window.y + 12,34,28),"X",_buttonStyle)) { CloseAppearance(); return; }
            Rect area = new Rect(window.x + 20,window.y + 54,w - 40,h - 110);
            _appearanceScroll = GUI.BeginScrollView(area,_appearanceScroll,new Rect(0,0,area.width - 20,184));
            float inner = area.width - 20;
            GUI.Label(new Rect(0,0,inner - 120,24),RecipeUiText.Text(UiTextKey.InterfaceScale),_headingStyle);
            string typed = GUI.TextField(new Rect(inner - 102,0,70,24),_interfaceScaleText,3,_inputStyle);
            GUI.Label(new Rect(inner - 28,2,24,22),"%",_statusStyle);
            if (typed != _interfaceScaleText)
            {
                _interfaceScaleText = typed; int percent;
                if (int.TryParse(typed,out percent) && percent >= 50 && percent <= 200)
                { _interfaceSettings.Scale = percent / 100f; InterfaceSettingsChanged(); }
            }
            GUI.color = UiColor(_palette.Accent,1);
            float next = Mathf.Round(GUI.HorizontalSlider(new Rect(0,34,inner,18),_interfaceSettings.Scale * 100,50,200)) / 100f;
            GUI.color = Color.white;
            if (Math.Abs(next - _interfaceSettings.Scale) > 0.001f)
            {
                _interfaceSettings.Scale = next;
                _interfaceScaleText = Math.Round(next * 100).ToString(CultureInfo.InvariantCulture);
                InterfaceSettingsChanged();
            }
            GUI.Label(new Rect(0,65,inner,24),RecipeUiText.Text(UiTextKey.InterfaceTheme),_headingStyle);
            UiTextKey[] themes = {UiTextKey.KitchenTheme,UiTextKey.CreamTheme,UiTextKey.OceanTheme,UiTextKey.CharcoalTheme};
            for (int i = 0; i < themes.Length; i++)
                if (GUI.Button(new Rect((i % 2) * (inner + 8) / 2,96 + (i / 2) * 36,(inner - 8) / 2,30),RecipeUiText.Text(themes[i]),
                    _interfaceSettings.Theme == (UiTheme)i ? _selectedStyle : _buttonStyle))
                { _interfaceSettings.Theme = (UiTheme)i; InterfaceSettingsChanged(); }
            GUI.EndScrollView();
            if (_interfaceSaveFailed) GUI.Label(new Rect(window.x + 20,window.yMax - 64,w - 40,20),RecipeUiText.Text(UiTextKey.InterfaceSettingsFailed),_statusStyle);
            if (GUI.Button(new Rect(window.x + 20,window.yMax - 46,100,28),RecipeUiText.Text(UiTextKey.ResetInterface),_buttonStyle))
            {
                _interfaceSettings.Scale = 1; _interfaceSettings.Theme = UiTheme.Kitchen; _interfaceScaleText = "100";
                InterfaceSettingsChanged();
            }
            if (GUI.Button(new Rect(window.xMax - 120,window.yMax - 46,100,28),RecipeUiText.Text(UiTextKey.Close),_buttonStyle)) CloseAppearance();
            if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp || Event.current.type == EventType.ScrollWheel) Event.current.Use();
        }
    }
}
