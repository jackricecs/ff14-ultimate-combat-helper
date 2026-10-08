using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UltimateCombatHelper.Diagnostics;
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
        private readonly DiagLog diag;
        private readonly HashSet<string> reportedUnknown = new HashSet<string>();
        private DateTime lastNoIdReport = DateTime.MinValue;
        private Dictionary<string, DotDef> defs;
        private double globalLead;

        public DotTracker(Func<string> myId, Func<DateTime> clock, DiagLog diag = null)
        {
            this.myId = myId; this.clock = clock;
            this.diag = diag ?? new DiagLog(clock);
            defs = new Dictionary<string, DotDef>();
        }

        public void Configure(DotConfig cfg)
        {
            lock (sync) { defs = cfg.Index(); globalLead = cfg.LeadSeconds; }
        }

        public void OnLogLine(LogLine l)
        {
            List<DiagEntry> notes = null;   // 锁外再写日志，避免持锁回调界面；绝大多数行用不到，按需分配
            lock (sync)
            {
                switch (l.Type)
                {
                    case LogType.ChangeZone: entries.Clear(); break;
                    case LogType.ChangePrimaryPlayer: entries.Clear(); break;
                    case LogType.RemoveCombatant:
                    case LogType.Death: RemoveTarget(l.Id(2), l.Type == LogType.Death ? "目标死亡" : "目标消失", ref notes); break;
                    case LogType.StatusAdd: OnStatusAdd(l, ref notes); break;
                    case LogType.StatusRemove:
                        if (l.Count < 8 || l.Id(5) != myId()) break;   // 先过滤来源，他人的行不做多余的字符串处理
                        DotEntry gone;
                        string k = l.Id(7) + "|" + l.Id(2);
                        if (entries.TryGetValue(k, out gone))
                        {
                            entries.Remove(k);
                            Add(ref notes, Note(DiagLevel.Debug, "dot.remove", string.Format(CultureInfo.InvariantCulture,
                                "{0} → {1} 消失（预计剩余 {2:0.0}s）", gone.DisplayName, gone.TargetName, gone.Remaining(clock())), null));
                        }
                        break;
                }
            }
            if (notes != null) foreach (var n in notes) diag.Write(n.Level, n.Category, n.Message, n.Tag);
        }

        private static void Add(ref List<DiagEntry> notes, DiagEntry e)
        {
            if (notes == null) notes = new List<DiagEntry>();
            notes.Add(e);
        }

        private DiagEntry Note(DiagLevel level, string category, string message, string tag)
        {
            return new DiagEntry(DateTime.MinValue, level, category, message, tag);
        }

        private void OnStatusAdd(LogLine l, ref List<DiagEntry> notes)
        {
            if (l.Count < 9) return;
            string me = myId();
            DotDef def;
            string effectId = l.Id(2);
            bool known = defs.TryGetValue(effectId, out def);
            if (me.Length == 0)
            {
                DateTime t = clock();
                if (known && t - lastNoIdReport > TimeSpan.FromSeconds(15))
                {
                    lastNoIdReport = t;
                    Add(ref notes, Note(DiagLevel.Info, "dot.skip", "收到 DoT 状态 " + l[3] + "，但还不知道你的角色 ID，无法判断是不是你挂的（看总览页角色ID）", "no-id"));
                }
                return;
            }
            if (l.Id(5) != me) return;
            double duration = l.Double(4);
            string targetId = l.Id(7);
            if (!known)
            {
                // 你挂在敌人身上、但不在列表里的有时限状态：DoT ID 不对时就靠这条找出真实 ID
                if (Hex.IsNpc(targetId) && duration > 0 && reportedUnknown.Count < 300 && reportedUnknown.Add(effectId))
                    Add(ref notes, Note(DiagLevel.Debug, "dot.unknown", string.Format(CultureInfo.InvariantCulture,
                        "你对 {0} 施加的状态 {1}（ID {2}，{3:0.##}s）不在 DoT 监控列表中；如果它是 DoT，把 ID 加进列表", l[8], l[3], effectId, duration), null));
                return;
            }
            if (!def.Enabled)
            {
                Add(ref notes, Note(DiagLevel.Debug, "dot.skip", def.Note + "（" + l[3] + "，ID " + effectId + "）在列表中但未勾选启用", "disabled-def"));
                return;
            }
            if (duration <= 0)
            {
                Add(ref notes, Note(DiagLevel.Debug, "dot.skip", l[3] + " 的持续时间字段无效：" + l[4], "bad-duration"));
                return;
            }

            string key = targetId + "|" + effectId;
            double lead = def.LeadSeconds ?? globalLead;
            DotEntry e;
            bool refresh = entries.TryGetValue(key, out e);
            if (!refresh) { e = new DotEntry(); entries[key] = e; }
            DateTime now = clock();
            e.TargetId = targetId;
            e.TargetName = l[8];
            e.EffectId = effectId;
            e.DisplayName = string.IsNullOrEmpty(def.Name) ? l[3] : def.Name;
            e.AppliedAt = now;
            e.ExpiresAt = now.AddSeconds(duration);
            e.LeadSeconds = lead;
            e.Warned = duration <= lead;   // 刷新即重置；本来就不足阈值则不报
            Add(ref notes, Note(DiagLevel.Debug, "dot.apply", string.Format(CultureInfo.InvariantCulture,
                "{0}{1} → {2}（{3}），{4:0.##}s，剩 {5:0.#}s 时提醒{6}", refresh ? "刷新 " : "挂上 ", e.DisplayName, e.TargetName, targetId,
                duration, lead, e.Warned ? "（时长不足提醒阈值，本次不报）" : ""), null));
        }

        private void RemoveTarget(string t, string why, ref List<DiagEntry> notes)
        {
            foreach (var k in entries.Where(kv => kv.Value.TargetId == t).Select(kv => kv.Key).ToList())
            {
                Add(ref notes, Note(DiagLevel.Debug, "dot.remove", entries[k].DisplayName + " → " + entries[k].TargetName + "：" + why, null));
                entries.Remove(k);
            }
        }

        public void Clear() { lock (sync) entries.Clear(); }

        /// <summary>
        /// 返回进入提醒区间的 DoT。shouldWarn 为 false 的也会标记已处理，不会稍后再报。
        /// shouldWarn 在锁外调用（它可能查询 SDK 当前目标，不能挡住日志线程）。
        /// </summary>
        public List<DotWarning> Poll(Func<DotEntry, bool> shouldWarn)
        {
            DateTime now = clock();
            var due = new List<DotWarning>();
            var dead = new List<string>();
            lock (sync)
            {
                foreach (var kv in entries)
                {
                    var e = kv.Value;
                    double rem = e.Remaining(now);
                    if (rem < -3) { dead.Add(kv.Key); continue; }       // 没收到 30 行的兜底清理
                    if (e.Warned || rem > e.LeadSeconds || rem < 0) continue;
                    e.Warned = true;
                    due.Add(new DotWarning { Entry = e.Clone(), Remaining = rem });
                }
                foreach (var k in dead) entries.Remove(k);
            }
            if (dead.Count > 0) diag.Trace("dot.remove", "清理 " + dead.Count + " 个已过期但没收到消失行的 DoT");
            return shouldWarn == null ? due : due.Where(w => shouldWarn(w.Entry)).ToList();
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
