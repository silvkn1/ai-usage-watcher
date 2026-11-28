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
        private System.Windows.Forms.Timer _updateTimer;
        private FileSystemWatcher _logWatcher, _appWatcher, _geminiWatcher, _codexWatcher, _hooksWatcher, _gemCliWatcher;
        private readonly SynchronizationContext _ui;
        private volatile bool _disposed;
        private bool _exiting, _companionBusy, _settingsAppliedInDialog;

        private void PostUi(Action action)
        {
            if (_disposed || _ui == null || action == null) return;
            try
            {
                _ui.Post(delegate
                {
                    if (_disposed) return;
                    action();
                }, null);
            }
            catch { }
        }

        private ToolStripMenuItem _miShow, _miCompact, _miClickThrough, _miAutostart, _miDock, _miSnap, _miProfiles, _miCompanion, _miRefresh, _miCopy, _miWizard, _miSettings, _miUpdate, _miAbout, _miData, _miExit, _miCsv, _miJson, _miReport, _miProjects, _miMarkSessions;
        private ToolStripMenuItem _miViewGroup, _miToolsGroup, _miHelpGroup, _miZen;
        private ToolStripSeparator _sepProject;
        private ToolStripMenuItem _miOpenFolder, _miOpenTerminal;

        private static string MenuText(string s) { return string.IsNullOrEmpty(s) ? s : s.Replace("&", "&&"); }

        /// <summary>Terapkan bahasa terpilih ke menu yang sudah dibuat, tooltip, dan widget.</summary>
        private void ApplyLanguage()
        {
            _miShow.Text = L.T("tray.show");
            if (_miViewGroup != null) _miViewGroup.Text = MenuText(L.T("tray.menu.view"));
            if (_miToolsGroup != null) _miToolsGroup.Text = MenuText(L.T("tray.menu.tools"));
            if (_miHelpGroup != null) _miHelpGroup.Text = MenuText(L.T("tray.menu.help"));
            _miDock.Text = L.T("tray.dock");
            if (_miSnap != null) { _miSnap.Text = L.T("tray.snap"); PopulateSnapMenu(_miSnap); }
            if (_miProfiles != null) { _miProfiles.Text = L.T("tray.profiles"); PopulateProfilesMenu(); }
            _miCompact.Text = L.T("tray.compact");
            if (_miZen != null) _miZen.Text = L.T("tray.zen");
            _miClickThrough.Text = L.T("tray.clickthrough");
            _miRemaining.Text = L.T("tray.remaining");
            _miRefresh.Text = L.T("tray.refresh");
            if (_miCopy != null) _miCopy.Text = L.T("tray.copystatus");
            if (_miWizard != null) _miWizard.Text = L.T("tray.wizard");
            _miSettings.Text = L.T("tray.settings");
            if (_miUpdate != null) _miUpdate.Text = L.T("tray.update");
            if (_miAbout != null) _miAbout.Text = L.T("tray.about");
            if (_miCompanion != null) _miCompanion.Text = L.T("tray.companion");
            _miData.Text = L.T("tray.datafolder");
            _miCsv.Text = L.T("tray.exportcsv");
            if (_miJson != null) _miJson.Text = L.T("tray.exportjson");
            if (_miReport != null) _miReport.Text = L.T("tray.exportreport");
            _miAutostart.Text = L.T("tray.autostart");
            _miExit.Text = L.T("tray.exit");
            UpdateQuickJumpMenu();
            UpdateMarkSessionsMenu();
            _probeStatus = null;    // pesan galat lama masih berbahasa sebelumnya; diganti saat refresh berikutnya
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
            AutoUpdater.CleanupOldVersion();
            CompanionInstaller.SyncIfInstalled();
            _cfg = AppConfig.Load();
            L.Set(_cfg.Language);   // sebelum menu dan layout pertama
            AudioAlert.CustomWavPath = _cfg.CustomSoundPath;
            ApplyCurrencyOpts();
            _pricing = Pricing.Load();
            _scanner = new UsageScanner(_pricing);
            _scanner.ScanClaude = _cfg.ScanClaude;
            _scanner.ScanGemini = _cfg.ScanGemini;
            _scanner.ScanCodex = _cfg.ScanCodex;
            _scanner.ScanGeminiCli = _cfg.ScanGeminiCli;
            _scanner.ScanOpenCode = _cfg.ScanOpenCode;
            _scanner.ScanCline = _cfg.ScanCline;
            _scanner.ScanCopilot = _cfg.ScanCopilot;
            _scanner.CustomJsonlDirs = _cfg.CustomJsonlDirs;
            _scanner.Roots = ProjectRoots;
            _scanner.SetTrackProjects(true);   // statistik proyek disimpan di memori untuk Project Explorer; _cfg.ShowProjects hanya kontrol tampilan widget
            _scanner.AntigravityQuota5hTokens = _cfg.AntigravityQuota5hTokens;
            _scanner.AntigravityQuota7dTokens = _cfg.AntigravityQuota7dTokens;
            _history = HistoryStore.Load();
            Opts.ShowRemaining = _cfg.ShowRemaining;

            Palette.Apply(ThemeUtil.ResolveLight(_cfg.Theme));
            _float = new FloatingForm();
            _float.Backdrop = _cfg.Backdrop;
            _float.Compact = _cfg.Compact;
            _float.ClickThrough = _cfg.ClickThrough;
            ApplyOpacity();
            _float.PositionCommitted += delegate { SavePosition(); };
            _float.CompactToggled += delegate { SetCompact(!_float.Compact); };
            _float.ClaudeCardDoubleClicked += delegate { OpenProjectTerminal(); };
            _float.AntigravityCardDoubleClicked += delegate { OpenAntigravityFolder(); };
            _float.CardDoubleClicked += delegate(object s, string prov) { OpenProviderCard(prov); };
            _float.DoneSessionsClicked += delegate { AcknowledgeDoneSessions(); };
            _float.SessionMenuRequested += delegate(object s, MouseEventArgs me) { ShowSessionJumpMenu(_float, me.Location); };
            _float.CopyCompactRequested += delegate { CopyCompactCommand(); };
            _float.OpenActiveTerminalRequested += delegate { OpenProjectTerminal(); };
            _float.SettingsRequested += delegate { OpenSettings(); };
            _float.PresetCycleRequested += delegate { CycleViewPreset(); };
            IntPtr force = _float.Handle;   // buat handle sekarang (untuk DPI & ukuran)
            _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

            _menu = BuildMenu();
            MenuTheme.Apply(_menu);
            _float.ContextMenuStrip = _menu;

            _tray = new NotifyIcon();
            _tray.ContextMenuStrip = _menu;
            _tray.Text = L.AppName;
            _tray.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) OnTrayClick(); };
            _tray.BalloonTipClicked += delegate
            {
                if (_lastBalloonIsUpdate && _pendingUpdate != null)
                {
                    _lastBalloonIsUpdate = false;
                    HandleUpdateResult(_pendingUpdate, true);
                    return;
                }
                if (!string.IsNullOrEmpty(_lastBalloonHint) || !string.IsNullOrEmpty(_lastBalloonCwd))
                {
                    FocusOrOpenSession(_lastBalloonHint, _lastBalloonCwd, _lastBalloonProvider);
                    return;
                }
                if (!_cfg.ShowFloating) ToggleFloating();
                else _float.Activate();
            };
            UpdateTrayIcon();
            _tray.Visible = true;

            _float.SetData(BuildVm());
            _float.PlaceAt(_cfg.FloatX, _cfg.FloatY, _cfg.HasPosition);
            _float.Edge = _cfg.DockEdge;
            _float.MonitorName = _cfg.DockMonitor;
            _float.Backdrop = _cfg.Backdrop;
            Point dockCenter = _float.DockDefaultCenter();
            _float.DockCenterX = _cfg.HasDockX ? _cfg.DockCenterX : dockCenter.X;
            _float.DockCenterY = _cfg.HasDockY ? _cfg.DockCenterY : dockCenter.Y;
            if (_cfg.DockTop) _float.DockTop = true;
            ApplyVisibility();
            ApplyTopMost(false);
            _miShow.Checked = _cfg.ShowFloating;

            _fsTimer = new System.Windows.Forms.Timer();
            _fsTimer.Interval = 1000;
            _fsTimer.Tick += delegate
            {
                CheckForeground();
                CheckSystemTheme();
                bool altHover = _cfg.ClickThrough && !_cfg.DockTop && _float.Visible &&
                                (Control.ModifierKeys & Keys.Alt) != 0 && _float.Bounds.Contains(Cursor.Position);
                _float.SetTempInteractive(altHover);
            };
            _fsTimer.Start();

            _tick = new System.Windows.Forms.Timer();
            _tick.Interval = 60 * 1000;
            _tick.Tick += delegate { OnTick(); };
            _tick.Start();

            if (_cfg.AutoUpdateCheck)
            {
                _updateTimer = new System.Windows.Forms.Timer();
                _updateTimer.Interval = 5000;
                _updateTimer.Tick += delegate
                {
                    if (_updateTimer != null)
                    {
                        _updateTimer.Stop();
                        _updateTimer.Dispose();
                        _updateTimer = null;
                    }
                    if (!_disposed) CheckForUpdates(false);
                };
                _updateTimer.Start();
            }

            _debounce = new System.Windows.Forms.Timer();
            _debounce.Interval = 800;
            _debounce.Tick += delegate { _debounce.Stop(); RequestScan(false); };

            _prewarmTimer = new System.Windows.Forms.Timer();
            _prewarmTimer.Interval = 1200;
            _prewarmTimer.Tick += delegate
            {
                _prewarmTimer.Stop();
                _prewarmTimer.Dispose();
                _prewarmTimer = null;
                try { if (_settingsForm == null) EnsureSettingsForm().PrewarmHandles(); } catch { }
            };
            _prewarmTimer.Start();

            _hotkey = new HotkeyWindow();
            _hotkey.Pressed += delegate { ToggleFloating(); };
            ApplyHotkey(false);

            StartWatchers();
            _scanner.SyncHookSessions(NotifyCli.GetActiveHookSessions());
            HandleNotify();   // peristiwa hook yang tertinggal (yang basi diabaikan)
            ReloadLimits();
            RequestScan();
            MaybeFetchOrg(false);
            MaybeFetchGoogleRate(false);
            MaybeFetchOfficialPricing(false);
            MaybeFetchAntigravityRate(false);
            MaybeCheckOllama(false);
            if (!_cfg.FirstRunCompleted)
            {
                PostUi(delegate { ShowWizard(); });
            }
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
            if (_popup != null && !_popup.IsDisposed && _popup.Visible) { _popup.Hide(); _popupHiddenAt = DateTime.Now; return; }
            // Klik pada ikon saat popup terbuka lebih dulu membuat popup kehilangan fokus (tertutup); jangan buka lagi oleh klik yang sama.
            if ((DateTime.Now - _popupHiddenAt).TotalMilliseconds < 400) return;
            ShowPopup();
        }

        private void ShowPopup()
        {
            if (_popup == null || _popup.IsDisposed)
            {
                _popup = new FloatingForm(true);
                _popup.Backdrop = _cfg.Backdrop;
                _popup.Dismissed += delegate { _popupHiddenAt = DateTime.Now; };
                _popup.DoneSessionsClicked += delegate { AcknowledgeDoneSessions(); };
                _popup.SessionMenuRequested += delegate(object s, MouseEventArgs me) { ShowSessionJumpMenu(_popup, me.Location); };
                _popup.CopyCompactRequested += delegate { CopyCompactCommand(); };
                _popup.OpenActiveTerminalRequested += delegate { OpenProjectTerminal(); };
                _popup.CardDoubleClicked += delegate(object s, string prov) { OpenProviderCard(prov); };
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
            if (!_popup.IsDisposed) { _popup.Hide(); _popup.Dispose(); }
            _popup = null;
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
            if (_cfg.ScanCodex)
            {
                if (Directory.Exists(Paths.CodexSessionsDir) && !r.Contains(Paths.CodexSessionsDir, StringComparer.OrdinalIgnoreCase))
                    r.Add(Paths.CodexSessionsDir);
                if (_cfg.ScanWsl)
                    foreach (string w in WslRoots.GetCodex()) if (!r.Contains(w, StringComparer.OrdinalIgnoreCase)) r.Add(w);
            }
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
                if (_cfg.Hotkey != "Ctrl+Alt+U" && _hotkey.Register("Ctrl+Alt+U"))
                {
                    Log.Write("hotkey: registered Ctrl+Alt+U as fallback");
                    return;
                }
                Log.Write("hotkey: registration failed");
                if (reportFailure) MessageBox.Show(L.T("msg.hotkey.fail", _cfg.Hotkey), L.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private string _lastBalloonHint, _lastBalloonCwd, _lastBalloonProvider;

        private void FocusOrOpenSession(string hint, string cwd, string provider)
        {
            if (ProcessHelper.FocusWindow(hint, cwd, provider)) return;
            if (!string.IsNullOrEmpty(cwd) && Directory.Exists(cwd))
                ProcessHelper.OpenTerminal(cwd);
        }

        private void ShowBalloon(string text, ToolTipIcon icon, string focusHint = null, string cwd = null, string provider = null)
        {
            _lastBalloonIsUpdate = false;
            _lastBalloonHint = focusHint;
            _lastBalloonCwd = cwd;
            _lastBalloonProvider = provider;
            try { _tray.ShowBalloonTip(8000, L.AppName, text, icon); }
            catch (Exception ex) { Log.Write("balloon", ex); }
        }

        // ---------------- anggaran biaya, peristiwa hook, ekspor CSV ----------------
        private readonly BudgetEngine _budget = new BudgetEngine();

        private bool IsQuietNow() { return QuietHours.IsActive(_cfg, DateTime.Now); }

        private void CopyCompactCommand()
        {
            try
            {
                Clipboard.SetText("/compact");
                ShowBalloon(L.T("msg.compact.copied"), ToolTipIcon.Info);
            }
            catch (Exception ex) { Log.Write("copy.compact", ex); }
        }

        private void RunBudget()
        {
            if (_usage == null || !_usage.Ready) return;
            if (_cfg.BudgetDaily <= 0 && _cfg.BudgetMonthly <= 0) return;
            bool quiet = IsQuietNow();
            foreach (BudgetAlert a in _budget.Evaluate(_usage.TodayTotal.Cost, _history.MonthToDateCost(DateTime.Now),
                         _cfg.BudgetDaily, _cfg.BudgetMonthly, DateTime.Now))
            {
                bool exceeded = a.Percent >= 100;
                string text = exceeded
                    ? L.T("alert.budget.exceeded", Fmt.Usd(a.Cost), a.Percent, Fmt.Usd(a.Budget))
                    : L.T(a.Monthly ? "alert.budget.month" : "alert.budget.day", Fmt.Usd(a.Cost), a.Percent, Fmt.Usd(a.Budget));

                Log.Write("alert budget " + (a.Monthly ? "month " : "day ") + a.Percent);
                ShowBalloon(text, exceeded ? ToolTipIcon.Error : ToolTipIcon.Warning);
                if (_cfg.AudioAlertsEnabled && !quiet)
                {
                    AudioAlert.PlayWarning();
                    if (exceeded) AudioAlert.PlayWarning();
                }
                if (_cfg.WebhookEnabled && !quiet)
                    WebhookSender.Send(_cfg.GetWebhookUrl(), exceeded ? "⚠️ Budget Limit Exceeded!" : "AI Usage Watcher Budget Alert", text);
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
            string sid = J.Str(e, "sid");
            long elapsed = J.Long(e, "elapsed");
            double ts = J.Num(e, "ts");
            double nowUnix = (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
            if (!double.IsNaN(ts) && nowUnix - ts > 90) return;                               // peristiwa basi (widget baru jalan)

            if (kind == "stop")
            {
                _scanner.MarkHookFinished(sid);
                _usage = _scanner.Snapshot();
                Refresh();
            }
            else if (kind == "input")
            {
                _scanner.MarkHookNeedsConfirm(sid);
                _usage = _scanner.Snapshot();
                Refresh();
            }

            if (kind == "prompt") return;
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
            string nHint = _usage != null ? _usage.LastActiveProject : null;
            string nCwd = _usage != null ? _usage.LastActiveCwd : null;
            if (!string.IsNullOrEmpty(sid) && _usage != null && _usage.Sessions != null && _usage.Sessions.Items != null)
            {
                foreach (SessionActivity sa in _usage.Sessions.Items)
                {
                    if (sa != null && string.Equals(sa.Id, sid, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(sa.Project)) nHint = sa.Project;
                        if (!string.IsNullOrEmpty(sa.Cwd)) nCwd = sa.Cwd;
                        break;
                    }
                }
            }
            ShowBalloon(text, ToolTipIcon.Info, nHint, nCwd, "Claude");
            bool quiet = IsQuietNow();
            if (_cfg.AudioAlertsEnabled && !quiet)
            {
                if (kind == "input") AudioAlert.PlayPrompt();
                else AudioAlert.PlayNotify();
            }
            if (_cfg.WebhookEnabled && kind == "input" && !quiet)
            {
                WebhookSender.Send(_cfg.GetWebhookUrl(), "Claude Code Input Required", text);
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

        private void ExportJson()
        {
            if (_history.Count == 0) { MessageBox.Show(L.T("msg.csv.empty"), L.AppName); return; }
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Filter = L.T("msg.json.filter");
                d.FileName = "ai-usage-history-" + DateTime.Now.ToString("yyyyMMdd") + ".json";
                d.DefaultExt = "json";
                d.OverwritePrompt = true;
                if (d.ShowDialog() != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(d.FileName, _history.ToJson(), new System.Text.UTF8Encoding(false));
                    MessageBox.Show(L.T("msg.csv.done", d.FileName), L.AppName);
                }
                catch (Exception ex) { Log.Write("json.export", ex); MessageBox.Show(ex.Message, L.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
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
                else if (a.Window == "codex_five_hour") name = L.T("limit5.cx");
                else if (a.Window == "codex_seven_day") name = L.T("limit7.cx");
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
                ShowBalloon(text, a.IsReset ? ToolTipIcon.Info : ToolTipIcon.Warning);
                bool quiet = IsQuietNow();
                if (_cfg.AudioAlertsEnabled && !quiet)
                {
                    if (a.IsReset) AudioAlert.PlayNotify();
                    else AudioAlert.PlayWarning();
                }
                if (_cfg.WebhookEnabled && !quiet) WebhookSender.Send(_cfg.GetWebhookUrl(), "AI Usage Watcher Alert", text);
            }
        }

        private DateTime _lastBurnRateAlert = DateTime.MinValue;
        private DateTime _lastContextAlert = DateTime.MinValue;
        private DateTime _lastCacheDropAlert = DateTime.MinValue;

        private void CheckBurnRate()
        {
            if (!_cfg.BurnRateAlertEnabled || _usage == null || !_usage.Ready) return;
            int cd = Math.Max(1, _cfg.AlertCooldownMinutes);
            if (_usage.TokensLast10m >= _cfg.BurnRateThresholdTokens && (DateTime.Now - _lastBurnRateAlert).TotalMinutes >= cd)
            {
                _lastBurnRateAlert = DateTime.Now;
                string text = L.T("alert.burnrate", Fmt.Tokens(_usage.TokensLast10m));
                ShowBalloon(text, ToolTipIcon.Warning);
                bool quiet = IsQuietNow();
                if (_cfg.AudioAlertsEnabled && !quiet) AudioAlert.PlayWarning();
                if (_cfg.WebhookEnabled && !quiet) WebhookSender.Send(_cfg.GetWebhookUrl(), "Token Burn Rate Alert", text);
                Log.Write("alert burnrate " + _usage.TokensLast10m);
            }
        }

        private void CheckContextAlert()
        {
            if (!_cfg.ContextAlertEnabled || _usage == null || !_usage.Ready) return;
            int cd = Math.Max(1, _cfg.AlertCooldownMinutes);
            long cap = ContextLimits.WindowForModel(_usage.LastContextModel, _usage.LastContextTokens);
            double pctExact = (double)_usage.LastContextTokens / cap * 100.0;
            if (pctExact >= 75.0 && (DateTime.Now - _lastContextAlert).TotalMinutes >= cd)
            {
                _lastContextAlert = DateTime.Now;
                int pct = (int)Math.Round(pctExact);
                string text = L.T("alert.context", Fmt.Tokens(_usage.LastContextTokens), pct);
                ShowBalloon(text, ToolTipIcon.Warning, _usage.LastActiveProject, _usage.LastActiveCwd, null);
                bool quiet = IsQuietNow();
                if (_cfg.AudioAlertsEnabled && !quiet) AudioAlert.PlayWarning();
                if (_cfg.WebhookEnabled && !quiet) WebhookSender.Send(_cfg.GetWebhookUrl(), "Session Context Alert", text);
                Log.Write("alert context " + _usage.LastContextTokens);
            }
        }

        private void CheckCacheEfficiencyAlert()
        {
            if (!_cfg.CacheDropAlertEnabled || _usage == null || !_usage.Ready) return;
            int cd = Math.Max(1, _cfg.AlertCooldownMinutes);
            if ((DateTime.Now - _lastCacheDropAlert).TotalMinutes < cd) return;
            SessionCostRecord drop = CacheEfficiencyAdvisor.FindCacheDrop(_usage, _cfg.CacheDropMinPercent);
            if (drop == null) return;

            _lastCacheDropAlert = DateTime.Now;
            int hitPct = (int)Math.Round(drop.CacheHit * 100);
            string proj = string.IsNullOrEmpty(drop.Project) ? drop.Provider : drop.Project;
            string cwd = null;
            if (_usage.ProjectPaths != null && !string.IsNullOrEmpty(drop.Project))
                _usage.ProjectPaths.TryGetValue(drop.Project, out cwd);
            string text = L.T("alert.cachedrop", proj, hitPct, Fmt.Tokens(drop.InputSide));
            ShowBalloon(text, ToolTipIcon.Warning, proj, cwd, drop.Provider);
            bool quiet = IsQuietNow();
            if (_cfg.AudioAlertsEnabled && !quiet) AudioAlert.PlayWarning();
            if (_cfg.WebhookEnabled && !quiet) WebhookSender.Send(_cfg.GetWebhookUrl(), "Cache Efficiency Alert", text);
            Log.Write("alert cachedrop " + proj + " " + hitPct + "%");
        }

        private void ApplyCurrencyOpts()
        {
            Opts.CurrencyCustomEnabled = _cfg.CurrencyCustomEnabled;
            Opts.CurrencySymbol = _cfg.CurrencySymbol;
            Opts.CurrencyRate = _cfg.CurrencyRate;
            Opts.CurrencyShowBoth = _cfg.CurrencyShowBoth;
            Opts.CardLayout = _cfg.CardLayout;
            Opts.MiniPillMode = _cfg.MiniPillMode;
            Opts.ShowContextBar = _cfg.ShowContextBar;
            Opts.WidgetScale = _cfg.WidgetScale > 0 ? _cfg.WidgetScale : 1.0;
            Opts.ZenMode = _cfg.ZenMode;
            Opts.ContextWindowOverride = _cfg.ContextWindowOverride;
        }

        private ContextMenuStrip _sessionJumpMenu;

        private void AcknowledgeDoneSessions()
        {
            _scanner.AcknowledgeDoneSessions();
            _usage = _scanner.Snapshot();
            Refresh();
            UpdateMarkSessionsMenu();
        }

        private void ShowSessionJumpMenu(Control anchor, Point loc)
        {
            if (anchor == null || _usage == null || _usage.Sessions == null || _usage.Sessions.Items.Count == 0) return;
            try
            {
                if (_sessionJumpMenu != null) { _sessionJumpMenu.Dispose(); _sessionJumpMenu = null; }
                ContextMenuStrip sm = new ContextMenuStrip();
                bool hasCode = ProcessHelper.HasVSCode();
                bool anyAck = false;
                foreach (SessionActivity sa in _usage.Sessions.Items)
                {
                    SessionActivity item = sa;
                    string icon = item.State == "confirm" ? "? " : (item.State == "running" ? "● " : "✓ ");
                    string lbl = string.IsNullOrEmpty(item.Project) || string.Equals(item.Project, item.Provider, StringComparison.OrdinalIgnoreCase)
                        ? item.Provider
                        : item.Provider + " · " + item.Project;
                    if (item.SessionTokens > 0)
                    {
                        string stat = item.SessionCost > 0
                            ? Fmt.Usd(item.SessionCost)
                            : Fmt.Tokens(item.SessionTokens);
                        if (item.SessionCacheHit > 0.005)
                            stat += string.Format(" · {0}%", (int)Math.Round(item.SessionCacheHit * 100));
                        lbl += "  (" + stat + ")";
                    }
                    ToolStripMenuItem mi = new ToolStripMenuItem(MenuText(icon + lbl));
                    bool dirOk = !string.IsNullOrEmpty(item.Cwd) && Directory.Exists(item.Cwd);
                    string focusHint = !string.IsNullOrEmpty(item.Project) ? item.Project : item.Provider;
                    mi.Click += delegate { FocusOrOpenSession(focusHint, item.Cwd, item.Provider); };
                    if (!string.IsNullOrEmpty(focusHint))
                    {
                        mi.DropDownItems.Add(new ToolStripMenuItem(MenuText(L.T("sess.menu.focus", focusHint)), null, delegate
                        {
                            FocusOrOpenSession(focusHint, item.Cwd, item.Provider);
                        }));
                    }
                    if (dirOk)
                    {
                        string pName = item.Project ?? item.Provider;
                        mi.DropDownItems.Add(new ToolStripMenuItem(MenuText(L.T("sess.menu.term", pName)), null, delegate { ProcessHelper.OpenTerminal(item.Cwd); }));
                        mi.DropDownItems.Add(new ToolStripMenuItem(MenuText(L.T("sess.menu.folder", pName)), null, delegate { ProcessHelper.OpenFolder(item.Cwd); }));
                        if (hasCode) mi.DropDownItems.Add(new ToolStripMenuItem(MenuText(L.T("sess.menu.vscode", pName)), null, delegate { ProcessHelper.OpenVSCode(item.Cwd); }));
                    }
                    if (item.State != "running")
                    {
                        anyAck = true;
                        if (dirOk) mi.DropDownItems.Add(new ToolStripSeparator());
                        mi.DropDownItems.Add(new ToolStripMenuItem(L.T("sess.menu.ack"), null, delegate
                        {
                            _scanner.AcknowledgeSession(item.Id);
                            _usage = _scanner.Snapshot();
                            Refresh();
                            UpdateMarkSessionsMenu();
                        }));
                    }
                    sm.Items.Add(mi);
                }
                if (anyAck)
                {
                    sm.Items.Add(new ToolStripSeparator());
                    sm.Items.Add(new ToolStripMenuItem(L.T("sess.menu.ackall"), null, delegate { AcknowledgeDoneSessions(); }));
                }
                MenuTheme.Apply(sm);
                _sessionJumpMenu = sm;
                sm.Show(anchor, loc);
            }
            catch (Exception ex) { Log.Write("sess.menu", ex); }
        }

        private void UpdateMarkSessionsMenu()
        {
            if (_miMarkSessions == null) return;
            int pending = (_usage != null && _usage.Sessions != null)
                ? (_usage.Sessions.DoneCount + _usage.Sessions.ConfirmCount)
                : 0;
            _miMarkSessions.Text = L.T("tray.marksessions", pending);
            _miMarkSessions.Visible = pending > 0;
        }

        private void SyncHookSessionsAndRefresh()
        {
            _scanner.SyncHookSessions(NotifyCli.GetActiveHookSessions());
            _usage = _scanner.Snapshot();
            Refresh();
        }

        private void UpdateQuickJumpMenu()
        {
            if (_miOpenFolder == null || _miOpenTerminal == null) return;
            string cwd = _usage != null ? _usage.LastActiveCwd : null;
            string proj = _usage != null ? _usage.LastActiveProject : null;
            bool hasCwd = !string.IsNullOrEmpty(cwd) && Directory.Exists(cwd);
            _miOpenFolder.Visible = hasCwd;
            _miOpenTerminal.Visible = hasCwd;
            if (_sepProject != null) _sepProject.Visible = hasCwd;
            if (hasCwd)
            {
                _miOpenFolder.Text = MenuText(L.T("tray.openfolder", proj ?? "Project"));
                _miOpenTerminal.Text = MenuText(L.T("tray.openterminal", proj ?? "Project"));
            }
        }

        private void OpenProjectFolder()
        {
            try
            {
                if (_usage != null && !string.IsNullOrEmpty(_usage.LastActiveCwd) && Directory.Exists(_usage.LastActiveCwd))
                    ProcessHelper.OpenFolder(_usage.LastActiveCwd);
            }
            catch (Exception ex) { Log.Write("open.folder", ex); }
        }

        private void OpenProjectTerminal()
        {
            try
            {
                if (_usage != null && !string.IsNullOrEmpty(_usage.LastActiveCwd) && Directory.Exists(_usage.LastActiveCwd))
                    ProcessHelper.OpenTerminal(_usage.LastActiveCwd);
            }
            catch (Exception ex) { Log.Write("open.terminal", ex); }
        }

        private void OpenAntigravityFolder()
        {
            try { ProcessHelper.OpenFolder(Paths.AntigravityDir); }
            catch (Exception ex) { Log.Write("open.antigravity", ex); }
        }

        private void OpenProviderCard(string title)
        {
            if (string.IsNullOrEmpty(title)) return;
            if (title.IndexOf("Antigravity", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                OpenAntigravityFolder();
                return;
            }
            string provKey = title.IndexOf("Codex", StringComparison.OrdinalIgnoreCase) >= 0 ? "Codex"
                : (title.IndexOf("Gemini", StringComparison.OrdinalIgnoreCase) >= 0 ? "Gemini CLI"
                : (title.IndexOf("OpenCode", StringComparison.OrdinalIgnoreCase) >= 0 ? "OpenCode"
                : (title.IndexOf("Cline", StringComparison.OrdinalIgnoreCase) >= 0 ? "Cline"
                : (title.IndexOf("Copilot", StringComparison.OrdinalIgnoreCase) >= 0 ? "Copilot" : "Claude"))));
            string dir = null;
            if (_usage != null)
            {
                if (_usage.Sessions != null && _usage.Sessions.Items != null)
                {
                    foreach (SessionActivity sa in _usage.Sessions.Items)
                    {
                        if (string.Equals(sa.Provider, provKey, StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrEmpty(sa.Cwd) && Directory.Exists(sa.Cwd))
                        { dir = sa.Cwd; break; }
                    }
                }
                if (dir == null && _usage.ProjectProviders != null && _usage.ProjectPaths != null)
                {
                    foreach (KeyValuePair<string, string> kv in _usage.ProjectProviders)
                    {
                        if (kv.Value != null && kv.Value.IndexOf(provKey, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            string candidate;
                            if (_usage.ProjectPaths.TryGetValue(kv.Key, out candidate) && Directory.Exists(candidate))
                            { dir = candidate; break; }
                        }
                    }
                }
                if (dir == null && provKey == "Claude" && !string.IsNullOrEmpty(_usage.LastActiveCwd) && Directory.Exists(_usage.LastActiveCwd))
                    dir = _usage.LastActiveCwd;
            }
            if (!string.IsNullOrEmpty(dir)) ProcessHelper.OpenTerminal(dir);
            else if (provKey == "Claude") OpenProjectTerminal();
        }

        private void CopyStatus()
        {
            try
            {
                string text = StatusFormatter.Format(_limits, _usage, _cfg.ScanClaude, _cfg.ScanGemini, _cfg.ScanCodex, OllamaProbe.Current);
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
            _miRefresh = new ToolStripMenuItem(L.T("tray.refresh"), null, delegate { ReloadLimits(); RequestScan(); MaybeFetchOrg(true); MaybeProbe(true); MaybeFetchGoogleRate(true); MaybeFetchOfficialPricing(true); MaybeFetchAntigravityRate(true); MaybeCheckOllama(true); });
            _miCopy = new ToolStripMenuItem(L.T("tray.copystatus"), null, delegate { CopyStatus(); });

            // 1. Submenu: Tampilan & Posisi (View & Layout)
            _miViewGroup = new ToolStripMenuItem(MenuText(L.T("tray.menu.view")));
            _miDock = new ToolStripMenuItem(L.T("tray.dock"), null, delegate { SetDockTop(!_float.DockTop); });
            _miDock.Checked = _cfg.DockTop;
            _miSnap = new ToolStripMenuItem(L.T("tray.snap"));
            PopulateSnapMenu(_miSnap);
            _miProfiles = new ToolStripMenuItem(MenuText(L.T("tray.profiles")));
            _miProfiles.DropDownOpening += delegate { PopulateProfilesMenu(); };
            PopulateProfilesMenu();
            _miCompact = new ToolStripMenuItem(L.T("tray.compact"), null, delegate { SetCompact(!_float.Compact); });
            _miCompact.Checked = _cfg.Compact;
            _miCompact.Enabled = !_cfg.DockTop;
            _miZen = new ToolStripMenuItem(L.T("tray.zen"), null, delegate { SetZenMode(!_cfg.ZenMode); });
            _miZen.Checked = _cfg.ZenMode;
            _miRemaining = new ToolStripMenuItem(L.T("tray.remaining"), null, delegate { SetRemaining(!_cfg.ShowRemaining); });
            _miRemaining.Checked = _cfg.ShowRemaining;
            _miClickThrough = new ToolStripMenuItem(L.T("tray.clickthrough"), null, delegate { SetClickThrough(!_cfg.ClickThrough); });
            _miClickThrough.Checked = _cfg.ClickThrough;
            _miClickThrough.Enabled = !_cfg.DockTop;

            _miViewGroup.DropDownItems.Add(_miDock);
            _miViewGroup.DropDownItems.Add(_miSnap);
            _miViewGroup.DropDownItems.Add(_miProfiles);
            _miViewGroup.DropDownItems.Add(new ToolStripSeparator());
            _miViewGroup.DropDownItems.Add(_miCompact);
            _miViewGroup.DropDownItems.Add(_miZen);
            _miViewGroup.DropDownItems.Add(_miRemaining);
            _miViewGroup.DropDownItems.Add(_miClickThrough);

            // 2. Submenu: Laporan & Alat (Reports & Tools)
            _miToolsGroup = new ToolStripMenuItem(MenuText(L.T("tray.menu.tools")));
            _miProjects = new ToolStripMenuItem(L.T("tray.projects"), null, delegate { OpenProjectExplorer(); });
            _miMarkSessions = new ToolStripMenuItem(L.T("tray.marksessions", 0), null, delegate { AcknowledgeDoneSessions(); });
            _miMarkSessions.Visible = false;
            _miReport = new ToolStripMenuItem(L.T("tray.exportreport"), null, delegate { ExportMonthlyReport(); });
            _miCsv = new ToolStripMenuItem(L.T("tray.exportcsv"), null, delegate { ExportCsv(); });
            _miJson = new ToolStripMenuItem(L.T("tray.exportjson"), null, delegate { ExportJson(); });
            _miData = new ToolStripMenuItem(L.T("tray.datafolder"), null, delegate
            {
                try
                {
                    Paths.Ensure();
                    Process.Start(ProcessHelper.ExplorerExe, "\"" + Paths.AppDir + "\"");
                }
                catch (Exception ex) { Log.Write("open.data", ex); }
            });
            _miCompanion = new ToolStripMenuItem(L.T("tray.companion"), null, delegate { InstallCompanion(); });

            _miToolsGroup.DropDownItems.Add(_miProjects);
            _miToolsGroup.DropDownItems.Add(_miMarkSessions);
            _miToolsGroup.DropDownItems.Add(_miReport);
            _miToolsGroup.DropDownItems.Add(_miCsv);
            _miToolsGroup.DropDownItems.Add(_miJson);
            _miToolsGroup.DropDownItems.Add(new ToolStripSeparator());
            _miToolsGroup.DropDownItems.Add(_miData);
            _miToolsGroup.DropDownItems.Add(_miCompanion);

            // 3. Submenu: Bantuan & Tentang (Help & About)
            _miHelpGroup = new ToolStripMenuItem(MenuText(L.T("tray.menu.help")));
            _miUpdate = new ToolStripMenuItem(L.T("tray.update"), null, delegate { CheckForUpdates(true); });
            _miWizard = new ToolStripMenuItem(L.T("tray.wizard"), null, delegate { ShowWizard(); });
            _miAbout = new ToolStripMenuItem(L.T("tray.about"), null, delegate { ShowAbout(); });

            _miHelpGroup.DropDownItems.Add(_miUpdate);
            _miHelpGroup.DropDownItems.Add(_miWizard);
            _miHelpGroup.DropDownItems.Add(new ToolStripSeparator());
            _miHelpGroup.DropDownItems.Add(_miAbout);

            // Pengaturan & Umum
            _miSettings = new ToolStripMenuItem(L.T("tray.settings"), null, delegate { OpenSettings(); });
            _miAutostart = new ToolStripMenuItem(L.T("tray.autostart"), null, delegate { ToggleAutostart(); });
            try { _miAutostart.Checked = Autostart.IsEnabled; } catch { }
            _miExit = new ToolStripMenuItem(L.T("tray.exit"), null, delegate { ExitThread(); });

            // Aksi Konteks Proyek Aktif
            _miOpenFolder = new ToolStripMenuItem("", null, delegate { OpenProjectFolder(); });
            _miOpenTerminal = new ToolStripMenuItem("", null, delegate { OpenProjectTerminal(); });
            _miOpenFolder.Visible = false;
            _miOpenTerminal.Visible = false;
            _sepProject = new ToolStripSeparator();
            _sepProject.Visible = false;

            // Susun menu utama
            m.Items.Add(_miShow);
            m.Items.Add(_miRefresh);
            m.Items.Add(_miCopy);
            m.Items.Add(new ToolStripSeparator());

            m.Items.Add(_miViewGroup);
            m.Items.Add(_miToolsGroup);
            m.Items.Add(new ToolStripSeparator());

            m.Items.Add(_miSettings);
            m.Items.Add(_miAutostart);
            m.Items.Add(_miHelpGroup);

            m.Items.Add(_sepProject);
            m.Items.Add(_miOpenFolder);
            m.Items.Add(_miOpenTerminal);

            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(_miExit);

            m.Opening += delegate
            {
                _miShow.Checked = _cfg.ShowFloating;
                if (_miSnap != null) PopulateSnapMenu(_miSnap);
                UpdateQuickJumpMenu();
                UpdateMarkSessionsMenu();
                MenuTheme.Apply(m);
            };
            return m;
        }

        private void PopulateSnapMenu(ToolStripMenuItem snapRoot)
        {
            snapRoot.DropDownItems.Clear();
            Screen[] screens = Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                Screen s = screens[i];
                string scName = s.Primary
                    ? string.Format("Display {0} ({1}) - {2}x{3}", i + 1, L.T("monitor.primary"), s.Bounds.Width, s.Bounds.Height)
                    : string.Format("Display {0} - {1}x{2}", i + 1, s.Bounds.Width, s.Bounds.Height);

                ToolStripMenuItem miScreen = new ToolStripMenuItem(scName);
                string devName = s.DeviceName;
                bool isCurrentScreen = string.IsNullOrEmpty(_cfg.DockMonitor) ? s.Primary : string.Equals(_cfg.DockMonitor, devName, StringComparison.OrdinalIgnoreCase);

                AddSnapItem(miScreen, L.T("edge.top_left"), "top-left", devName, isCurrentScreen);
                AddSnapItem(miScreen, L.T("edge.top_right"), "top-right", devName, isCurrentScreen);
                AddSnapItem(miScreen, L.T("edge.bottom_left"), "bottom-left", devName, isCurrentScreen);
                AddSnapItem(miScreen, L.T("edge.bottom_right"), "bottom-right", devName, isCurrentScreen);
                miScreen.DropDownItems.Add(new ToolStripSeparator());
                AddSnapItem(miScreen, L.T("edge.top"), "top", devName, isCurrentScreen);
                AddSnapItem(miScreen, L.T("edge.bottom"), "bottom", devName, isCurrentScreen);
                AddSnapItem(miScreen, L.T("edge.left"), "left", devName, isCurrentScreen);
                AddSnapItem(miScreen, L.T("edge.right"), "right", devName, isCurrentScreen);

                snapRoot.DropDownItems.Add(miScreen);
            }
        }

        private void PopulateProfilesMenu()
        {
            if (_miProfiles == null) return;
            _miProfiles.DropDownItems.Clear();

            ToolStripMenuItem presets = new ToolStripMenuItem(L.T("tray.profiles.presets"));
            presets.DropDownItems.Add(new ToolStripMenuItem(L.T("tray.profiles.preset.work"), null, delegate { ApplyLoadedProfile("Work", AppConfig.BuildPresetProfile("work", _cfg)); }));
            presets.DropDownItems.Add(new ToolStripMenuItem(L.T("tray.profiles.preset.focus"), null, delegate { ApplyLoadedProfile("Focus", AppConfig.BuildPresetProfile("focus", _cfg)); }));
            presets.DropDownItems.Add(new ToolStripMenuItem(L.T("tray.profiles.preset.all"), null, delegate { ApplyLoadedProfile("Default", AppConfig.BuildPresetProfile("all", _cfg)); }));
            _miProfiles.DropDownItems.Add(presets);

            List<string> saved = AppConfig.ListProfileNames();
            if (saved.Count > 0)
            {
                _miProfiles.DropDownItems.Add(new ToolStripSeparator());
                foreach (string name in saved)
                {
                    string profName = name;
                    _miProfiles.DropDownItems.Add(new ToolStripMenuItem(MenuText(profName), null, delegate
                    {
                        AppConfig loaded = _cfg.LoadNamedProfile(profName);
                        if (loaded != null) ApplyLoadedProfile(profName, loaded);
                    }));
                }
            }

            _miProfiles.DropDownItems.Add(new ToolStripSeparator());
            _miProfiles.DropDownItems.Add(new ToolStripMenuItem(L.T("tray.profiles.save"), null, delegate { PromptSaveProfile(); }));

            if (saved.Count > 0)
            {
                ToolStripMenuItem delMenu = new ToolStripMenuItem(L.T("tray.profiles.delete"));
                foreach (string name in saved)
                {
                    string profName = name;
                    delMenu.DropDownItems.Add(new ToolStripMenuItem(MenuText(profName), null, delegate
                    {
                        AppConfig.DeleteNamedProfileInDir(profName, Paths.ProfilesDir);
                        PopulateProfilesMenu();
                    }));
                }
                _miProfiles.DropDownItems.Add(delMenu);
            }
            MenuTheme.ApplyItems(_miProfiles.DropDownItems);
        }

        private void ApplyLoadedProfile(string displayName, AppConfig imported)
        {
            if (imported == null) return;
            string oldEdge = _cfg.DockEdge, oldMonitor = _cfg.DockMonitor;
            _cfg.ApplyProfileFrom(imported);
            ApplyConfigChanges(oldEdge, oldMonitor);
            if (_settingsForm != null && !_settingsForm.IsDisposed) _settingsForm.PrepareForOpen();
            ShowBalloon(L.T("tray.profiles.applied", displayName), ToolTipIcon.Info);
        }

        private void PromptSaveProfile()
        {
            using (Form dlg = new Form())
            using (Font dlgFont = new Font("Segoe UI", 9f))
            {
                dlg.Text = L.T("tray.profiles.prompt.title");
                try { Icon ic = AppIcon.Get(); if (ic != null) dlg.Icon = ic; } catch { }
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MaximizeBox = false; dlg.MinimizeBox = false; dlg.ShowInTaskbar = false;
                dlg.StartPosition = FormStartPosition.CenterScreen;
                dlg.AutoScaleMode = AutoScaleMode.Dpi;
                dlg.AutoScaleDimensions = new SizeF(96f, 96f);
                dlg.AutoSize = true;
                dlg.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                dlg.Padding = new Padding(14);
                dlg.Font = dlgFont;
                dlg.BackColor = Palette.Bg; dlg.ForeColor = Palette.Text;
                DarkUi.TitleBar(dlg.Handle, !Palette.IsLight);

                TableLayoutPanel t = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, Margin = new Padding(0) };
                t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                Label lbl = new Label { Text = L.T("tray.profiles.prompt.label"), AutoSize = true, UseMnemonic = false, ForeColor = Palette.Text, Margin = new Padding(0, 0, 0, 6) };
                TextBox tb = new TextBox { Width = 310, BackColor = Palette.Surface, ForeColor = Palette.Text, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 0, 0, 12) };
                FlowLayoutPanel btns = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, WrapContents = false, Dock = DockStyle.Fill, Margin = new Padding(0) };
                Button cancel = new Button { Text = L.T("set.cancel"), DialogResult = DialogResult.Cancel, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Palette.Surface, ForeColor = Palette.Text };
                cancel.FlatAppearance.BorderColor = Palette.Border;
                Button ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Palette.Accent, ForeColor = Color.White, Margin = new Padding(0, 0, 8, 0) };
                ok.FlatAppearance.BorderSize = 0;
                btns.Controls.Add(cancel);
                btns.Controls.Add(ok);
                t.Controls.Add(lbl); t.Controls.Add(tb); t.Controls.Add(btns);
                dlg.Controls.Add(t);
                dlg.AcceptButton = ok; dlg.CancelButton = cancel;
                if (dlg.ShowDialog() != DialogResult.OK) return;

                string safe = AppConfig.SanitizeProfileName(tb.Text);
                if (safe == null)
                {
                    MessageBox.Show(L.T("tray.profiles.invalid"), L.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (_cfg.SaveNamedProfile(safe))
                {
                    PopulateProfilesMenu();
                    ShowBalloon(L.T("tray.profiles.saved", safe), ToolTipIcon.Info);
                }
            }
        }

        private void AddSnapItem(ToolStripMenuItem parent, string label, string edge, string monitorDevice, bool isCurrentScreen)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(label, null, delegate
            {
                SnapToPosition(edge, monitorDevice);
            });
            if (_cfg.DockTop && isCurrentScreen && string.Equals(_cfg.DockEdge, edge, StringComparison.OrdinalIgnoreCase))
            {
                item.Checked = true;
            }
            parent.DropDownItems.Add(item);
        }

        private void SnapToPosition(string edge, string monitorDevice)
        {
            _cfg.DockTop = true;
            _cfg.DockEdge = edge;
            _cfg.DockMonitor = monitorDevice;
            if (!_cfg.ShowFloating)
            {
                _cfg.ShowFloating = true;
                _miShow.Checked = true;
                _hiddenByFs = false;
                ApplyVisibility();
            }
            _float.SnapTo(edge, monitorDevice);
            ApplyTopMost(true);
            _cfg.DockCenterX = _float.DockCenterX;
            _cfg.DockCenterY = _float.DockCenterY;
            _cfg.HasDockX = true;
            _cfg.HasDockY = true;
            _cfg.Save();
            _miDock.Checked = true;
            _miClickThrough.Enabled = false;
            _miCompact.Enabled = false;
            if (_miSnap != null) PopulateSnapMenu(_miSnap);
        }

        private void InstallCompanion()
        {
            if (_companionBusy) return;
            _companionBusy = true;
            if (_miCompanion != null) _miCompanion.Enabled = false;
            Task.Run(delegate
            {
                string paths = null, error = null;
                bool ok = false;
                try { ok = CompanionInstaller.Install(out paths, out error); }
                catch (Exception ex) { error = ex.Message; }
                PostUi(delegate
                {
                    _companionBusy = false;
                    if (_miCompanion != null && !_exiting) _miCompanion.Enabled = true;
                    if (_disposed) return;
                    if (ok)
                        MessageBox.Show(L.T("companion.success", paths), L.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    else
                        MessageBox.Show(L.T("companion.fail", error), L.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                });
            });
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

        private void SetZenMode(bool on)
        {
            _cfg.ZenMode = on;
            Opts.ZenMode = on;
            if (_miZen != null) _miZen.Checked = on;
            if (on && !_cfg.DockTop && _float.Compact)
            {
                _float.Compact = false;
                if (_miCompact != null) _miCompact.Checked = false;
                _cfg.Compact = false;
            }
            _cfg.Save();
            Refresh();
        }

        private void CycleViewPreset()
        {
            if (!_float.Compact && !Opts.ZenMode)
            {
                SetZenMode(true);
            }
            else if (!_float.Compact && Opts.ZenMode)
            {
                if (_cfg.DockTop)
                {
                    SetZenMode(false);
                }
                else
                {
                    _cfg.ZenMode = false;
                    Opts.ZenMode = false;
                    if (_miZen != null) _miZen.Checked = false;
                    SetCompact(true);
                    Refresh();
                }
            }
            else
            {
                _cfg.ZenMode = false;
                Opts.ZenMode = false;
                if (_miZen != null) _miZen.Checked = false;
                SetCompact(false);
                Refresh();
            }
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

        private void OpenProjectExplorer()
        {
            try
            {
                using (ProjectExplorerForm dlg = new ProjectExplorerForm(_usage, _history, _cfg.BudgetDaily, _cfg.BudgetMonthly, _history.MonthToDateCost(DateTime.Now)))
                {
                    dlg.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                Log.Write("project.explorer", ex);
            }
        }

        private SettingsForm _settingsForm;
        private System.Windows.Forms.Timer _prewarmTimer;
        private string _settingsOldEdge, _settingsOldMonitor;

        private SettingsForm EnsureSettingsForm()
        {
            if (_settingsForm == null || _settingsForm.IsDisposed)
            {
                SettingsCallbacks cb = new SettingsCallbacks();
                cb.ExportCsv = ExportCsv;
                cb.Diagnostics = delegate { return Diag.Build(_cfg, _limits, _usage, _probeFailed, _org, _history.Count); };
                cb.IsOpenCodeSqliteMissing = delegate { return _usage != null && _usage.OpenCodeSqliteMissingCli; };
                cb.CheckUpdates = delegate { CheckForUpdates(true); };
                cb.PricingUpdated = delegate
                {
                    _pricing = Pricing.Load();
                    CheckCustomPricingWarning();
                    _scanner.ResetWithPricing(_pricing);
                    RequestScan();
                };
                cb.Apply = delegate
                {
                    _settingsAppliedInDialog = true;
                    ApplyConfigChanges(_settingsOldEdge, _settingsOldMonitor);
                    _settingsOldEdge = _cfg.DockEdge;
                    _settingsOldMonitor = _cfg.DockMonitor;
                };
                _settingsForm = new SettingsForm(_cfg, cb);
            }
            return _settingsForm;
        }

        private void OpenSettings()
        {
            if (_settingsForm != null && !_settingsForm.IsDisposed && _settingsForm.Visible)
            {
                _settingsForm.Activate();
                return;
            }
            _settingsOldEdge = _cfg.DockEdge;
            _settingsOldMonitor = _cfg.DockMonitor;
            _settingsAppliedInDialog = false;
            SettingsForm f = EnsureSettingsForm();
            f.PrepareForOpen();
            if (f.ShowDialog() != DialogResult.OK) return;
            if (!_settingsAppliedInDialog) ApplyConfigChanges(_settingsOldEdge, _settingsOldMonitor);
        }

        private void ShowWizard()
        {
            string oldEdge = _cfg.DockEdge, oldMonitor = _cfg.DockMonitor;
            using (SetupWizardForm wiz = new SetupWizardForm(_cfg))
            {
                if (wiz.ShowDialog() != DialogResult.OK) return;
            }
            ApplyConfigChanges(oldEdge, oldMonitor);
            ShowBalloon(L.T("wizard.done.tip"), ToolTipIcon.Info);
        }

        private void ShowAbout()
        {
            try
            {
                string repoUrl = "https://github.com/" + AutoUpdater.DefaultRepo;
                using (Form dlg = new Form())
                using (Font dlgFont = new Font("Segoe UI", 9f))
                using (Font titleFont = new Font("Segoe UI", 12f, FontStyle.Bold))
                {
                    Bitmap logoBmp = null;
                    try
                    {
                        dlg.Text = L.T("about.title");
                        try { Icon ic = AppIcon.Get(); if (ic != null) { dlg.Icon = ic; logoBmp = ic.ToBitmap(); } } catch { }
                        dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                        dlg.MaximizeBox = false;
                        dlg.MinimizeBox = false;
                        dlg.ShowInTaskbar = false;
                        dlg.StartPosition = FormStartPosition.CenterScreen;
                        dlg.AutoScaleMode = AutoScaleMode.Dpi;
                        dlg.AutoScaleDimensions = new SizeF(96f, 96f);
                        dlg.AutoSize = true;
                        dlg.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                        dlg.Padding = new Padding(18);
                        dlg.BackColor = Palette.Bg;
                        dlg.ForeColor = Palette.Text;
                        dlg.Font = dlgFont;
                        DarkUi.TitleBar(dlg.Handle, !Palette.IsLight);

                        TableLayoutPanel root = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, Margin = new Padding(0) };
                        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

                        FlowLayoutPanel head = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 12) };
                        PictureBox pic = new PictureBox { Size = new Size(42, 42), SizeMode = PictureBoxSizeMode.StretchImage, Margin = new Padding(0, 0, 12, 0), Image = logoBmp };
                        TableLayoutPanel headTxt = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0) };
                        Label title = new Label { Text = L.AppFullName, Font = titleFont, ForeColor = Palette.Text, AutoSize = true, UseMnemonic = false, Margin = new Padding(0, 0, 0, 2) };
                        Label ver = new Label { Text = L.T("about.version", AutoUpdater.CurrentVersionStr), ForeColor = Palette.Accent, AutoSize = true, UseMnemonic = false, Margin = new Padding(0) };
                        headTxt.Controls.Add(title); headTxt.Controls.Add(ver);
                        head.Controls.Add(pic); head.Controls.Add(headTxt);
                        root.Controls.Add(head);

                        Label desc = new Label { Text = L.T("about.desc"), UseMnemonic = false, ForeColor = Palette.Dim, AutoSize = true, MaximumSize = new Size(390, 0), Margin = new Padding(0, 0, 0, 12) };
                        root.Controls.Add(desc);

                        Label repoLbl = new Label { Text = L.T("about.repo"), UseMnemonic = false, ForeColor = Palette.Dim, AutoSize = true, Margin = new Padding(0, 0, 0, 2) };
                        root.Controls.Add(repoLbl);

                        LinkLabel link = new LinkLabel { Text = repoUrl, AutoSize = true, Margin = new Padding(0, 0, 0, 16), LinkColor = Palette.Accent, ActiveLinkColor = Palette.Text, VisitedLinkColor = Palette.Accent };
                        link.LinkClicked += delegate
                        {
                            if (AutoUpdater.IsTrustedUpdateUrl(repoUrl))
                            {
                                try { Process.Start(new ProcessStartInfo(repoUrl) { UseShellExecute = true }); } catch { }
                            }
                        };
                        root.Controls.Add(link);

                        FlowLayoutPanel btns = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
                        Button btnUpdate = new Button { Text = L.T("tray.update").Replace("...", ""), AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
                        btnUpdate.Click += delegate { dlg.Close(); CheckForUpdates(true); };

                        Button btnCopy = new Button { Text = L.T("about.copy"), AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
                        btnCopy.Click += delegate
                        {
                            try
                            {
                                Clipboard.SetText(L.AppFullName + " v" + AutoUpdater.CurrentVersionStr + " (" + repoUrl + ")");
                                ShowBalloon(L.T("about.copied"), ToolTipIcon.Info);
                            }
                            catch { }
                        };

                        Button btnClose = new Button { Text = L.T("about.close"), AutoSize = true, DialogResult = DialogResult.OK, Margin = new Padding(0) };
                        btns.Controls.Add(btnUpdate); btns.Controls.Add(btnCopy); btns.Controls.Add(btnClose);
                        root.Controls.Add(btns);

                        dlg.Controls.Add(root);
                        dlg.AcceptButton = btnClose;
                        dlg.CancelButton = btnClose;
                        dlg.ShowDialog();
                    }
                    finally
                    {
                        if (logoBmp != null) logoBmp.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("about", ex);
            }
        }

        private void ApplyConfigChanges(string oldEdge, string oldMonitor)
        {
            bool scanTogglesChanged =
                _scanner.ScanClaude != _cfg.ScanClaude ||
                _scanner.ScanGemini != _cfg.ScanGemini ||
                _scanner.ScanCodex != _cfg.ScanCodex ||
                _scanner.ScanGeminiCli != _cfg.ScanGeminiCli ||
                _scanner.ScanOpenCode != _cfg.ScanOpenCode ||
                _scanner.ScanCline != _cfg.ScanCline ||
                _scanner.ScanCopilot != _cfg.ScanCopilot ||
                !string.Equals(_scanner.CustomJsonlDirs ?? "", _cfg.CustomJsonlDirs ?? "", StringComparison.OrdinalIgnoreCase);
            _scanner.ScanClaude = _cfg.ScanClaude;
            _scanner.ScanGemini = _cfg.ScanGemini;
            _scanner.ScanCodex = _cfg.ScanCodex;
            _scanner.ScanGeminiCli = _cfg.ScanGeminiCli;
            _scanner.ScanOpenCode = _cfg.ScanOpenCode;
            _scanner.ScanCline = _cfg.ScanCline;
            _scanner.ScanCopilot = _cfg.ScanCopilot;
            _scanner.CustomJsonlDirs = _cfg.CustomJsonlDirs;
            if (scanTogglesChanged) _scanner.ResetWithPricing(_pricing);
            _scanner.AntigravityQuota5hTokens = _cfg.AntigravityQuota5hTokens;
            _scanner.AntigravityQuota7dTokens = _cfg.AntigravityQuota7dTokens;
            StartWatchers();
            L.Set(_cfg.Language);
            AudioAlert.CustomWavPath = _cfg.CustomSoundPath;
            ApplyCurrencyOpts();
            ApplyLanguage();
            _scanner.SetTrackProjects(true);
            _float.Edge = _cfg.DockEdge;
            _float.MonitorName = _cfg.DockMonitor;
            _float.Backdrop = _cfg.Backdrop;
            if (oldEdge != _cfg.DockEdge || oldMonitor != _cfg.DockMonitor)
            {
                Point c = _float.DockDefaultCenter();   // tepi/monitor baru: mulai dari tengah tepi itu
                _float.DockCenterX = c.X; _float.DockCenterY = c.Y;
                _cfg.DockCenterX = c.X; _cfg.DockCenterY = c.Y; _cfg.HasDockX = true; _cfg.HasDockY = true;
            }
            if (_cfg.DockTop && !_float.DockTop && _float.Visible)
            {
                _cfg.FloatX = _float.Left; _cfg.FloatY = _float.Top; _cfg.HasPosition = true;
            }
            _float.DockTop = _cfg.DockTop;
            if (!_cfg.DockTop) _float.PlaceAt(_cfg.FloatX, _cfg.FloatY, _cfg.HasPosition);
            _float.ApplyDock();
            _miDock.Checked = _cfg.DockTop;
            _miClickThrough.Enabled = !_cfg.DockTop;
            _miCompact.Enabled = !_cfg.DockTop;

            Palette.Apply(ThemeUtil.ResolveLight(_cfg.Theme));
            _float.ApplyTheme();
            MenuTheme.Apply(_menu);
            ResetPopup();
            ApplyTopMost(true);
            _hiddenByFs = _hiddenByFs && _cfg.HideOnFullscreen;
            ApplyVisibility();

            _float.Compact = _cfg.Compact;
            _miCompact.Checked = _cfg.Compact;
            if (_miZen != null) _miZen.Checked = _cfg.ZenMode;
            ApplyOpacity();
            _float.ClickThrough = _cfg.ClickThrough;
            _miClickThrough.Checked = _cfg.ClickThrough;
            Opts.ShowRemaining = _cfg.ShowRemaining;
            _miRemaining.Checked = _cfg.ShowRemaining;
            _miShow.Checked = _cfg.ShowFloating;

            ApplyHotkey(true);
            try { _miAutostart.Checked = Autostart.IsEnabled; } catch { }
            _cfg.Save();

            if (!_cfg.AdminEnabled) _org = null;
            MaybeFetchOrg(true);
            MaybeFetchGoogleRate(true);
            MaybeFetchOfficialPricing(true);
            MaybeFetchAntigravityRate(true);
            MaybeCheckOllama(true);
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
            if (_codexWatcher != null) { try { _codexWatcher.Dispose(); } catch { } _codexWatcher = null; }
            if (_gemCliWatcher != null) { try { _gemCliWatcher.Dispose(); } catch { } _gemCliWatcher = null; }
            if (_hooksWatcher != null) { try { _hooksWatcher.Dispose(); } catch { } _hooksWatcher = null; }

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
                            PostUi(delegate { if (!_debounce.Enabled) _debounce.Start(); });   // paling lambat 2 dtk sekali
                        };
                        _logWatcher.Changed += h; _logWatcher.Created += h;
                        _logWatcher.Renamed += delegate(object s, RenamedEventArgs e) { h(s, e); };
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
                        PostUi(delegate { ReloadLimits(); });
                    else if (string.Equals(e.Name, "notify.json", StringComparison.OrdinalIgnoreCase))
                        PostUi(delegate { HandleNotify(); });
                    else if (string.Equals(e.Name, "command.json", StringComparison.OrdinalIgnoreCase))
                        PostUi(delegate { HandleExternalCommand(); });
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
                            PostUi(delegate { if (!_debounce.Enabled) _debounce.Start(); });
                        };
                        _geminiWatcher.Changed += hG; _geminiWatcher.Created += hG;
                        _geminiWatcher.Renamed += delegate(object s, RenamedEventArgs e) { hG(s, e); };
                        _geminiWatcher.EnableRaisingEvents = true;
                    }
                }
                catch (Exception ex) { Log.Write("watch.gemini", ex); }
            }
            if (_cfg.ScanCodex)
            {
                try
                {
                    if (Directory.Exists(Paths.CodexSessionsDir))
                    {
                        _codexWatcher = new FileSystemWatcher(Paths.CodexSessionsDir, "*.jsonl");
                        _codexWatcher.IncludeSubdirectories = true;
                        _codexWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName;
                        _codexWatcher.InternalBufferSize = 64 * 1024;
                        FileSystemEventHandler hC = delegate(object s, FileSystemEventArgs e)
                        {
                            lock (_dirty) _dirty.Add(e.FullPath);
                            PostUi(delegate { if (!_debounce.Enabled) _debounce.Start(); });
                        };
                        _codexWatcher.Changed += hC; _codexWatcher.Created += hC;
                        _codexWatcher.Renamed += delegate(object s, RenamedEventArgs e) { hC(s, e); };
                        _codexWatcher.EnableRaisingEvents = true;
                    }
                }
                catch (Exception ex) { Log.Write("watch.codex", ex); }
            }
            if (_cfg.ScanGeminiCli)
            {
                try
                {
                    if (Directory.Exists(Paths.GeminiCliTmpDir))
                    {
                        _gemCliWatcher = new FileSystemWatcher(Paths.GeminiCliTmpDir, "session-*.json");
                        _gemCliWatcher.IncludeSubdirectories = true;
                        _gemCliWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName;
                        _gemCliWatcher.InternalBufferSize = 64 * 1024;
                        FileSystemEventHandler hGc = delegate(object s, FileSystemEventArgs e)
                        {
                            lock (_dirty) _dirty.Add(e.FullPath);
                            PostUi(delegate { if (!_debounce.Enabled) _debounce.Start(); });
                        };
                        _gemCliWatcher.Changed += hGc; _gemCliWatcher.Created += hGc;
                        _gemCliWatcher.Renamed += delegate(object s, RenamedEventArgs e) { hGc(s, e); };
                        _gemCliWatcher.EnableRaisingEvents = true;
                    }
                }
                catch (Exception ex) { Log.Write("watch.gemcli", ex); }
            }
            try
            {
                Directory.CreateDirectory(Paths.HooksDir);
                _hooksWatcher = new FileSystemWatcher(Paths.HooksDir, "*.txt");
                _hooksWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName;
                FileSystemEventHandler hHooks = delegate { PostUi(delegate { SyncHookSessionsAndRefresh(); }); };
                _hooksWatcher.Created += hHooks; _hooksWatcher.Changed += hHooks; _hooksWatcher.Deleted += hHooks;
                _hooksWatcher.Renamed += delegate(object s, RenamedEventArgs e) { PostUi(delegate { SyncHookSessionsAndRefresh(); }); };
                _hooksWatcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) { Log.Write("watch.hooks", ex); }
        }

        private void HandleExternalCommand()
        {
            try
            {
                if (!File.Exists(Paths.CommandFile)) return;
                string raw = null;
                for (int i = 0; i < 3; i++)
                {
                    try { raw = File.ReadAllText(Paths.CommandFile); break; }
                    catch { Thread.Sleep(40); }
                }
                try { File.Delete(Paths.CommandFile); } catch { }
                string cmd = CommandParser.ParseCommandName(raw);
                if (string.IsNullOrEmpty(cmd)) return;
                if (cmd == "refresh")
                {
                    ReloadLimits(); RequestScan(); MaybeFetchOrg(true); MaybeCheckOllama(true);
                }
                else if (cmd == "open_explorer") OpenProjectExplorer();
                else if (cmd == "toggle_compact") SetCompact(!_float.Compact);
                else if (cmd == "toggle_zen") SetZenMode(!_cfg.ZenMode);
                else if (cmd == "copy_status") CopyStatus();
                else if (cmd == "copy_compact") CopyCompactCommand();
                else if (cmd == "open_terminal") OpenProjectTerminal();
                else if (cmd == "open_settings") OpenSettings();
                else if (cmd == "ack_sessions") AcknowledgeDoneSessions();
            }
            catch (Exception ex) { Log.Write("ipc.cmd", ex); }
        }

        private void MaybeSendDigest()
        {
            DateTime now = DateTime.Now;
            if (!DigestScheduler.ShouldSend(_cfg, now)) return;
            _cfg.LastDigestKey = DigestScheduler.ComputePeriodKey(_cfg.DigestSchedule, now);
            _cfg.Save();
            string title = string.Equals(_cfg.DigestSchedule, "weekly", StringComparison.OrdinalIgnoreCase)
                ? "Weekly AI Usage Digest"
                : "Daily AI Usage Digest";
            string body = DigestScheduler.BuildDigestText(_limits, _usage, _history, _cfg);
            WebhookSender.Send(_cfg.GetWebhookUrl(), title, body);
            Log.Write("digest.sent " + _cfg.LastDigestKey);
        }

        private DateTime _lastCustomPricingWarnedAt = DateTime.MinValue;

        private void CheckCustomPricingWarning()
        {
            if (_pricing == null || string.IsNullOrEmpty(_pricing.LastCustomError)) return;
            if (_pricing.LoadedCustomFileTime == _lastCustomPricingWarnedAt) return;
            _lastCustomPricingWarnedAt = _pricing.LoadedCustomFileTime;
            ShowBalloon(L.T("msg.pricing.invalid", _pricing.LastCustomError), ToolTipIcon.Warning);
        }

        private void OnTick()
        {
            try
            {
                DateTime mainTime = File.Exists(Paths.PricingFile) ? File.GetLastWriteTimeUtc(Paths.PricingFile) : DateTime.MinValue;
                DateTime customTime = File.Exists(Paths.CustomPricingFile) ? File.GetLastWriteTimeUtc(Paths.CustomPricingFile) : DateTime.MinValue;
                if (mainTime != _pricing.LoadedFileTime || customTime != _pricing.LoadedCustomFileTime)
                {
                    _pricing = Pricing.Load();
                    CheckCustomPricingWarning();
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
            MaybeCheckOllama(false);
            MaybeSendDigest();
            if (_cfg.AutoUpdateCheck && (DateTime.Now - _lastUpdateCheck).TotalHours >= 24)
            {
                CheckForUpdates(false);
            }
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
                PostUi(delegate
                {
                    _probeBusy = false;
                    _probeStatus = err == null ? null : L.T("probe.failed", err);
                    _probeFailed = err;
                    if (err != null) Log.Write("probe: " + err);
                    ReloadLimits();
                });
            });
        }

        private void ReloadLimits()
        {
            _limits = RateLimitReader.Read();
            SyncUsageDerivedLimits(_usage);
            Refresh();
            RunAlerts();
        }

        private void SyncUsageDerivedLimits(UsageSnapshot snap)
        {
            if (snap == null) return;
            if (!_limits.AntigravityFiveHour.Available && snap.AntigravityFiveHour != null && snap.AntigravityFiveHour.Available)
            {
                _limits.AntigravityFiveHour = snap.AntigravityFiveHour;
                _limits.AntigravitySevenDay = snap.AntigravitySevenDay;
            }
            if (_cfg.ScanCodex)
            {
                if (!_limits.CodexFiveHour.Available && snap.CodexFiveHour != null && snap.CodexFiveHour.Available)
                    _limits.CodexFiveHour = snap.CodexFiveHour;
                if (!_limits.CodexSevenDay.Available && snap.CodexSevenDay != null && snap.CodexSevenDay.Available)
                    _limits.CodexSevenDay = snap.CodexSevenDay;
            }
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
                    PostUi(delegate { ReloadLimits(); });
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
                        _scanner.SyncHookSessions(NotifyCli.GetActiveHookSessions());
                        if (doFull) _scanner.Scan();
                        else if (dirty.Count > 0) _scanner.ScanFiles(dirty);
                    }
                    catch (Exception ex) { Log.Write("scan", ex); }
                    UsageSnapshot snap = _scanner.Snapshot();
                    if (snap.Ready) _history.Merge(snap.Days);   // hanya setelah pindai lengkap (bukan saat hitung ulang)
                    PostUi(delegate
                    {
                        _usage = snap;
                        SyncUsageDerivedLimits(snap);
                        Refresh();
                        MaybeProbe(false);
                        RunBudget();
                        CheckBurnRate();
                        CheckContextAlert();
                        CheckCacheEfficiencyAlert();
                        CheckConfirmNotifications(snap);
                        RunAlerts();
                    });
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
            if (_disposed || !_cfg.AdminEnabled || _orgBusy) return;
            if (!force && DateTime.Now < _orgNext) return;
            if (force && _org != null && (DateTime.Now - _org.FetchedAt).TotalMinutes < 5) return;   // jangan membanjiri API
            _orgBusy = true;
            try
            {
                string key = _cfg.GetAdminKey();
                _org = await AdminApi.FetchMonthCost(key, _cfg.AdminApiKeyIds, _cfg.AdminWorkspaceIds);
                if (_disposed) return;
                if (!_org.Ok) Log.Write("adminapi: " + _org.Error);
            }
            catch (Exception ex) { Log.Write("adminapi", ex); }
            finally { _orgBusy = false; _orgNext = DateTime.Now.AddHours(1); }
            if (!_disposed) Refresh();
        }

        private async void MaybeFetchGoogleRate(bool force)
        {
            if (_disposed || !_cfg.CurrencyCustomEnabled || !GoogleRateFetcher.ShouldRefresh(force)) return;
            try
            {
                string cur = GoogleRateFetcher.NormalizeCurrencyCode(_cfg.CurrencySymbol);
                double rate = await GoogleRateFetcher.FetchRateAsync(cur);
                if (_disposed) return;
                double minValid = cur == "IDR" ? 5000.0 : 0.01;
                if (rate > minValid)
                {
                    _cfg.CurrencyRate = Math.Round(rate, cur == "IDR" ? 2 : 4);
                    Opts.CurrencyRate = _cfg.CurrencyRate;
                    _cfg.Save();
                    Refresh();
                }
            }
            catch (Exception ex) { Log.Write("rate.google", ex); }
        }

        private async void MaybeFetchOfficialPricing(bool force)
        {
            if (_disposed) return;
            if (force || OfficialPricingFetcher.ShouldAutoRefresh())
            {
                try
                {
                    int count = await OfficialPricingFetcher.FetchAndSaveAsync();
                    if (_disposed) return;
                    if (count > 0)
                    {
                        _pricing = Pricing.Load();
                        _scanner.ResetWithPricing(_pricing);
                        RequestScan();
                    }
                }
                catch (Exception ex) { Log.Write("pricing.official", ex); }
            }
        }

        private DateTime _lastOllamaCheck = DateTime.MinValue;
        private async void MaybeCheckOllama(bool force)
        {
            if (_disposed) return;
            if (!_cfg.OllamaEnabled)
            {
                if (OllamaProbe.Current != null && OllamaProbe.Current.Online)
                {
                    OllamaProbe.Current = new OllamaStatus();
                    Refresh();
                }
                return;
            }
            if (!force && (DateTime.Now - _lastOllamaCheck).TotalMinutes < 5) return;
            _lastOllamaCheck = DateTime.Now;
            OllamaStatus prev = OllamaProbe.Current;
            bool wasOnline = prev != null && prev.Online;
            int wasCount = prev != null ? prev.ModelCount : 0;
            int wasLoaded = prev != null ? prev.LoadedCount : 0;
            long wasVram = prev != null ? prev.TotalVramBytes : 0;
            try
            {
                OllamaStatus cur = await OllamaProbe.CheckAsync(_cfg.OllamaPort);
                if (_disposed) return;
                if (cur != null && (force || cur.Online != wasOnline || cur.ModelCount != wasCount ||
                                    cur.LoadedCount != wasLoaded || cur.TotalVramBytes != wasVram))
                    Refresh();
            }
            catch { }
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
            vm.ScanCodex = _cfg.ScanCodex;
            vm.ScanGeminiCli = _cfg.ScanGeminiCli;
            vm.ScanOpenCode = _cfg.ScanOpenCode;
            vm.ScanCline = _cfg.ScanCline;
            vm.ScanCopilot = _cfg.ScanCopilot;
            vm.Ollama = OllamaProbe.Current;
            vm.ProbeStatus = _probeStatus;
            vm.History = _history.Series(_cfg.HistoryDays);
            vm.BudgetDaily = _cfg.BudgetDaily; vm.BudgetMonthly = _cfg.BudgetMonthly;
            vm.MonthCost = _history.MonthToDateCost(DateTime.Now);
            vm.ShowProjects = _cfg.ShowProjects;
            return vm;
        }

        private void Refresh()
        {
            if (_disposed) return;
            ViewModel vm = BuildVm();
            _float.SetData(vm);
            if (_popup != null && !_popup.IsDisposed && _popup.Visible) { _popup.SetData(vm); PositionPopup(); }
            UpdateTrayIcon();
            StatusExporter.Export(_usage, _limits, vm);
        }

        private readonly HashSet<string> _notifiedConfirmIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _confirmBaselineReady;

        private void CheckConfirmNotifications(UsageSnapshot snap)
        {
            if (snap == null || snap.Sessions == null || snap.Sessions.Items == null) return;
            HashSet<string> currentConfirm = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            SessionActivity newlyConfirmed = null;
            foreach (SessionActivity sa in snap.Sessions.Items)
            {
                if (sa == null || sa.State != "confirm" || string.IsNullOrEmpty(sa.Id)) continue;
                currentConfirm.Add(sa.Id);
                if (_confirmBaselineReady && !_notifiedConfirmIds.Contains(sa.Id) && newlyConfirmed == null)
                    newlyConfirmed = sa;
            }
            _notifiedConfirmIds.Clear();
            foreach (string id in currentConfirm) _notifiedConfirmIds.Add(id);
            if (!_confirmBaselineReady)
            {
                _confirmBaselineReady = true;
                return;
            }
            if (newlyConfirmed == null || !_cfg.HookNotifyInput) return;
            if (_lastNotifyKind == "input" && (DateTime.Now - _lastNotifyAt).TotalSeconds < 15) return;

            _lastNotifyAt = DateTime.Now;
            _lastNotifyKind = "input";
            string proj = string.IsNullOrEmpty(newlyConfirmed.Project) ? newlyConfirmed.Provider : newlyConfirmed.Project;
            string text = L.T("notify.confirm.sess", newlyConfirmed.Provider, proj);
            Log.Write("notify confirm " + newlyConfirmed.Provider + " " + proj);
            ShowBalloon(text, ToolTipIcon.Info, proj, newlyConfirmed.Cwd, newlyConfirmed.Provider);
            bool quiet = IsQuietNow();
            if (_cfg.AudioAlertsEnabled && !quiet) AudioAlert.PlayPrompt();
            if (_cfg.WebhookEnabled && !quiet) WebhookSender.Send(_cfg.GetWebhookUrl(), "AI Confirmation Required", text);
        }

        // ---------------- ikon tray dinamis ----------------
        private void UpdateTrayIcon()
        {
            // Satu angka besar: limit 5 jam, atau limit mingguan (cincin putih) bila mingguan sudah penuh.
            string txt; Color badge; bool weekly;
            TrayText.IconSpec(_limits, _cfg.ScanClaude, _cfg.ScanGemini, _cfg.ScanCodex, out txt, out badge, out weekly);
            bool hasConfirm = _usage != null && _usage.Sessions != null && _usage.Sessions.ConfirmCount > 0;

            Size sz = SystemInformation.SmallIconSize;
            using (Bitmap bmp = new Bitmap(sz.Width, sz.Height))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                    TrayIconPainter.Draw(g, Math.Min(sz.Width, sz.Height), txt, badge, weekly, hasConfirm);
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

        private string Tooltip() { return TrayText.Tooltip(_limits, _usage, _probeFailed, LongTipOk ? 127 : 63, _cfg.ScanClaude, _cfg.ScanGemini, _cfg.ScanCodex); }

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

        // ---------------- auto updater ----------------
        private ReleaseInfo _pendingUpdate;
        private DateTime _lastUpdateCheck = DateTime.MinValue;
        private bool _updateCheckBusy;
        private bool _updateApplying;
        private bool _lastBalloonIsUpdate;

        private void CheckForUpdates(bool manual)
        {
            if (_updateCheckBusy || _updateApplying) return;
            _updateCheckBusy = true;
            _lastUpdateCheck = DateTime.Now;
            if (manual)
            {
                _lastBalloonIsUpdate = false;
                _tray.ShowBalloonTip(3000, L.T("update.title"), L.T("update.checking"), ToolTipIcon.Info);
            }

            AutoUpdater.CheckForUpdatesAsync(delegate(ReleaseInfo rel)
            {
                PostUi(delegate
                {
                    _updateCheckBusy = false;
                    if (_disposed) return;
                    HandleUpdateResult(rel, manual);
                });
            });
        }

        private void HandleUpdateResult(ReleaseInfo rel, bool manual)
        {
            if (rel == null)
            {
                if (manual)
                {
                    MessageBox.Show(
                        L.T("update.failed", "Unable to connect to GitHub / periksa koneksi internet."),
                        L.T("update.title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return;
            }

            if (!rel.IsNewer)
            {
                if (manual)
                {
                    MessageBox.Show(
                        L.T("update.uptodate", AutoUpdater.CurrentVersionStr),
                        L.T("update.title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return;
            }

            _pendingUpdate = rel;

            if (!manual)
            {
                _lastBalloonIsUpdate = true;
                _tray.ShowBalloonTip(10000, L.T("update.title"), L.T("update.balloon", rel.TagName), ToolTipIcon.Info);
                return;
            }

            string body = string.IsNullOrWhiteSpace(rel.Body) ? "-" : (rel.Body.Length > 500 ? rel.Body.Substring(0, 500) + "..." : rel.Body);
            string msg = L.T("update.available",
                rel.TagName ?? rel.Version.ToString(),
                AutoUpdater.CurrentVersionStr,
                body);

            DialogResult dr = MessageBox.Show(msg, L.T("update.title"), MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (dr == DialogResult.Yes)
            {
                PerformUpdate(rel);
            }
        }

        private void PerformUpdate(ReleaseInfo rel)
        {
            if (_updateApplying) return;
            _updateApplying = true;
            _lastBalloonIsUpdate = false;
            _tray.ShowBalloonTip(5000, L.T("update.title"), L.T("update.downloading", rel.AssetName ?? "update"), ToolTipIcon.Info);
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    bool ok = AutoUpdater.DownloadAndApplyUpdate(rel, null, delegate(int pct, string status)
                    {
                    });
                    if (ok)
                    {
                        PostUi(delegate { ExitThread(); });
                        return;
                    }
                    _updateApplying = false;
                }
                catch (Exception ex)
                {
                    _updateApplying = false;
                    Log.Write("update.apply", ex);
                    PostUi(delegate
                    {
                        if (_disposed) return;
                        DialogResult dr = MessageBox.Show(
                            L.T("update.failed", ex.Message),
                            L.T("update.title"), MessageBoxButtons.YesNo, MessageBoxIcon.Error);
                        if (dr == DialogResult.Yes && AutoUpdater.IsTrustedUpdateUrl(rel.HtmlUrl))
                        {
                            try { Process.Start(new ProcessStartInfo(rel.HtmlUrl) { UseShellExecute = true }); } catch { }
                        }
                    });
                }
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ExitThreadCore();
            }
            base.Dispose(disposing);
        }

        protected override void ExitThreadCore()
        {
            if (_exiting) return;
            _exiting = true;
            try
            {
                _disposed = true;
                if (_float != null && !_float.IsDisposed && _float.Visible) SavePosition();
                _tick.Stop(); _tick.Dispose();
                _debounce.Stop(); _debounce.Dispose();
                _fsTimer.Stop(); _fsTimer.Dispose();
                if (_updateTimer != null) { _updateTimer.Stop(); _updateTimer.Dispose(); _updateTimer = null; }
                if (_prewarmTimer != null) { _prewarmTimer.Stop(); _prewarmTimer.Dispose(); _prewarmTimer = null; }
                if (_settingsForm != null && !_settingsForm.IsDisposed) { _settingsForm.Dispose(); _settingsForm = null; }
                if (_popup != null && !_popup.IsDisposed) { _popup.Dispose(); _popup = null; }
                if (_hotkey != null) _hotkey.Dispose();
                if (_logWatcher != null) _logWatcher.Dispose();
                if (_appWatcher != null) _appWatcher.Dispose();
                if (_geminiWatcher != null) _geminiWatcher.Dispose();
                if (_codexWatcher != null) _codexWatcher.Dispose();
                if (_gemCliWatcher != null) _gemCliWatcher.Dispose();
                if (_hooksWatcher != null) _hooksWatcher.Dispose();
                if (_sessionJumpMenu != null) { _sessionJumpMenu.Dispose(); _sessionJumpMenu = null; }
                _tray.Visible = false;
                _tray.Dispose();
                if (_menu != null) _menu.Dispose();
                if (_icon != null) _icon.Dispose();
                if (_iconHandle != IntPtr.Zero) { DestroyIcon(_iconHandle); _iconHandle = IntPtr.Zero; }
                if (_float != null && !_float.IsDisposed) _float.Close();
            }
            catch { }
            base.ExitThreadCore();
        }
    }
}
