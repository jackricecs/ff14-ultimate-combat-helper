using System;
using System.Collections.Generic;
using System.Linq;
using UltimateCombatHelper.Logs;

namespace UltimateCombatHelper.Diagnostics
{
    public sealed class RecordedLine
    {
        public readonly DateTime Time;
        public readonly int Type;
        public readonly string Raw;
        public RecordedLine(DateTime time, int type, string raw) { Time = time; Type = type; Raw = raw; }
    }

    /// <summary>每种 ACT detectedType 的前两条原始样本（排查日志格式问题用）。</summary>
    public sealed class LineSample
    {
        public int DetectedType;
        public int PrefixType;
        public string LogLine;
        public string OriginalLogLine;
    }

    /// <summary>
    /// 记录战斗日志行（环形缓冲）并统计所有收到的行，用于导出诊断包。线程安全。
    /// 默认只记录与"我"相关的技能/状态行，避免 8 人本里他人的海量技能行把缓冲挤满。
    /// </summary>
    public sealed class LineRecorder
    {
        private const int SamplesPerType = 2;

        /// <summary>会被记录的行类型。</summary>
        public static readonly HashSet<int> RecordTypes = new HashSet<int> {
            LogType.ChangeZone, LogType.ChangePrimaryPlayer, LogType.AddCombatant, LogType.RemoveCombatant,
            LogType.PlayerStats, LogType.StartsCasting, LogType.Ability, LogType.AoeAbility, LogType.Death,
            LogType.StatusAdd, LogType.StatusRemove, LogType.InCombat,
        };

        private readonly object sync = new object();
        private readonly Queue<RecordedLine> lines = new Queue<RecordedLine>();
        private readonly Dictionary<int, long> byPrefixType = new Dictionary<int, long>();
        private readonly Dictionary<long, long> typeMismatch = new Dictionary<long, long>();
        private readonly Dictionary<string, string> players = new Dictionary<string, string>(StringComparer.Ordinal);
        private const int MaxPlayers = 3000;
        private readonly Dictionary<int, List<LineSample>> samples = new Dictionary<int, List<LineSample>>();
        private int capacity = 20000;
        private long total, emptyOriginal;

        public bool Enabled { get; set; }
        /// <summary>同时记录其他玩家的技能/状态行（文件会大很多）。</summary>
        public bool RecordOthers { get; set; }
        public DateTime? FirstLineAt { get; private set; }

        public LineRecorder() { Enabled = true; }

        public int Capacity
        {
            get { return capacity; }
            set { lock (sync) { capacity = Math.Max(1000, value); Trim(); } }
        }

        /// <summary>每一行都调用（在解析之前）：计数、记录 detectedType 与行首类型是否一致、采样。</summary>
        public void Observe(int detectedType, int prefixType, string logLine, string originalLogLine)
        {
            lock (sync)
            {
                total++;
                if (!FirstLineAt.HasValue) FirstLineAt = DateTime.Now;
                if (string.IsNullOrEmpty(originalLogLine)) emptyOriginal++;
                Inc(byPrefixType, prefixType);
                if (detectedType != prefixType)
                {
                    long k = ((long)detectedType << 32) | (uint)prefixType;   // 热路径上不拼字符串
                    long n; typeMismatch.TryGetValue(k, out n); typeMismatch[k] = n + 1;
                }
                if (prefixType == LogType.Chat || (prefixType < 0 && detectedType == LogType.Chat)) return;   // 聊天内容不采样（隐私）
                List<LineSample> s;
                if (!samples.TryGetValue(detectedType, out s)) { s = new List<LineSample>(); samples[detectedType] = s; }
                if (s.Count < SamplesPerType)
                    s.Add(new LineSample { DetectedType = detectedType, PrefixType = prefixType, LogLine = logLine, OriginalLogLine = originalLogLine });
            }
        }

        /// <summary>对已解析的行决定是否记录。</summary>
        public void Offer(LogLine l, string raw, string myId)
        {
            if (l == null || !RecordTypes.Contains(l.Type)) return;
            RememberPlayers(l);
            if (!Enabled || (!RecordOthers && !InvolvesMe(l, myId))) return;
            var r = new RecordedLine(l.ReceivedAt, l.Type, raw);
            lock (sync) { lines.Enqueue(r); Trim(); }
        }

        /// <summary>技能/状态/读条/死亡行：来源或目标是我才算；其他类型都算。不知道自己 ID 时全部算（这本身就是要排查的情况）。</summary>
        public static bool InvolvesMe(LogLine l, string myId)
        {
            if (string.IsNullOrEmpty(myId)) return true;
            switch (l.Type)
            {
                case LogType.StartsCasting:
                case LogType.Ability:
                case LogType.AoeAbility: return l.Id(2) == myId || l.Id(6) == myId;
                case LogType.StatusAdd:
                case LogType.StatusRemove: return l.Id(5) == myId || l.Id(7) == myId;
                case LogType.Death: return l.Id(2) == myId || l.Id(4) == myId;
                default: return true;
            }
        }

        /// <summary>记下出现过的玩家（ID→名字），导出时据此脱敏——即使对应的行已被挤出缓冲或没被记录。</summary>
        private void RememberPlayers(LogLine l)
        {
            int[][] pairs;
            if (!NameMasker.NamePairs.TryGetValue(l.Type, out pairs)) return;
            foreach (var p in pairs)
            {
                string id = l[p[0]], name = l[p[1]];
                if (id.Length != 8 || id[0] != '1' || name.Length == 0) continue;   // 玩家 ID：10xxxxxx（原样，不做规范化以免分配）
                lock (sync) { if (players.Count < MaxPlayers && !players.ContainsKey(name)) players[name] = id.ToUpperInvariant(); }
            }
        }

        public Dictionary<string, string> KnownPlayers() { lock (sync) return new Dictionary<string, string>(players); }

        private static void Inc(Dictionary<int, long> d, int k) { long n; d.TryGetValue(k, out n); d[k] = n + 1; }
        private void Trim() { while (lines.Count > capacity) lines.Dequeue(); }

        public List<RecordedLine> Lines(DateTime? since = null, DateTime? until = null)
        {
            RecordedLine[] copy;
            lock (sync) copy = lines.ToArray();   // 锁内只复制，过滤放到锁外，避免导出时挡住日志线程
            return copy.Where(x => (!since.HasValue || x.Time >= since.Value) && (!until.HasValue || x.Time <= until.Value)).ToList();
        }

        public int LineCount { get { lock (sync) return lines.Count; } }
        public long Total { get { lock (sync) return total; } }
        public long EmptyOriginal { get { lock (sync) return emptyOriginal; } }

        public Dictionary<int, long> CountsByType() { lock (sync) return new Dictionary<int, long>(byPrefixType); }
        public long CountOf(int type) { lock (sync) { long n; return byPrefixType.TryGetValue(type, out n) ? n : 0; } }
        /// <summary>"detectedType→行首类型" → 次数。</summary>
        public Dictionary<string, long> Mismatches()
        {
            lock (sync) return typeMismatch.ToDictionary(kv => (kv.Key >> 32) + "→" + (int)(kv.Key & 0xFFFFFFFF), kv => kv.Value);
        }

        public List<LineSample> Samples()
        {
            lock (sync) return samples.OrderBy(kv => kv.Key).SelectMany(kv => kv.Value).ToList();
        }

        public void ClearLines() { lock (sync) lines.Clear(); }
    }
}
