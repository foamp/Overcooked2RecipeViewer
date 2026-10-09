# Overcooked2RecipeViewer v0.9.0

## 简体中文

本次更新新增完整图片导出预览：导出前可缩放、滚动查看，调整背景、排版间距和外围边距，再保存 PNG 或复制图片。

- 支持透明、原版默认、白色、深色及自定义 RGB 背景，自动裁去透明空白并保留阴影。
- 紧凑、标准、宽松三种排版保留手动分行和行内顺序；四档边距独立调整，不修改游戏内布局。
- 固定原版卡片 1x 输出，捕获后显示像素尺寸；完整 PNG 使用流式编码，预览和缓存设有安全限制。
- 新增 50%–200% 界面缩放及厨房绿、奶油白、深海蓝、炭灰四种配色；导出与界面设置自动记忆。
- 沿用 BepInEx 5、原插件 GUID、快捷键、配置及布局存储路径。不修改游戏实际订单、计时或得分。

安装前关闭游戏，用 ZIP 中唯一的 DLL 替换现有 Viewer DLL，保留配置与布局，避免重复安装。进入关卡按 Insert，选择「导出图片」打开预览。超大图片的剪贴板副本会缩小并提示尺寸；PNG 保持完整分辨率。1x 沿用原版素材，不提供额外的高清素材细节。

## English

v0.9.0 introduces a full-image export preview. Check the complete recipe board, adjust its appearance, then export a PNG or copy the image.

### What's new

- Zoom, scroll and fit the preview to the window, with visible-region detail when enlarged.
- Choose transparent, original default, white, dark or custom RGB backgrounds. Transparent space is trimmed while preserving shadows; four outer margins are available.
- Adjust Compact, Standard or Wide spacing while preserving manual rows and card order. Export settings leave the viewer and saved layouts unchanged.
- Export at the original card's fixed 1x size, with exact dimensions shown after capture. Shared rendering, cached cards and streaming PNG encoding support complete boards beyond the visible screen, with safety limits for large images.
- Adjust interface scale from 50% to 200% and choose Kitchen, Cream, Ocean or Charcoal colors. A wrapping toolbar, rounded controls and remembered preferences keep the interface flexible.
- Export and interface settings persist in the existing configuration file. Plugin GUID, shortcut settings, saved-layout format and storage paths remain compatible.

### Install and use

Close the game, extract the single DLL from **Overcooked2RecipeViewer-v0.9.0.zip**, and replace the existing Viewer DLL under `BepInEx\plugins`. Keep only one copy of the mod and retain your configuration and layouts. Requires Overcooked! 2 for Windows / Steam and BepInEx 5.

Press **Insert → Export image**, adjust the preview, then choose **Export image** or **Copy image**. PNGs are saved in `BepInEx\RecipePreviewExports`. Interface settings are available in the toolbar and preview header.

### Notes

Native 1x retains the original assets' detail limits. Large clipboard copies are reduced with a size message; PNG exports retain full resolution. Clipboard transparency depends on the receiving application. Game orders, timers and scoring are unchanged.
