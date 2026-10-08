using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UltimateCombatHelper.Data;

namespace UltimateCombatHelper.Modules.Dot
{
    /// <summary>界面上"当前职业 / DoT 剩余时间"一行的内容。</summary>
    public sealed class DotStatusView
    {
        public string Job;
        public string Dot;
        /// <summary>有 DoT 已进入提醒区间（界面标红）。</summary>
        public bool Urgent;
    }

    public static class DotStatus
    {
        /// <summary>配置里属于该职业的 DoT（基础职业按特职算）。</summary>
        public static List<DotDef> DefsForJob(DotConfig cfg, int job)
        {
            if (job == Jobs.Unknown || cfg == null || cfg.Dots == null) return new List<DotDef>();
            string name = Jobs.NameCn(Jobs.ToAdvanced(job));
            return cfg.Dots.Where(d => d.Job == name).ToList();
        }

        public static DotStatusView Describe(int job, DotConfig cfg, IList<DotEntry> active, DateTime now)
        {
            var v = new DotStatusView { Job = job == Jobs.Unknown ? "识别中…（进副本/换职业/打一个技能后出现）" : Jobs.Describe(job) };
            var live = (active ?? new List<DotEntry>()).Where(e => e.Remaining(now) > 0).OrderBy(e => e.ExpiresAt).ToList();
            if (live.Count > 0)
            {
                v.Dot = string.Join("；", live.Take(4).Select(e => string.Format(CultureInfo.InvariantCulture,
                    "{0} → {1} 剩 {2:0.0} 秒", e.DisplayName, e.TargetName, e.Remaining(now))));
                if (live.Count > 4) v.Dot += string.Format("（另有 {0} 个）", live.Count - 4);
                v.Urgent = live.Any(e => e.Remaining(now) <= e.LeadSeconds);
                return v;
            }
            if (job == Jobs.Unknown) { v.Dot = "—"; return v; }
            var defs = DefsForJob(cfg, job);
            if (defs.Count == 0) v.Dot = "暂缺（本职业没有可监控的 DoT）";
            else if (!defs.Any(d => d.Enabled)) v.Dot = "暂缺（本职业的 DoT 在下方列表里都没勾选启用）";
            else v.Dot = "未挂（本职业监控 " + defs.Count(d => d.Enabled) + " 种：" + string.Join("/", defs.Where(d => d.Enabled).Select(d => d.Note)) + "）";
            return v;
        }
    }
}
