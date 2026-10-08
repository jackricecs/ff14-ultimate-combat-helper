using System;
using System.Collections.Generic;
using System.Globalization;
using UltimateCombatHelper.Data;
using UltimateCombatHelper.Diagnostics;
using UltimateCombatHelper.Logs;

namespace UltimateCombatHelper.Modules.Dot
{
    /// <summary>功能一：DoT 到期语音提醒。</summary>
    public sealed class DotModule : IModule
    {
        private IModuleHost host;
        private DotConfig config;
        private bool testModeAnywhere;
        private DotTracker tracker;

        public string Id { get { return "dot"; } }
        public string DisplayName { get { return "DoT 监控"; } }
        public DotTracker Tracker { get { return tracker; } }

        /// <summary>最近一次提醒的文本（供界面显示/测试）。</summary>
        public event Action<string, IList<DotWarning>> Warned;

        public DotModule(DotConfig config, bool testModeAnywhere)
        {
            Configure(config, testModeAnywhere);
        }

        public void Configure(DotConfig cfg, bool testMode)
        {
            config = cfg;
            testModeAnywhere = testMode;
            if (tracker != null) tracker.Configure(cfg);
        }

        public void Attach(IModuleHost h)
        {
            host = h;
            tracker = new DotTracker(() => host.Context.MyId, host.Clock, host.Diag);
            tracker.Configure(config);
        }

        public void OnLogLine(LogLine line) { if (tracker != null) tracker.OnLogLine(line); }

        public void OnTick()
        {
            if (tracker == null) return;
            var cfg = config;
            var suppressed = new List<KeyValuePair<DotWarning, Suppression>>();
            var targetCache = new TargetCache(host);
            var ws = tracker.Poll(e =>
            {
                var why = Check(cfg, e, targetCache);
                if (why == null) return true;
                suppressed.Add(new KeyValuePair<DotWarning, Suppression>(new DotWarning { Entry = e, Remaining = e.Remaining(host.Clock()) }, why));
                return false;
            });
            foreach (var s in suppressed)
                host.Diag.Info(FightTracker.DotSuppress, string.Format(CultureInfo.InvariantCulture, "{0} → {1} 剩 {2:0.0}s，未播报：{3}",
                    s.Key.Entry.DisplayName, s.Key.Entry.TargetName, s.Key.Remaining, s.Value.Text), s.Value.Code);
            if (ws.Count == 0) return;

            string text = DotTracker.ComposeSpeech(ws, cfg.SpeechTemplate);
            if (host.Notifier.Speak(text, "dot:" + text))
            {
                host.Diag.Info(FightTracker.DotWarn, string.Format(CultureInfo.InvariantCulture, "播报「{0}」（{1} 个 DoT，目标：{2}，剩 {3:0.0}s）",
                    text, ws.Count, ws[0].Entry.TargetName, ws[0].Remaining));
                var h = Warned; if (h != null) h(text, ws);
            }
            else
            {
                host.Diag.Info(FightTracker.DotSuppress, "「" + text + "」与刚播过的相同，被去重冷却跳过（通用页可调间隔）", "cooldown");
            }
        }

        /// <summary>不该播报的原因；null = 应该播报。</summary>
        private Suppression Check(DotConfig cfg, DotEntry e, TargetCache target)
        {
            var ctx = host.Context;
            if (!cfg.Enabled) return new Suppression("dot-off", "DoT 提醒未启用");
            if (cfg.OnlyInCombat && !ctx.InCombat) return new Suppression("not-in-combat", "ACT 认为当前不在战斗中（已勾选「仅战斗中」）");
            if (!testModeAnywhere && !cfg.TierAllowed(ctx.Zone.Tier))
                return new Suppression("tier", string.Format("当前区域「{0}」难度为「{1}」，不在提醒列表（打木人请开测试模式）",
                    ctx.Zone.Id == 0 ? "未知" : ctx.Zone.NameCn, ZoneTable.DisplayName(ctx.Zone.Tier)));
            switch (cfg.TargetMode)
            {
                case DotTargetMode.All: return null;
                case DotTargetMode.Current:
                    string cur = target.Get();
                    return cur == null || Hex.Norm(cur) == e.TargetId ? null   // 无目标时不过滤
                        : new Suppression("not-target", "不是当前选中目标（当前目标 " + cur + "）");
                default:   // Major
                    return ctx.IsMajorEnemy(e.TargetId, cfg.MajorHpRatio) ? null
                        : new Suppression("not-major", string.Format(CultureInfo.InvariantCulture, "不是主要敌人（最大HP {0:N0}，场上最高 {1:N0}，阈值 {2:0.##} 倍）",
                            ctx.MaxHpOf(e.TargetId), ctx.HighestEnemyMaxHp, cfg.MajorHpRatio));
            }
        }

        public void Detach() { if (tracker != null) tracker.Clear(); }

        private sealed class Suppression
        {
            public readonly string Code, Text;
            public Suppression(string code, string text) { Code = code; Text = text; }
        }

        /// <summary>一次 Tick 里最多查询一次当前目标（SDK 调用较重）。</summary>
        private sealed class TargetCache
        {
            private readonly IModuleHost host;
            private bool fetched;
            private string value;
            public TargetCache(IModuleHost host) { this.host = host; }
            public string Get()
            {
                if (!fetched) { value = host.GetCurrentTargetId(); fetched = true; }
                return value;
            }
        }
    }
}
