# Overcooked2RecipePreview
A BepInEx mod for Overcooked! 2 with recipe preview, arrangement and customization features.

## Overcooked 2 Recipe Preview — 最小验证版

当前版本：`0.8.3`。

这个 BepInEx 5 插件会在关卡场景开始加载之前读取所选关卡配置，随后：

- 在 BepInEx 日志中打印这一关所有可能出现的菜谱；
- 在 BepInEx Configuration Manager 的 Mod Settings 中注册可修改的显示快捷键；
- 按快捷键时，克隆游戏原版订单卡片，显示该关所有可能出现的菜谱；
- 鼠标拖动菜谱调整行内及跨行位置；拖到两行间的窄插入区可新建一行；拖动左侧把手可移动整行。排序方式从 Preset 下拉菜单选择；
- 点击 `Save PNG` 将当前完整排列（包括滚动区域外的菜谱）保存为可分享、可打印的长图，并自动把图片复制到 Windows 剪贴板；
- 点击 `Open PNG folder` 打开图片保存目录；
- 再按一次相同快捷键关闭图片界面；
- 不修改订单生成、顺序、权重、分数或关卡数据。

## 已确认的数据链

关卡选择进入厨房时会调用：

`PlayerLobbyFlowroutine.LoadKitchen(int, string, Sprite, GameSession.GameLevelSettings)`

此时 `GameLevelSettings.SceneDirectoryVarientEntry.LevelConfig` 已经包含对应玩家人数变体的关卡配置，而 `LoadingScreenFlow.LoadScene(...)` 尚未执行，所以可以在正式加载场景前读取。

为了兼容会绕过原版大厅流程的 DIYLevel、街机类 MOD，插件还会在 `LoadingScreenFlow.LoadScene(...)` 入口再次检查 `GameSession.LevelSettings`。两个入口之间有去重，不会重复采集同一关。

菜谱数据主要来自：

- `LevelConfigBase.GetAllRecipes()`：游戏提供的统一公开入口；
- `CampaignLevelConfigBase.GetAllRecipes()`：普通关卡的 `RoundData.m_recipes.m_recipes`；
- `DynamicCampaignLevelConfigBase.GetAllRecipes()`：汇总所有 `DynamicRoundData.Phases[*].Recipes`；
- `ScriptedRoundData.m_manualOrder`：脚本关卡的固定前置订单；
- `SinglePlayerLevelConfig.RecipeOrder`：单人特殊关卡订单。

插件会按 Unity 对象实例去重。日志仍使用 `OrderDefinitionNode.name`、`m_uID` 和节点类型方便排查。预览卡片使用游戏自带的 `RecipeWidgetUIController` 和 `RecipeWidgetTile` 生成，不再手工拼接图片。全部菜谱放在同一张可滚动的长页面中，可用鼠标滚轮、右侧滚动条或 PageUp / PageDown 浏览。Shift + 鼠标滚轮或底部滚动条可横向查看超宽的行。相邻卡片之间保留固定空隙，但卡片的位置按其原版组件实际渲染宽度计算，不使用固定列宽。

## 直接安装

将 `Overcooked2RecipePreview.dll` 复制到：

`<游戏目录>\BepInEx\plugins\Overcooked2RecipePreview.dll`

启动游戏并进入任意厨房关卡。默认按 `Insert` 显示全部菜单图片，再按 `Insert` 关闭。同样的菜单数据会写入：

`<游戏目录>\BepInEx\LogOutput.log`

搜索 `=== Recipe preview ===` 即可定位。

## Mod Settings 设置

打开 Configuration Manager 后会新增：

`Overcooked 2 Recipe Preview 0.8.3`

其中 `Toggle all recipe images` 是显示/关闭快捷键，默认值为 `Insert`。可以在 Keyboard shortcuts 页面直接改成其他按键。已有配置不会被默认值覆盖。

## 菜单排序与拖拽（待实机验证）

首次打开某关时默认按 Cookware 排序。拖动菜谱或整行后自动切换为「自定义」，并把当前行结构与顺序写入 %APPDATA%\Overcooked2RecipePreview\layouts.txt；再次按 Insert 打开或重启游戏后打开同一关，会优先恢复上次编辑的布局。点击 Preset 打开下拉菜单，明确选择 Original order、Auto arrange、Name A-Z、Cookware、上次编辑的自定义布局，或已保存的布局。点击 Save layout 可将当前布局另存为该关的 Saved 1、Saved 2 等快照；这些快照会出现在 Preset 中。选择内置排序或已保存快照不会抹掉上次编辑的自定义布局，只有再次拖拽才更新它。原有 Auto arrange 的分组结果和规则保持不变，只是移到了菜单里。名称排序使用订单资源名；厨具排序优先使用游戏资源中明确命名的 CookingStepData 设备（例如 Pot、Steamer、FryingPan、OvenTray），以实际烹饪设备分组（搅拌只作为无烹饪步骤时的分类），多种烹饪设备的组合单独成行；未知步骤才回退到当前厨房的工作站和可识别锅具组件。无法确定时保留宽泛分组。游戏没有可靠的菜品类型字段，所以不提供按名称猜测的菜品类型预设。

拖动卡片可调整行内与跨行顺序。相邻两行中央约 10 个界面单位的范围是新行插入区，拖入时显示金色横线；行首上方和末行下方继续沿用原有跨行拖动行为。每行左侧的三条横纹是整行把手，拖动时整行变淡并显示目标位置。拖拽结束后空行会移除。横向滚动不移动整行把手。PNG 导出使用当前的卡片顺序和行结构。

Ctrl + 鼠标滚轮可缩放菜单内容（向上放大、向下缩小，范围 50%–200%），顶部显示当前百分比。缩放优先以鼠标位置为中心；普通滚轮仍纵向滚动，Shift + 滚轮仍横向滚动。缩放只影响显示，不改变行结构、自定义布局或 Preset。当前游戏运行期间再次打开预览会保留缩放，重启游戏恢复 100%。拖拽、导出和 Preset 下拉菜单打开期间不调整缩放。PNG 导出使用正常比例，导出后恢复页面缩放及滚动位置。

界面语言通过游戏自身的 Localization.GetLanguage() 读取：简体或繁体中文使用中文界面，其他语言统一使用英文界面。工具栏、预设、保存布局的显示名称、操作状态和导出反馈都会切换；配置管理器中的快捷键显示名称和说明也提供双语。配置键、布局存储标识和原版菜谱图片保持稳定。

在排序预设菜单里，每个已保存布局右侧都有 X。点击会立即删除这一保存项并写入本地文件，其他保存项、自动保存的自定义草稿和其他关卡不会被删除。删除当前选中的保存项时，本页排列仍然保留；再次打开预览仍优先恢复上次手动编辑的草稿。

## 重新编译

在此目录打开 PowerShell，运行：

```powershell
.\build.ps1
```

如果游戏不在脚本默认位置：

```powershell
.\build.ps1 -GameDir '你的胡闹厨房2目录'
```

本机使用 .NET Framework 3.5 的 `csc.exe`，并通过 `/noconfig`、`/nostdlib+` 显式引用游戏自带的 `mscorlib.dll`、`System.dll`、`System.Core.dll`。这样可在编译时发现游戏运行时不存在的类型和方法，产物保持 v2 CLR 元数据格式。

## 编译引用

必需：

- `BepInEx\core\BepInEx.dll`
- `BepInEx\core\0Harmony.dll`
- `Overcooked2_Data\Managed\Assembly-CSharp.dll`
- `Overcooked2_Data\Managed\UnityEngine.dll`
- `Overcooked2_Data\Managed\UnityEngine.CoreModule.dll`
- `Overcooked2_Data\Managed\UnityEngine.AnimationModule.dll`
- `Overcooked2_Data\Managed\UnityEngine.ImageConversionModule.dll`
- `Overcooked2_Data\Managed\UnityEngine.IMGUIModule.dll`
- UnityEngine.TextRenderingModule.dll（位于 Overcooked2_Data/Managed）
- `Overcooked2_Data\Managed\UnityEngine.UIModule.dll`
- `Overcooked2_Data\Managed\UnityEngine.UI.dll`

当前游戏环境核对结果：BepInEx `5.4.23.1`，Harmony `2.9.0.0`。

## 验证重点

优先测试三类关卡：

1. 普通故事关；
2. 会中途切换菜谱池的动态关卡；
3. 有固定出单脚本的教程或特殊关卡。

预览会克隆游戏的原版订单卡片，但不会调用 `RecipeFlowGUI.AddElement`，因此不会进入真实订单列表或参与倒计时、得分。克隆卡片上的 `UI_Move` 偏移材质会在生成后关闭，避免旧版出现的图片跑到屏幕左上角的问题。插件隐藏原版订单的绿色进度条，在拖动时给卡片加边框、显示插入占位框，并让其他卡片实时避让。`0.8.3` 以原版上方纸张背景自身的完整边界居中成品图，并考虑菜谱图片内的透明留白来放大成品，保持它与上框更协调。滚动范围会在卡片渲染后根据实际底边再次校准。`Auto arrange` 保留原有名称规则，将汉堡、披萨等类别分别放到一排；一排太宽时可以横向滚动。`Save PNG` 不直接发送实体打印任务，而是逐张采集卡片并写入浅米色背景的完整长图，保存在 `BepInEx\RecipePreviewExports`，可再分享或从图片查看器打印。PNG 写入器不依赖显卡最大纹理高度；保存后会自动把全图或适度缩小的全图复制到 Windows 剪贴板。若剪贴板被占用，PNG 文件仍会保留。
