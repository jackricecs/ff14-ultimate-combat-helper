using System;
using System.Collections.Generic;
using System.Threading;
using UltimateCombatHelper.Context;
using UltimateCombatHelper.Diagnostics;
using UltimateCombatHelper.Logs;
using UltimateCombatHelper.Notify;

namespace UltimateCombatHelper.Modules
{
    /// <summary>
    /// 把日志行先交给 CombatContext，再分发给各模块；统一驱动 OnTick；隔离模块异常。
    /// 与 ACT 无关，插件和测试共用。
    /// </summary>
    public sealed class ModuleManager : IModuleHost
    {
        private readonly List<IModule> modules = new List<IModule>();
        private readonly Func<string> targetProvider;
        private int ticking;

        public CombatContext Context { get; private set; }
        public ThrottledNotifier Notifier { get; private set; }
        public Func<DateTime> Clock { get; private set; }
        public DiagLog Diag { get; private set; }

        /// <summary>只有这些类型会被解析和分发；模块需要新类型时加到这里。</summary>
        public static readonly HashSet<int> InterestingTypes = new HashSet<int> {
            LogType.ChangeZone, LogType.ChangePrimaryPlayer, LogType.AddCombatant, LogType.RemoveCombatant,
            LogType.PlayerStats, LogType.Ability, LogType.AoeAbility, LogType.Death, LogType.StatusAdd, LogType.StatusRemove,
        };

        public ModuleManager(INotifier notifier, Func<string> currentTargetProvider, Func<DateTime> clock = null, DiagLog diag = null)
        {
            Clock = clock ?? (() => DateTime.Now);
            Diag = diag ?? new DiagLog(Clock);
            Context = new CombatContext();
            Notifier = new ThrottledNotifier(notifier, Clock);
            targetProvider = currentTargetProvider;
        }

        public string GetCurrentTargetId()
        {
            try { return targetProvider == null ? null : targetProvider(); } catch (Exception) { return null; }
        }

        public void Add(IModule m) { m.Attach(this); lock (modules) modules.Add(m); }

        public IList<IModule> Modules { get { lock (modules) return modules.ToArray(); } }

        /// <summary>快速判断是否需要处理（在拆分字符串之前调用）。</summary>
        public static bool WantsType(int detectedType) { return InterestingTypes.Contains(detectedType); }

        public void Dispatch(LogLine l)
        {
            if (l == null) return;
            Context.OnLogLine(l);
            foreach (var m in Modules)
            {
                try { m.OnLogLine(l); }
                catch (Exception ex) { ReportError(m, "处理日志出错", ex, l); }
            }
        }

        public void Tick()
        {
            if (Interlocked.Exchange(ref ticking, 1) == 1) return;   // 防重入
            try
            {
                foreach (var m in Modules)
                {
                    try { m.OnTick(); }
                    catch (Exception ex) { ReportError(m, "定时处理出错", ex, null); }
                }
            }
            finally { Interlocked.Exchange(ref ticking, 0); }
        }

        private readonly Dictionary<string, DateTime> lastError = new Dictionary<string, DateTime>();

        /// <summary>同一个错误 10 秒内只记一次（每 100ms 抛一次的异常不能刷爆日志）。</summary>
        private void ReportError(IModule m, string what, Exception ex, LogLine l)
        {
            string key = m.Id + "|" + what + "|" + ex.GetType().Name + "|" + ex.Message;
            DateTime now = Clock(), t;
            lock (lastError)
            {
                if (lastError.TryGetValue(key, out t) && now - t < TimeSpan.FromSeconds(10)) return;
                if (lastError.Count > 200) lastError.Clear();   // 异常信息里常带变化的数值，防止无限增长
                lastError[key] = now;
            }
            Notifier.Log("[" + m.Id + "] " + what + "：" + ex.Message);
            Diag.Error(m.Id, what + "：" + ex + (l == null ? "" : "\n  日志行：" +string.Join("|", l.Fields)));
        }

        public void DetachAll()
        {
            foreach (var m in Modules) { try { m.Detach(); } catch (Exception) { } }
            lock (modules) modules.Clear();
        }
    }
}
