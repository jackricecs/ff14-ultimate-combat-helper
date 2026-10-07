using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using UltimateCombatHelper.Data;

namespace UltimateCombatHelper.Modules.Dot
{
    /// <summary>DoT 提醒的目标过滤方式。</summary>
    public static class DotTargetMode
    {
        /// <summary>只提醒主要敌人（最大 HP ≥ 比例 × 场上最高）。切目标奶人、双 Boss、忽略小怪都适用。默认。</summary>
        public const string Major = "Major";
        /// <summary>只提醒当前选中目标。</summary>
        public const string Current = "Current";
        /// <summary>所有挂了 DoT 的目标都提醒。</summary>
        public const string All = "All";
    }

    [DataContract]
    public sealed class DotDef
    {
        [DataMember(Order = 0)] public string Id;          // 十六进制状态 ID
        [DataMember(Order = 1)] public string Job;         // 仅用于显示/分组
        [DataMember(Order = 2)] public string Note;        // 英文名备注
        [DataMember(Order = 3)] public string Name;        // 播报名；空 = 用游戏内状态名（国服即中文）
        [DataMember(Order = 4)] public double? LeadSeconds; // 空 = 用全局
        [DataMember(Order = 5)] public bool Enabled;

        public static DotDef Of(string id, string job, string note, bool enabled = true)
        {
            return new DotDef { Id = id, Job = job, Note = note, Name = "", Enabled = enabled };
        }
    }

    [DataContract]
    public sealed class DotConfig
    {
        [DataMember(Order = 0)] public bool Enabled;
        [DataMember(Order = 1)] public double LeadSeconds;
        /// <summary>{dot}=DoT名 {target}=目标名 {sec}=剩余秒数</summary>
        [DataMember(Order = 2)] public string SpeechTemplate;
        [DataMember(Order = 3)] public string TargetMode;
        /// <summary>Major 模式下"主要敌人"的 HP 比例阈值（0~1）。</summary>
        [DataMember(Order = 4)] public double MajorHpRatio;
        [DataMember(Order = 5)] public bool OnlyInCombat;
        /// <summary>在哪些副本难度下提醒（ContentTier 名称）。空列表 = 任何地方。</summary>
        [DataMember(Order = 6)] public List<string> Tiers;
        [DataMember(Order = 7)] public List<DotDef> Dots;

        [OnDeserializing] private void OnDeserializing(StreamingContext c) { ScalarDefaults(); }
        [OnDeserialized] private void OnDeserialized(StreamingContext c)
        {
            if (Tiers == null) Tiers = DefaultTiers();
            if (Dots == null) Dots = DefaultDots();
        }

        private void ScalarDefaults()
        {
            Enabled = true;
            LeadSeconds = 4;
            SpeechTemplate = "{dot}快掉了，该补了";
            TargetMode = DotTargetMode.Major;
            MajorHpRatio = 0.5;
            OnlyInCombat = true;
        }

        public static DotConfig CreateDefault()
        {
            var c = new DotConfig();
            c.ScalarDefaults();
            c.Tiers = DefaultTiers();
            c.Dots = DefaultDots();
            return c;
        }

        /// <summary>默认：团本与高难（普通团本、零式、绝、极神、幻巧、诛灭、零式异闻）。</summary>
        public static List<string> DefaultTiers()
        {
            return new List<string> {
                ContentTier.Raid.ToString(), ContentTier.Savage.ToString(), ContentTier.Ultimate.ToString(),
                ContentTier.Extreme.ToString(), ContentTier.Unreal.ToString(), ContentTier.Chaotic.ToString(),
                ContentTier.CriterionSavage.ToString() };
        }

        // 状态 ID 取自 FFXIV_ACT_Plugin 仓库 Definitions/*.json 中 timeproc.type == "dot" 的条目（2026-09）。
        public static List<DotDef> DefaultDots()
        {
            return new List<DotDef> {
                DotDef.Of("F1F", "黑魔法师", "High Thunder"),     DotDef.Of("F20", "黑魔法师", "High Thunder II"),
                DotDef.Of("A3",  "黑魔法师", "Thunder III"),      DotDef.Of("4BA", "黑魔法师", "Thunder IV"),
                DotDef.Of("A1",  "黑魔法师", "Thunder"),          DotDef.Of("A2",  "黑魔法师", "Thunder II"),
                DotDef.Of("74F", "白魔法师", "Dia"),              DotDef.Of("8F",  "白魔法师", "Aero"),
                DotDef.Of("90",  "白魔法师", "Aero II"),
                DotDef.Of("767", "学者", "Biolysis"),             DotDef.Of("BD",  "学者", "Bio II"),
                DotDef.Of("B3",  "学者", "Bio"),                  DotDef.Of("F2B", "学者", "Baneful Impaction", false),
                DotDef.Of("759", "占星术士", "Combust III"),      DotDef.Of("34B", "占星术士", "Combust II"),
                DotDef.Of("346", "占星术士", "Combust"),
                DotDef.Of("A38", "贤者", "Eukrasian Dosis III"),  DotDef.Of("A37", "贤者", "Eukrasian Dosis II"),
                DotDef.Of("A36", "贤者", "Eukrasian Dosis"),      DotDef.Of("F39", "贤者", "Eukrasian Dyskrasia"),
                DotDef.Of("4B0", "吟游诗人", "Caustic Bite"),     DotDef.Of("4B1", "吟游诗人", "Stormbite"),
                DotDef.Of("7C",  "吟游诗人", "Venomous Bite"),    DotDef.Of("81",  "吟游诗人", "Windbite"),
                DotDef.Of("4CC", "武士", "Higanbana"),
                DotDef.Of("A9F", "龙骑士", "Chaotic Spring"),     DotDef.Of("76",  "龙骑士", "Chaos Thrust"),
                DotDef.Of("72D", "绝枪战士", "Sonic Break", false), DotDef.Of("72E", "绝枪战士", "Bow Shock", false),
                DotDef.Of("F8",  "骑士", "Circle of Scorn", false), DotDef.Of("74A", "机工士", "Bioblaster", false),
            };
        }

        public bool TierAllowed(ContentTier t)
        {
            return Tiers == null || Tiers.Count == 0 || Tiers.Contains(t.ToString());
        }

        public Dictionary<string, DotDef> Index()
        {
            var d = new Dictionary<string, DotDef>();
            foreach (var x in Dots ?? new List<DotDef>())
                if (!string.IsNullOrEmpty(x.Id)) d[Logs.Hex.Norm(x.Id)] = x;
            return d;
        }
    }
}
