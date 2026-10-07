using System;
using System.Collections.Generic;
using System.Linq;
using UltimateCombatHelper.Data;
using UltimateCombatHelper.Logs;

namespace UltimateCombatHelper.Context
{
    /// <summary>
    /// 所有模块共享的战斗上下文：我是谁、在哪个副本（难度）、场上敌人的最大 HP。
    /// 由插件在分发给各模块之前先喂日志行，保证模块看到的是最新状态。线程安全。
    /// </summary>
    public sealed class CombatContext
    {
        private readonly object sync = new object();
        private readonly Dictionary<string, EnemyInfo> enemies = new Dictionary<string, EnemyInfo>();
        private long highestCache = -1;

        public string MyId { get; private set; }
        /// <summary>兜底：取不到 ID 时用角色名（ACT 的 ActGlobals.charName）从自己的技能/状态行学习 ID。</summary>
        public string MyName { get; set; }
        public ZoneInfo Zone { get; private set; }
        public bool InCombat { get; set; }

        public event Action<ZoneInfo> ZoneChanged;
        public event Action<string> PlayerChanged;

        public CombatContext()
        {
            MyId = "";
            Zone = ZoneInfo.Unknown(0, "");
        }

        public sealed class EnemyInfo
        {
            public string Id;
            public string Name;
            public long MaxHp;
        }

        public void SetMyId(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            string n = Hex.Norm(id);
            bool changed;
            lock (sync) { changed = n != MyId; MyId = n; }
            if (changed) { var h = PlayerChanged; if (h != null) h(n); }
        }

        public void SetZone(uint territoryId, string name)
        {
            var z = ZoneTable.Get(territoryId, name);
            lock (sync) { Zone = z; enemies.Clear(); highestCache = -1; }
            var h = ZoneChanged; if (h != null) h(z);
        }

        public void OnLogLine(LogLine l)
        {
            switch (l.Type)
            {
                case LogType.ChangeZone: SetZone(l.HexUInt(2), l[3]); break;
                case LogType.ChangePrimaryPlayer: SetMyId(l[2]); break;
                case LogType.AddCombatant: Observe(l.Id(2), l[3], l.Long(12)); break;          // 03: maxHp 在下标 12
                case LogType.RemoveCombatant:
                case LogType.Death: Forget(l.Id(2)); break;
                case LogType.Ability:
                case LogType.AoeAbility:
                    Observe(l.Id(6), l[7], l.Long(25));                                         // 21/22: 目标 maxHp 下标 25
                    LearnMyId(l.Id(2), l[3]);
                    break;
                case LogType.StatusAdd:
                    Observe(l.Id(7), l[8], l.Long(10));                                         // 26: 目标 maxHp 下标 10
                    LearnMyId(l.Id(5), l[6]);
                    break;
            }
        }

        private void LearnMyId(string sourceId, string sourceName)
        {
            if (MyId.Length > 0 || string.IsNullOrEmpty(MyName) || sourceName != MyName || !Hex.IsPlayer(sourceId)) return;
            SetMyId(sourceId);
        }

        private void Observe(string id, string name, long maxHp)
        {
            if (!Hex.IsNpc(id) || maxHp <= 0) return;
            lock (sync)
            {
                EnemyInfo e;
                if (!enemies.TryGetValue(id, out e)) { e = new EnemyInfo { Id = id }; enemies[id] = e; }
                if (!string.IsNullOrEmpty(name)) e.Name = name;
                if (e.MaxHp != maxHp) { e.MaxHp = maxHp; highestCache = -1; }
            }
        }

        private void Forget(string id)
        {
            lock (sync) { if (enemies.Remove(id)) highestCache = -1; }
        }

        public long HighestEnemyMaxHp
        {
            get
            {
                lock (sync)
                {
                    if (highestCache < 0) highestCache = enemies.Count == 0 ? 0 : enemies.Values.Max(e => e.MaxHp);
                    return highestCache;
                }
            }
        }

        public long MaxHpOf(string id)
        {
            lock (sync) { EnemyInfo e; return enemies.TryGetValue(Hex.Norm(id), out e) ? e.MaxHp : 0; }
        }

        /// <summary>
        /// 是否"主要敌人"：最大 HP ≥ ratio × 场上最高最大 HP。
        /// 双 Boss 血量相近 → 都算；小怪血量远低于 Boss → 不算。HP 未知时视为主要（宁可多报不漏报）。
        /// </summary>
        public bool IsMajorEnemy(string id, double ratio)
        {
            long hp = MaxHpOf(id), top = HighestEnemyMaxHp;
            if (hp <= 0 || top <= 0) return true;
            return hp >= ratio * top;
        }

        public List<EnemyInfo> Enemies()
        {
            lock (sync) return enemies.Values.Select(e => new EnemyInfo { Id = e.Id, Name = e.Name, MaxHp = e.MaxHp }).ToList();
        }
    }
}
