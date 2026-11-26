using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace ClaudeUsageWidget
{
    /// <summary>Pengaturan pengguna, disimpan di %LOCALAPPDATA%\ClaudeUsageWidget\config.json.
    /// Admin key TIDAK pernah disimpan plaintext: dienkripsi DPAPI (CurrentUser).</summary>
    public class AppConfig
    {
        public int FloatX { get; set; }
        public int FloatY { get; set; }
        public bool HasPosition { get; set; }
        public int OpacityPercent { get; set; }
        public bool Compact { get; set; }
        public bool ClickThrough { get; set; }
        public bool ShowFloating { get; set; }
        public bool DockTop { get; set; }          // tempel di tepi atas: ringkas, lengkap saat hover
        public bool AlwaysOnTop { get; set; }      // di atas jendela lain (default aktif)
        public bool HideOnFullscreen { get; set; } // sembunyi saat ada aplikasi layar penuh di monitor yang sama (default aktif)
        public string Theme { get; set; }          // "dark" (default) atau "light"
        public string Language { get; set; }       // "en" (default) atau "id"
        public bool ShowRemaining { get; set; }    // tampilkan sisa (bukan terpakai)
        public bool AlertsEnabled { get; set; }    // notifikasi ambang (default aktif)
        public string AlertThresholds { get; set; } // persen terpakai, mis. "80,95"
        public bool ResetNotify { get; set; }      // notifikasi saat limit reset (default mati)
        public bool ScanWsl { get; set; }          // pindai log distro WSL yang sedang berjalan (default mati)
        public string ExtraClaudeDirs { get; set; } // folder konfigurasi Claude tambahan, dipisah ';'
        public bool HotkeyEnabled { get; set; }    // hotkey global tampil/sembunyi (default mati)
        public string Hotkey { get; set; }
        public int HistoryDays { get; set; }       // rentang grafik riwayat: 14 atau 30
        public double BudgetDaily { get; set; }    // anggaran harian estimasi (USD, harga API); 0 = mati
        public double BudgetMonthly { get; set; }  // anggaran bulanan estimasi (USD); 0 = mati
        public bool ShowProjects { get; set; }     // tampilkan 5 proyek teratas (nama hanya di layar; default mati)
        public string DockEdge { get; set; }       // "top" | "bottom" | "left" | "right"
        public string DockMonitor { get; set; }    // Screen.DeviceName; kosong = monitor utama
        public int DockCenterY { get; set; }
        public bool HasDockY { get; set; }
        public bool HookNotifyStop { get; set; }   // notifikasi saat respons panjang selesai (butuh hook terpasang)
        public int HookNotifyMinSeconds { get; set; }
        public bool HookNotifyInput { get; set; }  // notifikasi saat Claude butuh input/izin
        public int DockCenterX { get; set; }
        public bool HasDockX { get; set; }

        public bool AdminEnabled { get; set; }
        public string AdminKeyProtected { get; set; }
        public string AdminApiKeyIds { get; set; }     // dipisah koma, opsional
        public string AdminWorkspaceIds { get; set; }  // dipisah koma, opsional

        // Probe limit via Claude Code CLI headless (opt-in; memakai sedikit kuota tiap probe).
        public bool ProbeEnabled { get; set; }
        public int ProbeMinMinutes { get; set; }
        public string ProbeModel { get; set; }
        // Cermin rate_limits hasil probe ke file JSON lain (mis. file status mod Windhawk). Default mati.
        public bool MirrorEnabled { get; set; }
        public string MirrorFilePath { get; set; }

        // Konversi mata uang kustom (mis. IDR / Rupiah)
        public bool CurrencyCustomEnabled { get; set; }
        public string CurrencySymbol { get; set; }
        public double CurrencyRate { get; set; }
        public bool CurrencyShowBoth { get; set; }

        // Suara alert & notifikasi
        public bool AudioAlertsEnabled { get; set; }

        // Alert batas konteks sesi & lonjakan pemakaian (burn rate)
        public bool ContextAlertEnabled { get; set; }
        public bool BurnRateAlertEnabled { get; set; }
        public long BurnRateThresholdTokens { get; set; }

        // Integrasi Webhook (Discord/Slack/Telegram)
        public bool WebhookEnabled { get; set; }
        public string WebhookUrl { get; set; } // legacy plaintext (dimigrasi ke DPAPI saat Load/Save)
        public string WebhookUrlProtected { get; set; }

        // Tombol taskbar & progress bar
        public bool ShowInTaskbar { get; set; }

        // Pindai log Claude Code (~/.claude/projects), Antigravity (~/.gemini/antigravity), & Codex (~/.codex/sessions)
        public bool ScanClaude { get; set; }
        public bool ScanGemini { get; set; }
        public bool ScanCodex { get; set; }
        public long AntigravityQuota5hTokens { get; set; }
        public long AntigravityQuota7dTokens { get; set; }

        // Detektor LLM Lokal Ollama
        public bool OllamaEnabled { get; set; }
        public int OllamaPort { get; set; }

        // Efek visual latar belakang: "solid", "acrylic", "mica", "mica-alt"
        public string Backdrop { get; set; }

        // Penyiapan awal (Setup Wizard) sudah diselesaikan
        public bool FirstRunCompleted { get; set; }

        // Pembaruan otomatis (GitHub Releases)
        public bool AutoUpdateCheck { get; set; }

        // Layout kartu 3+ provider, Mode Mini HUD Pill, & Bar Konteks Sesi
        public string CardLayout { get; set; }         // "auto", "2col", "3col"
        public string MiniPillMode { get; set; }       // "all", "highest", "cost_runway"
        public bool ShowContextBar { get; set; }

        // Ekspansi scanner CLI lain
        public bool ScanGeminiCli { get; set; }
        public bool ScanOpenCode { get; set; }

        // Quiet Hours & Cooldown Alert (menit)
        public bool QuietHoursEnabled { get; set; }
        public int QuietStartHour { get; set; }
        public int QuietEndHour { get; set; }
        public int AlertCooldownMinutes { get; set; }

        // Scheduled Webhook Digest
        public bool DigestEnabled { get; set; }
        public string DigestSchedule { get; set; }     // "daily", "weekly"
        public int DigestHour { get; set; }
        public string LastDigestKey { get; set; }

        public AppConfig()
        {
            AutoUpdateCheck = true;
            CardLayout = "auto";
            MiniPillMode = "all";
            ShowContextBar = true;
            ScanGeminiCli = true;
            ScanOpenCode = true;
            QuietHoursEnabled = false;
            QuietStartHour = 22;
            QuietEndHour = 7;
            AlertCooldownMinutes = 15;
            DigestEnabled = false;
            DigestSchedule = "daily";
            DigestHour = 18;
            LastDigestKey = "";
            OpacityPercent = 90;
            ShowFloating = true;
            ProbeMinMinutes = 10;
            ProbeModel = "claude-haiku-5-5";
            AlwaysOnTop = true;
            HideOnFullscreen = true;
            Theme = "dark";
            Language = "en";
            AlertsEnabled = true;
            AlertThresholds = "80,95";
            ResetNotify = true;
            HotkeyEnabled = true;
            Hotkey = "Win+Alt+U";
            HistoryDays = 14;
            DockEdge = "top";
            DockMonitor = "";
            HookNotifyStop = true;
            HookNotifyMinSeconds = 30;
            HookNotifyInput = true;

            CurrencyCustomEnabled = false;
            CurrencySymbol = "Rp";
            CurrencyRate = 16200;
            CurrencyShowBoth = true;
            AudioAlertsEnabled = true;
            ContextAlertEnabled = true;
            BurnRateAlertEnabled = true;
            BurnRateThresholdTokens = 100000;
            WebhookEnabled = false;
            WebhookUrl = "";
            ShowInTaskbar = false;
            ScanClaude = true;
            ScanGemini = true;
            ScanCodex = true;
            AntigravityQuota5hTokens = 1000000;
            AntigravityQuota7dTokens = 10000000;
            OllamaEnabled = true;
            OllamaPort = 11434;
            Backdrop = DwmBackdrop.IsWindows11 ? "acrylic" : "solid";
        }

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(Paths.ConfigFile))
                {
                    AppConfig c = J.Ser.Deserialize<AppConfig>(File.ReadAllText(Paths.ConfigFile, Encoding.UTF8));
                    if (c != null)
                    {
                        c.Normalize();
                        return c;
                    }
                }
            }
            catch (Exception ex) { Log.Write("config.load", ex); }
            return new AppConfig();
        }

        public void Normalize()
        {
            if (OpacityPercent < 30 || OpacityPercent > 100) OpacityPercent = 90;
            if (ProbeMinMinutes < 5 || ProbeMinMinutes > 240) ProbeMinMinutes = 10;
            if (string.IsNullOrEmpty(ProbeModel)) ProbeModel = "claude-haiku-5-5";
            if (Theme != "light" && Theme != "system") Theme = "dark";
            if (!LangPacks.Exists(Language)) Language = "en";   // "en", "id", atau kode paket bahasa terpasang
            if (HistoryDays != 30) HistoryDays = 14;
            if (DockEdge != "bottom" && DockEdge != "left" && DockEdge != "right" &&
                DockEdge != "top-left" && DockEdge != "top-right" &&
                DockEdge != "bottom-left" && DockEdge != "bottom-right" &&
                DockEdge != "top-center" && DockEdge != "bottom-center")
                DockEdge = "top";
            if (DockMonitor == null) DockMonitor = "";
            if (double.IsNaN(BudgetDaily) || BudgetDaily < 0) BudgetDaily = 0;
            if (double.IsNaN(BudgetMonthly) || BudgetMonthly < 0) BudgetMonthly = 0;
            if (HookNotifyMinSeconds < 0 || HookNotifyMinSeconds > 3600) HookNotifyMinSeconds = 30;
            if (AlertEngine.ParseThresholds(AlertThresholds) == null) AlertThresholds = "80,95";
            if (string.IsNullOrWhiteSpace(Hotkey)) Hotkey = "Win+Alt+U";
            if (string.IsNullOrEmpty(CurrencySymbol)) CurrencySymbol = "Rp";
            if (CurrencyRate <= 0 || double.IsNaN(CurrencyRate)) CurrencyRate = 16200;
            if (BurnRateThresholdTokens <= 0) BurnRateThresholdTokens = 100000;
            if (!string.IsNullOrEmpty(WebhookUrl))
            {
                SetWebhookUrl(WebhookUrl);
                WebhookUrl = null;
            }
            if (OllamaPort <= 0 || OllamaPort > 65535) OllamaPort = 11434;
            if (Backdrop != "solid" && Backdrop != "acrylic" && Backdrop != "mica" && Backdrop != "mica-alt")
                Backdrop = DwmBackdrop.IsWindows11 ? "acrylic" : "solid";
            if (CardLayout != "2col" && CardLayout != "3col") CardLayout = "auto";
            if (MiniPillMode != "highest" && MiniPillMode != "cost_runway") MiniPillMode = "all";
            if (QuietStartHour < 0 || QuietStartHour > 23) QuietStartHour = 22;
            if (QuietEndHour < 0 || QuietEndHour > 23) QuietEndHour = 7;
            if (AlertCooldownMinutes < 1 || AlertCooldownMinutes > 1440) AlertCooldownMinutes = 15;
            if (DigestSchedule != "weekly") DigestSchedule = "daily";
            if (DigestHour < 0 || DigestHour > 23) DigestHour = 18;
            if (LastDigestKey == null) LastDigestKey = "";
            ShowInTaskbar = false;
        }

        private static readonly System.Collections.Generic.HashSet<string> SafeProfileProperties =
            new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal)
        {
            "FloatX", "FloatY", "HasPosition", "OpacityPercent", "Compact", "ClickThrough", "ShowFloating",
            "DockTop", "AlwaysOnTop", "HideOnFullscreen", "Theme", "Language", "ShowRemaining",
            "AlertsEnabled", "AlertThresholds", "ResetNotify", "HotkeyEnabled", "Hotkey",
            "HistoryDays", "BudgetDaily", "BudgetMonthly", "ShowProjects",
            "DockEdge", "DockMonitor", "DockCenterY", "HasDockY", "DockCenterX", "HasDockX",
            "HookNotifyStop", "HookNotifyMinSeconds", "HookNotifyInput",
            "ProbeEnabled", "ProbeMinMinutes", "ProbeModel",
            "CurrencyCustomEnabled", "CurrencySymbol", "CurrencyRate", "CurrencyShowBoth",
            "AudioAlertsEnabled", "ContextAlertEnabled", "BurnRateAlertEnabled", "BurnRateThresholdTokens",
            "ScanClaude", "ScanGemini", "ScanCodex", "ScanGeminiCli", "ScanOpenCode",
            "AntigravityQuota5hTokens", "AntigravityQuota7dTokens", "OllamaEnabled",
            "Backdrop", "CardLayout", "MiniPillMode", "ShowContextBar",
            "QuietHoursEnabled", "QuietStartHour", "QuietEndHour", "AlertCooldownMinutes",
            "DigestSchedule", "DigestHour"
        };

        public string ExportProfileJson()
        {
            AppConfig clone = J.Ser.Deserialize<AppConfig>(J.Ser.Serialize(this));
            clone.AdminKeyProtected = null;
            clone.WebhookUrlProtected = null;
            clone.WebhookUrl = null;
            clone.AdminEnabled = false;
            clone.AdminApiKeyIds = "";
            clone.AdminWorkspaceIds = "";
            clone.WebhookEnabled = false;
            clone.MirrorEnabled = false;
            clone.MirrorFilePath = "";
            clone.ExtraClaudeDirs = "";
            clone.ScanWsl = false;
            clone.OllamaPort = 11434;
            clone.AutoUpdateCheck = true;
            clone.LastDigestKey = "";
            return J.Ser.Serialize(clone);
        }

        public static AppConfig ImportProfileJson(string json, AppConfig preserveSecretsFrom)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            AppConfig raw = J.Ser.Deserialize<AppConfig>(json);
            if (raw == null) return null;
            AppConfig dst = preserveSecretsFrom != null
                ? J.Ser.Deserialize<AppConfig>(J.Ser.Serialize(preserveSecretsFrom))
                : new AppConfig();
            dst.ApplyProfileFrom(raw);
            return dst;
        }

        public void ApplyProfileFrom(AppConfig src)
        {
            if (src == null) return;
            foreach (System.Reflection.PropertyInfo p in typeof(AppConfig).GetProperties())
            {
                if (!p.CanRead || !p.CanWrite) continue;
                if (!SafeProfileProperties.Contains(p.Name)) continue;
                p.SetValue(this, p.GetValue(src, null), null);
            }
            Normalize();
        }

        public void Save()
        {
            try
            {
                Paths.Ensure();
                WebhookUrl = null;   // jangan pernah simpan URL webhook plaintext
                AtomicFile.Write(Paths.ConfigFile, J.Ser.Serialize(this));
            }
            catch (Exception ex) { Log.Write("config.save", ex); }
        }

        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ClaudeUsageWidget.v1");

        private static string ProtectStr(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return null;
            byte[] enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(enc);
        }

        private static string UnprotectStr(string protectedB64, string logContext)
        {
            if (string.IsNullOrEmpty(protectedB64)) return null;
            try
            {
                byte[] dec = ProtectedData.Unprotect(Convert.FromBase64String(protectedB64), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(dec);
            }
            catch (Exception ex) { Log.Write(logContext, ex); return null; }
        }

        public void SetAdminKey(string plain) { AdminKeyProtected = ProtectStr(plain); }
        public string GetAdminKey() { return UnprotectStr(AdminKeyProtected, "config.unprotect"); }

        public void SetWebhookUrl(string plain)
        {
            WebhookUrl = null;
            WebhookUrlProtected = ProtectStr(plain);
        }

        public string GetWebhookUrl()
        {
            string dec = UnprotectStr(WebhookUrlProtected, "config.unprotect.webhook");
            if (!string.IsNullOrEmpty(dec)) return dec;
            return WebhookUrl ?? "";
        }
    }

    /// <summary>Autostart via HKCU\...\Run (tanpa hak admin).</summary>
    internal static class Autostart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Name = "AIUsageWatcher";
        private const string LegacyName = "ClaudeUsageWidget";

        public static bool IsEnabled
        {
            get
            {
                if (Paths.Portable) return false;   // mode portabel tidak menulis registry
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                    return k != null && (k.GetValue(Name) != null || k.GetValue(LegacyName) != null);
            }
        }

        public static void Set(bool on)
        {
            if (Paths.Portable) return;
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on)
                {
                    k.SetValue(Name, "\"" + System.Windows.Forms.Application.ExecutablePath + "\"");
                    if (k.GetValue(LegacyName) != null) k.DeleteValue(LegacyName);
                }
                else
                {
                    if (k.GetValue(Name) != null) k.DeleteValue(Name);
                    if (k.GetValue(LegacyName) != null) k.DeleteValue(LegacyName);
                }
            }
        }
    }
}
