# 02 副本难度过滤 & 主要敌人识别（v0.2 已实现）

## 副本难度
- `01 ChangeZone` 给出区域 ID → `ZoneTable` 查分类：Dungeon / Trial / Extreme / Unreal / Raid / Savage / Ultimate / Chaotic / Criterion / CriterionSavage / Other。
- 表由 `tools/gen_zone_table.py` 从 cactbot `resources/zone_info.ts` 生成（contentType + 英文名中的 `(Savage)`/`(Extreme)`/`(Unreal)` 判断）。**新副本上线后需重新生成并发版**，否则新副本会被当作 Other。
- 每个模块有自己的 `Tiers` 白名单；「通用 → 测试模式」忽略全部难度过滤。

> 后续可选：OverlayPlugin 的 `265 ContentFinderSettings` 行能补充"是否在任务搜索器内、解限"等信息，可用于区分"解限低难度老本"。

## 主要敌人（Major）
需求：奶人切目标、双 Boss、Boss 战中刷小怪时，只关心 Boss 身上的 DoT。

做法：`CombatContext` 维护本区域所有 NPC 的最大 HP，来源包括：
- `03 AddCombatant`（下标 12）
- `21/22` 技能命中的目标 maxHp（下标 25）。因此即使你没给 Boss 挂 DoT，只要有人打了它，它的 HP 就会被记录。
- `26` 状态目标 maxHp（下标 10）

判定：`maxHp ≥ 比例 × 场上最高 maxHp`（默认 0.5）→ 主要敌人。死亡/消失的敌人移出统计，所以 Boss 死后剩下的敌人会成为"主要"。HP 未知时视为主要（宁可多报）。

| 场景 | 结果 |
|---|---|
| 单 Boss + 小怪 | 只报 Boss |
| 双 Boss（HP 相近） | 都报，同时到期合成一句 |
| 治疗切到队友身上 | 不受影响（不依赖当前目标） |
| 小怪阶段（无 Boss） | 小怪成为最高者 → 报 |
| Boss 有多个 HP 很高的"假体/机关" | 可能误判；调高比例，或后续加"按 BNpcNameID 排除"配置 |

其他模式：`Current`（仅当前目标，需 SDK）、`All`（全部）。
