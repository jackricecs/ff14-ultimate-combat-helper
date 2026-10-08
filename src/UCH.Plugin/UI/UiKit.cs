// UiKit.cs — 纯代码 WinForms 的小工具（各设置页共用）
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace UltimateCombatHelper.Plugin.UI
{
    internal static class UiKit
    {
        public static readonly Font Normal = new Font("Microsoft YaHei UI", 9.5f);
        public static readonly Font Emphasis = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold);

        public static TableLayoutPanel Grid(int rows, SizeType[] types, float[] sizes)
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = rows, Padding = new Padding(4) };
            for (int i = 0; i < rows; i++) t.RowStyles.Add(new RowStyle(types[i], sizes[i]));
            return t;
        }

        public static FlowLayoutPanel Flow(params Control[] cs)
        {
            var f = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill };
            f.Controls.AddRange(cs);
            return f;
        }

        public static FlowLayoutPanel Column(params Control[] cs)
        {
            var f = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
            f.Controls.AddRange(cs);
            return f;
        }

        public static GroupBox Group(string text, Control inner, int w = 0, int h = 0)
        {
            var g = new GroupBox { Text = text, Dock = w == 0 ? DockStyle.Fill : DockStyle.None, Padding = new Padding(6) };
            if (w > 0) g.Size = new Size(w, h);
            inner.Dock = DockStyle.Fill;
            g.Controls.Add(inner);
            return g;
        }

        public static Label Lbl(string t) { return new Label { Text = t, AutoSize = true, Margin = new Padding(3, 7, 3, 0) }; }

        public static Label Hint(string t)
        {
            return new Label { Text = t, AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(3, 6, 3, 0) };
        }

        public static Button Btn(string t, Action a) { var b = new Button { Text = t, AutoSize = true }; b.Click += (s, e) => a(); return b; }

        public static NumericUpDown Num(decimal min, decimal max, int dp, decimal inc, int w)
        {
            return new NumericUpDown { Minimum = min, Maximum = max, DecimalPlaces = dp, Increment = inc, Width = w };
        }

        public static decimal Clamp(double v, NumericUpDown n) { return Math.Min(n.Maximum, Math.Max(n.Minimum, (decimal)v)); }

        public static ListView List(string[] cols, int[] widths)
        {
            var lv = new ListView { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill };
            for (int i = 0; i < cols.Length; i++) lv.Columns.Add(cols[i], widths[i]);
            return lv;
        }

        public static void Fill(ListView lv, IEnumerable<ListViewItem> items)
        {
            lv.BeginUpdate(); lv.Items.Clear(); lv.Items.AddRange(items.ToArray()); lv.EndUpdate();
        }

        public static string Cell(DataGridViewRow r, string col)
        {
            var v = r.Cells[col].Value;
            return v == null ? "" : v.ToString().Trim();
        }

        /// <summary>在资源管理器中选中文件（文件不存在时打开所在目录）。</summary>
        public static void Reveal(string path)
        {
            try
            {
                if (System.IO.File.Exists(path)) System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\"");
                else
                {
                    System.IO.Directory.CreateDirectory(path);
                    System.Diagnostics.Process.Start("explorer.exe", "\"" + path + "\"");
                }
            }
            catch (Exception) { }
        }
    }
}
