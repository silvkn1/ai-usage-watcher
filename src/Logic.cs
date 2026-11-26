using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ClaudeUsageWidget
{
    /// <summary>Opsi tampilan global yang dibaca widget, tray, dan tooltip.</summary>
    internal static class Opts
    {
        public static bool ShowRemaining;   // tampilkan sisa, bukan terpakai
        public static bool CurrencyCustomEnabled;
        public static string CurrencySymbol = "Rp";
        public static double CurrencyRate = 16200;
        public static bool CurrencyShowBoth = true;
        public static string CardLayout = "auto";
        public static string MiniPillMode = "all";
        public static bool ShowContextBar = true;
    }

    internal static class ContextLimits
    {
        public static long WindowForModel(string model)
        {
            if (string.IsNullOrEmpty(model)) return 200000L;
            if (model.IndexOf("gemini", StringComparison.OrdinalIgnoreCase) >= 0) return 1000000L;
            if (model.IndexOf("[1m]", StringComparison.OrdinalIgnoreCase) >= 0 ||
                model.IndexOf("-1m", StringComparison.OrdinalIgnoreCase) >= 0) return 1000000L;
            if (model.IndexOf("gpt-5", StringComparison.OrdinalIgnoreCase) >= 0 ||
                model.IndexOf("codex", StringComparison.OrdinalIgnoreCase) >= 0) return 400000L;
            return 200000L;
        }

        public static string FormatCapShort(long window)
        {
            if (window >= 1000000L) return "1M";
            if (window >= 400000L) return "400k";
            return "200k";
        }
    }

    internal static class QuietHours
    {
        public static bool IsActive(AppConfig cfg, DateTime now)
        {
            if (cfg == null || !cfg.QuietHoursEnabled) return false;
            int h = now.Hour;
            int start = cfg.QuietStartHour;
            int end = cfg.QuietEndHour;
            if (start == end) return true;
            if (start < end) return h >= start && h < end;
            return h >= start || h < end;
        }
    }

    internal static class DigestScheduler
    {
        public static string ComputePeriodKey(string schedule, DateTime now)
        {
            if (string.Equals(schedule, "weekly", StringComparison.OrdinalIgnoreCase))
            {
                Calendar cal = CultureInfo.InvariantCulture.Calendar;
                int wk = cal.GetWeekOfYear(now, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
                return string.Format(CultureInfo.InvariantCulture, "{0:0000}-W{1:00}", now.Year, wk);
            }
            return now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        }

        public static bool ShouldSend(AppConfig cfg, DateTime now)
        {
            if (cfg == null || !cfg.DigestEnabled || !cfg.WebhookEnabled) return false;
            if (QuietHours.IsActive(cfg, now)) return false;
            if (string.Equals(cfg.DigestSchedule, "weekly", StringComparison.OrdinalIgnoreCase))
            {
                if (now.DayOfWeek != DayOfWeek.Sunday || now.Hour < cfg.DigestHour) return false;
            }
            else if (now.Hour < cfg.DigestHour)
            {
                return false;
            }
            string key = ComputePeriodKey(cfg.DigestSchedule, now);
            return !string.Equals(cfg.LastDigestKey, key, StringComparison.Ordinal);
        }

        public static string BuildDigestText(RateLimitSnapshot limits, UsageSnapshot usage, HistoryStore history, AppConfig cfg)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(StatusFormatter.Format(limits, usage,
                cfg == null || cfg.ScanClaude,
                cfg == null || cfg.ScanGemini,
                cfg != null && cfg.ScanCodex,
                OllamaProbe.Current));
            if (history != null)
            {
                DayStat[] last7 = history.Series(7);
                double sum7 = 0; long tok7 = 0;
                for (int i = 0; i < last7.Length; i++) { sum7 += last7[i].Cost; tok7 += last7[i].Tokens; }
                sb.AppendLine(string.Format("• 7d Total: {0} · {1}", Fmt.Usd(sum7), Fmt.Tokens(tok7)));
                sb.AppendLine(string.Format("• Month-to-Date: {0}", Fmt.Usd(history.MonthToDateCost(DateTime.Now))));
            }
            return sb.ToString().TrimEnd();
        }
    }

    internal static class CommandParser
    {
        public static string ParseCommandName(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                Dictionary<string, object> d = J.Parse(json);
                string cmd = J.Str(d, "cmd") ?? J.Str(d, "command");
                return string.IsNullOrWhiteSpace(cmd) ? null : cmd.Trim().ToLowerInvariant();
            }
            catch { return null; }
        }
    }

    internal static class ThemeUtil
    {
        /// <summary>theme: "dark", "light", atau "system" (ikuti pengaturan aplikasi Windows).</summary>
        public static bool ResolveLight(string theme)
        {
            if (theme == "light") return true;
            if (theme == "system") return SystemUsesLightApps();
            return false;
        }

        public static bool SystemUsesLightApps()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    return v is int && (int)v == 1;
                }
            }
            catch { return false; }
        }

        public static Color Blend(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }
    }

    /// <summary>Laju pemakaian: seberapa jauh jendela berjalan dan prakiraan kapan limit tercapai.</summary>
    internal static class Pace
    {
        public const double FiveHourSecs = 18000, SevenDaySecs = 604800;

        /// <summary>Pecahan jendela yang sudah berlalu pada waktu 'at' (0..1); NaN bila waktu reset tak diketahui.</summary>
        public static double ElapsedAt(LimitWindow w, DateTime at, double windowSecs)
        {
            if (w.ResetsAt == DateTime.MinValue) return double.NaN;
            double f = 1 - (w.ResetsAt - at).TotalSeconds / windowSecs;
            return Math.Max(0, Math.Min(1, f));
        }

        /// <summary>
        /// Prakiraan dihitung dari saat snapshot ditangkap (persentase berlaku pada waktu itu), lalu dikurangi
        /// waktu yang sudah lewat. Disembunyikan bila data terlalu dini/sedikit, snapshot basi, atau limit tidak akan
        /// tercapai sebelum reset.
        /// </summary>
        public static bool TryForecast(LimitWindow w, DateTime capturedAt, double windowSecs, bool stale, out TimeSpan untilHit)
        {
            untilHit = TimeSpan.Zero;
            if (!w.Available || w.IsStale || stale || w.ResetsAt == DateTime.MinValue || capturedAt == DateTime.MinValue) return false;
            double e = ElapsedAt(w, capturedAt, windowSecs);
            if (double.IsNaN(e) || e < 0.10 || w.UsedPercent < 5 || w.UsedPercent >= 100) return false;
            double rate = w.UsedPercent / (e * windowSecs);              // persen per detik
            double secsToHit = (100 - w.UsedPercent) / rate;
            DateTime hit = capturedAt.AddSeconds(secsToHit);
            if (hit >= w.ResetsAt) return false;
            TimeSpan left = hit - DateTime.Now;
            untilHit = left < TimeSpan.Zero ? TimeSpan.Zero : left;
            return true;
        }
    }

    internal class Alert
    {
        public string Window;        // "five_hour" atau "seven_day"
        public int Threshold;        // persen terpakai yang dilewati (0 untuk notifikasi reset)
        public bool IsReset;
        public double Used;
        public DateTime ResetsAt;
    }

    /// <summary>
    /// Alert ambang & reset, sekali per lompatan. Status per jendela (kunci: waktu reset, toleransi 10 menit agar
    /// pergantian sumber snapshot tidak mengaktifkan ulang alert). Saat start, ambang yang sudah terlewati ditandai
    /// diam-diam. Notifikasi reset hanya untuk jendela yang pernah melewati ambang.
    /// </summary>
    internal class AlertEngine
    {
        private class St { public DateTime ResetsAt; public HashSet<int> Fired = new HashSet<int>(); public bool Crossed, ResetNotified; }
        private readonly Dictionary<string, St> _s = new Dictionary<string, St>();
        private bool _baselined;

        public List<Alert> Evaluate(RateLimitSnapshot snap, int[] thresholds, bool resetNotify)
        {
            List<Alert> r = new List<Alert>();
            if (snap == null) return r;
            bool silent = !_baselined;
            if (snap.FileFound)
            {
                Check("five_hour", snap.FiveHour, thresholds, resetNotify, silent, r);
                Check("seven_day", snap.SevenDay, thresholds, resetNotify, silent, r);
            }
            if (snap.AntigravityFiveHour != null && snap.AntigravityFiveHour.Available)
                Check("antigravity_five_hour", snap.AntigravityFiveHour, thresholds, resetNotify, silent, r);
            if (snap.AntigravitySevenDay != null && snap.AntigravitySevenDay.Available)
                Check("antigravity_seven_day", snap.AntigravitySevenDay, thresholds, resetNotify, silent, r);
            if (snap.CodexFiveHour != null && snap.CodexFiveHour.Available)
                Check("codex_five_hour", snap.CodexFiveHour, thresholds, resetNotify, silent, r);
            if (snap.CodexSevenDay != null && snap.CodexSevenDay.Available)
                Check("codex_seven_day", snap.CodexSevenDay, thresholds, resetNotify, silent, r);
            _baselined = true;
            return r;
        }

        private void Check(string name, LimitWindow w, int[] th, bool resetNotify, bool silent, List<Alert> r)
        {
            if (!w.Available) return;
            St st;
            _s.TryGetValue(name, out st);

            if (w.IsStale)   // jendela sudah berakhir
            {
                if (st != null)
                {
                    if (st.Crossed && !st.ResetNotified && resetNotify && !silent) r.Add(ResetAlert(name, st));
                    st.ResetNotified = true;
                }
                return;
            }

            bool same = st != null && (w.ResetsAt == DateTime.MinValue
                ? st.ResetsAt == DateTime.MinValue
                : st.ResetsAt != DateTime.MinValue && Math.Abs((w.ResetsAt - st.ResetsAt).TotalMinutes) < 10);
            if (!same)
            {
                if (st != null && st.Crossed && !st.ResetNotified && resetNotify && !silent) r.Add(ResetAlert(name, st));
                st = new St(); st.ResetsAt = w.ResetsAt;
                _s[name] = st;
            }

            int top = 0;
            int[] sorted = (int[])th.Clone();
            Array.Sort(sorted);
            foreach (int t in sorted)
                if (w.UsedPercent >= t && st.Fired.Add(t)) top = t;
            if (top > 0)
            {
                st.Crossed = true;
                if (!silent) { Alert a = new Alert(); a.Window = name; a.Threshold = top; a.Used = w.UsedPercent; a.ResetsAt = w.ResetsAt; r.Add(a); }
            }
            else if (w.UsedPercent >= 30)
            {
                st.Crossed = true;
            }
        }

        private static Alert ResetAlert(string name, St st)
        {
            Alert a = new Alert(); a.Window = name; a.IsReset = true; a.ResetsAt = st.ResetsAt;
            return a;
        }

        /// <summary>"80, 95" -> [80,95]; null bila ada nilai di luar 1..100 atau lebih dari 4 nilai atau kosong.</summary>
        public static int[] ParseThresholds(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            List<int> l = new List<int>();
            foreach (string p in text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int v;
                if (!int.TryParse(p.Trim(), out v) || v < 1 || v > 100) return null;
                if (!l.Contains(v)) l.Add(v);
            }
            if (l.Count == 0 || l.Count > 4) return null;
            l.Sort();
            return l.ToArray();
        }
    }

    internal class BudgetAlert { public bool Monthly; public int Percent; public double Cost, Budget; }

    /// <summary>
    /// Peringatan anggaran biaya (ESTIMASI setara harga API, bukan tagihan; untuk pelanggan langganan hanya sebagai ukuran
    /// pemakaian). Ambang 80% dan 100%, satu kali per hari/bulan. Saat start, ambang yang sudah terlewati ditandai diam-diam.
    /// </summary>
    internal class BudgetEngine
    {
        private readonly HashSet<string> _fired = new HashSet<string>();
        private bool _baselined;

        public List<BudgetAlert> Evaluate(double todayCost, double monthCost, double dailyBudget, double monthlyBudget, DateTime now)
        {
            List<BudgetAlert> r = new List<BudgetAlert>();
            bool silent = !_baselined;
            if (dailyBudget > 0) Check(false, "d" + now.ToString("yyyyMMdd"), todayCost, dailyBudget, silent, r);
            if (monthlyBudget > 0) Check(true, "m" + now.ToString("yyyyMM"), monthCost, monthlyBudget, silent, r);
            _baselined = true;
            return r;
        }

        private void Check(bool monthly, string period, double cost, double budget, bool silent, List<BudgetAlert> r)
        {
            int top = 0;
            foreach (int t in new[] { 80, 100 })
                if (cost >= budget * t / 100.0 && _fired.Add(period + ":" + t)) top = t;
            if (top > 0 && !silent)
            {
                BudgetAlert a = new BudgetAlert(); a.Monthly = monthly; a.Percent = top; a.Cost = cost; a.Budget = budget;
                r.Add(a);
            }
        }
    }

    /// <summary>Teks tooltip tray. Maksimal 63 karakter (batas NotifyIcon.Text di .NET Framework).</summary>
    internal static class TrayText
    {
        public static string Pct(LimitWindow w)
        {
            if (!w.Available) return L.T("na");
            if (w.IsStale) return L.T("reset.short");
            int used = (int)Math.Round(w.UsedPercent);
            return Opts.ShowRemaining ? L.T("pct.left", 100 - used) : used + "%";
        }

        /// <summary>
        /// Isi ikon tray: angka limit 5 jam. Bila limit mingguan sudah penuh (>=100% terpakai), ikon menampilkan limit
        /// mingguan (ditandai cincin putih). Angka mengikuti mode terpakai/sisa; warna menurut persen terpakai.
        /// </summary>
        public static void IconSpec(RateLimitSnapshot limits, out string text, out Color badge, out bool weekly)
        {
            IconSpec(limits, true, true, false, out text, out badge, out weekly);
        }

        public static void IconSpec(RateLimitSnapshot limits, bool scanClaude, bool scanGemini, out string text, out Color badge, out bool weekly)
        {
            IconSpec(limits, scanClaude, scanGemini, false, out text, out badge, out weekly);
        }

        public static void IconSpec(RateLimitSnapshot limits, bool scanClaude, bool scanGemini, bool scanCodex, out string text, out Color badge, out bool weekly)
        {
            LimitWindow w5 = null, w7 = null;
            if (limits != null)
            {
                if (scanClaude) PickHigherWindow(limits.FiveHour, limits.SevenDay, ref w5, ref w7);
                if (scanGemini) PickHigherWindow(limits.AntigravityFiveHour, limits.AntigravitySevenDay, ref w5, ref w7);
                if (scanCodex) PickHigherWindow(limits.CodexFiveHour, limits.CodexSevenDay, ref w5, ref w7);
            }
            weekly = w7 != null && w7.Available && !w7.IsStale && Math.Round(w7.UsedPercent) >= 100;
            LimitWindow w = weekly ? w7 : w5;
            bool show = w != null && w.Available && !w.IsStale;
            int used = show ? (int)Math.Round(w.UsedPercent) : 0;
            int v = Math.Max(0, Math.Min(100, Opts.ShowRemaining ? 100 - used : used));
            text = show ? v.ToString() : "--";
            badge = show ? TrayIconPainter.StatusColor(w.UsedPercent) : TrayIconPainter.StGray;
        }

        private static void PickHigherWindow(LimitWindow cand5, LimitWindow cand7, ref LimitWindow w5, ref LimitWindow w7)
        {
            if (cand5 == null || !cand5.Available) return;
            if (w5 == null || !w5.Available || w5.IsStale || (!cand5.IsStale && cand5.UsedPercent > w5.UsedPercent))
            {
                w5 = cand5;
                w7 = cand7;
            }
        }

        private static string Line(LimitWindow w, string withReset, string noReset)
        {
            if (w == null || !w.Available) return L.T(noReset, L.T("na"));
            if (w.IsStale) return L.T(noReset, L.T("reset.short"));
            string pct = Pct(w);
            if (w.ResetsAt != DateTime.MinValue) return L.T(withReset, pct, Fmt.Until(w.ResetsAt), Fmt.Clock(w.ResetsAt));
            return L.T(noReset, pct);
        }

        private static string ProviderSummary(string name, LimitWindow w5, LimitWindow w7, bool shortPrefix)
        {
            string prefix;
            if (shortPrefix)
            {
                if (name.StartsWith("A", StringComparison.OrdinalIgnoreCase)) prefix = "AG: ";
                else if (name.StartsWith("Codex", StringComparison.OrdinalIgnoreCase)) prefix = "CX: ";
                else prefix = "C: ";
            }
            else
            {
                prefix = name + ": ";
            }
            string s5 = L.T("tip.l5n", Pct(w5));
            if (!shortPrefix && w5 != null && w5.Available && !w5.IsStale && w5.ResetsAt != DateTime.MinValue)
                s5 += " (" + Fmt.Until(w5.ResetsAt) + ")";
            string s7 = L.T("tip.l7n", Pct(w7));
            if (!shortPrefix && w7 != null && w7.Available && !w7.IsStale && w7.ResetsAt != DateTime.MinValue)
                s7 += " (" + Fmt.Until(w7.ResetsAt) + ")";
            return prefix + s5 + " · " + s7;
        }

        /// <summary>Baris 1: 5h, baris 2: 7d (persen, sisa waktu, dan jam reset), baris 3: galat probe terakhir atau biaya hari ini.
        /// maxLen: 127 bila batas tooltip bisa diperluas, selain itu 63 (batas NotifyIcon.Text).</summary>
        public static string Tooltip(RateLimitSnapshot limits, UsageSnapshot usage, string probeFailed, int maxLen)
        {
            return Tooltip(limits, usage, probeFailed, maxLen, true, true, false);
        }

        public static string Tooltip(RateLimitSnapshot limits, UsageSnapshot usage, string probeFailed, int maxLen, bool scanClaude, bool scanGemini)
        {
            return Tooltip(limits, usage, probeFailed, maxLen, scanClaude, scanGemini, false);
        }

        public static string Tooltip(RateLimitSnapshot limits, UsageSnapshot usage, string probeFailed, int maxLen, bool scanClaude, bool scanGemini, bool scanCodex)
        {
            List<string> lines = new List<string>();
            bool hasCodexData = scanCodex && ((usage != null && usage.CodexWeek.Total > 0) ||
                (limits != null && ((limits.CodexFiveHour != null && limits.CodexFiveHour.Available) ||
                                    (limits.CodexSevenDay != null && limits.CodexSevenDay.Available))));
            int count = (scanClaude ? 1 : 0) + (scanGemini ? 1 : 0) + (scanCodex && (hasCodexData || (!scanClaude && !scanGemini)) ? 1 : 0);

            if (count >= 2)
            {
                bool shortPrefix = maxLen <= 63;
                if (scanClaude)
                    lines.Add(ProviderSummary("Claude", limits != null ? limits.FiveHour : null, limits != null ? limits.SevenDay : null, shortPrefix));
                if (scanGemini)
                    lines.Add(ProviderSummary("Antigravity", limits != null ? limits.AntigravityFiveHour : null, limits != null ? limits.AntigravitySevenDay : null, shortPrefix));
                if (scanCodex && (hasCodexData || (!scanClaude && !scanGemini)))
                    lines.Add(ProviderSummary("Codex", limits != null ? limits.CodexFiveHour : null, limits != null ? limits.CodexSevenDay : null, shortPrefix));
            }
            else if (scanClaude)
            {
                LimitWindow w5 = limits != null ? limits.FiveHour : null;
                LimitWindow w7 = limits != null ? limits.SevenDay : null;
                if (maxLen > 63)
                {
                    lines.Add("Claude Code:");
                    lines.Add("  " + Line(w5, "tip.l5", "tip.l5n"));
                    lines.Add("  " + Line(w7, "tip.l7", "tip.l7n"));
                }
                else
                {
                    lines.Add(ProviderSummary("Claude", w5, w7, true));
                }
            }
            else if (scanGemini)
            {
                LimitWindow w5 = limits != null ? limits.AntigravityFiveHour : null;
                LimitWindow w7 = limits != null ? limits.AntigravitySevenDay : null;
                if (maxLen > 63)
                {
                    lines.Add("Antigravity:");
                    lines.Add("  " + Line(w5, "tip.l5", "tip.l5n"));
                    lines.Add("  " + Line(w7, "tip.l7", "tip.l7n"));
                }
                else
                {
                    lines.Add(ProviderSummary("Antigravity", w5, w7, true));
                }
            }
            else if (scanCodex)
            {
                LimitWindow w5 = limits != null ? limits.CodexFiveHour : null;
                LimitWindow w7 = limits != null ? limits.CodexSevenDay : null;
                if (maxLen > 63 && ((w5 != null && w5.Available) || (w7 != null && w7.Available)))
                {
                    lines.Add("Codex CLI:");
                    lines.Add("  " + Line(w5, "tip.l5", "tip.l5n"));
                    lines.Add("  " + Line(w7, "tip.l7", "tip.l7n"));
                }
                else
                {
                    lines.Add("Codex CLI: " + Fmt.Tokens(usage != null ? usage.CodexToday.Total : 0) + " · " + Fmt.UsdShort(usage != null ? usage.CodexToday.Cost : 0));
                }
            }
            else
            {
                lines.Add(L.AppName);
                lines.Add(L.T("tip.noproviders"));
            }

            if (!string.IsNullOrEmpty(probeFailed) && scanClaude)
            {
                lines.Add(L.T("tip.error", probeFailed));
            }
            else if (usage != null)
            {
                lines.Add(L.T("tip.today", Fmt.UsdShort(usage.TodayTotal.Cost)));
            }

            string s = string.Join("\n", lines.ToArray());
            if (string.IsNullOrEmpty(s)) s = L.AppName;
            return s.Length > maxLen ? s.Substring(0, maxLen - 1) + "…" : s;
        }
    }

    internal static class HotkeyUtil
    {
        public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;

        /// <summary>"Ctrl+Alt+U", "Win+Shift+F9". Wajib ada minimal satu pengubah (modifier).</summary>
        public static bool TryParse(string spec, out uint mods, out uint vk)
        {
            mods = 0; vk = 0;
            if (string.IsNullOrWhiteSpace(spec)) return false;
            string keyPart = null;
            foreach (string raw in spec.Split('+'))
            {
                string p = raw.Trim().ToLowerInvariant();
                if (p.Length == 0) return false;
                switch (p)
                {
                    case "ctrl": case "control": mods |= MOD_CONTROL; break;
                    case "alt": mods |= MOD_ALT; break;
                    case "shift": mods |= MOD_SHIFT; break;
                    case "win": case "windows": mods |= MOD_WIN; break;
                    default:
                        if (keyPart != null) return false;
                        keyPart = p; break;
                }
            }
            if (keyPart == null || mods == 0) return false;
            if (keyPart.Length == 1 && char.IsLetterOrDigit(keyPart[0]) && keyPart[0] < 128) { vk = (uint)char.ToUpperInvariant(keyPart[0]); return true; }
            int n;
            if (keyPart[0] == 'f' && int.TryParse(keyPart.Substring(1), out n) && n >= 1 && n <= 24) { vk = (uint)(0x70 + n - 1); return true; }
            return false;
        }
    }

    /// <summary>Jendela tak terlihat yang menerima WM_HOTKEY.</summary>
    internal class HotkeyWindow : NativeWindow, IDisposable
    {
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr h, int id);
        private const int WM_HOTKEY = 0x0312, Id = 0x5A11;
        private bool _registered;
        public event EventHandler Pressed;

        public HotkeyWindow() { CreateHandle(new CreateParams()); }

        /// <summary>Daftarkan (menggantikan yang lama). False bila format salah atau kombinasi sudah dipakai aplikasi lain.</summary>
        public bool Register(string spec)
        {
            Unregister();
            uint mods, vk;
            if (!HotkeyUtil.TryParse(spec, out mods, out vk)) return false;
            _registered = RegisterHotKey(Handle, Id, mods | HotkeyUtil.MOD_NOREPEAT, vk);
            return _registered;
        }

        public void Unregister()
        {
            if (_registered) { UnregisterHotKey(Handle, Id); _registered = false; }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == Id) { if (Pressed != null) Pressed(this, EventArgs.Empty); return; }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            Unregister();
            if (Handle != IntPtr.Zero) DestroyHandle();
        }
    }

    internal static class RunwayEstimator
    {
        public static double EstimateMinutes(RateLimitSnapshot limits, UsageSnapshot usage)
        {
            if (usage == null || usage.TokensLast10m < 5000) return 0;
            double minMins = double.MaxValue;
            if (limits != null)
            {
                ConsiderWindow(limits.FiveHour, ref minMins);
                ConsiderWindow(limits.AntigravityFiveHour, ref minMins);
                ConsiderWindow(limits.CodexFiveHour, ref minMins);
            }
            return (minMins < double.MaxValue && minMins > 0) ? minMins : 0;
        }

        private static void ConsiderWindow(LimitWindow w, ref double minMins)
        {
            if (w == null || !w.Available || w.IsStale || w.UsedPercent <= 5) return;
            double leftPct = Math.Max(0, 100.0 - w.UsedPercent);
            double elapsed = Pace.ElapsedAt(w, DateTime.Now, Pace.FiveHourSecs);
            if (double.IsNaN(elapsed) || elapsed <= 0.05) return;
            double pctPerMin = w.UsedPercent / (elapsed * 300.0);
            if (pctPerMin <= 0) return;
            double m = leftPct / pctPerMin;
            if (m < minMins) minMins = m;
        }
    }

    internal static class StatusFormatter
    {
        public static string Format(RateLimitSnapshot limits, UsageSnapshot usage, bool scanClaude, bool scanGemini)
        {
            return Format(limits, usage, scanClaude, scanGemini, false, null);
        }

        public static string Format(RateLimitSnapshot limits, UsageSnapshot usage, bool scanClaude, bool scanGemini, bool scanCodex, OllamaStatus ollama)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("🤖 AI Quota Status (AI Usage Watcher)");
            if (scanClaude)
            {
                LimitWindow w5 = limits != null ? limits.FiveHour : null;
                LimitWindow w7 = limits != null ? limits.SevenDay : null;
                string s5 = (w5 != null && w5.Available && !w5.IsStale)
                    ? string.Format("{0}%{1}", (int)Math.Round(w5.UsedPercent), w5.ResetsAt != DateTime.MinValue ? " (reset " + Fmt.Until(w5.ResetsAt) + " / " + Fmt.Clock(w5.ResetsAt) + ")" : "")
                    : "n/a";
                string s7 = (w7 != null && w7.Available && !w7.IsStale)
                    ? string.Format("{0}%{1}", (int)Math.Round(w7.UsedPercent), w7.ResetsAt != DateTime.MinValue ? " (reset " + Fmt.Until(w7.ResetsAt) + " / " + Fmt.Clock(w7.ResetsAt) + ")" : "")
                    : "n/a";
                sb.AppendLine("• Claude Code: 5h " + s5 + " · 7d " + s7);
            }
            if (scanGemini)
            {
                LimitWindow w5 = limits != null ? limits.AntigravityFiveHour : null;
                LimitWindow w7 = limits != null ? limits.AntigravitySevenDay : null;
                string s5 = (w5 != null && w5.Available && !w5.IsStale)
                    ? string.Format("{0}%{1}", (int)Math.Round(w5.UsedPercent), w5.ResetsAt != DateTime.MinValue ? " (reset " + Fmt.Until(w5.ResetsAt) + " / " + Fmt.Clock(w5.ResetsAt) + ")" : "")
                    : "0%";
                string s7 = (w7 != null && w7.Available && !w7.IsStale)
                    ? string.Format("{0}%{1}", (int)Math.Round(w7.UsedPercent), w7.ResetsAt != DateTime.MinValue ? " (reset " + Fmt.Until(w7.ResetsAt) + " / " + Fmt.Clock(w7.ResetsAt) + ")" : "")
                    : "0%";
                sb.AppendLine("• Antigravity: 5h " + s5 + " · 7d " + s7);
            }
            if (scanCodex && usage != null && (usage.CodexWeek.Total > 0 || (limits != null && limits.CodexFiveHour != null && limits.CodexFiveHour.Available)))
            {
                string lim = "";
                if (limits != null && ((limits.CodexFiveHour != null && limits.CodexFiveHour.Available) || (limits.CodexSevenDay != null && limits.CodexSevenDay.Available)))
                {
                    string c5 = (limits.CodexFiveHour != null && limits.CodexFiveHour.Available && !limits.CodexFiveHour.IsStale)
                        ? string.Format("{0}%", (int)Math.Round(limits.CodexFiveHour.UsedPercent)) : "n/a";
                    string c7 = (limits.CodexSevenDay != null && limits.CodexSevenDay.Available && !limits.CodexSevenDay.IsStale)
                        ? string.Format("{0}%", (int)Math.Round(limits.CodexSevenDay.UsedPercent)) : "n/a";
                    lim = " · 5h " + c5 + " · 7d " + c7;
                }
                sb.AppendLine(string.Format("• Codex CLI: Today {0} (≈{1}) · 7d {2}{3}",
                    Fmt.Tokens(usage.CodexToday.Total), Fmt.Usd(usage.CodexToday.Cost), Fmt.Tokens(usage.CodexWeek.Total), lim));
            }
            if (ollama != null && ollama.Online)
            {
                string olVram = ollama.LoadedCount > 0 && ollama.TotalVramBytes > 0
                    ? string.Format(CultureInfo.InvariantCulture, " · {0} loaded ({1:0.0} GB VRAM)", ollama.LoadedCount, ollama.TotalVramBytes / (1024.0 * 1024.0 * 1024.0))
                    : (ollama.LoadedCount > 0 ? string.Format(CultureInfo.InvariantCulture, " · {0} loaded", ollama.LoadedCount) : "");
                sb.AppendLine(string.Format("• Ollama: Online ({0} models{1})", ollama.ModelCount, olVram));
            }
            if (usage != null && usage.GeminiCliWeek.Total > 0)
            {
                sb.AppendLine(string.Format("• Gemini CLI: Today {0} (≈{1}) · 7d {2}",
                    Fmt.Tokens(usage.GeminiCliToday.Total), Fmt.Usd(usage.GeminiCliToday.Cost), Fmt.Tokens(usage.GeminiCliWeek.Total)));
            }
            if (usage != null && usage.OpenCodeWeek.Total > 0)
            {
                sb.AppendLine(string.Format("• OpenCode: Today {0} (≈{1}) · 7d {2}",
                    Fmt.Tokens(usage.OpenCodeToday.Total), Fmt.Usd(usage.OpenCodeToday.Cost), Fmt.Tokens(usage.OpenCodeWeek.Total)));
            }
            if (usage != null)
            {
                sb.AppendLine(string.Format("• Today: {0} · {1}", Fmt.Usd(usage.TodayTotal.Cost), Fmt.Tokens(usage.TodayTotal.Total)));
                if (usage.LastRecord != DateTime.MinValue)
                    sb.AppendLine("• Last Chat: " + Fmt.Clock(usage.LastRecord));
            }
            return sb.ToString().TrimEnd();
        }
    }

    internal static class ReportExporter
    {
        public static string GenerateMonthlyMarkdown(HistoryStore history, UsageSnapshot usage, DateTime month)
        {
            StringBuilder sb = new StringBuilder();
            string titleMonth = month.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            sb.Append("# AI Usage Monthly Report - ").Append(titleMonth).Append("\r\n\r\n");
            sb.Append("Generated on: ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append("\r\n\r\n");

            List<DayStat> days = history != null ? history.GetMonth(month) : new List<DayStat>();
            long totalTokens = 0;
            long totalReqs = 0;
            double totalCost = 0;
            foreach (DayStat d in days)
            {
                totalTokens += d.Tokens;
                totalReqs += d.Requests;
                totalCost += d.Cost;
            }

            sb.Append("## Monthly Summary\r\n");
            sb.Append("- **Total Requests**: ").Append(totalReqs.ToString("N0", CultureInfo.InvariantCulture)).Append("\r\n");
            sb.Append("- **Total Tokens**: ").Append(Fmt.Tokens(totalTokens)).Append(" (").Append(totalTokens.ToString("N0", CultureInfo.InvariantCulture)).Append(")\r\n");
            sb.Append("- **Estimated Cost (USD)**: ").Append(totalCost.ToString("$0.0000", CultureInfo.InvariantCulture)).Append("\r\n");
            sb.Append("- **Estimated Cost (IDR)**: ").Append(Fmt.Usd(totalCost)).Append("\r\n\r\n");

            if (usage != null && usage.Week != null && usage.Week.Count > 0)
            {
                sb.Append("## Active Models\r\n");
                sb.Append("| Model | Requests | Tokens | Cost (Est) |\r\n");
                sb.Append("| :--- | :--- | :--- | :--- |\r\n");
                List<KeyValuePair<string, Tally>> sortedModels = new List<KeyValuePair<string, Tally>>(usage.Week);
                sortedModels.Sort(delegate(KeyValuePair<string, Tally> a, KeyValuePair<string, Tally> b) { return b.Value.Total.CompareTo(a.Value.Total); });
                foreach (KeyValuePair<string, Tally> kv in sortedModels)
                {
                    sb.Append("| `").Append(kv.Key).Append("` | ")
                      .Append(kv.Value.Requests.ToString("N0", CultureInfo.InvariantCulture)).Append(" | ")
                      .Append(Fmt.Tokens(kv.Value.Total)).Append(" | ")
                      .Append(Fmt.Usd(kv.Value.Cost)).Append(" |\r\n");
                }
                sb.Append("\r\n");
            }

            sb.Append("## Daily Breakdown\r\n");
            sb.Append("| Date | Requests | Tokens | Cost (USD) | Cost (IDR) |\r\n");
            sb.Append("| :--- | :--- | :--- | :--- | :--- |\r\n");
            if (days.Count == 0)
            {
                sb.Append("| *No records* | - | - | - | - |\r\n");
            }
            else
            {
                foreach (DayStat d in days)
                {
                    sb.Append("| ").Append(d.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(" | ")
                      .Append(d.Requests.ToString("N0", CultureInfo.InvariantCulture)).Append(" | ")
                      .Append(Fmt.Tokens(d.Tokens)).Append(" | ")
                      .Append(d.Cost.ToString("$0.0000", CultureInfo.InvariantCulture)).Append(" | ")
                      .Append(Fmt.Usd(d.Cost)).Append(" |\r\n");
                }
            }
            return sb.ToString();
        }
    }

    internal static class StatusExporter
    {
        public static void Export(UsageSnapshot u, RateLimitSnapshot rl, ViewModel vm)
        {
            if (u == null) return;
            try
            {
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["updated_at"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

                if (rl != null)
                {
                    if (rl.FiveHour != null && rl.FiveHour.Available)
                    {
                        d["claude_5h_percent"] = Math.Round(rl.FiveHour.UsedPercent, 1);
                        d["five_hour_percent"] = Math.Round(rl.FiveHour.UsedPercent, 1);
                        if (rl.FiveHour.ResetsAt != DateTime.MinValue)
                            d["claude_5h_resets_at"] = rl.FiveHour.ResetsAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
                    }
                    if (rl.SevenDay != null && rl.SevenDay.Available)
                    {
                        d["claude_7d_percent"] = Math.Round(rl.SevenDay.UsedPercent, 1);
                        d["seven_day_percent"] = Math.Round(rl.SevenDay.UsedPercent, 1);
                        if (rl.SevenDay.ResetsAt != DateTime.MinValue)
                            d["claude_7d_resets_at"] = rl.SevenDay.ResetsAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
                    }
                    if (rl.AntigravityFiveHour != null && rl.AntigravityFiveHour.Available)
                    {
                        d["antigravity_5h_percent"] = Math.Round(rl.AntigravityFiveHour.UsedPercent, 1);
                        if (rl.AntigravityFiveHour.ResetsAt != DateTime.MinValue)
                            d["antigravity_5h_resets_at"] = rl.AntigravityFiveHour.ResetsAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
                    }
                    if (rl.AntigravitySevenDay != null && rl.AntigravitySevenDay.Available)
                    {
                        d["antigravity_7d_percent"] = Math.Round(rl.AntigravitySevenDay.UsedPercent, 1);
                        if (rl.AntigravitySevenDay.ResetsAt != DateTime.MinValue)
                            d["antigravity_7d_resets_at"] = rl.AntigravitySevenDay.ResetsAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
                    }
                    if (rl.CodexFiveHour != null && rl.CodexFiveHour.Available)
                    {
                        d["codex_5h_percent"] = Math.Round(rl.CodexFiveHour.UsedPercent, 1);
                        if (rl.CodexFiveHour.ResetsAt != DateTime.MinValue)
                            d["codex_5h_resets_at"] = rl.CodexFiveHour.ResetsAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
                    }
                    if (rl.CodexSevenDay != null && rl.CodexSevenDay.Available)
                    {
                        d["codex_7d_percent"] = Math.Round(rl.CodexSevenDay.UsedPercent, 1);
                        if (rl.CodexSevenDay.ResetsAt != DateTime.MinValue)
                            d["codex_7d_resets_at"] = rl.CodexSevenDay.ResetsAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
                    }
                }

                d["today_tokens"] = u.TodayTotal.Total;
                d["today_cost_usd"] = Math.Round(u.TodayTotal.Cost, 4);
                d["today_cost_formatted"] = Fmt.Usd(u.TodayTotal.Cost);
                d["today_saved_usd"] = Math.Round(u.TodayTotal.Saved, 4);
                d["cache_hit_percent"] = Math.Round(u.TodayTotal.CacheHit * 100, 1);
                d["burn_rate_10m"] = u.TokensLast10m;
                d["last_context_tokens"] = u.LastContextTokens;
                d["last_active_project"] = u.LastActiveProject ?? "";
                if (u.CodexWeek.Total > 0)
                {
                    d["codex_today_tokens"] = u.CodexToday.Total;
                    d["codex_today_cost_usd"] = Math.Round(u.CodexToday.Cost, 4);
                }
                if (u.GeminiCliWeek.Total > 0)
                {
                    d["gemini_cli_today_tokens"] = u.GeminiCliToday.Total;
                    d["gemini_cli_today_cost_usd"] = Math.Round(u.GeminiCliToday.Cost, 4);
                }
                if (u.OpenCodeWeek.Total > 0)
                {
                    d["opencode_today_tokens"] = u.OpenCodeToday.Total;
                    d["opencode_today_cost_usd"] = Math.Round(u.OpenCodeToday.Cost, 4);
                }
                if (u.Sessions != null)
                {
                    d["running_sessions"] = u.Sessions.RunningCount;
                    d["confirm_sessions"] = u.Sessions.ConfirmCount;
                    d["done_sessions"] = u.Sessions.DoneCount;
                }

                double runway = RunwayEstimator.EstimateMinutes(rl, u);
                if (runway > 0) d["runway_minutes"] = Math.Round(runway, 1);

                if (OllamaProbe.Current != null && OllamaProbe.Current.Online)
                {
                    d["ollama_online"] = true;
                    d["ollama_model_count"] = OllamaProbe.Current.ModelCount;
                    d["ollama_models"] = OllamaProbe.Current.ModelNames;
                    d["ollama_loaded_count"] = OllamaProbe.Current.LoadedCount;
                    if (OllamaProbe.Current.LoadedModels.Count > 0)
                        d["ollama_loaded_models"] = OllamaProbe.Current.LoadedModels;
                    if (OllamaProbe.Current.TotalVramBytes > 0)
                        d["ollama_vram_bytes"] = OllamaProbe.Current.TotalVramBytes;
                }

                string json = PrettyJson.Write(d);
                try { AtomicFile.Write(Paths.StatusExportJson, json); } catch { }
                try { Paths.Ensure(); AtomicFile.Write(Paths.AppStatusJson, json); } catch { }
            }
            catch (Exception ex)
            {
                Log.Write("status.export", ex);
            }
        }
    }
}
