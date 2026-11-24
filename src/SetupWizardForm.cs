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
        private readonly CheckBox _chkGemini = new CheckBox();
        private readonly Label _lblGeminiStatus = new Label();
        private readonly CheckBox _chkOllama = new CheckBox();
        private readonly Label _lblOllamaStatus = new Label();

        // Step 2 controls (Theme, Backdrop & Placement)
        private readonly ComboBox _cboTheme = new ComboBox();
        private readonly ComboBox _cboBackdrop = new ComboBox();
        private readonly CheckBox _chkDock = new CheckBox();
        private readonly ComboBox _cboEdge = new ComboBox();
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
            ClientSize = new Size(580, 520);

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
            _headerPanel.Height = 85;
            _headerPanel.Padding = new Padding(20, 14, 20, 8);
            _headerPanel.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Palette.Border, 1f))
                    e.Graphics.DrawLine(p, 0, _headerPanel.Height - 1, _headerPanel.Width, _headerPanel.Height - 1);
            };

            _titleLabel.Text = L.T("wizard.welcome");
            _titleLabel.Font = new Font("Segoe UI Semibold", 13f, FontStyle.Bold);
            _titleLabel.AutoSize = true;
            _titleLabel.Location = new Point(20, 12);

            _subtitleLabel.Text = L.T("wizard.welcome.sub");
            _subtitleLabel.Font = new Font("Segoe UI", 8.5f);
            _subtitleLabel.AutoSize = true;
            _subtitleLabel.Location = new Point(22, 38);

            FlowLayoutPanel pills = new FlowLayoutPanel();
            pills.FlowDirection = FlowDirection.LeftToRight;
            pills.AutoSize = true;
            pills.Location = new Point(20, 60);

            InitPill(_stepPill1, L.T("wizard.step1"));
            InitPill(_stepPill2, L.T("wizard.step2"));
            InitPill(_stepPill3, L.T("wizard.step3"));
            pills.Controls.Add(_stepPill1);
            pills.Controls.Add(_stepPill2);
            pills.Controls.Add(_stepPill3);

            _headerPanel.Controls.Add(_titleLabel);
            _headerPanel.Controls.Add(_subtitleLabel);
            _headerPanel.Controls.Add(pills);
            Controls.Add(_headerPanel);
        }

        private void InitPill(Label lbl, string text)
        {
            lbl.Text = text;
            lbl.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            lbl.Padding = new Padding(6, 2, 6, 2);
            lbl.Margin = new Padding(0, 0, 8, 0);
            lbl.AutoSize = true;
        }

        private void BuildFooter()
        {
            _footer.Dock = DockStyle.Bottom;
            _footer.Height = 52;
            _footer.Padding = new Padding(16, 10, 16, 10);
            _footer.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Palette.Border, 1f))
                    e.Graphics.DrawLine(p, 0, 0, _footer.Width, 0);
            };

            _btnSkip.Text = L.T("wizard.btn.skip");
            _btnSkip.AutoSize = true;
            _btnSkip.Location = new Point(16, 12);
            _btnSkip.Click += delegate { FinishAndSave(); };

            _btnBack.Text = L.T("wizard.btn.back");
            _btnBack.AutoSize = true;
            _btnBack.Location = new Point(370, 12);
            _btnBack.Click += delegate { if (_step > 0) GoToStep(_step - 1); };

            _btnNext.Text = L.T("wizard.btn.next");
            _btnNext.AutoSize = true;
            _btnNext.Location = new Point(460, 12);
            _btnNext.Click += delegate
            {
                if (_step < 2) GoToStep(_step + 1);
                else FinishAndSave();
            };

            _footer.Controls.Add(_btnSkip);
            _footer.Controls.Add(_btnBack);
            _footer.Controls.Add(_btnNext);
            Controls.Add(_footer);
        }

        private void BuildBody()
        {
            _body.Dock = DockStyle.Fill;
            _body.Padding = new Padding(24, 16, 24, 16);

            _page1.Dock = DockStyle.Fill;
            _page2.Dock = DockStyle.Fill;
            _page3.Dock = DockStyle.Fill;

            _body.Controls.Add(_page1);
            _body.Controls.Add(_page2);
            _body.Controls.Add(_page3);
            Controls.Add(_body);
        }

        private void BuildPage1()
        {
            int y = 4;

            // Language Selector
            Label lblLang = new Label();
            lblLang.Text = L.T("set.language");
            lblLang.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
            lblLang.AutoSize = true;
            lblLang.Location = new Point(0, y);
            _page1.Controls.Add(lblLang);

            _cboLang.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboLang.Width = 200;
            _cboLang.Location = new Point(220, y - 2);
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
            _page1.Controls.Add(_cboLang);
            y += 36;

            // Divider
            Label div1 = new Label(); div1.BorderStyle = BorderStyle.Fixed3D; div1.Height = 2; div1.Width = 510; div1.Location = new Point(0, y);
            _page1.Controls.Add(div1);
            y += 14;

            // Detection logic
            bool claudeFound = Directory.Exists(Paths.ProjectsDir);
            bool geminiFound = Directory.Exists(Paths.AntigravityBrainDir);

            // Card 1: Claude Code
            _chkClaude.Text = L.T("wizard.claude.title");
            _chkClaude.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
            _chkClaude.AutoSize = true;
            _chkClaude.Location = new Point(4, y);
            _chkClaude.Checked = _cfg.ScanClaude || claudeFound;
            _page1.Controls.Add(_chkClaude);

            _lblClaudeStatus.Text = claudeFound ? L.T("wizard.detected") : L.T("wizard.notdetected");
            _lblClaudeStatus.ForeColor = claudeFound ? Palette.Green : Palette.Dim;
            _lblClaudeStatus.AutoSize = true;
            _lblClaudeStatus.Location = new Point(260, y + 2);
            _page1.Controls.Add(_lblClaudeStatus);
            y += 22;

            Label lblClaudeDesc = new Label();
            lblClaudeDesc.Text = L.T("wizard.claude.desc");
            lblClaudeDesc.ForeColor = Palette.Dim;
            lblClaudeDesc.AutoSize = true;
            lblClaudeDesc.Location = new Point(24, y);
            _page1.Controls.Add(lblClaudeDesc);
            y += 38;

            // Card 2: Google Antigravity
            _chkGemini.Text = L.T("wizard.gemini.title");
            _chkGemini.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
            _chkGemini.AutoSize = true;
            _chkGemini.Location = new Point(4, y);
            _chkGemini.Checked = _cfg.ScanGemini || geminiFound;
            _page1.Controls.Add(_chkGemini);

            _lblGeminiStatus.Text = geminiFound ? L.T("wizard.detected") : L.T("wizard.notdetected");
            _lblGeminiStatus.ForeColor = geminiFound ? Palette.Green : Palette.Dim;
            _lblGeminiStatus.AutoSize = true;
            _lblGeminiStatus.Location = new Point(260, y + 2);
            _page1.Controls.Add(_lblGeminiStatus);
            y += 22;

            Label lblGeminiDesc = new Label();
            lblGeminiDesc.Text = L.T("wizard.gemini.desc");
            lblGeminiDesc.ForeColor = Palette.Dim;
            lblGeminiDesc.AutoSize = true;
            lblGeminiDesc.Location = new Point(24, y);
            _page1.Controls.Add(lblGeminiDesc);
            y += 38;

            // Card 3: Ollama
            _chkOllama.Text = L.T("wizard.ollama.title");
            _chkOllama.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
            _chkOllama.AutoSize = true;
            _chkOllama.Location = new Point(4, y);
            _chkOllama.Checked = _cfg.OllamaEnabled;
            _page1.Controls.Add(_chkOllama);

            _lblOllamaStatus.Text = "Port " + _cfg.OllamaPort;
            _lblOllamaStatus.ForeColor = Palette.Dim;
            _lblOllamaStatus.AutoSize = true;
            _lblOllamaStatus.Location = new Point(260, y + 2);
            _page1.Controls.Add(_lblOllamaStatus);
            y += 22;

            Label lblOllamaDesc = new Label();
            lblOllamaDesc.Text = L.T("wizard.ollama.desc");
            lblOllamaDesc.ForeColor = Palette.Dim;
            lblOllamaDesc.AutoSize = true;
            lblOllamaDesc.Location = new Point(24, y);
            _page1.Controls.Add(lblOllamaDesc);
        }

        private void BuildPage2()
        {
            int y = 8;

            // Theme selector
            Label lblTheme = new Label();
            lblTheme.Text = L.T("wizard.theme");
            lblTheme.Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
            lblTheme.AutoSize = true;
            lblTheme.Location = new Point(4, y + 3);
            _page2.Controls.Add(lblTheme);

            _cboTheme.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboTheme.Width = 220;
            _cboTheme.Location = new Point(220, y);
            _cboTheme.Items.Add(L.T("set.theme.dark"));
            _cboTheme.Items.Add(L.T("set.theme.light"));
            _cboTheme.Items.Add(L.T("set.theme.system"));
            _cboTheme.SelectedIndex = _cfg.Theme == "light" ? 1 : (_cfg.Theme == "system" ? 2 : 0);
            _page2.Controls.Add(_cboTheme);
            y += 40;

            // Backdrop selector
            Label lblBackdrop = new Label();
            lblBackdrop.Text = L.T("wizard.backdrop");
            lblBackdrop.Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
            lblBackdrop.AutoSize = true;
            lblBackdrop.Location = new Point(4, y + 3);
            _page2.Controls.Add(lblBackdrop);

            _cboBackdrop.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboBackdrop.Width = 220;
            _cboBackdrop.Location = new Point(220, y);
            _cboBackdrop.Items.Add(L.T("backdrop.acrylic"));
            _cboBackdrop.Items.Add(L.T("backdrop.mica"));
            _cboBackdrop.Items.Add(L.T("backdrop.mica_alt"));
            _cboBackdrop.Items.Add(L.T("backdrop.solid"));
            int bi = Array.IndexOf(Backdrops, _cfg.Backdrop);
            _cboBackdrop.SelectedIndex = bi >= 0 ? bi : 0;
            _page2.Controls.Add(_cboBackdrop);
            y += 44;

            // Divider
            Label div2 = new Label(); div2.BorderStyle = BorderStyle.Fixed3D; div2.Height = 2; div2.Width = 510; div2.Location = new Point(0, y);
            _page2.Controls.Add(div2);
            y += 16;

            // Placement: Dock
            _chkDock.Text = L.T("wizard.dock.enable");
            _chkDock.Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
            _chkDock.AutoSize = true;
            _chkDock.Location = new Point(4, y);
            _chkDock.Checked = _cfg.DockTop;
            _page2.Controls.Add(_chkDock);
            y += 32;

            Label lblEdge = new Label();
            lblEdge.Text = L.T("wizard.dock.pos");
            lblEdge.AutoSize = true;
            lblEdge.Location = new Point(24, y + 3);
            _page2.Controls.Add(lblEdge);

            _cboEdge.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboEdge.Width = 200;
            _cboEdge.Location = new Point(220, y);
            _cboEdge.Items.Add(L.T("edge.top"));
            _cboEdge.Items.Add(L.T("edge.top_left"));
            _cboEdge.Items.Add(L.T("edge.top_right"));
            _cboEdge.Items.Add(L.T("edge.bottom"));
            _cboEdge.Items.Add(L.T("edge.bottom_left"));
            _cboEdge.Items.Add(L.T("edge.bottom_right"));
            int ei = Array.IndexOf(Edges, _cfg.DockEdge);
            _cboEdge.SelectedIndex = ei >= 0 ? ei : 0;
            _page2.Controls.Add(_cboEdge);
            y += 44;

            // Mode: Dual Cards vs Mini Pill
            Label lblMode = new Label();
            lblMode.Text = L.T("wizard.view.mode");
            lblMode.Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
            lblMode.AutoSize = true;
            lblMode.Location = new Point(4, y);
            _page2.Controls.Add(lblMode);
            y += 24;

            _rbDualCards.Text = L.T("wizard.view.dual");
            _rbDualCards.AutoSize = true;
            _rbDualCards.Location = new Point(24, y);
            _rbDualCards.Checked = !_cfg.Compact;
            _page2.Controls.Add(_rbDualCards);
            y += 26;

            _rbPill.Text = L.T("wizard.view.pill");
            _rbPill.AutoSize = true;
            _rbPill.Location = new Point(24, y);
            _rbPill.Checked = _cfg.Compact;
            _page2.Controls.Add(_rbPill);
        }

        private void BuildPage3()
        {
            int y = 8;

            // Autostart Checkbox
            _chkAutostart.Text = L.T("wizard.autostart");
            _chkAutostart.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
            _chkAutostart.AutoSize = true;
            _chkAutostart.Location = new Point(4, y);
            _chkAutostart.Checked = true;
            try { _chkAutostart.Checked = Autostart.IsEnabled; } catch { }
            _page3.Controls.Add(_chkAutostart);
            y += 34;

            // Companion Extension Checkbox
            _chkCompanion.Text = L.T("wizard.companion");
            _chkCompanion.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
            _chkCompanion.AutoSize = true;
            _chkCompanion.Location = new Point(4, y);
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            bool hasIde = Directory.Exists(Path.Combine(userProfile, ".vscode")) || Directory.Exists(Path.Combine(userProfile, ".cursor"));
            _chkCompanion.Checked = hasIde;
            _page3.Controls.Add(_chkCompanion);
            y += 22;

            _lblCompanionDesc.Text = L.T("wizard.companion.desc");
            _lblCompanionDesc.ForeColor = Palette.Dim;
            _lblCompanionDesc.AutoSize = true;
            _lblCompanionDesc.Location = new Point(24, y);
            _page3.Controls.Add(_lblCompanionDesc);
            y += 34;

            // Currency Checkbox
            _chkCurrency.Text = L.T("wizard.currency");
            _chkCurrency.Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
            _chkCurrency.AutoSize = true;
            _chkCurrency.Location = new Point(4, y);
            _chkCurrency.Checked = _cfg.CurrencyCustomEnabled || string.Equals(_cfg.Language, "id", StringComparison.OrdinalIgnoreCase);
            _page3.Controls.Add(_chkCurrency);
            y += 36;

            // Tips Card
            _tipsCard.Location = new Point(4, y);
            _tipsCard.Size = new Size(516, 128);
            _tipsCard.Padding = new Padding(12);
            _tipsCard.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Palette.Border, 1f))
                    e.Graphics.DrawRectangle(p, 0, 0, _tipsCard.Width - 1, _tipsCard.Height - 1);
            };

            _lblTipsTitle.Text = L.T("wizard.tips.title");
            _lblTipsTitle.Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
            _lblTipsTitle.AutoSize = true;
            _lblTipsTitle.Location = new Point(10, 8);
            _tipsCard.Controls.Add(_lblTipsTitle);

            _lblTip1.Text = L.T("wizard.tip.hotkey");
            _lblTip1.AutoSize = true;
            _lblTip1.Location = new Point(12, 30);
            _tipsCard.Controls.Add(_lblTip1);

            _lblTip2.Text = L.T("wizard.tip.hover");
            _lblTip2.AutoSize = true;
            _lblTip2.Location = new Point(12, 50);
            _tipsCard.Controls.Add(_lblTip2);

            _lblTip3.Text = L.T("wizard.tip.doubleclick");
            _lblTip3.AutoSize = true;
            _lblTip3.Location = new Point(12, 70);
            _tipsCard.Controls.Add(_lblTip3);

            _lblTip4.Text = L.T("wizard.tip.tray");
            _lblTip4.AutoSize = true;
            _lblTip4.Location = new Point(12, 90);
            _tipsCard.Controls.Add(_lblTip4);

            _page3.Controls.Add(_tipsCard);
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

            _chkClaude.Text = L.T("wizard.claude.title");
            _chkGemini.Text = L.T("wizard.gemini.title");
            _chkOllama.Text = L.T("wizard.ollama.title");

            _chkDock.Text = L.T("wizard.dock.enable");
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
