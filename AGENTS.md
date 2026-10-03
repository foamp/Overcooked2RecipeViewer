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

## Deployment

凡是修改了会影响 Mod 运行的源码，最终编译成功后默认自动执行 deploy.ps1。

以下情况不部署：
- 用户明确要求不要部署；
- 只修改文档、Git 配置或项目规则；
- 只是调查、分析或反编译；
- 没有实际源码变化；
- 编译失败。

不要自动启动游戏。

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
