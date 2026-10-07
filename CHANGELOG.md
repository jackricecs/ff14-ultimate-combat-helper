# 更新日志

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
