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
        public string Provider;
        public double TodayCost;
        public double WeekCost;
        public long WeekTokens;
        public long WeekRequests;
        public string Path;
    }

    internal class ProviderTrendsControl : Control
    {
        private DayStat[] _days = new DayStat[0];
        private readonly ToolTip _tip = new ToolTip();
        private int _hoverIdx = -1;
        private float _dpiScale;
        private double _dailyTarget, _monthlyBudget, _monthCost;
        private string _providerFilter = "";
        private static readonly string[] Providers = { "Claude", "Antigravity", "Codex", "Gemini CLI", "OpenCode", "Cline", "Copilot" };

        private float GetDpiScale()
        {
            if (_dpiScale <= 0f) { using (Graphics g = CreateGraphics()) _dpiScale = g.DpiX / 96f; }
            return _dpiScale;
        }

        protected override void OnDpiChangedAfterParent(EventArgs e)
        {
            base.OnDpiChangedAfterParent(e);
            _dpiScale = 0f;
        }

        public ProviderTrendsControl()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
        }

        public void SetDays(DayStat[] days)
        {
            _days = days ?? new DayStat[0];
            Invalidate();
        }

        public void SetBudgetForecast(double dailyTarget, double monthlyBudget, double monthCost)
        {
            _dailyTarget = dailyTarget;
            _monthlyBudget = monthlyBudget;
            _monthCost = monthCost;
            Invalidate();
        }

        public void SetProviderFilter(string provider)
        {
            _providerFilter = provider ?? "";
            Invalidate();
        }

        private static Color ColorForProvider(string prov)
        {
            if (prov == "Antigravity") return Palette.AntigravityColor;
            if (prov == "Codex") return Palette.CodexColor;
            if (prov == "Gemini CLI") return Palette.GeminiCliColor;
            if (prov == "OpenCode") return Palette.OpenCodeColor;
            if (prov == "Cline") return Palette.ClineColor;
            if (prov == "Copilot") return Palette.CopilotColor;
            return Palette.ClaudeColor;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_days.Length == 0) return;
            float s = GetDpiScale();
            int padL = (int)(16 * s), padR = (int)(16 * s);
            int avail = Width - padL - padR;
            int idx = -1;
            if (avail > 0 && e.X >= padL && e.X < padL + avail)
            {
                idx = (int)((e.X - padL) / (avail / (float)_days.Length));
                if (idx < 0 || idx >= _days.Length) idx = -1;
            }
            if (idx != _hoverIdx)
            {
                _hoverIdx = idx;
                if (idx >= 0)
                {
                    DayStat d = _days[idx];
                    List<string> parts = new List<string>();
                    bool hasFilter = !string.IsNullOrEmpty(_providerFilter);
                    double filteredSum = 0;
                    foreach (string p in Providers)
                    {
                        if (hasFilter && !string.Equals(p, _providerFilter, StringComparison.OrdinalIgnoreCase)) continue;
                        double pv = d.CostFor(p);
                        if (pv > 0.001) { parts.Add(p + " " + Fmt.UsdShort(pv)); filteredSum += pv; }
                    }
                    string detail = parts.Count > 0
                        ? string.Join(" · ", parts.ToArray())
                        : Fmt.UsdShort(hasFilter ? filteredSum : d.Cost);
                    _tip.SetToolTip(this, string.Format("{0:yyyy-MM-dd}: {1}", d.Date, detail));
                }
                else _tip.SetToolTip(this, null);
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

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.Clear(Palette.Bg);
            float s = g.DpiX / 96f;
            int pad = (int)(16 * s);
            int legH = (int)(26 * s);
            int chartTop = pad + (int)(22 * s);
            int chartBot = Height - legH - (int)(24 * s);
            if (chartBot <= chartTop + 20) return;

            double[] provTotals = new double[Providers.Length];
            double peak = 0;
            bool anyProvData = false;
            bool hasProvFilter = !string.IsNullOrEmpty(_providerFilter);
            for (int i = 0; i < _days.Length; i++)
            {
                double sum = 0;
                for (int pi = 0; pi < Providers.Length; pi++)
                {
                    if (hasProvFilter && !string.Equals(Providers[pi], _providerFilter, StringComparison.OrdinalIgnoreCase)) continue;
                    double v = _days[i].CostFor(Providers[pi]);
                    if (v > 0) { anyProvData = true; provTotals[pi] += v; sum += v; }
                }
                if (!hasProvFilter && sum == 0 && _days[i].Cost > 0) sum = _days[i].Cost;
                if (sum > peak) peak = sum;
            }
            double scalePeak = _dailyTarget > 0 ? Math.Max(peak, _dailyTarget) : peak;

            using (Font fHead = new Font("Segoe UI Semibold", 9.5f))
            using (Font fSmall = new Font("Segoe UI", 8.25f))
            {
                List<string> sumParts = new List<string>();
                for (int pi = 0; pi < Providers.Length; pi++)
                    if (provTotals[pi] > 0) sumParts.Add(Providers[pi] + ": " + Fmt.UsdShort(provTotals[pi]));
                string summaryStr = string.Join("  ·  ", sumParts.ToArray());
                string headText = anyProvData
                    ? (_days.Length == 30 ? L.T("proj.trends.summary", summaryStr) : L.T("proj.trends.summary_n", _days.Length, summaryStr))
                    : L.T("proj.trends.empty");
                if (_monthlyBudget > 0 || _monthCost > 0)
                {
                    double eom = BudgetForecast.EstimateMonthEnd(_monthCost, _days, DateTime.Now);
                    if (eom > 0) headText += "  |  " + L.T("proj.trends.eom", Fmt.Usd(eom));
                }
                if (_dailyTarget > 0)
                    headText += "  |  " + L.T("proj.trends.target", Fmt.UsdShort(_dailyTarget));
                TextRenderer.DrawText(g, headText, fHead, new Rectangle(pad, pad, Width - pad * 2, (int)(20 * s)), Palette.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

                int n = Math.Max(1, _days.Length);
                int availW = Math.Max(40, Width - pad * 2);
                float slot = availW / (float)n;
                float bw = Math.Max(3f, slot - 3f * s);

                using (Pen axis = new Pen(Palette.Border, 1f))
                    g.DrawLine(axis, pad, chartBot, Width - pad, chartBot);

                if (scalePeak > 0)
                {
                    for (int i = 0; i < _days.Length; i++)
                    {
                        float bx = pad + i * slot + (slot - bw) / 2f;
                        float curY = chartBot;
                        bool drewSegment = false;
                        for (int pi = 0; pi < Providers.Length; pi++)
                        {
                            if (hasProvFilter && !string.Equals(Providers[pi], _providerFilter, StringComparison.OrdinalIgnoreCase)) continue;
                            double pv = _days[i].CostFor(Providers[pi]);
                            if (pv <= 0) continue;
                            drewSegment = true;
                            float sh = (float)(pv / scalePeak * (chartBot - chartTop));
                            if (sh < 1.5f) sh = 1.5f;
                            using (SolidBrush br = new SolidBrush(ColorForProvider(Providers[pi])))
                                g.FillRectangle(br, bx, curY - sh, bw, sh);
                            curY -= sh;
                        }
                        if (!hasProvFilter && !drewSegment && _days[i].Cost > 0)
                        {
                            float sh = (float)(_days[i].Cost / scalePeak * (chartBot - chartTop));
                            if (sh < 2f) sh = 2f;
                            using (SolidBrush br = new SolidBrush(Palette.Accent))
                                g.FillRectangle(br, bx, curY - sh, bw, sh);
                        }
                        if (i == _hoverIdx)
                        {
                            using (Pen hp = new Pen(Palette.Text, 1f))
                                g.DrawRectangle(hp, bx, chartTop, bw, chartBot - chartTop);
                        }
                    }

                    if (_dailyTarget > 0)
                    {
                        float ty = chartBot - (float)(_dailyTarget / scalePeak * (chartBot - chartTop));
                        using (Pen tp = new Pen(Color.FromArgb(200, Palette.Amber), Math.Max(1f, s)))
                        {
                            tp.DashStyle = DashStyle.Dash;
                            g.DrawLine(tp, pad, ty, Width - pad, ty);
                        }
                    }
                }

                // Legend row
                int lx = pad;
                int ly = Height - legH;
                for (int pi = 0; pi < Providers.Length; pi++)
                {
                    bool activeProv = !hasProvFilter || string.Equals(Providers[pi], _providerFilter, StringComparison.OrdinalIgnoreCase);
                    Color swCol = activeProv ? ColorForProvider(Providers[pi]) : ThemeUtil.Blend(ColorForProvider(Providers[pi]), Palette.Track, 0.72);
                    Color lblCol = activeProv ? Palette.Dim : Palette.Gray;
                    using (SolidBrush lb = new SolidBrush(swCol))
                        g.FillRectangle(lb, lx, ly + (int)(5 * s), (int)(10 * s), (int)(10 * s));
                    lx += (int)(14 * s);
                    int tw = TextRenderer.MeasureText(g, Providers[pi], fSmall, new Size(200, 24), TextFormatFlags.NoPadding).Width + (int)(12 * s);
                    TextRenderer.DrawText(g, Providers[pi], fSmall, new Rectangle(lx, ly, tw, (int)(20 * s)), lblCol,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    lx += tw;
                }
            }
        }
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
        private float _dpiScale;

        private float GetDpiScale()
        {
            if (_dpiScale <= 0f) { using (Graphics g = CreateGraphics()) _dpiScale = g.DpiX / 96f; }
            return _dpiScale;
        }

        protected override void OnDpiChangedAfterParent(EventArgs e)
        {
            base.OnDpiChangedAfterParent(e);
            _dpiScale = 0f;
        }

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
            float s = GetDpiScale();
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
            using (StringFormat rightFmt = new StringFormat { Alignment = StringAlignment.Far })
            {
                g.DrawString(L.T("proj.legend.less"), legFont, legBrush, new RectangleF(legX - 80 * s, legY - 1, 76 * s, legendBox + 4), rightFmt);
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
        private readonly ProviderTrendsControl _trends = new ProviderTrendsControl();
        private readonly FlowLayoutPanel _tabStrip = new FlowLayoutPanel();
        private readonly Panel _pageHost = new Panel();
        private readonly Panel _projectsPage = new Panel();
        private readonly Panel _trendsPage = new Panel();
        private readonly Panel _sessionsPage = new Panel();
        private readonly Panel _toolsPage = new Panel();
        private readonly List<Button> _tabBtns = new List<Button>();
        private int _tabIndex;
        private readonly Panel _searchPanel = new Panel();
        private readonly Label _searchLabel = new Label();
        private readonly TextBox _searchBox = new TextBox();
        private readonly Label _lblProvFilter = new Label();
        private readonly ComboBox _providerFilter = new ComboBox();
        private static readonly string[] FilterProviders = { "", "Claude", "Antigravity", "Codex", "Gemini CLI", "OpenCode", "Cline", "Copilot" };
        private readonly Panel _summaryPanel = new Panel();
        private readonly Label _summaryLabel = new Label();
        private readonly ListView _list = new ListView();
        private readonly ListView _sessionList = new ListView();
        private readonly ListView _toolList = new ListView();
        private readonly List<ProjectRow> _rows = new List<ProjectRow>();
        private readonly List<ProjectRow> _filteredRows = new List<ProjectRow>();
        private ContextMenuStrip _ctxMenu, _sessMenu, _toolMenu;
        private readonly List<Button> _trendRangeBtns = new List<Button>();
        private Font _boldFont, _tabFont;
        private UsageSnapshot _usageRef;
        private HistoryStore _historyRef;
        private string[] _colTitles;
        private int _sortCol = 3;      // default: 7-Day Cost
        private bool _sortAsc = false; // default: descending

        public ProjectExplorerForm(UsageSnapshot usage, HistoryStore history, double budgetDaily = 0, double budgetMonthly = 0, double monthCost = 0)
        {
            _usageRef = usage;
            _historyRef = history;
            _trends.SetBudgetForecast(budgetDaily, budgetMonthly, monthCost);
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
            Width = (int)Math.Round(840 * s);
            Height = (int)Math.Round(540 * s);
            MinimumSize = new Size((int)Math.Round(640 * s), (int)Math.Round(420 * s));

            BackColor = Palette.Bg;
            ForeColor = Palette.Text;

            BuildRows(usage);
            InitLayout(s, usage, history);

            Load += delegate
            {
                DarkUi.TitleBar(Handle, !Palette.IsLight);
                DarkUi.ControlTheme(_list.Handle, !Palette.IsLight);
                DarkUi.ControlTheme(_sessionList.Handle, !Palette.IsLight);
                DarkUi.ControlTheme(_toolList.Handle, !Palette.IsLight);
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

                string prov;
                if (usage.ProjectProviders != null && usage.ProjectProviders.TryGetValue(name, out prov))
                    r.Provider = prov;
                else
                    r.Provider = "-";

                _rows.Add(r);
            }

            // Urutkan berdasarkan biaya 7 hari terbanyak
            _rows.Sort(delegate(ProjectRow a, ProjectRow b) { return b.WeekCost.CompareTo(a.WeekCost); });
        }

        internal static string SessionAnomalyLabel(SessionCostRecord sr, double avgCost, int totalSessions)
        {
            if (sr == null) return "";
            if (sr.InputSide >= 10000 && sr.CacheHit < 0.25)
                return L.T("proj.sess.lowcache", (int)Math.Round(sr.CacheHit * 100));
            if (sr.Cost >= 5.0 || (totalSessions >= 3 && avgCost > 0 && sr.Cost >= avgCost * 2.5))
                return L.T("proj.sess.highcost");
            return "";
        }

        private void AddTabButton(string title, int index)
        {
            if (_tabFont == null) _tabFont = new Font("Segoe UI Semibold", 9.2f);
            Button b = new Button();
            b.Text = title;
            b.AutoSize = true;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = Palette.Bg;
            b.ForeColor = Palette.Dim;
            b.Font = _tabFont;
            b.Padding = new Padding(10, 5, 10, 5);
            b.Margin = new Padding(0, 0, 4, 0);
            b.Click += delegate { SelectTab(index); };
            b.Paint += delegate(object sender, PaintEventArgs pe)
            {
                if (index != _tabIndex) return;
                using (SolidBrush br = new SolidBrush(Palette.Accent))
                    pe.Graphics.FillRectangle(br, 6, ((Button)sender).Height - 3, ((Button)sender).Width - 12, 3);
            };
            _tabBtns.Add(b);
            _tabStrip.Controls.Add(b);
        }

        private void SelectTab(int i)
        {
            _tabIndex = i;
            _projectsPage.Visible = (i == 0);
            _trendsPage.Visible = (i == 1);
            _sessionsPage.Visible = (i == 2);
            _toolsPage.Visible = (i == 3);
            for (int k = 0; k < _tabBtns.Count; k++)
            {
                _tabBtns[k].ForeColor = (k == i) ? Palette.Text : Palette.Dim;
                _tabBtns[k].Invalidate();
            }
        }

        private void InitLayout(float s, UsageSnapshot usage, HistoryStore history)
        {
            _tabStrip.Dock = DockStyle.Top;
            _tabStrip.Height = (int)Math.Round(34 * s);
            _tabStrip.WrapContents = false;
            _tabStrip.BackColor = Palette.Bg;
            _tabStrip.Padding = new Padding((int)Math.Round(8 * s), (int)Math.Round(2 * s), (int)Math.Round(8 * s), 0);
            _tabStrip.Paint += delegate(object sender, PaintEventArgs pe)
            {
                using (Pen p = new Pen(Palette.Border))
                    pe.Graphics.DrawLine(p, 0, _tabStrip.Height - 1, _tabStrip.Width, _tabStrip.Height - 1);
            };
            AddTabButton(L.T("proj.tab.projects"), 0);
            AddTabButton(L.T("proj.tab.trends"), 1);
            AddTabButton(L.T("proj.tab.sessions"), 2);
            AddTabButton(L.T("proj.tab.tools"), 3);

            _pageHost.Dock = DockStyle.Fill;
            _pageHost.BackColor = Palette.Bg;
            _projectsPage.Dock = DockStyle.Fill; _projectsPage.BackColor = Palette.Bg;
            _trendsPage.Dock = DockStyle.Fill; _trendsPage.BackColor = Palette.Bg;
            _sessionsPage.Dock = DockStyle.Fill; _sessionsPage.BackColor = Palette.Bg;
            _toolsPage.Dock = DockStyle.Fill; _toolsPage.BackColor = Palette.Bg;

            // Panel Atas: Heatmap
            _heatmap.Dock = DockStyle.Top;
            _heatmap.Height = (int)Math.Round(150 * s);
            if (history != null)
            {
                _heatmap.SetDays(history.Series(35));
                _trends.SetDays(history.Series(30));
            }

            // Search / Filter strip
            _searchPanel.Dock = DockStyle.Top;
            _searchPanel.Height = (int)Math.Round(32 * s);
            _searchPanel.Padding = new Padding((int)Math.Round(10 * s), (int)Math.Round(4 * s), (int)Math.Round(10 * s), (int)Math.Round(4 * s));
            _searchPanel.BackColor = Palette.Bg;

            _searchLabel.Text = L.T("proj.search");
            _searchLabel.AutoSize = true;
            _searchLabel.ForeColor = Palette.Dim;
            _searchLabel.Dock = DockStyle.Left;
            _searchLabel.TextAlign = ContentAlignment.MiddleLeft;
            _searchLabel.Padding = new Padding(0, 0, (int)Math.Round(8 * s), 0);

            _searchBox.Width = (int)Math.Round(220 * s);
            _searchBox.Dock = DockStyle.Left;
            _searchBox.BackColor = Palette.Surface;
            _searchBox.ForeColor = Palette.Text;
            _searchBox.BorderStyle = BorderStyle.FixedSingle;
            _searchBox.TextChanged += delegate { OnFilterChanged(); };

            _lblProvFilter.Text = L.T("proj.filter.provider");
            _lblProvFilter.AutoSize = true;
            _lblProvFilter.ForeColor = Palette.Dim;
            _lblProvFilter.Dock = DockStyle.Left;
            _lblProvFilter.TextAlign = ContentAlignment.MiddleLeft;
            _lblProvFilter.Padding = new Padding((int)Math.Round(12 * s), 0, (int)Math.Round(6 * s), 0);

            _providerFilter.DropDownStyle = ComboBoxStyle.DropDownList;
            _providerFilter.Dock = DockStyle.Left;
            _providerFilter.Width = (int)Math.Round(135 * s);
            _providerFilter.FlatStyle = FlatStyle.Flat;
            _providerFilter.BackColor = Palette.Surface;
            _providerFilter.ForeColor = Palette.Text;
            _providerFilter.Items.Add(L.T("proj.filter.all"));
            for (int pi = 1; pi < FilterProviders.Length; pi++) _providerFilter.Items.Add(FilterProviders[pi]);
            _providerFilter.SelectedIndex = 0;
            _providerFilter.SelectedIndexChanged += delegate { OnFilterChanged(); };

            _searchPanel.Controls.Add(_providerFilter);
            _searchPanel.Controls.Add(_lblProvFilter);
            _searchPanel.Controls.Add(_searchBox);
            _searchPanel.Controls.Add(_searchLabel);
            _searchPanel.Height = Math.Max(_searchPanel.Height, _providerFilter.PreferredHeight + (int)Math.Round(8 * s));

            // List View
            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.GridLines = true;
            _list.BackColor = Palette.Surface;
            _list.ForeColor = Palette.Text;
            _list.BorderStyle = BorderStyle.FixedSingle;

            _colTitles = new[]
            {
                L.T("proj.col.project"),
                L.T("proj.col.provider"),
                L.T("proj.col.today"),
                L.T("proj.col.week"),
                L.T("proj.col.tokens"),
                L.T("proj.col.reqs"),
                L.T("proj.col.path")
            };
            _list.Columns.Add(_colTitles[0], (int)Math.Round(150 * s), HorizontalAlignment.Left);
            _list.Columns.Add(_colTitles[1], (int)Math.Round(90 * s), HorizontalAlignment.Left);
            _list.Columns.Add(_colTitles[2], (int)Math.Round(90 * s), HorizontalAlignment.Right);
            _list.Columns.Add(_colTitles[3], (int)Math.Round(90 * s), HorizontalAlignment.Right);
            _list.Columns.Add(_colTitles[4], (int)Math.Round(90 * s), HorizontalAlignment.Right);
            _list.Columns.Add(_colTitles[5], (int)Math.Round(75 * s), HorizontalAlignment.Right);
            _list.Columns.Add(_colTitles[6], (int)Math.Round(225 * s), HorizontalAlignment.Left);
            _list.ColumnClick += delegate(object sender, ColumnClickEventArgs e)
            {
                if (_sortCol == e.Column) _sortAsc = !_sortAsc;
                else { _sortCol = e.Column; _sortAsc = (e.Column == 0 || e.Column == 1 || e.Column == 6); }
                SortRows();
                UpdateColumnHeaders();
                PopulateList();
            };

            // Summary footer strip
            _summaryPanel.Dock = DockStyle.Bottom;
            _summaryPanel.Height = (int)Math.Round(28 * s);
            _summaryPanel.Padding = new Padding((int)Math.Round(10 * s), 0, (int)Math.Round(10 * s), 0);
            _summaryPanel.BackColor = Palette.Surface;
            _summaryPanel.Paint += delegate(object sender, PaintEventArgs pe)
            {
                using (Pen p = new Pen(Palette.Border, 1f))
                    pe.Graphics.DrawLine(p, 0, 0, _summaryPanel.Width, 0);
            };
            _boldFont = new Font(_list.Font, FontStyle.Bold);
            _summaryLabel.Dock = DockStyle.Fill;
            _summaryLabel.Font = _boldFont;
            _summaryLabel.ForeColor = Palette.Text;
            _summaryLabel.TextAlign = ContentAlignment.MiddleLeft;
            _summaryPanel.Controls.Add(_summaryLabel);

            // Menu Klik Kanan (Context Menu)
            _ctxMenu = new ContextMenuStrip();
            ToolStripMenuItem miFolder = new ToolStripMenuItem(L.T("proj.btn.folder"), null, delegate { OpenFolderSelected(); });
            ToolStripMenuItem miTerminal = new ToolStripMenuItem(L.T("proj.btn.terminal"), null, delegate { OpenTerminalSelected(); });
            ToolStripMenuItem miCode = new ToolStripMenuItem(L.T("proj.btn.vscode"), null, delegate { OpenVSCodeSelected(); });
            ToolStripMenuItem miCopy = new ToolStripMenuItem(L.T("proj.btn.copy"), null, delegate { CopyMarkdownTable(); });
            _ctxMenu.Items.Add(miFolder);
            _ctxMenu.Items.Add(miTerminal);
            _ctxMenu.Items.Add(miCode);
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
                miCode.Enabled = pathOk && ProcessHelper.HasVSCode();
            };

            UpdateColumnHeaders();
            PopulateList();
            _list.DoubleClick += delegate { OpenTerminalSelected(); };

            _projectsPage.Controls.Add(_list);
            _projectsPage.Controls.Add(_summaryPanel);
            _projectsPage.Controls.Add(_heatmap);

            // Trends page (with 7d / 30d / 90d range selector)
            FlowLayoutPanel trendBar = new FlowLayoutPanel();
            trendBar.Dock = DockStyle.Top;
            trendBar.Height = (int)Math.Round(30 * s);
            trendBar.BackColor = Palette.Bg;
            trendBar.Padding = new Padding((int)Math.Round(12 * s), (int)Math.Round(4 * s), 0, 0);
            Label lblRange = new Label();
            lblRange.Text = L.T("proj.trends.range");
            lblRange.AutoSize = true;
            lblRange.ForeColor = Palette.Dim;
            lblRange.Margin = new Padding(0, (int)Math.Round(4 * s), (int)Math.Round(6 * s), 0);
            trendBar.Controls.Add(lblRange);
            int[] ranges = { 7, 30, 90 };
            foreach (int rDays in ranges)
            {
                int daysVal = rDays;
                Button rb = new Button();
                rb.Text = daysVal + "d";
                rb.AutoSize = true;
                rb.FlatStyle = FlatStyle.Flat;
                rb.FlatAppearance.BorderColor = Palette.Border;
                rb.BackColor = daysVal == 30 ? Palette.Accent : Palette.Surface;
                rb.ForeColor = daysVal == 30 ? Color.White : Palette.Text;
                rb.Margin = new Padding(0, 0, (int)Math.Round(6 * s), 0);
                rb.Click += delegate
                {
                    if (_historyRef != null) _trends.SetDays(_historyRef.Series(daysVal));
                    foreach (Button b2 in _trendRangeBtns)
                    {
                        bool active = (b2 == rb);
                        b2.BackColor = active ? Palette.Accent : Palette.Surface;
                        b2.ForeColor = active ? Color.White : Palette.Text;
                    }
                };
                _trendRangeBtns.Add(rb);
                trendBar.Controls.Add(rb);
            }
            _trends.Dock = DockStyle.Fill;
            _trendsPage.Controls.Add(_trends);
            _trendsPage.Controls.Add(trendBar);

            // Expensive Sessions page
            _sessionList.Dock = DockStyle.Fill;
            _sessionList.View = View.Details;
            _sessionList.FullRowSelect = true;
            _sessionList.GridLines = true;
            _sessionList.BackColor = Palette.Surface;
            _sessionList.ForeColor = Palette.Text;
            _sessionList.BorderStyle = BorderStyle.FixedSingle;
            _sessionList.Columns.Add(L.T("proj.sess.session"), (int)Math.Round(180 * s), HorizontalAlignment.Left);
            _sessionList.Columns.Add(L.T("proj.col.provider"), (int)Math.Round(90 * s), HorizontalAlignment.Left);
            _sessionList.Columns.Add(L.T("proj.sess.model"), (int)Math.Round(135 * s), HorizontalAlignment.Left);
            _sessionList.Columns.Add(L.T("proj.col.tokens"), (int)Math.Round(85 * s), HorizontalAlignment.Right);
            _sessionList.Columns.Add(L.T("proj.col.reqs"), (int)Math.Round(65 * s), HorizontalAlignment.Right);
            _sessionList.Columns.Add(L.T("proj.sess.cache"), (int)Math.Round(70 * s), HorizontalAlignment.Right);
            _sessionList.Columns.Add(L.T("proj.sess.cost"), (int)Math.Round(80 * s), HorizontalAlignment.Right);
            _sessionList.Columns.Add(L.T("proj.sess.anomaly"), (int)Math.Round(110 * s), HorizontalAlignment.Left);
            PopulateSessionsList(usage);
            _sessMenu = new ContextMenuStrip();
            ToolStripMenuItem smFocus = new ToolStripMenuItem(L.T("proj.sess.focus"), null, delegate
            {
                if (_sessionList.SelectedItems.Count == 0) return;
                SessionCostRecord sr = _sessionList.SelectedItems[0].Tag as SessionCostRecord;
                string p = SelectedSessionPath();
                string hint = sr != null && !string.IsNullOrEmpty(sr.Project) ? sr.Project : (sr != null ? sr.Provider : null);
                if (!ProcessHelper.FocusWindow(hint, p, sr != null ? sr.Provider : null) && p != null)
                    ProcessHelper.OpenTerminal(p);
            });
            ToolStripMenuItem smFolder = new ToolStripMenuItem(L.T("proj.btn.folder"), null, delegate { string p = SelectedSessionPath(); if (p != null) ProcessHelper.OpenFolder(p); });
            ToolStripMenuItem smTerm = new ToolStripMenuItem(L.T("proj.btn.terminal"), null, delegate { string p = SelectedSessionPath(); if (p != null) ProcessHelper.OpenTerminal(p); });
            ToolStripMenuItem smCode = new ToolStripMenuItem(L.T("proj.btn.vscode"), null, delegate { string p = SelectedSessionPath(); if (p != null) ProcessHelper.OpenVSCode(p); });
            ToolStripMenuItem smCopyCompact = new ToolStripMenuItem(L.T("proj.sess.copycompact"), null, delegate { try { Clipboard.SetText("/compact"); } catch { } });
            ToolStripMenuItem smCopy = new ToolStripMenuItem(L.T("proj.sess.copydetail"), null, delegate { CopySelectedSession(); });
            _sessMenu.Items.Add(smFocus);
            _sessMenu.Items.Add(smFolder);
            _sessMenu.Items.Add(smTerm);
            _sessMenu.Items.Add(smCode);
            _sessMenu.Items.Add(new ToolStripSeparator());
            _sessMenu.Items.Add(smCopyCompact);
            _sessMenu.Items.Add(smCopy);
            MenuTheme.Apply(_sessMenu);
            _sessionList.ContextMenuStrip = _sessMenu;
            _sessMenu.Opening += delegate
            {
                string p = SelectedSessionPath();
                bool pathOk = !string.IsNullOrEmpty(p) && Directory.Exists(p);
                bool hasSel = _sessionList.SelectedItems.Count > 0 && (_sessionList.SelectedItems[0].Tag is SessionCostRecord);
                smFocus.Enabled = hasSel;
                smFolder.Enabled = pathOk;
                smTerm.Enabled = pathOk;
                smCode.Enabled = pathOk && ProcessHelper.HasVSCode();
                smCopyCompact.Enabled = hasSel;
                smCopy.Enabled = hasSel;
            };
            _sessionList.DoubleClick += delegate { string p = SelectedSessionPath(); if (p != null) try { ProcessHelper.OpenTerminal(p); } catch { } };
            _sessionsPage.Controls.Add(_sessionList);

            // Tool Analytics page
            _toolList.Dock = DockStyle.Fill;
            _toolList.View = View.Details;
            _toolList.FullRowSelect = true;
            _toolList.GridLines = true;
            _toolList.BackColor = Palette.Surface;
            _toolList.ForeColor = Palette.Text;
            _toolList.BorderStyle = BorderStyle.FixedSingle;
            _toolList.Columns.Add(L.T("proj.tools.tool"), (int)Math.Round(210 * s), HorizontalAlignment.Left);
            _toolList.Columns.Add(L.T("proj.col.provider"), (int)Math.Round(95 * s), HorizontalAlignment.Left);
            _toolList.Columns.Add(L.T("proj.tools.calls"), (int)Math.Round(80 * s), HorizontalAlignment.Right);
            _toolList.Columns.Add(L.T("proj.col.tokens"), (int)Math.Round(95 * s), HorizontalAlignment.Right);
            _toolList.Columns.Add(L.T("proj.tools.avg"), (int)Math.Round(95 * s), HorizontalAlignment.Right);
            _toolList.Columns.Add(L.T("proj.sess.cost"), (int)Math.Round(85 * s), HorizontalAlignment.Right);
            _toolList.Columns.Add(L.T("proj.tools.share"), (int)Math.Round(70 * s), HorizontalAlignment.Right);
            PopulateToolsList(usage);
            _toolMenu = new ContextMenuStrip();
            _toolMenu.Items.Add(new ToolStripMenuItem(L.T("proj.btn.copy"), null, delegate { CopyToolsMarkdown(usage); }));
            MenuTheme.Apply(_toolMenu);
            _toolList.ContextMenuStrip = _toolMenu;
            _toolsPage.Controls.Add(_toolList);

            _pageHost.Controls.Add(_projectsPage);
            _pageHost.Controls.Add(_trendsPage);
            _pageHost.Controls.Add(_sessionsPage);
            _pageHost.Controls.Add(_toolsPage);

            Controls.Add(_pageHost);
            Controls.Add(_searchPanel);
            Controls.Add(_tabStrip);
            SelectTab(0);
        }

        private string SelectedProviderFilter()
        {
            int idx = _providerFilter.SelectedIndex;
            if (idx > 0 && idx < FilterProviders.Length) return FilterProviders[idx];
            return "";
        }

        private bool MatchesFilter(string provider, params string[] fields)
        {
            string pf = SelectedProviderFilter();
            if (!string.IsNullOrEmpty(pf) &&
                (string.IsNullOrEmpty(provider) || provider.IndexOf(pf, StringComparison.OrdinalIgnoreCase) < 0))
                return false;
            string q = (_searchBox.Text ?? "").Trim();
            if (q.Length == 0) return true;
            if (!string.IsNullOrEmpty(provider) && provider.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            for (int i = 0; i < fields.Length; i++)
                if (!string.IsNullOrEmpty(fields[i]) && fields[i].IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private void OnFilterChanged()
        {
            _trends.SetProviderFilter(SelectedProviderFilter());
            PopulateList();
            PopulateSessionsList(_usageRef);
            PopulateToolsList(_usageRef);
        }

        private void PopulateToolsList(UsageSnapshot usage)
        {
            _toolList.Items.Clear();
            List<ToolUsageRecord> list = usage != null ? usage.ToolBreakdown : null;
            if (list == null || list.Count == 0)
            {
                ListViewItem empty = new ListViewItem(L.T("proj.tools.empty"));
                empty.ForeColor = Palette.Dim;
                _toolList.Items.Add(empty);
                return;
            }
            long totalTokens = 0;
            for (int i = 0; i < list.Count; i++)
                if (MatchesFilter(list[i].Provider, list[i].Tool)) totalTokens += list[i].Tokens;
            foreach (ToolUsageRecord tr in list)
            {
                if (!MatchesFilter(tr.Provider, tr.Tool)) continue;
                long avg = tr.Calls > 0 ? tr.Tokens / tr.Calls : 0;
                int share = totalTokens > 0 ? (int)Math.Round((double)tr.Tokens / totalTokens * 100.0) : 0;
                ListViewItem it = new ListViewItem(tr.Tool ?? "-");
                it.SubItems.Add(tr.Provider ?? "-");
                it.SubItems.Add(tr.Calls.ToString(CultureInfo.InvariantCulture));
                it.SubItems.Add(Fmt.Tokens(tr.Tokens));
                it.SubItems.Add(Fmt.Tokens(avg));
                it.SubItems.Add(Fmt.UsdShort(tr.Cost));
                it.SubItems.Add(share + "%");
                it.Tag = tr;
                _toolList.Items.Add(it);
            }
            if (_toolList.Items.Count == 0)
            {
                ListViewItem empty = new ListViewItem(L.T("proj.tools.empty"));
                empty.ForeColor = Palette.Dim;
                _toolList.Items.Add(empty);
            }
        }

        private void CopyToolsMarkdown(UsageSnapshot usage)
        {
            List<ToolUsageRecord> list = usage != null ? usage.ToolBreakdown : null;
            if (list == null || list.Count == 0) return;
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("| Tool / Agent | Provider | Calls | Tokens | Est. Cost |");
                sb.AppendLine("|:---|:---|---:|---:|---:|");
                foreach (ToolUsageRecord tr in list)
                {
                    if (!MatchesFilter(tr.Provider, tr.Tool)) continue;
                    sb.AppendFormat(CultureInfo.InvariantCulture,
                        "| `{0}` | {1} | {2} | {3} | ${4:F2} |\r\n",
                        tr.Tool, tr.Provider, tr.Calls, Fmt.Tokens(tr.Tokens), tr.Cost);
                }
                Clipboard.SetText(sb.ToString());
                MessageBox.Show(this, L.T("proj.copied"), L.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { Log.Write("proj.copytools", ex); }
        }

        private string SelectedSessionPath()
        {
            if (_sessionList.SelectedItems.Count == 0) return null;
            SessionCostRecord sr = _sessionList.SelectedItems[0].Tag as SessionCostRecord;
            if (sr == null || string.IsNullOrEmpty(sr.Project) || _usageRef == null || _usageRef.ProjectPaths == null) return null;
            string p;
            if (_usageRef.ProjectPaths.TryGetValue(sr.Project, out p) && Directory.Exists(p)) return p;
            return null;
        }

        private void CopySelectedSession()
        {
            if (_sessionList.SelectedItems.Count == 0) return;
            SessionCostRecord sr = _sessionList.SelectedItems[0].Tag as SessionCostRecord;
            if (sr == null) return;
            try
            {
                string txt = string.Format(CultureInfo.InvariantCulture,
                    "{0} ({1}) · Model: {2} · Tokens: {3} · Req: {4} · Cache: {5}% · Cost: {6}",
                    sr.Project ?? sr.SessionId, sr.Provider ?? "-", sr.TopModel ?? "-",
                    Fmt.Tokens(sr.TotalTokens), sr.Requests, (int)Math.Round(sr.CacheHit * 100), Fmt.UsdShort(sr.Cost));
                Clipboard.SetText(txt);
                MessageBox.Show(this, L.T("proj.sess.copied"), L.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { Log.Write("proj.copysess", ex); }
        }

        private void PopulateSessionsList(UsageSnapshot usage)
        {
            _sessionList.Items.Clear();
            List<SessionCostRecord> list = usage != null ? usage.ExpensiveSessions : null;
            if (list == null || list.Count == 0)
            {
                ListViewItem empty = new ListViewItem(L.T("proj.sess.empty"));
                empty.ForeColor = Palette.Dim;
                _sessionList.Items.Add(empty);
                return;
            }
            double total = 0;
            for (int i = 0; i < list.Count; i++) total += list[i].Cost;
            double avg = total / list.Count;
            foreach (SessionCostRecord sr in list)
            {
                if (!MatchesFilter(sr.Provider, sr.Project, sr.SessionId, sr.TopModel)) continue;
                string sidShort = string.IsNullOrEmpty(sr.SessionId) ? "" : (sr.SessionId.Length > 10 ? sr.SessionId.Substring(0, 10) : sr.SessionId);
                string label = string.IsNullOrEmpty(sr.Project) ? sidShort : (sr.Project + " · " + sidShort);
                ListViewItem it = new ListViewItem(label);
                it.SubItems.Add(sr.Provider ?? "-");
                it.SubItems.Add(sr.TopModel ?? "-");
                it.SubItems.Add(Fmt.Tokens(sr.TotalTokens));
                it.SubItems.Add(sr.Requests.ToString(CultureInfo.InvariantCulture));
                it.SubItems.Add(string.Format(CultureInfo.InvariantCulture, "{0}%", (int)Math.Round(sr.CacheHit * 100)));
                it.SubItems.Add(Fmt.UsdShort(sr.Cost));
                string anom = SessionAnomalyLabel(sr, avg, list.Count);
                it.SubItems.Add(anom);
                it.Tag = sr;
                if (!string.IsNullOrEmpty(anom)) it.ForeColor = Palette.Amber;
                _sessionList.Items.Add(it);
            }
            if (_sessionList.Items.Count == 0)
            {
                ListViewItem empty = new ListViewItem(L.T("proj.sess.empty"));
                empty.ForeColor = Palette.Dim;
                _sessionList.Items.Add(empty);
            }
        }

        private void UpdateColumnHeaders()
        {
            if (_colTitles == null) return;
            for (int i = 0; i < _list.Columns.Count && i < _colTitles.Length; i++)
            {
                _list.Columns[i].Text = i == _sortCol
                    ? _colTitles[i] + (_sortAsc ? " ▲" : " ▼")
                    : _colTitles[i];
            }
        }

        private void SortRows()
        {
            _rows.Sort(delegate(ProjectRow a, ProjectRow b)
            {
                int cmp;
                switch (_sortCol)
                {
                    case 0: cmp = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); break;
                    case 1: cmp = string.Compare(a.Provider, b.Provider, StringComparison.OrdinalIgnoreCase); break;
                    case 2: cmp = a.TodayCost.CompareTo(b.TodayCost); break;
                    case 4: cmp = a.WeekTokens.CompareTo(b.WeekTokens); break;
                    case 5: cmp = a.WeekRequests.CompareTo(b.WeekRequests); break;
                    case 6: cmp = string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase); break;
                    default: cmp = a.WeekCost.CompareTo(b.WeekCost); break;
                }
                return _sortAsc ? cmp : -cmp;
            });
        }

        private void PopulateList()
        {
            _list.Items.Clear();
            _filteredRows.Clear();
            int shown = 0;
            double sumToday = 0, sumWeek = 0;
            long sumTokens = 0, sumReqs = 0;
            foreach (ProjectRow r in _rows)
            {
                if (!MatchesFilter(r.Provider, r.Name, r.Path))
                    continue;

                _filteredRows.Add(r);
                sumToday += r.TodayCost;
                sumWeek += r.WeekCost;
                sumTokens += r.WeekTokens;
                sumReqs += r.WeekRequests;

                string todayStr = Fmt.UsdShort(r.TodayCost);
                string weekStr = Fmt.UsdShort(r.WeekCost);

                ListViewItem item = new ListViewItem(r.Name);
                item.SubItems.Add(string.IsNullOrEmpty(r.Provider) ? "-" : r.Provider);
                item.SubItems.Add(todayStr);
                item.SubItems.Add(weekStr);
                item.SubItems.Add(Fmt.Tokens(r.WeekTokens));
                item.SubItems.Add(r.WeekRequests.ToString(CultureInfo.InvariantCulture));
                item.SubItems.Add(string.IsNullOrEmpty(r.Path) ? "-" : r.Path);
                item.Tag = r;
                _list.Items.Add(item);
                shown++;
            }
            if (shown == 0)
            {
                ListViewItem empty = new ListViewItem(L.T("proj.empty"));
                empty.ForeColor = Palette.Dim;
                _list.Items.Add(empty);
            }
            else
            {
                ListViewItem totalRow = new ListViewItem(L.T("proj.grandtotal"));
                totalRow.SubItems.Add(string.Format(CultureInfo.InvariantCulture, "({0})", shown));
                totalRow.SubItems.Add(Fmt.UsdShort(sumToday));
                totalRow.SubItems.Add(Fmt.UsdShort(sumWeek));
                totalRow.SubItems.Add(Fmt.Tokens(sumTokens));
                totalRow.SubItems.Add(sumReqs.ToString(CultureInfo.InvariantCulture));
                totalRow.SubItems.Add("-");
                totalRow.Font = _boldFont ?? _list.Font;
                totalRow.ForeColor = Palette.Text;
                totalRow.BackColor = ThemeUtil.Blend(Palette.Track, Palette.Surface, 0.35);
                _list.Items.Add(totalRow);
            }
            _summaryLabel.Text = L.T("proj.summary", shown, Fmt.Usd(sumToday), Fmt.Usd(sumWeek), Fmt.Tokens(sumTokens), sumReqs.ToString("N0", CultureInfo.InvariantCulture));
        }

        private ProjectRow SelectedRowWithPath()
        {
            if (_list.SelectedItems.Count == 0) return null;
            ProjectRow r = _list.SelectedItems[0].Tag as ProjectRow;
            if (r == null || string.IsNullOrEmpty(r.Path) || !Directory.Exists(r.Path)) return null;
            return r;
        }

        private void OpenFolderSelected()
        {
            ProjectRow r = SelectedRowWithPath();
            if (r == null) return;
            try { ProcessHelper.OpenFolder(r.Path); }
            catch (Exception ex) { Log.Write("proj.openfolder", ex); }
        }

        private void OpenTerminalSelected()
        {
            ProjectRow r = SelectedRowWithPath();
            if (r == null) return;
            try { ProcessHelper.OpenTerminal(r.Path); }
            catch (Exception ex) { Log.Write("proj.openterminal", ex); }
        }

        private void OpenVSCodeSelected()
        {
            ProjectRow r = SelectedRowWithPath();
            if (r == null) return;
            try { ProcessHelper.OpenVSCode(r.Path); }
            catch (Exception ex) { Log.Write("proj.openvscode", ex); }
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

            double sumToday = 0, sumWeek = 0;
            long sumTokens = 0, sumReqs = 0;
            foreach (ProjectRow r in rows)
            {
                sumToday += r.TodayCost;
                sumWeek += r.WeekCost;
                sumTokens += r.WeekTokens;
                sumReqs += r.WeekRequests;
                string p = string.IsNullOrEmpty(r.Path) ? "-" : r.Path;
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "| {0} | ${1:F2} | ${2:F2} | {3} | {4} | {5} |\r\n",
                    r.Name, r.TodayCost, r.WeekCost, Fmt.Tokens(r.WeekTokens), r.WeekRequests, p);
            }
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "| **Grand Total** | ${0:F2} | ${1:F2} | {2} | {3} | - |\r\n",
                sumToday, sumWeek, Fmt.Tokens(sumTokens), sumReqs);
            return sb.ToString();
        }

        private void CopyMarkdownTable()
        {
            try
            {
                bool hasFilter = !string.IsNullOrWhiteSpace(_searchBox.Text) || !string.IsNullOrEmpty(SelectedProviderFilter());
                string md = BuildMarkdownTable(_filteredRows.Count > 0 || hasFilter ? _filteredRows : _rows);
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
            if (disposing)
            {
                if (_ctxMenu != null) { _ctxMenu.Dispose(); _ctxMenu = null; }
                if (_sessMenu != null) { _sessMenu.Dispose(); _sessMenu = null; }
                if (_toolMenu != null) { _toolMenu.Dispose(); _toolMenu = null; }
                if (_boldFont != null) { _boldFont.Dispose(); _boldFont = null; }
                if (_tabFont != null) { _tabFont.Dispose(); _tabFont = null; }
            }
            base.Dispose(disposing);
        }
    }
}
