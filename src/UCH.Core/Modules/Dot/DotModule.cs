using System;
using System.Collections.Generic;
using UltimateCombatHelper.Data;
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
            tracker = new DotTracker(() => host.Context.MyId, host.Clock);
            tracker.Configure(config);
        }

        public void OnLogLine(LogLine line) { if (tracker != null) tracker.OnLogLine(line); }

        public void OnTick()
        {
            if (tracker == null) return;
            var cfg = config;
            var ctx = host.Context;
            bool active = cfg.Enabled
                          && (!cfg.OnlyInCombat || ctx.InCombat)
                          && (testModeAnywhere || cfg.TierAllowed(ctx.Zone.Tier));

            string currentTarget = null; bool targetFetched = false;
            var ws = tracker.Poll(e =>
            {
                if (!active) return false;
                switch (cfg.TargetMode)
                {
                    case DotTargetMode.All: return true;
                    case DotTargetMode.Current:
                        if (!targetFetched) { currentTarget = host.GetCurrentTargetId(); targetFetched = true; }
                        return currentTarget == null || Hex.Norm(currentTarget) == e.TargetId;   // 无目标时不过滤
                    default:   // Major
                        return ctx.IsMajorEnemy(e.TargetId, cfg.MajorHpRatio);
                }
            });
            if (ws.Count == 0) return;
            string text = DotTracker.ComposeSpeech(ws, cfg.SpeechTemplate);
            if (host.Notifier.Speak(text, "dot:" + text))
            {
                var h = Warned; if (h != null) h(text, ws);
            }
        }

        public void Detach() { if (tracker != null) tracker.Clear(); }
    }
}
