using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace ClaudeUsageWidget
{
    internal class ProjectRow
    {
        public string Name;
        public double TodayCost;
        public double WeekCost;
        public long WeekTokens;
        public long WeekRequests;
        public string Path;
    }

    /// <summary>
    /// Kontrol Heatmap kontribusi/aktivitas 35 hari (5 minggu x 7 hari), gaya GitHub.
    /// Menampilkan densitas token harian dan estimasi biaya.
    /// </summary>
    internal class ActivityHeatmapControl : Control
    {
        private DayStat[] _days;
        private readonly ToolTip _tip = new ToolTip();
        private int _hoverIdx = -1;

        public ActivityHeatmapControl()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
        }

        public void SetDays(DayStat[] days)
        {
            _days = days;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_days == null || _days.Length == 0) return;

            int idx = HitTest(e.Location);
            if (idx != _hoverIdx)
            {
                _hoverIdx = idx;
                if (idx >= 0 && idx < _days.Length)
                {
                    DayStat d = _days[idx];
                    string costStr = Fmt.Usd(d.Cost);
                    string tipText = d.Tokens > 0
                        ? L.T("hist.hover", d.Date.ToString("yyyy-MM-dd"), costStr, Fmt.Tokens(d.Tokens), d.Requests)
                        : string.Format("{0:yyyy-MM-dd}: 0 tokens", d.Date);
                    _tip.SetToolTip(this, tipText);
                }
                else
                {
                    _tip.SetToolTip(this, null);
                }
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverIdx = -1;
            _tip.SetToolTip(this, null);
            Invalidate();
        }

        private int HitTest(Point p)
        {
            float s;
            using (Graphics g = CreateGraphics()) { s = g.DpiX / 96f; }
            int boxSize = (int)Math.Round(18 * s);
            int gap = (int)Math.Round(4 * s);
            int startX = (int)Math.Round(12 * s);
            int startY = (int)Math.Round(28 * s);

            if (_days == null) return -1;
            int cols = 7; // Sen-Min

            for (int i = 0; i < _days.Length && i < 35; i++)
            {
                int r = i / cols;
                int c = i % cols;
                int x = startX + c * (boxSize + gap);
                int y = startY + r * (boxSize + gap);
                Rectangle rect = new Rectangle(x, y, boxSize, boxSize);
                if (rect.Contains(p)) return i;
            }
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float s = g.DpiX / 96f;
            using (SolidBrush bg = new SolidBrush(Palette.Surface))
                g.FillRectangle(bg, ClientRectangle);

            using (Font titleFont = new Font("Segoe UI", 9f, FontStyle.Bold))
            using (SolidBrush textBrush = new SolidBrush(Palette.Text))
            {
                g.DrawString(L.T("proj.heatmap"), titleFont, textBrush, 12 * s, 8 * s);
            }

            if (_days == null || _days.Length == 0) return;

            // Cari nilai token tertinggi untuk kuantil warna
            long maxTokens = 1;
            for (int i = 0; i < _days.Length; i++)
                if (_days[i].Tokens > maxTokens) maxTokens = _days[i].Tokens;

            int boxSize = (int)Math.Round(18 * s);
            int gap = (int)Math.Round(4 * s);
            int startX = (int)Math.Round(12 * s);
            int startY = (int)Math.Round(28 * s);
            int cols = 7;

            Color emptyColor = Palette.IsLight ? Color.FromArgb(235, 238, 242) : Color.FromArgb(36, 40, 50);
            Color[] greenLevels = Palette.IsLight
                ? new[] { Color.FromArgb(155, 233, 168), Color.FromArgb(64, 196, 99), Color.FromArgb(48, 161, 78), Color.FromArgb(33, 110, 57) }
                : new[] { Color.FromArgb(14, 68, 41), Color.FromArgb(0, 109, 50), Color.FromArgb(38, 166, 65), Color.FromArgb(57, 211, 83) };

            for (int i = 0; i < _days.Length && i < 35; i++)
            {
                int r = i / cols;
                int c = i % cols;
                int x = startX + c * (boxSize + gap);
                int y = startY + r * (boxSize + gap);
                Rectangle rect = new Rectangle(x, y, boxSize, boxSize);

                DayStat d = _days[i];
                Color fill = emptyColor;
                if (d.Tokens > 0)
                {
                    double ratio = (double)d.Tokens / maxTokens;
                    if (ratio <= 0.25) fill = greenLevels[0];
                    else if (ratio <= 0.50) fill = greenLevels[1];
                    else if (ratio <= 0.75) fill = greenLevels[2];
                    else fill = greenLevels[3];
                }

                using (SolidBrush fb = new SolidBrush(fill))
                    g.FillRectangle(fb, rect);

                if (i == _hoverIdx)
                {
                    using (Pen hp = new Pen(Palette.Text, 1.5f))
                        g.DrawRectangle(hp, rect);
                }
                else
                {
                    using (Pen bp = new Pen(Palette.Border, 1f))
                        g.DrawRectangle(bp, rect);
                }
            }

            // Gambar Legenda (Less ... More) di sisi kanan
            int legendBox = (int)Math.Round(12 * s);
            int legX = Width - (int)Math.Round(140 * s);
            int legY = startY + 4 * (boxSize + gap) + 4;
            using (Font legFont = new Font("Segoe UI", 7.5f))
            using (SolidBrush legBrush = new SolidBrush(Palette.Dim))
            {
                g.DrawString(L.T("proj.legend.less"), legFont, legBrush, legX - 28 * s, legY - 1);
                using (SolidBrush eb = new SolidBrush(emptyColor))
                    g.FillRectangle(eb, legX, legY, legendBox, legendBox);
                for (int l = 0; l < 4; l++)
                {
                    using (SolidBrush gb = new SolidBrush(greenLevels[l]))
                        g.FillRectangle(gb, legX + (l + 1) * (legendBox + 3), legY, legendBox, legendBox);
                }
                g.DrawString(L.T("proj.legend.more"), legFont, legBrush, legX + 5 * (legendBox + 3) + 2, legY - 1);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tip.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Dialog Penjelajah Biaya dan Aktivitas per Proyek (dengan Heatmap & Menu Konteks).
    /// </summary>
    internal class ProjectExplorerForm : Form
    {
        private readonly ActivityHeatmapControl _heatmap = new ActivityHeatmapControl();
        private readonly ListView _list = new ListView();
        private readonly List<ProjectRow> _rows = new List<ProjectRow>();
        private ContextMenuStrip _ctxMenu;

        public ProjectExplorerForm(UsageSnapshot usage, HistoryStore history)
        {
            Text = L.T("proj.window.title");
            Font = new Font("Segoe UI", 9f);
            StartPosition = FormStartPosition.CenterScreen;
            Icon = AppIcon.Get();
            FormBorderStyle = FormBorderStyle.Sizable;
            KeyPreview = true;
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) Close();
            };

            float s;
            using (Graphics g = CreateGraphics()) { s = g.DpiX / 96f; }
            Width = (int)Math.Round(760 * s);
            Height = (int)Math.Round(500 * s);
            MinimumSize = new Size((int)Math.Round(600 * s), (int)Math.Round(380 * s));

            BackColor = Palette.Bg;
            ForeColor = Palette.Text;

            BuildRows(usage);
            InitLayout(s, history);

            Load += delegate
            {
                DarkUi.TitleBar(Handle, !Palette.IsLight);
                DarkUi.ControlTheme(_list.Handle, !Palette.IsLight);
            };
        }

        private void BuildRows(UsageSnapshot usage)
        {
            _rows.Clear();
            if (usage == null) return;

            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (usage.WeekProjects != null)
                foreach (string k in usage.WeekProjects.Keys) names.Add(k);
            if (usage.TodayProjects != null)
                foreach (string k in usage.TodayProjects.Keys) names.Add(k);

            foreach (string name in names)
            {
                ProjectRow r = new ProjectRow();
                r.Name = name;
                Tally tw, tt;
                if (usage.TodayProjects != null && usage.TodayProjects.TryGetValue(name, out tt))
                    r.TodayCost = tt.Cost;
                if (usage.WeekProjects != null && usage.WeekProjects.TryGetValue(name, out tw))
                {
                    r.WeekCost = tw.Cost;
                    r.WeekTokens = tw.Total;
                    r.WeekRequests = tw.Requests;
                }
                string p;
                if (usage.ProjectPaths != null && usage.ProjectPaths.TryGetValue(name, out p))
                    r.Path = p;
                else
                    r.Path = "";

                _rows.Add(r);
            }

            // Urutkan berdasarkan biaya 7 hari terbanyak
            _rows.Sort(delegate(ProjectRow a, ProjectRow b) { return b.WeekCost.CompareTo(a.WeekCost); });
        }

        private void InitLayout(float s, HistoryStore history)
        {
            // Panel Atas: Heatmap
            _heatmap.Dock = DockStyle.Top;
            _heatmap.Height = (int)Math.Round(150 * s);
            if (history != null)
            {
                _heatmap.SetDays(history.Series(35));
            }

            // List View
            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.GridLines = true;
            _list.BackColor = Palette.Surface;
            _list.ForeColor = Palette.Text;
            _list.BorderStyle = BorderStyle.FixedSingle;

            _list.Columns.Add(L.T("proj.col.project"), (int)Math.Round(160 * s), HorizontalAlignment.Left);
            _list.Columns.Add(L.T("proj.col.today"), (int)Math.Round(95 * s), HorizontalAlignment.Right);
            _list.Columns.Add(L.T("proj.col.week"), (int)Math.Round(95 * s), HorizontalAlignment.Right);
            _list.Columns.Add(L.T("proj.col.tokens"), (int)Math.Round(90 * s), HorizontalAlignment.Right);
            _list.Columns.Add(L.T("proj.col.reqs"), (int)Math.Round(75 * s), HorizontalAlignment.Right);
            _list.Columns.Add(L.T("proj.col.path"), (int)Math.Round(220 * s), HorizontalAlignment.Left);

            // Menu Klik Kanan (Context Menu)
            _ctxMenu = new ContextMenuStrip();
            ToolStripMenuItem miFolder = new ToolStripMenuItem(L.T("proj.btn.folder"), null, delegate { OpenFolderSelected(); });
            ToolStripMenuItem miTerminal = new ToolStripMenuItem(L.T("proj.btn.terminal"), null, delegate { OpenTerminalSelected(); });
            ToolStripMenuItem miCopy = new ToolStripMenuItem(L.T("proj.btn.copy"), null, delegate { CopyMarkdownTable(); });
            _ctxMenu.Items.Add(miFolder);
            _ctxMenu.Items.Add(miTerminal);
            _ctxMenu.Items.Add(new ToolStripSeparator());
            _ctxMenu.Items.Add(miCopy);
            MenuTheme.Apply(_ctxMenu);
            _list.ContextMenuStrip = _ctxMenu;

            _ctxMenu.Opening += delegate
            {
                bool hasSel = _list.SelectedItems.Count > 0;
                string path = null;
                if (hasSel)
                {
                    ProjectRow r = _list.SelectedItems[0].Tag as ProjectRow;
                    if (r != null && !string.IsNullOrEmpty(r.Path)) path = r.Path;
                }
                bool pathOk = !string.IsNullOrEmpty(path) && Directory.Exists(path);
                miFolder.Enabled = pathOk;
                miTerminal.Enabled = pathOk;
            };

            PopulateList();
            _list.DoubleClick += delegate { OpenTerminalSelected(); };

            Controls.Add(_list);
            Controls.Add(_heatmap);
        }

        private void PopulateList()
        {
            _list.Items.Clear();
            if (_rows.Count == 0)
            {
                ListViewItem empty = new ListViewItem(L.T("proj.empty"));
                empty.ForeColor = Palette.Dim;
                _list.Items.Add(empty);
                return;
            }
            foreach (ProjectRow r in _rows)
            {
                string todayStr = Fmt.UsdShort(r.TodayCost);
                string weekStr = Fmt.UsdShort(r.WeekCost);

                ListViewItem item = new ListViewItem(r.Name);
                item.SubItems.Add(todayStr);
                item.SubItems.Add(weekStr);
                item.SubItems.Add(Fmt.Tokens(r.WeekTokens));
                item.SubItems.Add(r.WeekRequests.ToString(CultureInfo.InvariantCulture));
                item.SubItems.Add(string.IsNullOrEmpty(r.Path) ? "-" : r.Path);
                item.Tag = r;
                _list.Items.Add(item);
            }
        }

        private void OpenFolderSelected()
        {
            if (_list.SelectedItems.Count == 0) return;
            ProjectRow r = _list.SelectedItems[0].Tag as ProjectRow;
            if (r == null || string.IsNullOrEmpty(r.Path) || !Directory.Exists(r.Path)) return;
            try { Process.Start("explorer.exe", "\"" + r.Path + "\""); }
            catch (Exception ex) { Log.Write("proj.openfolder", ex); }
        }

        private void OpenTerminalSelected()
        {
            if (_list.SelectedItems.Count == 0) return;
            ProjectRow r = _list.SelectedItems[0].Tag as ProjectRow;
            if (r == null || string.IsNullOrEmpty(r.Path) || !Directory.Exists(r.Path)) return;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/K cd /d \"" + r.Path + "\"");
                psi.WorkingDirectory = r.Path;
                Process.Start(psi);
            }
            catch (Exception ex) { Log.Write("proj.openterminal", ex); }
        }

        public static string BuildMarkdownTable(List<ProjectRow> rows)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("| Project | Today Cost | 7-Day Cost | Tokens (7d) | Requests | Path |");
            sb.AppendLine("|:---|---:|---:|---:|---:|:---|");
            if (rows == null || rows.Count == 0)
            {
                sb.AppendLine("| *(No project data)* | $0.00 | $0.00 | 0 | 0 | - |");
                return sb.ToString();
            }

            foreach (ProjectRow r in rows)
            {
                string p = string.IsNullOrEmpty(r.Path) ? "-" : r.Path;
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "| {0} | ${1:F2} | ${2:F2} | {3} | {4} | {5} |\r\n",
                    r.Name, r.TodayCost, r.WeekCost, Fmt.Tokens(r.WeekTokens), r.WeekRequests, p);
            }
            return sb.ToString();
        }

        private void CopyMarkdownTable()
        {
            try
            {
                string md = BuildMarkdownTable(_rows);
                Clipboard.SetText(md);
                MessageBox.Show(this, L.T("proj.copied"), L.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Log.Write("proj.copymd", ex);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _ctxMenu != null) { _ctxMenu.Dispose(); _ctxMenu = null; }
            base.Dispose(disposing);
        }
    }
}
