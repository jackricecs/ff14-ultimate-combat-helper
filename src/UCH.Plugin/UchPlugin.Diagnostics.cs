// UchPlugin.Diagnostics.cs — 诊断信息里与 ACT 相关的部分：日志格式自检、心跳、ACT 遭遇信息、诊断包的环境/状态。
using System;
using System.Globalization;
using System.Linq;
using Advanced_Combat_Tracker;
using UltimateCombatHelper.Data;
using UltimateCombatHelper.Diagnostics;
using UltimateCombatHelper.Logs;
using UltimateCombatHelper.Modules;
using UltimateCombatHelper.Modules.Dot;

namespace UltimateCombatHelper.Plugin
{
    public sealed partial class UchPlugin
    {
        private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

        private bool firstLineNoted, badFormatNoted, mismatchNoted;
        private int lineErrors;
        private DateTime lastHeartbeat = DateTime.MinValue;
        private long heartbeatTotal, heartbeatStatus;

        /// <summary>第一次收到日志行、行格式异常、detectedType 与行首类型不一致时各记一次。</summary>
        private void NoteLineFormat(int detectedType, int prefixType, string logLine)
        {
            var log = diagnostics.Log;
            if (!firstLineNoted)
            {
                firstLineNoted = true;
                log.Info("lines", string.Format("收到第一条日志行（detectedType={0}，行首类型={1}）", detectedType, prefixType));
            }
            if (prefixType < 0 && !badFormatNoted)
            {
                badFormatNoted = true;
                // 只记开头几个字符：这类行可能是聊天内容
                log.Info("lines", "originalLogLine 不是「类型|时间|…」格式，这类行会被忽略。logLine 开头：" + Truncate(logLine, 12) + "（长度 " + (logLine ?? "").Length + "）");
            }
            if (prefixType >= 0 && detectedType != prefixType && !mismatchNoted && ModuleManager.WantsType(prefixType))
            {
                mismatchNoted = true;
                log.Info("lines", string.Format("注意：ACT detectedType={0} 与日志行类型 {1} 不一致（已按行首类型处理）", detectedType, prefixType));
            }
        }

        private void NoteLineError(Exception ex, LogLineEventArgs logInfo)
        {
            int n = ++lineErrors;
            if (n <= 5 || n % 1000 == 0)
                diagnostics.Log.Error("lines", string.Format("处理日志行出错（第 {0} 次）：{1}\n  行：{2}", n, ex, Truncate(logInfo.originalLogLine, 300)));
        }

        private void ResetHeartbeat()
        {
            lastHeartbeat = DateTime.Now;
            heartbeatTotal = diagnostics.Lines.Total;
            heartbeatStatus = diagnostics.Lines.CountOf(LogType.StatusAdd);
        }

        /// <summary>战斗中每 30 秒记一条状态，确认日志行在流入、DoT 在被跟踪。</summary>
        private void Heartbeat()
        {
            DateTime now = DateTime.Now;
            if (!wasInCombat || now - lastHeartbeat < HeartbeatInterval) return;
            lastHeartbeat = now;
            var ctx = manager.Context;
            long total = diagnostics.Lines.Total, status = diagnostics.Lines.CountOf(LogType.StatusAdd);
            diagnostics.Log.Debug("heartbeat", string.Format(CultureInfo.InvariantCulture,
                "角色 {0} | 职业 {1} | 区域 {2}（{3}）| 跟踪 DoT {4} 个 | 近 30 秒收到 {5} 行，其中 26 行 {6} 条",
                ctx.MyId.Length == 0 ? "未知" : ctx.MyId, ctx.MyJob == Jobs.Unknown ? "未知" : Jobs.Describe(ctx.MyJob),
                ctx.Zone.Id == 0 ? "未知" : ctx.Zone.NameCn, ZoneTable.DisplayName(ctx.Zone.Tier),
                dot.Tracker == null ? 0 : dot.Tracker.Snapshot().Count, total - heartbeatTotal, status - heartbeatStatus));
            heartbeatTotal = total; heartbeatStatus = status;
        }

        /// <summary>脱战时从 ACT 读取刚结束的遭遇：名字、时长、队伍 DPS。</summary>
        private void EnrichFight(FightSummary f)
        {
            var zone = ActGlobals.oFormActMain.ActiveZone;
            var ed = zone == null ? null : zone.ActiveEncounter;
            if (ed == null) return;
            f.ActInfo.Add(string.Format(CultureInfo.InvariantCulture, "ACT 遭遇：{0}　时长 {1}　总伤害 {2:N0}　区域 {3}",
                ed.Title, ed.DurationS, ed.Damage, ed.ZoneName));
            foreach (var a in ed.GetAllies().OrderByDescending(a => a.EncDPS).Take(8))
                f.ActInfo.Add(string.Format(CultureInfo.InvariantCulture, "{0}：DPS {1:0}　伤害 {2:N0}　暴击 {3}/{4}　死亡 {5}",
                    a.Name, a.EncDPS, a.Damage, a.CritHits, a.Hits, a.Deaths));
        }

        /// <summary>诊断包的环境与当前状态（导出按钮在界面线程调用；自动保存在线程池调用，读不到的项写出原因）。</summary>
        private void FillReport(DiagnosticReport r)
        {
            var form = ActGlobals.oFormActMain;
            var ctx = manager.Context;
            r.MyName = ActGlobals.charName;
            r.MyId = ctx.MyId;
            r.ConfigJson = Safe(() => Config.ToJson());

            r.Env("插件版本", Version);
            r.Env("ACT 版本", Safe(() => form.GetType().Assembly.GetName().Version.ToString()));
            r.Env(".NET CLR", Environment.Version);
            r.Env("操作系统", Environment.OSVersion + (Environment.Is64BitProcess ? "（64 位进程）" : "（32 位进程）"));
            r.Env("解析插件", bridge.Description ?? (bridge.IsAttached ? "已连接" : "未连接（「仅当前目标」与 SDK 识别角色/职业不可用）"));
            r.Env("ACT 插件", Safe(() => string.Join("；", form.ActPlugins.ToList().Select(p =>
                (p.pluginFile == null ? "?" : p.pluginFile.Name) + " " + p.pluginVersion + (p.cbEnabled.Checked ? "" : "（未启用）")))));
            Safe(() => { FillState(r); return null; });
        }

        /// <summary>当前状态各行；单独 Safe 包裹，出错不影响上面的环境信息。</summary>
        private void FillState(DiagnosticReport r)
        {
            var ctx = manager.Context;

            r.State.Add("角色名（ACT）：" + ActGlobals.charName);
            r.State.Add("角色 ID：" + (ctx.MyId.Length == 0 ? "未知" : ctx.MyId));
            r.State.Add("职业：" + (ctx.MyJob == Jobs.Unknown ? "未知" : Jobs.Describe(ctx.MyJob) + "（来源：" + ctx.MyJobSource + "）"));
            r.State.Add(string.Format("区域：{0}（ID {1}，难度 {2}）", ctx.Zone.NameCn, ctx.Zone.Id, ZoneTable.DisplayName(ctx.Zone.Tier)));
            r.State.Add("ACT 战斗中：" + (ctx.InCombat ? "是" : "否") + "　插件总开关：" + (Config.General.Enabled ? "开" : "关")
                        + "　测试模式：" + (Config.General.TestModeAnywhere ? "开" : "关"));
            r.State.Add("当前选中目标：" + (Safe(GetCurrentTarget) ?? "无/未知"));
            double ratio = Config.Dot.MajorHpRatio;
            r.State.Add("场上敌人：" + string.Join("；", ctx.Enemies().OrderByDescending(e => e.MaxHp).Take(15).Select(e =>
                string.Format(CultureInfo.InvariantCulture, "{0}({1}) HP {2:N0}{3}", e.Name, e.Id, e.MaxHp, ctx.IsMajorEnemy(e.Id, ratio) ? " 主要" : ""))));
            var now = DateTime.Now;
            var active = dot.Tracker == null ? new System.Collections.Generic.List<DotEntry>() : dot.Tracker.Snapshot();
            r.State.Add("正在跟踪的 DoT：" + (active.Count == 0 ? "无" : string.Join("；", active.Select(e => string.Format(CultureInfo.InvariantCulture,
                "{0}({1}) → {2}({3}) 剩 {4:0.0}s 提前 {5}s{6}", e.DisplayName, e.EffectId, e.TargetName, e.TargetId, e.Remaining(now), e.LeadSeconds, e.Warned ? " 已处理" : "")))));
            var v = DotStatus.Describe(ctx.MyJob, Config.Dot, active, now);
            r.State.Add("界面状态：职业 " + v.Job + "　DoT " + v.Dot);
        }

        private static string Safe(Func<string> f)
        {
            try { return f(); } catch (Exception ex) { return "（读取失败：" + ex.Message + "）"; }
        }

        private static string Truncate(string s, int n)
        {
            if (s == null) return "(null)";
            return s.Length <= n ? s : s.Substring(0, n) + "…";
        }
    }
}
