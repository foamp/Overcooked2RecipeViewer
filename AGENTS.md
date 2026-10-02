# Project

这是 Overcooked! 2 的 BepInEx Mod：Overcooked2RecipePreview。

游戏安装目录：

`E:\game\steam\steamapps\common\Overcooked! 2`

测试插件目录：

`E:\game\steam\steamapps\common\Overcooked! 2\BepInEx\plugins\zhimo`

# Development

- 修改前先阅读现有实现。
- 优先做小范围增量修改。
- 不要为了小功能无意义地大规模重构。
- 每次修改后必须自行编译。
- 编译错误应自行定位并修复。
- 对游戏内部机制存在疑问时，优先检查游戏程序集、反编译代码、日志或增加 Debug 输出，不要凭猜测实现。
- 不要破坏已经验证正常的现有功能。

# Runtime testing

游戏实机测试由用户负责。

不要花时间尝试启动、控制或游玩 Overcooked! 2。

需要实机测试时，应明确告诉用户：

1. 测试目的
2. 具体操作步骤
3. 预期结果
4. 需要重点观察什么
5. 失败时需要提供哪些日志、截图或现象

收到用户实机测试结果之后再继续修改。

# Game directory safety

默认情况下，不要修改、删除或覆盖游戏安装目录中的文件。

只有用户明确要求“部署测试版”“安装测试 DLL”或表达同等意思时，才允许部署当前 Mod DLL。

部署只能影响：

`E:\game\steam\steamapps\common\Overcooked! 2\BepInEx\plugins\zhimo`

中的当前 Mod DLL。

不得修改或删除其他 Mod 或游戏文件。

# Completion

每完成一个可测试阶段，汇报：

- 做了什么
- 修改了哪些关键文件
- 是否编译成功
- 已自动验证什么
- 还需要用户实机测试什么
