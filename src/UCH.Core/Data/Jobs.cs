namespace UltimateCombatHelper.Data
{
    /// <summary>职业 ID（ClassJob 表）→ 中文名 / 缩写。03 行 job 字段（十六进制）、12 行（十进制）、SDK Combatant.Job 用的都是这个 ID。</summary>
    public static class Jobs
    {
        public const int Unknown = -1;

        private static readonly string[] Cn = {
            "冒险者", "剑术师", "格斗家", "斧术师", "枪术师", "弓箭手", "幻术师", "咒术师",
            "刻木匠", "锻铁匠", "铸甲匠", "雕金匠", "制革匠", "裁衣匠", "炼金术士", "烹调师",
            "采矿工", "园艺工", "捕鱼人",
            "骑士", "武僧", "战士", "龙骑士", "吟游诗人", "白魔法师", "黑魔法师",
            "秘术师", "召唤师", "学者", "双剑师", "忍者", "机工士", "暗黑骑士", "占星术士",
            "武士", "赤魔法师", "青魔法师", "绝枪战士", "舞者", "钐镰客", "贤者", "蝰蛇剑士", "绘灵法师",
        };

        private static readonly string[] Abbr = {
            "ADV", "GLA", "PGL", "MRD", "LNC", "ARC", "CNJ", "THM",
            "CRP", "BSM", "ARM", "GSM", "LTW", "WVR", "ALC", "CUL",
            "MIN", "BTN", "FSH",
            "PLD", "MNK", "WAR", "DRG", "BRD", "WHM", "BLM",
            "ACN", "SMN", "SCH", "ROG", "NIN", "MCH", "DRK", "AST",
            "SAM", "RDM", "BLU", "GNB", "DNC", "RPR", "SGE", "VPR", "PCT",
        };

        public static bool IsKnown(int id) { return id >= 0 && id < Cn.Length; }

        /// <summary>合理范围（新职业上线前也接受，只是没有名字）。</summary>
        public static bool IsPlausible(long id) { return id >= 0 && id < 100; }

        public static string NameCn(int id)
        {
            if (id == Unknown) return "";
            return IsKnown(id) ? Cn[id] : "职业#" + id;
        }

        public static string Abbreviation(int id) { return IsKnown(id) ? Abbr[id] : ""; }

        /// <summary>"黑魔法师(BLM)"；未知返回空串。</summary>
        public static string Describe(int id)
        {
            if (id == Unknown) return "";
            return IsKnown(id) ? Cn[id] + "(" + Abbr[id] + ")" : NameCn(id);
        }

        /// <summary>基础职业 → 对应特职（DoT 配置按特职名分组，咒术师的雷也算黑魔法师）。</summary>
        public static int ToAdvanced(int id)
        {
            switch (id)
            {
                case 1: return 19;   // 剑术师 → 骑士
                case 2: return 20;   // 格斗家 → 武僧
                case 3: return 21;   // 斧术师 → 战士
                case 4: return 22;   // 枪术师 → 龙骑士
                case 5: return 23;   // 弓箭手 → 吟游诗人
                case 6: return 24;   // 幻术师 → 白魔法师
                case 7: return 25;   // 咒术师 → 黑魔法师
                case 26: return 28;  // 秘术师 → 学者（毒菌）
                case 29: return 30;  // 双剑师 → 忍者
                default: return id;
            }
        }
    }
}
