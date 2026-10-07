using System.Collections.Generic;

namespace UltimateCombatHelper.Data
{
    /// <summary>副本难度分类。由 tools/gen_zone_table.py 根据 cactbot 数据生成区域映射。</summary>
    public enum ContentTier
    {
        Other = 0,        // 野外、主城、木人、未知区域
        Dungeon,          // 四人副本
        Trial,            // 普通歼灭战
        Extreme,          // 歼殛战（极神）
        Unreal,           // 幻巧战
        Raid,             // 普通大型任务/团本（含 24 人）
        Savage,           // 零式
        Ultimate,         // 绝境战
        Chaotic,          // 诛灭战
        Criterion,        // 异闻
        CriterionSavage,  // 零式异闻
    }

    public sealed class ZoneInfo
    {
        public readonly uint Id;
        public readonly ContentTier Tier;
        public readonly string NameEn;
        public readonly string NameCn;

        public ZoneInfo(uint id, ContentTier tier, string en, string cn)
        {
            Id = id; Tier = tier; NameEn = en; NameCn = cn;
        }

        public static ZoneInfo Unknown(uint id, string name)
        {
            return new ZoneInfo(id, ContentTier.Other, name ?? "", name ?? "");
        }
    }

    public static partial class ZoneTable
    {
        public static ZoneInfo Get(uint territoryId, string fallbackName = null)
        {
            ZoneInfo z;
            return Zones.TryGetValue(territoryId, out z) ? z : ZoneInfo.Unknown(territoryId, fallbackName);
        }

        public static int Count { get { return Zones.Count; } }

        /// <summary>高难：零式、绝、极神、幻巧、诛灭、零式异闻。</summary>
        public static bool IsHighEnd(ContentTier t)
        {
            return t == ContentTier.Savage || t == ContentTier.Ultimate || t == ContentTier.Extreme ||
                   t == ContentTier.Unreal || t == ContentTier.Chaotic || t == ContentTier.CriterionSavage;
        }

        public static string DisplayName(ContentTier t)
        {
            switch (t)
            {
                case ContentTier.Dungeon: return "四人副本";
                case ContentTier.Trial: return "普通歼灭战";
                case ContentTier.Extreme: return "极神";
                case ContentTier.Unreal: return "幻巧战";
                case ContentTier.Raid: return "普通团本";
                case ContentTier.Savage: return "零式";
                case ContentTier.Ultimate: return "绝本";
                case ContentTier.Chaotic: return "诛灭战";
                case ContentTier.Criterion: return "异闻";
                case ContentTier.CriterionSavage: return "零式异闻";
                default: return "其他（野外/木人）";
            }
        }

        public static IEnumerable<ContentTier> AllTiers()
        {
            return (ContentTier[])System.Enum.GetValues(typeof(ContentTier));
        }
    }
}
