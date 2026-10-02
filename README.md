# Overcooked 2 Recipe Preview — 最小验证版

当前版本：`0.8.3`。

这个 BepInEx 5 插件会在关卡场景开始加载之前读取所选关卡配置，随后：

- 在 BepInEx 日志中打印这一关所有可能出现的菜谱；
- 在 BepInEx Configuration Manager 的 Mod Settings 中注册可修改的显示快捷键；
- 按快捷键时，克隆游戏原版订单卡片，显示该关所有可能出现的菜谱；
- 鼠标拖动菜谱到其他行；拖动时会显示占位框，其他卡片实时让位。也可点击 `Auto arrange` 按类别自动整理；
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

插件会按 Unity 对象实例去重。日志仍使用 `OrderDefinitionNode.name`、`m_uID` 和节点类型方便排查。预览卡片使用游戏自带的 `RecipeWidgetUIController` 和 `RecipeWidgetTile` 生成，不再手工拼接图片。全部菜谱放在同一张可滚动的长页面中，可用鼠标滚轮、右侧滚动条或 PageUp / PageDown 浏览。Shift + 鼠标滚轮可横向滚动超宽的分类行。相邻卡片之间保留固定空隙，但卡片的位置按其原版组件实际渲染宽度计算，不使用固定列宽。

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

## 重新编译

在此目录打开 PowerShell，运行：

```powershell
.\build.ps1
```

如果游戏不在脚本默认位置：

```powershell
.\build.ps1 -GameDir '你的胡闹厨房2目录'
```

本机使用 .NET Framework 3.5 的 `csc.exe`，使产物保持游戏当前 Mono/BepInEx 环境使用的 v2 CLR 元数据格式。

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
- `Overcooked2_Data\Managed\UnityEngine.UIModule.dll`
- `Overcooked2_Data\Managed\UnityEngine.UI.dll`

当前游戏环境核对结果：BepInEx `5.4.23.1`，Harmony `2.9.0.0`。

## 验证重点

优先测试三类关卡：

1. 普通故事关；
2. 会中途切换菜谱池的动态关卡；
3. 有固定出单脚本的教程或特殊关卡。

预览会克隆游戏的原版订单卡片，但不会调用 `RecipeFlowGUI.AddElement`，因此不会进入真实订单列表或参与倒计时、得分。克隆卡片上的 `UI_Move` 偏移材质会在生成后关闭，避免旧版出现的图片跑到屏幕左上角的问题。插件隐藏原版订单的绿色进度条，在拖动时给卡片加边框、显示插入占位框，并让其他卡片实时避让。`0.8.3` 以原版上方纸张背景自身的完整边界居中成品图，并考虑菜谱图片内的透明留白来放大成品，保持它与上框更协调。滚动范围会在卡片渲染后根据实际底边再次校准。`Auto arrange` 使用内置名称规则将汉堡、披萨等类别分别放到一排；一排太宽时可以横向滚动。`Save PNG` 不直接发送实体打印任务，而是逐张采集卡片并写入浅米色背景的完整长图，保存在 `BepInEx\RecipePreviewExports`，可再分享或从图片查看器打印。PNG 写入器不依赖显卡最大纹理高度；保存后会自动把全图或适度缩小的全图复制到 Windows 剪贴板。若剪贴板被占用，PNG 文件仍会保留。
