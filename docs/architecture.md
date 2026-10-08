# 架构

## 设计原则

1. **只读**：只消费 FFXIV 解析插件产生的日志行与 SDK 查询结果，不读写游戏内存、不注入、不发送游戏指令。这条边界决定了哪些功能做、怎么做（例如机制站位只"提示"不"标点"，见 [功能 05](features/05-mechanic-positions.md)）。
2. **逻辑与 ACT 解耦**：所有业务逻辑在 `UCH.Core`，不引用 ACT / WinForms，可在 Linux/Mono 下单元测试；`UCH.Plugin` 只做适配（日志进、语音出、界面）。
3. **模块化**：每个功能是一个 `IModule`，共享 `CombatContext`；新增功能不改动已有模块。
4. **降级而非崩溃**：解析插件 SDK 缺失或版本不符、日志字段不全、模块抛异常，都只影响对应功能并写日志。
5. **离线重活、在线轻活**：FFLogs 抓取、AI 分析等耗时/联网任务放在独立的离线工具里，产物是数据文件；战斗中插件只读文件、按日志对时。

## 数据流

```
FFXIV 客户端 ─► FFXIV_ACT_Plugin(_CN) ─► ACT OnLogLineRead(originalLogLine: "26|ts|...")
                                              │
                                     UchPlugin（按行首类型号预过滤，并交给 LineRecorder 记录）
                                              │ LogLine.Parse
                                              ▼
                                       ModuleManager.Dispatch
                                       ├─► CombatContext.OnLogLine   我是谁 / 副本难度 / 敌人最大HP
                                       └─► 各 IModule.OnLogLine       DotModule …（后续模块）
                       100ms 定时器 ─► ModuleManager.Tick ─► IModule.OnTick ─► ThrottledNotifier ─► ACT TTS
```

## 目录

```
src/UCH.Core/                 纯逻辑（net48，无 ACT 依赖）
  Logs/LogLine.cs             网络日志行解析、LogType 常量、Hex 规范化
  Context/CombatContext.cs    共享战斗上下文
  Data/ZoneTable(.g).cs       副本 → 难度分类（tools/gen_zone_table.py 从 cactbot 生成）
  Config/UchConfig.cs         JSON 配置（DataContractJsonSerializer，向后兼容默认值）
  Notify/Notifier.cs          提醒接口与去重节流
  Modules/IModule.cs          模块接口与宿主接口
  Modules/ModuleManager.cs    分发、定时、异常隔离
  Modules/Dot/                功能：DoT 监控
  Diagnostics/                运行日志、战斗日志行记录、战斗摘要、诊断包导出与脱敏
src/UCH.Plugin/               ACT 插件（编译时把 Core 源码一并编入，输出单个 DLL）
  UchPlugin.cs                IActPluginV1 入口
  UchPlugin.Diagnostics.cs    诊断包里与 ACT 相关的部分（环境、状态、遭遇信息）
  FfxivBridge.cs              解析插件 SDK 安全对接（反射 + NoInlining + 重试）
  UI/MainPanel.cs             设置界面（UI/DiagnosticsTab.cs 诊断页，UI/UiKit.cs 控件小工具）
tests/UCH.Core.Tests/         无框架控制台测试（Windows / Mono 通用）
tools/                        依赖下载、Linux 构建、API 审计、数据生成
docs/                         设计文档
```

## 关键约定

| 主题 | 约定 |
|---|---|
| 日志字段 | 一律用网络日志行（`originalLogLine` 按 `|` 拆分），下标见 cactbot `docs/LogGuide.md`。在 `LogLine` 上用 `Id(i)`/`Long(i)`/`Double(i)` 安全取值，越界返回空/0 |
| ID | 统一用 `Hex.Norm`（大写、去前导 0）。玩家 `10xxxxxx`，NPC `40xxxxxx`，无目标 `E0000000` |
| 线程 | `OnLogLine` 在 ACT 日志线程、`OnTick` 在计时器线程，模块内部自行加锁；界面只读快照 |
| 时间 | 用"收到日志的本机时间"计时（`IModuleHost.Clock`），测试时注入假时钟 |
| 配置 | 每个模块一个 `[DataContract]` 配置节；默认值写在 `[OnDeserializing]`/`CreateDefault()`，新增字段必须有默认值以兼容旧文件 |
| .NET API | 目标 .NET Framework 4.7/4.8。Linux 构建必须走 `tools/build_netfx.sh`（参考程序集 + 审计），源码用 `Split(char[])`、`TrimStart(char[])` 等 Framework 存在的重载 |

## 新增一个模块

1. 在 `src/UCH.Core/Modules/<Name>/` 实现 `IModule`，配置类加 `[DataContract]`。
2. 在 `UchConfig` 加一个 `[DataMember]` 配置节，并在 `OnDeserialized` 里补默认值。
3. 若需要新的日志类型，加到 `ModuleManager.InterestingTypes`。
4. 在 `UchPlugin.InitPlugin` 里 `manager.Add(new XxxModule(...))`，在 `ApplyConfig` 里下发配置。
5. 在 `MainPanel` 加一个设置页。
6. 在 `tests/UCH.Core.Tests` 加用例（用 `Rig` 喂日志行 + 假时钟），`tools/build-linux.sh` 全绿后提 PR。
