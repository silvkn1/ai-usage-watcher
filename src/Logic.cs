using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
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
            LimitWindow w5 = limits.FiveHour, w7 = limits.SevenDay;
            if ((!w5.Available || w5.IsStale) && limits.AntigravityFiveHour != null && limits.AntigravityFiveHour.Available)
            {
                w5 = limits.AntigravityFiveHour;
                if (limits.AntigravitySevenDay != null && limits.AntigravitySevenDay.Available)
                    w7 = limits.AntigravitySevenDay;
            }
            weekly = w7.Available && !w7.IsStale && Math.Round(w7.UsedPercent) >= 100;
            LimitWindow w = weekly ? w7 : w5;
            bool show = w.Available && !w.IsStale;
            int used = (int)Math.Round(w.UsedPercent);
            int v = Math.Max(0, Math.Min(100, Opts.ShowRemaining ? 100 - used : used));
            text = show ? v.ToString() : "--";
            badge = show ? TrayIconPainter.StatusColor(w.UsedPercent) : TrayIconPainter.StGray;
        }

        private static string Line(LimitWindow w, string withReset, string noReset)
        {
            string pct = Pct(w);
            if (w.Available && !w.IsStale && w.ResetsAt != DateTime.MinValue) return L.T(withReset, pct, Fmt.Until(w.ResetsAt), Fmt.Clock(w.ResetsAt));
            return L.T(noReset, pct);
        }

        /// <summary>Baris 1: 5h, baris 2: 7d (persen, sisa waktu, dan jam reset), baris 3: galat probe terakhir atau biaya hari ini.
        /// maxLen: 127 bila batas tooltip bisa diperluas, selain itu 63 (batas NotifyIcon.Text).</summary>
        public static string Tooltip(RateLimitSnapshot limits, UsageSnapshot usage, string probeFailed, int maxLen)
        {
            string s = Line(limits.FiveHour, "tip.l5", "tip.l5n") + "\n" + Line(limits.SevenDay, "tip.l7", "tip.l7n");
            if (!string.IsNullOrEmpty(probeFailed)) s += "\n" + L.T("tip.error", probeFailed);
            else if (usage != null) s += "\n" + L.T("tip.today", Fmt.UsdShort(usage.TodayTotal.Cost));
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
}
