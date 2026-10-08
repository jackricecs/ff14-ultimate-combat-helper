using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace UltimateCombatHelper.Diagnostics
{
    /// <summary>
    /// 诊断功能的总入口：运行日志 + 战斗日志行记录 + 战斗切分，以及导出诊断包、脱战自动保存战斗记录。
    /// 与 ACT 无关；环境/状态信息由插件通过 Fill 回调填入报告。
    /// </summary>
    public sealed class DiagnosticsService
    {
        public const string ExportPrefix = "UCH-诊断-";
        public const string FightPrefix = "UCH-战斗-";

        private readonly Func<DateTime> clock;
        private DiagnosticsConfig config = new DiagnosticsConfig();

        public DiagLog Log { get; private set; }
        public LineRecorder Lines { get; private set; }
        public FightTracker Fights { get; private set; }
        /// <summary>导出目录。</summary>
        public string LogsDir { get; set; }
        /// <summary>插件填入环境、当前状态、配置 JSON、角色名等。</summary>
        public Action<DiagnosticReport> Fill { get; set; }
        /// <summary>自动保存是否放到线程池（测试时关掉）。</summary>
        public bool SaveInBackground { get; set; }

        public DiagnosticsService(Func<DateTime> clock = null)
        {
            this.clock = clock ?? (() => DateTime.Now);
            Log = new DiagLog(this.clock);
            Lines = new LineRecorder();
            Fights = new FightTracker(Log);
            SaveInBackground = true;
            Apply(config);
        }

        public DiagnosticsConfig Config { get { return config; } }

        public void Apply(DiagnosticsConfig c)
        {
            config = c ?? new DiagnosticsConfig();
            Log.Level = config.ParsedLevel();
            Lines.Enabled = config.RecordLines;
            Lines.RecordOthers = config.RecordOthers;
            Lines.Capacity = config.MaxLines;
        }

        private DiagnosticReport NewReport(string title)
        {
            var r = new DiagnosticReport { Title = title, Log = Log, Lines = Lines, MaskNames = config.MaskNames };
            var f = Fill;
            if (f != null)
            {
                try { f(r); }
                catch (Exception ex) { r.State.Add("收集环境信息出错：" + ex); }
            }
            return r;
        }

        /// <summary>导出完整诊断包，返回文件路径。</summary>
        public string ExportAll()
        {
            DateTime now = clock();
            var r = NewReport("诊断包");
            r.Fights.AddRange(Fights.DescribeAll(now));
            string path = Path.Combine(EnsureDir(), ExportPrefix + now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".txt");
            Write(path, r, now);
            Log.Info("diag", "已导出诊断包：" + path);
            return path;
        }

        public FightSummary CombatStarted(string zone, string tier, string job)
        {
            var f = Fights.Begin(clock(), zone, tier, job);
            Log.Info("fight", string.Format("战斗 #{0} 开始：{1}（{2}），职业 {3}", f.Index, zone, tier, string.IsNullOrEmpty(job) ? "未知" : job));
            return f;
        }

        /// <summary>脱战：结束当前战斗，enrich 里补充 ACT 的遭遇信息，按设置自动保存。</summary>
        public FightSummary CombatEnded(Action<FightSummary> enrich)
        {
            DateTime now = clock();
            var f = Fights.End(now);
            if (f == null) return null;
            if (enrich != null)
            {
                try { enrich(f); }
                catch (Exception ex) { f.ActInfo.Add("读取 ACT 遭遇信息出错：" + ex.Message); }
            }
            Log.Info("fight", "战斗结束：" + Fights.Describe(f, now));
            foreach (var s in f.ActInfo) Log.Info("fight", "  " + s);
            if (config.AutoSaveFights && f.Seconds(now) >= config.MinFightSeconds && !string.IsNullOrEmpty(LogsDir))
            {
                if (SaveInBackground) ThreadPool.QueueUserWorkItem(_ => SaveFight(f));
                else SaveFight(f);
            }
            return f;
        }

        /// <summary>保存单场战斗记录（只含该时间段的运行日志与战斗日志行），返回路径；失败返回 null。</summary>
        public string SaveFight(FightSummary f)
        {
            try
            {
                DateTime now = clock();
                var r = NewReport("战斗记录 #" + f.Index);
                r.Since = f.Start.AddSeconds(-5);
                r.Until = (f.End ?? now).AddSeconds(2);
                r.Fights.Add(Fights.Describe(f, now));
                r.Fights.AddRange(f.ActInfo.Select(s => "  " + s));
                string name = FightPrefix + f.Start.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + SafeName(f.Zone) + ".txt";
                string path = Path.Combine(EnsureDir(), name);
                Write(path, r, now);
                Prune();
                Log.Debug("diag", "已自动保存战斗记录：" + path);
                return path;
            }
            catch (Exception ex)
            {
                Log.Error("diag", "保存战斗记录失败：" + ex.Message);
                return null;
            }
        }

        private void Prune()
        {
            var old = new DirectoryInfo(LogsDir).GetFiles(FightPrefix + "*.txt")
                .OrderByDescending(x => x.Name, StringComparer.Ordinal).Skip(Math.Max(1, config.KeepFightFiles));
            foreach (var x in old)
            {
                try { x.Delete(); } catch (IOException) { }
            }
        }

        private string EnsureDir()
        {
            if (string.IsNullOrEmpty(LogsDir)) throw new InvalidOperationException("日志目录未设置");
            Directory.CreateDirectory(LogsDir);
            return LogsDir;
        }

        private static void Write(string path, DiagnosticReport r, DateTime now)
        {
            string tmp = path + ".tmp";
            using (var w = new StreamWriter(tmp, false, new UTF8Encoding(true)))   // 带 BOM：老版记事本也能正确显示中文
                r.WriteTo(w, now);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        public static string SafeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "未知区域";
            var bad = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (char c in s) sb.Append(bad.Contains(c) || c == ' ' ? '_' : c);
            return sb.Length > 40 ? sb.ToString(0, 40) : sb.ToString();
        }
    }
}
