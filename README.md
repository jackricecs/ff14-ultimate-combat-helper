# FF14 Ultimate Combat Helper

最终幻想14（国服 / 国际服）的 ACT 战斗辅助插件。目标是帮你在团本与高难副本里少犯"打法上的错"：DoT 断了、资源溢出了、爆发没对齐、机制站错了位。

> **只读原则**：插件只读取 FFXIV 解析插件产生的战斗日志，用 ACT 的 TTS / 界面提醒你。它**不向游戏写入任何数据**，也不代替你操作。详见 [设计原则](docs/architecture.md#设计原则)。

## 功能

| # | 功能 | 状态 |
|---|---|---|
| 1 | **DoT 到期语音提醒**：剩 N 秒时播报"高闪雷快掉了，该补了" | ✅ v0.2 |
| 2 | **按副本难度启用**：默认只在团本、零式、绝、极神等高难副本提醒，打木人可开测试模式 | ✅ v0.2 |
| 3 | **主要敌人识别**：按最大 HP 判断 Boss；切目标奶人、双 Boss、小怪混战时仍只盯 Boss 的 DoT | ✅ v0.2 |
| — | **当前职业 / DoT 剩余时间**显示；**诊断与日志**：记录每次未播报的原因，一键导出诊断包 | ✅ v0.3 |
| 4 | 资源 / 充能溢出监控与战后报告 | 🧭 设计中 → [文档](docs/features/03-resource-overcap.md) |
| 5 | 顶尖玩家手法时间轴：从 FFLogs 学习，AI 辅助整理，按战斗时间点提示 | 🧭 设计中 → [文档](docs/features/04-fflogs-timeline.md) |
| 6 | 机制站位提示：按你的职能（MT/ST/H1/H2/D1–D4）播报和显示安全位置 | 🧭 设计中 → [文档](docs/features/05-mechanic-positions.md) |

完整计划见 [ROADMAP](ROADMAP.md)。

## 安装

1. 先装好 ACT 与 FFXIV 解析插件。国服用 [FFXIV_ACT_Plugin_CN](https://github.com/TundraWork/FFXIV_ACT_Plugin_CN)，国际服用原版解析插件。
2. 从 [Releases](https://github.com/jackricecs/ff14-ultimate-combat-helper/releases) 下载对应版本：国服下载 `-cn.zip`，国际服下载 `-global.zip`。解压到任意目录，例如 `ACT\Plugins\UltimateCombatHelper\`。
3. 在 ACT「插件」页选择 `UltimateCombatHelper.dll`，点「添加/启用插件」，会出现「战斗辅助」标签页。
4. 在 ACT「选项 → 声音设置」中把 TTS 设为中文语音（如 Microsoft Huihui / Xiaoxiao）。
5. 在「DoT 监控」页点「试听」，确认能听到语音。

> 国服与国际服的两个 DLL 代码相同，只是引用的解析插件 SDK 不同（国服 SDK 没有强命名）。如果用错了，DoT 提醒仍然可用，只有「仅当前目标」模式会降级。

**测试**：在主城或木人处默认不会提醒。打木人测试时，先到「通用」页勾选**测试模式**。

**没有提醒？** 总览页的日志会写明每次没播报的原因（例如"不在战斗中""副本难度不在提醒列表""不是主要敌人"）。还解决不了就到「诊断与日志」页点**导出诊断包**，把生成的 txt 发到 Issue 里（默认已把玩家名替换为 `<我>`/`玩家N`）。

## 配置

配置文件路径为 `%APPDATA%\Advanced Combat Tracker\Config\UltimateCombatHelper.json`。界面上的修改会写回这个文件。手动编辑后，在「总览」页点「重新加载配置」生效。

DoT 监控的主要选项：

| 选项 | 说明 |
|---|---|
| 剩余 N 秒时提醒 | 全局值；每个 DoT 也可以单独设置 |
| 语音模板 | 可用 `{dot}`、`{target}`、`{sec}` |
| 目标 | **主要敌人**（默认）/ 仅当前目标 / 所有目标 |
| 主要敌人 HP 阈值 | 默认 0.5：敌人最大 HP ≥ 场上最高者的 50% 才算主要敌人。双 Boss 血量相近时都会算上，小怪不算 |
| 在这些副本提醒 | 默认勾选普通团本、零式、绝、极神、幻巧、诛灭、零式异闻 |
| 监控的 DoT | 已内置白魔、学者、占星、贤者、黑魔、诗人、武士、龙骑的 DoT，可增删 |

## 开发

- 架构与模块化说明：[docs/architecture.md](docs/architecture.md)
- 构建、测试、发布：[docs/development.md](docs/development.md)
- 贡献指南：[CONTRIBUTING.md](CONTRIBUTING.md)

快速构建：

```powershell
# Windows
./tools/fetch-deps.ps1
dotnet build src/UCH.Plugin -c Release -p:SdkVariant=cn
```

```bash
# Linux（Mono，含 .NET Framework API 兼容性审计）
bash tools/build-linux.sh
```

## 致谢

- [cactbot](https://github.com/OverlayPlugin/cactbot)：日志行格式文档与副本数据（Apache-2.0）
- [OverlayPlugin](https://github.com/OverlayPlugin/OverlayPlugin)：解析插件 SDK 的对接方式
- [FFXIV_ACT_Plugin](https://github.com/ravahn/FFXIV_ACT_Plugin) / [FFXIV_ACT_Plugin_CN](https://github.com/TundraWork/FFXIV_ACT_Plugin_CN)

## 免责声明

Square Enix 的用户协议原则上不允许使用第三方工具，使用本插件的风险由你自行承担。请不要在游戏内公开讨论插件或输出数据，也不要用插件信息指责或要求队友。

本项目与 Square Enix、盛趣游戏及 FFLogs 均无关联。FINAL FANTASY 是 Square Enix 的注册商标。
