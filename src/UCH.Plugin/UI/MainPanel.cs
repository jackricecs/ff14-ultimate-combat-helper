// MainPanel.cs — 插件设置界面（纯代码 WinForms，无设计器文件，便于在任何环境编译与代码审查）
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Advanced_Combat_Tracker;
using UltimateCombatHelper.Data;
using UltimateCombatHelper.Diagnostics;
using UltimateCombatHelper.Modules.Dot;
using static UltimateCombatHelper.Plugin.UI.UiKit;

namespace UltimateCombatHelper.Plugin.UI
{
    internal sealed class MainPanel : UserControl
    {
        private const string RepoUrl = "https://github.com/jackricecs/ff14-ultimate-combat-helper";

        private readonly UchPlugin plugin;
        private const int MaxPendingLog = 2000;

        private readonly Timer uiTimer = new Timer { Interval = 300 };
        private readonly ConcurrentQueue<string> pendingLog = new ConcurrentQueue<string>();
        private readonly DiagnosticsTab diagTab;
        private bool loading;
        private int highlightedJob = int.MinValue;

        // 总览
        private Label lblStatus, lblJobDot, lblDotJob;
        private ListView lvEnemies;
        private TextBox logBox;
        // 通用
        private CheckBox cbEnabled, cbTestMode;
        private NumericUpDown numCooldown;
        // DoT
        private CheckBox cbDotEnabled, cbDotCombat;
        private NumericUpDown numLead, numRatio;
        private TextBox txtTemplate;
        private ComboBox cmbTargetMode;
        private CheckedListBox clbTiers;
        private DataGridView gridDots;
        private ListView lvActive;

        private static readonly KeyValuePair<string, string>[] TargetModes = {
            new KeyValuePair<string, string>(DotTargetMode.Major, "主要敌人（推荐：忽略小怪，切目标/双Boss也能报）"),
            new KeyValuePair<string, string>(DotTargetMode.Current, "仅当前选中目标"),
            new KeyValuePair<string, string>(DotTargetMode.All, "所有目标"),
        };

        public MainPanel(UchPlugin plugin)
        {
            this.plugin = plugin;
            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(BuildOverview());
            tabs.TabPages.Add(BuildDot());
            tabs.TabPages.Add(BuildGeneral());
            diagTab = new DiagnosticsTab(plugin);
            tabs.TabPages.Add(diagTab);
            Controls.Add(tabs);
            plugin.Diagnostics.Log.Written += OnDiagWritten;
            uiTimer.Tick += (s, e) => RefreshLive();
            uiTimer.Start();
        }

        public void StopTimers()
        {
            plugin.Diagnostics.Log.Written -= OnDiagWritten;
            uiTimer.Stop(); uiTimer.Dispose();
        }

        /// <summary>任意线程写入的日志先入队，由界面定时器批量追加（避免每条日志一次跨线程调用）。</summary>
        private void OnDiagWritten(DiagEntry e)
        {
            if (pendingLog.Count < MaxPendingLog) pendingLog.Enqueue(e.ToString());
        }

        private static Label JobDotLabel()
        {
            return new Label { AutoSize = true, Font = Emphasis, Margin = new Padding(3, 4, 3, 4), Text = "当前职业：识别中…" };
        }

        // ================= 总览 =================
        private TabPage BuildOverview()
        {
            var page = new TabPage("总览");
            var t = Grid(3, new[] { SizeType.AutoSize, SizeType.Percent, SizeType.Percent }, new[] { 0f, 40f, 60f });
            lblStatus = new Label { AutoSize = true, Font = Normal };
            lblJobDot = JobDotLabel();
            var buttons = Flow(
                Btn("重新加载配置", () => plugin.ReloadConfig()),
                Btn("打开配置目录", () => Reveal(plugin.ConfigPath)),
                Btn("导出诊断包", () => diagTab.ExportNow()),
                Btn("打开日志目录", () => Reveal(plugin.Diagnostics.LogsDir)),
                Btn("项目主页", () => { try { System.Diagnostics.Process.Start(RepoUrl); } catch (Exception) { } }));
            var top = Column(lblStatus, lblJobDot, buttons);
            lvEnemies = List(new[] { "敌人", "最大HP", "主要?" }, new[] { 220, 120, 60 });
            logBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Microsoft YaHei UI", 9f) };
            t.Controls.Add(top); t.Controls.Add(Group("场上敌人（用于判断主要目标）", lvEnemies)); t.Controls.Add(Group("日志", logBox));
            page.Controls.Add(t);
            return page;
        }

        // ================= DoT =================
        private TabPage BuildDot()
        {
            var page = new TabPage("DoT 监控");
            var t = Grid(4, new[] { SizeType.AutoSize, SizeType.AutoSize, SizeType.Percent, SizeType.Percent }, new[] { 0f, 0f, 55f, 45f });

            lblDotJob = JobDotLabel();
            cbDotEnabled = new CheckBox { Text = "启用 DoT 提醒", AutoSize = true };
            numLead = Num(0, 30, 1, 0.5m, 60);
            cbDotCombat = new CheckBox { Text = "仅战斗中", AutoSize = true };
            txtTemplate = new TextBox { Width = 220 };
            cmbTargetMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
            foreach (var m in TargetModes) cmbTargetMode.Items.Add(m.Value);
            numRatio = Num(0.05m, 1, 2, 0.05m, 60);
            clbTiers = new CheckedListBox { CheckOnClick = true, Height = 110, Width = 170 };
            foreach (var tier in ZoneTable.AllTiers()) clbTiers.Items.Add(ZoneTable.DisplayName(tier));

            var row1 = Flow(cbDotEnabled, Lbl("剩余"), numLead, Lbl("秒时提醒"), cbDotCombat, Lbl("  语音："), txtTemplate,
                            Btn("试听", () => ActGlobals.oFormActMain.TTS(txtTemplate.Text.Replace("{dot}", "高闪雷").Replace("{target}", "木人")
                                .Replace("{sec}", ((int)numLead.Value).ToString(CultureInfo.InvariantCulture)))));
            var row2 = Flow(Lbl("目标："), cmbTargetMode, Lbl("主要敌人 HP ≥ 最高的"), numRatio, Lbl("倍"));
            var left = Column(row1, row2);
            left.Dock = DockStyle.None;
            var settings = Flow(left, Group("在这些副本提醒", clbTiers, 190, 140));

            gridDots = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = true, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
            gridDots.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "启用", FillWeight = 12 });
            gridDots.Columns.Add("Job", "职业"); gridDots.Columns.Add("Id", "状态ID(十六进制)");
            gridDots.Columns.Add("Note", "备注"); gridDots.Columns.Add("Name", "播报名(空=游戏内名称)"); gridDots.Columns.Add("Lead", "提前秒数(空=全局)");

            lvActive = List(new[] { "DoT", "目标", "剩余(秒)", "状态" }, new[] { 140, 200, 80, 100 });

            var gridBox = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
            gridBox.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); gridBox.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            gridBox.Controls.Add(gridDots); gridBox.Controls.Add(Flow(Btn("保存 DoT 设置", SaveDot), Lbl("修改上面任意设置后点保存；黄色行 = 当前职业。不知道 DoT 的状态 ID？挂一次后看日志里「不在 DoT 监控列表」那条")));

            t.Controls.Add(lblDotJob);
            t.Controls.Add(settings); t.Controls.Add(Group("监控的 DoT", gridBox)); t.Controls.Add(Group("实时（你挂着的 DoT）", lvActive));
            page.Controls.Add(t);
            return page;
        }

        // ================= 通用 =================
        private TabPage BuildGeneral()
        {
            var page = new TabPage("通用");
            cbEnabled = new CheckBox { Text = "启用插件全部功能", AutoSize = true };
            cbTestMode = new CheckBox { Text = "测试模式：忽略副本难度过滤（打木人时打开，用完记得关）", AutoSize = true };
            numCooldown = Num(0, 10, 1, 0.5m, 60);
            var f = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(8) };
            f.Controls.Add(cbEnabled); f.Controls.Add(cbTestMode);
            f.Controls.Add(Flow(Lbl("同一句话最短重复间隔"), numCooldown, Lbl("秒")));
            f.Controls.Add(Btn("保存", SaveGeneral));
            f.Controls.Add(new Label { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 16, 0, 0),
                Text = "本插件只读取 FFXIV 解析插件产生的日志并通过 ACT 的 TTS 播报，不向游戏写入任何数据。\n" +
                       "TTS 音色在 ACT「选项 → 声音设置」中选择中文语音。" });
            page.Controls.Add(f);
            return page;
        }

        // ================= 配置 <-> 界面 =================
        public void LoadFromConfig()
        {
            if (InvokeRequired) { BeginInvoke(new Action(LoadFromConfig)); return; }
            loading = true;
            var c = plugin.Config;
            cbEnabled.Checked = c.General.Enabled;
            cbTestMode.Checked = c.General.TestModeAnywhere;
            numCooldown.Value = Clamp(c.General.SpeechCooldownSeconds, numCooldown);

            var d = c.Dot;
            cbDotEnabled.Checked = d.Enabled;
            cbDotCombat.Checked = d.OnlyInCombat;
            numLead.Value = Clamp(d.LeadSeconds, numLead);
            numRatio.Value = Clamp(d.MajorHpRatio, numRatio);
            txtTemplate.Text = d.SpeechTemplate;
            int mi = Array.FindIndex(TargetModes, m => m.Key == d.TargetMode);
            cmbTargetMode.SelectedIndex = mi < 0 ? 0 : mi;
            var tiers = ZoneTable.AllTiers().ToList();
            for (int i = 0; i < tiers.Count; i++) clbTiers.SetItemChecked(i, d.Tiers.Contains(tiers[i].ToString()));

            gridDots.Rows.Clear();
            foreach (var x in d.Dots)
                gridDots.Rows.Add(x.Enabled, x.Job, x.Id, x.Note, x.Name,
                                  x.LeadSeconds.HasValue ? x.LeadSeconds.Value.ToString(CultureInfo.InvariantCulture) : "");
            highlightedJob = int.MinValue;
            diagTab.LoadFromConfig(c.Diagnostics);
            loading = false;
        }

        private void SaveDot()
        {
            if (loading) return;
            var d = plugin.Config.Dot;
            d.Enabled = cbDotEnabled.Checked;
            d.OnlyInCombat = cbDotCombat.Checked;
            d.LeadSeconds = (double)numLead.Value;
            d.MajorHpRatio = (double)numRatio.Value;
            if (txtTemplate.Text.Trim().Length > 0) d.SpeechTemplate = txtTemplate.Text.Trim();
            d.TargetMode = TargetModes[Math.Max(0, cmbTargetMode.SelectedIndex)].Key;
            var tiers = ZoneTable.AllTiers().ToList();
            d.Tiers = Enumerable.Range(0, tiers.Count).Where(i => clbTiers.GetItemChecked(i)).Select(i => tiers[i].ToString()).ToList();

            var list = new List<DotDef>();
            foreach (DataGridViewRow r in gridDots.Rows)
            {
                if (r.IsNewRow) continue;
                string id = Cell(r, "Id");
                if (id.Length == 0) continue;
                double lead;
                list.Add(new DotDef
                {
                    Enabled = r.Cells["Enabled"].Value is bool && (bool)r.Cells["Enabled"].Value,
                    Job = Cell(r, "Job"), Id = id.ToUpperInvariant(), Note = Cell(r, "Note"), Name = Cell(r, "Name"),
                    LeadSeconds = double.TryParse(Cell(r, "Lead"), NumberStyles.Float, CultureInfo.InvariantCulture, out lead) ? (double?)lead : null,
                });
            }
            d.Dots = list;
            plugin.ApplyConfig();
            plugin.Log("DoT 设置已保存（监控 " + list.Count(x => x.Enabled) + " 种）");
        }

        private void SaveGeneral()
        {
            var g = plugin.Config.General;
            g.Enabled = cbEnabled.Checked;
            g.TestModeAnywhere = cbTestMode.Checked;
            g.SpeechCooldownSeconds = (double)numCooldown.Value;
            plugin.ApplyConfig();
            plugin.Log("通用设置已保存" + (g.TestModeAnywhere ? "（测试模式已开启）" : ""));
        }

        // ================= 实时刷新 =================
        private void RefreshLive()
        {
            var m = plugin.Manager;
            if (m == null) return;
            var ctx = m.Context;
            lblStatus.Text = string.Format("v{0}　角色ID：{1}　区域：{2}（{3}）　{4}{5}",
                UchPlugin.Version, ctx.MyId.Length == 0 ? "等待中…" : ctx.MyId,
                ctx.Zone.Id == 0 ? "未知" : ctx.Zone.NameCn, ZoneTable.DisplayName(ctx.Zone.Tier),
                ctx.InCombat ? "战斗中" : "非战斗", plugin.Config.General.TestModeAnywhere ? "　[测试模式]" : "");
            RefreshJobDot(ctx.MyJob);
            FlushLog();
            diagTab.RefreshLive();

            if (lvEnemies.Parent != null && lvEnemies.Visible)
            {
                var ratio = plugin.Config.Dot.MajorHpRatio;
                Fill(lvEnemies, ctx.Enemies().OrderByDescending(e => e.MaxHp).Take(30).Select(e =>
                    new ListViewItem(new[] { e.Name, e.MaxHp.ToString("N0"), ctx.IsMajorEnemy(e.Id, ratio) ? "是" : "" })));
            }
            if (lvActive.Visible && plugin.Dot != null && plugin.Dot.Tracker != null)
            {
                var now = DateTime.Now;
                Fill(lvActive, plugin.Dot.Tracker.Snapshot().Select(e =>
                {
                    double rem = Math.Max(0, e.Remaining(now));
                    var it = new ListViewItem(new[] { e.DisplayName, e.TargetName, rem.ToString("0.0"),
                        e.Warned ? "已提醒" : (ctx.IsMajorEnemy(e.TargetId, plugin.Config.Dot.MajorHpRatio) ? "" : "非主要目标") });
                    if (rem <= e.LeadSeconds) it.ForeColor = Color.OrangeRed;
                    return it;
                }));
            }
        }

        /// <summary>两个页顶部的"当前职业 / DoT 剩余"，以及 DoT 列表里高亮当前职业的行。</summary>
        private void RefreshJobDot(int job)
        {
            var dot = plugin.Dot;
            var active = dot == null || dot.Tracker == null ? new List<DotEntry>() : dot.Tracker.Snapshot();
            var v = DotStatus.Describe(job, plugin.Config.Dot, active, DateTime.Now);
            string text = "当前职业：" + v.Job + "　　DoT：" + v.Dot;
            foreach (var l in new[] { lblJobDot, lblDotJob })
            {
                if (l.Text != text) l.Text = text;
                l.ForeColor = v.Urgent ? Color.OrangeRed : SystemColors.ControlText;
            }
            if (job == highlightedJob) return;
            highlightedJob = job;
            string name = job == Jobs.Unknown ? null : Jobs.NameCn(Jobs.ToAdvanced(job));
            foreach (DataGridViewRow r in gridDots.Rows)
                if (!r.IsNewRow) r.DefaultCellStyle.BackColor = name != null && Cell(r, "Job") == name ? Color.LightYellow : Color.Empty;
        }

        private void FlushLog()
        {
            if (pendingLog.IsEmpty || logBox.IsDisposed) return;
            var sb = new StringBuilder();
            string msg;
            while (pendingLog.TryDequeue(out msg)) sb.Append(msg).Append(Environment.NewLine);
            if (logBox.TextLength + sb.Length > 60000) logBox.Clear();
            logBox.AppendText(sb.ToString());
        }
    }
}
