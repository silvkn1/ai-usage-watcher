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
        public string WebhookUrl { get; set; }

        // Tombol taskbar & progress bar
        public bool ShowInTaskbar { get; set; }

        // Pindai log Claude Code (~/.claude/projects) & Antigravity (~/.gemini/antigravity)
        public bool ScanClaude { get; set; }
        public bool ScanGemini { get; set; }
        public long AntigravityQuota5hTokens { get; set; }
        public long AntigravityQuota7dTokens { get; set; }

        // Detektor LLM Lokal Ollama
        public bool OllamaEnabled { get; set; }
        public int OllamaPort { get; set; }

        // Efek visual latar belakang: "solid", "acrylic", "mica", "mica-alt"
        public string Backdrop { get; set; }

        // Penyiapan awal (Setup Wizard) sudah diselesaikan
        public bool FirstRunCompleted { get; set; }

        public AppConfig()
        {
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
                        if (c.OpacityPercent < 30 || c.OpacityPercent > 100) c.OpacityPercent = 90;
                        if (c.ProbeMinMinutes < 5 || c.ProbeMinMinutes > 240) c.ProbeMinMinutes = 10;
                        if (string.IsNullOrEmpty(c.ProbeModel)) c.ProbeModel = "claude-haiku-5-5";
                        if (c.Theme != "light" && c.Theme != "system") c.Theme = "dark";
                        if (!LangPacks.Exists(c.Language)) c.Language = "en";   // "en", "id", atau kode paket bahasa terpasang
                        if (c.HistoryDays != 30) c.HistoryDays = 14;
                        if (c.DockEdge != "bottom" && c.DockEdge != "left" && c.DockEdge != "right" &&
                            c.DockEdge != "top-left" && c.DockEdge != "top-right" &&
                            c.DockEdge != "bottom-left" && c.DockEdge != "bottom-right" &&
                            c.DockEdge != "top-center" && c.DockEdge != "bottom-center")
                            c.DockEdge = "top";
                        if (c.DockMonitor == null) c.DockMonitor = "";
                        if (double.IsNaN(c.BudgetDaily) || c.BudgetDaily < 0) c.BudgetDaily = 0;
                        if (double.IsNaN(c.BudgetMonthly) || c.BudgetMonthly < 0) c.BudgetMonthly = 0;
                        if (c.HookNotifyMinSeconds < 0 || c.HookNotifyMinSeconds > 3600) c.HookNotifyMinSeconds = 30;
                        if (AlertEngine.ParseThresholds(c.AlertThresholds) == null) c.AlertThresholds = "80,95";
                        if (string.IsNullOrWhiteSpace(c.Hotkey)) c.Hotkey = "Win+Alt+U";
                        if (string.IsNullOrEmpty(c.CurrencySymbol)) c.CurrencySymbol = "Rp";
                        if (c.CurrencyRate <= 0 || double.IsNaN(c.CurrencyRate)) c.CurrencyRate = 16200;
                        if (c.BurnRateThresholdTokens <= 0) c.BurnRateThresholdTokens = 100000;
                        if (c.WebhookUrl == null) c.WebhookUrl = "";
                        if (c.OllamaPort <= 0 || c.OllamaPort > 65535) c.OllamaPort = 11434;
                        if (c.Backdrop != "solid" && c.Backdrop != "acrylic" && c.Backdrop != "mica" && c.Backdrop != "mica-alt")
                            c.Backdrop = DwmBackdrop.IsWindows11 ? "acrylic" : "solid";
                        c.ShowInTaskbar = false;
                        c.FirstRunCompleted = true;
                        return c;
                    }
                }
            }
            catch (Exception ex) { Log.Write("config.load", ex); }
            return new AppConfig();
        }

        public void Save()
        {
            try
            {
                Paths.Ensure();
                string tmp = Paths.ConfigFile + ".tmp";
                File.WriteAllText(tmp, J.Ser.Serialize(this), new UTF8Encoding(false));
                if (File.Exists(Paths.ConfigFile)) File.Delete(Paths.ConfigFile);
                File.Move(tmp, Paths.ConfigFile);
            }
            catch (Exception ex) { Log.Write("config.save", ex); }
        }

        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ClaudeUsageWidget.v1");

        public void SetAdminKey(string plain)
        {
            if (string.IsNullOrEmpty(plain)) { AdminKeyProtected = null; return; }
            byte[] enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
            AdminKeyProtected = Convert.ToBase64String(enc);
        }

        public string GetAdminKey()
        {
            if (string.IsNullOrEmpty(AdminKeyProtected)) return null;
            try
            {
                byte[] dec = ProtectedData.Unprotect(Convert.FromBase64String(AdminKeyProtected), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(dec);
            }
            catch (Exception ex) { Log.Write("config.unprotect", ex); return null; }
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
