using System;
using System.Collections;
using System.Globalization;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace Overcooked2RecipeViewer
{
    public sealed partial class RecipeViewerPlugin
    {
        private ConfigEntry<ExportBackground> _exportBackground;
        private ConfigEntry<ExportMargin> _exportMargin;
        private ConfigEntry<float> _exportScale;
        private ConfigEntry<ExportLayout> _exportLayout;
        private ConfigEntry<string> _exportColor;
        private ExportSettings _exportSettings;
        private RecipeExport _exportCache;
        private ExportThumbnail _thumbnailJob;
        private IEnumerator _captureWork, _saveWork;
        private Texture2D _previewTexture, _checkerTexture;
        private Texture2D _detailTexture;
        private ExportThumbnail _detailJob;
        private PreviewRegion _detailRequest, _detailBuilding, _detailReady;
        private bool _detailRequested;
        private float _detailUpdateAt;
        private float _detailRetryAt;
        private bool _exportPreviewOpen, _previewDirty, _settingsDirty;
        private bool _settingsSaveFailed;
        private float _previewUpdateAt, _settingsSaveAt, _previewZoom = 1f;
        private Vector2 _previewScroll;
        private Vector2 _exportSettingsScroll;
        private UiTextKey _previewStatus;
        private object[] _previewStatusValues = new object[0];

        private void BindExportSettings()
        {
            const string section = "Image export";
            _exportBackground = Config.Bind(section, "Background", ExportBackground.Default,
                ExportConfiguration.Hidden("Export background only / 仅影响导出背景: Default, Transparent, White, Dark, Custom."));
            _exportColor = Config.Bind(section, "Color", "#EBE3D1", ExportConfiguration.Hidden("Custom RGB color / 自定义颜色: #RRGGBB."));
            // Retain this key for old candidate configs, but never read it into
            // rendering settings. Even an old Scale=2 file now exports at 1x.
            _exportScale = Config.Bind(section, "Scale", 1f, ExportConfiguration.Hidden("Legacy compatibility key; output is fixed at native 1x / 兼容旧配置，导出固定为原版 1x。"));
            _exportMargin = Config.Bind(section, "Margin", ExportMargin.Standard,
                ExportConfiguration.Hidden("Export margin only / 仅影响导出边距: None, Compact, Standard, Wide."));
            _exportLayout = Config.Bind(section, "Layout", ExportLayout.Standard,
                ExportConfiguration.Hidden("Export layout only / 仅影响导出排版: Compact, Standard, Wide. Legacy Current becomes Standard."));
        }

        private void OpenExportPreview()
        {
            if (_nativeOverlay == null || !_nativeOverlay.Ready || _nativeOverlay.Dragging) return;
            CloseExportPreview();
            _exportSettings = new ExportSettings();
            _exportSettings.Background = _exportBackground.Value;
            _exportSettings.Margin = _exportMargin.Value;
            _exportSettings.Layout = _exportLayout.Value;
            int color;
            string hex = (_exportColor.Value ?? "").Trim().TrimStart('#');
            if (hex.Length == 6 && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out color))
            {
                _exportSettings.Red = (byte)(color >> 16); _exportSettings.Green = (byte)(color >> 8);
                _exportSettings.Blue = (byte)color;
            }
            _exportSettings.Normalize();
            _presetOpen = false;
            _exportPreviewOpen = true;
            _previewZoom = 1f; _previewScroll = Vector2.zero;
            _exportSettingsScroll = Vector2.zero;
            _settingsDirty = true; _settingsSaveAt = Time.realtimeSinceStartup + 0.4f;
            _previewDirty = true; _previewUpdateAt = Time.realtimeSinceStartup;
            SetPreviewStatus(UiTextKey.Rendering);
        }

        private void SetPreviewStatus(UiTextKey key, params object[] values)
        { _previewStatus = key; _previewStatusValues = values; }

        private void ExportSettingsChanged()
        {
            DisposeThumbnail();
            ReleasePreviewDetail();
            _previewDirty = _settingsDirty = true;
            _previewUpdateAt = Time.realtimeSinceStartup + 0.18f;
            _settingsSaveAt = Time.realtimeSinceStartup + 0.4f;
            SetPreviewStatus(UiTextKey.PreviewUpdating);
        }

        private void PersistExportSettings()
        {
            if (!_settingsDirty || _exportSettings == null) return;
            bool autoSave = Config.SaveOnConfigSet;
            try
            {
                Config.SaveOnConfigSet = false;
                _exportBackground.Value = _exportSettings.Background;
                _exportMargin.Value = _exportSettings.Margin;
                _exportScale.Value = 1f;
                _exportLayout.Value = _exportSettings.Layout;
                _exportColor.Value = string.Format("#{0:X2}{1:X2}{2:X2}", _exportSettings.Red, _exportSettings.Green, _exportSettings.Blue);
                Config.Save();
                _settingsSaveFailed = false;
            }
            catch (Exception exception)
            {
                Logger.LogWarning("Could not save export settings: " + exception);
                _settingsSaveFailed = true;
                SetPreviewStatus(UiTextKey.ExportSettingsFailed);
            }
            finally { Config.SaveOnConfigSet = autoSave; _settingsDirty = false; }
        }

        // Explicitly own/dispose iterators: cancellation also runs their finally
        // blocks when Unity would otherwise abandon a stopped coroutine.
        private void UpdateExportPreview()
        {
            if (!_exportPreviewOpen) return;
            float now = Time.realtimeSinceStartup;
            if (_settingsDirty && now >= _settingsSaveAt) PersistExportSettings();
            if (_saveWork != null) { StepExportWork(ref _saveWork); return; }
            if (_captureWork != null)
            {
                SetPreviewStatus(UiTextKey.PreviewCapturing, _nativeOverlay.Status);
                StepExportWork(ref _captureWork);
                return;
            }
            if (_thumbnailJob != null)
            {
                try
                {
                    float start = Time.realtimeSinceStartup;
                    int count = 0;
                    while (_thumbnailJob.NextRow())
                    {
                        if (++count >= 32 || Time.realtimeSinceStartup - start >= 0.008f) return;
                    }
                    int previewWidth = _thumbnailJob.Width, previewHeight = _thumbnailJob.Height;
                    byte[] pixels = _thumbnailJob.Pixels;
                    Color32[] colors = new Color32[previewWidth * previewHeight];
                    for (int i = 0; i < colors.Length; i++)
                        colors[i] = new Color32(pixels[i * 4], pixels[i * 4 + 1], pixels[i * 4 + 2], pixels[i * 4 + 3]);
                    ReleasePreviewTexture();
                    _previewTexture = new Texture2D(previewWidth, previewHeight, TextureFormat.RGBA32, false);
                    _previewTexture.hideFlags = HideFlags.HideAndDontSave;
                    _previewTexture.wrapMode = TextureWrapMode.Clamp;
                    _previewTexture.filterMode = FilterMode.Bilinear;
                    _previewTexture.SetPixels32(colors); _previewTexture.Apply(false, true);
                    DisposeThumbnail();
                    _previewDirty = false;
                    SetPreviewStatus(_settingsSaveFailed ? UiTextKey.ExportSettingsFailed : UiTextKey.PreviewReady);
                }
                catch (Exception exception)
                {
                    DisposeThumbnail(); _previewDirty = false;
                    Logger.LogError("Export thumbnail failed: " + exception);
                    SetPreviewStatus(UiTextKey.ExportFailed);
                }
                return;
            }
            if (!_previewDirty) { UpdatePreviewDetail(); return; }
            if (now < _previewUpdateAt) return;
            int width, height;
            if (_exportCache == null && !_nativeOverlay.CanCaptureExport())
            {
                _previewDirty = false;
                ReleasePreviewTexture();
                SetPreviewStatus(UiTextKey.ExportTooLarge);
                return;
            }
            if (_exportCache == null)
            {
                ReleaseExportCache();
                _captureWork = _nativeOverlay.CaptureExport(
                    delegate(RecipeExport ready) { _exportCache = ready; });
                return;
            }
            if (!_exportCache.Dimensions(_exportSettings, out width, out height))
            {
                _previewDirty = false; ReleasePreviewTexture();
                SetPreviewStatus(UiTextKey.ExportTooLarge);
                return;
            }
            try
            {
                int side = Math.Min(2048, SystemInfo.maxTextureSize);
                _thumbnailJob = new ExportThumbnail(_exportCache, _exportSettings, side, 2000000);
                SetPreviewStatus(UiTextKey.PreviewUpdating);
            }
            catch (Exception exception)
            {
                _previewDirty = false;
                Logger.LogError("Export preview failed: " + exception);
                SetPreviewStatus(UiTextKey.ExportFailed);
            }
        }

        private void StepExportWork(ref IEnumerator work)
        {
            try
            {
                if (work.MoveNext()) return;
                IDisposable disposable = work as IDisposable;
                if (disposable != null) disposable.Dispose();
                work = null;
            }
            catch (Exception exception)
            {
                Logger.LogError("Image export operation failed: " + exception);
                DisposeExportWork(ref work);
                _previewDirty = false;
                SetPreviewStatus(UiTextKey.ExportFailed);
            }
        }

        private void DisposeExportWork(ref IEnumerator work)
        {
            IEnumerator previous = work; work = null;
            try
            {
                IDisposable disposable = previous as IDisposable;
                if (disposable != null) disposable.Dispose();
            }
            catch (Exception exception) { Logger.LogWarning("Export cancellation cleanup failed: " + exception); }
        }

        private void ReleasePreviewTexture()
        {
            if (_previewTexture != null) UnityEngine.Object.Destroy(_previewTexture);
            _previewTexture = null;
        }

        private void DisposeThumbnail()
        {
            if (_thumbnailJob != null) _thumbnailJob.Dispose();
            _thumbnailJob = null;
        }

        private void ReleaseExportCache()
        {
            DisposeThumbnail();
            ReleasePreviewDetail();
            ReleasePreviewTexture();
            RecipeExport cache = _exportCache; _exportCache = null;
            if (cache == null) return;
            try { cache.Dispose(); }
            catch (Exception exception) { Logger.LogWarning("Could not remove temporary export cache: " + exception); }
        }

        private void CloseExportPreview()
        {
            _exportPreviewOpen = false;
            PersistExportSettings();
            DisposeExportWork(ref _saveWork);
            DisposeExportWork(ref _captureWork);
            ReleaseExportCache();
            _previewDirty = false;
        }

        private IEnumerator SavePreviewPng(string path, RecipeExport cache, ExportSettings settings)
        {
            int width, height;
            if (!cache.Dimensions(settings, out width, out height)) yield break;
            string temporary = path + ".tmp";
            bool complete = false, created = false;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (FileStream file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                {
                    created = true;
                    using (ExportScanlines source = cache.OpenScanlines(settings))
                    using (PngStreamWriter.Encoder encoder = new PngStreamWriter.Encoder(file, width, height))
                    {
                        byte[] row = new byte[checked(width * 4 + 1)];
                        int y = 0;
                        while (y < height)
                        {
                            float start = Time.realtimeSinceStartup;
                            int count = 0;
                            do { source.Fill(y++, row); encoder.WriteRow(row); count++; }
                            while (y < height && count < 32 && Time.realtimeSinceStartup - start < 0.008f);
                            SetPreviewStatus(UiTextKey.SavingPngProgress, y * 100 / height);
                            yield return null;
                        }
                        encoder.Finish();
                    }
                }
                File.Move(temporary, path); complete = true;
                SetPreviewStatus(UiTextKey.PngSaved, path);
                Logger.LogInfo("Saved recipe PNG " + width + "x" + height + " (native 1x, layout=" + settings.Layout + ", margin=" + settings.Margin + "): " + path);
            }
            finally { if (created && !complete && File.Exists(temporary)) File.Delete(temporary); }
        }

        private void CopyPreviewImage()
        {
            try
            {
                int width, height, fullWidth, fullHeight;
                _exportCache.Dimensions(_exportSettings, out fullWidth, out fullHeight);
                byte[] pixels = _exportCache.Thumbnail(_exportSettings, 4096, 4000000, out width, out height);
                string error;
                if (ClipboardImage.Copy(pixels, width, height, out error))
                    SetPreviewStatus(width == fullWidth && height == fullHeight ? UiTextKey.ImageCopied : UiTextKey.ImageCopiedReduced, width, height);
                else
                {
                    SetPreviewStatus(UiTextKey.ClipboardFailed);
                    Logger.LogWarning("Clipboard image copy failed: " + error);
                }
            }
            catch (Exception exception)
            {
                SetPreviewStatus(UiTextKey.ClipboardFailed);
                Logger.LogError("Clipboard image copy failed: " + exception);
            }
        }

        private void EnsureCheckerTexture()
        {
            if (_checkerTexture != null) return;
            _checkerTexture = new Texture2D(32, 32, TextureFormat.RGB24, false);
            _checkerTexture.hideFlags = HideFlags.HideAndDontSave;
            _checkerTexture.wrapMode = TextureWrapMode.Repeat;
            _checkerTexture.filterMode = FilterMode.Point;
            Color32[] pixels = new Color32[1024];
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
                pixels[y * 32 + x] = ((x / 16 + y / 16) % 2 == 0) ? new Color32(180, 180, 180, 255) : new Color32(225, 225, 225, 255);
            _checkerTexture.SetPixels32(pixels); _checkerTexture.Apply(false, true);
            _uiTextures.Add(_checkerTexture);
        }

        private void DrawExportPreview()
        {
            Matrix4x4 previousMatrix = GUI.matrix;
            try
            {
                float uiScale = InterfaceViewScale();
                GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));
                float width = Screen.width / uiScale, height = Screen.height / uiScale;
                UiPreviewLayout layout = UiPreviewLayout.Create(width,height);
                Rect window = UiRect(layout.Window);
                GUI.Box(window, "", _menuFrameStyle);
                GUI.Box(new Rect(22, 22, width - 44, height - 44), "", _menuBoxStyle);
                bool interactive = !_appearanceOpen;
                GUI.enabled = interactive;
                GUI.Label(UiRect(layout.Title), RecipeUiText.Text(UiTextKey.ExportPreview), _headingStyle);
                if (GUI.Button(UiRect(layout.Interface),RecipeUiText.Text(UiTextKey.InterfaceSettings),_buttonStyle)) OpenAppearance();
                if (GUI.Button(UiRect(layout.Close), "X", _buttonStyle)) { CloseExportPreview(); return; }
                int fullWidth = 0, fullHeight = 0;
                bool safe = _exportCache != null && _exportCache.Dimensions(_exportSettings, out fullWidth, out fullHeight);
                GUI.Label(UiRect(layout.Resolution), fullWidth > 0 && fullHeight > 0
                    ? RecipeUiText.Text(UiTextKey.ExportResolution, fullWidth, fullHeight) : RecipeUiText.Text(UiTextKey.Rendering), _headingStyle);
                Rect previewArea = UiRect(layout.Preview);
                float fit = fullWidth > 0 && fullHeight > 0
                    ? Math.Min(1f, Math.Min((previewArea.width - 22) / fullWidth, (previewArea.height - 22) / fullHeight)) : 1f;
                float viewScale = fit * _previewZoom;
                if (interactive && Event.current.type == EventType.ScrollWheel && previewArea.Contains(Event.current.mousePosition) && Event.current.control)
                {
                    _previewZoom = Mathf.Clamp(_previewZoom * Mathf.Pow(1.12f, -Event.current.delta.y), 0.25f, 256f);
                    Event.current.Use();
                }
                GUI.Box(previewArea, "", _menuBoxStyle);
                float imageWidth = Math.Max(1, fullWidth * viewScale), imageHeight = Math.Max(1, fullHeight * viewScale);
                float areaWidth = previewArea.width - 20, areaHeight = previewArea.height - 20;
                Rect content = new Rect(0, 0, Math.Max(areaWidth, imageWidth), Math.Max(areaHeight, imageHeight));
                _previewScroll.x = Mathf.Clamp(_previewScroll.x, 0, Math.Max(0, content.width - areaWidth));
                _previewScroll.y = Mathf.Clamp(_previewScroll.y, 0, Math.Max(0, content.height - areaHeight));
                _previewScroll = GUI.BeginScrollView(previewArea, _previewScroll, content);
                if (_previewTexture != null)
                {
                    Rect image = new Rect(Math.Max(0, (areaWidth - imageWidth) / 2), Math.Max(0, (areaHeight - imageHeight) / 2), imageWidth, imageHeight);
                    if (_exportSettings.Background == ExportBackground.Transparent)
                    {
                        EnsureCheckerTexture();
                        GUI.DrawTextureWithTexCoords(image, _checkerTexture, new Rect(0, 0, image.width / 32, image.height / 32));
                    }
                    DrawPreviewDetail(image, areaWidth, areaHeight, viewScale, uiScale, fullWidth, fullHeight);
                }
                GUI.EndScrollView();
                GUIStyle zoomStyle = layout.CompactZoom ? _compactButtonStyle : _buttonStyle;
                if (GUI.Button(UiRect(layout.ZoomButtons[0]), RecipeUiText.Text(UiTextKey.FitPreview), zoomStyle)) { _previewZoom = 1f; _previewScroll = Vector2.zero; }
                if (GUI.Button(UiRect(layout.ZoomButtons[1]), "100%", zoomStyle)) _previewZoom = Math.Min(256f, 1f / Math.Max(0.001f, fit));
                if (GUI.Button(UiRect(layout.ZoomButtons[2]), "−", zoomStyle)) _previewZoom = Math.Max(0.25f, _previewZoom / 1.25f);
                if (GUI.Button(UiRect(layout.ZoomButtons[3]), "+", zoomStyle)) _previewZoom = Math.Min(256f, _previewZoom * 1.25f);
                GUI.Label(UiRect(layout.ZoomLabel), RecipeUiText.Text(layout.CompactZoom ? UiTextKey.PreviewZoomShort : UiTextKey.PreviewZoom, (int)Math.Round(viewScale * 100)), _statusStyle);
                bool busy = _captureWork != null || _saveWork != null;
                GUI.enabled = !busy && interactive;
                DrawExportSettings(UiRect(layout.Settings));
                GUI.enabled = interactive;
                GUI.Label(UiRect(layout.Status), RecipeUiText.Text(_previewStatus, _previewStatusValues), _statusStyle);
                GUI.enabled = interactive && !busy && !_previewDirty && safe && _previewTexture != null;
                if (GUI.Button(UiRect(layout.Actions[0]), RecipeUiText.Text(UiTextKey.CopyImage), _buttonStyle)) CopyPreviewImage();
                if (GUI.Button(UiRect(layout.Actions[1]), RecipeUiText.Text(UiTextKey.SavePng), _buttonStyle))
                {
                    string name = "recipes-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".png";
                    string path = Path.Combine(Paths.BepInExRootPath, Path.Combine("RecipePreviewExports", name));
                    _saveWork = SavePreviewPng(path, _exportCache, _exportSettings.Clone());
                    SetPreviewStatus(UiTextKey.SavingPng);
                }
                GUI.enabled = interactive;
                if (GUI.Button(UiRect(layout.Actions[2]), RecipeUiText.Text(UiTextKey.Cancel), _buttonStyle)) CloseExportPreview();
                if (interactive && (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp || Event.current.type == EventType.ScrollWheel))
                    Event.current.Use();
            }
            finally { GUI.matrix = previousMatrix; }
        }

        private void DrawExportSettings(Rect area)
        {
            // Settings can grow with RGB controls; keep them usable at 720p.
            _exportSettingsScroll = GUI.BeginScrollView(area, _exportSettingsScroll,
                new Rect(0, 0, 204, _exportSettings.Background == ExportBackground.Custom ? 418 : 320));
            float x = 0, y = 0;
            GUI.Label(new Rect(x, y, 204, 24), RecipeUiText.Text(UiTextKey.Background), _headingStyle); y += 28;
            UiTextKey[] backgrounds = { UiTextKey.DefaultBackground, UiTextKey.TransparentBackground, UiTextKey.WhiteBackground, UiTextKey.DarkBackground, UiTextKey.CustomBackground };
            for (int i = 0; i < backgrounds.Length; i++)
            {
                Rect button = new Rect(x + (i % 2) * 104, y + (i / 2) * 30, 100, 27);
                if (GUI.Button(button, RecipeUiText.Text(backgrounds[i]), _exportSettings.Background == (ExportBackground)i ? _selectedStyle : _buttonStyle))
                { _exportSettings.Background = (ExportBackground)i; ExportSettingsChanged(); }
            }
            y += 94;
            if (_exportSettings.Background == ExportBackground.Custom)
            {
                GUI.Label(new Rect(x, y, 204, 22), string.Format("#{0:X2}{1:X2}{2:X2}", _exportSettings.Red, _exportSettings.Green, _exportSettings.Blue), _statusStyle);
                y += 22;
                byte r = ColorSlider(x, y, "R", _exportSettings.Red); y += 24;
                byte g = ColorSlider(x, y, "G", _exportSettings.Green); y += 24;
                byte b = ColorSlider(x, y, "B", _exportSettings.Blue); y += 28;
                if (r != _exportSettings.Red || g != _exportSettings.Green || b != _exportSettings.Blue)
                { _exportSettings.Red = r; _exportSettings.Green = g; _exportSettings.Blue = b; ExportSettingsChanged(); }
            }
            GUI.Label(new Rect(x, y, 204, 24), RecipeUiText.Text(UiTextKey.ExportLayout), _headingStyle); y += 28;
            UiTextKey[] layouts = { UiTextKey.CompactMargin, UiTextKey.StandardMargin, UiTextKey.WideMargin };
            for (int i = 0; i < layouts.Length; i++)
                if (GUI.Button(new Rect(x + (i % 2) * 104, y + (i / 2) * 30, i == 2 ? 204 : 100, 27), RecipeUiText.Text(layouts[i]), _exportSettings.Layout == (ExportLayout)(i + 1) ? _selectedStyle : _buttonStyle))
                { _exportSettings.Layout = (ExportLayout)(i + 1); _previewScroll = Vector2.zero; ExportSettingsChanged(); }
            y += 68;
            GUI.Label(new Rect(x, y, 204, 24), RecipeUiText.Text(UiTextKey.ExportMargins), _headingStyle); y += 28;
            UiTextKey[] margins = { UiTextKey.NoMargin, UiTextKey.CompactMargin, UiTextKey.StandardMargin, UiTextKey.WideMargin };
            for (int i = 0; i < 4; i++)
                if (GUI.Button(new Rect(x + (i % 2) * 104, y + (i / 2) * 30, 100, 27), RecipeUiText.Text(margins[i]), _exportSettings.Margin == (ExportMargin)i ? _selectedStyle : _buttonStyle))
                { _exportSettings.Margin = (ExportMargin)i; ExportSettingsChanged(); }
            GUI.EndScrollView();
        }

        private byte ColorSlider(float x, float y, string label, byte value)
        {
            GUI.Label(new Rect(x, y, 42, 22), label + " " + value, _statusStyle);
            return (byte)Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(x + 48, y + 6, 156, 16), value, 0, 255));
        }

        private struct PreviewRegion
        {
            internal int Left, Top, Width, Height, TextureWidth, TextureHeight;
            internal bool Same(PreviewRegion other)
            {
                return Left == other.Left && Top == other.Top && Width == other.Width && Height == other.Height &&
                    TextureWidth == other.TextureWidth && TextureHeight == other.TextureHeight;
            }
        }

        private void DrawPreviewDetail(Rect image, float areaWidth, float areaHeight,
            float viewScale, float uiScale, int fullWidth, int fullHeight)
        {
            if (_previewDirty || _exportCache == null || viewScale <= 0 ||
                (_previewTexture.width >= fullWidth * viewScale * uiScale && _previewTexture.height >= fullHeight * viewScale * uiScale))
            {
                _detailRequested = false;
                GUI.DrawTexture(image, _previewTexture, ScaleMode.StretchToFill, true);
                return;
            }
            PreviewRegion next = new PreviewRegion();
            next.Left = Math.Max(0, (int)Math.Floor((_previewScroll.x - image.x) / viewScale));
            next.Top = Math.Max(0, (int)Math.Floor((_previewScroll.y - image.y) / viewScale));
            int right = Math.Min(fullWidth, (int)Math.Ceiling((_previewScroll.x + areaWidth - image.x) / viewScale));
            int bottom = Math.Min(fullHeight, (int)Math.Ceiling((_previewScroll.y + areaHeight - image.y) / viewScale));
            next.Width = right - next.Left; next.Height = bottom - next.Top;
            if (next.Width < 1 || next.Height < 1)
            {
                _detailRequested = false;
                GUI.DrawTexture(image, _previewTexture, ScaleMode.StretchToFill, true);
                return;
            }
            int tw = Math.Max(1, Math.Min(next.Width, (int)Math.Ceiling(next.Width * viewScale * uiScale)));
            int th = Math.Max(1, Math.Min(next.Height, (int)Math.Ceiling(next.Height * viewScale * uiScale)));
            RecipeExport.ThumbnailSize(tw, th, Math.Min(2048, SystemInfo.maxTextureSize), 2000000, out next.TextureWidth, out next.TextureHeight);
            if (!_detailRequested || !next.Same(_detailRequest))
            {
                _detailRequest = next; _detailRequested = true;
                _detailUpdateAt = Math.Max(Time.realtimeSinceStartup + 0.1f, _detailRetryAt);
            }
            if (_detailTexture != null && next.Same(_detailReady))
            {
                Rect tile = new Rect(image.x + next.Left * viewScale, image.y + next.Top * viewScale,
                    next.Width * viewScale, next.Height * viewScale);
                // Draw disjoint areas so translucent edges are blended once.
                DrawThumbnailPiece(image, new Rect(image.x, image.y, image.width, tile.y - image.y));
                DrawThumbnailPiece(image, new Rect(image.x, tile.yMax, image.width, image.yMax - tile.yMax));
                DrawThumbnailPiece(image, new Rect(image.x, tile.y, tile.x - image.x, tile.height));
                DrawThumbnailPiece(image, new Rect(tile.xMax, tile.y, image.xMax - tile.xMax, tile.height));
                GUI.DrawTexture(tile, _detailTexture, ScaleMode.StretchToFill, true);
            }
            else GUI.DrawTexture(image, _previewTexture, ScaleMode.StretchToFill, true);
        }

        private void DrawThumbnailPiece(Rect image, Rect piece)
        {
            if (piece.width <= 0 || piece.height <= 0) return;
            GUI.DrawTextureWithTexCoords(piece, _previewTexture,
                new Rect((piece.x - image.x) / image.width, 1f - (piece.yMax - image.y) / image.height,
                    piece.width / image.width, piece.height / image.height), true);
        }

        private void UpdatePreviewDetail()
        {
            if (!_detailRequested || !_detailBuilding.Same(_detailRequest))
            {
                if (_detailJob != null) _detailJob.Dispose();
                _detailJob = null;
            }
            if (!_detailRequested || Time.realtimeSinceStartup < _detailUpdateAt ||
                (_detailTexture != null && _detailReady.Same(_detailRequest))) return;
            try
            {
                if (_detailJob == null)
                {
                    _detailBuilding = _detailRequest;
                    _detailJob = new ExportThumbnail(_exportCache, _exportSettings,
                        _detailBuilding.Left, _detailBuilding.Top, _detailBuilding.Width, _detailBuilding.Height,
                        _detailBuilding.TextureWidth, _detailBuilding.TextureHeight);
                }
                float start = Time.realtimeSinceStartup;
                int count = 0;
                while (_detailJob.NextRow())
                    if (++count >= 32 || Time.realtimeSinceStartup - start >= 0.008f) return;
                byte[] pixels = _detailJob.Pixels;
                Color32[] colors = new Color32[_detailJob.Width * _detailJob.Height];
                for (int i = 0; i < colors.Length; i++) colors[i] = new Color32(pixels[i * 4], pixels[i * 4 + 1], pixels[i * 4 + 2], pixels[i * 4 + 3]);
                if (_detailTexture != null) UnityEngine.Object.Destroy(_detailTexture);
                _detailTexture = new Texture2D(_detailJob.Width, _detailJob.Height, TextureFormat.RGBA32, false);
                _detailTexture.hideFlags = HideFlags.HideAndDontSave;
                _detailTexture.wrapMode = TextureWrapMode.Clamp; _detailTexture.filterMode = FilterMode.Bilinear;
                _detailTexture.SetPixels32(colors); _detailTexture.Apply(false, true);
                _detailReady = _detailBuilding;
                _detailJob.Dispose(); _detailJob = null;
            }
            catch (Exception exception)
            {
                ReleasePreviewDetail();
                // Keep the full-board thumbnail usable if optional detail fails.
                _detailRetryAt = Time.realtimeSinceStartup + 2f;
                Logger.LogWarning("Could not render preview detail: " + exception);
            }
        }

        private void ReleasePreviewDetail()
        {
            if (_detailJob != null) _detailJob.Dispose();
            _detailJob = null; _detailRequested = false;
            if (_detailTexture != null) UnityEngine.Object.Destroy(_detailTexture);
            _detailTexture = null;
        }
    }
}
