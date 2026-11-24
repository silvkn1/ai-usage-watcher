using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ClaudeUsageWidget
{
    internal class SetupWizardForm : Form
    {
        private static readonly string[] Edges = { "top", "top-left", "top-right", "bottom", "bottom-left", "bottom-right" };
        private static readonly string[] Backdrops = { "acrylic", "mica", "mica-alt", "solid" };

        private readonly AppConfig _cfg;
        private int _step = 0;

        // Header controls
        private readonly Panel _headerPanel = new Panel();
        private readonly Label _titleLabel = new Label();
        private readonly Label _subtitleLabel = new Label();
        private readonly Label _stepPill1 = new Label();
        private readonly Label _stepPill2 = new Label();
        private readonly Label _stepPill3 = new Label();

        // Main panels for the 3 steps
        private readonly Panel _body = new Panel();
        private readonly Panel _page1 = new Panel();
        private readonly Panel _page2 = new Panel();
        private readonly Panel _page3 = new Panel();

        // Footer buttons
        private readonly Panel _footer = new Panel();
        private readonly Button _btnSkip = new Button();
        private readonly Button _btnBack = new Button();
        private readonly Button _btnNext = new Button();

        // Step 1 controls (AI Providers & Language)
        private readonly ComboBox _cboLang = new ComboBox();
        private readonly List<string> _langCodes = new List<string>();
        private readonly CheckBox _chkClaude = new CheckBox();
        private readonly Label _lblClaudeStatus = new Label();
        private readonly Label _lblClaudeDesc = new Label();
        private readonly CheckBox _chkGemini = new CheckBox();
        private readonly Label _lblGeminiStatus = new Label();
        private readonly Label _lblGeminiDesc = new Label();
        private readonly CheckBox _chkOllama = new CheckBox();
        private readonly Label _lblOllamaStatus = new Label();
        private readonly Label _lblOllamaDesc = new Label();
        private readonly Label _lblLang = new Label();

        // Step 2 controls (Theme, Backdrop & Placement)
        private readonly Label _lblTheme = new Label();
        private readonly ComboBox _cboTheme = new ComboBox();
        private readonly Label _lblBackdrop = new Label();
        private readonly ComboBox _cboBackdrop = new ComboBox();
        private readonly CheckBox _chkDock = new CheckBox();
        private readonly Label _lblEdge = new Label();
        private readonly ComboBox _cboEdge = new ComboBox();
        private readonly Label _lblMode = new Label();
        private readonly RadioButton _rbDualCards = new RadioButton();
        private readonly RadioButton _rbPill = new RadioButton();

        // Step 3 controls (Integrations & Quick Start)
        private readonly CheckBox _chkAutostart = new CheckBox();
        private readonly CheckBox _chkCompanion = new CheckBox();
        private readonly Label _lblCompanionDesc = new Label();
        private readonly CheckBox _chkCurrency = new CheckBox();
        private readonly Panel _tipsCard = new Panel();
        private readonly Label _lblTipsTitle = new Label();
        private readonly Label _lblTip1 = new Label();
        private readonly Label _lblTip2 = new Label();
        private readonly Label _lblTip3 = new Label();
        private readonly Label _lblTip4 = new Label();

        public SetupWizardForm(AppConfig cfg)
        {
            _cfg = cfg;
            Text = L.T("wizard.title");
            try { Icon ic = AppIcon.Get(); if (ic != null) Icon = ic; } catch { }
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(620, 560);
            MinimumSize = new Size(580, 520);

            BuildHeader();
            BuildFooter();
            BuildBody();

            BuildPage1();
            BuildPage2();
            BuildPage3();

            ApplyTheme();
            GoToStep(0);
        }

        private void BuildHeader()
        {
            _headerPanel.Dock = DockStyle.Top;
            _headerPanel.AutoSize = true;
            _headerPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _headerPanel.Padding = new Padding(24, 16, 24, 12);
            _headerPanel.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Palette.Border, 1f))
                    e.Graphics.DrawLine(p, 0, _headerPanel.Height - 1, _headerPanel.Width, _headerPanel.Height - 1);
            };

            TableLayoutPanel tHeader = new TableLayoutPanel();
            tHeader.ColumnCount = 1;
            tHeader.AutoSize = true;
            tHeader.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            tHeader.Dock = DockStyle.Top;

            _titleLabel.Text = L.T("wizard.welcome");
            _titleLabel.Font = new Font("Segoe UI Semibold", 13f, FontStyle.Bold);
            _titleLabel.AutoSize = true;
            _titleLabel.Margin = new Padding(0, 0, 0, 4);

            _subtitleLabel.Text = L.T("wizard.welcome.sub");
            _subtitleLabel.Font = new Font("Segoe UI", 8.5f);
            _subtitleLabel.AutoSize = true;
            _subtitleLabel.MaximumSize = new Size(560, 0);
            _subtitleLabel.Margin = new Padding(0, 0, 0, 10);

            FlowLayoutPanel pills = new FlowLayoutPanel();
            pills.FlowDirection = FlowDirection.LeftToRight;
            pills.AutoSize = true;
            pills.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            pills.Margin = new Padding(0, 2, 0, 2);

            InitPill(_stepPill1, L.T("wizard.step1"));
            InitPill(_stepPill2, L.T("wizard.step2"));
            InitPill(_stepPill3, L.T("wizard.step3"));
            pills.Controls.Add(_stepPill1);
            pills.Controls.Add(_stepPill2);
            pills.Controls.Add(_stepPill3);

            tHeader.Controls.Add(_titleLabel);
            tHeader.Controls.Add(_subtitleLabel);
            tHeader.Controls.Add(pills);

            _headerPanel.Controls.Add(tHeader);
            Controls.Add(_headerPanel);
        }

        private void InitPill(Label lbl, string text)
        {
            lbl.Text = text;
            lbl.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            lbl.Padding = new Padding(8, 3, 8, 3);
            lbl.Margin = new Padding(0, 0, 8, 0);
            lbl.AutoSize = true;
        }

        private void BuildFooter()
        {
            _footer.Dock = DockStyle.Bottom;
            _footer.Height = 54;
            _footer.Padding = new Padding(20, 10, 20, 10);
            _footer.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Palette.Border, 1f))
                    e.Graphics.DrawLine(p, 0, 0, _footer.Width, 0);
            };

            _btnSkip.Text = L.T("wizard.btn.skip");
            _btnSkip.AutoSize = true;
            _btnSkip.Dock = DockStyle.Left;
            _btnSkip.Click += delegate { FinishAndSave(); };

            FlowLayoutPanel rightButtons = new FlowLayoutPanel();
            rightButtons.FlowDirection = FlowDirection.RightToLeft;
            rightButtons.AutoSize = true;
            rightButtons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            rightButtons.Dock = DockStyle.Right;

            _btnNext.Text = L.T("wizard.btn.next");
            _btnNext.AutoSize = true;
            _btnNext.Margin = new Padding(6, 0, 0, 0);
            _btnNext.Click += delegate
            {
                if (_step < 2) GoToStep(_step + 1);
                else FinishAndSave();
            };

            _btnBack.Text = L.T("wizard.btn.back");
            _btnBack.AutoSize = true;
            _btnBack.Margin = new Padding(6, 0, 0, 0);
            _btnBack.Click += delegate { if (_step > 0) GoToStep(_step - 1); };

            rightButtons.Controls.Add(_btnNext);
            rightButtons.Controls.Add(_btnBack);

            _footer.Controls.Add(_btnSkip);
            _footer.Controls.Add(rightButtons);
            Controls.Add(_footer);
        }

        private void BuildBody()
        {
            _body.Dock = DockStyle.Fill;
            _body.Padding = new Padding(24, 16, 24, 16);

            _page1.Dock = DockStyle.Fill;
            _page1.AutoScroll = true;
            _page2.Dock = DockStyle.Fill;
            _page2.AutoScroll = true;
            _page3.Dock = DockStyle.Fill;
            _page3.AutoScroll = true;

            _body.Controls.Add(_page1);
            _body.Controls.Add(_page2);
            _body.Controls.Add(_page3);
            Controls.Add(_body);
        }

        private void BuildPage1()
        {
            TableLayoutPanel t = new TableLayoutPanel();
            t.ColumnCount = 1;
            t.AutoSize = true;
            t.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            t.Dock = DockStyle.Top;
            t.Padding = new Padding(0, 0, 12, 0);

            // Language Row
            FlowLayoutPanel langRow = new FlowLayoutPanel();
            langRow.FlowDirection = FlowDirection.LeftToRight;
            langRow.AutoSize = true;
            langRow.Margin = new Padding(0, 0, 0, 12);

            _lblLang.Text = L.T("set.language");
            _lblLang.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
            _lblLang.AutoSize = true;
            _lblLang.Margin = new Padding(0, 4, 12, 0);
            langRow.Controls.Add(_lblLang);

            _cboLang.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboLang.Width = 200;
            _cboLang.Items.Add("English"); _langCodes.Add("en");
            _cboLang.Items.Add("Bahasa Indonesia"); _langCodes.Add("id");
            foreach (LangPackInfo p in LangPacks.Discover()) { _cboLang.Items.Add(p.Name); _langCodes.Add(p.Code); }
            int li = _langCodes.IndexOf(_cfg.Language);
            _cboLang.SelectedIndex = li >= 0 ? li : 0;
            _cboLang.SelectedIndexChanged += delegate
            {
                if (_cboLang.SelectedIndex >= 0 && _cboLang.SelectedIndex < _langCodes.Count)
                {
                    string chosen = _langCodes[_cboLang.SelectedIndex];
                    L.Set(chosen);
                    UpdateAllTexts();
                }
            };
            langRow.Controls.Add(_cboLang);
            t.Controls.Add(langRow);

            // Divider
            Label div = new Label();
            div.BorderStyle = BorderStyle.Fixed3D;
            div.Height = 2;
            div.Dock = DockStyle.Top;
            div.Margin = new Padding(0, 0, 0, 14);
            t.Controls.Add(div);

            // Detection logic
            bool claudeFound = Directory.Exists(Paths.ProjectsDir);
            bool geminiFound = Directory.Exists(Paths.AntigravityBrainDir);

            // Provider 1: Claude Code
            t.Controls.Add(BuildProviderCard(
                _chkClaude, L.T("wizard.claude.title"), _cfg.ScanClaude || claudeFound,
                _lblClaudeStatus, claudeFound ? L.T("wizard.detected") : L.T("wizard.notdetected"), claudeFound,
                _lblClaudeDesc, L.T("wizard.claude.desc")
            ));

            // Provider 2: Google Antigravity
            t.Controls.Add(BuildProviderCard(
                _chkGemini, L.T("wizard.gemini.title"), _cfg.ScanGemini || geminiFound,
                _lblGeminiStatus, geminiFound ? L.T("wizard.detected") : L.T("wizard.notdetected"), geminiFound,
                _lblGeminiDesc, L.T("wizard.gemini.desc")
            ));

            // Provider 3: Ollama
            t.Controls.Add(BuildProviderCard(
                _chkOllama, L.T("wizard.ollama.title"), _cfg.OllamaEnabled,
                _lblOllamaStatus, "Port " + _cfg.OllamaPort, false,
                _lblOllamaDesc, L.T("wizard.ollama.desc")
            ));

            _page1.Controls.Add(t);
        }

        private Panel BuildProviderCard(CheckBox chk, string title, bool isChecked, Label lblStatus, string statusText, bool isDetected, Label lblDesc, string descText)
        {
            Panel card = new Panel();
            card.AutoSize = true;
            card.Dock = DockStyle.Top;
            card.Margin = new Padding(0, 0, 0, 14);

            FlowLayoutPanel topRow = new FlowLayoutPanel();
            topRow.FlowDirection = FlowDirection.LeftToRight;
            topRow.AutoSize = true;
            topRow.Dock = DockStyle.Top;

            chk.Text = title;
            chk.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
            chk.AutoSize = true;
            chk.Checked = isChecked;
            chk.Margin = new Padding(0, 0, 10, 0);
            topRow.Controls.Add(chk);

            lblStatus.Text = statusText;
            lblStatus.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
            lblStatus.ForeColor = isDetected ? Palette.Green : Palette.Dim;
            lblStatus.AutoSize = true;
            lblStatus.Margin = new Padding(0, 2, 0, 0);
            topRow.Controls.Add(lblStatus);

            lblDesc.Text = descText;
            lblDesc.ForeColor = Palette.Dim;
            lblDesc.Font = new Font("Segoe UI", 8.5f);
            lblDesc.AutoSize = true;
            lblDesc.MaximumSize = new Size(540, 0);
            lblDesc.Margin = new Padding(22, 3, 0, 0);
            lblDesc.Dock = DockStyle.Top;

            card.Controls.Add(lblDesc);
            card.Controls.Add(topRow);
            return card;
        }

        private void BuildPage2()
        {
            TableLayoutPanel t = new TableLayoutPanel();
            t.ColumnCount = 2;
            t.AutoSize = true;
            t.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            t.Dock = DockStyle.Top;
            t.Padding = new Padding(0, 0, 12, 0);
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            // Theme row
            _lblTheme.Text = L.T("wizard.theme");
            _lblTheme.Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
            _lblTheme.AutoSize = true;
            _lblTheme.Anchor = AnchorStyles.Left;
            t.Controls.Add(_lblTheme, 0, 0);

            _cboTheme.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboTheme.Width = 220;
            _cboTheme.Items.Add(L.T("set.theme.dark"));
            _cboTheme.Items.Add(L.T("set.theme.light"));
            _cboTheme.Items.Add(L.T("set.theme.system"));
            _cboTheme.SelectedIndex = _cfg.Theme == "light" ? 1 : (_cfg.Theme == "system" ? 2 : 0);
            _cboTheme.Margin = new Padding(0, 4, 0, 8);
            t.Controls.Add(_cboTheme, 1, 0);

            // Backdrop row
            _lblBackdrop.Text = L.T("wizard.backdrop");
            _lblBackdrop.Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
            _lblBackdrop.AutoSize = true;
            _lblBackdrop.Anchor = AnchorStyles.Left;
            t.Controls.Add(_lblBackdrop, 0, 1);

            _cboBackdrop.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboBackdrop.Width = 220;
            _cboBackdrop.Items.Add(L.T("backdrop.acrylic"));
            _cboBackdrop.Items.Add(L.T("backdrop.mica"));
            _cboBackdrop.Items.Add(L.T("backdrop.mica_alt"));
            _cboBackdrop.Items.Add(L.T("backdrop.solid"));
            int bi = Array.IndexOf(Backdrops, _cfg.Backdrop);
            _cboBackdrop.SelectedIndex = bi >= 0 ? bi : 0;
            _cboBackdrop.Margin = new Padding(0, 4, 0, 14);
            t.Controls.Add(_cboBackdrop, 1, 1);

            // Divider row
            Label div = new Label();
            div.BorderStyle = BorderStyle.Fixed3D;
            div.Height = 2;
            div.Dock = DockStyle.Top;
            div.Margin = new Padding(0, 4, 0, 14);
            t.Controls.Add(div, 0, 2);
            t.SetColumnSpan(div, 2);

            // Dock Checkbox row
            _chkDock.Text = L.T("wizard.dock.enable");
            _chkDock.Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
            _chkDock.AutoSize = true;
            _chkDock.Checked = _cfg.DockTop;
            _chkDock.Margin = new Padding(0, 0, 0, 8);
            t.Controls.Add(_chkDock, 0, 3);
            t.SetColumnSpan(_chkDock, 2);

            // Dock Position row
            _lblEdge.Text = L.T("wizard.dock.pos");
            _lblEdge.AutoSize = true;
            _lblEdge.Anchor = AnchorStyles.Left;
            _lblEdge.Margin = new Padding(22, 0, 0, 0);
            t.Controls.Add(_lblEdge, 0, 4);

            _cboEdge.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboEdge.Width = 220;
            _cboEdge.Items.Add(L.T("edge.top"));
            _cboEdge.Items.Add(L.T("edge.top_left"));
            _cboEdge.Items.Add(L.T("edge.top_right"));
            _cboEdge.Items.Add(L.T("edge.bottom"));
            _cboEdge.Items.Add(L.T("edge.bottom_left"));
            _cboEdge.Items.Add(L.T("edge.bottom_right"));
            int ei = Array.IndexOf(Edges, _cfg.DockEdge);
            _cboEdge.SelectedIndex = ei >= 0 ? ei : 0;
            _cboEdge.Margin = new Padding(0, 2, 0, 16);
            t.Controls.Add(_cboEdge, 1, 4);

            // View Mode row
            _lblMode.Text = L.T("wizard.view.mode");
            _lblMode.Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
            _lblMode.AutoSize = true;
            _lblMode.Margin = new Padding(0, 0, 0, 6);
            t.Controls.Add(_lblMode, 0, 5);
            t.SetColumnSpan(_lblMode, 2);

            _rbDualCards.Text = L.T("wizard.view.dual");
            _rbDualCards.AutoSize = true;
            _rbDualCards.Checked = !_cfg.Compact;
            _rbDualCards.Margin = new Padding(22, 0, 0, 4);
            t.Controls.Add(_rbDualCards, 0, 6);
            t.SetColumnSpan(_rbDualCards, 2);

            _rbPill.Text = L.T("wizard.view.pill");
            _rbPill.AutoSize = true;
            _rbPill.Checked = _cfg.Compact;
            _rbPill.Margin = new Padding(22, 0, 0, 8);
            t.Controls.Add(_rbPill, 0, 7);
            t.SetColumnSpan(_rbPill, 2);

            _page2.Controls.Add(t);
        }

        private void BuildPage3()
        {
            TableLayoutPanel t = new TableLayoutPanel();
            t.ColumnCount = 1;
            t.AutoSize = true;
            t.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            t.Dock = DockStyle.Top;
            t.Padding = new Padding(0, 0, 12, 0);

            // Autostart
            _chkAutostart.Text = L.T("wizard.autostart");
            _chkAutostart.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
            _chkAutostart.AutoSize = true;
            _chkAutostart.Checked = true;
            try { _chkAutostart.Checked = Autostart.IsEnabled; } catch { }
            _chkAutostart.Margin = new Padding(0, 0, 0, 10);
            t.Controls.Add(_chkAutostart);

            // Companion
            _chkCompanion.Text = L.T("wizard.companion");
            _chkCompanion.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
            _chkCompanion.AutoSize = true;
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            bool hasIde = Directory.Exists(Path.Combine(userProfile, ".vscode")) || Directory.Exists(Path.Combine(userProfile, ".cursor"));
            _chkCompanion.Checked = hasIde;
            _chkCompanion.Margin = new Padding(0, 0, 0, 2);
            t.Controls.Add(_chkCompanion);

            _lblCompanionDesc.Text = L.T("wizard.companion.desc");
            _lblCompanionDesc.ForeColor = Palette.Dim;
            _lblCompanionDesc.Font = new Font("Segoe UI", 8.5f);
            _lblCompanionDesc.AutoSize = true;
            _lblCompanionDesc.MaximumSize = new Size(540, 0);
            _lblCompanionDesc.Margin = new Padding(22, 0, 0, 12);
            t.Controls.Add(_lblCompanionDesc);

            // Currency
            _chkCurrency.Text = L.T("wizard.currency");
            _chkCurrency.Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
            _chkCurrency.AutoSize = true;
            _chkCurrency.Checked = _cfg.CurrencyCustomEnabled || string.Equals(_cfg.Language, "id", StringComparison.OrdinalIgnoreCase);
            _chkCurrency.Margin = new Padding(0, 0, 0, 16);
            t.Controls.Add(_chkCurrency);

            // Tips Card
            _tipsCard.AutoSize = true;
            _tipsCard.Dock = DockStyle.Top;
            _tipsCard.Padding = new Padding(14, 10, 14, 10);
            _tipsCard.Margin = new Padding(0, 0, 0, 8);
            _tipsCard.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Palette.Border, 1f))
                    e.Graphics.DrawRectangle(p, 0, 0, _tipsCard.Width - 1, _tipsCard.Height - 1);
            };

            TableLayoutPanel tTips = new TableLayoutPanel();
            tTips.ColumnCount = 1;
            tTips.AutoSize = true;
            tTips.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            tTips.Dock = DockStyle.Top;

            _lblTipsTitle.Text = L.T("wizard.tips.title");
            _lblTipsTitle.Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
            _lblTipsTitle.AutoSize = true;
            _lblTipsTitle.Margin = new Padding(0, 0, 0, 6);
            tTips.Controls.Add(_lblTipsTitle);

            InitTipLabel(_lblTip1, L.T("wizard.tip.hotkey"));
            InitTipLabel(_lblTip2, L.T("wizard.tip.hover"));
            InitTipLabel(_lblTip3, L.T("wizard.tip.doubleclick"));
            InitTipLabel(_lblTip4, L.T("wizard.tip.tray"));

            tTips.Controls.Add(_lblTip1);
            tTips.Controls.Add(_lblTip2);
            tTips.Controls.Add(_lblTip3);
            tTips.Controls.Add(_lblTip4);

            _tipsCard.Controls.Add(tTips);
            t.Controls.Add(_tipsCard);

            _page3.Controls.Add(t);
        }

        private void InitTipLabel(Label lbl, string text)
        {
            lbl.Text = text;
            lbl.Font = new Font("Segoe UI", 8.5f);
            lbl.ForeColor = Palette.Dim;
            lbl.AutoSize = true;
            lbl.MaximumSize = new Size(510, 0);
            lbl.Margin = new Padding(0, 0, 0, 4);
        }

        private void GoToStep(int s)
        {
            _step = Math.Max(0, Math.Min(2, s));
            _page1.Visible = (_step == 0);
            _page2.Visible = (_step == 1);
            _page3.Visible = (_step == 2);

            _btnBack.Enabled = (_step > 0);
            _btnNext.Text = (_step == 2) ? L.T("wizard.btn.finish") : L.T("wizard.btn.next");

            UpdatePillAppearance(_stepPill1, _step == 0);
            UpdatePillAppearance(_stepPill2, _step == 1);
            UpdatePillAppearance(_stepPill3, _step == 2);
        }

        private void UpdatePillAppearance(Label pill, bool active)
        {
            pill.BackColor = active ? Palette.Accent : Palette.Track;
            pill.ForeColor = active ? Color.White : Palette.Dim;
        }

        private void UpdateAllTexts()
        {
            Text = L.T("wizard.title");
            _titleLabel.Text = L.T("wizard.welcome");
            _subtitleLabel.Text = L.T("wizard.welcome.sub");
            _stepPill1.Text = L.T("wizard.step1");
            _stepPill2.Text = L.T("wizard.step2");
            _stepPill3.Text = L.T("wizard.step3");

            _lblLang.Text = L.T("set.language");

            _chkClaude.Text = L.T("wizard.claude.title");
            _lblClaudeDesc.Text = L.T("wizard.claude.desc");

            _chkGemini.Text = L.T("wizard.gemini.title");
            _lblGeminiDesc.Text = L.T("wizard.gemini.desc");

            _chkOllama.Text = L.T("wizard.ollama.title");
            _lblOllamaDesc.Text = L.T("wizard.ollama.desc");

            _lblTheme.Text = L.T("wizard.theme");
            _lblBackdrop.Text = L.T("wizard.backdrop");
            _chkDock.Text = L.T("wizard.dock.enable");
            _lblEdge.Text = L.T("wizard.dock.pos");
            _lblMode.Text = L.T("wizard.view.mode");
            _rbDualCards.Text = L.T("wizard.view.dual");
            _rbPill.Text = L.T("wizard.view.pill");

            _chkAutostart.Text = L.T("wizard.autostart");
            _chkCompanion.Text = L.T("wizard.companion");
            _lblCompanionDesc.Text = L.T("wizard.companion.desc");
            _chkCurrency.Text = L.T("wizard.currency");

            _lblTipsTitle.Text = L.T("wizard.tips.title");
            _lblTip1.Text = L.T("wizard.tip.hotkey");
            _lblTip2.Text = L.T("wizard.tip.hover");
            _lblTip3.Text = L.T("wizard.tip.doubleclick");
            _lblTip4.Text = L.T("wizard.tip.tray");

            _btnSkip.Text = L.T("wizard.btn.skip");
            _btnBack.Text = L.T("wizard.btn.back");
            _btnNext.Text = (_step == 2) ? L.T("wizard.btn.finish") : L.T("wizard.btn.next");
        }

        private void ApplyTheme()
        {
            BackColor = Palette.Bg;
            ForeColor = Palette.Text;

            _headerPanel.BackColor = Palette.Surface;
            _titleLabel.ForeColor = Palette.Text;
            _subtitleLabel.ForeColor = Palette.Dim;

            _footer.BackColor = Palette.Surface;

            _tipsCard.BackColor = Palette.Surface;
            _lblTipsTitle.ForeColor = Palette.Text;
            _lblTip1.ForeColor = Palette.Dim;
            _lblTip2.ForeColor = Palette.Dim;
            _lblTip3.ForeColor = Palette.Dim;
            _lblTip4.ForeColor = Palette.Dim;

            ThemeButton(_btnSkip, false);
            ThemeButton(_btnBack, false);
            ThemeButton(_btnNext, true);

            UpdatePillAppearance(_stepPill1, _step == 0);
            UpdatePillAppearance(_stepPill2, _step == 1);
            UpdatePillAppearance(_stepPill3, _step == 2);
        }

        private void ThemeButton(Button b, bool accent)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Palette.Border;
            b.Font = new Font("Segoe UI", 9f);
            b.Padding = new Padding(12, 4, 12, 4);
            if (accent)
            {
                b.BackColor = Palette.Accent;
                b.ForeColor = Color.White;
                b.FlatAppearance.BorderSize = 0;
            }
            else
            {
                b.BackColor = Palette.Surface;
                b.ForeColor = Palette.Text;
                b.FlatAppearance.BorderSize = 1;
            }
        }

        private void FinishAndSave()
        {
            _cfg.FirstRunCompleted = true;

            if (_cboLang.SelectedIndex >= 0 && _cboLang.SelectedIndex < _langCodes.Count)
                _cfg.Language = _langCodes[_cboLang.SelectedIndex];

            _cfg.ScanClaude = _chkClaude.Checked;
            _cfg.ScanGemini = _chkGemini.Checked;
            _cfg.OllamaEnabled = _chkOllama.Checked;

            _cfg.Theme = _cboTheme.SelectedIndex == 1 ? "light" : (_cboTheme.SelectedIndex == 2 ? "system" : "dark");
            if (_cboBackdrop.SelectedIndex >= 0 && _cboBackdrop.SelectedIndex < Backdrops.Length)
                _cfg.Backdrop = Backdrops[_cboBackdrop.SelectedIndex];

            _cfg.DockTop = _chkDock.Checked;
            if (_cboEdge.SelectedIndex >= 0 && _cboEdge.SelectedIndex < Edges.Length)
                _cfg.DockEdge = Edges[_cboEdge.SelectedIndex];

            _cfg.Compact = _rbPill.Checked;

            if (_chkCurrency.Checked)
            {
                _cfg.CurrencyCustomEnabled = true;
                _cfg.CurrencySymbol = "Rp";
                _cfg.CurrencyRate = 16200;
            }

            try
            {
                if (Autostart.IsEnabled != _chkAutostart.Checked)
                    Autostart.Set(_chkAutostart.Checked);
            }
            catch (Exception ex)
            {
                Log.Write("wizard.autostart", ex);
            }

            if (_chkCompanion.Checked)
            {
                string paths, error;
                CompanionInstaller.Install(out paths, out error);
            }

            _cfg.Save();
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
