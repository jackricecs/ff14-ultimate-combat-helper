// 职业识别、DoT 状态显示、诊断日志/记录/导出相关测试。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateCombatHelper.Config;
using UltimateCombatHelper.Data;
using UltimateCombatHelper.Diagnostics;
using UltimateCombatHelper.Logs;
using UltimateCombatHelper.Modules.Dot;

namespace UltimateCombatHelper.Tests
{
    internal static partial class Program
    {
        static void JobTests()
        {
            Ok(Jobs.NameCn(25) == "黑魔法师" && Jobs.Abbreviation(25) == "BLM" && Jobs.NameCn(42) == "绘灵法师", "职业表");
            Ok(Jobs.ToAdvanced(7) == 25 && Jobs.ToAdvanced(25) == 25, "咒术师按黑魔法师算");
            Ok(Jobs.NameCn(Jobs.Unknown) == "" && Jobs.NameCn(60) == "职业#60", "未知/新职业");

            var r = new Rig(null, true);
            Ok(r.M.Context.MyJob == Jobs.Unknown, "初始职业未知");
            r.Feed("12|ts|25|100|200|300|400|500|600|h");
            Ok(r.M.Context.MyJob == 25 && r.M.Context.MyJobSource == "12行", "12 行识别职业");
            r.Feed("03|ts|" + Me + "|Me|18|64|0000|00||0|0|1000|1000|10000|10000|||0|0|0|0|h");   // 0x18 = 24 白魔
            Ok(r.M.Context.MyJob == 24, "03 行（十六进制）识别职业");
            r.Feed("03|ts|10FF0099|Other|19|64|0000|00||0|0|1000|1000|10000|10000|||0|0|0|0|h");
            Ok(r.M.Context.MyJob == 24, "他人的 03 行不影响");
            var changes = new List<int>();
            r.M.Context.JobChanged += (j, src) => changes.Add(j);
            r.M.Context.SetMyJob(25, "SDK"); r.M.Context.SetMyJob(25, "SDK");
            Ok(changes.Count == 1 && changes[0] == 25, "职业变化只通知一次");
            r.M.Context.SetMyJob(5000, "坏数据");
            Ok(r.M.Context.MyJob == 25, "不合理的职业 ID 忽略");
            r.Feed("02|ts|10FF0002|Alt|h");
            Ok(r.M.Context.MyJob == Jobs.Unknown, "切换角色后职业重置");

            // 界面状态行
            var cfg = DotConfig.CreateDefault();
            var v = DotStatus.Describe(19, cfg, null, now);
            Ok(v.Dot.StartsWith("暂缺") && v.Job == "骑士(PLD)", "骑士 DoT 默认未启用 → 暂缺：" + v.Dot);
            Ok(DotStatus.Describe(20, cfg, null, now).Dot.StartsWith("暂缺（本职业没有"), "武僧没有 DoT → 暂缺");
            Ok(DotStatus.Describe(25, cfg, null, now).Dot.StartsWith("未挂"), "黑魔未挂 DoT");
            Ok(DotStatus.Describe(7, cfg, null, now).Dot.StartsWith("未挂"), "咒术师按黑魔显示");
            Ok(DotStatus.Describe(Jobs.Unknown, cfg, null, now).Job.StartsWith("识别中"), "职业未知");
            var e = new DotEntry { DisplayName = "高闪雷", TargetName = "木人", ExpiresAt = now.AddSeconds(23.4), LeadSeconds = 4 };
            v = DotStatus.Describe(25, cfg, new[] { e }, now);
            Ok(v.Dot == "高闪雷 → 木人 剩 23.4 秒" && !v.Urgent, "显示剩余时间：" + v.Dot);
            e.ExpiresAt = now.AddSeconds(3);
            Ok(DotStatus.Describe(25, cfg, new[] { e }, now).Urgent, "进入提醒区间标红");
        }

        static void DiagnosticsTests()
        {
            Ok(LogLine.PeekType("26|ts|x") == 26 && LogLine.PeekType("00|ts") == 0 && LogLine.PeekType("260|x") == 260, "PeekType");
            Ok(LogLine.PeekType("") == -1 && LogLine.PeekType("[12:00] 1A:x") == -1 && LogLine.PeekType("26") == -1, "PeekType 异常输入");

            var log = new DiagLog(() => now, 100) { Level = DiagLevel.Info };
            int emitted = 0, written = 0;
            log.Emitted += x => emitted++; log.Written += x => written++;
            log.Info("a", "info"); log.Debug("a", "debug"); log.Error("a", "err");
            Ok(emitted == 3 && written == 2 && log.Count == 2, "级别过滤：Debug 不保存但仍计数");
            for (int i = 0; i < 300; i++) log.Info("a", "x" + i);
            Ok(log.Count == 100 && log.Snapshot().Last().Message == "x299", "环形缓冲只保留最近的条目");
            Ok(DiagLog.ParseLevel("trace") == DiagLevel.Trace && DiagLog.ParseLevel("乱写") == DiagLevel.Debug && DiagLog.ParseLevel("99") == DiagLevel.Debug, "级别解析");

            // 行记录：默认只记录与自己相关的技能/状态行
            var rec = new LineRecorder();
            Action<string> offer = raw => { var l = L(raw); rec.Observe(l.Type, LogLine.PeekType(raw), "[x] " + raw, raw); rec.Offer(l, raw, Me); };
            offer(Add("F1F", "高闪雷", 30, Me, "40001234", "木人"));
            offer(Add("F1F", "高闪雷", 30, "10FF0099", "40001234", "木人"));
            offer(Add("31", "某buff", 30, "10FF0099", Me, "Me"));
            offer(Zone(129));
            offer("00|ts|0039|聊天|私聊内容|h");
            Ok(rec.LineCount == 3, "只记录与我相关的行 + 区域行：" + rec.LineCount);
            Ok(rec.Total == 5 && rec.CountOf(26) == 3, "所有行都计数");
            Ok(rec.Samples().All(x => x.PrefixType != 0), "聊天行不采样");
            rec.Observe(26, 26, "", null);
            Ok(rec.EmptyOriginal == 1, "统计 originalLogLine 为空");
            rec.Observe(99, 26, "x", "26|x");
            Ok(rec.Mismatches().Count == 1 && rec.Mismatches()["99→26"] == 1, "记录 detectedType 与行首类型不一致");
            rec.RecordOthers = true;
            offer(Add("F1F", "高闪雷", 30, "10FF0099", "40001234", "木人"));
            Ok(rec.LineCount == 4, "开启后记录他人行");
            rec.Capacity = 1000;
            for (int i = 0; i < 1500; i++) offer(Zone(129));
            Ok(rec.LineCount == 1000, "行缓冲有上限");

            // 脱敏
            var mk = new NameMasker("Me", Me);
            mk.Learn(L(Add("31", "某buff", 30, "10FF0099", Me, "Me")));
            mk.Learn(L("21|ts|10FF0099|队友甲|1|技能|40001234|木人|h"));
            string masked = mk.Mask("队友甲 给 Me 加了 buff，打 木人");
            Ok(masked == "玩家1 给 <我> 加了 buff，打 木人", "玩家名脱敏、NPC 保留：" + masked);

            // 战斗统计
            var fl = new DiagLog(() => now);
            var ft = new FightTracker(fl);
            fl.Info(FightTracker.DotWarn, "不在战斗中不计");
            var f = ft.Begin(now, "木人", "其他", "黑魔法师");
            fl.Debug(FightTracker.DotApply, "x"); fl.Debug(FightTracker.DotApply, "x");
            fl.Info(FightTracker.DotWarn, "x");
            fl.Info(FightTracker.DotSuppress, "x", "tier"); fl.Info(FightTracker.DotSuppress, "x", "tier");
            fl.Error("dot", "boom");
            ft.End(now.AddSeconds(42));
            fl.Info(FightTracker.DotWarn, "结束后不计");
            Ok(f.Count(FightTracker.DotApply) == 2 && f.Count(FightTracker.DotWarn) == 1 && f.SuppressReasons["tier"] == 2 && f.Count(FightTracker.Errors) == 1,
               "战斗内事件统计");
            string d = ft.Describe(f, now);
            Ok(d.Contains("持续 42s") && d.Contains("播报 1 次") && d.Contains("tier×2"), "战斗摘要文本：" + d);
            Ok(ft.End(now) == null, "没有进行中的战斗时 End 返回 null");
        }

        static void SuppressionTraceTests()
        {
            Func<Rig, string, bool> logged = (rig, s) => rig.M.Diag.Snapshot().Any(x => x.Message.Contains(s) || x.Tag == s);

            var r = new Rig();   // 非测试模式，主城 → 难度过滤
            r.Feed(Zone(129));
            r.Feed(Add("F1F", "高闪雷", 30, Me, "40001234", "木人"));
            Ok(logged(r, "挂上 高闪雷"), "记录 DoT 挂上");
            r.Advance(26.5);
            Ok(logged(r, "tier") && logged(r, "打木人请开测试模式"), "记录未播报原因：难度");

            var c = new Rig(null, true);
            c.M.Context.InCombat = false;
            c.Feed(Add("F1F", "高闪雷", 30, Me, "40001234", "木人"));
            c.Advance(26.5);
            Ok(logged(c, "not-in-combat"), "记录未播报原因：不在战斗");

            var m = new Rig(null, true);
            m.Feed(Add("F1F", "高闪雷", 30, Me, "40000001", "Boss", 10000000));
            m.Feed(Add("F1F", "高闪雷", 30, Me, "40000002", "小怪", 500000));
            m.Advance(26.5);
            Ok(logged(m, "not-major") && logged(m, "播报「高闪雷快掉了"), "记录未播报原因：非主要敌人；记录播报");

            var u = new Rig(null, true);
            u.Feed(Add("ABC", "新雷", 30, Me, "40001234", "木人"));
            u.Feed(Add("ABC", "新雷", 30, Me, "40001234", "木人"));
            Ok(u.M.Diag.Snapshot().Count(x => x.Category == "dot.unknown") == 1 && logged(u, "ID ABC"), "不在列表的状态只提示一次");

            var mgr = new UltimateCombatHelper.Modules.ModuleManager(new FakeNotifier(), null, () => now);
            mgr.Add(new DotModule(DotConfig.CreateDefault(), true));
            mgr.Dispatch(L(Add("F1F", "高闪雷", 30, Me, "40001234", "木人")));
            Ok(mgr.Diag.Snapshot().Any(x => x.Tag == "no-id"), "不知道角色 ID 时提示");

            var cd = new Rig(null, true);
            cd.M.Notifier.Cooldown = TimeSpan.FromSeconds(10);
            cd.Feed(Add("F1F", "高闪雷", 30, Me, "40000001", "Boss"));
            cd.Feed(Add("F20", "高闪雷", 30.5, Me, "40000001", "Boss"));
            cd.Advance(26.2); cd.Advance(0.5);
            Ok(logged(cd, "cooldown"), "记录被去重冷却跳过");
        }

        static void ReportTests()
        {
            var r = new Rig(null, true);
            var rec = new LineRecorder();
            foreach (var raw in new[] { Add("F1F", "高闪雷", 30, Me, "40001234", "木人"), "21|ts|10FF0099|队友甲|1|技能|" + Me + "|Me|h" })
            {
                var l = L(raw); rec.Observe(l.Type, l.Type, raw, raw); rec.Offer(l, raw, Me); r.M.Dispatch(l);
            }
            r.Advance(26.5);
            var rep = new DiagnosticReport { Log = r.M.Diag, Lines = rec, ConfigJson = new UchConfig().ToJson(), MyName = "Me", MyId = Me };
            rep.Env("插件版本", "0.3.0");
            rep.State.Add("角色名：Me");
            var sw = new StringWriter();
            rep.WriteTo(sw, now);
            string text = sw.ToString();
            Ok(text.Contains("---- 环境 ----") && text.Contains("---- 配置 ----") && text.Contains("---- 运行日志") && text.Contains("---- 战斗日志行"), "诊断包包含各节");
            Ok(text.Contains("\"Diagnostics\"") && text.Contains("播报「高闪雷"), "包含配置与运行日志");
            Ok(!text.Contains("队友甲") && !text.Contains("|Me|") && text.Contains("玩家1") && text.Contains("角色名：<我>"), "导出时脱敏");
            // 只出现在未记录的他人行里的玩家、用户目录路径，也要脱敏
            var other = L(Add("31", "某buff", 30, "10FF0077", "10FF0078", "队友乙"));
            rec.Offer(other, "x", Me);
            r.M.Diag.Info("plugin", "队友乙 的日志在 C:\\Users\\Someone\\AppData");
            rep.UserProfile = "C:\\Users\\Someone";
            sw = new StringWriter(); rep.WriteTo(sw, now);
            Ok(!sw.ToString().Contains("队友乙") && !sw.ToString().Contains("Someone") && sw.ToString().Contains("%USERPROFILE%\\AppData"),
               "未记录行里的玩家名与用户目录也脱敏");
            rep.MaskNames = false;
            sw = new StringWriter(); rep.WriteTo(sw, now);
            Ok(sw.ToString().Contains("队友甲"), "关闭脱敏");

            var c = new UchConfig();
            Ok(c.Diagnostics.RecordLines && c.Diagnostics.ParsedLevel() == DiagLevel.Debug && c.Diagnostics.MaxLines == 20000, "诊断配置默认值");
            string path = Path.Combine(Path.GetTempPath(), "uch-test-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, "{\"Version\":1,\"Dot\":{\"LeadSeconds\":2}}");
            Ok(ConfigStore.Load(path).Diagnostics.AutoSaveFights, "旧配置文件补上诊断配置默认值");
            File.Delete(path);

            ServiceTests();
        }

        static void ServiceTests()
        {
            string dir = Path.Combine(Path.GetTempPath(), "uch-logs-" + Guid.NewGuid().ToString("N"));
            var t = now;
            var svc = new DiagnosticsService(() => t) { LogsDir = dir, SaveInBackground = false };
            svc.Apply(new DiagnosticsConfig { KeepFightFiles = 2, MinFightSeconds = 10 });
            svc.Fill = rep => { rep.Env("插件版本", "test"); rep.MyName = "Me"; };
            var raw = Add("F1F", "高闪雷", 30, Me, "40001234", "木人");
            var l = LogLine.Parse(raw, t);
            svc.Lines.Observe(26, 26, raw, raw); svc.Lines.Offer(l, raw, Me);

            string p = svc.ExportAll();
            string text = File.ReadAllText(p);
            Ok(Path.GetFileName(p).StartsWith(DiagnosticsService.ExportPrefix) && text.Contains("插件版本：test") && text.Contains("|<我>|"), "导出诊断包文件");

            for (int i = 0; i < 4; i++)
            {
                svc.CombatStarted("木人 区", "其他", "黑魔法师");
                t = t.AddSeconds(i == 3 ? 5 : 30);   // 最后一场太短，不保存
                svc.CombatEnded(f => f.ActInfo.Add("遭遇：木人"));
                t = t.AddSeconds(5);
            }
            var fights = Directory.GetFiles(dir, DiagnosticsService.FightPrefix + "*.txt");
            Ok(fights.Length == 2, "自动保存战斗记录并只保留最近 N 份：" + fights.Length);
            Ok(fights.All(f => Path.GetFileName(f).Contains("木人_区")), "文件名中的非法字符被替换");
            Ok(File.ReadAllText(fights.OrderBy(f => f).Last()).Contains("遭遇：木人"), "战斗记录包含 ACT 遭遇信息");
            Ok(svc.Fights.Recent().Count == 4 && svc.CombatEnded(null) == null, "战斗列表");
            Directory.Delete(dir, true);
        }
    }
}
