# 01 DoT 到期提醒（v0.2 已实现）

## 行为
- 记录**自己**施加的、列表中的 DoT：`26 StatusAdd` 且 `sourceId == 我`。到期时间 = 收到时刻 + `duration`。
- 补 DoT 会再来一条 26 行 → 刷新到期时间并重置"已提醒"。
- 清除：`30 StatusRemove`（同来源/目标/状态）、`25 Death`/`04 RemoveCombatant`（目标）、`01 ChangeZone`、`02` 换角色、过期 3 秒兜底。
- 每 100ms 检查：剩余 ≤ 阈值且未提醒 → 进入过滤（见 02）→ 同一批合成一句话 → 节流后 TTS。
- 挂上时持续时间本来就 ≤ 阈值：不提醒（避免一挂上就报）。

## 诊断（v0.3）
- 每个进入提醒区间的 DoT 只判定一次；不播报时写一条 `dot.suppress` 日志并带原因代码：`dot-off` / `not-in-combat` / `tier` / `not-major` / `not-target` / `cooldown`。战斗摘要按原因计数。
- 自己挂在敌人身上、但不在监控列表里的有时限状态，每个 ID 记一次 `dot.unknown`，用于发现 DoT ID 变更。
- 收到列表内 DoT 但角色 ID 未知时记 `no-id`（15 秒一次）。
- 界面顶部「当前职业 / DoT」：职业来自 SDK `Combatant.Job` → `03` 行 job（十六进制，下标 4）→ `12` 行 job（十进制，下标 2）；基础职业按特职查 DoT（咒术师 → 黑魔法师）。

## 角色 ID 获取顺序
解析插件 SDK `GetCurrentPlayerID()` → `02` 行 → 用 `ActGlobals.charName` 匹配自己技能/状态行的来源名学习（ACT 晚于登录启动时）。

## DoT 清单来源
FFXIV_ACT_Plugin 仓库 `Definitions/*.json` 中 `timeproc.type == "dot"` 的条目。新版本新增 DoT 时据此更新 `DotConfig.DefaultDots()`（已有用户配置不会被覆盖，可在界面"监控的 DoT"中手动添加）。

## 已知限制
- 计时与游戏内显示有 0.1–0.3 秒误差，对"提前数秒提醒"无影响。
- 若未来出现"延长 DoT 但不产生 26 行"的技能，计时会偏短（只会提前报）。可改为结合 `38 StatusEffects` 校正。
