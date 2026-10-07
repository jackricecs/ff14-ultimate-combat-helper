using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UltimateCombatHelper.Logs;

namespace UltimateCombatHelper.Modules.Dot
{
    public sealed class DotEntry
    {
        public string TargetId, TargetName, EffectId, DisplayName;
        public DateTime AppliedAt, ExpiresAt;
        public double LeadSeconds;
        public bool Warned;
        public double Remaining(DateTime now) { return (ExpiresAt - now).TotalSeconds; }
        public DotEntry Clone() { return (DotEntry)MemberwiseClone(); }
    }

    public sealed class DotWarning
    {
        public DotEntry Entry;
        public double Remaining;
    }

    /// <summary>
    /// 跟踪"我"挂在各目标上的 DoT 的到期时间。纯逻辑，不依赖 ACT。
    ///   26 StatusAdd: effectId[2] effect[3] duration[4] sourceId[5] source[6] targetId[7] target[8]
    ///   30 StatusRemove: effectId[2] sourceId[5] targetId[7]
    ///   25 Death / 04 RemoveCombatant: id[2]；01 ChangeZone：清空
    /// </summary>
    public sealed class DotTracker
    {
        private readonly object sync = new object();
        private readonly Dictionary<string, DotEntry> entries = new Dictionary<string, DotEntry>();
        private readonly Func<DateTime> clock;
        private readonly Func<string> myId;
        private Dictionary<string, DotDef> defs;
        private double globalLead;

        public DotTracker(Func<string> myId, Func<DateTime> clock)
        {
            this.myId = myId; this.clock = clock;
            defs = new Dictionary<string, DotDef>();
        }

        public void Configure(DotConfig cfg)
        {
            lock (sync) { defs = cfg.Index(); globalLead = cfg.LeadSeconds; }
        }

        public void OnLogLine(LogLine l)
        {
            lock (sync)
            {
                switch (l.Type)
                {
                    case LogType.ChangeZone: entries.Clear(); break;
                    case LogType.ChangePrimaryPlayer: entries.Clear(); break;
                    case LogType.RemoveCombatant:
                    case LogType.Death: RemoveTarget(l.Id(2)); break;
                    case LogType.StatusAdd: OnStatusAdd(l); break;
                    case LogType.StatusRemove:
                        if (l.Count >= 8 && l.Id(5) == myId()) entries.Remove(l.Id(7) + "|" + l.Id(2));
                        break;
                }
            }
        }

        private void OnStatusAdd(LogLine l)
        {
            string me = myId();
            if (l.Count < 9 || me.Length == 0 || l.Id(5) != me) return;
            DotDef def;
            string effectId = l.Id(2);
            if (!defs.TryGetValue(effectId, out def) || !def.Enabled) return;
            double duration = l.Double(4);
            if (duration <= 0) return;

            string targetId = l.Id(7);
            string key = targetId + "|" + effectId;
            double lead = def.LeadSeconds ?? globalLead;
            DotEntry e;
            if (!entries.TryGetValue(key, out e)) { e = new DotEntry(); entries[key] = e; }
            DateTime now = clock();
            e.TargetId = targetId;
            e.TargetName = l[8];
            e.EffectId = effectId;
            e.DisplayName = string.IsNullOrEmpty(def.Name) ? l[3] : def.Name;
            e.AppliedAt = now;
            e.ExpiresAt = now.AddSeconds(duration);
            e.LeadSeconds = lead;
            e.Warned = duration <= lead;   // 刷新即重置；本来就不足阈值则不报
        }

        private void RemoveTarget(string t)
        {
            foreach (var k in entries.Where(kv => kv.Value.TargetId == t).Select(kv => kv.Key).ToList())
                entries.Remove(k);
        }

        public void Clear() { lock (sync) entries.Clear(); }

        /// <summary>返回进入提醒区间的 DoT。shouldWarn 为 false 的也会标记已处理，不会稍后再报。</summary>
        public List<DotWarning> Poll(Func<DotEntry, bool> shouldWarn)
        {
            DateTime now = clock();
            var result = new List<DotWarning>();
            lock (sync)
            {
                var dead = new List<string>();
                foreach (var kv in entries)
                {
                    var e = kv.Value;
                    double rem = e.Remaining(now);
                    if (rem < -3) { dead.Add(kv.Key); continue; }       // 没收到 30 行的兜底清理
                    if (e.Warned || rem > e.LeadSeconds || rem < 0) continue;
                    e.Warned = true;
                    if (shouldWarn == null || shouldWarn(e)) result.Add(new DotWarning { Entry = e.Clone(), Remaining = rem });
                }
                foreach (var k in dead) entries.Remove(k);
            }
            return result;
        }

        public List<DotEntry> Snapshot()
        {
            lock (sync) return entries.Values.Select(e => e.Clone()).OrderBy(e => e.ExpiresAt).ToList();
        }

        /// <summary>多个提醒合成一句，避免同时到期时连播。</summary>
        public static string ComposeSpeech(IList<DotWarning> ws, string template)
        {
            if (ws == null || ws.Count == 0) return null;
            var targets = ws.Select(w => w.Entry.TargetId).Distinct().Count();
            return (template ?? "{dot}")
                .Replace("{dot}", string.Join("、", ws.Select(w => w.Entry.DisplayName).Distinct()))
                .Replace("{target}", targets == 1 ? ws[0].Entry.TargetName : targets + "个目标")
                .Replace("{sec}", Math.Max(0, (int)Math.Round(ws[0].Remaining)).ToString(CultureInfo.InvariantCulture));
        }
    }
}
