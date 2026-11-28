using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ClaudeUsageWidget
{
    /// <summary>Aksi yang disediakan TrayApp untuk tombol di Pengaturan.</summary>
    internal class SettingsCallbacks
    {
        public Action ExportCsv;
        public Func<string> Diagnostics;
        public Func<bool> IsOpenCodeSqliteMissing;
        public Action Apply;
        public Action CheckUpdates;
        public Action PricingUpdated;
    }

    /// <summary>
    /// Dialog Pengaturan bertab dengan tema mengikuti widget (gelap/terang). Tab dibuat sendiri (bukan TabControl)
    /// agar seluruh permukaan, termasuk tepi dan strip tab, bisa mengikuti palet.
    /// </summary>
    internal class SettingsForm : Form
    {
        private static readonly string[] Edges = { "top", "bottom", "left", "right", "top-left", "top-right", "bottom-left", "bottom-right" };
        private static readonly string[] Backdrops = { "solid", "acrylic", "mica", "mica-alt" };

        private readonly AppConfig _cfg;
        private readonly SettingsCallbacks _cb;
        private readonly TrackBar _opacity = new TrackBar();
        private readonly Label _opacityLbl = new Label();
        private readonly CheckBox _showFloat = new CheckBox(), _compact = new CheckBox(), _click = new CheckBox(), _auto = new CheckBox();
        private readonly CheckBox _autoUpdate = new CheckBox();
        private readonly CheckBox _admin = new CheckBox(), _probe = new CheckBox();
        private readonly NumericUpDown _probeMin = new NumericUpDown();
        private readonly CheckBox _mirror = new CheckBox(), _dock = new CheckBox(), _onTop = new CheckBox(), _hideFs = new CheckBox();
        private readonly ComboBox _theme = new ComboBox(), _lang = new ComboBox();
        private readonly ComboBox _histDays = new ComboBox(), _edge = new ComboBox(), _monitor = new ComboBox(), _backdrop = new ComboBox();
        private readonly List<string> _langCodes = new List<string>();
        private readonly List<string> _monitorNames = new List<string>();
        private readonly CheckBox _remaining = new CheckBox(), _hotkeyOn = new CheckBox(), _alertsOn = new CheckBox(), _resetOn = new CheckBox(), _wsl = new CheckBox();
        private readonly CheckBox _projects = new CheckBox(), _stopOn = new CheckBox(), _inputOn = new CheckBox();
        private readonly NumericUpDown _budgetDay = new NumericUpDown(), _budgetMonth = new NumericUpDown(), _stopSecs = new NumericUpDown();
        private readonly Label _hookState = new Label();
        private readonly TextBox _hotkeyText = new TextBox(), _alertTh = new TextBox(), _extraDirs = new TextBox();
        private readonly TextBox _mirrorPath = new TextBox();
        private readonly TextBox _key = new TextBox(), _keyIds = new TextBox(), _wsIds = new TextBox();
        private readonly Label _keyState = new Label();
        private bool _clearKey;

        // Kontrol untuk fitur baru
        private readonly CheckBox _currencyOn = new CheckBox(), _currencyBoth = new CheckBox();
        private readonly TextBox _currencySymbol = new TextBox();
        private readonly NumericUpDown _currencyRate = new NumericUpDown();
        private readonly CheckBox _scanClaude = new CheckBox(), _scanGemini = new CheckBox(), _scanCodex = new CheckBox(), _adminData = new CheckBox();
        private readonly CheckBox _scanGeminiCli = new CheckBox(), _scanOpenCode = new CheckBox();
        private readonly CheckBox _scanCline = new CheckBox(), _scanCopilot = new CheckBox();
        private readonly TextBox _customJsonlDirs = new TextBox();
        private readonly CheckBox _ollamaOn = new CheckBox();
        private readonly NumericUpDown _ollamaPort = new NumericUpDown();
        private readonly CheckBox _audioOn = new CheckBox(), _contextOn = new CheckBox();
        private readonly TextBox _wavPath = new TextBox();
        private readonly CheckBox _burnRateOn = new CheckBox(), _cacheDropOn = new CheckBox();
        private readonly NumericUpDown _burnRateTokens = new NumericUpDown(), _cooldownMins = new NumericUpDown(), _cacheDropPct = new NumericUpDown();
        private readonly CheckBox _quietOn = new CheckBox(), _digestOn = new CheckBox(), _contextBar = new CheckBox(), _zenMode = new CheckBox();
        private readonly NumericUpDown _quietStart = new NumericUpDown(), _quietEnd = new NumericUpDown(), _digestHour = new NumericUpDown();
        private readonly ComboBox _cardLayout = new ComboBox(), _pillMode = new ComboBox(), _digestSchedule = new ComboBox(), _scale = new ComboBox(), _probeModel = new ComboBox(), _ctxWin = new ComboBox();
        private readonly CheckBox _webhookOn = new CheckBox();
        private readonly TextBox _webhookUrl = new TextBox();
        private readonly Button _webhookTestBtn = new Button();

        private readonly FlowLayoutPanel _tabStrip = new FlowLayoutPanel();
        private readonly Panel _content = new Panel();
        private readonly List<Button> _tabBtns = new List<Button>();
        private readonly List<Panel> _pages = new List<Panel>();
        private readonly List<TableLayoutPanel> _tabTables = new List<TableLayoutPanel>();
        private readonly bool[] _tabBuilt = new bool[6];
        private int _tab;
        private Button _saveBtn, _cancelBtn, _applyBtn;
        private readonly List<Action> _i18n = new List<Action>();
        private Dictionary<string, string> _i18nRev;
        private string _i18nRevLang;
        private Font _baseFont;
        private string _builtLang = L.Lang;
        private bool _appliedLight = Palette.IsLight;
        private bool _i18nCaptured;

        public SettingsForm(AppConfig cfg) : this(cfg, null) { }

        public SettingsForm(AppConfig cfg, SettingsCallbacks callbacks)
        {
            _cfg = cfg;
            _cb = callbacks;
            _admin.Checked = _cfg.AdminEnabled;
            _adminData.Checked = _cfg.AdminEnabled;
            Text = L.T("set.title");
            try { Icon ic = AppIcon.Get(); if (ic != null) Icon = ic; } catch { }
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            Font = (_baseFont = new Font("Segoe UI", 9f));
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(700, Math.Min(560, Math.Max(380, wa.Height - 140)));

            SuspendLayout();
            _tabStrip.SuspendLayout();
            _content.SuspendLayout();
            try
            {
                // Urutan penambahan menentukan docking: isi (Fill) dulu, lalu bawah, lalu atas.
                _content.Dock = DockStyle.Fill;
                Controls.Add(_content);

                FlowLayoutPanel buttons = new FlowLayoutPanel();
                buttons.SuspendLayout();
                buttons.FlowDirection = FlowDirection.RightToLeft; buttons.AutoSize = true; buttons.Dock = DockStyle.Bottom;
                buttons.Padding = new Padding(12, 8, 12, 10);
                _cancelBtn = new Button(); _cancelBtn.Text = L.T("set.cancel"); _cancelBtn.DialogResult = DialogResult.Cancel; _cancelBtn.AutoSize = true;
                _applyBtn = new Button(); _applyBtn.Text = L.T("set.apply"); _applyBtn.AutoSize = true; _applyBtn.Click += OnApply;
                _saveBtn = new Button(); _saveBtn.Text = L.T("set.save"); _saveBtn.AutoSize = true; _saveBtn.Click += OnSave;
                buttons.Controls.Add(_cancelBtn);
                buttons.Controls.Add(_applyBtn);
                buttons.Controls.Add(_saveBtn);
                buttons.ResumeLayout(false);
                Controls.Add(buttons);

                _tabStrip.Dock = DockStyle.Top; _tabStrip.Height = 40; _tabStrip.WrapContents = false;
                _tabStrip.Padding = new Padding(10, 4, 10, 0);
                _tabStrip.Paint += delegate(object s, PaintEventArgs e)
                {
                    using (Pen p = new Pen(Palette.Border)) e.Graphics.DrawLine(p, 0, _tabStrip.Height - 1, _tabStrip.Width, _tabStrip.Height - 1);
                };
                Controls.Add(_tabStrip);

                AddPage(L.T("tab.display"));
                AddPage(L.T("tab.behavior"));
                AddPage(L.T("tab.alerts"));
                AddPage(L.T("tab.data"));
                AddPage(L.T("tab.probe"));
                AddPage(L.T("tab.admin"));

                AcceptButton = _saveBtn; CancelButton = _cancelBtn;
                EnsureTabBuilt(0);
                ApplyTheme();
                SelectTab(0);
            }
            finally
            {
                _content.ResumeLayout(false);
                _tabStrip.ResumeLayout(false);
                ResumeLayout(false);
            }
        }

        // ---------------- tab ----------------
        private Font _tabFont, _headFont;

        private TableLayoutPanel AddPage(string title)
        {
            Panel page = new Panel();
            page.SuspendLayout();
            page.Dock = DockStyle.Fill; page.AutoScroll = true; page.Padding = new Padding(16, 12, 16, 8); page.Visible = false;
            page.HandleCreated += delegate { DarkUi.ControlTheme(page.Handle, !Palette.IsLight); };   // scrollbar mengikuti tema
            TableLayoutPanel t = new TableLayoutPanel();
            t.ColumnCount = 2; t.AutoSize = true; t.AutoSizeMode = AutoSizeMode.GrowAndShrink; t.Dock = DockStyle.Top;
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
            page.Controls.Add(t);
            page.ResumeLayout(false);
            _content.Controls.Add(page);
            _pages.Add(page);
            _tabTables.Add(t);

            Button b = new Button();
            if (_tabFont == null) _tabFont = new Font("Segoe UI", 9.5f);
            b.Text = title; b.AutoSize = true; b.Margin = new Padding(0, 0, 2, 0); b.Padding = new Padding(8, 3, 8, 3);
            b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0; b.Font = _tabFont;
            int index = _tabBtns.Count;
            b.Click += delegate { SelectTab(index); };
            b.Paint += delegate(object s, PaintEventArgs e)
            {
                if (_tabBtns.IndexOf((Button)s) != _tab) return;
                using (SolidBrush br = new SolidBrush(Palette.Accent)) e.Graphics.FillRectangle(br, 6, ((Button)s).Height - 3, ((Button)s).Width - 12, 3);
            };
            _tabBtns.Add(b);
            _tabStrip.Controls.Add(b);
            return t;
        }

        private void EnsureTabBuilt(int i)
        {
            if (i < 0 || i >= _tabTables.Count || _tabBuilt[i]) return;
            _tabBuilt[i] = true;
            Panel page = _pages[i];
            TableLayoutPanel t = _tabTables[i];
            page.SuspendLayout();
            t.SuspendLayout();
            try
            {
                switch (i)
                {
                    case 0: BuildDisplay(t); break;
                    case 1: BuildBehavior(t); break;
                    case 2: BuildAlerts(t); break;
                    case 3: BuildData(t); break;
                    case 4: BuildProbe(t); break;
                    case 5: BuildAdmin(t); break;
                }
                if (i > 0) ThemeControls(page);
                if (_i18nCaptured)
                    CaptureControlI18n(page, GetI18nRev());
            }
            finally
            {
                t.ResumeLayout(false);
                page.ResumeLayout(true);
            }
        }

        private void SelectTab(int i)
        {
            EnsureTabBuilt(i);
            _tab = i;
            for (int k = 0; k < _pages.Count; k++)
            {
                _pages[k].Visible = k == i;
                _tabBtns[k].ForeColor = k == i ? Palette.Text : Palette.Dim;
                _tabBtns[k].Invalidate();
            }
        }

        // ---------------- isi tab ----------------
        private void BuildDisplay(TableLayoutPanel t)
        {
            _lang.DropDownStyle = ComboBoxStyle.DropDownList; _lang.Width = 160;
            _lang.Items.Add("English"); _langCodes.Add("en");               // nama asli bahasa, tidak diterjemahkan
            _lang.Items.Add("Bahasa Indonesia"); _langCodes.Add("id");
            foreach (LangPackInfo p in LangPacks.Discover()) { _lang.Items.Add(p.Name); _langCodes.Add(p.Code); }   // paket bahasa JSON
            int li = _langCodes.IndexOf(_cfg.Language);
            _lang.SelectedIndex = li >= 0 ? li : 0;
            AddRow(t, L.T("set.language"), _lang);

            Button exp = new Button(); exp.Text = L.T("set.lang.export"); exp.AutoSize = true;
            exp.Click += delegate { ExportTemplate(); };
            Button folder = new Button(); folder.Text = L.T("set.lang.folder"); folder.AutoSize = true;
            folder.Click += delegate
            {
                try { System.IO.Directory.CreateDirectory(Paths.LangDirAppData); Process.Start(ProcessHelper.ExplorerExe, "\"" + Paths.LangDirAppData + "\""); } catch { }
            };
            FlowLayoutPanel lp = new FlowLayoutPanel(); lp.AutoSize = true; lp.WrapContents = false;
            lp.Controls.Add(exp); lp.Controls.Add(folder);
            t.Controls.Add(new Label()); t.Controls.Add(lp);

            _theme.DropDownStyle = ComboBoxStyle.DropDownList; _theme.Width = 160;
            _theme.Items.Add(L.T("set.theme.dark")); _theme.Items.Add(L.T("set.theme.light")); _theme.Items.Add(L.T("set.theme.system"));
            _theme.SelectedIndex = _cfg.Theme == "light" ? 1 : (_cfg.Theme == "system" ? 2 : 0);
            AddRow(t, L.T("set.theme"), _theme);

            _opacity.Minimum = 30; _opacity.Maximum = 100; _opacity.TickFrequency = 10; _opacity.Width = 250;
            _opacity.Value = Math.Max(30, Math.Min(100, _cfg.OpacityPercent));
            _opacity.ValueChanged += delegate { _opacityLbl.Text = L.T("set.opacity", _opacity.Value); };
            _opacityLbl.Text = L.T("set.opacity", _opacity.Value); _opacityLbl.AutoSize = true; _opacityLbl.Anchor = AnchorStyles.Left;
            t.Controls.Add(_opacityLbl); t.Controls.Add(_opacity);

            AddCheck(t, _remaining, L.T("set.remaining"), _cfg.ShowRemaining);

            _histDays.DropDownStyle = ComboBoxStyle.DropDownList; _histDays.Width = 160;
            _histDays.Items.Add(L.T("set.days14")); _histDays.Items.Add(L.T("set.days30"));
            _histDays.SelectedIndex = _cfg.HistoryDays == 30 ? 1 : 0;
            AddRow(t, L.T("set.histdays"), _histDays);

            AddCheck(t, _dock, L.T("set.dock"), _cfg.DockTop);
            _edge.DropDownStyle = ComboBoxStyle.DropDownList; _edge.Width = 180;
            _edge.Items.Add(L.T("edge.top"));
            _edge.Items.Add(L.T("edge.bottom"));
            _edge.Items.Add(L.T("edge.left"));
            _edge.Items.Add(L.T("edge.right"));
            _edge.Items.Add(L.T("edge.top_left"));
            _edge.Items.Add(L.T("edge.top_right"));
            _edge.Items.Add(L.T("edge.bottom_left"));
            _edge.Items.Add(L.T("edge.bottom_right"));
            int ei = Array.IndexOf(Edges, _cfg.DockEdge);
            _edge.SelectedIndex = ei >= 0 ? ei : 0;
            AddRow(t, L.T("set.dock.edge"), _edge);

            _monitor.DropDownStyle = ComboBoxStyle.DropDownList; _monitor.Width = 260;
            _monitor.Items.Add(L.T("monitor.primary")); _monitorNames.Add("");
            foreach (Screen s in Screen.AllScreens)
            {
                _monitor.Items.Add(L.T("monitor.item", s.DeviceName.TrimStart('\\', '.'), s.Bounds.Width, s.Bounds.Height));
                _monitorNames.Add(s.DeviceName);
            }
            int mi = string.IsNullOrEmpty(_cfg.DockMonitor) ? 0 : _monitorNames.IndexOf(_cfg.DockMonitor);
            _monitor.SelectedIndex = mi >= 0 ? mi : 0;
            AddRow(t, L.T("set.dock.monitor"), _monitor);

            _backdrop.DropDownStyle = ComboBoxStyle.DropDownList; _backdrop.Width = 200;
            _backdrop.Items.Add(L.T("backdrop.solid"));
            _backdrop.Items.Add(L.T("backdrop.acrylic"));
            _backdrop.Items.Add(L.T("backdrop.mica"));
            _backdrop.Items.Add(L.T("backdrop.mica_alt"));
            int bi = Array.IndexOf(Backdrops, _cfg.Backdrop);
            _backdrop.SelectedIndex = bi >= 0 ? bi : 0;
            AddRow(t, L.T("set.backdrop"), _backdrop);

            AddCheck(t, _compact, L.T("set.compact"), _cfg.Compact);
            AddCheck(t, _zenMode, L.T("set.zen"), _cfg.ZenMode);
            AddCheck(t, _click, L.T("set.click"), _cfg.ClickThrough);
            AddCheck(t, _contextBar, L.T("set.contextbar"), _cfg.ShowContextBar);

            _ctxWin.DropDownStyle = ComboBoxStyle.DropDownList; _ctxWin.Width = 220;
            _ctxWin.Items.Add(L.T("set.ctxwin.auto"));
            _ctxWin.Items.Add(L.T("set.ctxwin.200"));
            _ctxWin.Items.Add(L.T("set.ctxwin.400"));
            _ctxWin.Items.Add(L.T("set.ctxwin.1m"));
            _ctxWin.SelectedIndex = _cfg.ContextWindowOverride == 200000L ? 1 : (_cfg.ContextWindowOverride == 400000L ? 2 : (_cfg.ContextWindowOverride == 1000000L ? 3 : 0));
            AddRow(t, L.T("set.ctxwin"), _ctxWin);

            _cardLayout.DropDownStyle = ComboBoxStyle.DropDownList; _cardLayout.Width = 220;
            _cardLayout.Items.Add(L.T("set.cardlayout.auto"));
            _cardLayout.Items.Add(L.T("set.cardlayout.2col"));
            _cardLayout.Items.Add(L.T("set.cardlayout.3col"));
            _cardLayout.SelectedIndex = _cfg.CardLayout == "3col" ? 2 : (_cfg.CardLayout == "2col" ? 1 : 0);
            AddRow(t, L.T("set.cardlayout"), _cardLayout);

            _pillMode.DropDownStyle = ComboBoxStyle.DropDownList; _pillMode.Width = 220;
            _pillMode.Items.Add(L.T("set.pillmode.all"));
            _pillMode.Items.Add(L.T("set.pillmode.highest"));
            _pillMode.Items.Add(L.T("set.pillmode.cost_runway"));
            _pillMode.SelectedIndex = _cfg.MiniPillMode == "highest" ? 1 : (_cfg.MiniPillMode == "cost_runway" ? 2 : 0);
            AddRow(t, L.T("set.pillmode"), _pillMode);

            _scale.DropDownStyle = ComboBoxStyle.DropDownList; _scale.Width = 180;
            _scale.Items.Add(L.T("set.scale.85"));
            _scale.Items.Add(L.T("set.scale.100"));
            _scale.Items.Add(L.T("set.scale.115"));
            _scale.Items.Add(L.T("set.scale.130"));
            _scale.SelectedIndex = _cfg.WidgetScale < 0.92 ? 0 : (_cfg.WidgetScale < 1.08 ? 1 : (_cfg.WidgetScale < 1.22 ? 2 : 3));
            AddRow(t, L.T("set.scale"), _scale);

            AddNote(t, L.T("set.h.currency"), "head");
            AddCheck(t, _currencyOn, L.T("set.currency.enable"), _cfg.CurrencyCustomEnabled);
            _currencySymbol.Width = 80; _currencySymbol.Text = _cfg.CurrencySymbol ?? "Rp";
            _currencySymbol.TextChanged += delegate { ApplyCurrencyRateFormat(_currencySymbol.Text); };
            AddRow(t, L.T("set.currency.symbol"), _currencySymbol);
            _currencyRate.Minimum = 0.01m; _currencyRate.Maximum = 10000000m; _currencyRate.Width = 110;
            ApplyCurrencyRateFormat(_cfg.CurrencySymbol);
            _currencyRate.Value = ClampCurrencyRate(_cfg.CurrencyRate);

            Button fetchGoogle = new Button();
            fetchGoogle.Text = L.T("set.currency.google");
            fetchGoogle.AutoSize = true;
            fetchGoogle.Click += async delegate
            {
                fetchGoogle.Enabled = false;
                try
                {
                    string symInput = _currencySymbol.Text.Trim();
                    string curCode = GoogleRateFetcher.NormalizeCurrencyCode(symInput);
                    double r = await GoogleRateFetcher.FetchRateAsync(symInput);
                    if (IsDisposed) return;
                    double minValid = curCode == "IDR" ? 5000.0 : 0.01;
                    if (r > minValid)
                    {
                        ApplyCurrencyRateFormat(symInput);
                        _currencyRate.Value = Math.Max(_currencyRate.Minimum, Math.Min(_currencyRate.Maximum, (decimal)Math.Round(r, curCode == "IDR" ? 0 : 2)));
                        string dispSym = string.IsNullOrEmpty(symInput) ? curCode : symInput;
                        if (Visible)
                            MessageBox.Show(this, L.T("set.currency.google_ok", dispSym + " " + r.ToString(curCode == "IDR" ? "N0" : "N2", L.Culture)), L.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else if (Visible)
                    {
                        MessageBox.Show(this, L.T("set.currency.google_fail"), L.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
                finally
                {
                    if (!IsDisposed) fetchGoogle.Enabled = true;
                }
            };
            FlowLayoutPanel crp = new FlowLayoutPanel();
            crp.AutoSize = true; crp.WrapContents = false;
            crp.Controls.Add(_currencyRate); crp.Controls.Add(fetchGoogle);
            AddRow(t, L.T("set.currency.rate"), crp);
            AddCheck(t, _currencyBoth, L.T("set.currency.both"), _cfg.CurrencyShowBoth);
        }

        private void ExportTemplate()
        {
            try
            {
                System.IO.Directory.CreateDirectory(Paths.LangDirAppData);
                string path = System.IO.Path.Combine(Paths.LangDirAppData, "template.json");
                L.ExportTemplate(path);
                MessageBox.Show(this, L.T("msg.lang.exported", path), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { Log.Write("langtemplate", ex); MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private void BuildBehavior(TableLayoutPanel t)
        {
            AddCheck(t, _showFloat, L.T("set.showstart"), _cfg.ShowFloating);
            bool autoOn = false; try { autoOn = Autostart.IsEnabled; } catch { }
            AddCheck(t, _auto, L.T("set.auto"), autoOn);
            if (Paths.Portable)
            {
                _auto.Enabled = false;   // mode portabel tidak menulis registry
                AddNote(t, L.T("set.portable.note"), "dim");
            }
            AddCheck(t, _onTop, L.T("set.ontop"), _cfg.AlwaysOnTop);
            AddCheck(t, _hideFs, L.T("set.hidefs"), _cfg.HideOnFullscreen);
            AddCheck(t, _autoUpdate, L.T("set.autoupdate"), _cfg.AutoUpdateCheck);
            Button btnCheckUpdate = new Button();
            btnCheckUpdate.Text = L.T("set.checknow");
            btnCheckUpdate.AutoSize = true;
            btnCheckUpdate.Click += delegate
            {
                if (_cb != null && _cb.CheckUpdates != null)
                {
                    _cb.CheckUpdates();
                }
            };
            AddRow(t, L.T("tray.update"), btnCheckUpdate);
            AddCheck(t, _hotkeyOn, L.T("set.hotkey.enable"), _cfg.HotkeyEnabled);
            _hotkeyText.Width = 160; _hotkeyText.Text = _cfg.Hotkey ?? "Win+Alt+U";
            AddRow(t, L.T("set.hotkey.keys"), _hotkeyText);
        }

        private void BuildAlerts(TableLayoutPanel t)
        {
            AddCheck(t, _alertsOn, L.T("set.alerts.enable"), _cfg.AlertsEnabled);
            _alertTh.Width = 160; _alertTh.Text = _cfg.AlertThresholds ?? "80,95";
            AddRow(t, L.T("set.alerts.th"), _alertTh);
            AddCheck(t, _resetOn, L.T("set.alerts.reset"), _cfg.ResetNotify);
            AddCheck(t, _audioOn, L.T("set.audio.enable"), _cfg.AudioAlertsEnabled);
            _wavPath.Width = 140; _wavPath.Text = _cfg.CustomSoundPath ?? ""; _wavPath.Margin = new Padding(0, 2, 0, 0);
            Button wavBrowse = new Button { Text = L.T("set.browse"), AutoSize = true, Margin = new Padding(4, 0, 0, 0) };
            wavBrowse.Click += delegate
            {
                using (OpenFileDialog d = new OpenFileDialog())
                {
                    d.Filter = L.T("set.audio.wav.filter");
                    d.CheckFileExists = true;
                    if (d.ShowDialog(this) == DialogResult.OK) _wavPath.Text = d.FileName;
                }
            };
            Button wavClear = new Button { Text = L.T("set.audio.clear"), AutoSize = true, Margin = new Padding(4, 0, 0, 0) };
            wavClear.Click += delegate { _wavPath.Text = ""; };
            Button wavTest = new Button { Text = L.T("set.audio.test"), AutoSize = true, Margin = new Padding(4, 0, 0, 0) };
            wavTest.Click += delegate
            {
                AudioAlert.PlayFile(_wavPath.Text.Trim());
            };
            FlowLayoutPanel wavRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            wavRow.Controls.Add(_wavPath); wavRow.Controls.Add(wavBrowse); wavRow.Controls.Add(wavClear); wavRow.Controls.Add(wavTest);
            AddRow(t, L.T("set.audio.wav"), wavRow);

            AddCheck(t, _contextOn, L.T("set.context.enable"), _cfg.ContextAlertEnabled);
            AddCheck(t, _burnRateOn, L.T("set.burnrate.enable"), _cfg.BurnRateAlertEnabled);
            _burnRateTokens.Minimum = 1000; _burnRateTokens.Maximum = 10000000; _burnRateTokens.Increment = 10000; _burnRateTokens.Width = 110;
            _burnRateTokens.Value = Math.Max(1000, Math.Min(10000000, _cfg.BurnRateThresholdTokens <= 0 ? 100000 : _cfg.BurnRateThresholdTokens));
            AddRow(t, L.T("set.burnrate.th"), _burnRateTokens);
            AddCheck(t, _cacheDropOn, L.T("set.cachedrop.enable"), _cfg.CacheDropAlertEnabled);
            _cacheDropPct.Minimum = 5; _cacheDropPct.Maximum = 95; _cacheDropPct.Width = 80;
            _cacheDropPct.Value = Math.Max(5, Math.Min(95, _cfg.CacheDropMinPercent <= 0 ? 30 : _cfg.CacheDropMinPercent));
            AddRow(t, L.T("set.cachedrop.pct"), _cacheDropPct);
            _cooldownMins.Minimum = 1; _cooldownMins.Maximum = 1440; _cooldownMins.Width = 90;
            _cooldownMins.Value = Math.Max(1, Math.Min(1440, _cfg.AlertCooldownMinutes <= 0 ? 15 : _cfg.AlertCooldownMinutes));
            AddRow(t, L.T("set.cooldown"), _cooldownMins);
            AddNote(t, L.T("set.alerts.note"), "dim");

            AddNote(t, L.T("set.h.webhook"), "head");
            AddCheck(t, _webhookOn, L.T("set.webhook.enable"), _cfg.WebhookEnabled);
            _webhookUrl.Width = 200; _webhookUrl.Text = _cfg.GetWebhookUrl() ?? "";
            _webhookUrl.Margin = new Padding(0, 2, 0, 0);
            _webhookTestBtn.Text = L.T("set.webhook.test");
            _webhookTestBtn.AutoSize = true;
            _webhookTestBtn.Margin = new Padding(6, 0, 0, 0);
            _webhookTestBtn.Padding = new Padding(8, 2, 8, 2);
            _webhookTestBtn.Click += OnWebhookTestClick;
            FlowLayoutPanel webhookRow = new FlowLayoutPanel();
            webhookRow.FlowDirection = FlowDirection.LeftToRight;
            webhookRow.AutoSize = true;
            webhookRow.WrapContents = false;
            webhookRow.Margin = new Padding(0);
            webhookRow.Controls.Add(_webhookUrl);
            webhookRow.Controls.Add(_webhookTestBtn);
            AddRow(t, L.T("set.webhook.url"), webhookRow);

            AddNote(t, L.T("set.h.quiethours"), "head");
            AddCheck(t, _quietOn, L.T("set.quiet.enable"), _cfg.QuietHoursEnabled);
            _quietStart.Minimum = 0; _quietStart.Maximum = 23; _quietStart.Width = 70;
            _quietStart.Value = Math.Max(0, Math.Min(23, _cfg.QuietStartHour));
            AddRow(t, L.T("set.quiet.start"), _quietStart);
            _quietEnd.Minimum = 0; _quietEnd.Maximum = 23; _quietEnd.Width = 70;
            _quietEnd.Value = Math.Max(0, Math.Min(23, _cfg.QuietEndHour));
            AddRow(t, L.T("set.quiet.end"), _quietEnd);

            AddCheck(t, _digestOn, L.T("set.digest.enable"), _cfg.DigestEnabled);
            _digestSchedule.DropDownStyle = ComboBoxStyle.DropDownList; _digestSchedule.Width = 170;
            _digestSchedule.Items.Add(L.T("set.digest.daily"));
            _digestSchedule.Items.Add(L.T("set.digest.weekly"));
            _digestSchedule.SelectedIndex = _cfg.DigestSchedule == "weekly" ? 1 : 0;
            AddRow(t, L.T("set.digest.schedule"), _digestSchedule);
            _digestHour.Minimum = 0; _digestHour.Maximum = 23; _digestHour.Width = 70;
            _digestHour.Value = Math.Max(0, Math.Min(23, _cfg.DigestHour));
            AddRow(t, L.T("set.digest.hour"), _digestHour);

            SetupMoney(_budgetDay, _cfg.BudgetDaily);
            AddRow(t, L.T("set.budget.day"), _budgetDay);
            SetupMoney(_budgetMonth, _cfg.BudgetMonthly);
            AddRow(t, L.T("set.budget.month"), _budgetMonth);
            AddNote(t, L.T("set.budget.note"), "dim");

            // Notifikasi dari hook Claude Code (respons selesai / butuh input)
            AddCheck(t, _stopOn, L.T("set.hooks.stop"), _cfg.HookNotifyStop);
            _stopSecs.Minimum = 0; _stopSecs.Maximum = 3600; _stopSecs.Width = 90;
            _stopSecs.Value = Math.Max(0, Math.Min(3600, _cfg.HookNotifyMinSeconds));
            AddRow(t, L.T("set.hooks.secs"), _stopSecs);
            AddCheck(t, _inputOn, L.T("set.hooks.input"), _cfg.HookNotifyInput);
            _hookState.AutoSize = true; _hookState.Tag = "dim"; _hookState.Anchor = AnchorStyles.Left;
            Button inst = new Button(); inst.Text = L.T("set.hooks.install"); inst.AutoSize = true; inst.Click += delegate { OnInstallHooks(); };
            Button rem = new Button(); rem.Text = L.T("set.hooks.remove"); rem.AutoSize = true; rem.Click += delegate { OnRemoveHooks(); };
            FlowLayoutPanel hp = new FlowLayoutPanel(); hp.AutoSize = true; hp.WrapContents = false;
            hp.Controls.Add(inst); hp.Controls.Add(rem);
            RefreshHookState();
            t.Controls.Add(_hookState); t.Controls.Add(hp);
            AddNote(t, L.T("set.hooks.note"), "dim");
        }

        private static void SetupMoney(NumericUpDown n, double value)
        {
            n.DecimalPlaces = 2; n.Minimum = 0; n.Maximum = 1000000; n.Increment = 5; n.Width = 110;
            decimal v = (decimal)Math.Max(0, Math.Min(1000000, value));
            n.Value = v;
        }

        private void RefreshHookState()
        {
            _hookState.Text = HookInstaller.IsInstalled() ? L.T("set.hooks.on") : L.T("set.hooks.off");
        }

        private void OnInstallHooks()
        {
            DialogResult r = MessageBox.Show(this, L.T("msg.hooks.confirm"), L.T("msg.hooks.title"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (r != DialogResult.Yes) return;
            string err = HookInstaller.Install(typeof(SettingsForm).Assembly.Location);
            if (err != null) MessageBox.Show(this, L.T("msg.hooks.fail", err), L.T("msg.hooks.title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else MessageBox.Show(this, L.T("msg.hooks.done"), L.T("msg.hooks.title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            RefreshHookState();
        }

        private void OnRemoveHooks()
        {
            string err = HookInstaller.Uninstall();
            if (err != null) MessageBox.Show(this, L.T("msg.hooks.fail", err), L.T("msg.hooks.title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else MessageBox.Show(this, L.T("msg.hooks.removed"), L.T("msg.hooks.title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            RefreshHookState();
        }

        private void BuildData(TableLayoutPanel t)
        {
            AddNote(t, L.T("set.h.monitoring"), "head");
            AddCheck(t, _scanClaude, L.T("set.claude"), _cfg.ScanClaude);
            AddCheck(t, _scanGemini, L.T("set.gemini"), _cfg.ScanGemini);
            AddCheck(t, _scanCodex, L.T("set.codex"), _cfg.ScanCodex);
            AddCheck(t, _scanGeminiCli, L.T("set.geminicli"), _cfg.ScanGeminiCli);
            AddCheck(t, _scanOpenCode, L.T("set.opencode"), _cfg.ScanOpenCode);
            AddCheck(t, _scanCline, L.T("set.cline"), _cfg.ScanCline);
            AddCheck(t, _scanCopilot, L.T("set.copilot"), _cfg.ScanCopilot);
            if ((_cb != null && _cb.IsOpenCodeSqliteMissing != null && _cb.IsOpenCodeSqliteMissing()) ||
                (System.IO.File.Exists(System.IO.Path.Combine(Paths.OpenCodeDataDir, "opencode.db")) && ProcessHelper.FindExecutableOnPath("sqlite3.exe") == null))
            {
                AddNote(t, L.T("set.opencode.sqlite_warn"), "warn");
            }
            AddCheck(t, _adminData, L.T("set.claude_api"), _adminData.Checked);
            _adminData.CheckedChanged += delegate { if (_admin.Checked != _adminData.Checked) _admin.Checked = _adminData.Checked; };

            AddNote(t, L.T("set.h.ollama"), "head");
            AddCheck(t, _ollamaOn, L.T("set.ollama.enable"), _cfg.OllamaEnabled);
            _ollamaPort.Minimum = 1; _ollamaPort.Maximum = 65535; _ollamaPort.Width = 100;
            _ollamaPort.Value = _cfg.OllamaPort > 0 && _cfg.OllamaPort <= 65535 ? _cfg.OllamaPort : 11434;
            AddRow(t, L.T("set.ollama.port"), _ollamaPort);

            AddCheck(t, _wsl, L.T("set.wsl"), _cfg.ScanWsl);
            _extraDirs.Width = 160; _extraDirs.Text = _cfg.ExtraClaudeDirs ?? "";
            _extraDirs.Margin = new Padding(0, 2, 0, 0);
            Button browseExtra = new Button(); browseExtra.Text = L.T("set.browse"); browseExtra.AutoSize = true;
            browseExtra.Margin = new Padding(6, 0, 0, 0);
            browseExtra.Click += delegate
            {
                using (FolderBrowserDialog d = new FolderBrowserDialog())
                {
                    if (d.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(d.SelectedPath)) return;
                    string cur = _extraDirs.Text.Trim();
                    string picked = d.SelectedPath.Trim();
                    if (string.IsNullOrEmpty(cur)) _extraDirs.Text = picked;
                    else if (cur.IndexOf(picked, StringComparison.OrdinalIgnoreCase) < 0) _extraDirs.Text = cur.TrimEnd(';') + ";" + picked;
                }
            };
            FlowLayoutPanel extraRow = new FlowLayoutPanel();
            extraRow.AutoSize = true; extraRow.WrapContents = false; extraRow.Margin = new Padding(0);
            extraRow.Controls.Add(_extraDirs); extraRow.Controls.Add(browseExtra);
            AddRow(t, L.T("set.extradirs"), extraRow);

            _customJsonlDirs.Width = 160; _customJsonlDirs.Text = _cfg.CustomJsonlDirs ?? "";
            _customJsonlDirs.Margin = new Padding(0, 2, 0, 0);
            Button browseJsonl = new Button { Text = L.T("set.browse"), AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
            browseJsonl.Click += delegate
            {
                using (FolderBrowserDialog d = new FolderBrowserDialog())
                {
                    if (d.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(d.SelectedPath)) return;
                    string cur = _customJsonlDirs.Text.Trim();
                    string picked = d.SelectedPath.Trim();
                    if (string.IsNullOrEmpty(cur)) _customJsonlDirs.Text = picked;
                    else if (cur.IndexOf(picked, StringComparison.OrdinalIgnoreCase) < 0) _customJsonlDirs.Text = cur.TrimEnd(';') + ";" + picked;
                }
            };
            FlowLayoutPanel jsonlRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            jsonlRow.Controls.Add(_customJsonlDirs); jsonlRow.Controls.Add(browseJsonl);
            AddRow(t, L.T("set.customjsonl"), jsonlRow);
            AddNote(t, L.T("set.sources.note"), "dim");
            AddCheck(t, _projects, L.T("set.projects"), _cfg.ShowProjects);

            Button csv = new Button(); csv.Text = L.T("set.csv"); csv.AutoSize = true;
            csv.Click += delegate { if (_cb != null && _cb.ExportCsv != null) _cb.ExportCsv(); };
            Button diag = new Button(); diag.Text = L.T("set.diag"); diag.AutoSize = true;
            diag.Click += delegate { CopyDiagnostics(); };
            FlowLayoutPanel dp = new FlowLayoutPanel(); dp.AutoSize = true; dp.WrapContents = false;
            dp.Controls.Add(csv); dp.Controls.Add(diag);
            dp.Margin = new Padding(3, 10, 3, 6);
            t.Controls.Add(dp); t.SetColumnSpan(dp, 2);

            AddNote(t, L.T("set.h.pricing"), "head");
            FlowLayoutPanel pp = new FlowLayoutPanel(); pp.AutoSize = true; pp.WrapContents = false;
            Button pricing = new Button(); pricing.Text = L.T("set.pricing.open"); pricing.AutoSize = true;
            pricing.Click += delegate { try { Process.Start(ProcessHelper.SystemExe("notepad.exe"), "\"" + Paths.PricingFile + "\""); } catch { } };

            Button btnFetchPricing = new Button(); btnFetchPricing.Text = L.T("set.pricing.fetch"); btnFetchPricing.AutoSize = true;
            btnFetchPricing.Click += async delegate
            {
                btnFetchPricing.Enabled = false;
                btnFetchPricing.Text = L.T("set.pricing.fetching");
                try
                {
                    int count = await OfficialPricingFetcher.FetchAndSaveAsync();
                    if (IsDisposed) return;
                    if (count > 0)
                    {
                        if (_cb != null && _cb.PricingUpdated != null) _cb.PricingUpdated();
                        if (Visible)
                            MessageBox.Show(this, L.T("set.pricing.fetch_ok", count), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else if (Visible)
                        MessageBox.Show(this, L.T("set.pricing.fetch_fail"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch (Exception ex)
                {
                    Log.Write("pricing.fetch", ex);
                    if (!IsDisposed && Visible)
                        MessageBox.Show(this, L.T("set.pricing.fetch_fail"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                finally
                {
                    if (!IsDisposed)
                    {
                        btnFetchPricing.Enabled = true;
                        btnFetchPricing.Text = L.T("set.pricing.fetch");
                    }
                }
            };
            Button customPricing = new Button(); customPricing.Text = L.T("set.pricing.custom"); customPricing.AutoSize = true;
            customPricing.Click += delegate { Pricing.EnsureCustomTemplate(); try { Process.Start(ProcessHelper.SystemExe("notepad.exe"), "\"" + Paths.CustomPricingFile + "\""); } catch { } };
            pp.Controls.Add(pricing);
            pp.Controls.Add(customPricing);
            pp.Controls.Add(btnFetchPricing);
            pp.Margin = new Padding(3, 6, 3, 2);

            Label pnote = new Label(); pnote.AutoSize = true; pnote.Tag = "dim"; pnote.Anchor = AnchorStyles.Left;
            pnote.MaximumSize = new Size(620, 0);
            pnote.Margin = new Padding(3, 2, 3, 6);
            pnote.Text = L.T("set.pricing.note");
            t.Controls.Add(pp); t.SetColumnSpan(pp, 2);
            t.Controls.Add(pnote); t.SetColumnSpan(pnote, 2);

            AddNote(t, L.T("set.h.backup"), "head");
            FlowLayoutPanel bp = new FlowLayoutPanel(); bp.AutoSize = true; bp.WrapContents = false;
            Button btnExportProf = new Button(); btnExportProf.Text = L.T("set.profile.export"); btnExportProf.AutoSize = true;
            btnExportProf.Click += OnExportProfileClick;
            Button btnImportProf = new Button(); btnImportProf.Text = L.T("set.profile.import"); btnImportProf.AutoSize = true;
            btnImportProf.Click += OnImportProfileClick;
            bp.Controls.Add(btnExportProf);
            bp.Controls.Add(btnImportProf);
            t.Controls.Add(bp); t.SetColumnSpan(bp, 2);
        }

        private void CopyDiagnostics()
        {
            if (_cb == null || _cb.Diagnostics == null) return;
            try
            {
                Clipboard.SetText(_cb.Diagnostics());
                MessageBox.Show(this, L.T("msg.diag.copied"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { Log.Write("diag", ex); MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private void BuildProbe(TableLayoutPanel t)
        {
            AddNote(t, L.T("set.probe.warn"), "warn");
            AddCheck(t, _probe, L.T("set.probe.enable"), _cfg.ProbeEnabled);
            _probeMin.Minimum = 5; _probeMin.Maximum = 240; _probeMin.Width = 70;
            _probeMin.Value = Math.Max(5, Math.Min(240, _cfg.ProbeMinMinutes));
            AddRow(t, L.T("set.probe.every"), _probeMin);
            _probeModel.DropDownStyle = ComboBoxStyle.DropDown; _probeModel.Width = 220;
            _probeModel.Items.AddRange(new object[] { "claude-haiku-5-5", "claude-haiku-4-5", "claude-sonnet-4-6" });
            _probeModel.Text = string.IsNullOrEmpty(_cfg.ProbeModel) ? "claude-haiku-5-5" : _cfg.ProbeModel;
            AddRow(t, L.T("set.probe.model"), _probeModel);
            AddNote(t, L.T("set.probe.note"), "dim");

            AddCheck(t, _mirror, L.T("set.mirror"), _cfg.MirrorEnabled);
            _mirrorPath.Width = 260; _mirrorPath.Text = _cfg.MirrorFilePath ?? "";
            Button browse = new Button(); browse.Text = L.T("set.browse"); browse.AutoSize = true;
            browse.Click += delegate
            {
                using (OpenFileDialog d = new OpenFileDialog())
                {
                    d.Filter = "JSON (*.json)|*.json"; d.CheckFileExists = true;
                    if (d.ShowDialog(this) == DialogResult.OK) _mirrorPath.Text = d.FileName;
                }
            };
            AddRow(t, L.T("set.mirror.path"), _mirrorPath);
            t.Controls.Add(new Label()); t.Controls.Add(browse);
        }

        private void BuildAdmin(TableLayoutPanel t)
        {
            AddCheck(t, _admin, L.T("set.admin.enable"), _admin.Checked);
            _admin.CheckedChanged += delegate { if (_adminData.Checked != _admin.Checked) _adminData.Checked = _admin.Checked; };
            _key.UseSystemPasswordChar = true; _key.Width = 260;
            AddRow(t, L.T("set.admin.key"), _key);
            _keyState.AutoSize = true; _keyState.Tag = "dim";
            RefreshKeyState();
            _keyState.MaximumSize = new Size(260, 0);
            Button clear = new Button(); clear.Text = L.T("set.admin.clear"); clear.AutoSize = true;
            clear.Click += delegate { _clearKey = true; _key.Text = ""; RefreshKeyState(); };
            FlowLayoutPanel kp = new FlowLayoutPanel(); kp.AutoSize = true; kp.FlowDirection = FlowDirection.TopDown; kp.WrapContents = false;
            kp.Controls.Add(_keyState); kp.Controls.Add(clear);
            t.Controls.Add(new Label()); t.Controls.Add(kp);
            _keyIds.Width = 260; _keyIds.Text = _cfg.AdminApiKeyIds ?? "";
            AddRow(t, L.T("set.admin.keyids"), _keyIds);
            _wsIds.Width = 260; _wsIds.Text = _cfg.AdminWorkspaceIds ?? "";
            AddRow(t, L.T("set.admin.wsids"), _wsIds);
            AddNote(t, L.T("set.admin.note"), "dim");
        }

        private static void AddCheck(TableLayoutPanel t, CheckBox c, string text, bool value)
        {
            c.Text = text; c.UseMnemonic = false; c.Checked = value; c.AutoSize = true; c.MaximumSize = new Size(620, 0); c.Margin = new Padding(3, 4, 3, 4);
            t.Controls.Add(c); t.SetColumnSpan(c, 2);
        }

        private static void AddRow(TableLayoutPanel t, string label, Control c)
        {
            Label l = new Label(); l.Text = label; l.UseMnemonic = false; l.AutoSize = true; l.Anchor = AnchorStyles.Left; l.MaximumSize = new Size(330, 0);
            t.Controls.Add(l); t.Controls.Add(c);
        }

        /// <summary>Teks penjelas lintas dua kolom. kind: "dim" (abu), "warn" (peringatan), "head" (judul bagian).</summary>
        private void AddNote(TableLayoutPanel t, string text, string kind)
        {
            Label l = new Label(); l.Text = text; l.UseMnemonic = false; l.AutoSize = true; l.MaximumSize = new Size(620, 0); l.Tag = kind;
            l.Margin = kind == "head" ? new Padding(3, 14, 3, 4) : new Padding(3, 6, 3, 6);
            if (kind == "head")
            {
                if (_headFont == null) _headFont = new Font("Segoe UI Semibold", 9.5f);
                l.Font = _headFont;
            }
            t.Controls.Add(l); t.SetColumnSpan(l, 2);
        }

        // ---------------- tema ----------------
        private void ApplyTheme()
        {
            _appliedLight = Palette.IsLight;
            SuspendLayout();
            try
            {
                BackColor = Palette.Bg; ForeColor = Palette.Text;
                ThemeControls(this);
                _tabStrip.BackColor = Palette.Bg;
                foreach (Button b in _tabBtns)
                {
                    b.BackColor = Palette.Bg; b.ForeColor = Palette.Dim;
                    b.FlatAppearance.MouseOverBackColor = Palette.Hover; b.FlatAppearance.MouseDownBackColor = Palette.Hover;
                }
                if (_tab >= 0 && _tab < _tabBtns.Count) _tabBtns[_tab].ForeColor = Palette.Text;
                _saveBtn.BackColor = Palette.Accent; _saveBtn.ForeColor = Color.White;
                _saveBtn.FlatAppearance.BorderColor = Palette.Accent;
                if (_applyBtn != null)
                {
                    _applyBtn.BackColor = Palette.Surface; _applyBtn.ForeColor = Palette.Text;
                    _applyBtn.FlatAppearance.BorderColor = Palette.Border;
                }
                if (IsHandleCreated) DarkUi.TitleBar(Handle, !Palette.IsLight);
                foreach (Panel p in _pages)
                {
                    if (p.IsHandleCreated) DarkUi.ControlTheme(p.Handle, !Palette.IsLight);
                }
            }
            finally
            {
                ResumeLayout(false);
            }
            Invalidate(true);
        }

        private void ThemeControls(Control root)
        {
            foreach (Control c in root.Controls)
            {
                Label l = c as Label;
                if (l != null)
                {
                    string tag = l.Tag as string;
                    l.BackColor = Palette.Bg;
                    l.ForeColor = tag == "dim" ? Palette.Dim : (tag == "warn" ? Palette.Amber : Palette.Text);
                }
                else if (c is CheckBox) { c.BackColor = Palette.Bg; c.ForeColor = c.Enabled ? Palette.Text : Palette.Dim; }
                else if (c is TextBox) { TextBox tb = (TextBox)c; tb.BackColor = Palette.Surface; tb.ForeColor = Palette.Text; tb.BorderStyle = BorderStyle.FixedSingle; }
                else if (c is ComboBox) { ComboBox cb = (ComboBox)c; cb.FlatStyle = FlatStyle.Flat; cb.BackColor = Palette.Surface; cb.ForeColor = Palette.Text; }
                else if (c is NumericUpDown) { NumericUpDown n = (NumericUpDown)c; n.BackColor = Palette.Surface; n.ForeColor = Palette.Text; n.BorderStyle = BorderStyle.FixedSingle; }
                else if (c is TrackBar) { c.BackColor = Palette.Bg; }
                else if (c is Button)
                {
                    Button b = (Button)c;
                    if (!_tabBtns.Contains(b))
                    {
                        b.FlatStyle = FlatStyle.Flat; b.BackColor = Palette.Surface; b.ForeColor = Palette.Text;
                        b.FlatAppearance.BorderColor = Palette.Border; b.FlatAppearance.MouseOverBackColor = Palette.Hover;
                    }
                }
                else c.BackColor = Palette.Bg;
                if (c.HasChildren) ThemeControls(c);
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            DarkUi.TitleBar(Handle, !Palette.IsLight);
            CaptureI18nBindings();
        }

        private async void OnWebhookTestClick(object sender, EventArgs e)
        {
            string url = _webhookUrl.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                MessageBox.Show(this, string.Format(L.T("msg.webhook.fail"), "URL empty"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _webhookTestBtn.Enabled = false;
            try
            {
                string err = await WebhookSender.SendAsync(url, L.AppFullName, "Webhook test from " + L.AppFullName);
                if (IsDisposed) return;
                if (err == null)
                    MessageBox.Show(this, L.T("msg.webhook.ok"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                else
                    MessageBox.Show(this, string.Format(L.T("msg.webhook.fail"), err), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                if (!IsDisposed)
                    MessageBox.Show(this, string.Format(L.T("msg.webhook.fail"), ex.Message), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                if (!IsDisposed) _webhookTestBtn.Enabled = true;
            }
        }

        private Dictionary<string, string> GetI18nRev()
        {
            if (_i18nRev != null && string.Equals(_i18nRevLang, L.Lang, StringComparison.OrdinalIgnoreCase))
                return _i18nRev;
            Dictionary<string, string> rev = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string[]> kv in L.All)
            {
                string t = L.T(kv.Key);
                if (!string.IsNullOrEmpty(t) && !rev.ContainsKey(t)) rev[t] = kv.Key;
            }
            foreach (KeyValuePair<string, string[]> kv in L.All)
            {
                if (kv.Key.StartsWith("set.") || kv.Key.StartsWith("backdrop.") || kv.Key.StartsWith("btn."))
                {
                    string t = L.T(kv.Key);
                    if (!string.IsNullOrEmpty(t)) rev[t] = kv.Key;
                }
            }
            _i18nRev = rev;
            _i18nRevLang = L.Lang;
            return rev;
        }

        private void CaptureI18nBindings()
        {
            if (_i18nCaptured) return;
            _i18nCaptured = true;
            _i18n.Add(delegate { Text = L.T("set.title"); });
            CaptureControlI18n(this, GetI18nRev());
        }

        private void CaptureControlI18n(Control root, Dictionary<string, string> rev)
        {
            foreach (Control c in root.Controls)
            {
                if (c is TextBox || c is NumericUpDown || c is TrackBar) continue;
                ComboBox cb = c as ComboBox;
                if (cb != null)
                {
                    if (cb != _lang && cb != _monitor && cb.Items.Count > 0)
                    {
                        List<KeyValuePair<int, string>> itemKeys = new List<KeyValuePair<int, string>>();
                        for (int i = 0; i < cb.Items.Count; i++)
                        {
                            string s = cb.Items[i] as string;
                            string k;
                            if (s != null && rev.TryGetValue(s, out k))
                                itemKeys.Add(new KeyValuePair<int, string>(i, k));
                        }
                        if (itemKeys.Count > 0)
                        {
                            ComboBox targetCb = cb;
                            _i18n.Add(delegate
                            {
                                int sel = targetCb.SelectedIndex;
                                targetCb.BeginUpdate();
                                try
                                {
                                    foreach (KeyValuePair<int, string> ik in itemKeys)
                                        targetCb.Items[ik.Key] = L.T(ik.Value);
                                }
                                finally
                                {
                                    if (sel >= 0 && sel < targetCb.Items.Count) targetCb.SelectedIndex = sel;
                                    targetCb.EndUpdate();
                                }
                            });
                        }
                    }
                    continue;
                }

                if ((c is Label || c is CheckBox || c is Button) && !string.IsNullOrEmpty(c.Text)
                    && c != _opacityLbl && c != _hookState && c != _keyState)
                {
                    string key;
                    if (rev.TryGetValue(c.Text, out key))
                    {
                        Control target = c;
                        string capturedKey = key;
                        _i18n.Add(delegate { target.Text = L.T(capturedKey); });
                    }
                }
                if (c.HasChildren) CaptureControlI18n(c, rev);
            }
        }

        private void RefreshKeyState()
        {
            _keyState.Text = _clearKey
                ? L.T("set.admin.cleared")
                : (string.IsNullOrEmpty(_cfg.AdminKeyProtected) ? L.T("set.admin.nokey") : L.T("set.admin.stored"));
        }

        internal void RefreshI18n()
        {
            CaptureI18nBindings();
            _builtLang = L.Lang;
            SuspendLayout();
            try
            {
                foreach (Action a in _i18n) a();
                if (_monitor.Items.Count > 0)
                {
                    int mSel = _monitor.SelectedIndex;
                    _monitor.Items[0] = L.T("monitor.primary");
                    if (mSel >= 0 && mSel < _monitor.Items.Count) _monitor.SelectedIndex = mSel;
                }
                _opacityLbl.Text = L.T("set.opacity", _opacity.Value);
                if (_tabBuilt[2]) RefreshHookState();
                if (_tabBuilt[5]) RefreshKeyState();
            }
            finally
            {
                ResumeLayout(true);
            }
            PerformLayout();
            Invalidate(true);
        }

        // ---------------- simpan & terapkan ----------------
        private bool SaveToConfig()
        {
            if (_tabBuilt[4])
            {
                // Konfirmasi eksplisit setiap kali probe dinyalakan (dari mati ke hidup). Default tombol: Tidak.
                if (_probe.Checked && !_cfg.ProbeEnabled)
                {
                    DialogResult r = MessageBox.Show(this, L.T("msg.probe.body"), L.T("msg.probe.title"),
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                    if (r != DialogResult.Yes) { _probe.Checked = false; SelectTab(4); return false; }
                }
            }
            string key = _tabBuilt[5] ? _key.Text.Trim() : "";
            if (_tabBuilt[5])
            {
                if (key.Length > 0 && !key.StartsWith("sk-ant-admin", StringComparison.Ordinal))
                {
                    SelectTab(5);
                    MessageBox.Show(this, L.T("msg.key.prefix"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                if (_admin.Checked && key.Length == 0 && (_clearKey || string.IsNullOrEmpty(_cfg.AdminKeyProtected)))
                {
                    SelectTab(5);
                    MessageBox.Show(this, L.T("msg.key.required"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
            }
            else if (_tabBuilt[3] && _adminData.Checked && string.IsNullOrEmpty(_cfg.AdminKeyProtected))
            {
                SelectTab(5);
                MessageBox.Show(this, L.T("msg.key.required"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            string pModel = _tabBuilt[4] ? (_probeModel.Text ?? "").Trim() : null;
            if (_tabBuilt[4])
            {
                if (_mirror.Checked && !System.IO.File.Exists(_mirrorPath.Text.Trim()))
                {
                    SelectTab(4);
                    MessageBox.Show(this, L.T("msg.mirror.missing"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                if (pModel.Length == 0) pModel = "claude-haiku-5-5";
                if (!CliProbe.IsValidModelName(pModel))
                {
                    SelectTab(4);
                    MessageBox.Show(this, L.T("msg.probe.model_invalid"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
            }
            if (_tabBuilt[2] && AlertEngine.ParseThresholds(_alertTh.Text) == null)
            {
                SelectTab(2);
                MessageBox.Show(this, L.T("msg.thresholds.invalid"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (_tabBuilt[1])
            {
                uint hm, hv;
                if (_hotkeyOn.Checked && !HotkeyUtil.TryParse(_hotkeyText.Text, out hm, out hv))
                {
                    SelectTab(1);
                    MessageBox.Show(this, L.T("msg.hotkey.invalid"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
            }
            string rawWebhook = _tabBuilt[2] ? _webhookUrl.Text.Trim() : null;
            if (_tabBuilt[2] && (_webhookOn.Checked || rawWebhook.Length > 0))
            {
                Uri dummyUri;
                string wErr;
                if (!WebhookSender.IsSafeWebhookUrl(rawWebhook, out dummyUri, out wErr))
                {
                    SelectTab(2);
                    MessageBox.Show(this, string.Format(L.T("msg.webhook.fail"), wErr), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
            }
            if (_tabBuilt[2] && !string.IsNullOrWhiteSpace(_wavPath.Text) && !AudioAlert.IsValidWavFile(_wavPath.Text))
            {
                SelectTab(2);
                MessageBox.Show(this, L.T("msg.wav.invalid"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (_tabBuilt[1])
            {
                try { Autostart.Set(_auto.Checked); } catch (Exception ex) { Log.Write("autostart", ex); }
                _cfg.ShowFloating = _showFloat.Checked;
                _cfg.AlwaysOnTop = _onTop.Checked;
                _cfg.HideOnFullscreen = _hideFs.Checked;
                _cfg.AutoUpdateCheck = _autoUpdate.Checked;
                _cfg.HotkeyEnabled = _hotkeyOn.Checked;
                _cfg.Hotkey = _hotkeyText.Text.Trim();
            }

            // Tab 0 (Display) - always built
            _cfg.ShowRemaining = _remaining.Checked;
            _cfg.Language = _lang.SelectedIndex >= 0 && _lang.SelectedIndex < _langCodes.Count ? _langCodes[_lang.SelectedIndex] : "en";
            _cfg.HistoryDays = _histDays.SelectedIndex == 1 ? 30 : 14;
            _cfg.DockEdge = _edge.SelectedIndex >= 0 && _edge.SelectedIndex < Edges.Length ? Edges[_edge.SelectedIndex] : "top";
            _cfg.DockMonitor = _monitor.SelectedIndex >= 0 && _monitor.SelectedIndex < _monitorNames.Count ? _monitorNames[_monitor.SelectedIndex] : "";
            _cfg.Backdrop = _backdrop.SelectedIndex >= 0 && _backdrop.SelectedIndex < Backdrops.Length ? Backdrops[_backdrop.SelectedIndex] : "solid";
            _cfg.OpacityPercent = _opacity.Value;
            _cfg.Compact = _compact.Checked;
            _cfg.ZenMode = _zenMode.Checked;
            _cfg.ShowContextBar = _contextBar.Checked;
            _cfg.ContextWindowOverride = _ctxWin.SelectedIndex == 1 ? 200000L : (_ctxWin.SelectedIndex == 2 ? 400000L : (_ctxWin.SelectedIndex == 3 ? 1000000L : 0));
            _cfg.CardLayout = _cardLayout.SelectedIndex == 2 ? "3col" : (_cardLayout.SelectedIndex == 1 ? "2col" : "auto");
            _cfg.MiniPillMode = _pillMode.SelectedIndex == 1 ? "highest" : (_pillMode.SelectedIndex == 2 ? "cost_runway" : "all");
            _cfg.WidgetScale = _scale.SelectedIndex == 0 ? 0.85 : (_scale.SelectedIndex == 2 ? 1.15 : (_scale.SelectedIndex == 3 ? 1.30 : 1.0));
            _cfg.DockTop = _dock.Checked;
            _cfg.Theme = _theme.SelectedIndex == 1 ? "light" : (_theme.SelectedIndex == 2 ? "system" : "dark");
            _cfg.ClickThrough = _click.Checked;
            _cfg.ShowInTaskbar = false;
            _cfg.CurrencyCustomEnabled = _currencyOn.Checked;
            _cfg.CurrencySymbol = _currencySymbol.Text.Trim();
            if (string.IsNullOrEmpty(_cfg.CurrencySymbol)) _cfg.CurrencySymbol = "Rp";
            _cfg.CurrencyRate = (double)_currencyRate.Value;
            _cfg.CurrencyShowBoth = _currencyBoth.Checked;

            if (_tabBuilt[2])
            {
                _cfg.AlertsEnabled = _alertsOn.Checked;
                _cfg.AlertThresholds = string.Join(",", Array.ConvertAll(AlertEngine.ParseThresholds(_alertTh.Text), delegate(int v) { return v.ToString(); }));
                _cfg.ResetNotify = _resetOn.Checked;
                _cfg.BudgetDaily = (double)_budgetDay.Value;
                _cfg.BudgetMonthly = (double)_budgetMonth.Value;
                _cfg.HookNotifyStop = _stopOn.Checked;
                _cfg.HookNotifyMinSeconds = (int)_stopSecs.Value;
                _cfg.HookNotifyInput = _inputOn.Checked;
                _cfg.AudioAlertsEnabled = _audioOn.Checked;
                _cfg.CustomSoundPath = _wavPath.Text.Trim();
                _cfg.ContextAlertEnabled = _contextOn.Checked;
                _cfg.BurnRateAlertEnabled = _burnRateOn.Checked;
                _cfg.BurnRateThresholdTokens = (long)_burnRateTokens.Value;
                _cfg.CacheDropAlertEnabled = _cacheDropOn.Checked;
                _cfg.CacheDropMinPercent = (int)_cacheDropPct.Value;
                _cfg.AlertCooldownMinutes = (int)_cooldownMins.Value;
                _cfg.QuietHoursEnabled = _quietOn.Checked;
                _cfg.QuietStartHour = (int)_quietStart.Value;
                _cfg.QuietEndHour = (int)_quietEnd.Value;
                _cfg.DigestEnabled = _digestOn.Checked;
                _cfg.DigestSchedule = _digestSchedule.SelectedIndex == 1 ? "weekly" : "daily";
                _cfg.DigestHour = (int)_digestHour.Value;
                _cfg.WebhookEnabled = _webhookOn.Checked;
                _cfg.SetWebhookUrl(rawWebhook);
            }
            if (_tabBuilt[3])
            {
                _cfg.ScanClaude = _scanClaude.Checked;
                _cfg.ScanGemini = _scanGemini.Checked;
                _cfg.ScanCodex = _scanCodex.Checked;
                _cfg.ScanGeminiCli = _scanGeminiCli.Checked;
                _cfg.ScanOpenCode = _scanOpenCode.Checked;
                _cfg.ScanCline = _scanCline.Checked;
                _cfg.ScanCopilot = _scanCopilot.Checked;
                _cfg.ScanWsl = _wsl.Checked;
                _cfg.ShowProjects = _projects.Checked;
                _cfg.ExtraClaudeDirs = _extraDirs.Text.Trim();
                _cfg.CustomJsonlDirs = _customJsonlDirs.Text.Trim();
                _cfg.OllamaEnabled = _ollamaOn.Checked;
                _cfg.OllamaPort = (int)_ollamaPort.Value;
            }
            if (_tabBuilt[4])
            {
                _cfg.ProbeEnabled = _probe.Checked;
                _cfg.ProbeMinMinutes = (int)_probeMin.Value;
                _cfg.ProbeModel = pModel;
                _cfg.MirrorEnabled = _mirror.Checked;
                _cfg.MirrorFilePath = _mirrorPath.Text.Trim();
            }
            if (_tabBuilt[3] || _tabBuilt[5])
                _cfg.AdminEnabled = _tabBuilt[5] ? _admin.Checked : _adminData.Checked;
            if (_tabBuilt[5])
            {
                if (_clearKey) { _cfg.SetAdminKey(null); _clearKey = false; }
                if (key.Length > 0) _cfg.SetAdminKey(key);
                _cfg.AdminApiKeyIds = _keyIds.Text.Trim();
                _cfg.AdminWorkspaceIds = _wsIds.Text.Trim();
                _key.Text = "";
            }
            _cfg.Save();
            return true;
        }

        private void OnExportProfileClick(object sender, EventArgs e)
        {
            if (!SaveToConfig()) return;
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Filter = L.T("msg.json.filter");
                d.FileName = "ai-usage-profile-" + DateTime.Now.ToString("yyyyMMdd") + ".json";
                d.DefaultExt = "json";
                d.OverwritePrompt = true;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    System.IO.File.WriteAllText(d.FileName, _cfg.ExportProfileJson(), new System.Text.UTF8Encoding(false));
                    MessageBox.Show(this, L.T("msg.profile.exported", d.FileName), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    Log.Write("profile.export", ex);
                    MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void OnImportProfileClick(object sender, EventArgs e)
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Filter = L.T("msg.json.filter");
                d.CheckFileExists = true;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string json = System.IO.File.ReadAllText(d.FileName, System.Text.Encoding.UTF8);
                    AppConfig imported = AppConfig.ImportProfileJson(json, _cfg);
                    if (imported == null) throw new InvalidOperationException("Empty profile");
                    _cfg.ApplyProfileFrom(imported);
                    _cfg.Save();
                    SyncControlsFromConfig();
                    L.Set(_cfg.Language);
                    Palette.Apply(ThemeUtil.ResolveLight(_cfg.Theme));
                    RefreshI18n();
                    ApplyTheme();
                    if (_cb != null && _cb.Apply != null) _cb.Apply();
                    MessageBox.Show(this, L.T("msg.profile.imported"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    Log.Write("profile.import", ex);
                    MessageBox.Show(this, L.T("msg.profile.invalid", ex.Message), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        internal void PrewarmHandles()
        {
            if (IsDisposed) return;
            IntPtr h = Handle;
            ForceChildHandles(_pages[0]);
            ForceChildHandles(_tabStrip);
        }

        private static void ForceChildHandles(Control root)
        {
            if (root == null) return;
            foreach (Control c in root.Controls)
            {
                IntPtr ch = c.Handle;
                if (c.HasChildren) ForceChildHandles(c);
            }
        }

        internal void PrepareForOpen()
        {
            SuspendLayout();
            try
            {
                SelectTab(0);
                SyncControlsFromConfig();
                if (!string.Equals(_builtLang, L.Lang, StringComparison.OrdinalIgnoreCase))
                    RefreshI18n();
                if (_appliedLight != Palette.IsLight)
                    ApplyTheme();
                CenterToScreen();
            }
            finally
            {
                ResumeLayout(false);
            }
        }

        private void SyncControlsFromConfig()
        {
            int li = _langCodes.IndexOf(_cfg.Language);
            _lang.SelectedIndex = li >= 0 ? li : 0;
            _theme.SelectedIndex = _cfg.Theme == "light" ? 1 : (_cfg.Theme == "system" ? 2 : 0);
            _opacity.Value = Math.Max(30, Math.Min(100, _cfg.OpacityPercent));
            _opacityLbl.Text = L.T("set.opacity", _opacity.Value);
            _remaining.Checked = _cfg.ShowRemaining;
            _histDays.SelectedIndex = _cfg.HistoryDays == 30 ? 1 : 0;
            _dock.Checked = _cfg.DockTop;
            int ei = Array.IndexOf(Edges, _cfg.DockEdge); _edge.SelectedIndex = ei >= 0 ? ei : 0;
            int mi = string.IsNullOrEmpty(_cfg.DockMonitor) ? 0 : _monitorNames.IndexOf(_cfg.DockMonitor); _monitor.SelectedIndex = mi >= 0 ? mi : 0;
            int bi = Array.IndexOf(Backdrops, _cfg.Backdrop); _backdrop.SelectedIndex = bi >= 0 ? bi : 0;
            _compact.Checked = _cfg.Compact;
            _zenMode.Checked = _cfg.ZenMode;
            _click.Checked = _cfg.ClickThrough;
            _contextBar.Checked = _cfg.ShowContextBar;
            _ctxWin.SelectedIndex = _cfg.ContextWindowOverride == 200000L ? 1 : (_cfg.ContextWindowOverride == 400000L ? 2 : (_cfg.ContextWindowOverride == 1000000L ? 3 : 0));
            _cardLayout.SelectedIndex = _cfg.CardLayout == "3col" ? 2 : (_cfg.CardLayout == "2col" ? 1 : 0);
            _pillMode.SelectedIndex = _cfg.MiniPillMode == "highest" ? 1 : (_cfg.MiniPillMode == "cost_runway" ? 2 : 0);
            _scale.SelectedIndex = _cfg.WidgetScale < 0.92 ? 0 : (_cfg.WidgetScale < 1.08 ? 1 : (_cfg.WidgetScale < 1.22 ? 2 : 3));
            _currencyOn.Checked = _cfg.CurrencyCustomEnabled;
            _currencySymbol.Text = _cfg.CurrencySymbol ?? "Rp";
            ApplyCurrencyRateFormat(_cfg.CurrencySymbol);
            _currencyRate.Value = ClampCurrencyRate(_cfg.CurrencyRate);
            _currencyBoth.Checked = _cfg.CurrencyShowBoth;
            _admin.Checked = _cfg.AdminEnabled;
            _adminData.Checked = _cfg.AdminEnabled;
            if (_tabBuilt[1])
            {
                _showFloat.Checked = _cfg.ShowFloating;
                try { _auto.Checked = Autostart.IsEnabled; } catch { }
                _onTop.Checked = _cfg.AlwaysOnTop;
                _hideFs.Checked = _cfg.HideOnFullscreen;
                _autoUpdate.Checked = _cfg.AutoUpdateCheck;
                _hotkeyOn.Checked = _cfg.HotkeyEnabled;
                _hotkeyText.Text = _cfg.Hotkey ?? "Win+Alt+U";
            }
            if (_tabBuilt[2])
            {
                _alertsOn.Checked = _cfg.AlertsEnabled;
                _alertTh.Text = _cfg.AlertThresholds ?? "80,95";
                _resetOn.Checked = _cfg.ResetNotify;
                _audioOn.Checked = _cfg.AudioAlertsEnabled;
                _wavPath.Text = _cfg.CustomSoundPath ?? "";
                _contextOn.Checked = _cfg.ContextAlertEnabled;
                _burnRateOn.Checked = _cfg.BurnRateAlertEnabled;
                _burnRateTokens.Value = Math.Max(1000, Math.Min(10000000, _cfg.BurnRateThresholdTokens <= 0 ? 100000 : _cfg.BurnRateThresholdTokens));
                _cacheDropOn.Checked = _cfg.CacheDropAlertEnabled;
                _cacheDropPct.Value = Math.Max(5, Math.Min(95, _cfg.CacheDropMinPercent <= 0 ? 30 : _cfg.CacheDropMinPercent));
                _cooldownMins.Value = Math.Max(1, Math.Min(1440, _cfg.AlertCooldownMinutes <= 0 ? 15 : _cfg.AlertCooldownMinutes));
                _webhookOn.Checked = _cfg.WebhookEnabled;
                _webhookUrl.Text = _cfg.GetWebhookUrl() ?? "";
                _quietOn.Checked = _cfg.QuietHoursEnabled;
                _quietStart.Value = Math.Max(0, Math.Min(23, _cfg.QuietStartHour));
                _quietEnd.Value = Math.Max(0, Math.Min(23, _cfg.QuietEndHour));
                _digestOn.Checked = _cfg.DigestEnabled;
                _digestSchedule.SelectedIndex = _cfg.DigestSchedule == "weekly" ? 1 : 0;
                _digestHour.Value = Math.Max(0, Math.Min(23, _cfg.DigestHour));
                _budgetDay.Value = (decimal)Math.Max(0, Math.Min(1000000, _cfg.BudgetDaily));
                _budgetMonth.Value = (decimal)Math.Max(0, Math.Min(1000000, _cfg.BudgetMonthly));
                _stopOn.Checked = _cfg.HookNotifyStop;
                _stopSecs.Value = Math.Max(0, Math.Min(3600, _cfg.HookNotifyMinSeconds));
                _inputOn.Checked = _cfg.HookNotifyInput;
                RefreshHookState();
            }
            if (_tabBuilt[3])
            {
                _scanClaude.Checked = _cfg.ScanClaude;
                _scanGemini.Checked = _cfg.ScanGemini;
                _scanCodex.Checked = _cfg.ScanCodex;
                _scanGeminiCli.Checked = _cfg.ScanGeminiCli;
                _scanOpenCode.Checked = _cfg.ScanOpenCode;
                _scanCline.Checked = _cfg.ScanCline;
                _scanCopilot.Checked = _cfg.ScanCopilot;
                _wsl.Checked = _cfg.ScanWsl;
                _projects.Checked = _cfg.ShowProjects;
                _extraDirs.Text = _cfg.ExtraClaudeDirs ?? "";
                _customJsonlDirs.Text = _cfg.CustomJsonlDirs ?? "";
                _ollamaOn.Checked = _cfg.OllamaEnabled;
                _ollamaPort.Value = _cfg.OllamaPort > 0 && _cfg.OllamaPort <= 65535 ? _cfg.OllamaPort : 11434;
            }
            if (_tabBuilt[4])
            {
                _probe.Checked = _cfg.ProbeEnabled;
                _probeMin.Value = Math.Max(5, Math.Min(240, _cfg.ProbeMinMinutes));
                _probeModel.Text = string.IsNullOrEmpty(_cfg.ProbeModel) ? "claude-haiku-5-5" : _cfg.ProbeModel;
                _mirror.Checked = _cfg.MirrorEnabled;
                _mirrorPath.Text = _cfg.MirrorFilePath ?? "";
            }
            if (_tabBuilt[5])
            {
                _clearKey = false;
                _key.Text = "";
                RefreshKeyState();
                _keyIds.Text = _cfg.AdminApiKeyIds ?? "";
                _wsIds.Text = _cfg.AdminWorkspaceIds ?? "";
            }
        }

        private void OnApply(object sender, EventArgs e)
        {
            CaptureI18nBindings();
            string prevLang = L.Lang;
            if (!SaveToConfig()) return;
            if (!string.Equals(prevLang, _cfg.Language, StringComparison.OrdinalIgnoreCase))
            {
                L.Set(_cfg.Language);
                RefreshI18n();
            }
            Palette.Apply(ThemeUtil.ResolveLight(_cfg.Theme));
            ApplyTheme();
            if (_cb != null && _cb.Apply != null) _cb.Apply();
        }

        private void OnSave(object sender, EventArgs e)
        {
            if (!SaveToConfig()) return;
            Palette.Apply(ThemeUtil.ResolveLight(_cfg.Theme));
            ApplyTheme();
            if (_cb != null && _cb.Apply != null) _cb.Apply();
            DialogResult = DialogResult.OK;
            Close();
        }

        private void ApplyCurrencyRateFormat(string sym)
        {
            bool isIdr = string.Equals(GoogleRateFetcher.NormalizeCurrencyCode(sym), "IDR", StringComparison.OrdinalIgnoreCase);
            _currencyRate.DecimalPlaces = isIdr ? 0 : 2;
            _currencyRate.Increment = isIdr ? 100m : 0.1m;
        }

        private static decimal ClampCurrencyRate(double rate)
        {
            double v = rate <= 0 || double.IsNaN(rate) || double.IsInfinity(rate) ? 16200.0 : rate;
            return (decimal)Math.Max(0.01, Math.Min(10000000.0, v));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_baseFont != null) { _baseFont.Dispose(); _baseFont = null; }
                if (_tabFont != null) { _tabFont.Dispose(); _tabFont = null; }
                if (_headFont != null) { _headFont.Dispose(); _headFont = null; }
            }
            base.Dispose(disposing);
        }
    }
}
