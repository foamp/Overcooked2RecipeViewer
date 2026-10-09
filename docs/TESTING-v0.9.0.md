# v0.9.0 验收 / Acceptance

开发阶段未启动、控制或游玩游戏。下列实机项目均由用户验收；自动化结果不能代替它们。

安装前关闭游戏，备份当前 Viewer DLL、`BepInEx\config\io.github.overcooked2.recipepreview.cfg` 和 `%APPDATA%\Overcooked2RecipePreview\layouts.txt`。按原安装目录替换测试 DLL，确保同 GUID 只有一份。现有 `deploy.ps1 -WhatIf` 可检查安装路径。

| 项目 | 操作与期望结果 |
| --- | --- |
| 1 普通官方关卡 | Insert → 导出图片；默认标准排版保留查看器的分行与顺序，透明外围空白缩小，预览标题尺寸与 PNG 属性一致，无缺图或阴影裁切。两处导出按钮均显示「导出图片」；设置区没有「当前布局」和底部固定 1x 说明。 |
| 2 大量菜谱自制地图 | 提供标准数据的地图，查看长图最下方并导出；每张菜谱存在，尾行完整，游戏仍响应。过大图片明确拒绝，可尝试紧凑排版减小间距。 |
| 3 五种背景 | 同一布局分别使用默认、透明、白、深色、自定义 RGB；预览与 PNG 背景一致，背景覆盖全部边距。查看器背景不变。 |
| 4 透明边缘 | 透明 PNG 在支持 Alpha 的编辑器中置于红、黑、白底上；空白 Alpha=0，纸张/阴影边缘平滑，无棋盘格、黑底或白色晕边。 |
| 5 固定 1x / 手动分行 | 导出界面没有倍率选项。手动把卡片分成若干短行，保存布局；依次选择紧凑、标准、宽松，三者的行数、每行卡片数量及顺序必须一致。只调整间距、行距并左对齐，不合并短行、不拆分宽行、无重叠，不改变卡片尺寸。关闭预览并重开、恢复已保存布局后仍正确。捕获后才显示准确分辨率。 |
| 6 四种边距 | 固定排版，选择无、紧凑、标准、宽松；每侧分别为 0/24/64/128 像素，围绕实际可见内容计算，浅阴影、装饰、食材标签完整。 |
| 7 同步刷新/细节 | 快速切换背景、拖动 RGB 滑条、改变排版/边距；显示更新状态，完成前禁用输出；切换排版不重新捕获卡片；100% / + / Ctrl+滚轮放大、滚动，停止后细节刷新，透明边缘不重复叠加。 |
| 8 剪贴板 | 复制后分别粘贴到支持 PNG/Alpha 的编辑器和普通画图工具；方向/内容正确。大图显示缩小尺寸，PNG 尺寸不受影响。旧软件使用白底兼容副本。 |
| 9 关闭/重复打开 | 在初次捕获、缩略图更新、PNG 写入中分别用取消、X、Esc、Insert 关闭；再开能正常导出；原排列、排序、滚动、缩放不变，无半成品 `.png.tmp`。 |
| 10 重启记忆 / 配置管理器 | 选自定义颜色、紧凑排版、宽松边距后关闭并退出；重启预览设置保留。BepInEx 配置管理器中没有图示 Image export 设置区；本 MOD 的快捷键仍能修改，其他 MOD 不受影响。 |
| 11 旧存档兼容 | 使用 v0.8.3 配置和已保存布局；快捷键、草稿、命名布局能读取/切换/删除，原布局身份与文件头不变。已有候选版 Scale=2 配置仍能读取颜色和边距，导出固定 1x，保存后兼容键为 1；Layout=Current 自动读取并保存为 Standard。 |
| 12 分辨率/语言 | 1280×720、1920×1080、2560×1440；窗口、全屏与窗口尺寸切换，中/英语言；预览操作可见，滚动正常，改变窗口不会改变输出像素尺寸。 |
| 13 原功能回归 | 原版/动态阶段/脚本关卡及标准自制图；四种排序、卡片和整行拖拽、保存/恢复布局、横纵滚动和查看器缩放均正常；订单生成、出现顺序、计时、得分无变化。 |
| 14 生命周期 | 预览或保存期间切换关卡/退出；旧预览关闭，新关卡可打开；临时目录、Camera、Canvas、纹理没有持续累积（进程崩溃可能留下临时目录）。 |
| 15 连续界面缩放 | 工具栏 / 预览右上角 → 界面设置；滑条从 50% 到 200%，输入 137%。按钮、文字、面板、滚动条和行把手同步调整，工具栏随宽度换行；关闭、导出、恢复默认始终可用。菜谱缩放百分比、手动分行、已保存布局和 PNG 尺寸不变。在行拖动中不可打开设置；设置打开时不能拖动或滚动底层菜谱。 |
| 16 配色 / 记忆 | 切换厨房绿、奶油白、深海蓝、炭灰；工具栏、查看器面板、行把手与预览窗口同步更新，正文/选中按钮可读。保持同一导出背景导出 PNG，其颜色不因主题变化。关闭重开、换关卡、重启后保留 137% / 配色；恢复默认回到 100% / 厨房绿，Esc / Insert 只关闭设置。配置管理器不出现新增 Interface 设置区。 |

记录关卡/玩家数、背景/图片排版/边距、预览与 PNG 尺寸、导出时间和错误日志。失败时提供复现步骤、PNG 与截图，以及 `BepInEx\LogOutput.log` 相关片段。发布检查中的自动化结果不能代替上述实机项目的确认。

## Automated checks

```powershell
.\build.ps1 -GameDir '<game-directory>'
.\tests\run-tests.ps1 -GameDir '<game-directory>' -PythonPath '<Python-with-Pillow>'
```

Checks cover alpha recovery/composition, all background/layout/margin combinations,
preview/detail-to-PNG pixel parity, independent PNG CRC/zlib decoding, 20,000 px
long-image completion, safety bounds, partial-file cleanup, clipboard DIB payloads,
alpha crop with faint shadows, explicit manual-row preservation (including
34 cards in 9 rows, singleton rows, wide rows and unequal crop offsets), spacing/non-overlap, old layout
round-trip and real BepInEx configuration persistence/visibility tags. Unity camera
rendering is not tested. Interface checks cover continuous scaling, responsive
control bounds/non-overlap across seven resolutions, palette contrast,
preference persistence and legacy Current-to-Standard migration. Actual Unity UI
rendering and actual clipboard API/paste calls are deliberately excluded.

## English runtime checklist

With the game closed, back up the installed DLL, existing config and layouts;
replace only the Viewer DLL and keep a single plugin GUID installation. This
agent never starts or controls the game; installation is separate from runtime acceptance.

Follow rows 1–16 above: official level; large standard-data custom map and final
row; all five backgrounds; transparent edges over red/black/white; fixed native
1x and all three image layouts with identical manual row groups and order;
all four margins; rapid settings updates plus
zoomed detail; clipboard paste in modern/legacy apps; cancellation during each
stage and reopening; persistence after restart and hidden Configuration Manager
export entries (shortcut stays visible); v0.8.3 config/layout compatibility and
legacy Scale=2 migration;
720p/1080p/1440p, window/fullscreen and Chinese/English; sorting/dragging/saved-layout/
scroll/zoom regression; scene/quit cleanup; live 50%–200% scaling/percentage entry;
four themes, reset and restart persistence. Window resizing must preserve PNG
resolution. Actual game order behavior must remain unchanged.

Report level/player count, settings, preview/PNG dimensions, time, screenshots,
PNG and relevant BepInEx logs. Publish only after runtime acceptance and explicit
approval; no automatic publishing occurs.
