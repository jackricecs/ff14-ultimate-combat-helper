using System;
using System.Globalization;

namespace UltimateCombatHelper.Logs
{
    /// <summary>网络日志行类型（十进制），字段定义见 cactbot docs/LogGuide.md。</summary>
    public static class LogType
    {
        public const int ChangeZone = 1;
        public const int ChangePrimaryPlayer = 2;
        public const int AddCombatant = 3;
        public const int RemoveCombatant = 4;
        public const int StartsCasting = 20;
        public const int Ability = 21;
        public const int AoeAbility = 22;
        public const int Death = 25;
        public const int StatusAdd = 26;
        public const int StatusRemove = 30;
        public const int Gauge = 31;
        public const int InCombat = 260;
    }

    /// <summary>一条网络日志行（"类型|时间戳|字段..."），对应 ACT LogLineEventArgs.originalLogLine。</summary>
    public sealed class LogLine
    {
        private static readonly char[] Pipe = { '|' };

        public readonly int Type;
        public readonly string[] Fields;
        public readonly DateTime ReceivedAt;

        public LogLine(int type, string[] fields, DateTime receivedAt)
        {
            Type = type; Fields = fields; ReceivedAt = receivedAt;
        }

        public static LogLine Parse(string raw, DateTime receivedAt)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            string[] f = raw.Split(Pipe);
            int type;
            if (f.Length < 2 || !int.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out type)) return null;
            return new LogLine(type, f, receivedAt);
        }

        public int Count { get { return Fields.Length; } }
        public string this[int i] { get { return i < Fields.Length ? Fields[i] : ""; } }

        /// <summary>规范化的十六进制 ID（大写、去前导 0）。</summary>
        public string Id(int i) { return Hex.Norm(this[i]); }

        public double Double(int i)
        {
            double v;
            return double.TryParse(this[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        public long Long(int i)
        {
            long v;
            return long.TryParse(this[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        public uint HexUInt(int i)
        {
            uint v;
            return uint.TryParse(this[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v) ? v : 0;
        }
    }

    public static class Hex
    {
        private static readonly char[] Zero = { '0' };   // .NET Framework 只有 TrimStart(char[])

        public static string Norm(string id)
        {
            string s = (id ?? "").Trim().ToUpperInvariant();
            if (s.StartsWith("0X", StringComparison.Ordinal)) s = s.Substring(2);
            s = s.TrimStart(Zero);
            return s.Length == 0 ? "0" : s;
        }

        /// <summary>对象 ID：1 开头为玩家，4 开头为 NPC/敌人（规范化后）。</summary>
        public static bool IsPlayer(string normId) { return normId.Length == 8 && normId[0] == '1'; }
        public static bool IsNpc(string normId) { return normId.Length == 8 && normId[0] == '4'; }
    }
}
