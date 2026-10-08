using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace UltimateCombatHelper.Diagnostics
{
    /// <summary>一场战斗（ACT 进入战斗 → 脱战）的摘要。</summary>
    public sealed class FightSummary
    {
        public int Index;
        public DateTime Start;
        public DateTime? End;
        public string Zone, Tier, Job;
        /// <summary>ACT 侧的战斗信息（遭遇名、DPS 等），脱战时由插件填入。</summary>
        public List<string> ActInfo = new List<string>();
        public readonly Dictionary<string, int> Counts = new Dictionary<string, int>();
        public readonly Dictionary<string, int> SuppressReasons = new Dictionary<string, int>();

        public double Seconds(DateTime now) { return ((End ?? now) - Start).TotalSeconds; }
        public int Count(string category) { int n; return Counts.TryGetValue(category, out n) ? n : 0; }

        public string Describe(DateTime now)
        {
            var sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture, "#{0} {1:MM-dd HH:mm:ss} 持续 {2:0}s  区域：{3}（{4}）  职业：{5}",
                Index, Start, Seconds(now), Zone, Tier, string.IsNullOrEmpty(Job) ? "未知" : Job);
            sb.AppendFormat(CultureInfo.InvariantCulture, "  DoT 挂上 {0} 次 / 播报 {1} 次 / 未播报 {2} 次",
                Count(FightTracker.DotApply), Count(FightTracker.DotWarn), Count(FightTracker.DotSuppress));
            if (SuppressReasons.Count > 0)
                sb.Append("（" + string.Join("，", SuppressReasons.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + "×" + kv.Value)) + "）");
            if (Count(FightTracker.Errors) > 0) sb.Append("  错误 " + Count(FightTracker.Errors) + " 条");
            return sb.ToString();
        }
    }

    /// <summary>按 ACT 的战斗状态切分战斗，并从 DiagLog 的条目里统计 DoT 相关事件。线程安全。</summary>
    public sealed class FightTracker
    {
        public const string DotApply = "dot.apply";
        public const string DotWarn = "dot.warn";
        public const string DotSuppress = "dot.suppress";
        public const string Errors = "error";
        private const int Keep = 30;

        private readonly object sync = new object();
        private readonly List<FightSummary> fights = new List<FightSummary>();
        private FightSummary current;
        private int counter;

        public FightTracker(DiagLog log)
        {
            log.Emitted += OnEntry;
        }

        private void OnEntry(DiagEntry e)
        {
            lock (sync)
            {
                if (current == null) return;
                string key = e.Level == DiagLevel.Error ? Errors : e.Category;
                int n; current.Counts.TryGetValue(key, out n); current.Counts[key] = n + 1;
                if (e.Category == DotSuppress && !string.IsNullOrEmpty(e.Tag))
                {
                    current.SuppressReasons.TryGetValue(e.Tag, out n); current.SuppressReasons[e.Tag] = n + 1;
                }
            }
        }

        public FightSummary Current { get { lock (sync) return current; } }

        public FightSummary Begin(DateTime t, string zone, string tier, string job)
        {
            lock (sync)
            {
                current = new FightSummary { Index = ++counter, Start = t, Zone = zone, Tier = tier, Job = job };
                fights.Add(current);
                if (fights.Count > Keep) fights.RemoveAt(0);
                return current;
            }
        }

        /// <summary>结束当前战斗并返回它；没有进行中的战斗返回 null。</summary>
        public FightSummary End(DateTime t)
        {
            lock (sync)
            {
                var f = current;
                if (f == null) return null;
                f.End = t;
                current = null;
                return f;
            }
        }

        public List<FightSummary> Recent() { lock (sync) return fights.ToList(); }

        /// <summary>在锁内生成文字摘要（统计字典可能正被日志线程写入，界面线程不要直接枚举）。</summary>
        public string Describe(FightSummary f, DateTime now) { lock (sync) return f.Describe(now); }

        public List<string> DescribeAll(DateTime now) { lock (sync) return fights.Select(f => f.Describe(now)).ToList(); }
    }
}
