using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ClaudeUsageWidget
{
    /// <summary>Konteks aplikasi: ikon tray dinamis + floating window + timer/watcher.</summary>
    internal class TrayApp : ApplicationContext
    {
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr h);
        [DllImport("kernel32.dll")] private static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);

        private readonly AppConfig _cfg;
        private Pricing _pricing;
        private readonly UsageScanner _scanner;
        private readonly NotifyIcon _tray;
        private readonly FloatingForm _float;
        private readonly ContextMenuStrip _menu;
        private readonly System.Windows.Forms.Timer _tick, _debounce, _fsTimer;
        private FileSystemWatcher _logWatcher, _appWatcher, _geminiWatcher;
        private readonly SynchronizationContext _ui;

        private ToolStripMenuItem _miShow, _miCompact, _miClickThrough, _miAutostart, _miDock, _miRefresh, _miCopy, _miSettings, _miData, _miExit, _miCsv, _miReport;
        private ToolStripMenuItem _miOpenFolder, _miOpenTerminal;

        /// <summary>Terapkan bahasa terpilih ke menu yang sudah dibuat, tooltip, dan widget.</summary>
        private void ApplyLanguage()
        {
            _miShow.Text = L.T("tray.show");
            _miDock.Text = L.T("tray.dock");
            _miCompact.Text = L.T("tray.compact");
            _miClickThrough.Text = L.T("tray.clickthrough");
            _miRemaining.Text = L.T("tray.remaining");
            _miRefresh.Text = L.T("tray.refresh");
            if (_miCopy != null) _miCopy.Text = L.T("tray.copystatus");
            _miSettings.Text = L.T("tray.settings");
            _miData.Text = L.T("tray.datafolder");
            _miCsv.Text = L.T("tray.exportcsv");
            if (_miReport != null) _miReport.Text = L.T("tray.exportreport");
            _miAutostart.Text = L.T("tray.autostart");
            _miExit.Text = L.T("tray.exit");
            UpdateQuickJumpMenu();
            _probeStatus = null;    // pesan galat lama masih berbahasa sebelumnya; diganti saat refresh berikutnya
            Refresh();
        }
        private IntPtr _iconHandle = IntPtr.Zero;
        private Icon _icon;

        private UsageSnapshot _usage;
        private RateLimitSnapshot _limits = new RateLimitSnapshot();
        private OrgCostResult _org;
        private DateTime _orgNext = DateTime.MinValue;
        private bool _orgBusy;
        private int _scanBusy;          // 0/1
        private volatile bool _scanAgain;
        private bool _initialTrimDone;
        private volatile bool _pendingFull;
        // Probe CLI
        private DateTime _lastSeenRecord = DateTime.MinValue, _lastProbe = DateTime.MinValue;
        private bool _activityPending, _probeBusy;
        private string _probeStatus;
        private readonly HashSet<string> _dirty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public TrayApp()
        {
            Paths.Ensure();
            _cfg = AppConfig.Load();
            L.Set(_cfg.Language);   // sebelum menu dan layout pertama
            ApplyCurrencyOpts();
            _pricing = Pricing.Load();
            _scanner = new UsageScanner(_pricing);
            _scanner.ScanClaude = _cfg.ScanClaude;
            _scanner.ScanGemini = _cfg.ScanGemini;
            _scanner.Roots = ProjectRoots;
            _scanner.SetTrackProjects(_cfg.ShowProjects);
            _scanner.AntigravityQuota5hTokens = _cfg.AntigravityQuota5hTokens;
            _scanner.AntigravityQuota7dTokens = _cfg.AntigravityQuota7dTokens;
            _history = HistoryStore.Load();
            Opts.ShowRemaining = _cfg.ShowRemaining;

            Palette.Apply(ThemeUtil.ResolveLight(_cfg.Theme));
            _float = new FloatingForm();
            _float.Compact = _cfg.Compact;
            _float.ClickThrough = _cfg.ClickThrough;
            ApplyOpacity();
            _float.PositionCommitted += delegate { SavePosition(); };
            _float.CompactToggled += delegate { SetCompact(!_float.Compact); };
            _float.ClaudeCardDoubleClicked += delegate { OpenProjectTerminal(); };
            _float.AntigravityCardDoubleClicked += delegate { OpenAntigravityFolder(); };
            IntPtr force = _float.Handle;   // buat handle sekarang (untuk DPI & ukuran)
            _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

            _menu = BuildMenu();
            MenuTheme.Apply(_menu);
            _float.ContextMenuStrip = _menu;

            _tray = new NotifyIcon();
            _tray.ContextMenuStrip = _menu;
            _tray.Text = L.AppName;
            _tray.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) OnTrayClick(); };
            UpdateTrayIcon();
            _tray.Visible = true;

            _float.SetData(BuildVm());
            _float.PlaceAt(_cfg.FloatX, _cfg.FloatY, _cfg.HasPosition);
            _float.Edge = _cfg.DockEdge;
            _float.MonitorName = _cfg.DockMonitor;
            Point dockCenter = _float.DockDefaultCenter();
            _float.DockCenterX = _cfg.HasDockX ? _cfg.DockCenterX : dockCenter.X;
            _float.DockCenterY = _cfg.HasDockY ? _cfg.DockCenterY : dockCenter.Y;
            if (_cfg.DockTop) _float.DockTop = true;
            ApplyVisibility();
            ApplyTopMost(false);
            _miShow.Checked = _cfg.ShowFloating;

            _fsTimer = new System.Windows.Forms.Timer();
            _fsTimer.Interval = 1000;
            _fsTimer.Tick += delegate { CheckForeground(); CheckSystemTheme(); };
            _fsTimer.Start();

            _tick = new System.Windows.Forms.Timer();
            _tick.Interval = 60 * 1000;
            _tick.Tick += delegate { OnTick(); };
            _tick.Start();

            _debounce = new System.Windows.Forms.Timer();
            _debounce.Interval = 2000;
            _debounce.Tick += delegate { _debounce.Stop(); RequestScan(false); };

            _hotkey = new HotkeyWindow();
            _hotkey.Pressed += delegate { ToggleFloating(); };
            ApplyHotkey(false);

            StartWatchers();
            HandleNotify();   // peristiwa hook yang tertinggal (yang basi diabaikan)
            ReloadLimits();
            RequestScan();
            MaybeFetchOrg(false);
            MaybeFetchGoogleRate(false);
            MaybeFetchOfficialPricing(false);
            MaybeFetchAntigravityRate(false);
        }

        // ---------------- jendela detail di atas ikon tray ----------------
        [DllImport("shell32.dll")] private static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER id, out RECT rect);
        [StructLayout(LayoutKind.Sequential)] private struct NOTIFYICONIDENTIFIER { public uint cbSize; public IntPtr hWnd; public uint uID; public Guid guidItem; }
        private static readonly System.Reflection.FieldInfo NiWindow = typeof(NotifyIcon).GetField("window", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        private static readonly System.Reflection.FieldInfo NiId = typeof(NotifyIcon).GetField("id", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        private FloatingForm _popup;
        private Rectangle _popupAnchor;
        private DateTime _popupHiddenAt = DateTime.MinValue;

        /// <summary>Klik kiri ikon tray: buka/tutup jendela detail tepat di atas ikon. Widget yang ter-pin di tepi tidak disentuh.</summary>
        private void OnTrayClick()
        {
            if (_popup != null && _popup.Visible) { _popup.Hide(); _popupHiddenAt = DateTime.Now; return; }
            // Klik pada ikon saat popup terbuka lebih dulu membuat popup kehilangan fokus (tertutup); jangan buka lagi oleh klik yang sama.
            if ((DateTime.Now - _popupHiddenAt).TotalMilliseconds < 400) return;
            ShowPopup();
        }

        private void ShowPopup()
        {
            if (_popup == null)
            {
                _popup = new FloatingForm(true);
                _popup.Dismissed += delegate { _popupHiddenAt = DateTime.Now; };
                IntPtr force = _popup.Handle;   // buat handle agar ukuran/DPI benar
            }
            _popupAnchor = TrayIconRect();
            _popup.SetData(BuildVm());
            PositionPopup();
            _popup.Show();
            _popup.Activate();
        }

        /// <summary> Ganti popup (tema/bahasa berubah): buang agar dibuat ulang dengan palet dan teks baru.</summary>
        private void ResetPopup()
        {
            if (_popup == null) return;
            _popup.Hide(); _popup.Dispose(); _popup = null;
        }

        /// <summary>Posisi ikon tray yang diklik; bila tak tersedia, sekitar kursor saat klik.</summary>
        private Rectangle TrayIconRect()
        {
            Point c = Cursor.Position;
            Rectangle fallback = new Rectangle(c.X - 8, c.Y - 8, 16, 16);
            try
            {
                NativeWindow w = NiWindow == null ? null : NiWindow.GetValue(_tray) as NativeWindow;
                if (w == null || NiId == null) return fallback;
                NOTIFYICONIDENTIFIER nid = new NOTIFYICONIDENTIFIER();
                nid.cbSize = (uint)Marshal.SizeOf(typeof(NOTIFYICONIDENTIFIER));
                nid.hWnd = w.Handle;
                nid.uID = (uint)(int)NiId.GetValue(_tray);
                RECT r;
                if (Shell_NotifyIconGetRect(ref nid, out r) == 0)
                {
                    Rectangle rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                    bool near = Math.Abs(rect.Left + rect.Width / 2 - c.X) < 300 && Math.Abs(rect.Top + rect.Height / 2 - c.Y) < 200;
                    if (rect.Width > 0 && rect.Height > 0 && near) return rect;   // ikon di overflow bisa memberi posisi jauh dari klik
                }
            }
            catch (Exception ex) { Log.Write("trayrect", ex); }
            return fallback;
        }

        /// <summary>Taskbar di bawah: popup menempel tepat di atasnya, dipusatkan pada ikon. Taskbar di sisi lain: di sisi dalamnya.</summary>
        private void PositionPopup()
        {
            const int gap = 10;
            Rectangle a = _popupAnchor;
            Size sz = _popup.Size;
            Screen scr = Screen.FromRectangle(a);
            Rectangle b = scr.Bounds, wa = scr.WorkingArea;
            int x = a.Left + a.Width / 2 - sz.Width / 2, y;
            if (wa.Bottom < b.Bottom) y = wa.Bottom - sz.Height - gap;
            else if (wa.Top > b.Top) y = wa.Top + gap;
            else if (wa.Left > b.Left || wa.Right < b.Right)
            {
                x = wa.Left > b.Left ? wa.Left + gap : wa.Right - sz.Width - gap;
                y = a.Top + a.Height / 2 - sz.Height / 2;
            }
            else y = a.Top - sz.Height - gap;   // taskbar auto-hide: tepat di atas ikon
            x = Math.Max(wa.Left + gap, Math.Min(x, wa.Right - sz.Width - gap));
            y = Math.Max(wa.Top + gap, Math.Min(y, wa.Bottom - sz.Height - gap));
            _popup.Location = new Point(x, y);
        }

        // ---------------- fitur tambahan: folder pindai, tema sistem, hotkey, alert ----------------
        private readonly HistoryStore _history;
        private readonly AlertEngine _alerts = new AlertEngine();
        private HotkeyWindow _hotkey;
        private ToolStripMenuItem _miRemaining;
        private string _probeFailed;

        /// <summary>Folder 'projects' yang dipindai: default, folder tambahan, dan (opsional) distro WSL yang berjalan.</summary>
        private List<string> ProjectRoots()
        {
            List<string> r = new List<string>();
            if (_cfg.ScanClaude)
            {
                r.Add(Paths.ProjectsDir);
                string extra = _cfg.ExtraClaudeDirs;
                if (!string.IsNullOrWhiteSpace(extra))
                    foreach (string raw in extra.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string p = raw.Trim().Trim('"');
                        if (p.Length == 0) continue;
                        if (!p.TrimEnd('\\', '/').EndsWith("projects", StringComparison.OrdinalIgnoreCase)) p = Path.Combine(p, "projects");
                        if (!r.Contains(p, StringComparer.OrdinalIgnoreCase)) r.Add(p);
                    }
                if (_cfg.ScanWsl)
                    foreach (string w in WslRoots.Get()) if (!r.Contains(w, StringComparer.OrdinalIgnoreCase)) r.Add(w);
            }
            if (_cfg.ScanGemini && Directory.Exists(Paths.AntigravityBrainDir))
                if (!r.Contains(Paths.AntigravityBrainDir, StringComparer.OrdinalIgnoreCase)) r.Add(Paths.AntigravityBrainDir);
            return r;
        }

        private void CheckSystemTheme()
        {
            if (_cfg.Theme != "system") return;
            bool light = ThemeUtil.ResolveLight("system");
            if (light != Palette.IsLight) { Palette.Apply(light); _float.ApplyTheme(); MenuTheme.Apply(_menu); ResetPopup(); UpdateTrayIcon(); }
        }

        /// <summary>Daftarkan hotkey sesuai config. Bila gagal (dipakai aplikasi lain), beri tahu bila diminta.</summary>
        private void ApplyHotkey(bool reportFailure)
        {
            if (!_cfg.HotkeyEnabled) { _hotkey.Unregister(); return; }
            if (!_hotkey.Register(_cfg.Hotkey))
            {
                Log.Write("hotkey: registration failed");
                if (reportFailure) MessageBox.Show(L.T("msg.hotkey.fail", _cfg.Hotkey), L.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ShowBalloon(string text, ToolTipIcon icon)
        {
            try { _tray.ShowBalloonTip(8000, L.AppName, text, icon); }
            catch (Exception ex) { Log.Write("balloon", ex); }
        }

        // ---------------- anggaran biaya, peristiwa hook, ekspor CSV ----------------
        private readonly BudgetEngine _budget = new BudgetEngine();

        private void RunBudget()
        {
            if (_usage == null || !_usage.Ready) return;
            if (_cfg.BudgetDaily <= 0 && _cfg.BudgetMonthly <= 0) return;
            foreach (BudgetAlert a in _budget.Evaluate(_usage.TodayTotal.Cost, _history.MonthToDateCost(DateTime.Now),
                         _cfg.BudgetDaily, _cfg.BudgetMonthly, DateTime.Now))
            {
                string text = L.T(a.Monthly ? "alert.budget.month" : "alert.budget.day", Fmt.Usd(a.Cost), a.Percent, Fmt.Usd(a.Budget));
                Log.Write("alert budget " + (a.Monthly ? "month " : "day ") + a.Percent);
                ShowBalloon(text, ToolTipIcon.Warning);
                if (_cfg.AudioAlertsEnabled) AudioAlert.PlayWarning();
                if (_cfg.WebhookEnabled) WebhookSender.Send(_cfg.WebhookUrl, "Claude Budget Alert", text);
            }
        }

        private DateTime _lastNotifyAt = DateTime.MinValue;
        private string _lastNotifyKind;

        /// <summary>Peristiwa dari hook Claude Code (notify.json): respons panjang selesai, atau Claude butuh input.</summary>
        private void HandleNotify()
        {
            Dictionary<string, object> e = NotifyEvents.TryTake();
            if (e == null) return;
            string kind = J.Str(e, "kind");
            long elapsed = J.Long(e, "elapsed");
            double ts = J.Num(e, "ts");
            double nowUnix = (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
            if (!double.IsNaN(ts) && nowUnix - ts > 90) return;                               // peristiwa basi (widget baru jalan)
            if (kind == _lastNotifyKind && (DateTime.Now - _lastNotifyAt).TotalSeconds < 15) return;   // redam peristiwa beruntun
            string text;
            if (kind == "stop")
            {
                if (!_cfg.HookNotifyStop) return;
                if (elapsed < _cfg.HookNotifyMinSeconds) return;                                 // termasuk durasi tak diketahui (-1) bila ambang > 0
                text = elapsed >= 0 ? L.T("notify.stop", Fmt.Duration(elapsed)) : L.T("notify.stop.nodur");
            }
            else if (kind == "input")
            {
                if (!_cfg.HookNotifyInput) return;
                text = L.T("notify.input");
            }
            else return;
            _lastNotifyAt = DateTime.Now; _lastNotifyKind = kind;
            Log.Write("notify " + kind + (elapsed >= 0 ? " " + elapsed + "s" : ""));
            ShowBalloon(text, ToolTipIcon.Info);
            if (_cfg.AudioAlertsEnabled)
            {
                if (kind == "input") AudioAlert.PlayPrompt();
                else AudioAlert.PlayNotify();
            }
            if (_cfg.WebhookEnabled && kind == "input")
            {
                WebhookSender.Send(_cfg.WebhookUrl, "Claude Code Input Required", text);
            }
        }

        /// <summary>Ekspor riwayat harian (agregat) ke CSV. Pilihan 2 memakai titik koma dan koma desimal untuk Excel regional.</summary>
        private void ExportCsv()
        {
            if (_history.Count == 0) { MessageBox.Show(L.T("msg.csv.empty"), L.AppName); return; }
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Filter = L.T("msg.csv.filter");
                d.FilterIndex = L.IsId ? 2 : 1;
                d.FileName = "claude-usage-" + DateTime.Now.ToString("yyyyMMdd") + ".csv";
                d.DefaultExt = "csv";
                d.OverwritePrompt = true;
                if (d.ShowDialog() != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(d.FileName, _history.ToCsv(d.FilterIndex == 2 ? ';' : ','), new System.Text.UTF8Encoding(true));
                    MessageBox.Show(L.T("msg.csv.done", d.FileName), L.AppName);
                }
                catch (Exception ex) { Log.Write("csv", ex); MessageBox.Show(ex.Message, L.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }

        private void ExportMonthlyReport()
        {
            try
            {
                using (SaveFileDialog d = new SaveFileDialog())
                {
                    d.Filter = L.T("msg.report.filter");
                    d.FileName = string.Format("AI_Usage_Report_{0:yyyy_MM}.md", DateTime.Now);
                    d.DefaultExt = "md";
                    d.OverwritePrompt = true;
                    if (d.ShowDialog() != DialogResult.OK) return;

                    string md = ReportExporter.GenerateMonthlyMarkdown(_history, _usage, DateTime.Now);
                    File.WriteAllText(d.FileName, md, new System.Text.UTF8Encoding(true));
                    MessageBox.Show(L.T("msg.report.done", d.FileName), L.AppName);
                }
            }
            catch (Exception ex)
            {
                Log.Write("report.export", ex);
                MessageBox.Show(ex.Message, L.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void RunAlerts()
        {
            if (!_cfg.AlertsEnabled) return;
            int[] th = AlertEngine.ParseThresholds(_cfg.AlertThresholds) ?? new[] { 80, 95 };
            foreach (Alert a in _alerts.Evaluate(_limits, th, _cfg.ResetNotify))
            {
                string name;
                if (a.Window == "five_hour") name = L.T("limit5");
                else if (a.Window == "seven_day") name = L.T("limit7");
                else if (a.Window == "antigravity_five_hour") name = L.T("limit5.ag");
                else if (a.Window == "antigravity_seven_day") name = L.T("limit7.ag");
                else name = a.Window;
                string text;
                if (a.IsReset) text = L.T("alert.reset", name);
                else
                {
                    int used = (int)Math.Round(a.Used);
                    text = Opts.ShowRemaining ? L.T("alert.pct.left", name, 100 - used) : L.T("alert.pct", name, used);
                    if (a.ResetsAt != DateTime.MinValue && a.ResetsAt > DateTime.Now) text += " · " + L.T("resetin.at", Fmt.Until(a.ResetsAt), Fmt.Clock(a.ResetsAt));
                }
                Log.Write("alert " + a.Window + (a.IsReset ? " reset" : " " + a.Threshold));
                try { _tray.ShowBalloonTip(8000, L.AppName, text, a.IsReset ? ToolTipIcon.Info : ToolTipIcon.Warning); }
                catch (Exception ex) { Log.Write("alert.show", ex); }
                if (_cfg.AudioAlertsEnabled)
                {
                    if (a.IsReset) AudioAlert.PlayNotify();
                    else AudioAlert.PlayWarning();
                }
                if (_cfg.WebhookEnabled) WebhookSender.Send(_cfg.WebhookUrl, "AI Usage Watcher Alert", text);
            }
        }

        private DateTime _lastBurnRateAlert = DateTime.MinValue;
        private DateTime _lastContextAlert = DateTime.MinValue;

        private void CheckBurnRate()
        {
            if (!_cfg.BurnRateAlertEnabled || _usage == null || !_usage.Ready) return;
            if (_usage.TokensLast10m >= _cfg.BurnRateThresholdTokens && (DateTime.Now - _lastBurnRateAlert).TotalMinutes >= 15)
            {
                _lastBurnRateAlert = DateTime.Now;
                string text = L.T("alert.burnrate", Fmt.Tokens(_usage.TokensLast10m));
                ShowBalloon(text, ToolTipIcon.Warning);
                if (_cfg.AudioAlertsEnabled) AudioAlert.PlayWarning();
                if (_cfg.WebhookEnabled) WebhookSender.Send(_cfg.WebhookUrl, "Token Burn Rate Alert", text);
                Log.Write("alert burnrate " + _usage.TokensLast10m);
            }
        }

        private void CheckContextAlert()
        {
            if (!_cfg.ContextAlertEnabled || _usage == null || !_usage.Ready) return;
            if (_usage.LastContextTokens >= 150000 && (DateTime.Now - _lastContextAlert).TotalMinutes >= 20)
            {
                _lastContextAlert = DateTime.Now;
                int pct = (int)Math.Round((double)_usage.LastContextTokens / 200000.0 * 100.0);
                string text = L.T("alert.context", Fmt.Tokens(_usage.LastContextTokens), pct);
                ShowBalloon(text, ToolTipIcon.Warning);
                if (_cfg.AudioAlertsEnabled) AudioAlert.PlayWarning();
                if (_cfg.WebhookEnabled) WebhookSender.Send(_cfg.WebhookUrl, "Session Context Alert", text);
                Log.Write("alert context " + _usage.LastContextTokens);
            }
        }

        private void ApplyCurrencyOpts()
        {
            Opts.CurrencyCustomEnabled = _cfg.CurrencyCustomEnabled;
            Opts.CurrencySymbol = _cfg.CurrencySymbol;
            Opts.CurrencyRate = _cfg.CurrencyRate;
            Opts.CurrencyShowBoth = _cfg.CurrencyShowBoth;
        }

        private void UpdateQuickJumpMenu()
        {
            if (_miOpenFolder == null || _miOpenTerminal == null) return;
            string cwd = _usage != null ? _usage.LastActiveCwd : null;
            string proj = _usage != null ? _usage.LastActiveProject : null;
            bool hasCwd = !string.IsNullOrEmpty(cwd) && Directory.Exists(cwd);
            _miOpenFolder.Visible = hasCwd;
            _miOpenTerminal.Visible = hasCwd;
            if (hasCwd)
            {
                _miOpenFolder.Text = L.T("tray.openfolder", proj ?? "Project");
                _miOpenTerminal.Text = L.T("tray.openterminal", proj ?? "Project");
            }
        }

        private void OpenProjectFolder()
        {
            try
            {
                if (_usage != null && !string.IsNullOrEmpty(_usage.LastActiveCwd) && Directory.Exists(_usage.LastActiveCwd))
                    Process.Start("explorer.exe", "\"" + _usage.LastActiveCwd + "\"");
            }
            catch (Exception ex) { Log.Write("open.folder", ex); }
        }

        private void OpenProjectTerminal()
        {
            try
            {
                if (_usage != null && !string.IsNullOrEmpty(_usage.LastActiveCwd) && Directory.Exists(_usage.LastActiveCwd))
                {
                    ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/K cd /d \"" + _usage.LastActiveCwd + "\"");
                    psi.WorkingDirectory = _usage.LastActiveCwd;
                    psi.UseShellExecute = true;
                    Process.Start(psi);
                }
            }
            catch (Exception ex) { Log.Write("open.terminal", ex); }
        }

        private void OpenAntigravityFolder()
        {
            try
            {
                string dir = Paths.AntigravityDir;
                if (Directory.Exists(dir))
                    Process.Start("explorer.exe", "\"" + dir + "\"");
            }
            catch (Exception ex) { Log.Write("open.antigravity", ex); }
        }

        private void CopyStatus()
        {
            try
            {
                string text = StatusFormatter.Format(_limits, _usage, _cfg.ScanClaude, _cfg.ScanGemini);
                Clipboard.SetText(text);
                ShowBalloon(L.T("msg.copied"), ToolTipIcon.Info);
            }
            catch (Exception ex) { Log.Write("copystatus", ex); }
        }

        // ---------------- menu ----------------
        private ContextMenuStrip BuildMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            _miShow = new ToolStripMenuItem(L.T("tray.show"), null, delegate { ToggleFloating(); });
            _miCompact = new ToolStripMenuItem(L.T("tray.compact"), null, delegate { SetCompact(!_float.Compact); });
            _miCompact.Checked = _cfg.Compact;
            _miClickThrough = new ToolStripMenuItem(L.T("tray.clickthrough"), null, delegate { SetClickThrough(!_cfg.ClickThrough); });
            _miClickThrough.Checked = _cfg.ClickThrough;
            _miRemaining = new ToolStripMenuItem(L.T("tray.remaining"), null, delegate { SetRemaining(!_cfg.ShowRemaining); });
            _miRemaining.Checked = _cfg.ShowRemaining;
            _miAutostart = new ToolStripMenuItem(L.T("tray.autostart"), null, delegate { ToggleAutostart(); });
            try { _miAutostart.Checked = Autostart.IsEnabled; } catch { }

            _miDock = new ToolStripMenuItem(L.T("tray.dock"), null, delegate { SetDockTop(!_float.DockTop); });
            _miDock.Checked = _cfg.DockTop;
            _miCompact.Enabled = !_cfg.DockTop;
            _miClickThrough.Enabled = !_cfg.DockTop;

            m.Items.Add(_miShow);
            m.Items.Add(_miDock);
            m.Items.Add(_miCompact);
            m.Items.Add(_miRemaining);
            m.Items.Add(_miClickThrough);
            m.Items.Add(new ToolStripSeparator());
            _miRefresh = new ToolStripMenuItem(L.T("tray.refresh"), null, delegate { ReloadLimits(); RequestScan(); MaybeFetchOrg(true); MaybeProbe(true); MaybeFetchGoogleRate(true); MaybeFetchOfficialPricing(true); MaybeFetchAntigravityRate(true); });
            _miCopy = new ToolStripMenuItem(L.T("tray.copystatus"), null, delegate { CopyStatus(); });
            _miSettings = new ToolStripMenuItem(L.T("tray.settings"), null, delegate { OpenSettings(); });
            _miData = new ToolStripMenuItem(L.T("tray.datafolder"), null, delegate { Process.Start("explorer.exe", "\"" + Paths.AppDir + "\""); });
            _miCsv = new ToolStripMenuItem(L.T("tray.exportcsv"), null, delegate { ExportCsv(); });
            _miReport = new ToolStripMenuItem(L.T("tray.exportreport"), null, delegate { ExportMonthlyReport(); });
            _miExit = new ToolStripMenuItem(L.T("tray.exit"), null, delegate { ExitThread(); });
            m.Items.Add(_miRefresh);
            m.Items.Add(_miCopy);
            m.Items.Add(_miSettings);
            m.Items.Add(_miData);
            m.Items.Add(_miCsv);
            m.Items.Add(_miReport);
            m.Items.Add(_miAutostart);

            _miOpenFolder = new ToolStripMenuItem("", null, delegate { OpenProjectFolder(); });
            _miOpenTerminal = new ToolStripMenuItem("", null, delegate { OpenProjectTerminal(); });
            _miOpenFolder.Visible = false;
            _miOpenTerminal.Visible = false;
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(_miOpenFolder);
            m.Items.Add(_miOpenTerminal);

            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(_miExit);
            m.Opening += delegate { _miShow.Checked = _cfg.ShowFloating; UpdateQuickJumpMenu(); };
            return m;
        }

        /// <summary>Pilihan user (ShowFloating) terpisah dari sembunyi otomatis saat layar penuh.
        /// Bila user memilih tampil saat aplikasi layar penuh aktif, jendela itu dikecualikan sampai fokus berpindah.</summary>
        private void ToggleFloating()
        {
            _cfg.ShowFloating = !_cfg.ShowFloating;
            if (_cfg.ShowFloating && _hiddenByFs) { _fsIgnoreHwnd = _lastFg; _hiddenByFs = false; }
            _miShow.Checked = _cfg.ShowFloating;
            _cfg.Save();
            ApplyVisibility();
        }

        private void ApplyVisibility()
        {
            bool show = _cfg.ShowFloating && !_hiddenByFs;
            if (show == _float.Visible) return;
            if (show) { _float.Show(); ApplyTopMost(true); } else _float.Hide();
        }

        private void ApplyTopMost(bool reassert)
        {
            bool want = _cfg.AlwaysOnTop || _cfg.DockTop;   // mode tepi atas butuh topmost agar hover bisa dicapai
            if (_float.TopMost != want) _float.TopMost = want;
            if (want && reassert && _float.Visible)
                SetWindowPos(_float.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        }

        // ---------------- layar penuh & z-order (poll 1 detik) ----------------
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int max);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr h);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int idx);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);

        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_NOOWNERZORDER = 0x200;
        private const int GWL_STYLE = -16, WS_CAPTION = 0x00C00000;

        private IntPtr _lastFg = IntPtr.Zero, _fsIgnoreHwnd = IntPtr.Zero;
        private bool _hiddenByFs;

        private void CheckForeground()
        {
            IntPtr fg = GetForegroundWindow();
            bool fgChanged = fg != _lastFg;
            _lastFg = fg;
            if (fgChanged && fg != _fsIgnoreHwnd) _fsIgnoreHwnd = IntPtr.Zero;

            bool fs = false;
            try { fs = _cfg.HideOnFullscreen && fg != _fsIgnoreHwnd && IsFullscreenOnWidgetMonitor(fg); }
            catch (Exception ex) { Log.Write("fullscreen", ex); }
            if (fs != _hiddenByFs) { _hiddenByFs = fs; ApplyVisibility(); }

            // Tegaskan ulang topmost hanya saat jendela aktif berganti (bukan timer tetap, agar tidak berebut
            // dengan jendela topmost lain), dan tidak saat menu terbuka atau widget sedang diseret.
            if (fgChanged && !_menu.Visible && Control.MouseButtons == MouseButtons.None) ApplyTopMost(true);
        }

        private bool IsFullscreenOnWidgetMonitor(IntPtr fg)
        {
            if (fg == IntPtr.Zero || !IsWindowVisible(fg)) return false;
            uint pid;
            GetWindowThreadProcessId(fg, out pid);
            if (pid == (uint)Process.GetCurrentProcess().Id) return false;          // jendela widget sendiri

            System.Text.StringBuilder cls = new System.Text.StringBuilder(256);
            GetClassName(fg, cls, cls.Capacity);
            string c = cls.ToString();
            if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd") return false;   // desktop/taskbar

            const uint MONITOR_DEFAULTTONEAREST = 2;
            IntPtr mon = MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST);
            if (mon != MonitorFromWindow(_float.Handle, MONITOR_DEFAULTTONEAREST)) return false;   // layar penuh di monitor lain

            int state;
            if (SHQueryUserNotificationState(out state) == 0 && (state == 3 || state == 4)) return true;   // D3D layar penuh / presentasi

            MONITORINFO mi = new MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (!GetMonitorInfo(mon, ref mi)) return false;
            RECT r;
            if (!GetWindowRect(fg, out r)) return false;
            bool covers = r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top && r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
            if (!covers) return false;
            if (IsZoomed(fg)) return false;                                          // jendela maximize biasa (mis. taskbar auto-hide)
            if ((GetWindowLong(fg, GWL_STYLE) & WS_CAPTION) == WS_CAPTION) return false;   // masih punya title bar
            return true;
        }

        private void SetCompact(bool on)
        {
            _float.Compact = on;
            _miCompact.Checked = on;
            _cfg.Compact = on;
            _cfg.Save();
        }

        /// <summary>Jendela layered tanpa SetLayeredWindowAttributes tidak digambar; WinForms melewatkannya pada
        /// Opacity 100%. Saat klik-tembus (yang butuh WS_EX_LAYERED) aktif, batasi ke 99%.</summary>
        private void ApplyOpacity()
        {
            double o = _cfg.OpacityPercent / 100.0;
            if (_cfg.ClickThrough && o > 0.99) o = 0.99;
            _float.Opacity = o;
        }

        private void SetClickThrough(bool on)
        {
            _cfg.ClickThrough = on;
            ApplyOpacity();
            _float.ClickThrough = on;
            _miClickThrough.Checked = on;
            _cfg.ClickThrough = on;
            _cfg.Save();
        }

        private void ToggleAutostart()
        {
            try { Autostart.Set(!Autostart.IsEnabled); _miAutostart.Checked = Autostart.IsEnabled; }
            catch (Exception ex) { Log.Write("autostart", ex); MessageBox.Show(L.T("msg.autostartfail"), L.AppName); }
        }

        private void SavePosition()
        {
            if (_float.DockTop)
            {
                _cfg.DockCenterX = _float.DockCenterX; _cfg.HasDockX = true;
                _cfg.DockCenterY = _float.DockCenterY; _cfg.HasDockY = true;
            }
            else { _cfg.FloatX = _float.Left; _cfg.FloatY = _float.Top; _cfg.HasPosition = true; }
            _cfg.Save();
        }

        private void SetDockTop(bool on)
        {
            if (on == _float.DockTop) { _miDock.Checked = on; return; }
            if (!on)
            {
                _float.DockTop = false;
                _float.PlaceAt(_cfg.FloatX, _cfg.FloatY, _cfg.HasPosition);   // kembali ke posisi mengambang terakhir
            }
            else
            {
                if (_float.Visible && !_float.DockTop) SavePosition();          // simpan posisi mengambang dulu
                _float.DockTop = true;
                if (!_cfg.ShowFloating) ToggleFloating();
            }
            _miDock.Checked = on;
            _miClickThrough.Enabled = !on;
            _miCompact.Enabled = !on;
            _cfg.DockTop = on;
            _cfg.Save();
            ApplyTopMost(true);
        }

        private void OpenSettings()
        {
            string oldEdge = _cfg.DockEdge, oldMonitor = _cfg.DockMonitor;
            SettingsCallbacks cb = new SettingsCallbacks();
            cb.ExportCsv = ExportCsv;
            cb.Diagnostics = delegate { return Diag.Build(_cfg, _limits, _usage, _probeFailed, _org, _history.Count); };
            using (SettingsForm f = new SettingsForm(_cfg, cb))
            {
                if (f.ShowDialog() != DialogResult.OK) return;
            }
            _cfg.Save();
            _scanner.ScanClaude = _cfg.ScanClaude;
            _scanner.ScanGemini = _cfg.ScanGemini;
            _scanner.AntigravityQuota5hTokens = _cfg.AntigravityQuota5hTokens;
            _scanner.AntigravityQuota7dTokens = _cfg.AntigravityQuota7dTokens;
            StartWatchers();
            L.Set(_cfg.Language);
            ApplyCurrencyOpts();
            ApplyLanguage();
            ResetPopup();
            _scanner.SetTrackProjects(_cfg.ShowProjects);   // aktif/nonaktif memicu pindai ulang
            _float.Edge = _cfg.DockEdge;
            _float.MonitorName = _cfg.DockMonitor;
            if (oldEdge != _cfg.DockEdge || oldMonitor != _cfg.DockMonitor)
            {
                Point c = _float.DockDefaultCenter();   // tepi/monitor baru: mulai dari tengah tepi itu
                _float.DockCenterX = c.X; _float.DockCenterY = c.Y;
                _cfg.DockCenterX = c.X; _cfg.DockCenterY = c.Y; _cfg.HasDockX = true; _cfg.HasDockY = true;
                _cfg.Save();
            }
            _float.ApplyDock();
            bool wantDock = _cfg.DockTop;
            _cfg.DockTop = _float.DockTop;
            SetDockTop(wantDock);
            Palette.Apply(ThemeUtil.ResolveLight(_cfg.Theme));
            _float.ApplyTheme();
            MenuTheme.Apply(_menu);
            ResetPopup();
            ApplyTopMost(true);
            _hiddenByFs = _hiddenByFs && _cfg.HideOnFullscreen;
            ApplyVisibility();
            SetCompact(_cfg.Compact);
            SetClickThrough(_cfg.ClickThrough);
            SetRemaining(_cfg.ShowRemaining);
            ApplyHotkey(true);
            try { _miAutostart.Checked = Autostart.IsEnabled; } catch { }
            if (!_cfg.AdminEnabled) _org = null;
            MaybeFetchOrg(true);
            RequestScan();   // folder pindai (WSL / tambahan) mungkin berubah
            Refresh();
        }

        private void SetRemaining(bool on)
        {
            _cfg.ShowRemaining = on;
            Opts.ShowRemaining = on;
            _miRemaining.Checked = on;
            _cfg.Save();
            Refresh();
        }

        // ---------------- data ----------------
        private void StartWatchers()
        {
            if (_logWatcher != null) { try { _logWatcher.Dispose(); } catch { } _logWatcher = null; }
            if (_appWatcher != null) { try { _appWatcher.Dispose(); } catch { } _appWatcher = null; }
            if (_geminiWatcher != null) { try { _geminiWatcher.Dispose(); } catch { } _geminiWatcher = null; }

            if (_cfg.ScanClaude)
            {
                try
                {
                    if (Directory.Exists(Paths.ProjectsDir))
                    {
                        _logWatcher = new FileSystemWatcher(Paths.ProjectsDir, "*.jsonl");
                        _logWatcher.IncludeSubdirectories = true;
                        _logWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName;
                        _logWatcher.InternalBufferSize = 64 * 1024;
                        FileSystemEventHandler h = delegate(object s, FileSystemEventArgs e)
                        {
                            lock (_dirty) _dirty.Add(e.FullPath);
                            _ui.Post(delegate { if (!_debounce.Enabled) _debounce.Start(); }, null);   // paling lambat 2 dtk sekali
                        };
                        _logWatcher.Changed += h; _logWatcher.Created += h;
                        _logWatcher.EnableRaisingEvents = true;
                    }
                }
                catch (Exception ex) { Log.Write("watch.logs", ex); }
            }
            try
            {
                _appWatcher = new FileSystemWatcher(Paths.AppDir, "*.json");
                _appWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName;
                FileSystemEventHandler h2 = delegate(object s, FileSystemEventArgs e)
                {
                    if (string.Equals(e.Name, "ratelimits.json", StringComparison.OrdinalIgnoreCase))
                        _ui.Post(delegate { ReloadLimits(); }, null);
                    else if (string.Equals(e.Name, "notify.json", StringComparison.OrdinalIgnoreCase))
                        _ui.Post(delegate { HandleNotify(); }, null);
                };
                _appWatcher.Changed += h2; _appWatcher.Created += h2;
                _appWatcher.Renamed += delegate(object s, RenamedEventArgs e) { h2(s, e); };
                _appWatcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) { Log.Write("watch.app", ex); }
            if (_cfg.ScanGemini)
            {
                try
                {
                    if (Directory.Exists(Paths.AntigravityBrainDir))
                    {
                        _geminiWatcher = new FileSystemWatcher(Paths.AntigravityBrainDir, "transcript.jsonl");
                        _geminiWatcher.IncludeSubdirectories = true;
                        _geminiWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName;
                        _geminiWatcher.InternalBufferSize = 64 * 1024;
                        FileSystemEventHandler hG = delegate(object s, FileSystemEventArgs e)
                        {
                            lock (_dirty) _dirty.Add(e.FullPath);
                            _ui.Post(delegate { if (!_debounce.Enabled) _debounce.Start(); }, null);
                        };
                        _geminiWatcher.Changed += hG; _geminiWatcher.Created += hG;
                        _geminiWatcher.EnableRaisingEvents = true;
                    }
                }
                catch (Exception ex) { Log.Write("watch.gemini", ex); }
            }
        }

        private void OnTick()
        {
            try
            {
                if (File.Exists(Paths.PricingFile) && File.GetLastWriteTimeUtc(Paths.PricingFile) != _pricing.LoadedFileTime)
                {
                    _pricing = Pricing.Load();
                    _scanner.ResetWithPricing(_pricing);
                }
            }
            catch (Exception ex) { Log.Write("pricing.watch", ex); }
            ReloadLimits();
            RequestScan();
            MaybeFetchOrg(false);
            MaybeProbe(false);
            MaybeFetchGoogleRate(false);
            MaybeFetchAntigravityRate(false);
        }

        /// <summary>
        /// Jalankan probe CLI bila: diaktifkan, ada chat baru sejak probe terakhir, snapshot limit sudah lebih tua
        /// dari batas menit, dan probe terakhir juga lebih lama dari batas itu. force = dari menu Refresh (min. jeda 1 menit).
        /// </summary>
        private void MaybeProbe(bool force)
        {
            if (!_cfg.ScanClaude || !_cfg.ProbeEnabled || _probeBusy) return;
            if (_usage != null && _usage.LastRecord != DateTime.MinValue)
            {
                if (_lastSeenRecord == DateTime.MinValue) _lastSeenRecord = _usage.LastRecord;   // patokan saat app mulai
                else if (_usage.LastRecord > _lastSeenRecord) { _lastSeenRecord = _usage.LastRecord; _activityPending = true; }
            }
            DateTime now = DateTime.Now;
            if (force)
            {
                if ((now - _lastProbe).TotalMinutes < 1) return;
            }
            else
            {
                TimeSpan min = TimeSpan.FromMinutes(_cfg.ProbeMinMinutes);
                if (!_activityPending) return;
                if (now - _limits.CapturedAt < min) return;   // snapshot masih baru (statusline/probe)
                if (now - _lastProbe < min) return;
            }
            _activityPending = false;
            _lastProbe = now;
            _probeBusy = true;
            _probeStatus = L.T("probe.running");
            Refresh();
            string model = _cfg.ProbeModel;
            string mirror = _cfg.MirrorEnabled ? _cfg.MirrorFilePath : null;
            Task.Run(delegate
            {
                string err;
                try
                {
                    string rl;
                    err = CliProbe.Run(model, out rl);
                    if (err == null && !string.IsNullOrEmpty(mirror))
                    {
                        string merr = null;
                        try { merr = Mirror.Write(mirror, rl); } catch (Exception mex) { merr = mex.GetType().Name; }
                        if (merr != null) Log.Write("mirror: " + merr);
                    }
                }
                catch (Exception ex) { err = ex.GetType().Name; }
                _ui.Post(delegate
                {
                    _probeBusy = false;
                    _probeStatus = err == null ? null : L.T("probe.failed", err);
                    _probeFailed = err;
                    if (err != null) Log.Write("probe: " + err);
                    ReloadLimits();
                }, null);
            });
        }

        private void ReloadLimits()
        {
            _limits = RateLimitReader.Read();
            if (!_limits.AntigravityFiveHour.Available && _usage != null && _usage.AntigravityFiveHour != null && _usage.AntigravityFiveHour.Available)
            {
                _limits.AntigravityFiveHour = _usage.AntigravityFiveHour;
                _limits.AntigravitySevenDay = _usage.AntigravitySevenDay;
            }
            Refresh();
            RunAlerts();
        }

        private void MaybeFetchAntigravityRate(bool force)
        {
            if (!_cfg.ScanGemini) return;
            if (!force && !AntigravityRateFetcher.ShouldRefresh(false)) return;
            Task.Run(async delegate
            {
                bool ok = await AntigravityRateFetcher.FetchAndSaveAsync().ConfigureAwait(false);
                if (ok)
                {
                    _ui.Post(delegate { ReloadLimits(); }, null);
                }
            });
        }

        /// <summary>full = enumerasi semua file (timer 1 menit); selain itu hanya file yang dilaporkan watcher.</summary>
        private void RequestScan(bool full = true)
        {
            if (full) _pendingFull = true;
            if (Interlocked.CompareExchange(ref _scanBusy, 1, 0) != 0) { _scanAgain = true; return; }
            Task.Run(delegate
            {
                do
                {
                    _scanAgain = false;
                    bool doFull = _pendingFull;
                    _pendingFull = false;
                    List<string> dirty;
                    lock (_dirty) { dirty = new List<string>(_dirty); _dirty.Clear(); }
                    try
                    {
                        if (doFull) _scanner.Scan();
                        else if (dirty.Count > 0) _scanner.ScanFiles(dirty);
                    }
                    catch (Exception ex) { Log.Write("scan", ex); }
                    UsageSnapshot snap = _scanner.Snapshot();
                    if (snap.Ready) _history.Merge(snap.Days);   // hanya setelah pindai lengkap (bukan saat hitung ulang)
                    _ui.Post(delegate
                    {
                        _usage = snap;
                        if (!_limits.AntigravityFiveHour.Available && snap.AntigravityFiveHour != null && snap.AntigravityFiveHour.Available)
                        {
                            _limits.AntigravityFiveHour = snap.AntigravityFiveHour;
                            _limits.AntigravitySevenDay = snap.AntigravitySevenDay;
                        }
                        Refresh();
                        MaybeProbe(false);
                        RunBudget();
                        CheckBurnRate();
                        CheckContextAlert();
                        RunAlerts();
                    }, null);
                } while (_scanAgain);
                Interlocked.Exchange(ref _scanBusy, 0);
                // Sekali saja setelah scan awal (yang membaca ratusan MB): bersihkan buffer besar & kembalikan memori ke OS.
                if (!_initialTrimDone)
                {
                    _initialTrimDone = true;
                    GC.Collect(2, GCCollectionMode.Forced, true, true);
                    GC.WaitForPendingFinalizers();
                    try { SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, (IntPtr)(-1), (IntPtr)(-1)); } catch { }
                }
            });
        }

        private async void MaybeFetchOrg(bool force)
        {
            if (!_cfg.AdminEnabled || _orgBusy) return;
            if (!force && DateTime.Now < _orgNext) return;
            if (force && _org != null && (DateTime.Now - _org.FetchedAt).TotalMinutes < 5) return;   // jangan membanjiri API
            _orgBusy = true;
            try
            {
                string key = _cfg.GetAdminKey();
                _org = await AdminApi.FetchMonthCost(key, _cfg.AdminApiKeyIds, _cfg.AdminWorkspaceIds);
                if (!_org.Ok) Log.Write("adminapi: " + _org.Error);
            }
            catch (Exception ex) { Log.Write("adminapi", ex); }
            finally { _orgBusy = false; _orgNext = DateTime.Now.AddHours(1); }
            Refresh();
        }

        private async void MaybeFetchGoogleRate(bool force)
        {
            if (!_cfg.CurrencyCustomEnabled || !GoogleRateFetcher.ShouldRefresh(force)) return;
            double rate = await GoogleRateFetcher.FetchRateAsync();
            if (rate > 5000)
            {
                _cfg.CurrencyRate = Math.Round(rate, 2);
                Opts.CurrencyRate = _cfg.CurrencyRate;
                _cfg.Save();
                Refresh();
            }
        }

        private async void MaybeFetchOfficialPricing(bool force)
        {
            if (force || OfficialPricingFetcher.ShouldAutoRefresh())
            {
                await OfficialPricingFetcher.FetchAndSaveAsync();
            }
        }

        private ViewModel BuildVm()
        {
            ViewModel vm = new ViewModel();
            vm.Usage = _usage;
            vm.Limits = _limits;
            vm.Org = _org;
            vm.AdminEnabled = _cfg.AdminEnabled;
            vm.ScanClaude = _cfg.ScanClaude;
            vm.ScanGemini = _cfg.ScanGemini;
            vm.ProbeStatus = _probeStatus;
            vm.History = _history.Series(_cfg.HistoryDays);
            vm.BudgetDaily = _cfg.BudgetDaily; vm.BudgetMonthly = _cfg.BudgetMonthly;
            vm.MonthCost = _history.MonthToDateCost(DateTime.Now);
            vm.ShowProjects = _cfg.ShowProjects;
            return vm;
        }

        private void Refresh()
        {
            _float.SetData(BuildVm());
            if (_popup != null && _popup.Visible) { _popup.SetData(BuildVm()); PositionPopup(); }
            UpdateTrayIcon();
        }

        // ---------------- ikon tray dinamis ----------------
        private void UpdateTrayIcon()
        {
            // Satu angka besar: limit 5 jam, atau limit mingguan (cincin putih) bila mingguan sudah penuh.
            string txt; Color badge; bool weekly;
            TrayText.IconSpec(_limits, _cfg.ScanClaude, _cfg.ScanGemini, out txt, out badge, out weekly);

            Size sz = SystemInformation.SmallIconSize;
            using (Bitmap bmp = new Bitmap(sz.Width, sz.Height))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                    TrayIconPainter.Draw(g, Math.Min(sz.Width, sz.Height), txt, badge, weekly);
                IntPtr h = bmp.GetHicon();
                Icon newIcon = Icon.FromHandle(h);
                _tray.Icon = newIcon;
                // Bebaskan handle lama agar tidak bocor GDI (refresh tiap menit).
                if (_icon != null) _icon.Dispose();
                if (_iconHandle != IntPtr.Zero) DestroyIcon(_iconHandle);
                _icon = newIcon; _iconHandle = h;
            }
            SetTrayTip(Tooltip());
        }

        // NotifyIcon.Text dibatasi 63 karakter oleh .NET Framework, padahal API Windows mendukung 127. Batas diperluas dengan
        // mengisi field privat lalu memperbarui ikon (cara yang umum dipakai di .NET Framework 4.x). Bila gagal: kembali ke 63.
        private const System.Reflection.BindingFlags Priv = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        private static readonly System.Reflection.FieldInfo NiText = typeof(NotifyIcon).GetField("text", Priv);
        private static readonly System.Reflection.FieldInfo NiAdded = typeof(NotifyIcon).GetField("added", Priv);
        private static readonly System.Reflection.MethodInfo NiUpdate = typeof(NotifyIcon).GetMethod("UpdateIcon", Priv);
        private static readonly bool LongTipOk = NiText != null && NiAdded != null && NiUpdate != null;

        private string Tooltip() { return TrayText.Tooltip(_limits, _usage, _probeFailed, LongTipOk ? 127 : 63, _cfg.ScanClaude, _cfg.ScanGemini); }

        private void SetTrayTip(string text)
        {
            if (text.Length <= 63) { _tray.Text = text; return; }
            if (LongTipOk)
            {
                try
                {
                    NiText.SetValue(_tray, text);
                    if ((bool)NiAdded.GetValue(_tray)) NiUpdate.Invoke(_tray, new object[] { true });
                    return;
                }
                catch (Exception ex) { Log.Write("tray.tip", ex); }
            }
            _tray.Text = text.Substring(0, 62) + "…";
        }

        protected override void ExitThreadCore()
        {
            try
            {
                if (_float.Visible) SavePosition();
                _tick.Stop(); _debounce.Stop(); _fsTimer.Stop();
                if (_hotkey != null) _hotkey.Dispose();
                if (_logWatcher != null) _logWatcher.Dispose();
                if (_appWatcher != null) _appWatcher.Dispose();
                if (_geminiWatcher != null) _geminiWatcher.Dispose();
                _tray.Visible = false;
                _tray.Dispose();
                if (_icon != null) _icon.Dispose();
                if (_iconHandle != IntPtr.Zero) DestroyIcon(_iconHandle);
                _float.Close();
            }
            catch { }
            base.ExitThreadCore();
        }
    }
}
