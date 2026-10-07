// UCH.Core 单元测试：不依赖测试框架，Windows(.NET Framework) 与 Linux(Mono) 都能直接运行。
// 运行：见 docs/development.md。退出码 0 = 全部通过。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UltimateCombatHelper.Config;
using UltimateCombatHelper.Data;
using UltimateCombatHelper.Logs;
using UltimateCombatHelper.Modules;
using UltimateCombatHelper.Modules.Dot;
using UltimateCombatHelper.Notify;

namespace UltimateCombatHelper.Tests
{
    internal sealed class FakeNotifier : INotifier
    {
        public readonly List<string> Spoken = new List<string>();
        public readonly List<string> Logs = new List<string>();
        public void Speak(string t) { Spoken.Add(t); }
        public void Log(string t) { Logs.Add(t); }
    }

    internal static class Program
    {
        static int pass, fail;
        static DateTime now = new DateTime(2026, 10, 7, 20, 0, 0);
        const string Me = "10FF0001";

        static void Ok(bool c, string name) { if (c) pass++; else { fail++; Console.WriteLine("FAIL: " + name); } }

        static LogLine L(string raw) { return LogLine.Parse(raw, now); }
        static string Add(string eff, string name, double dur, string src, string tgt, string tgtName, long tgtMaxHp = 100000)
        {
            return string.Format(CultureInfo.InvariantCulture, "26|ts|{0}|{1}|{2:0.00}|{3}|Me|{4}|{5}|00|{6}|50000|h", eff, name, dur, src, tgt, tgtName, tgtMaxHp);
        }
        static string Zone(uint id) { return "01|ts|" + id.ToString("X") + "|zone|h"; }

        sealed class Rig
        {
            public FakeNotifier N = new FakeNotifier();
            public ModuleManager M;
            public DotModule Dot;
            public string Target;
            public Rig(DotConfig cfg = null, bool testMode = false)
            {
                M = new ModuleManager(N, () => Target, () => now);
                M.Notifier.Cooldown = TimeSpan.Zero;
                Dot = new DotModule(cfg ?? DotConfig.CreateDefault(), testMode);
                M.Add(Dot);
                M.Context.InCombat = true;
                Feed("02|ts|" + Me + "|Me|h");
            }
            public void Feed(string raw) { M.Dispatch(L(raw)); }
            public int Advance(double sec) { int before = N.Spoken.Count; now = now.AddSeconds(sec); M.Tick(); return N.Spoken.Count - before; }
            public string Last { get { return N.Spoken.LastOrDefault(); } }
        }

        static int Main()
        {
            ZoneTests();
            ConfigTests();
            BasicDotTests();
            TierFilterTests();
            TargetModeTests();
            RobustnessTests();
            Console.WriteLine("通过 " + pass + " / " + (pass + fail));
            return fail == 0 ? 0 : 1;
        }

        static void ZoneTests()
        {
            Ok(ZoneTable.Count > 300, "副本表已生成");
            Ok(ZoneTable.Get(1321).Tier == ContentTier.Savage, "M1S = 零式");
            Ok(ZoneTable.Get(1363).Tier == ContentTier.Ultimate, "妖星乱舞 = 绝");
            Ok(ZoneTable.Get(1362).Tier == ContentTier.Extreme && ZoneTable.Get(1361).Tier == ContentTier.Trial, "极神/普通歼灭战区分");
            Ok(ZoneTable.Get(129).Tier == ContentTier.Other, "主城 = Other");
            Ok(ZoneTable.IsHighEnd(ContentTier.Ultimate) && !ZoneTable.IsHighEnd(ContentTier.Raid), "高难判定");
        }

        static void ConfigTests()
        {
            string path = Path.Combine(Path.GetTempPath(), "uch-test-" + Guid.NewGuid().ToString("N") + ".json");
            var c = ConfigStore.Load(path);
            Ok(File.Exists(path), "首次加载写出默认配置");
            Ok(c.Dot.LeadSeconds == 4 && c.Dot.TargetMode == DotTargetMode.Major && c.Dot.Dots.Any(d => d.Id == "F1F"), "默认值");
            c.Dot.LeadSeconds = 3.5; c.Dot.Dots[0].Name = "高闪雷（自定义）"; c.Dot.Dots[0].LeadSeconds = 6;
            ConfigStore.Save(path, c);
            var c2 = ConfigStore.Load(path);
            Ok(c2.Dot.LeadSeconds == 3.5 && c2.Dot.Dots[0].Name == "高闪雷（自定义）" && c2.Dot.Dots[0].LeadSeconds == 6, "保存/读取往返（含中文）");
            File.WriteAllText(path, "{\"Version\":1,\"Dot\":{\"LeadSeconds\":2}}", new UTF8Encoding(false));
            var c3 = ConfigStore.Load(path);
            Ok(c3.Dot.LeadSeconds == 2 && c3.Dot.SpeechTemplate == "{dot}快掉了，该补了" && c3.Dot.Dots.Count > 10 && c3.General.Enabled,
               "缺失字段补默认值（向后兼容）");
            File.WriteAllText(path, "{ 坏掉的 json", new UTF8Encoding(false));
            var logs = new List<string>();
            var c4 = ConfigStore.Load(path, logs.Add);
            Ok(c4.Dot.LeadSeconds == 4 && logs.Count == 1 && Directory.GetFiles(Path.GetDirectoryName(path), Path.GetFileName(path) + ".*.bak").Length == 1,
               "损坏配置自动备份并回退默认");
            foreach (var f in Directory.GetFiles(Path.GetDirectoryName(path), Path.GetFileName(path) + "*")) File.Delete(f);
        }

        static void BasicDotTests()
        {
            var r = new Rig(null, testMode: true);
            r.Feed(Add("F1F", "高闪雷", 30, Me, "40001234", "木人"));
            Ok(r.Advance(25.9) == 0, "剩 4.1s 不提醒");
            Ok(r.Advance(0.2) == 1 && r.Last == "高闪雷快掉了，该补了", "剩 3.9s 提醒：" + r.Last);
            Ok(r.Advance(1) == 0, "不重复提醒");
            r.Feed(Add("F1F", "高闪雷", 30, Me, "40001234", "木人"));
            Ok(r.Advance(25) == 0 && r.Advance(1.1) == 1, "补 DoT 后重新计时");
            r.Feed(Add("F1F", "高闪雷", 30, "10FF0099", "40001234", "木人"));
            r.Dot.Tracker.Clear(); r.Feed(Add("F1F", "高闪雷", 30, "10FF0099", "40001234", "木人"));
            Ok(r.Dot.Tracker.Snapshot().Count == 0, "他人 DoT 不记录");
            r.Feed(Add("31", "某buff", 30, Me, "40001234", "木人"));
            Ok(r.Dot.Tracker.Snapshot().Count == 0, "非 DoT 状态不记录");
            r.Feed(Add("F1F", "高闪雷", 30, Me, "40001234", "木人"));
            r.Feed("25|ts|40001234|木人|" + Me + "|Me|h");
            Ok(r.Dot.Tracker.Snapshot().Count == 0, "目标死亡清除");
            r.Feed(Add("F1F", "高闪雷", 30, Me, "40001234", "木人"));
            r.Feed("30|ts|F1F|高闪雷|0.00|" + Me + "|Me|40001234|木人|00|h");
            Ok(r.Dot.Tracker.Snapshot().Count == 0, "状态消失清除");
            r.Feed(Add("F1F", "高闪雷", 3, Me, "40001234", "木人"));
            Ok(r.Advance(0.1) == 0, "挂上即低于阈值不报");
            r.Advance(10);
            Ok(r.Dot.Tracker.Snapshot().Count == 0, "过期兜底清理");
            r.M.Context.InCombat = false;
            r.Feed(Add("F1F", "高闪雷", 30, Me, "40001234", "木人"));
            Ok(r.Advance(26.5) == 0, "非战斗不提醒");
            r.M.Context.InCombat = true;

            // 按角色名学习 ID（ACT 晚于登录启动，收不到 02 行）
            var n = new FakeNotifier();
            var m = new ModuleManager(n, null, () => now);
            m.Context.MyName = "Me";
            m.Dispatch(L(Add("F1F", "高闪雷", 30, Me, "40001234", "木人")));
            Ok(m.Context.MyId == Me, "按角色名学习自身 ID");

            // 单 DoT 自定义名称与提前量
            var cfg = DotConfig.CreateDefault();
            var def = cfg.Dots.First(d => d.Id == "F1F"); def.Name = "雷"; def.LeadSeconds = 6;
            var r2 = new Rig(cfg, true);
            r2.Feed(Add("F1F", "高闪雷", 30, Me, "40001234", "木人"));
            Ok(r2.Advance(23.9) == 0 && r2.Advance(0.2) == 1 && r2.Last.StartsWith("雷"), "单 DoT 覆盖名称/秒数");
        }

        static void TierFilterTests()
        {
            var r = new Rig();   // 默认：仅团本/高难，非测试模式
            r.Feed(Zone(129));   // 主城
            r.Feed(Add("F1F", "高闪雷", 30, Me, "40001234", "木人"));
            Ok(r.Advance(26.5) == 0, "主城/木人默认不提醒");
            r.Feed(Zone(1321));  // M1S
            r.Feed(Add("F1F", "高闪雷", 30, Me, "40001234", "Boss"));
            Ok(r.Advance(26.5) == 1, "零式提醒");
            r.Feed(Zone(1361));  // 普通歼灭战
            r.Feed(Add("F1F", "高闪雷", 30, Me, "40001234", "Boss"));
            Ok(r.Advance(26.5) == 0, "普通歼灭战默认不提醒");
            r.Feed(Zone(1363));  // 绝
            r.Feed(Add("F1F", "高闪雷", 30, Me, "40001234", "Boss"));
            Ok(r.Advance(26.5) == 1, "绝本提醒");
            var t = new Rig(null, testMode: true);
            t.Feed(Zone(129));
            t.Feed(Add("F1F", "高闪雷", 30, Me, "40001234", "木人"));
            Ok(t.Advance(26.5) == 1, "测试模式无视副本过滤");
        }

        static void TargetModeTests()
        {
            // Major（默认）：Boss 1000 万，小怪 50 万 → 只报 Boss；奶人切目标不影响
            var r = new Rig(null, true);
            r.Feed("03|ts|40000001|Boss|00|64|0000|00||1|1|10000000|10000000|10000|10000|||0|0|0|0|h");
            r.Feed(Add("F1F", "高闪雷", 30, Me, "40000001", "Boss", 10000000));
            r.Feed(Add("F1F", "高闪雷", 30, Me, "40000002", "小怪", 500000));
            r.Target = "10FF0005";   // 治疗切到队友身上
            Ok(r.Advance(26.5) == 1 && r.Last == "高闪雷快掉了，该补了", "Major：只报 Boss，切目标奶人不影响");
            Ok(r.N.Spoken.Count == 1, "小怪的 DoT 不报");

            // 双 Boss：血量相近都算主要
            var d = new Rig(null, true);
            d.Feed(Add("F1F", "高闪雷", 30, Me, "40000001", "Boss A", 8000000));
            d.Feed(Add("F1F", "高闪雷", 30, Me, "40000002", "Boss B", 7600000));
            d.Feed(Add("F1F", "高闪雷", 30, Me, "40000003", "小怪", 300000));
            var ws = new List<DotWarning>();
            d.Dot.Warned += (t, w) => ws.AddRange(w);
            Ok(d.Advance(26.5) == 1 && ws.Count == 2 && ws.All(w => w.Entry.TargetName.StartsWith("Boss")), "双 Boss 都报、小怪不报，合成一句");

            // Boss 未挂 DoT 也能作为基准：从 21 行得知 Boss 血量，挂在小怪上的 DoT 不报
            var b = new Rig(null, true);
            var f = Enumerable.Repeat("", 46).ToArray();
            f[0] = "21"; f[1] = "ts"; f[2] = "10FF0005"; f[3] = "队友"; f[6] = "40000001"; f[7] = "Boss"; f[24] = "9000000"; f[25] = "10000000";
            b.Feed(string.Join("|", f));
            b.Feed(Add("F1F", "高闪雷", 30, Me, "40000009", "小怪", 400000));
            Ok(b.Advance(26.5) == 0, "从技能行得知 Boss 血量，只挂小怪不报");
            b.Feed("25|ts|40000001|Boss|0|x|h");    // Boss 死亡后小怪成为最高
            b.Feed(Add("F1F", "高闪雷", 30, Me, "40000009", "小怪", 400000));
            Ok(b.Advance(26.5) == 1, "Boss 死亡后剩余敌人成为主要目标");

            // Current：只报当前目标；无目标不过滤
            var cfg = DotConfig.CreateDefault(); cfg.TargetMode = DotTargetMode.Current;
            var c = new Rig(cfg, true);
            c.Feed(Add("F1F", "高闪雷", 30, Me, "40000001", "Boss", 10000000));
            c.Feed(Add("F20", "高震雷", 30, Me, "40000002", "小怪", 500000));
            c.Target = "40000002";
            var cw = new List<DotWarning>(); c.Dot.Warned += (t, w) => cw.AddRange(w);
            Ok(c.Advance(26.5) == 1 && cw.Count == 1 && cw[0].Entry.TargetName == "小怪", "Current：只报当前目标");

            // All
            var acfg = DotConfig.CreateDefault(); acfg.TargetMode = DotTargetMode.All;
            var a = new Rig(acfg, true);
            a.Feed(Add("F1F", "高闪雷", 30, Me, "40000001", "Boss", 10000000));
            a.Feed(Add("F1F", "高闪雷", 30, Me, "40000002", "小怪", 500000));
            var aw = new List<DotWarning>(); a.Dot.Warned += (t, w) => aw.AddRange(w);
            Ok(a.Advance(26.5) == 1 && aw.Count == 2, "All：全部报，合成一句");
            Ok(DotTracker.ComposeSpeech(aw, "{dot}对{target}快掉了") == "高闪雷对2个目标快掉了", "多目标合成文本");
        }

        static void RobustnessTests()
        {
            var r = new Rig(null, true);
            r.M.Add(new ThrowingModule());
            r.Feed(Add("F1F", "高闪雷", 30, Me, "40000001", "Boss"));
            Ok(r.Advance(26.5) == 1, "其他模块抛异常不影响 DoT 模块");
            Ok(r.N.Logs.Any(x => x.Contains("boom")), "模块异常写入日志");
            Ok(LogLine.Parse("", now) == null && LogLine.Parse("abc|x", now) == null, "异常行安全忽略");
            r.Feed("26|ts|F1F");   // 字段不足
            Ok(true, "字段不足不抛异常");
        }

        sealed class ThrowingModule : IModule
        {
            public string Id { get { return "throw"; } }
            public string DisplayName { get { return "throw"; } }
            public void Attach(IModuleHost host) { }
            public void OnLogLine(LogLine line) { throw new InvalidOperationException("boom"); }
            public void OnTick() { throw new InvalidOperationException("boom"); }
            public void Detach() { }
        }
    }
}
