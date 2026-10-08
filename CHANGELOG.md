# 更新日志

## v0.3.0 — 2026-10-08
- 新增：界面显示**当前职业**与**本职业 DoT 剩余时间**（总览页与 DoT 页顶部；职业没有可监控的 DoT 时显示「暂缺」），DoT 列表中高亮当前职业的行。职业来源：解析插件 SDK → 03/12 日志行。
- 新增「诊断与日志」页：日志详细程度（信息/调试/追踪）、记录战斗日志行（默认只记与自己相关的）、每次脱战自动保存战斗记录、导出诊断包（单个 txt，默认把玩家名替换为 `<我>`/`玩家N`）。日志目录 `%APPDATA%\Advanced Combat Tracker\UltimateCombatHelper\logs`。
- 新增：DoT 决策日志——每次挂上/刷新/消失、每个**未播报的原因**（不在战斗、副本难度、非主要敌人、非当前目标、去重冷却）、你挂上但不在监控列表的状态 ID、还不知道角色 ID 等。
- 新增：战斗摘要（区域、职业、DoT 挂上/播报/未播报次数及原因，ACT 遭遇名与队伍 DPS）、战斗中每 30 秒心跳、日志行格式自检（detectedType 与行首类型是否一致、originalLogLine 是否为空，每种类型留 2 条样本）。
- 修复（可能导致「听不到 DoT 提醒」）：日志行改为按 `originalLogLine` 行首类型号过滤，不再依赖 ACT 的 `detectedType`；TTS 改为在 ACT 界面线程调用，与「试听」走同一路径。
- 测试：93 项。

## v0.2.0 — 2026-10-07
- 由 DotWatch 重构为 FF14 Ultimate Combat Helper：`UCH.Core`（纯逻辑，可测试）+ `UCH.Plugin`（ACT 适配），模块化 `IModule`。
- 新增：按副本难度启用（默认团本与高难），内置 424 个副本的难度分类（来自 cactbot 数据）；测试模式。
- 新增：DoT 目标模式「主要敌人」（默认）/「仅当前目标」/「所有目标」；主要敌人按最大 HP 比例判定，支持切目标奶人、双 Boss、忽略小怪。
- 配置改为 JSON（`UltimateCombatHelper.json`），缺失字段自动补默认值，损坏时自动备份并回退。
- 新界面：总览（角色/副本/敌人 HP）、DoT 监控（表格编辑 DoT 列表、实时列表）、通用。
- 构建：Windows `dotnet build`；Linux `tools/build-linux.sh`（.NET Framework 参考程序集编译 + API 兼容审计）；GitHub Actions 自动构建与发布。
- 测试：41 项核心逻辑测试。

## v0.1.1（DotWatch 1.0.1）
- 修复：Mono 编译导致在 ACT 中加载时报 `MissingMethodException`（`String.Split(char, StringSplitOptions)`）。

## v0.1.0（DotWatch 1.0.0）
- DoT 剩余 N 秒语音提醒。
