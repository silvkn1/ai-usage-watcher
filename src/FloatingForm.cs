using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClaudeUsageWidget
{
    internal class ViewModel
    {
        public UsageSnapshot Usage;
        public RateLimitSnapshot Limits;
        public OrgCostResult Org;
        public bool AdminEnabled;
        public string ProbeStatus;
        public DayStat[] History;
        public double BudgetDaily, BudgetMonthly, MonthCost;
        public bool ShowProjects;
    }

    internal static class Palette
    {
        public static Color Bg, Text, Dim, Track, Green, Amber, Red, Gray;
        public static Color Surface, Border, Accent, Hover;   // untuk Pengaturan dan menu klik kanan
        public static bool IsLight { get; private set; }

        static Palette() { Apply(false); }

        /// <summary>Tema widget. Terang memakai warna status lebih gelap agar teks tetap terbaca.</summary>
        public static void Apply(bool light)
        {
            IsLight = light;
            if (light)
            {
                Bg = Color.FromArgb(246, 246, 248); Text = Color.FromArgb(28, 28, 32); Dim = Color.FromArgb(96, 96, 106);
                Track = Color.FromArgb(218, 218, 224); Green = Color.FromArgb(26, 127, 55); Amber = Color.FromArgb(166, 100, 0);
                Red = Color.FromArgb(196, 36, 36); Gray = Color.FromArgb(128, 128, 136);
                Surface = Color.FromArgb(255, 255, 255); Border = Color.FromArgb(200, 200, 208);
                Accent = Color.FromArgb(0, 95, 184); Hover = Color.FromArgb(230, 230, 238);
            }
            else
            {
                Bg = Color.FromArgb(28, 28, 32); Text = Color.FromArgb(235, 235, 240); Dim = Color.FromArgb(150, 150, 160);
                Track = Color.FromArgb(60, 60, 68); Green = Color.FromArgb(46, 160, 67); Amber = Color.FromArgb(214, 145, 22);
                Red = Color.FromArgb(218, 54, 51); Gray = Color.FromArgb(110, 110, 120);
                Surface = Color.FromArgb(44, 44, 50); Border = Color.FromArgb(74, 74, 84);
                Accent = Color.FromArgb(90, 150, 255); Hover = Color.FromArgb(58, 58, 66);
            }
        }

        public static Color ForPercent(double p)
        {
            if (p >= 80) return Red;
            if (p >= 50) return Amber;
            return Green;
        }
    }

    /// <summary>Jendela mengambang: tanpa border, TopMost, semi transparan, bisa digeser, opsi klik-tembus.</summary>
    internal class FloatingForm : Form
    {
        private const int WS_EX_TOOLWINDOW = 0x80, WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
        private const int GWL_EXSTYLE = -20, WM_NCLBUTTONDOWN = 0xA1, HTCAPTION = 2, WM_EXITSIZEMOVE = 0x232, WM_DPICHANGED = 0x02E0;

        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int idx);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int idx, int val);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr h);
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

        private ViewModel _vm;
        private bool _compact, _clickThrough;
        private Font _fTitle, _fBody, _fSmall;
        private float _fontScale;

        public event EventHandler PositionCommitted;
        public event EventHandler CompactToggled;

        private readonly bool _popup;          // jendela detail di atas ikon tray: selalu lengkap, bisa aktif, hilang saat kehilangan fokus
        public event EventHandler Dismissed;   // popup ditutup (fokus pindah atau Esc)
        private bool _showInTaskbar;

        public bool ShowInTaskbarMode
        {
            get { return _showInTaskbar; }
            set
            {
                if (_showInTaskbar == value) return;
                _showInTaskbar = value;
                ShowInTaskbar = value;
                if (IsHandleCreated) RecreateHandle();
            }
        }

        public FloatingForm() : this(false) { }

        public FloatingForm(bool popup)
        {
            _popup = popup;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Palette.Bg;
            DoubleBuffered = true;
            Text = "Claude Usage";
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                if (!_showInTaskbar) cp.ExStyle |= WS_EX_TOOLWINDOW; // tidak muncul di Alt-Tab bila bukan mode taskbar
                if (!_popup) cp.ExStyle |= WS_EX_NOACTIVATE;         // widget biasa tidak mencuri fokus; popup perlu aktif agar tahu saat fokus pindah
                if (_clickThrough && !_popup) cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_LAYERED;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return !_popup; } }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (_popup && Visible) Dismiss();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_popup && keyData == Keys.Escape) { Dismiss(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void Dismiss()
        {
            Hide();
            if (Dismissed != null) Dismissed(this, EventArgs.Empty);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4); } catch { }  // sudut membulat Win11
            Relayout();
        }

        public void ApplyTheme()
        {
            BackColor = Palette.Bg;
            Relayout();
            Invalidate();
        }

        public bool Compact
        {
            get { return _compact; }
            set { _compact = value; Relayout(); }
        }

        public bool ClickThrough
        {
            get { return _clickThrough; }
            set { _clickThrough = value; ApplyClickThrough(); }
        }

        private void ApplyClickThrough()
        {
            if (!IsHandleCreated) return;
            bool on = _clickThrough && !_dockTop;   // mode tepi atas butuh hover, jadi klik-tembus diabaikan
            int ex = GetWindowLong(Handle, GWL_EXSTYLE);
            ex = on ? (ex | WS_EX_TRANSPARENT | WS_EX_LAYERED) : (ex & ~WS_EX_TRANSPARENT);
            SetWindowLong(Handle, GWL_EXSTYLE, ex);
        }

        // ---------- mode tempel di tepi atas: ringkas, lengkap saat kursor di atasnya ----------
        private bool _dockTop, _expanded;
        private Timer _hoverTimer;
        private DateTime _outsideSince = DateTime.MinValue;

        /// <summary>Titik tengah horizontal widget saat menempel di tepi atas (koordinat layar).</summary>
        public int DockCenterX { get; set; }

        public bool DockTop
        {
            get { return _dockTop; }
            set
            {
                if (value && !_dockTop && DockCenterX == 0) DockCenterX = Left + Width / 2;
                if (value && !_dockTop && DockCenterY == 0) DockCenterY = Top + Height / 2;
                _dockTop = value;
                _expanded = false;
                if (_hoverTimer == null)
                {
                    _hoverTimer = new Timer();
                    _hoverTimer.Interval = 120;
                    _hoverTimer.Tick += delegate { CheckHover(); };
                }
                _hoverTimer.Enabled = value;
                ApplyClickThrough();
                Relayout();
            }
        }

        private bool ShowCompact { get { return !_popup && (_compact || (_dockTop && !_expanded)); } }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            if (_dockTop && !_expanded) { _expanded = true; _outsideSince = DateTime.MinValue; Relayout(); }
        }

        private void CheckHover()
        {
            if (!_dockTop || !_expanded) return;
            bool menuOpen = ContextMenuStrip != null && ContextMenuStrip.Visible;
            if (menuOpen || Bounds.Contains(Cursor.Position) || MouseButtons != MouseButtons.None) { _outsideSince = DateTime.MinValue; return; }
            if (_outsideSince == DateTime.MinValue) { _outsideSince = DateTime.Now; return; }
            if ((DateTime.Now - _outsideSince).TotalMilliseconds >= 400) { _expanded = false; Relayout(); }
        }

        /// <summary>Tepi tempel: "top", "bottom", "left", atau "right". Widget menempel rata pada tepi area kerja monitor tempel.</summary>
        public string Edge = "top";
        /// <summary>Screen.DeviceName monitor tempel; kosong atau tidak ditemukan = monitor utama.</summary>
        public string MonitorName = "";
        /// <summary>Titik tengah vertikal widget saat menempel di tepi kiri/kanan (koordinat layar).</summary>
        public int DockCenterY { get; set; }

        private Screen DockScreen()
        {
            if (!string.IsNullOrEmpty(MonitorName))
                foreach (Screen s in Screen.AllScreens) if (s.DeviceName == MonitorName) return s;
            return Screen.PrimaryScreen;
        }

        /// <summary>Pusat area kerja monitor tempel (posisi awal di sepanjang tepi).</summary>
        public Point DockDefaultCenter()
        {
            Rectangle wa = DockScreen().WorkingArea;
            return new Point(wa.Left + wa.Width / 2, wa.Top + wa.Height / 2);
        }

        /// <summary>Terapkan ulang tepi/monitor (setelah pengaturan berubah).</summary>
        public void ApplyDock()
        {
            if (_dockTop) Relayout();
        }

        private static int Clamp(int v, int min, int max) { return Math.Max(min, Math.Min(v, max)); }

        private void PositionDocked()
        {
            Rectangle wa = DockScreen().WorkingArea;
            int x, y;
            switch (Edge)
            {
                case "bottom": x = Clamp(DockCenterX - Width / 2, wa.Left, wa.Right - Width); y = wa.Bottom - Height; break;   // tumbuh ke atas
                case "left":   x = wa.Left; y = Clamp(DockCenterY - Height / 2, wa.Top, wa.Bottom - Height); break;
                case "right":  x = wa.Right - Width; y = Clamp(DockCenterY - Height / 2, wa.Top, wa.Bottom - Height); break;
                default:       x = Clamp(DockCenterX - Width / 2, wa.Left, wa.Right - Width); y = wa.Top; break;
            }
            Location = new Point(x, y);
        }

        public void SetData(ViewModel vm)
        {
            _vm = vm;
            if (_showInTaskbar && IsHandleCreated && vm != null && vm.Limits != null)
            {
                TaskbarProgress.Update(Handle, vm.Limits.FiveHour.Available ? vm.Limits.FiveHour.UsedPercent : -1);
            }
            Relayout();
        }

        private float DpiScale
        {
            get
            {
                try { if (IsHandleCreated) return GetDpiForWindow(Handle) / 96f; } catch { }
                return 1f;
            }
        }

        private void EnsureFonts()
        {
            float s = DpiScale;
            if (_fBody != null && Math.Abs(s - _fontScale) < 0.01f) return;
            if (_fBody != null) { _fTitle.Dispose(); _fBody.Dispose(); _fSmall.Dispose(); }
            _fontScale = s;
            // Ukuran dalam piksel agar skala DPI dikendalikan manual.
            _fTitle = new Font("Segoe UI Semibold", 12f * s, FontStyle.Regular, GraphicsUnit.Pixel);
            _fBody = new Font("Segoe UI", 12f * s, FontStyle.Regular, GraphicsUnit.Pixel);
            _fSmall = new Font("Segoe UI", 10.5f * s, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        // ---------- tata letak sebagai daftar baris ----------
        private enum Kind { Title, Text, Bar, Small, Sep, Spark }

        private class Row
        {
            public Kind Kind;
            public string Left, Right;
            public Color Color = Palette.Text;
            public double Percent = -1;
            public double Marker = -1;          // posisi penanda laju (0..1) searah dengan isi bar
            public bool Warn, Faded;
            public double[] Series;             // untuk Kind.Spark
            public DayStat[] Days;              // data harian di balik Series (untuk teks saat kursor di atas batang)
            public Row(Kind k, string l, string r) { Kind = k; Left = l; Right = r; }
        }

        private List<Row> BuildRows()
        {
            List<Row> rows = new List<Row>();
            ViewModel vm = _vm;
            if (vm == null || vm.Usage == null) { rows.Add(new Row(Kind.Text, L.T("loading"), null)); return rows; }
            RateLimitSnapshot rl = vm.Limits ?? new RateLimitSnapshot();
            UsageSnapshot u = vm.Usage;

            if (ShowCompact)
            {
                string line = L.T("compact", PctShort(rl.FiveHour), PctShort(rl.SevenDay), Fmt.UsdShort(u.TodayTotal.Cost));
                Row r = new Row(Kind.Text, line, null);
                rows.Add(r);
                return rows;
            }

            // Jam pesan Claude terakhir yang tercatat di log (CLI maupun desktop): bukti widget ikut refresh tiap chat.
            rows.Add(new Row(Kind.Title, L.AppName, L.T("lastchat", Fmt.Clock(u.LastRecord))));
            // Basi = ada chat setelah snapshot ditangkap (bukan sekadar lama: malam hari tanpa chat tidak basi).
            bool stale = rl.FileFound && u.LastRecord != DateTime.MinValue && u.LastRecord > rl.CapturedAt.AddMinutes(5);
            AddLimit(rows, L.T("limit5"), rl.FiveHour, rl.CapturedAt, Pace.FiveHourSecs, stale);
            AddLimit(rows, L.T("limit7"), rl.SevenDay, rl.CapturedAt, Pace.SevenDaySecs, stale);
            if (!rl.FileFound) rows.Add(new Row(Kind.Small, L.T("nostatusline"), null));
            else rows.Add(new Row(Kind.Small, L.T("snapshot", Fmt.Clock(rl.CapturedAt),
                rl.Source == "cli-probe" ? L.T("src.probe") : L.T("src.statusline")) + (stale ? L.T("snapshot.stale") : ""), null));
            if (!string.IsNullOrEmpty(vm.ProbeStatus)) rows.Add(new Row(Kind.Small, vm.ProbeStatus, null));

            rows.Add(new Row(Kind.Sep, null, null));
            if (!u.Ready) rows.Add(new Row(Kind.Small, L.T("scanning"), null));

            if (u.LastContextTokens > 0)
            {
                double ctxPct = (double)u.LastContextTokens / 200000.0 * 100.0;
                rows.Add(new Row(Kind.Text, L.T("context.title", Fmt.Tokens(u.LastContextTokens)), string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0}%", ctxPct)));
                Row rCtx = new Row(Kind.Bar, null, null);
                rCtx.Percent = Math.Min(100, Math.Max(0, ctxPct));
                rCtx.Color = Palette.ForPercent(ctxPct);
                rows.Add(rCtx);
                if (ctxPct >= 75)
                    rows.Add(new Row(Kind.Small, L.T("context.compact_hint"), null) { Color = Palette.Amber });
            }
            if (u.TokensLast10m > 5000)
            {
                rows.Add(new Row(Kind.Small, L.T("burnrate.row", Fmt.Tokens(u.TokensLast10m)), null));
            }

            rows.Add(new Row(Kind.Text, L.T("today", Fmt.Tokens(u.TodayTotal.Total)), "≈" + Fmt.Usd(u.TodayTotal.Cost)));
            AddModels(rows, u.Today, 3);
            rows.Add(new Row(Kind.Text, L.T("week", Fmt.Tokens(u.WeekTotal.Total)), "≈" + Fmt.Usd(u.WeekTotal.Cost)));
            AddModels(rows, u.Week, 3);

            if (u.WeekTotal.InputSide > 0)   // efisiensi cache (estimasi bersih setelah biaya menulis cache)
                rows.Add(new Row(Kind.Small, L.T("cache.row", (int)Math.Round(u.WeekTotal.CacheHit * 100),
                    Fmt.Usd(u.TodayTotal.Saved), Fmt.Usd(u.WeekTotal.Saved)), null));
            if (vm.BudgetDaily > 0) AddBudget(rows, "budget.day", u.TodayTotal.Cost, vm.BudgetDaily);
            if (vm.BudgetMonthly > 0) AddBudget(rows, "budget.month", vm.MonthCost, vm.BudgetMonthly);
            if (vm.ShowProjects && u.WeekProjects.Count > 0) AddProjects(rows, u.WeekProjects, 5);

            if (vm.History != null && vm.History.Length > 0)
            {
                double[] ser = new double[vm.History.Length];
                double total = 0, peak = 0;
                for (int i = 0; i < ser.Length; i++) { ser[i] = vm.History[i].Cost; total += ser[i]; if (ser[i] > peak) peak = ser[i]; }
                if (total > 0)
                {
                    Row sp = new Row(Kind.Spark, L.T("hist.title", ser.Length, Fmt.Usd(total)), L.T("hist.peak", Fmt.Usd(peak)));
                    sp.Series = ser;
                    sp.Days = vm.History;
                    rows.Add(sp);
                }
            }

            if (vm.AdminEnabled)
            {
                rows.Add(new Row(Kind.Sep, null, null));
                OrgCostResult o = vm.Org;
                if (o == null) rows.Add(new Row(Kind.Small, L.T("admin.wait"), null));
                else if (!o.Ok) rows.Add(new Row(Kind.Small, L.T("admin.fail", o.Error), null));
                else
                {
                    rows.Add(new Row(Kind.Text, L.T("org.month"), Fmt.Usd(o.MonthUsd)));
                    int n = 0;
                    foreach (KeyValuePair<string, double> kv in o.ByModel)
                    {
                        if (n++ >= 3) break;
                        Row r = new Row(Kind.Small, "   " + ShortModel(kv.Key), Fmt.Usd(kv.Value));
                        rows.Add(r);
                    }
                    rows.Add(new Row(Kind.Small, L.T("org.fetched", o.Endpoint, Fmt.Clock(o.FetchedAt)), null));
                }
            }

            rows.Add(new Row(Kind.Sep, null, null));
            rows.Add(new Row(Kind.Small, L.T("footer"), null));
            long unpriced = u.WeekTotal.UnpricedRequests;
            if (unpriced > 0 || u.SkippedLines > 0 || u.WeekTotal.FastRequests > 0)
                rows.Add(new Row(Kind.Small, L.T("stats", unpriced, u.SkippedLines, u.WeekTotal.FastRequests), null));
            return rows;
        }

        private static void AddBudget(List<Row> rows, string key, double cost, double budget)
        {
            int pct = (int)Math.Round(cost / budget * 100);
            Row r = new Row(Kind.Small, L.T(key, Fmt.Usd(cost), Fmt.Usd(budget), pct), null);
            r.Warn = pct >= 80;
            rows.Add(r);
        }

        private static void AddProjects(List<Row> rows, Dictionary<string, Tally> d, int max)
        {
            List<KeyValuePair<string, Tally>> list = new List<KeyValuePair<string, Tally>>(d);
            list.Sort(delegate(KeyValuePair<string, Tally> a, KeyValuePair<string, Tally> b) { return b.Value.Cost.CompareTo(a.Value.Cost); });
            rows.Add(new Row(Kind.Small, L.T("proj.title"), null));
            int n = 0;
            foreach (KeyValuePair<string, Tally> kv in list)
            {
                if (n++ >= max) break;
                string name = kv.Key.Length > 28 ? kv.Key.Substring(0, 27) + "…" : kv.Key;
                rows.Add(new Row(Kind.Small, "   " + name, "≈" + Fmt.Usd(kv.Value.Cost)));
            }
        }

        private static string PctShort(LimitWindow w)
        {
            if (!w.Available) return L.T("na");
            if (w.IsStale) return L.T("reset.short");
            int used = (int)Math.Round(w.UsedPercent);
            return Opts.ShowRemaining ? L.T("pct.left", 100 - used) : used + "%";
        }

        private static void AddLimit(List<Row> rows, string label, LimitWindow w, DateTime captured, double windowSecs, bool stale)
        {
            if (!w.Available) { Row na = new Row(Kind.Text, label, L.T("unavailable")); na.Color = Palette.Dim; rows.Add(na); return; }
            if (w.IsStale)
            {
                Row st = new Row(Kind.Text, label, L.T("alreadyreset", Fmt.Clock(w.ResetsAt)));
                st.Color = Palette.Dim; rows.Add(st); return;
            }
            int used = (int)Math.Round(w.UsedPercent);
            Row r = new Row(Kind.Bar, Opts.ShowRemaining ? L.T("limit.left", label, 100 - used) : label + "  " + used + "%",
                w.ResetsAt == DateTime.MinValue ? "" : L.T("resetin.at", Fmt.Until(w.ResetsAt), Fmt.Clock(w.ResetsAt)));
            r.Percent = Opts.ShowRemaining ? 100 - w.UsedPercent : w.UsedPercent;   // isi bar mengikuti mode
            r.Color = Palette.ForPercent(w.UsedPercent);                              // warna tetap menurut % terpakai
            r.Faded = stale;
            double e = Pace.ElapsedAt(w, DateTime.Now, windowSecs);
            if (!double.IsNaN(e)) r.Marker = Opts.ShowRemaining ? 1 - e : e;          // penanda ikut arah isi bar
            rows.Add(r);

            TimeSpan hit;
            if (Pace.TryForecast(w, captured, windowSecs, stale, out hit))
            {
                Row f = new Row(Kind.Small, hit < TimeSpan.FromMinutes(1) ? L.T("pace.now") : L.T("pace.hit", Fmt.Until(DateTime.Now + hit)), null);
                f.Warn = true;
                rows.Add(f);
            }
        }

        private static void AddModels(List<Row> rows, Dictionary<string, Tally> d, int max)
        {
            List<KeyValuePair<string, Tally>> list = new List<KeyValuePair<string, Tally>>(d);
            list.Sort(delegate(KeyValuePair<string, Tally> a, KeyValuePair<string, Tally> b) { return b.Value.Total.CompareTo(a.Value.Total); });
            int n = 0;
            foreach (KeyValuePair<string, Tally> kv in list)
            {
                if (n++ >= max) break;
                string cost = kv.Value.UnpricedRequests > 0 && kv.Value.Cost == 0 ? "?" : "≈" + Fmt.Usd(kv.Value.Cost);
                rows.Add(new Row(Kind.Small, "   " + ShortModel(kv.Key) + "  " + Fmt.Tokens(kv.Value.Total), cost));
            }
        }

        private static string ShortModel(string m)
        {
            return m != null && m.StartsWith("claude-") ? m.Substring(7) : m;
        }

        private int RowHeight(Row r, float s)
        {
            switch (r.Kind)
            {
                case Kind.Title: return (int)(22 * s);
                case Kind.Bar: return (int)(30 * s);
                case Kind.Spark: return (int)(50 * s);
                case Kind.Small: return (int)(16 * s);
                case Kind.Sep: return (int)(9 * s);
                default: return (int)(19 * s);
            }
        }

        private Font FontFor(Row r)
        {
            if (r.Kind == Kind.Title) return _fTitle;
            if (r.Kind == Kind.Small || r.Kind == Kind.Spark) return _fSmall;
            return _fBody;
        }

        private void Relayout()
        {
            if (!IsHandleCreated) return;
            EnsureFonts();
            float s = DpiScale;
            int pad = (int)(10 * s);
            List<Row> rows = BuildRows();
            int w = 0, h = pad * 2;
            using (Graphics g = CreateGraphics())
            {
                foreach (Row r in rows)
                {
                    h += RowHeight(r, s);
                    if (r.Kind == Kind.Sep) continue;
                    Font f = FontFor(r);
                    int lw = TextRenderer.MeasureText(g, r.Left ?? "", f).Width;
                    int rw = string.IsNullOrEmpty(r.Right) ? 0 : TextRenderer.MeasureText(g, r.Right, f).Width + (int)(16 * s);
                    w = Math.Max(w, lw + rw);
                }
            }
            int minW = ShowCompact ? 0 : (int)(270 * s);
            Size = new Size(Math.Max(minW, w + pad * 2), h);
            if (_dockTop) PositionDocked(); else KeepOnScreen();
            Invalidate();
        }

        /// <summary>Saat ukuran berubah, geser agar tetap di area kerja (tidak tertutup taskbar).</summary>
        private void KeepOnScreen()
        {
            Rectangle wa = Screen.FromRectangle(Bounds).WorkingArea;
            int left = Left, top = Top;
            if (left + Width > wa.Right) left = wa.Right - Width;
            if (top + Height > wa.Bottom) top = wa.Bottom - Height;
            if (left < wa.Left) left = wa.Left;
            if (top < wa.Top) top = wa.Top;
            if (left != Left || top != Top) Location = new Point(left, top);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            EnsureFonts();
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            float s = DpiScale;
            int pad = (int)(10 * s);
            int y = pad, x = pad, right = ClientSize.Width - pad;
            _sparkN = 0;   // diisi ulang saat baris grafik digambar

            foreach (Row r in BuildRows())
            {
                int rh = RowHeight(r, s);
                if (r.Kind == Kind.Sep)
                {
                    using (Pen p = new Pen(Palette.Track, Math.Max(1f, s)))
                        g.DrawLine(p, x, y + rh / 2, right, y + rh / 2);
                    y += rh; continue;
                }
                Font f = FontFor(r);
                bool dimKind = r.Kind == Kind.Small || r.Kind == Kind.Spark;
                Color left = dimKind ? (r.Warn ? Palette.Amber : Palette.Dim) : (r.Kind == Kind.Bar ? Palette.Text : r.Color);
                int textH = r.Kind == Kind.Bar ? (int)(18 * s) : (r.Kind == Kind.Spark ? (int)(16 * s) : rh);
                Rectangle rect = new Rectangle(x, y, right - x, textH);
                string leftText = r.Left, rightText = r.Right;
                if (r.Kind == Kind.Spark && _hoverBar >= 0 && r.Days != null && _hoverBar < r.Days.Length)
                {
                    // Kursor di atas batang: ganti label dengan rincian hari itu (hanya saat menggambar; tidak mengubah ukuran widget).
                    DayStat d = r.Days[_hoverBar];
                    leftText = L.T("hist.hover", d.Date.ToString("ddd dd MMM", L.Culture), Fmt.Usd(d.Cost), Fmt.Tokens(d.Tokens), d.Requests);
                    rightText = null;
                    left = Palette.Text;
                }
                TextRenderer.DrawText(g, leftText ?? "", f, rect, left, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                if (!string.IsNullOrEmpty(rightText))
                    TextRenderer.DrawText(g, rightText, f, rect, r.Kind == Kind.Title || dimKind ? Palette.Dim : r.Color,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                if (r.Kind == Kind.Bar)
                {
                    int bh = Math.Max(4, (int)(6 * s));
                    int by = y + textH + (int)(2 * s);
                    using (SolidBrush tb = new SolidBrush(Palette.Track)) g.FillRectangle(tb, x, by, right - x, bh);
                    int fw = (int)((right - x) * Math.Max(0, Math.Min(100, r.Percent)) / 100.0);
                    Color fc = r.Faded ? ThemeUtil.Blend(r.Color, Palette.Track, 0.55) : r.Color;   // snapshot basi: pudar
                    using (SolidBrush fb = new SolidBrush(fc)) g.FillRectangle(fb, x, by, fw, bh);
                    if (r.Marker >= 0)   // penanda laju: posisi waktu yang sudah berlalu
                    {
                        int mx = x + (int)((right - x) * Math.Max(0, Math.Min(1, r.Marker)));
                        using (Pen mp = new Pen(Palette.Text, Math.Max(1.5f, 1.5f * s)))
                            g.DrawLine(mp, mx, by - 2 * s, mx, by + bh + 2 * s);
                    }
                }
                else if (r.Kind == Kind.Spark && r.Series != null && r.Series.Length > 0)
                {
                    int top = y + textH + (int)(3 * s), bottom = y + rh - (int)(3 * s);
                    int n = r.Series.Length;
                    double peak = 0;
                    foreach (double v in r.Series) if (v > peak) peak = v;
                    float slot = (right - x) / (float)n, bw = Math.Max(2f, slot - 2f * s);
                    _sparkN = n; _sparkX = x; _sparkSlot = slot; _sparkRowTop = y; _sparkRowBottom = y + rh;   // geometri untuk hover
                    using (Pen basePen = new Pen(Palette.Track, Math.Max(1f, s)))
                        g.DrawLine(basePen, x, bottom, right, bottom);
                    for (int i = 0; i < n; i++)
                    {
                        float hgt = peak <= 0 ? 0 : (float)(r.Series[i] / peak) * (bottom - top);
                        if (r.Series[i] > 0 && hgt < 2f * s) hgt = 2f * s;
                        Color bc = i == _hoverBar ? Palette.Accent : (i == n - 1 ? Palette.Green : ThemeUtil.Blend(Palette.Dim, Palette.Track, 0.35));
                        using (SolidBrush bb = new SolidBrush(bc))
                            g.FillRectangle(bb, x + i * slot + (slot - bw) / 2f, bottom - hgt, bw, hgt);
                    }
                }
                y += rh;
            }
            if (_popup)   // bingkai tipis agar popup terlihat terpisah dari latar
                using (Pen bp = new Pen(Palette.Border)) g.DrawRectangle(bp, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        // ---------- hover pada grafik riwayat: batang yang disorot dan rincian harinya ----------
        private int _hoverBar = -1, _sparkN;
        private float _sparkX, _sparkSlot;
        private int _sparkRowTop, _sparkRowBottom;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int idx = -1;
            if (_sparkN > 0 && e.Y >= _sparkRowTop && e.Y <= _sparkRowBottom && e.X >= _sparkX && e.X < _sparkX + _sparkSlot * _sparkN)
            {
                idx = (int)((e.X - _sparkX) / _sparkSlot);
                if (idx < 0 || idx >= _sparkN) idx = -1;
            }
            if (idx != _hoverBar) { _hoverBar = idx; Invalidate(); }   // hanya saat batang yang disorot berubah
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverBar != -1) { _hoverBar = -1; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_popup) return;
            if (e.Button != MouseButtons.Left) return;
            if (e.Clicks >= 2)
            {
                if (!_dockTop && CompactToggled != null) CompactToggled(this, EventArgs.Empty);   // di tepi atas: ringkas/lengkap mengikuti hover
                return;
            }
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_EXITSIZEMOVE)
            {
                // Di mode tepi atas, geser hanya mengubah posisi horizontal; widget kembali menempel ke tepi atas.
                if (_dockTop) { DockCenterX = Left + Width / 2; DockCenterY = Top + Height / 2; PositionDocked(); }
                if (PositionCommitted != null) PositionCommitted(this, EventArgs.Empty);
            }
            else if (m.Msg == WM_DPICHANGED)
            {
                RECT rc = (RECT)Marshal.PtrToStructure(m.LParam, typeof(RECT));
                Location = new Point(rc.Left, rc.Top);
                Relayout();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        /// <summary>Pastikan jendela berada di layar yang terlihat (mis. setelah monitor dicabut).</summary>
        public void PlaceAt(int x, int y, bool hasPos)
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Point p = hasPos ? new Point(x, y) : new Point(wa.Right - Width - 16, wa.Bottom - Height - 16);
            bool visible = false;
            foreach (Screen sc in Screen.AllScreens)
                if (sc.WorkingArea.IntersectsWith(new Rectangle(p, new Size(Math.Max(40, Width), 40)))) { visible = true; break; }
            if (!visible) p = new Point(wa.Right - Width - 16, wa.Bottom - Height - 16);
            Location = p;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _fBody != null) { _fTitle.Dispose(); _fBody.Dispose(); _fSmall.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
