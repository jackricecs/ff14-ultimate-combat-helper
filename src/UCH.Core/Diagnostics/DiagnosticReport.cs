using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UltimateCombatHelper.Logs;

namespace UltimateCombatHelper.Diagnostics
{
    /// <summary>
    /// 诊断包（纯文本）：环境、当前状态、战斗摘要、日志行统计与样本、配置、运行日志、战斗日志行。
    /// 插件负责收集与 ACT 相关的部分（环境/状态），这里只负责组装与脱敏。
    /// </summary>
    public sealed class DiagnosticReport
    {
        public string Title = "诊断包";
        public readonly List<KeyValuePair<string, string>> Environment = new List<KeyValuePair<string, string>>();
        public readonly List<string> State = new List<string>();
        public readonly List<string> Fights = new List<string>();
        public string ConfigJson;
        public DiagLog Log;
        public LineRecorder Lines;
        /// <summary>只导出这个时间段（单场战斗记录用）；为空 = 全部。</summary>
        public DateTime? Since, Until;
        public bool MaskNames = true;
        public string MyName, MyId;
        /// <summary>导出时替换成 %USERPROFILE% 的用户目录（路径里含 Windows 用户名）。</summary>
        public string UserProfile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);

        public void Env(string key, object value)
        {
            Environment.Add(new KeyValuePair<string, string>(key, value == null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture)));
        }

        public void WriteTo(TextWriter w, DateTime now)
        {
            var lines = Lines == null ? new List<RecordedLine>() : Lines.Lines(Since, Until);
            var entries = Log == null ? new List<DiagEntry>() : Log.Snapshot(Since).Where(e => !Until.HasValue || e.Time <= Until.Value).ToList();
            var samples = Lines == null ? new List<LineSample>() : Lines.Samples();
            var masker = BuildMasker(lines, samples);
            Func<string, string> m = s => masker == null ? s : masker.Mask(s);

            w.WriteLine("==== FF14 Ultimate Combat Helper " + Title + " ====");
            w.WriteLine("生成时间：" + now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                        + (Since.HasValue ? "　范围：" + Since.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " ~ "
                           + (Until ?? now).ToString("HH:mm:ss", CultureInfo.InvariantCulture) : ""));
            if (masker != null) w.WriteLine("已脱敏：玩家名替换为 " + NameMasker.MeAlias + " / 玩家N，用户目录替换为 %USERPROFILE%");

            Section(w, "环境");
            foreach (var kv in Environment) w.WriteLine(kv.Key + "：" + m(kv.Value));

            Section(w, "当前状态");
            foreach (var s in State) w.WriteLine(m(s));

            Section(w, "战斗摘要（最近 " + Fights.Count + " 场）");
            foreach (var s in Fights) w.WriteLine(m(s));

            if (Lines != null) WriteCounters(w, samples, m);

            if (!string.IsNullOrEmpty(ConfigJson)) { Section(w, "配置"); w.WriteLine(ConfigJson); }

            Section(w, "运行日志（" + entries.Count + " 条）");
            foreach (var e in entries) w.WriteLine(m(e.ToString()));

            Section(w, "战斗日志行（" + lines.Count + " 行" + (Lines != null && Lines.RecordOthers ? "，含其他玩家" : "，仅与自己相关") + "）");
            foreach (var l in lines) w.WriteLine(m(l.Raw));
            w.WriteLine();
            w.WriteLine("==== 结束 ====");
        }

        private void WriteCounters(TextWriter w, List<LineSample> samples, Func<string, string> m)
        {
            Section(w, "日志行统计（插件启动以来）");
            w.WriteLine("总行数：" + Lines.Total + "　首行时间：" + (Lines.FirstLineAt.HasValue ? Lines.FirstLineAt.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture) : "（一行都没收到）"));
            w.WriteLine("originalLogLine 为空：" + Lines.EmptyOriginal);
            w.WriteLine("按类型：" + string.Join("  ", Lines.CountsByType().OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value)));
            var mm = Lines.Mismatches();
            w.WriteLine("detectedType 与行首类型不一致：" + (mm.Count == 0 ? "无" : string.Join("  ", mm.OrderByDescending(kv => kv.Value).Take(30).Select(kv => kv.Key + "×" + kv.Value))));
            Section(w, "原始行样本（每种 detectedType 前 2 条，聊天除外）");
            foreach (var s in samples)
            {
                w.WriteLine("detectedType=" + s.DetectedType + " 行首类型=" + s.PrefixType);
                w.WriteLine("  logLine        : " + m(s.LogLine ?? "(null)"));
                w.WriteLine("  originalLogLine: " + m(s.OriginalLogLine ?? "(null)"));
            }
        }

        private NameMasker BuildMasker(List<RecordedLine> lines, List<LineSample> samples)
        {
            if (!MaskNames) return null;
            var masker = new NameMasker(MyName, MyId);
            masker.AddLiteral(UserProfile != null && UserProfile.Length > 3 ? UserProfile : null, "%USERPROFILE%");
            if (Lines != null)   // 插件启动以来见过的所有玩家（含未记录/已挤出缓冲的行）
                foreach (var kv in Lines.KnownPlayers().OrderBy(kv => kv.Value, StringComparer.Ordinal)) masker.Add(kv.Key, kv.Value);
            foreach (var l in lines) masker.Learn(LogLine.Parse(l.Raw, l.Time));
            foreach (var s in samples) masker.Learn(LogLine.Parse(s.OriginalLogLine, DateTime.MinValue));
            return masker;
        }

        private static void Section(TextWriter w, string name)
        {
            w.WriteLine();
            w.WriteLine("---- " + name + " ----");
        }
    }
}
