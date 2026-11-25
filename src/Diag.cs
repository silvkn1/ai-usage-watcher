using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ClaudeUsageWidget
{
    /// <summary>
    /// Ringkasan diagnostik untuk laporan masalah. Tidak memuat admin key, nama proyek, isi percakapan, atau path pengguna
    /// (SID, nama pengguna, dan folder profil disamarkan). Konfigurasi hanya berupa nilai non-rahasia.
    /// </summary>
    internal static class Diag
    {
        public static string Redact(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            try
            {
                string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(profile)) s = Regex.Replace(s, Regex.Escape(profile), "%USERPROFILE%", RegexOptions.IgnoreCase);
                string user = Environment.UserName;
                if (!string.IsNullOrEmpty(user) && user.Length >= 3) s = Regex.Replace(s, Regex.Escape(user), "<user>", RegexOptions.IgnoreCase);
                s = Regex.Replace(s, @"S-1-5-21(-\d+){4}", "S-1-5-21-<sid>");
            }
            catch { }
            return s;
        }

        private static string YesNo(bool b) { return b ? "yes" : "no"; }

        private static string Window(string name, LimitWindow w)
        {
            if (w == null || !w.Available) return name + ": unavailable";
            string reset = w.ResetsAt == DateTime.MinValue ? "?" : (w.IsStale ? "already reset" : "in " + Fmt.Until(w.ResetsAt));
            return string.Format(CultureInfo.InvariantCulture, "{0}: {1:0}% used, resets {2}", name, w.UsedPercent, reset);
        }

        private static string CliKind()
        {
            string p = CliProbe.FindClaude();
            if (p == null) return "not found";
            if (p.IndexOf("Claude-Data", StringComparison.OrdinalIgnoreCase) >= 0) return "desktop app bundle";
            return p.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ? "npm (claude.cmd)" : "native (claude.exe)";
        }

        private static string StatuslineState()
        {
            try
            {
                string f = Path.Combine(Paths.ClaudeDir, "settings.json");
                if (!File.Exists(f)) return "no settings.json";
                string cmd = J.Str(J.Obj(J.Parse(File.ReadAllText(f, Encoding.UTF8)), "statusLine"), "command") ?? "";
                return cmd.IndexOf("cuw-statusline", StringComparison.OrdinalIgnoreCase) >= 0 ? "wrapper installed" : (cmd.Length == 0 ? "none" : "other command");
            }
            catch { return "unreadable"; }
        }

        public static string Build(AppConfig cfg, RateLimitSnapshot rl, UsageSnapshot u, string probeFailed, OrgCostResult org, int historyDaysStored)
        {
            StringBuilder b = new StringBuilder();
            b.AppendLine(L.AppFullName + " diagnostics");
            b.AppendLine("version: " + typeof(Diag).Assembly.GetName().Version);
            b.AppendLine("os: " + Environment.OSVersion.VersionString + (Environment.Is64BitOperatingSystem ? " (64-bit)" : " (32-bit)") + "; .NET " + Environment.Version);
            b.AppendLine("portable: " + YesNo(Paths.Portable) + "; language: " + cfg.Language + "; theme: " + cfg.Theme);
            b.AppendLine("display: dock=" + YesNo(cfg.DockTop) + " edge=" + cfg.DockEdge + " monitor=" + (string.IsNullOrEmpty(cfg.DockMonitor) ? "primary" : "custom")
                + " compact=" + YesNo(cfg.Compact) + " remaining=" + YesNo(cfg.ShowRemaining) + " onTop=" + YesNo(cfg.AlwaysOnTop)
                + " hideOnFullscreen=" + YesNo(cfg.HideOnFullscreen) + " historyDays=" + cfg.HistoryDays);
            b.AppendLine("alerts: " + YesNo(cfg.AlertsEnabled) + " thresholds=" + cfg.AlertThresholds + " resetNotify=" + YesNo(cfg.ResetNotify)
                + " budgetDaily=" + (cfg.BudgetDaily > 0 ? "set" : "off") + " budgetMonthly=" + (cfg.BudgetMonthly > 0 ? "set" : "off"));
            b.AppendLine("data: projects=" + YesNo(cfg.ShowProjects) + " wsl=" + YesNo(cfg.ScanWsl)
                + " extraFolders=" + (string.IsNullOrWhiteSpace(cfg.ExtraClaudeDirs) ? 0 : cfg.ExtraClaudeDirs.Split(';').Length)
                + " hotkey=" + YesNo(cfg.HotkeyEnabled) + " historyStored=" + historyDaysStored + " days");
            b.AppendLine("probe: " + YesNo(cfg.ProbeEnabled) + " every=" + cfg.ProbeMinMinutes + "m mirror=" + (cfg.MirrorEnabled ? "on" : "off")
                + " cli=" + CliKind() + "; admin api: " + YesNo(cfg.AdminEnabled) + " keyStored=" + YesNo(!string.IsNullOrEmpty(cfg.AdminKeyProtected)));
            b.AppendLine("integration: statusline=" + StatuslineState() + "; hooks=" + (HookInstaller.IsInstalled() ? "installed" : "not installed")
                + "; notifyStop=" + YesNo(cfg.HookNotifyStop) + " (>=" + cfg.HookNotifyMinSeconds + "s) notifyInput=" + YesNo(cfg.HookNotifyInput));
            if (rl != null)
            {
                b.AppendLine(Window("5h", rl.FiveHour));
                b.AppendLine(Window("7d", rl.SevenDay));
                b.AppendLine("limit snapshot: " + (rl.FileFound ? "captured " + Fmt.Clock(rl.CapturedAt) + ", source " + rl.Source : "none"));
            }
            if (u != null)
                b.AppendLine(string.Format(CultureInfo.InvariantCulture, "usage: ready={0} today={1} tokens/${2:0.00} week={3} tokens/${4:0.00} skippedLines={5} lastRecord={6}",
                    YesNo(u.Ready), u.TodayTotal.Total, u.TodayTotal.Cost, u.WeekTotal.Total, u.WeekTotal.Cost, u.SkippedLines, Fmt.Clock(u.LastRecord)));
            if (!string.IsNullOrEmpty(probeFailed)) b.AppendLine("last probe error: " + probeFailed);
            if (org != null && !org.Ok) b.AppendLine("last admin api error: " + org.Error);
            if (WebhookSender.FailCount > 0) b.AppendLine("webhook failures: " + WebhookSender.FailCount + (string.IsNullOrEmpty(WebhookSender.LastError) ? "" : " (" + WebhookSender.LastError + ")"));
            b.AppendLine("--- log tail ---");
            try
            {
                if (File.Exists(Paths.LogFile))
                {
                    string[] lines = File.ReadAllLines(Paths.LogFile, Encoding.UTF8);
                    for (int i = Math.Max(0, lines.Length - 25); i < lines.Length; i++) b.AppendLine(lines[i]);
                }
            }
            catch { }
            return Redact(b.ToString());
        }
    }
}
