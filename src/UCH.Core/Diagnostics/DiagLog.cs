using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace UltimateCombatHelper.Diagnostics
{
    /// <summary>数值越大越详细。配置里的级别以上（更详细）的条目不保存。</summary>
    public enum DiagLevel { Error = 0, Info = 1, Debug = 2, Trace = 3 }

    public sealed class DiagEntry
    {
        public readonly DateTime Time;
        public readonly DiagLevel Level;
        /// <summary>来源/类别，如 "dot.warn"、"context"、"plugin"。</summary>
        public readonly string Category;
        public readonly string Message;
        /// <summary>可选的机器可读标签（如抑制原因代码），用于战斗统计。</summary>
        public readonly string Tag;

        public DiagEntry(DateTime time, DiagLevel level, string category, string message, string tag)
        {
            Time = time; Level = level; Category = category ?? ""; Message = message ?? ""; Tag = tag;
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:HH:mm:ss.fff} [{1}] [{2}] {3}",
                Time, LevelName(Level), Category, Message);
        }

        public static string LevelName(DiagLevel l)
        {
            switch (l)
            {
                case DiagLevel.Error: return "错误";
                case DiagLevel.Info: return "信息";
                case DiagLevel.Debug: return "调试";
                default: return "追踪";
            }
        }
    }

    /// <summary>
    /// 插件运行日志：环形缓冲（只保留最近 Capacity 条），线程安全。
    /// Emitted 对所有写入触发（不受级别影响，供战斗统计计数）；Written 只对保存下来的条目触发（供界面显示）。
    /// </summary>
    public sealed class DiagLog
    {
        private readonly object sync = new object();
        private readonly Queue<DiagEntry> entries = new Queue<DiagEntry>();
        private readonly Func<DateTime> clock;
        private int capacity;

        public DiagLevel Level { get; set; }

        public event Action<DiagEntry> Emitted;
        public event Action<DiagEntry> Written;

        public DiagLog(Func<DateTime> clock = null, int capacity = 5000)
        {
            this.clock = clock ?? (() => DateTime.Now);
            Capacity = capacity;
            Level = DiagLevel.Debug;
        }

        public int Capacity
        {
            get { return capacity; }
            set { lock (sync) { capacity = Math.Max(100, value); Trim(); } }
        }

        public bool Enabled(DiagLevel l) { return l <= Level; }

        public void Write(DiagLevel level, string category, string message, string tag = null)
        {
            var e = new DiagEntry(clock(), level, category, message, tag);
            var em = Emitted; if (em != null) em(e);
            if (!Enabled(level)) return;
            lock (sync) { entries.Enqueue(e); Trim(); }
            var w = Written; if (w != null) w(e);
        }

        public void Error(string category, string message) { Write(DiagLevel.Error, category, message); }
        public void Info(string category, string message, string tag = null) { Write(DiagLevel.Info, category, message, tag); }
        public void Debug(string category, string message, string tag = null) { Write(DiagLevel.Debug, category, message, tag); }
        public void Trace(string category, string message) { Write(DiagLevel.Trace, category, message); }

        private void Trim() { while (entries.Count > capacity) entries.Dequeue(); }

        public List<DiagEntry> Snapshot(DateTime? since = null)
        {
            lock (sync)
                return since.HasValue ? entries.Where(e => e.Time >= since.Value).ToList() : entries.ToList();
        }

        public int Count { get { lock (sync) return entries.Count; } }

        public void Clear() { lock (sync) entries.Clear(); }

        public static DiagLevel ParseLevel(string s, DiagLevel fallback = DiagLevel.Debug)
        {
            DiagLevel l;
            return Enum.TryParse(s ?? "", true, out l) && Enum.IsDefined(typeof(DiagLevel), l) ? l : fallback;
        }
    }
}
