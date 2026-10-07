using System;
using UltimateCombatHelper.Context;
using UltimateCombatHelper.Logs;
using UltimateCombatHelper.Notify;

namespace UltimateCombatHelper.Modules
{
    /// <summary>模块运行所需的宿主服务。插件提供真实实现，测试提供假实现。</summary>
    public interface IModuleHost
    {
        CombatContext Context { get; }
        ThrottledNotifier Notifier { get; }
        Func<DateTime> Clock { get; }
        /// <summary>玩家当前选中目标 ID（十六进制）；无目标或未知返回 null。</summary>
        string GetCurrentTargetId();
    }

    /// <summary>
    /// 功能模块。新增功能 = 新增一个 IModule 实现 + 一个配置节 + 一个设置页。
    /// 生命周期：Attach → (OnLogLine / OnTick)* → Detach。
    /// OnLogLine 在 ACT 日志线程调用，OnTick 约每 100ms 在计时器线程调用；实现需自行保证线程安全。
    /// </summary>
    public interface IModule
    {
        string Id { get; }
        string DisplayName { get; }
        void Attach(IModuleHost host);
        void OnLogLine(LogLine line);
        void OnTick();
        void Detach();
    }
}
