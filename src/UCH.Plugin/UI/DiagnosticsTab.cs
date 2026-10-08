// DiagnosticsTab.cs — 「诊断与日志」设置页：日志详细程度、战斗日志行记录、自动保存、导出诊断包。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using UltimateCombatHelper.Diagnostics;
using UltimateCombatHelper.Logs;
using static UltimateCombatHelper.Plugin.UI.UiKit;

namespace UltimateCombatHelper.Plugin.UI
{
    internal sealed class DiagnosticsTab : TabPage
    {
        private static readonly KeyValuePair<DiagLevel, string>[] Levels = {
            new KeyValuePair<DiagLevel, string>(DiagLevel.Info, "信息（日常使用）"),
            new KeyValuePair<DiagLevel, string>(DiagLevel.Debug, "调试（推荐：记录每次挂 DoT、每个未播报原因）"),
            new KeyValuePair<DiagLevel, string>(DiagLevel.Trace, "追踪（最详细）"),
        };

        private readonly UchPlugin plugin;
        private readonly ComboBox cmbLevel;
        private readonly CheckBox cbRecord, cbOthers, cbAutoSave, cbMask;
        private readonly NumericUpDown numMaxLines, numKeep, numMinFight;
        private readonly Label lblStats;
        private readonly ListBox lstFights;
        private string lastFights;
        private bool exporting;

        public DiagnosticsTab(UchPlugin plugin) : base("诊断与日志")
        {
            this.plugin = plugin;
            cmbLevel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
            foreach (var l in Levels) cmbLevel.Items.Add(l.Value);
            cbRecord = new CheckBox { Text = "记录战斗日志行（导出时附带）", AutoSize = true };
            numMaxLines = Num(1000, 100000, 0, 5000, 80);
            cbOthers = new CheckBox { Text = "同时记录其他玩家的技能/状态行（8 人本里文件会大很多）", AutoSize = true };
            cbAutoSave = new CheckBox { Text = "每次脱战自动保存一份战斗记录", AutoSize = true };
            numKeep = Num(1, 200, 0, 1, 60);
            numMinFight = Num(0, 600, 0, 5, 60);
            cbMask = new CheckBox { Text = "导出时隐藏玩家角色名（替换为 <我> / 玩家1…，敌人名字保留）", AutoSize = true };
            lblStats = new Label { AutoSize = true, Font = Normal, Margin = new Padding(3, 8, 3, 4) };
            lstFights = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true, IntegralHeight = false };

            var settings = Column(
                Flow(Lbl("日志详细程度："), cmbLevel),
                Flow(cbRecord, Lbl("最多保留"), numMaxLines, Lbl("行")),
                cbOthers,
                Flow(cbAutoSave, Lbl("保留最近"), numKeep, Lbl("份，短于"), numMinFight, Lbl("秒的战斗不保存")),
                cbMask,
                Flow(Btn("保存诊断设置", Save)));
            var actions = Column(
                Flow(Btn("导出诊断包", ExportNow), Btn("打开日志目录", () => Reveal(plugin.Diagnostics.LogsDir)), Btn("清空已记录的数据", ClearRecorded)),
                Hint("遇到问题：保持插件开启打一场（木人也行，记得开测试模式）→ 点「导出诊断包」→ 把生成的 txt 发给开发者。\n" +
                     "诊断包含：环境与插件版本、角色/职业/区域状态、配置、每次挂 DoT 与未播报的原因、战斗摘要、相关战斗日志行。"),
                lblStats);

            var t = Grid(3, new[] { SizeType.AutoSize, SizeType.AutoSize, SizeType.Percent }, new[] { 0f, 0f, 100f });
            t.Controls.Add(Group("设置", settings));
            t.Controls.Add(Group("导出", actions));
            t.Controls.Add(Group("最近的战斗（ACT 进入战斗 → 脱战）", lstFights));
            foreach (Control c in t.Controls) if (c is GroupBox) ((GroupBox)c).AutoSize = true;
            Controls.Add(t);
        }

        public void LoadFromConfig(DiagnosticsConfig d)
        {
            int li = Array.FindIndex(Levels, l => l.Key == d.ParsedLevel());
            cmbLevel.SelectedIndex = li < 0 ? 1 : li;
            cbRecord.Checked = d.RecordLines;
            numMaxLines.Value = Clamp(d.MaxLines, numMaxLines);
            cbOthers.Checked = d.RecordOthers;
            cbAutoSave.Checked = d.AutoSaveFights;
            numKeep.Value = Clamp(d.KeepFightFiles, numKeep);
            numMinFight.Value = Clamp(d.MinFightSeconds, numMinFight);
            cbMask.Checked = d.MaskNames;
        }

        private void Save()
        {
            var d = plugin.Config.Diagnostics;
            d.Level = Levels[Math.Max(0, cmbLevel.SelectedIndex)].Key.ToString();
            d.RecordLines = cbRecord.Checked;
            d.MaxLines = (int)numMaxLines.Value;
            d.RecordOthers = cbOthers.Checked;
            d.AutoSaveFights = cbAutoSave.Checked;
            d.KeepFightFiles = (int)numKeep.Value;
            d.MinFightSeconds = (double)numMinFight.Value;
            d.MaskNames = cbMask.Checked;
            plugin.ApplyConfig();
            plugin.Log("诊断设置已保存（日志级别：" + DiagEntry.LevelName(d.ParsedLevel()) + "）");
        }

        /// <summary>在线程池里生成诊断包（几万行脱敏要一点时间，不能卡住 ACT 界面），完成后回到界面线程提示。</summary>
        public void ExportNow()
        {
            if (exporting) return;
            exporting = true;
            plugin.Log("正在导出诊断包…");
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                string path = null; Exception error = null;
                try { path = plugin.Diagnostics.ExportAll(); }
                catch (Exception ex) { error = ex; }
                try { BeginInvoke(new Action(() => ExportDone(path, error))); }
                catch (InvalidOperationException) { exporting = false; }   // 界面已关闭
            });
        }

        private void ExportDone(string path, Exception error)
        {
            exporting = false;
            if (error != null)
            {
                plugin.Diagnostics.Log.Error("diag", "导出诊断包失败：" + error);
                MessageBox.Show("导出失败：" + error.Message, "导出诊断包", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Reveal(path);
            try { Clipboard.SetText(path); } catch (Exception) { }
            MessageBox.Show("已导出：\n" + path + "\n\n（路径已复制到剪贴板）", "导出诊断包", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ClearRecorded()
        {
            plugin.Diagnostics.Log.Clear();
            plugin.Diagnostics.Lines.ClearLines();
            plugin.Log("已清空运行日志与战斗日志行记录（统计计数保留）");
        }

        /// <summary>由主面板的界面定时器调用。</summary>
        public void RefreshLive()
        {
            if (!Visible) return;
            var svc = plugin.Diagnostics;
            var lines = svc.Lines;
            lblStats.Text = string.Format(CultureInfo.InvariantCulture,
                "插件启动以来收到日志行 {0:N0}（26 状态行 {1:N0}，21/22 技能行 {2:N0}）　首行：{3}\n已记录战斗日志行 {4:N0} / {5:N0}　运行日志 {6:N0} 条　日志目录：{7}",
                lines.Total, lines.CountOf(LogType.StatusAdd), lines.CountOf(LogType.Ability) + lines.CountOf(LogType.AoeAbility),
                lines.FirstLineAt.HasValue ? lines.FirstLineAt.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture) : "还没收到（解析插件是否在运行？）",
                lines.LineCount, lines.Capacity, svc.Log.Count, svc.LogsDir);
            lblStats.ForeColor = lines.Total == 0 ? Color.OrangeRed : SystemColors.ControlText;

            var fights = svc.Fights.DescribeAll(DateTime.Now);
            string joined = string.Join("\n", fights);
            if (joined != lastFights)
            {
                lastFights = joined;
                lstFights.BeginUpdate();
                lstFights.Items.Clear();
                foreach (var f in Enumerable.Reverse(fights)) lstFights.Items.Add(f);
                lstFights.EndUpdate();
            }
        }
    }
}
