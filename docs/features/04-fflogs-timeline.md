# 04 顶尖玩家手法时间轴（规划中，v0.4–v0.5）

## 目标
对某个副本 + 某个职业，从 FFLogs 上的顶尖记录中提炼"在第几秒 / 哪个机制前后打什么"的**参考时间轴**，打本时在对应时间点提前提示（如"10 秒后团辅窗口""转场前留好充能"）。

## 关键决策
1. **用官方 API，不爬网页**。FFLogs 提供 GraphQL API v2：在 FFLogs 网站创建 API Client，用 client credentials 换 token，按"点数/小时"限流。国服记录在 cn.fflogs.com，接口相同、数据集不同。爬取 HTML 既不稳定也可能违反站点条款。
2. **离线工具，不在 ACT 里联网**。新建 `tools/uch-planner/`（Python 或 .NET 控制台）：抓数据 → 统计对齐 → （可选）AI 整理 → 输出 `timelines/<zoneId>-<job>.json`。插件只读这个文件。
3. **时间计算靠统计，AI 只做"整理和解释"**。不同记录的击杀时间、转场时间不同，不能直接平均绝对时间：
   - 用 Boss 的关键读条（`20`/`21` 中的 Boss 技能 ID）把每条记录切成阶段，在阶段内对齐（与 cactbot timeline 的 sync 思路相同）。
   - 对每个阶段，统计 Top N 玩家关键技能（团辅、爆发药、大招、充能技能）的相对时间分布，取中位数和一致性（例如 80% 的记录在 ±2 秒内释放）。
   - 只有一致性高的动作才进入时间轴。
   - AI（Qwen 等）的作用：把统计结果转成人能读的提示文本，归纳"为什么这么打"，标出分歧点。**AI 不负责算时间**，以免编造。
4. **模型可配置**：OpenAI 兼容接口（`base_url` + `model` + `api_key`）。通义千问可用 DashScope 的兼容模式端点。密钥只放在本机环境变量或 `*.secrets.json`（已在 .gitignore 中）。

## 运行时（插件侧）
- `TimelineModule : IModule`：进入 `zoneId` 时加载对应时间轴；战斗开始计时；遇到时间轴中定义的 sync 技能（Boss 读条/技能 ID）时校正当前位置；在 `time - 提前量` 播报提示。
- 时间轴格式参考 cactbot 的 timeline（`时间 "名称" sync /正则/ window 前,后`），以便复用 cactbot 已有的 Boss 时间轴作为"骨架"，只叠加职业手法。

## 输出文件草案
```json
{
  "zoneId": 1321, "job": "BLM", "source": "fflogs top 50, 2026-10", "phases": [
    { "name": "P1", "sync": { "abilityId": "A3DA" }, "cues": [
      { "t": 7.5, "lead": 3, "text": "黄雷准备", "confidence": 0.86 }
    ]}
  ]
}
```

## 分阶段
- v0.4：手写 / 导入 cactbot 时间轴 + 自己编辑提示（不依赖 FFLogs），先把运行时做稳。
- v0.5：`uch-planner` 抓取 FFLogs + 统计对齐 + AI 生成提示文本，人工审核后导入。

## 需要你准备
- FFLogs API Client（client id / secret）
- 想支持的副本与职业清单
- 模型服务的 API Key
