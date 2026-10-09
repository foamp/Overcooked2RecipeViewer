using System;
using BepInEx.Logging;

namespace Overcooked2RecipeViewer
{
    internal enum UiTextKey
    {
        None, Heading, PresetButton, Original, OriginalOrder, AutoArrange, Name, Cookware,
        Custom, CustomLastEdit, SavedName, SaveLayout, SavePng, OpenPngFolder, Close,
        DeleteTooltip, MovedRow, CreatedRow, MovedRecipe, RestoreDraft, LoadedSaved,
        NoDraft, SavedUnavailable, StorageUnavailable, SavedLayout, SaveFailed,
        DeletedLayout, DeleteFailed, SessionOnly, AutoSaveFailed, PresetApplied,
        SortFailed, Grouped, Zoom, Rendering, ExportTooLarge, RenderingCard,
        ExportFailed, SavingPng, PngCopied, PngNoClipboard, PngFailed,
        ShortcutCategory, ShortcutName, ShortcutDescription,
        ExportPreview, ExportResolution, Background, DefaultBackground, TransparentBackground,
        WhiteBackground, DarkBackground, CustomBackground, ExportLayout, ExportMargins,
        NoMargin, CompactMargin, StandardMargin, WideMargin, CopyImage, Cancel,
        FitPreview, PreviewZoom, PreviewZoomShort, PreviewReady, PreviewUpdating, PreviewCapturing,
        SavingPngProgress, PngSaved, ImageCopied, ImageCopiedReduced, ClipboardFailed, ExportSettingsFailed,
        InterfaceSettings, InterfaceScale, InterfaceTheme, KitchenTheme, CreamTheme,
        OceanTheme, CharcoalTheme, InterfaceSettingsFailed, ResetInterface
    }

    internal static class RecipeUiText
    {
        private static bool _chinese;
        private static bool _languageErrorLogged;

        internal static bool Chinese { get { return _chinese; } }

        internal static void RefreshLanguage(ManualLogSource logger)
        {
            try
            {
                SupportedLanguages language = Localization.GetLanguage();
                _chinese = IsChinese(language);
            }
            catch (Exception exception)
            {
                if (!_languageErrorLogged)
                {
                    _languageErrorLogged = true;
                    logger.LogWarning("Could not read game UI language; keeping current UI language: " +
                        exception);
                }
            }
        }

        internal static bool IsChinese(SupportedLanguages language)
        {
            return language == SupportedLanguages.Chinese ||
                language == SupportedLanguages.ChineseTraditional;
        }

        private static string Pair(string english, string chinese)
        {
            return _chinese ? chinese : english;
        }

        internal static string PresetLabel(RecipeBoard.SortPreset preset, bool menu)
        {
            switch (preset)
            {
                case RecipeBoard.SortPreset.Original:
                    return Text(menu ? UiTextKey.OriginalOrder : UiTextKey.Original);
                case RecipeBoard.SortPreset.AutoArrange: return Text(UiTextKey.AutoArrange);
                case RecipeBoard.SortPreset.Name: return Text(UiTextKey.Name);
                default: return Text(UiTextKey.Cookware);
            }
        }

        // The stored name is an identity; language changes only its display.
        internal static string LayoutName(string name)
        {
            if (name != null && name.StartsWith("Saved ", StringComparison.Ordinal))
            {
                int number;
                if (int.TryParse(name.Substring(6), out number))
                    return Text(UiTextKey.SavedName, number);
            }
            return name ?? string.Empty;
        }

        internal static string Text(UiTextKey key, params object[] values)
        {
            string format;
            switch (key)
            {
                case UiTextKey.None: return string.Empty;
                case UiTextKey.ExportPreview: format = Pair("Image export preview", "图片导出预览"); break;
                case UiTextKey.ExportResolution: format = "{0} × {1} px"; break;
                case UiTextKey.Background: format = Pair("Background", "背景"); break;
                case UiTextKey.DefaultBackground: format = Pair("Default", "原版默认"); break;
                case UiTextKey.TransparentBackground: format = Pair("Transparent", "透明"); break;
                case UiTextKey.WhiteBackground: format = Pair("White", "纯白"); break;
                case UiTextKey.DarkBackground: format = Pair("Dark", "深色"); break;
                case UiTextKey.CustomBackground: format = Pair("Custom color", "自定义颜色"); break;
                case UiTextKey.ExportLayout: format = Pair("Image layout", "图片排版"); break;
                case UiTextKey.ExportMargins: format = Pair("Margins", "边距"); break;
                case UiTextKey.NoMargin: format = Pair("None", "无边距"); break;
                case UiTextKey.CompactMargin: format = Pair("Compact", "紧凑"); break;
                case UiTextKey.StandardMargin: format = Pair("Standard", "标准"); break;
                case UiTextKey.WideMargin: format = Pair("Wide", "宽松"); break;
                case UiTextKey.CopyImage: format = Pair("Copy image", "复制到剪贴板"); break;
                case UiTextKey.Cancel: format = Pair("Cancel", "取消"); break;
                case UiTextKey.FitPreview: format = Pair("Fit", "适应"); break;
                case UiTextKey.PreviewZoom: format = Pair("View: {0}% · Ctrl + wheel", "查看：{0}% · Ctrl + 滚轮"); break;
                case UiTextKey.PreviewZoomShort: format = Pair("View: {0}%", "查看：{0}%"); break;
                case UiTextKey.InterfaceSettings: format = Pair("Interface", "界面设置"); break;
                case UiTextKey.InterfaceScale: format = Pair("Interface scale", "界面缩放"); break;
                case UiTextKey.InterfaceTheme: format = Pair("Color theme", "界面配色"); break;
                case UiTextKey.KitchenTheme: format = Pair("Kitchen", "厨房绿"); break;
                case UiTextKey.CreamTheme: format = Pair("Cream", "奶油白"); break;
                case UiTextKey.OceanTheme: format = Pair("Ocean", "深海蓝"); break;
                case UiTextKey.CharcoalTheme: format = Pair("Charcoal", "炭灰"); break;
                case UiTextKey.InterfaceSettingsFailed: format = Pair("Could not save interface settings; see log.", "界面设置保存失败，请查看日志。"); break;
                case UiTextKey.ResetInterface: format = Pair("Reset", "恢复默认"); break;
                case UiTextKey.PreviewReady: format = Pair("Ready to export. PNG preserves the full resolution.", "预览已更新，可以导出完整分辨率 PNG。"); break;
                case UiTextKey.PreviewUpdating: format = Pair("Updating preview...", "正在更新预览……"); break;
                case UiTextKey.PreviewCapturing: format = "{0}"; break;
                case UiTextKey.SavingPngProgress: format = Pair("Saving full PNG: {0}%", "正在保存完整 PNG：{0}%"); break;
                case UiTextKey.PngSaved: format = Pair("PNG saved: {0}", "PNG 已保存：{0}"); break;
                case UiTextKey.ImageCopied: format = Pair("Image copied: {0} × {1} px", "已复制图片：{0} × {1} px"); break;
                case UiTextKey.ImageCopiedReduced: format = Pair("Copied at {0} × {1} px for clipboard safety. Export image for full resolution.", "已复制 {0} × {1} px 缩略图；完整分辨率请导出图片。"); break;
                case UiTextKey.ClipboardFailed: format = Pair("Clipboard copy failed; see BepInEx log.", "剪贴板复制失败，请查看 BepInEx 日志。"); break;
                case UiTextKey.ExportSettingsFailed: format = Pair("Could not save export settings; see BepInEx log.", "无法保存导出设置，请查看 BepInEx 日志。"); break;
                case UiTextKey.Heading: format = Pair("Recipes: {0}   |   Zoom: {1}%   |   Ctrl + wheel: zoom",
                    "菜谱：{0}   |   缩放：{1}%   |   Ctrl + 滚轮：缩放"); break;
                case UiTextKey.PresetButton: format = Pair("Preset: {0}  ▼", "排序预设：{0}  ▼"); break;
                case UiTextKey.Original: format = Pair("Original", "原始顺序"); break;
                case UiTextKey.OriginalOrder: format = Pair("Original order", "原始顺序"); break;
                case UiTextKey.AutoArrange: format = Pair("Auto arrange", "自动排列"); break;
                case UiTextKey.Name: format = Pair("Name A-Z", "名称 A–Z"); break;
                case UiTextKey.Cookware: format = Pair("Cookware", "按厨具排序"); break;
                case UiTextKey.Custom: format = Pair("Custom", "自定义"); break;
                case UiTextKey.CustomLastEdit: format = Pair("Custom (last edit)", "自定义（上次编辑）"); break;
                case UiTextKey.SavedName: format = Pair("Saved {0}", "已保存布局 {0}"); break;
                case UiTextKey.SaveLayout: format = Pair("Save layout", "保存布局"); break;
                case UiTextKey.SavePng: format = Pair("Export image", "导出图片"); break;
                case UiTextKey.OpenPngFolder: format = Pair("Open PNG folder", "打开图片目录"); break;
                case UiTextKey.Close: format = Pair("Close", "关闭"); break;
                case UiTextKey.DeleteTooltip: format = Pair("Delete saved layout", "删除此保存布局"); break;
                case UiTextKey.MovedRow: format = Pair("Moved row {0} to {1}.", "已将第 {0} 行移至第 {1} 行。"); break;
                case UiTextKey.CreatedRow: format = Pair("Created a new row for {0}.", "已将菜谱放入新建的一行。"); break;
                case UiTextKey.MovedRecipe: format = Pair("Moved {0}.", "已移动菜谱。"); break;
                case UiTextKey.RestoreDraft: format = Pair("Restored last edited layout ({0} rows).",
                    "已恢复上次编辑的布局（共 {0} 行）。"); break;
                case UiTextKey.LoadedSaved: format = Pair("Loaded saved layout {0} ({1} rows).",
                    "已载入“{0}”（共 {1} 行）。"); break;
                case UiTextKey.NoDraft: format = Pair("No custom layout has been saved for this level.",
                    "此关卡尚无自动保存的自定义布局。"); break;
                case UiTextKey.SavedUnavailable: format = Pair("Saved layout is unavailable for this level.",
                    "此关卡的保存布局不可用。"); break;
                case UiTextKey.StorageUnavailable: format = Pair("Layout storage unavailable; see BepInEx log.",
                    "布局存储不可用，请查看 BepInEx 日志。"); break;
                case UiTextKey.SavedLayout: format = Pair("Saved layout {0} for this level.",
                    "已为此关卡保存“{0}”。"); break;
                case UiTextKey.SaveFailed: format = Pair("Could not save layout; see BepInEx log.",
                    "无法保存布局，请查看 BepInEx 日志。"); break;
                case UiTextKey.DeletedLayout: format = Pair("Deleted saved layout {0}.",
                    "已删除“{0}”。"); break;
                case UiTextKey.DeleteFailed: format = Pair("Could not delete layout; see BepInEx log.",
                    "无法删除布局，请查看 BepInEx 日志。"); break;
                case UiTextKey.SessionOnly: format = Pair("Layout changed for this session; storage unavailable.",
                    "已调整当前布局，但存储不可用，本次修改仅在当前页面有效。"); break;
                case UiTextKey.AutoSaveFailed: format = Pair("Layout changed but could not be saved; see BepInEx log.",
                    "已调整布局，但无法自动保存，请查看 BepInEx 日志。"); break;
                case UiTextKey.PresetApplied: format = Pair("Preset: {0} ({1} rows).",
                    "排序预设：{0}（共 {1} 行）。"); break;
                case UiTextKey.SortFailed: format = Pair("Sort preset failed; see BepInEx log.",
                    "应用排序预设失败，请查看 BepInEx 日志。"); break;
                case UiTextKey.Grouped: format = Pair("Grouped {0} recipes into {1} categories.",
                    "已将 {0} 个菜谱分为 {1} 组。"); break;
                case UiTextKey.Zoom: format = Pair("Zoom: {0}% (Ctrl + wheel).",
                    "缩放：{0}%（Ctrl + 滚轮）。"); break;
                case UiTextKey.Rendering: format = Pair("Rendering original order cards...", "正在生成菜谱图片……"); break;
                case UiTextKey.ExportTooLarge: format = Pair("Board dimensions are too large to export safely.",
                    "页面尺寸过大，无法安全导出。"); break;
                case UiTextKey.RenderingCard: format = Pair("Rendering card {0}/{1}...",
                    "正在生成菜谱图片：{0}/{1}……"); break;
                case UiTextKey.ExportFailed: format = Pair("Image export failed. See BepInEx log.",
                    "图片导出失败，请查看 BepInEx 日志。"); break;
                case UiTextKey.SavingPng: format = Pair("Saving full-length PNG...", "正在保存完整 PNG 图片……"); break;
                case UiTextKey.PngCopied: format = Pair("Saved full PNG and copied image: {0}",
                    "已保存完整 PNG 并复制到剪贴板：{0}"); break;
                case UiTextKey.PngNoClipboard: format = Pair("Saved full PNG (clipboard unavailable): {0}",
                    "已保存完整 PNG（剪贴板不可用）：{0}"); break;
                case UiTextKey.PngFailed: format = Pair("PNG save failed. See BepInEx log.",
                    "PNG 保存失败，请查看 BepInEx 日志。"); break;
                case UiTextKey.ShortcutCategory: format = Pair("Keyboard shortcuts", "快捷键"); break;
                case UiTextKey.ShortcutName: format = Pair("Toggle all recipe images", "显示／关闭全部菜谱"); break;
                case UiTextKey.ShortcutDescription: format = Pair(
                    "Show or hide every possible recipe in the current level using the game's original recipe cards.",
                    "使用游戏原版菜谱卡片显示或关闭当前关卡的全部可能菜谱。"); break;
                default: return string.Empty;
            }
            if (key == UiTextKey.LoadedSaved || key == UiTextKey.SavedLayout ||
                key == UiTextKey.DeletedLayout || key == UiTextKey.PresetApplied)
            {
                values = (object[])values.Clone();
                values[0] = key == UiTextKey.PresetApplied
                    ? PresetLabel((RecipeBoard.SortPreset)values[0], false)
                    : LayoutName((string)values[0]);
            }
            return string.Format(format, values);
        }
    }

}
