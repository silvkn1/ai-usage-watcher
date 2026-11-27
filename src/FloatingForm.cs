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
        public bool ScanClaude = true;
        public bool ScanGemini = true;
        public bool ScanCodex = true;
        public bool ScanGeminiCli = true;
        public bool ScanOpenCode = true;
        public OllamaStatus Ollama;
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

        public static Color ClaudeColor { get { return IsLight ? Color.FromArgb(198, 90, 55) : Color.FromArgb(217, 119, 87); } }
        public static Color AntigravityColor { get { return IsLight ? Color.FromArgb(24, 115, 232) : Color.FromArgb(66, 133, 244); } }
        public static Color CodexColor { get { return IsLight ? Color.FromArgb(16, 138, 101) : Color.FromArgb(16, 163, 127); } }
        public static Color GeminiCliColor { get { return IsLight ? Color.FromArgb(14, 116, 144) : Color.FromArgb(56, 189, 248); } }
        public static Color OpenCodeColor { get { return IsLight ? Color.FromArgb(147, 51, 234) : Color.FromArgb(192, 132, 252); } }
        public static Color ConfirmColor { get { return IsLight ? Color.FromArgb(124, 58, 237) : Color.FromArgb(192, 132, 252); } }
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
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

        private ViewModel _vm;
        private bool _compact, _clickThrough;
        private Font _fTitle, _fBody, _fSmall;
        private float _fontScale;

        public event EventHandler PositionCommitted;
        public event EventHandler CompactToggled;
        public event EventHandler ClaudeCardDoubleClicked;
        public event EventHandler AntigravityCardDoubleClicked;
        public event EventHandler<string> CardDoubleClicked;
        public event EventHandler DoneSessionsClicked;
        public event EventHandler<MouseEventArgs> SessionMenuRequested;
        public event EventHandler CopyCompactRequested;
        public event EventHandler OpenActiveTerminalRequested;
        public event EventHandler SettingsRequested;
        public event EventHandler PresetCycleRequested;

        internal int CardPage;
        private readonly List<KeyValuePair<string, Rectangle>> _cardRects = new List<KeyValuePair<string, Rectangle>>();
        private Rectangle _cardsSwitchRect = Rectangle.Empty;
        private Rectangle _ctxCopyRect = Rectangle.Empty;
        private Rectangle _ctxTermRect = Rectangle.Empty;
        private Rectangle _pillSessionRect = Rectangle.Empty;
        private Rectangle _pillDoneRect = Rectangle.Empty;
        private Rectangle _sessRowRect = Rectangle.Empty;
        private Rectangle _titleCompactRect = Rectangle.Empty;
        private Rectangle _titleModeRect = Rectangle.Empty;
        private Rectangle _titleSettingsRect = Rectangle.Empty;
        private readonly ToolTip _pillTip = new ToolTip();
        private string _lastTipText;
        private Timer _pulseTimer;
        private bool _pulsePhase;

        private readonly bool _popup;          // jendela detail di atas ikon tray: selalu lengkap, bisa aktif, hilang saat kehilangan fokus
        public event EventHandler Dismissed;   // popup ditutup (fokus pindah atau Esc)

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
            Text = L.AppName;
            try { Icon ic = AppIcon.Get(); if (ic != null) Icon = ic; } catch { }
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW;                      // tidak pernah muncul di taskbar atau Alt-Tab
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
            DwmBackdrop.Apply(Handle, Backdrop, !Palette.IsLight);
            Relayout();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) Relayout();
            UpdatePulseTimer();
        }

        private void UpdatePulseTimer()
        {
            SessionStatusSummary sess = (_vm != null && _vm.Usage != null) ? _vm.Usage.Sessions : null;
            bool needPulse = Visible && !_popup && sess != null && (sess.RunningCount > 0 || sess.ConfirmCount > 0);
            if (needPulse && _pulseTimer == null)
            {
                _pulseTimer = new Timer();
                _pulseTimer.Interval = 600;
                _pulseTimer.Tick += delegate
                {
                    _pulsePhase = !_pulsePhase;
                    Invalidate();
                };
            }
            if (_pulseTimer != null) _pulseTimer.Enabled = needPulse;
            if (!needPulse) _pulsePhase = false;
        }

        public void ApplyTheme()
        {
            BackColor = Palette.Bg;
            if (IsHandleCreated) DwmBackdrop.Apply(Handle, Backdrop, !Palette.IsLight);
            Relayout();
            Invalidate();
        }

        public bool Compact
        {
            get { return _compact; }
            set
            {
                _compact = value;
                _expanded = false;
                EnsureHoverTimer();
                Relayout();
            }
        }

        public bool ClickThrough
        {
            get { return _clickThrough; }
            set { _clickThrough = value; ApplyClickThrough(); }
        }

        private bool _tempInteractive;
        public void SetTempInteractive(bool on)
        {
            if (_tempInteractive == on) return;
            _tempInteractive = on;
            ApplyClickThrough();
        }

        private void ApplyClickThrough()
        {
            if (!IsHandleCreated) return;
            bool on = _clickThrough && !_tempInteractive && !_dockTop && !(_compact && !_expanded);   // mode tepi atas & mode pill butuh hover
            int ex = GetWindowLong(Handle, GWL_EXSTYLE);
            ex = on ? (ex | WS_EX_TRANSPARENT | WS_EX_LAYERED) : (ex & ~WS_EX_TRANSPARENT);
            SetWindowLong(Handle, GWL_EXSTYLE, ex);
        }

        // ---------- mode tempel di tepi atas & mode pill: ringkas, lengkap saat kursor di atasnya ----------
        private bool _dockTop, _expanded;
        private bool _snapLeft, _snapRight, _snapTop, _snapBottom;
        private Timer _hoverTimer;
        private DateTime _outsideSince = DateTime.MinValue;

        private void EnsureHoverTimer()
        {
            bool needHover = _dockTop || _compact;
            if (needHover && _hoverTimer == null)
            {
                _hoverTimer = new Timer();
                _hoverTimer.Interval = 120;
                _hoverTimer.Tick += delegate { CheckHover(); };
            }
            if (_hoverTimer != null) _hoverTimer.Enabled = needHover;
            ApplyClickThrough();
        }

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
                EnsureHoverTimer();
                Relayout();
            }
        }

        public bool SnappedLeft { get { return _snapLeft; } }
        public bool SnappedRight { get { return _snapRight; } }
        public bool SnappedTop { get { return _snapTop; } }
        public bool SnappedBottom { get { return _snapBottom; } }

        private bool ShowCompact { get { return !_popup && ((_compact || _dockTop) && !_expanded); } }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            if ((_dockTop || _compact) && !_expanded)
            {
                _expanded = true;
                _outsideSince = DateTime.MinValue;
                Relayout();
            }
        }

        private void CheckHover()
        {
            if ((!_dockTop && !_compact) || !_expanded) return;
            bool menuOpen = ContextMenuStrip != null && ContextMenuStrip.Visible;
            if (menuOpen || Bounds.Contains(Cursor.Position) || MouseButtons != MouseButtons.None)
            {
                _outsideSince = DateTime.MinValue;
                return;
            }
            if (_outsideSince == DateTime.MinValue)
            {
                _outsideSince = DateTime.Now;
                return;
            }
            if ((DateTime.Now - _outsideSince).TotalMilliseconds >= 400)
            {
                _expanded = false;
                Relayout();
            }
        }

        /// <summary>Tepi tempel: "top", "bottom", "left", "right", atau 4 sudut: "top-left", "top-right", "bottom-left", "bottom-right".</summary>
        public string Edge = "top";
        /// <summary>Screen.DeviceName monitor tempel; kosong atau tidak ditemukan = monitor utama.</summary>
        public string MonitorName = "";
        /// <summary>Titik tengah vertikal widget saat menempel di tepi kiri/kanan (koordinat layar).</summary>
        public int DockCenterY { get; set; }
        /// <summary>Jenis efek latar belakang Windows 11: "solid", "acrylic", "mica", "mica-alt".</summary>
        public string Backdrop { get; set; }

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
            float s = DpiScale;
            int margin = (int)(8 * s);
            switch (Edge)
            {
                case "top-left":
                    x = wa.Left + margin;
                    y = wa.Top + margin;
                    break;
                case "top-right":
                    x = wa.Right - Width - margin;
                    y = wa.Top + margin;
                    break;
                case "bottom-left":
                    x = wa.Left + margin;
                    y = wa.Bottom - Height - margin;
                    break;
                case "bottom-right":
                    x = wa.Right - Width - margin;
                    y = wa.Bottom - Height - margin;
                    break;
                case "bottom":
                case "bottom-center":
                    x = Clamp(DockCenterX - Width / 2, wa.Left, wa.Right - Width);
                    y = wa.Bottom - Height;
                    break;
                case "left":
                    x = wa.Left;
                    y = Clamp(DockCenterY - Height / 2, wa.Top, wa.Bottom - Height);
                    break;
                case "right":
                    x = wa.Right - Width;
                    y = Clamp(DockCenterY - Height / 2, wa.Top, wa.Bottom - Height);
                    break;
                default: // "top", "top-center"
                    x = Clamp(DockCenterX - Width / 2, wa.Left, wa.Right - Width);
                    y = wa.Top;
                    break;
            }
            Location = new Point(x, y);
        }

        public void SnapTo(string position, string monitorDeviceName)
        {
            if (!string.IsNullOrEmpty(monitorDeviceName))
                MonitorName = monitorDeviceName;
            Edge = position;
            _dockTop = true;
            PositionDocked();
            DockCenterX = Left + Width / 2;
            DockCenterY = Top + Height / 2;
            if (PositionCommitted != null) PositionCommitted(this, EventArgs.Empty);
            Invalidate();
        }

        public void SetData(ViewModel vm)
        {
            _vm = vm;
            UpdatePulseTimer();
            Relayout();
        }

        private float DpiScale
        {
            get
            {
                float dpi = 1f;
                try { if (IsHandleCreated) dpi = GetDpiForWindow(Handle) / 96f; } catch { }
                float user = (float)(Opts.WidgetScale > 0 ? Opts.WidgetScale : 1.0);
                if (user < 0.85f) user = 0.85f;
                if (user > 1.35f) user = 1.35f;
                return dpi * user;
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
        private enum Kind { Title, Text, Bar, Small, Sep, Spark, Pill, DualCards, ContextBar, SessionChips }

        private class CardCol
        {
            public string Title;
            public Color TitleColor;
            public LimitWindow Limit5;
            public LimitWindow Limit7;
            public DateTime CapturedAt;
            public bool Stale;
            public Tally Today;
            public Dictionary<string, Tally> Models;
        }

        private class Row
        {
            public Kind Kind;
            public string Left, Right;
            public Color Color = Palette.Text;
            public double Percent = -1;
            public double Marker = -1;          // posisi penanda laju (0..1) searah dengan isi bar
            public bool Warn, Faded, ClickDone, ClickSessionMenu, HasTerminalBtn;
            public double[] Series;             // untuk Kind.Spark
            public DayStat[] Days;              // data harian di balik Series
            public CardCol LeftCol, RightCol;   // untuk Kind.DualCards dan Kind.Pill
            public List<CardCol> CardCols;      // untuk grid 3 kolom / pager
            public string PageBadge;
            public Row(Kind k, string l, string r) { Kind = k; Left = l; Right = r; }
        }

        private static bool HasProviderInCards(List<CardCol> shown, string prefix)
        {
            if (shown == null) return false;
            for (int i = 0; i < shown.Count; i++)
                if (shown[i].Title != null && shown[i].Title.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        private List<Row> BuildRows()
        {
            List<Row> rows = new List<Row>();
            ViewModel vm = _vm;
            if (vm == null || vm.Usage == null) { rows.Add(new Row(Kind.Text, L.T("loading"), null)); return rows; }
            RateLimitSnapshot rl = vm.Limits ?? new RateLimitSnapshot();
            UsageSnapshot u = vm.Usage;

            bool scanClaude = vm.ScanClaude;
            bool scanGemini = vm.ScanGemini;
            bool scanCodex = vm.ScanCodex;
            bool scanGeminiCli = vm.ScanGeminiCli;
            bool scanOpenCode = vm.ScanOpenCode;

            List<CardCol> allCols = new List<CardCol>();
            if (scanClaude)
            {
                bool stale = rl.FileFound && u.LastRecord != DateTime.MinValue && u.LastRecord > rl.CapturedAt.AddMinutes(5);
                CardCol cCol = new CardCol();
                cCol.Title = "Claude Code";
                cCol.TitleColor = Palette.ClaudeColor;
                cCol.Limit5 = rl.FiveHour;
                cCol.Limit7 = rl.SevenDay;
                cCol.CapturedAt = rl.CapturedAt;
                cCol.Stale = stale;
                cCol.Today = u.ClaudeToday;
                cCol.Models = u.TodayClaude;
                allCols.Add(cCol);
            }

            if (scanGemini)
            {
                CardCol agCol = new CardCol();
                agCol.Title = "Antigravity";
                agCol.TitleColor = Palette.AntigravityColor;
                agCol.Limit5 = rl.AntigravityFiveHour;
                agCol.Limit7 = rl.AntigravitySevenDay;
                agCol.CapturedAt = u.ScannedAt != DateTime.MinValue ? u.ScannedAt : DateTime.Now;
                agCol.Stale = false;
                agCol.Today = u.AntigravityToday;
                agCol.Models = u.TodayAntigravity;
                allCols.Add(agCol);
            }

            bool codexHasLimits = (rl.CodexFiveHour != null && rl.CodexFiveHour.Available) || (rl.CodexSevenDay != null && rl.CodexSevenDay.Available);
            if (scanCodex && (allCols.Count < 2 || u.CodexWeek.Total > 0 || codexHasLimits))
            {
                if (allCols.Count == 0 || u.CodexWeek.Total > 0 || codexHasLimits)
                {
                    CardCol codexCol = new CardCol();
                    codexCol.Title = "Codex CLI";
                    codexCol.TitleColor = Palette.CodexColor;
                    codexCol.Limit5 = rl.CodexFiveHour;
                    codexCol.Limit7 = rl.CodexSevenDay;
                    codexCol.CapturedAt = u.ScannedAt != DateTime.MinValue ? u.ScannedAt : DateTime.Now;
                    codexCol.Stale = false;
                    codexCol.Today = u.CodexToday;
                    codexCol.Models = u.TodayCodex;
                    allCols.Add(codexCol);
                }
            }

            if (scanGeminiCli && (allCols.Count == 0 || u.GeminiCliWeek.Total > 0))
            {
                CardCol gcCol = new CardCol();
                gcCol.Title = "Gemini CLI";
                gcCol.TitleColor = Palette.GeminiCliColor;
                gcCol.CapturedAt = u.ScannedAt != DateTime.MinValue ? u.ScannedAt : DateTime.Now;
                gcCol.Today = u.GeminiCliToday;
                gcCol.Models = u.TodayGeminiCli;
                allCols.Add(gcCol);
            }

            if (scanOpenCode && (allCols.Count == 0 || u.OpenCodeWeek.Total > 0))
            {
                CardCol ocCol = new CardCol();
                ocCol.Title = "OpenCode";
                ocCol.TitleColor = Palette.OpenCodeColor;
                ocCol.CapturedAt = u.ScannedAt != DateTime.MinValue ? u.ScannedAt : DateTime.Now;
                ocCol.Today = u.OpenCodeToday;
                ocCol.Models = u.TodayOpenCode;
                allCols.Add(ocCol);
            }

            if (ShowCompact)
            {
                Row pill = new Row(Kind.Pill, null, null);
                if (string.Equals(Opts.MiniPillMode, "highest", StringComparison.OrdinalIgnoreCase))
                {
                    CardCol best = null;
                    double bestPct = -1;
                    foreach (CardCol col in allCols)
                    {
                        double p = (col.Limit5 != null && col.Limit5.Available && !col.Limit5.IsStale) ? col.Limit5.UsedPercent : -1;
                        if (best == null || p > bestPct) { best = col; bestPct = p; }
                    }
                    pill.LeftCol = best;
                    pill.RightCol = null;
                }
                else
                {
                    pill.LeftCol = allCols.Count > 0 ? allCols[0] : null;
                    pill.RightCol = allCols.Count > 1 ? allCols[1] : null;
                }
                rows.Add(pill);
                return rows;
            }

            // 1. Header
            rows.Add(new Row(Kind.Title, L.AppName, L.T("lastchat", Fmt.Clock(u.LastRecord))));

            // 2. Multi-Column Cards (2-col with optional pager, or 3-col grid)
            List<CardCol> visibleCards = new List<CardCol>();
            if (allCols.Count > 0)
            {
                bool threeCol = string.Equals(Opts.CardLayout, "3col", StringComparison.OrdinalIgnoreCase) && allCols.Count >= 3;
                int perPage = threeCol ? 3 : 2;
                int totalPages = (allCols.Count + perPage - 1) / perPage;
                if (CardPage < 0) CardPage = 0;
                if (CardPage >= totalPages) CardPage = CardPage % totalPages;
                int startIdx = CardPage * perPage;
                for (int i = startIdx; i < allCols.Count && i < startIdx + perPage; i++)
                    visibleCards.Add(allCols[i]);

                Row cards = new Row(Kind.DualCards, null, null);
                cards.CardCols = visibleCards;
                cards.LeftCol = visibleCards.Count > 0 ? visibleCards[0] : null;
                cards.RightCol = visibleCards.Count > 1 ? visibleCards[1] : null;
                if (totalPages > 1)
                    cards.PageBadge = L.T("cards.page", CardPage + 1, totalPages);
                rows.Add(cards);
            }
            else
            {
                rows.Add(new Row(Kind.Text, L.T("set.noproviders"), null));
            }

            // 3. Statusline / snapshot / sync info
            DateTime scanned = u.ScannedAt != DateTime.MinValue ? u.ScannedAt : DateTime.Now;
            DateTime nextSync = scanned.AddMinutes(1);
            bool combinedSyncSnap = scanClaude && rl.FileFound && string.IsNullOrEmpty(vm.ProbeStatus);
            if (combinedSyncSnap)
            {
                bool stale = u.LastRecord != DateTime.MinValue && u.LastRecord > rl.CapturedAt.AddMinutes(5);
                rows.Add(new Row(Kind.Small, L.T("sync.compact", Fmt.Clock(scanned), Fmt.Clock(rl.CapturedAt),
                    (rl.Source == "cli-probe" ? L.T("src.probe") : L.T("src.statusline")) + (stale ? L.T("snapshot.stale") : "")), null));
            }
            else
            {
                rows.Add(new Row(Kind.Small, L.T("sync.schedule", Fmt.Clock(scanned), Fmt.Clock(nextSync)), null));
            }

            if (u.Sessions != null && (u.Sessions.RunningCount > 0 || u.Sessions.ConfirmCount > 0 || u.Sessions.DoneCount > 0))
            {
                bool hasReview = u.Sessions.ConfirmCount > 0 || u.Sessions.DoneCount > 0;
                string rightAck = u.Sessions.ConfirmCount > 0 && u.Sessions.DoneCount > 0 ? "[? / ✓]" : (u.Sessions.ConfirmCount > 0 ? "[?]" : (u.Sessions.DoneCount > 0 ? "[✓]" : null));
                Row sr = new Row(Kind.SessionChips, L.T("sess.chips.lbl"), rightAck);
                sr.Color = u.Sessions.ConfirmCount > 0 ? Palette.ConfirmColor : (u.Sessions.DoneCount > 0 ? Palette.Green : Palette.Amber);
                sr.ClickDone = hasReview;
                sr.ClickSessionMenu = true;
                rows.Add(sr);
            }

            if (scanGemini && rl.AntigravityPools != null && rl.AntigravityPools.Count >= 2)
            {
                List<string> pParts = new List<string>();
                foreach (KeyValuePair<string, LimitWindow> kv in rl.AntigravityPools)
                {
                    if (kv.Value == null || !kv.Value.Available || kv.Value.IsStale) continue;
                    int pv = (int)Math.Round(Opts.ShowRemaining ? 100.0 - kv.Value.UsedPercent : kv.Value.UsedPercent);
                    pParts.Add(kv.Key + " " + pv + "%");
                    if (pParts.Count >= 4) break;
                }
                if (pParts.Count >= 2)
                {
                    Row pr = new Row(Kind.Small, L.T("ag.pools", string.Join("  ·  ", pParts.ToArray())), null);
                    pr.Color = Palette.AntigravityColor;
                    rows.Add(pr);
                }
            }

            if (scanClaude && !combinedSyncSnap)
            {
                bool stale = rl.FileFound && u.LastRecord != DateTime.MinValue && u.LastRecord > rl.CapturedAt.AddMinutes(5);
                if (!rl.FileFound)
                    rows.Add(new Row(Kind.Small, L.T("nostatusline"), null));
                else
                    rows.Add(new Row(Kind.Small, L.T("snapshot", Fmt.Clock(rl.CapturedAt),
                        rl.Source == "cli-probe" ? L.T("src.probe") : L.T("src.statusline")) + (stale ? L.T("snapshot.stale") : ""), null));
                if (!string.IsNullOrEmpty(vm.ProbeStatus))
                    rows.Add(new Row(Kind.Small, vm.ProbeStatus, null));
            }

            rows.Add(new Row(Kind.Sep, null, null));
            if (!u.Ready) rows.Add(new Row(Kind.Small, L.T("scanning"), null));

            // 4. Total Today
            string todayLeft = L.T("today", Fmt.Tokens(u.TodayTotal.Total));
            string todayRight = "≈" + Fmt.Usd(u.TodayTotal.Cost);
            rows.Add(new Row(Kind.Text, todayLeft, todayRight));

            if (vm.ScanCodex && !HasProviderInCards(visibleCards, "Codex") && (u.CodexWeek.Total > 0 || codexHasLimits))
            {
                rows.Add(new Row(Kind.Small, L.T("codex.row", Fmt.Tokens(u.CodexToday.Total)), "≈" + Fmt.Usd(u.CodexToday.Cost)));
                if (codexHasLimits)
                    rows.Add(new Row(Kind.Small, L.T("codex.limits", PctShort(rl.CodexFiveHour), PctShort(rl.CodexSevenDay)), null));
            }

            long ctxCap = ContextLimits.WindowForModel(u.LastContextModel, u.LastContextTokens);
            if (Opts.ShowContextBar && u.LastContextTokens > 0)
            {
                double ctxPct = Math.Min(100.0, Math.Max(0.0, (double)u.LastContextTokens / ctxCap * 100.0));
                Row ctxRow = new Row(Kind.ContextBar, L.T("ctx.bar.cap", Fmt.Tokens(u.LastContextTokens), ContextLimits.FormatCapShort(ctxCap), (int)Math.Round(ctxPct)), null);
                ctxRow.Percent = ctxPct;
                ctxRow.Color = ctxPct >= 75 ? Palette.Red : (ctxPct >= 50 ? Palette.Amber : Palette.Green);
                ctxRow.HasTerminalBtn = !string.IsNullOrEmpty(u.LastActiveCwd);
                rows.Add(ctxRow);
            }

            if (!Opts.ZenMode)
            {
                // 5. Secondary Metrics (Context fallback, Burn, Cache, Ollama)
                List<string> meta = new List<string>();
                if (!Opts.ShowContextBar && u.LastContextTokens > 0)
                {
                    double ctxPct = (double)u.LastContextTokens / ctxCap * 100.0;
                    meta.Add(string.Format("Ctx: {0} ({1:0}%)", Fmt.Tokens(u.LastContextTokens), ctxPct));
                }
                if (u.TokensLast10m > 5000)
                {
                    meta.Add(string.Format("Burn: {0}/10m", Fmt.Tokens(u.TokensLast10m)));
                    double runwayMins = RunwayEstimator.EstimateMinutes(rl, u);
                    if (runwayMins > 0)
                        meta.Add(L.T("runway", Fmt.Runway(runwayMins)));
                }
                if (u.WeekTotal.InputSide > 0)
                {
                    string cacheStr = string.Format("Cache: {0}%", (int)Math.Round(u.WeekTotal.CacheHit * 100));
                    if (u.TodayTotal.Saved > 0.005)
                        cacheStr += string.Format(" (≈{0})", Fmt.Usd(u.TodayTotal.Saved));
                    meta.Add(cacheStr);
                }

                if (meta.Count > 0)
                    rows.Add(new Row(Kind.Small, string.Join("  ·  ", meta.ToArray()), null));

                if (vm.Ollama != null && vm.Ollama.Online)
                {
                    string olRight = "";
                    List<string> olShow = (vm.Ollama.LoadedModels != null && vm.Ollama.LoadedModels.Count > 0)
                        ? vm.Ollama.LoadedModels
                        : vm.Ollama.ModelNames;
                    if (olShow != null && olShow.Count > 0)
                    {
                        int showN = Math.Min(2, olShow.Count);
                        olRight = string.Join(", ", olShow.GetRange(0, showN).ToArray());
                        if (olRight.Length > 28) olRight = olRight.Substring(0, 27) + "…";
                    }
                    string olLeft = vm.Ollama.LoadedCount > 0
                        ? L.T("ollama.online.vram", vm.Ollama.ModelCount, vm.Ollama.LoadedCount,
                            (vm.Ollama.TotalVramBytes / (1024.0 * 1024.0 * 1024.0)).ToString("0.0", L.Culture))
                        : L.T("ollama.online", vm.Ollama.ModelCount);
                    Row olRow = new Row(Kind.Small, olLeft, olRight);
                    olRow.Color = Palette.Green;
                    rows.Add(olRow);
                }

                if (vm.BudgetDaily > 0) AddBudget(rows, "budget.day", u.TodayTotal.Cost, vm.BudgetDaily);
                if (vm.BudgetMonthly > 0) AddBudget(rows, "budget.month", vm.MonthCost, vm.BudgetMonthly);
                if (vm.ShowProjects && u.WeekProjects.Count > 0) AddProjects(rows, u.WeekProjects, 5);

                // 6. Sparkline (7-day history)
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

                // 7. Admin API
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
                            rows.Add(new Row(Kind.Small, "   " + ShortModel(kv.Key), Fmt.Usd(kv.Value)));
                        }
                        rows.Add(new Row(Kind.Small, L.T("org.fetched", o.Endpoint, Fmt.Clock(o.FetchedAt)), null));
                    }
                }

                rows.Add(new Row(Kind.Sep, null, null));
                rows.Add(new Row(Kind.Small, L.T("footer"), null));
                long unpriced = u.WeekTotal.UnpricedRequests;
                if (unpriced > 0 || u.SkippedLines > 0 || u.WeekTotal.FastRequests > 0)
                    rows.Add(new Row(Kind.Small, L.T("stats", unpriced, u.SkippedLines, u.WeekTotal.FastRequests), null));
            }
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

        private static void AddLimit(List<Row> rows, string label, LimitWindow w, DateTime captured, double windowSecs, bool stale, Color? customColor = null)
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
            r.Color = (w.UsedPercent >= 80) ? Palette.Red : (w.UsedPercent >= 50 ? Palette.Amber : (customColor.HasValue ? customColor.Value : Palette.Green));
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
            if (string.IsNullOrEmpty(m)) return m;
            if (m.StartsWith("claude-", StringComparison.OrdinalIgnoreCase)) return m.Substring(7);
            if (m.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase)) return m.Substring(7);
            return m;
        }

        private int RowHeight(Row r, float s)
        {
            switch (r.Kind)
            {
                case Kind.Pill: return (int)(22 * s);
                case Kind.DualCards:
                    bool hasSaved = false;
                    if (r.CardCols != null && r.CardCols.Count > 0)
                    {
                        for (int i = 0; i < r.CardCols.Count; i++)
                            if (r.CardCols[i].Today != null && r.CardCols[i].Today.Saved > 0.005) { hasSaved = true; break; }
                    }
                    else
                    {
                        hasSaved = (r.LeftCol != null && r.LeftCol.Today != null && r.LeftCol.Today.Saved > 0.005)
                                || (r.RightCol != null && r.RightCol.Today != null && r.RightCol.Today.Saved > 0.005);
                    }
                    return (int)((hasSaved ? 134 : 118) * s);
                case Kind.ContextBar: return (int)(24 * s);
                case Kind.SessionChips: return (int)(22 * s);
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
            int pad = ShowCompact ? 0 : (int)(10 * s);
            List<Row> rows = BuildRows();
            int w = 0, h = pad * 2;
            using (Graphics g = CreateGraphics())
            {
                foreach (Row r in rows)
                {
                    h += RowHeight(r, s);
                    if (r.Kind == Kind.Sep || r.Kind == Kind.DualCards || r.Kind == Kind.Pill || r.Kind == Kind.ContextBar || r.Kind == Kind.SessionChips) continue;
                    Font f = FontFor(r);
                    int lw = TextRenderer.MeasureText(g, r.Left ?? "", f).Width;
                    int rw = string.IsNullOrEmpty(r.Right) ? 0 : TextRenderer.MeasureText(g, r.Right, f).Width + (int)(16 * s);
                    if (r.Kind == Kind.Title && !_popup) rw += (int)(58 * s);
                    w = Math.Max(w, lw + rw);
                }
            }
            int minW, maxW = int.MaxValue;
            if (ShowCompact)
            {
                bool costRunway = string.Equals(Opts.MiniPillMode, "cost_runway", StringComparison.OrdinalIgnoreCase);
                bool dual = false;
                foreach (Row r in rows) { if (r.Kind == Kind.Pill && r.LeftCol != null && r.RightCol != null) { dual = true; break; } }
                if (costRunway)
                    minW = Opts.CurrencyCustomEnabled ? (int)(225 * s) : (int)(200 * s);
                else if (dual)
                    minW = Opts.CurrencyCustomEnabled ? (int)(250 * s) : (int)(225 * s);
                else
                    minW = Opts.CurrencyCustomEnabled ? (int)(170 * s) : (int)(150 * s);
                if (_vm != null && _vm.Usage != null && _vm.Usage.Sessions != null)
                {
                    using (Graphics g = CreateGraphics())
                        minW += MeasureSessionBadgeWidth(g, _vm.Usage.Sessions, s);
                }
            }
            else
            {
                bool threeCols = false;
                foreach (Row r in rows) { if (r.Kind == Kind.DualCards && r.CardCols != null && r.CardCols.Count >= 3) { threeCols = true; break; } }
                minW = (int)((threeCols ? 495 : 395) * s);
                maxW = (int)((threeCols ? 560 : 450) * s);
            }
            int targetW = Math.Min(maxW, Math.Max(minW, w + pad * 2));
            int targetH = h;
            if (IsHandleCreated && targetH < 39)
                SetWindowPos(Handle, IntPtr.Zero, Left, Top, targetW, targetH, 0x0014); // SWP_NOZORDER | SWP_NOACTIVATE
            else
                Size = new Size(targetW, targetH);
            if (_dockTop) PositionDocked(); else KeepOnScreen();
            Invalidate();
        }

        /// <summary>Menempelkan jendela ke tepi layar terdekat jika dalam jarak magnetik.</summary>
        public void SnapToEdges()
        {
            Rectangle wa = Screen.FromRectangle(Bounds).WorkingArea;
            float s = DpiScale;
            int snapDist = (int)(20 * s);

            _snapLeft = Math.Abs(Left - wa.Left) <= snapDist;
            _snapRight = !_snapLeft && Math.Abs((Left + Width) - wa.Right) <= snapDist;
            _snapTop = Math.Abs(Top - wa.Top) <= snapDist;
            _snapBottom = !_snapTop && Math.Abs((Top + Height) - wa.Bottom) <= snapDist;

            int left = Left;
            int top = Top;

            if (_snapLeft) left = wa.Left;
            else if (_snapRight) left = wa.Right - Width;

            if (_snapTop) top = wa.Top;
            else if (_snapBottom) top = wa.Bottom - Height;

            if (left + Width > wa.Right) { left = wa.Right - Width; _snapRight = true; }
            if (top + Height > wa.Bottom) { top = wa.Bottom - Height; _snapBottom = true; }
            if (left < wa.Left) { left = wa.Left; _snapLeft = true; }
            if (top < wa.Top) { top = wa.Top; _snapTop = true; }

            if (left != Left || top != Top) Location = new Point(left, top);
        }

        /// <summary>Saat ukuran berubah, geser agar tetap di area kerja dan mempertahankan jangkar tepi.</summary>
        private void KeepOnScreen()
        {
            Rectangle wa = Screen.FromRectangle(Bounds).WorkingArea;
            int left = Left, top = Top;

            if (_snapRight) left = wa.Right - Width;
            else if (_snapLeft) left = wa.Left;

            if (_snapBottom) top = wa.Bottom - Height;
            else if (_snapTop) top = wa.Top;

            if (left + Width > wa.Right) left = wa.Right - Width;
            if (top + Height > wa.Bottom) top = wa.Bottom - Height;
            if (left < wa.Left) left = wa.Left;
            if (top < wa.Top) top = wa.Top;

            if (left != Left || top != Top) Location = new Point(left, top);
        }

        private void DrawPill(Graphics g, Row r, int x, int y, int right, int rh, float s)
        {
            int pillW = right - x;
            Rectangle pillRect = new Rectangle(x, y, pillW, rh);

            using (SolidBrush pb = new SolidBrush(ThemeUtil.Blend(Palette.Track, Palette.Bg, 0.35)))
                g.FillRectangle(pb, pillRect);

            SessionStatusSummary sess = _vm != null && _vm.Usage != null ? _vm.Usage.Sessions : null;
            int badgeW = MeasureSessionBadgeWidth(g, sess, s);
            int mainW = Math.Max(0, pillW - badgeW);
            int mainRight = x + mainW;

            int pad = (int)(3 * s);
            bool hasLeft = (r.LeftCol != null);
            bool hasRight = (r.RightCol != null);
            bool costRunwayMode = string.Equals(Opts.MiniPillMode, "cost_runway", StringComparison.OrdinalIgnoreCase);

            if (costRunwayMode && _vm != null && _vm.Usage != null)
            {
                int segW = mainW / 2;
                if (_vm.BudgetDaily > 0)
                {
                    double bPct = Math.Max(0, Math.Min(100.0, _vm.Usage.TodayTotal.Cost / _vm.BudgetDaily * 100.0));
                    FillPillBar(g, x + 1, y + 1, segW - 1, Math.Max(0, rh - 2), bPct, double.NaN, Palette.ForPercent(bPct), s);
                }
                string costText = Fmt.UsdShort(_vm.Usage.TodayTotal.Cost);
                TextRenderer.DrawText(g, costText, _fBody, new Rectangle(x + pad, y, segW - pad * 2, rh), Palette.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

                double rwMins = RunwayEstimator.EstimateMinutes(_vm.Limits, _vm.Usage);
                string rwText = rwMins > 0 ? L.T("pill.runway", Fmt.Runway(rwMins)) : Fmt.Tokens(_vm.Usage.TodayTotal.Total);
                int seg2X = x + segW + 1;
                TextRenderer.DrawText(g, rwText, _fBody, new Rectangle(seg2X + pad, y, mainRight - seg2X - pad * 2, rh), Palette.Dim,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

                using (Pen dp = new Pen(Palette.Track, 1f))
                    g.DrawLine(dp, x + segW, y + (int)(3 * s), x + segW, y + rh - (int)(3 * s));
            }
            else if (hasLeft && hasRight)
            {
                int segW = mainW / 3;

                // Segment 1
                DrawPillProgress(g, r.LeftCol, x + 1, y + 1, segW - 1, Math.Max(0, rh - 2), s);
                DrawPillSegmentLabel(g, r.LeftCol, sess, x + pad, y, segW - pad * 2, rh, s);

                // Segment 2
                DrawPillProgress(g, r.RightCol, x + segW + 1, y + 1, segW - 1, Math.Max(0, rh - 2), s);
                DrawPillSegmentLabel(g, r.RightCol, sess, x + segW + pad, y, segW - pad * 2, rh, s);

                // Segment 3: Total Spend (with optional daily budget progress background)
                int seg3X = x + segW * 2 + 1;
                int seg3W = Math.Max(0, mainRight - seg3X - 1);
                if (_vm != null && _vm.BudgetDaily > 0 && _vm.Usage != null)
                {
                    double bPct = Math.Max(0, Math.Min(100.0, _vm.Usage.TodayTotal.Cost / _vm.BudgetDaily * 100.0));
                    Color bCol = Palette.ForPercent(bPct);
                    FillPillBar(g, seg3X, y + 1, seg3W, Math.Max(0, rh - 2), bPct, double.NaN, bCol, s);
                }
                Rectangle r3 = new Rectangle(x + segW * 2 + pad, y, mainW - segW * 2 - pad * 2, rh);
                string costText = _vm != null && _vm.Usage != null ? Fmt.UsdShort(_vm.Usage.TodayTotal.Cost) : "$0.00";
                TextRenderer.DrawText(g, costText, _fBody, r3, Palette.Text,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

                // Dividers
                using (Pen dp = new Pen(Palette.Track, 1f))
                {
                    g.DrawLine(dp, x + segW, y + (int)(3 * s), x + segW, y + rh - (int)(3 * s));
                    g.DrawLine(dp, x + segW * 2, y + (int)(3 * s), x + segW * 2, y + rh - (int)(3 * s));
                }
            }
            else if (hasLeft || hasRight)
            {
                CardCol col = hasLeft ? r.LeftCol : r.RightCol;
                int segW = mainW / 2;

                DrawPillProgress(g, col, x + 1, y + 1, segW - 1, Math.Max(0, rh - 2), s);
                DrawPillSegmentLabel(g, col, sess, x + pad, y, segW - pad * 2, rh, s);

                int seg2X = x + segW + 1;
                int seg2W = Math.Max(0, mainRight - seg2X - 1);
                if (_vm != null && _vm.BudgetDaily > 0 && _vm.Usage != null)
                {
                    double bPct = Math.Max(0, Math.Min(100.0, _vm.Usage.TodayTotal.Cost / _vm.BudgetDaily * 100.0));
                    Color bCol = Palette.ForPercent(bPct);
                    FillPillBar(g, seg2X, y + 1, seg2W, Math.Max(0, rh - 2), bPct, double.NaN, bCol, s);
                }
                Rectangle r2 = new Rectangle(x + segW + pad, y, mainW - segW - pad * 2, rh);
                string costText = _vm != null && _vm.Usage != null ? Fmt.UsdShort(_vm.Usage.TodayTotal.Cost) : "$0.00";
                TextRenderer.DrawText(g, costText, _fBody, r2, Palette.Text,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

                using (Pen dp = new Pen(Palette.Track, 1f))
                    g.DrawLine(dp, x + segW, y + (int)(3 * s), x + segW, y + rh - (int)(3 * s));
            }
            else
            {
                Rectangle r0 = new Rectangle(x + pad, y, mainW - pad * 2, rh);
                TextRenderer.DrawText(g, L.T("set.noproviders"), _fBody, r0, Palette.Dim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            if (badgeW > 0 && sess != null)
            {
                using (Pen dp = new Pen(Palette.Track, 1f))
                    g.DrawLine(dp, mainRight, y + (int)(3 * s), mainRight, y + rh - (int)(3 * s));

                Rectangle bRect = new Rectangle(mainRight + 1, y + 1, Math.Max(0, badgeW - 2), Math.Max(0, rh - 2));
                _pillSessionRect = new Rectangle(mainRight, y, badgeW, rh);
                Color badgeTint = sess.ConfirmCount > 0 ? Palette.ConfirmColor : (sess.DoneCount > 0 ? Palette.Green : Palette.Amber);
                using (SolidBrush bb = new SolidBrush(Color.FromArgb(Palette.IsLight ? 42 : 52, badgeTint)))
                    g.FillRectangle(bb, bRect);

                int bx = mainRight + (int)(5 * s);
                int gap = (int)(5 * s);
                if (sess.RunningCount > 0)
                {
                    string runTxt = "●" + sess.RunningCount;
                    int rw = TextRenderer.MeasureText(g, runTxt, _fSmall, new Size(badgeW, rh), TextFormatFlags.NoPadding).Width;
                    Color runCol = _pulsePhase ? Palette.Amber : ThemeUtil.Blend(Palette.Amber, Palette.Text, 0.35);
                    TextRenderer.DrawText(g, runTxt, _fSmall, new Rectangle(bx, y, rw + 2, rh), runCol,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    bx += rw + gap;
                }
                int ackStartX = bx - (int)(2 * s);
                if (sess.ConfirmCount > 0)
                {
                    string cTxt = "?" + sess.ConfirmCount;
                    int cw = TextRenderer.MeasureText(g, cTxt, _fSmall, new Size(badgeW, rh), TextFormatFlags.NoPadding).Width;
                    Color cCol = _pulsePhase ? Palette.ConfirmColor : ThemeUtil.Blend(Palette.ConfirmColor, Palette.Text, 0.30);
                    TextRenderer.DrawText(g, cTxt, _fSmall, new Rectangle(bx, y, cw + 2, rh), cCol,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    bx += cw + gap;
                }
                if (sess.DoneCount > 0)
                {
                    string doneTxt = "✓" + sess.DoneCount;
                    int dw = TextRenderer.MeasureText(g, doneTxt, _fSmall, new Size(badgeW, rh), TextFormatFlags.NoPadding).Width;
                    TextRenderer.DrawText(g, doneTxt, _fSmall, new Rectangle(bx, y, dw + 2, rh), Palette.Green,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }
                if (sess.ConfirmCount > 0 || sess.DoneCount > 0)
                {
                    _pillDoneRect = sess.RunningCount == 0
                        ? _pillSessionRect
                        : new Rectangle(ackStartX, y, Math.Max(0, right - ackStartX), rh);
                }
            }

            using (Pen pp = new Pen(Palette.Border, 1f))
                g.DrawRectangle(pp, x, y, Math.Max(0, pillW - 1), Math.Max(0, rh - 1));
        }

        private int MeasureSessionBadgeWidth(Graphics g, SessionStatusSummary sess, float s)
        {
            if (sess == null || (sess.RunningCount <= 0 && sess.ConfirmCount <= 0 && sess.DoneCount <= 0)) return 0;
            int w = (int)(10 * s);
            int parts = 0;
            int gap = (int)(5 * s);
            if (sess.RunningCount > 0)
            {
                w += TextRenderer.MeasureText(g, "●" + sess.RunningCount, _fSmall, new Size(200, 30), TextFormatFlags.NoPadding).Width;
                parts++;
            }
            if (sess.ConfirmCount > 0)
            {
                if (parts > 0) w += gap;
                w += TextRenderer.MeasureText(g, "?" + sess.ConfirmCount, _fSmall, new Size(200, 30), TextFormatFlags.NoPadding).Width;
                parts++;
            }
            if (sess.DoneCount > 0)
            {
                if (parts > 0) w += gap;
                w += TextRenderer.MeasureText(g, "✓" + sess.DoneCount, _fSmall, new Size(200, 30), TextFormatFlags.NoPadding).Width;
            }
            return w;
        }

        private void DrawPillSegmentLabel(Graphics g, CardCol col, SessionStatusSummary sess, int x, int y, int w, int rh, float s)
        {
            bool provRun, provConfirm, provDone;
            GetProviderSessionFlags(col, sess, out provRun, out provConfirm, out provDone);
            int textX = x;
            int textW = w;
            if (provConfirm || provRun || provDone)
            {
                int dotD = Math.Max(4, (int)Math.Round(5f * s));
                int dotY = y + (rh - dotD) / 2;
                Color dotCol = provConfirm
                    ? (_pulsePhase ? Palette.ConfirmColor : ThemeUtil.Blend(Palette.ConfirmColor, Palette.Bg, 0.35))
                    : (provRun
                        ? (_pulsePhase ? Palette.Amber : ThemeUtil.Blend(Palette.Amber, Palette.Bg, 0.45))
                        : Palette.Green);
                using (SolidBrush db = new SolidBrush(dotCol))
                    g.FillEllipse(db, x, dotY, dotD, dotD);
                int shift = dotD + (int)(3 * s);
                textX += shift;
                textW = Math.Max(0, w - shift);
            }
            TextRenderer.DrawText(g, PillText(col), _fBody, new Rectangle(textX, y, textW, rh), col.TitleColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        private static void GetProviderSessionFlags(CardCol col, SessionStatusSummary sess, out bool running, out bool confirm, out bool done)
        {
            running = false; confirm = false; done = false;
            if (col == null || sess == null || col.Title == null) return;
            if (col.Title.IndexOf("Antigravity", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                running = sess.AntigravityRunning; confirm = sess.AntigravityConfirm; done = sess.AntigravityDone;
            }
            else if (col.Title.IndexOf("Codex", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                running = sess.CodexRunning; confirm = sess.CodexConfirm; done = sess.CodexDone;
            }
            else if (col.Title.IndexOf("Gemini", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                running = sess.GeminiCliRunning; confirm = sess.GeminiCliConfirm; done = sess.GeminiCliDone;
            }
            else if (col.Title.IndexOf("OpenCode", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                running = sess.OpenCodeRunning; confirm = sess.OpenCodeConfirm; done = sess.OpenCodeDone;
            }
            else
            {
                running = sess.ClaudeRunning; confirm = sess.ClaudeConfirm; done = sess.ClaudeDone;
            }
        }

        private static string JoinMax(List<string> items, int max)
        {
            if (items == null || items.Count == 0) return "";
            if (items.Count <= max) return string.Join(", ", items.ToArray());
            List<string> sub = items.GetRange(0, max);
            return string.Join(", ", sub.ToArray()) + " +" + (items.Count - max);
        }

        private static void DrawPillProgress(Graphics g, CardCol col, int segX, int segY, int segW, int segH, float s)
        {
            if (col == null || segW <= 0 || segH <= 0) return;
            LimitWindow w = col.Limit5;
            if (w == null || !w.Available || w.IsStale) return;

            double pct = Math.Max(0, Math.Min(100.0, Opts.ShowRemaining ? 100.0 - w.UsedPercent : w.UsedPercent));
            Color barCol = (w.UsedPercent >= 80) ? Palette.Red : (w.UsedPercent >= 50 ? Palette.Amber : col.TitleColor);
            if (col.Stale) barCol = ThemeUtil.Blend(barCol, Palette.Track, 0.55);

            double elapsed = Pace.ElapsedAt(w, DateTime.Now, Pace.FiveHourSecs);
            double marker = double.NaN;
            if (!double.IsNaN(elapsed)) marker = Opts.ShowRemaining ? 1.0 - elapsed : elapsed;

            FillPillBar(g, segX, segY, segW, segH, pct, marker, barCol, s);
        }

        private static void FillPillBar(Graphics g, int x, int y, int w, int h, double pct, double marker, Color barCol, float s)
        {
            if (w <= 0 || h <= 0) return;
            int fillW = (int)Math.Round(w * (pct / 100.0));
            if (fillW > 0)
            {
                using (SolidBrush bgFill = new SolidBrush(Color.FromArgb(Palette.IsLight ? 54 : 68, barCol)))
                    g.FillRectangle(bgFill, x, y, fillW, h);

                int stripeH = Math.Max(2, (int)Math.Round(2f * s));
                if (stripeH < h)
                {
                    using (SolidBrush stripe = new SolidBrush(Color.FromArgb(220, barCol)))
                        g.FillRectangle(stripe, x, y + h - stripeH, fillW, stripeH);
                }
            }
            if (!double.IsNaN(marker))
            {
                int mx = x + (int)Math.Round(w * Math.Max(0, Math.Min(1, marker)));
                using (Pen mp = new Pen(Color.FromArgb(140, Palette.Text), Math.Max(1f, s)))
                    g.DrawLine(mp, mx, y + 1, mx, y + h - 1);
            }
        }

        private static string PillText(CardCol col)
        {
            string prefix = "C";
            if (col.Title != null)
            {
                if (col.Title.IndexOf("Antigravity", StringComparison.OrdinalIgnoreCase) >= 0) prefix = "AG";
                else if (col.Title.IndexOf("Codex", StringComparison.OrdinalIgnoreCase) >= 0) prefix = "CX";
                else if (col.Title.IndexOf("Gemini", StringComparison.OrdinalIgnoreCase) >= 0) prefix = "GM";
                else if (col.Title.IndexOf("OpenCode", StringComparison.OrdinalIgnoreCase) >= 0) prefix = "OC";
            }
            if (col.Limit5 != null && col.Limit5.Available)
                return prefix + " 5h: " + PctShort(col.Limit5);
            if (col.Today != null && col.Today.Total > 0 && (prefix == "CX" || prefix == "GM" || prefix == "OC"))
                return prefix + ": " + Fmt.Tokens(col.Today.Total);
            return prefix + " 5h: " + L.T("na");
        }

        private void DrawCardCol(Graphics g, CardCol col, Rectangle rect, float s)
        {
            using (SolidBrush bg = new SolidBrush(ThemeUtil.Blend(Palette.Surface, Palette.Bg, Palette.IsLight ? 0.30 : 0.45)))
                g.FillRectangle(bg, rect);
            using (Pen border = new Pen(Palette.Border, 1f))
                g.DrawRectangle(border, rect);

            int pad = (int)(6 * s);
            int cx = rect.X + pad;
            int cy = rect.Y + pad;
            int cw = rect.Width - pad * 2;

            int titleH = (int)(16 * s);
            Rectangle titleRect = new Rectangle(cx, cy, cw, titleH);
            TextRenderer.DrawText(g, col.Title ?? "", _fTitle, titleRect, col.TitleColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            cy += titleH + (int)(3 * s);

            DrawMiniLimit(g, "5h", col.Limit5, col.CapturedAt, Pace.FiveHourSecs, col.Stale, col.TitleColor, cx, ref cy, cw, s);
            DrawMiniLimit(g, "7d", col.Limit7, col.CapturedAt, Pace.SevenDaySecs, col.Stale, col.TitleColor, cx, ref cy, cw, s);

            if (col.Today != null)
            {
                int useH = (int)(15 * s);
                Rectangle useRect = new Rectangle(cx, cy, cw, useH);
                string leftStr = Fmt.Tokens(col.Today.Total);
                string rightStr = "≈" + Fmt.Usd(col.Today.Cost);
                TextRenderer.DrawText(g, leftStr, _fSmall, useRect, Palette.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, rightStr, _fSmall, useRect, Palette.Dim,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                cy += useH + (int)(2 * s);

                if (col.Today.Saved > 0.005)
                {
                    int saveH = (int)(12 * s);
                    Rectangle saveRect = new Rectangle(cx, cy, cw, saveH);
                    string saveStr = "⚡ " + L.T("cache.saved", Fmt.Usd(col.Today.Saved), (int)Math.Round(col.Today.CacheHit * 100));
                    TextRenderer.DrawText(g, saveStr, _fSmall, saveRect, Palette.Green,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                    cy += saveH + (int)(2 * s);
                }
            }

            if (col.Models != null && col.Models.Count > 0)
            {
                List<KeyValuePair<string, Tally>> sorted = new List<KeyValuePair<string, Tally>>(col.Models);
                sorted.Sort(delegate(KeyValuePair<string, Tally> a, KeyValuePair<string, Tally> b) { return b.Value.Total.CompareTo(a.Value.Total); });
                int modH = (int)(12 * s);
                int shown = Math.Min(2, sorted.Count);
                for (int i = 0; i < shown; i++)
                {
                    Rectangle modRect = new Rectangle(cx, cy, cw, modH);
                    string mName = ShortModel(sorted[i].Key);
                    string mTok = Fmt.Tokens(sorted[i].Value.Total);
                    int tokW = TextRenderer.MeasureText(g, mTok, _fSmall, new Size(cw, modH), TextFormatFlags.NoPadding).Width + (int)(6 * s);
                    Rectangle nameRect = new Rectangle(cx, cy, Math.Max(10, cw - tokW), modH);
                    TextRenderer.DrawText(g, mName, _fSmall, nameRect, Palette.Dim,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                    TextRenderer.DrawText(g, mTok, _fSmall, modRect, Palette.Dim,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    cy += modH;
                }
            }
        }

        private void DrawMiniLimit(Graphics g, string label, LimitWindow w, DateTime captured, double windowSecs, bool stale, Color accent, int cx, ref int cy, int cw, float s)
        {
            int lineH = (int)(14 * s);
            Rectangle tr = new Rectangle(cx, cy, cw, lineH);
            if (w == null || !w.Available)
            {
                TextRenderer.DrawText(g, label + "  " + L.T("na"), _fSmall, tr, Palette.Dim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                cy += lineH + (int)(6 * s);
                return;
            }
            if (w.IsStale)
            {
                TextRenderer.DrawText(g, label + "  " + L.T("reset.short"), _fSmall, tr, Palette.Dim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                cy += lineH + (int)(6 * s);
                return;
            }

            int used = (int)Math.Round(w.UsedPercent);
            string lStr = label + "  " + (Opts.ShowRemaining ? (100 - used) + "%" : used + "%");
            string rStr = "";
            if (w.ResetsAt != DateTime.MinValue)
            {
                string until = Fmt.Until(w.ResetsAt);
                string allow = Math.Abs(windowSecs - Pace.SevenDaySecs) < 10 ? Pace.DailyAllowance(w, DateTime.Now) : null;
                rStr = !string.IsNullOrEmpty(allow) ? (until + " · " + allow) : (until + " (" + Fmt.Clock(w.ResetsAt) + ")");
            }
            int lW = TextRenderer.MeasureText(g, lStr, _fSmall, new Size(cw, lineH), TextFormatFlags.NoPadding).Width + (int)(4 * s);
            TextRenderer.DrawText(g, lStr, _fSmall, tr, Palette.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (!string.IsNullOrEmpty(rStr) && cw - lW > (int)(20 * s))
            {
                Rectangle rr = new Rectangle(cx + lW, cy, Math.Max(10, cw - lW), lineH);
                TextRenderer.DrawText(g, rStr, _fSmall, rr, Palette.Dim,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
            cy += lineH;

            int bh = Math.Max(3, (int)(4f * s));
            int by = cy + (int)(1 * s);
            using (SolidBrush tb = new SolidBrush(Palette.Track))
                g.FillRectangle(tb, cx, by, cw, bh);

            int fw = (int)(cw * Math.Max(0, Math.Min(100, Opts.ShowRemaining ? 100 - w.UsedPercent : w.UsedPercent)) / 100.0);
            Color barCol = (w.UsedPercent >= 80) ? Palette.Red : (w.UsedPercent >= 50 ? Palette.Amber : accent);
            if (stale) barCol = ThemeUtil.Blend(barCol, Palette.Track, 0.55);
            using (SolidBrush fb = new SolidBrush(barCol))
                g.FillRectangle(fb, cx, by, fw, bh);

            double e = Pace.ElapsedAt(w, DateTime.Now, windowSecs);
            if (!double.IsNaN(e))
            {
                double markerPos = Opts.ShowRemaining ? 1 - e : e;
                int mx = cx + (int)(cw * Math.Max(0, Math.Min(1, markerPos)));
                using (Pen mp = new Pen(Palette.Text, Math.Max(1.2f, 1.2f * s)))
                    g.DrawLine(mp, mx, by - 1, mx, by + bh + 1);
            }
            cy += bh + (int)(4 * s);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (DwmBackdrop.IsBackdropActive(Backdrop))
            {
                CompositingMode prev = e.Graphics.CompositingMode;
                e.Graphics.CompositingMode = CompositingMode.SourceCopy;
                int alpha = (Backdrop == "acrylic") ? 222 : 185;
                Color bg = Palette.IsLight ? Color.FromArgb(alpha, 245, 245, 248) : Color.FromArgb(alpha, 22, 22, 26);
                using (SolidBrush b = new SolidBrush(bg))
                {
                    e.Graphics.FillRectangle(b, ClientRectangle);
                }
                e.Graphics.CompositingMode = prev;
            }
            else
            {
                base.OnPaintBackground(e);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            EnsureFonts();
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            float s = DpiScale;
            int pad = ShowCompact ? 0 : (int)(10 * s);
            int y = pad, x = pad, right = ClientSize.Width - pad;
            _sparkN = 0;   // diisi ulang saat baris grafik digambar
            _cardRects.Clear();
            _cardsSwitchRect = Rectangle.Empty;
            _ctxCopyRect = Rectangle.Empty;
            _ctxTermRect = Rectangle.Empty;
            _pillSessionRect = Rectangle.Empty;
            _pillDoneRect = Rectangle.Empty;
            _sessRowRect = Rectangle.Empty;
            _titleCompactRect = Rectangle.Empty;
            _titleModeRect = Rectangle.Empty;
            _titleSettingsRect = Rectangle.Empty;

            foreach (Row r in BuildRows())
            {
                int rh = RowHeight(r, s);
                if (r.Kind == Kind.Pill)
                {
                    DrawPill(g, r, x, y, right, rh, s);
                    y += rh; continue;
                }
                if (r.Kind == Kind.DualCards)
                {
                    int cardH = rh - (int)(4 * s);
                    List<CardCol> cols = r.CardCols;
                    if (cols == null || cols.Count == 0)
                    {
                        cols = new List<CardCol>();
                        if (r.LeftCol != null) cols.Add(r.LeftCol);
                        if (r.RightCol != null) cols.Add(r.RightCol);
                    }
                    int nCols = cols.Count;
                    if (nCols > 0)
                    {
                        int colGap = (int)(8 * s);
                        int availW = right - x - colGap * (nCols - 1);
                        int colW = Math.Max(40, availW / nCols);
                        int cx = x;
                        for (int ci = 0; ci < nCols; ci++)
                        {
                            int cw = (ci == nCols - 1) ? (right - cx) : colW;
                            Rectangle crect = new Rectangle(cx, y + (int)(2 * s), cw, cardH);
                            RecordCardRect(cols[ci], crect);
                            DrawCardCol(g, cols[ci], crect, s);
                            if (ci == nCols - 1 && !string.IsNullOrEmpty(r.PageBadge))
                            {
                                int padC = (int)(6 * s);
                                int bw = TextRenderer.MeasureText(g, r.PageBadge, _fSmall, new Size(crect.Width, 20), TextFormatFlags.NoPadding).Width + (int)(8 * s);
                                int bh = (int)(16 * s);
                                _cardsSwitchRect = new Rectangle(crect.Right - padC - bw, crect.Y + padC - 1, bw, bh);
                                using (SolidBrush swBg = new SolidBrush(ThemeUtil.Blend(Palette.Accent, Palette.Surface, 0.72)))
                                    g.FillRectangle(swBg, _cardsSwitchRect);
                                TextRenderer.DrawText(g, r.PageBadge, _fSmall, _cardsSwitchRect, Palette.Accent,
                                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                            }
                            cx += cw + colGap;
                        }
                    }
                    y += rh; continue;
                }
                if (r.Kind == Kind.ContextBar)
                {
                    int ctxH = (int)(15 * s);
                    int btnY = y;
                    int ctxRx = right;
                    if (r.HasTerminalBtn)
                    {
                        string tTxt = L.T("ctx.term");
                        int tw = TextRenderer.MeasureText(g, tTxt, _fSmall, new Size(100, ctxH), TextFormatFlags.NoPadding).Width + (int)(8 * s);
                        ctxRx -= tw;
                        _ctxTermRect = new Rectangle(ctxRx, btnY, tw, ctxH);
                        using (SolidBrush ctxTb = new SolidBrush(Palette.Surface)) g.FillRectangle(ctxTb, _ctxTermRect);
                        TextRenderer.DrawText(g, tTxt, _fSmall, _ctxTermRect, Palette.Dim,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                        ctxRx -= (int)(4 * s);
                    }
                    string cTxt = L.T("ctx.copy");
                    int ctxCw = TextRenderer.MeasureText(g, cTxt, _fSmall, new Size(120, ctxH), TextFormatFlags.NoPadding).Width + (int)(8 * s);
                    ctxRx -= ctxCw;
                    _ctxCopyRect = new Rectangle(ctxRx, btnY, ctxCw, ctxH);
                    using (SolidBrush ctxCb = new SolidBrush(ThemeUtil.Blend(r.Color, Palette.Surface, 0.75))) g.FillRectangle(ctxCb, _ctxCopyRect);
                    TextRenderer.DrawText(g, cTxt, _fSmall, _ctxCopyRect, Palette.Text,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

                    Rectangle lblRect = new Rectangle(x, y, Math.Max(10, ctxRx - x - (int)(6 * s)), ctxH);
                    TextRenderer.DrawText(g, r.Left ?? "", _fSmall, lblRect, Palette.Dim,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

                    int ctxBh = Math.Max(3, (int)(5 * s));
                    int ctxBy = y + ctxH + (int)(2 * s);
                    using (SolidBrush tr = new SolidBrush(Palette.Track)) g.FillRectangle(tr, x, ctxBy, right - x, ctxBh);
                    int ctxFw = (int)((right - x) * Math.Max(0, Math.Min(100, r.Percent)) / 100.0);
                    using (SolidBrush ctxFb = new SolidBrush(r.Color)) g.FillRectangle(ctxFb, x, ctxBy, ctxFw, ctxBh);
                    y += rh; continue;
                }
                if (r.Kind == Kind.SessionChips)
                {
                    DrawSessionChipsRow(g, r, x, y, right, rh, s);
                    y += rh; continue;
                }
                if (r.Kind == Kind.Sep)
                {
                    using (Pen p = new Pen(Palette.Track, Math.Max(1f, s)))
                        g.DrawLine(p, x, y + rh / 2, right, y + rh / 2);
                    y += rh; continue;
                }
                Font f = FontFor(r);
                bool dimKind = r.Kind == Kind.Small || r.Kind == Kind.Spark;
                Color left = dimKind ? (r.Warn ? Palette.Amber : (r.Color != Palette.Text ? r.Color : Palette.Dim)) : (r.Kind == Kind.Bar ? Palette.Text : r.Color);
                int textH = r.Kind == Kind.Bar ? (int)(18 * s) : (r.Kind == Kind.Spark ? (int)(16 * s) : rh);
                Rectangle rect = new Rectangle(x, y, right - x, textH);
                string leftText = r.Left, rightText = r.Right;
                if (r.Kind == Kind.Title && !_popup)
                {
                    int bw = (int)(16 * s);
                    int bgap = (int)(3 * s);
                    int by = y + Math.Max(0, (textH - bw) / 2);
                    int rx = right;
                    rx -= bw; _titleSettingsRect = new Rectangle(rx, by, bw, bw);
                    DrawHeaderIconBtn(g, _titleSettingsRect, "⚙");
                    rx -= (bw + bgap); _titleModeRect = new Rectangle(rx, by, bw, bw);
                    DrawHeaderIconBtn(g, _titleModeRect, Opts.ZenMode ? "▤" : "▫");
                    if (!_dockTop)
                    {
                        rx -= (bw + bgap); _titleCompactRect = new Rectangle(rx, by, bw, bw);
                        DrawHeaderIconBtn(g, _titleCompactRect, "—");
                    }
                    rect = new Rectangle(x, y, Math.Max(40, rx - x - (int)(6 * s)), textH);
                }
                if (r.Kind == Kind.Spark && _hoverBar >= 0 && r.Days != null && _hoverBar < r.Days.Length)
                {
                    // Kursor di atas batang: ganti label dengan rincian hari itu (hanya saat menggambar; tidak mengubah ukuran widget).
                    DayStat d = r.Days[_hoverBar];
                    leftText = L.T("hist.hover", d.Date.ToString("ddd dd MMM", L.Culture), Fmt.Usd(d.Cost), Fmt.Tokens(d.Tokens), d.Requests);
                    rightText = null;
                    left = Palette.Text;
                }
                int rightW = 0;
                if (!string.IsNullOrEmpty(rightText))
                {
                    rightW = TextRenderer.MeasureText(g, rightText, f, new Size(rect.Width, rect.Height), TextFormatFlags.NoPadding).Width + (int)(8 * s);
                }
                Rectangle leftRect = rightW > 0 && rightW < rect.Width - (int)(30 * s)
                    ? new Rectangle(rect.X, rect.Y, rect.Width - rightW, rect.Height)
                    : rect;
                TextRenderer.DrawText(g, leftText ?? "", f, leftRect, left, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                if (!string.IsNullOrEmpty(rightText))
                {
                    Color rightCol = r.ClickDone ? Palette.Green : (r.Kind == Kind.Title || dimKind ? Palette.Dim : r.Color);
                    TextRenderer.DrawText(g, rightText, f, rect, rightCol,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    if (r.ClickDone)
                    {
                        _pillDoneRect = new Rectangle(Math.Max(rect.X, rect.Right - rightW), rect.Y, rightW, rect.Height);
                    }
                }
                if (r.ClickSessionMenu)
                {
                    int menuW = _pillDoneRect.IsEmpty ? rect.Width : Math.Max(10, _pillDoneRect.Left - rect.X);
                    _sessRowRect = new Rectangle(rect.X, rect.Y, menuW, rect.Height);
                }
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
            UpdatePillTooltip(e.Location);
        }

        private void DrawHeaderIconBtn(Graphics g, Rectangle r, string glyph)
        {
            using (SolidBrush br = new SolidBrush(Palette.Surface)) g.FillRectangle(br, r);
            using (Pen pn = new Pen(Palette.Border, 1f)) g.DrawRectangle(pn, r.X, r.Y, r.Width - 1, r.Height - 1);
            TextRenderer.DrawText(g, glyph, _fSmall, r, Palette.Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        private void DrawSessionChipsRow(Graphics g, Row r, int x, int y, int right, int rh, float s)
        {
            SessionStatusSummary sess = (_vm != null && _vm.Usage != null) ? _vm.Usage.Sessions : null;
            if (sess == null) return;
            int ackW = 0;
            if (r.ClickDone && !string.IsNullOrEmpty(r.Right))
            {
                ackW = TextRenderer.MeasureText(g, r.Right, _fSmall, new Size(120, rh), TextFormatFlags.NoPadding).Width + (int)(8 * s);
                _pillDoneRect = new Rectangle(right - ackW, y, ackW, rh);
                TextRenderer.DrawText(g, r.Right, _fSmall, _pillDoneRect, Palette.Green,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            string lbl = (r.Left ?? "Sessions") + ":";
            int lw = TextRenderer.MeasureText(g, lbl, _fSmall, new Size(120, rh), TextFormatFlags.NoPadding).Width + (int)(6 * s);
            TextRenderer.DrawText(g, lbl, _fSmall, new Rectangle(x, y, lw, rh), Palette.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            int cx = x + lw;
            int maxChipRight = right - ackW - (int)(4 * s);
            int availW = maxChipRight - cx;
            bool compactLabels = availW < (int)(260 * s);
            List<KeyValuePair<string, Color>> chips = new List<KeyValuePair<string, Color>>();
            if (sess.ConfirmCount > 0)
                chips.Add(new KeyValuePair<string, Color>(
                    compactLabels ? "? " + sess.ConfirmCount : L.T("sess.chip.confirm", sess.ConfirmCount),
                    _pulsePhase ? Palette.ConfirmColor : ThemeUtil.Blend(Palette.ConfirmColor, Palette.Text, 0.25)));
            if (sess.RunningCount > 0)
                chips.Add(new KeyValuePair<string, Color>(
                    compactLabels ? "● " + sess.RunningCount : L.T("sess.chip.run", sess.RunningCount),
                    _pulsePhase ? Palette.Amber : ThemeUtil.Blend(Palette.Amber, Palette.Text, 0.25)));
            if (sess.DoneCount > 0)
                chips.Add(new KeyValuePair<string, Color>(
                    compactLabels ? "✓ " + sess.DoneCount : L.T("sess.chip.done", sess.DoneCount),
                    Palette.Green));

            int chipTop = y + (int)(2 * s);
            int chipH = Math.Max(14, rh - (int)(4 * s));
            int chipGap = (int)(5 * s);
            int firstChipX = cx;
            for (int i = 0; i < chips.Count; i++)
            {
                string txt = chips[i].Key;
                Color col = chips[i].Value;
                int cw = TextRenderer.MeasureText(g, txt, _fSmall, new Size(200, chipH), TextFormatFlags.NoPadding).Width + (int)(10 * s);
                if (cx + cw > maxChipRight && i > 0) break;
                Rectangle cr = new Rectangle(cx, chipTop, Math.Min(cw, Math.Max(16, maxChipRight - cx)), chipH);
                using (SolidBrush cb = new SolidBrush(Color.FromArgb(Palette.IsLight ? 38 : 48, col))) g.FillRectangle(cb, cr);
                using (Pen cp = new Pen(Color.FromArgb(140, col), 1f)) g.DrawRectangle(cp, cr.X, cr.Y, cr.Width - 1, cr.Height - 1);
                TextRenderer.DrawText(g, txt, _fSmall, cr, col,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                cx += cr.Width + chipGap;
            }
            _sessRowRect = new Rectangle(firstChipX, y, Math.Max(20, maxChipRight - firstChipX), rh);
        }

        private void UpdatePillTooltip(Point loc)
        {
            SessionStatusSummary sess = _vm != null && _vm.Usage != null ? _vm.Usage.Sessions : null;
            string tip = null;
            if (!_popup && !_titleSettingsRect.IsEmpty && _titleSettingsRect.Contains(loc))
                tip = L.T("hud.btn.set.tip");
            else if (!_popup && !_titleModeRect.IsEmpty && _titleModeRect.Contains(loc))
                tip = L.T("hud.btn.zen.tip");
            else if (!_popup && !_titleCompactRect.IsEmpty && _titleCompactRect.Contains(loc))
                tip = L.T("hud.btn.pill.tip");
            else if (sess != null && (sess.RunningCount > 0 || sess.ConfirmCount > 0 || sess.DoneCount > 0) &&
                     ((ShowCompact && !_pillSessionRect.IsEmpty && _pillSessionRect.Contains(loc)) ||
                      (!ShowCompact && !_sessRowRect.IsEmpty && _sessRowRect.Contains(loc))))
            {
                List<string> lines = new List<string>();
                if (sess.ConfirmCount > 0) lines.Add(L.T("sess.confirm", sess.ConfirmCount, JoinMax(sess.ConfirmLabels, 3)));
                if (sess.RunningCount > 0) lines.Add(L.T("sess.running", sess.RunningCount, JoinMax(sess.RunningLabels, 3)));
                if (sess.DoneCount > 0) lines.Add(L.T("sess.done", sess.DoneCount, JoinMax(sess.DoneLabels, 3)));
                if (sess.ConfirmCount > 0 || sess.DoneCount > 0) lines.Add(L.T("sess.clear.hint"));
                tip = string.Join("\n", lines.ToArray());
            }

            if (!string.Equals(tip, _lastTipText, StringComparison.Ordinal))
            {
                _lastTipText = tip;
                _pillTip.SetToolTip(this, tip);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverBar != -1) { _hoverBar = -1; Invalidate(); }
            if (_lastTipText != null) { _lastTipText = null; _pillTip.SetToolTip(this, null); }
        }

        private void RecordCardRect(CardCol col, Rectangle rect)
        {
            if (col == null || string.IsNullOrEmpty(col.Title)) return;
            _cardRects.Add(new KeyValuePair<string, Rectangle>(col.Title, rect));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && e.Clicks == 1)
            {
                if (!_popup && !_titleSettingsRect.IsEmpty && _titleSettingsRect.Contains(e.Location))
                {
                    if (SettingsRequested != null) SettingsRequested(this, EventArgs.Empty);
                    return;
                }
                if (!_popup && !_titleModeRect.IsEmpty && _titleModeRect.Contains(e.Location))
                {
                    if (PresetCycleRequested != null) PresetCycleRequested(this, EventArgs.Empty);
                    return;
                }
                if (!_popup && !_dockTop && !_titleCompactRect.IsEmpty && _titleCompactRect.Contains(e.Location))
                {
                    if (CompactToggled != null) CompactToggled(this, EventArgs.Empty);
                    return;
                }
                if (!_pillDoneRect.IsEmpty && _pillDoneRect.Contains(e.Location))
                {
                    if (DoneSessionsClicked != null) DoneSessionsClicked(this, EventArgs.Empty);
                    return;
                }
                if ((!_pillSessionRect.IsEmpty && _pillSessionRect.Contains(e.Location)) ||
                    (!_sessRowRect.IsEmpty && _sessRowRect.Contains(e.Location)))
                {
                    if (SessionMenuRequested != null) SessionMenuRequested(this, e);
                    return;
                }
                if (!_cardsSwitchRect.IsEmpty && _cardsSwitchRect.Contains(e.Location))
                {
                    CardPage++;
                    Relayout();
                    return;
                }
                if (!_ctxCopyRect.IsEmpty && _ctxCopyRect.Contains(e.Location))
                {
                    if (CopyCompactRequested != null) CopyCompactRequested(this, EventArgs.Empty);
                    return;
                }
                if (!_ctxTermRect.IsEmpty && _ctxTermRect.Contains(e.Location))
                {
                    if (OpenActiveTerminalRequested != null) OpenActiveTerminalRequested(this, EventArgs.Empty);
                    return;
                }
            }
            if (_popup) return;
            if (e.Button != MouseButtons.Left) return;
            if (e.Clicks >= 2)
            {
                for (int i = 0; i < _cardRects.Count; i++)
                {
                    if (_cardRects[i].Value.Contains(e.Location))
                    {
                        string title = _cardRects[i].Key;
                        if (CardDoubleClicked != null) CardDoubleClicked(this, title);
                        else if (title.IndexOf("Claude", StringComparison.OrdinalIgnoreCase) >= 0 && ClaudeCardDoubleClicked != null)
                            ClaudeCardDoubleClicked(this, EventArgs.Empty);
                        else if (title.IndexOf("Antigravity", StringComparison.OrdinalIgnoreCase) >= 0 && AntigravityCardDoubleClicked != null)
                            AntigravityCardDoubleClicked(this, EventArgs.Empty);
                        return;
                    }
                }
                if (!_dockTop && CompactToggled != null) CompactToggled(this, EventArgs.Empty);   // di tepi atas: ringkas/lengkap mengikuti hover
                return;
            }
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        }

        private const int WM_GETMINMAXINFO = 0x0024;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_GETMINMAXINFO)
            {
                base.WndProc(ref m);
                MINMAXINFO mmi = (MINMAXINFO)Marshal.PtrToStructure(m.LParam, typeof(MINMAXINFO));
                mmi.ptMinTrackSize.x = 10;
                mmi.ptMinTrackSize.y = 10;
                Marshal.StructureToPtr(mmi, m.LParam, false);
                return;
            }
            base.WndProc(ref m);
            if (m.Msg == WM_EXITSIZEMOVE)
            {
                // Di mode tepi atas, geser hanya mengubah posisi horizontal; widget kembali menempel ke tepi atas.
                if (_dockTop) { DockCenterX = Left + Width / 2; DockCenterY = Top + Height / 2; PositionDocked(); }
                else SnapToEdges();
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
            if (!_dockTop && hasPos) SnapToEdges();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_fBody != null) { _fTitle.Dispose(); _fBody.Dispose(); _fSmall.Dispose(); }
                if (_hoverTimer != null) { _hoverTimer.Dispose(); _hoverTimer = null; }
                if (_pulseTimer != null) { _pulseTimer.Dispose(); _pulseTimer = null; }
                _pillTip.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
