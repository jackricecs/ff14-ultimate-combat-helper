using System;
using System.Collections.Generic;
using System.Linq;
using UltimateCombatHelper.Logs;

namespace UltimateCombatHelper.Diagnostics
{
    /// <summary>
    /// 导出诊断包时把玩家角色名替换成"我 / 玩家1 / 玩家2…"。NPC/敌人名字保留（排查需要）。
    /// 先用 Learn 扫一遍日志行收集 (玩家ID, 名字)，再用 Mask 处理任意文本。
    /// </summary>
    public sealed class NameMasker
    {
        public const string MeAlias = "<我>";

        // 各类型行里 (ID 下标, 名字下标) 的位置，见 cactbot LogGuide
        internal static readonly Dictionary<int, int[][]> NamePairs = new Dictionary<int, int[][]> {
            { LogType.ChangePrimaryPlayer, new[] { new[] { 2, 3 } } },
            { LogType.AddCombatant,        new[] { new[] { 2, 3 } } },
            { LogType.RemoveCombatant,     new[] { new[] { 2, 3 } } },
            { LogType.StartsCasting,       new[] { new[] { 2, 3 }, new[] { 6, 7 } } },
            { LogType.Ability,             new[] { new[] { 2, 3 }, new[] { 6, 7 } } },
            { LogType.AoeAbility,          new[] { new[] { 2, 3 }, new[] { 6, 7 } } },
            { LogType.Death,               new[] { new[] { 2, 3 }, new[] { 4, 5 } } },
            { LogType.StatusAdd,           new[] { new[] { 5, 6 }, new[] { 7, 8 } } },
            { LogType.StatusRemove,        new[] { new[] { 5, 6 }, new[] { 7, 8 } } },
        };

        private readonly Dictionary<string, string> alias = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly string myId;
        private int next = 1;
        private string[] ordered;

        public NameMasker(string myName, string myId)
        {
            this.myId = myId ?? "";
            if (!string.IsNullOrEmpty(myName)) alias[myName] = MeAlias;
        }

        public void Learn(LogLine l)
        {
            int[][] pairs;
            if (l == null || !NamePairs.TryGetValue(l.Type, out pairs)) return;
            foreach (var p in pairs)
            {
                string id = l.Id(p[0]), name = l[p[1]];
                if (!Hex.IsPlayer(id) || string.IsNullOrEmpty(name) || alias.ContainsKey(name)) continue;
                alias[name] = id == myId && myId.Length > 0 ? MeAlias : "玩家" + next++;
                ordered = null;
            }
        }

        public void Add(string name, string id)
        {
            if (string.IsNullOrEmpty(name) || alias.ContainsKey(name)) return;
            alias[name] = Hex.Norm(id) == myId && myId.Length > 0 ? MeAlias : "玩家" + next++;
            ordered = null;
        }

        /// <summary>额外要替换的固定文本（如 Windows 用户目录 → %USERPROFILE%）。</summary>
        public void AddLiteral(string text, string replacement)
        {
            if (string.IsNullOrEmpty(text) || alias.ContainsKey(text)) return;
            alias[text] = replacement;
            ordered = null;
        }

        public int Known { get { return alias.Count; } }

        /// <summary>替换文本中所有已知玩家名（长名字优先，避免"阿A"被"阿"先替换掉一半）。</summary>
        public string Mask(string text)
        {
            if (string.IsNullOrEmpty(text) || alias.Count == 0) return text;
            if (ordered == null) ordered = alias.Keys.Where(k => k.Length >= 2).OrderByDescending(k => k.Length).ToArray();
            foreach (var name in ordered)
                if (text.IndexOf(name, StringComparison.Ordinal) >= 0) text = text.Replace(name, alias[name]);
            return text;
        }
    }
}
