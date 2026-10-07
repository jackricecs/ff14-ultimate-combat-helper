# 贡献指南

欢迎 Issue 与 PR。提交前请确认：

1. **只读原则**：不接受读写游戏内存、注入、向游戏发送指令的功能（包括自动标点、自动按键）。
2. **逻辑放在 `UCH.Core`**，并在 `tests/UCH.Core.Tests` 补测试；`tools/build-linux.sh`（或 Windows CI）全绿。
3. **.NET Framework 兼容**：目标 4.7/4.8；不要使用 .NET Core 才有的 API 重载（如 `Split(char)`、`TrimStart(char)`、`string.Contains(char)`）。Linux 上请用 `tools/build_netfx.sh`，它会自动审计。
4. **数据来源注明许可**：引用 cactbot 等项目的数据请保留出处与许可说明。
5. **不要提交** `thirdparty/`（ACT 与解析插件为闭源软件）、任何 API Key。

## 提交 Bug 时请附上
- 插件版本、国服/国际服、解析插件版本
- 「总览」页日志截图
- 如可能：ACT 日志中相关的几行（`%APPDATA%\Advanced Combat Tracker\FFXIVLogs\Network_*.log`，可以删去角色名）
