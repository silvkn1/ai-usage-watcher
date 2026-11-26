// Claude Usage Widget - utilitas bersama (C# 5 / .NET Framework 4.8)
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]
[assembly: System.Reflection.AssemblyTitle("AI Usage Watcher")]
[assembly: System.Reflection.AssemblyProduct("AI Usage Watcher")]
[assembly: System.Reflection.AssemblyVersion("1.2.0.0")]

namespace ClaudeUsageWidget
{
    internal static class Paths
    {
        /// <summary>Folder exe. Bila ada berkas 'portable.flag' di sini, mode portabel: data disimpan di '.\data'.</summary>
        public static readonly string ExeDir = DetectExeDir();
        public static readonly bool Portable = File.Exists(Path.Combine(ExeDir, "portable.flag"));
        /// <summary>Lokasi data standar (dipakai juga oleh pembungkus statusline, bahkan saat mode portabel).</summary>
        public static readonly string StandardDir = DetectStandardDir();
        public static readonly string AppDir = Portable ? Path.Combine(ExeDir, "data") : StandardDir;

        private static string DetectStandardDir()
        {
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string newDir = Path.Combine(localApp, "AIUsageWatcher");
            string oldDir = Path.Combine(localApp, "ClaudeUsageWidget");
            if (!Directory.Exists(newDir) && Directory.Exists(oldDir)) return oldDir;
            return newDir;
        }

        private static string DetectExeDir()
        {
            try { return Path.GetDirectoryName(typeof(Paths).Assembly.Location) ?? AppDomain.CurrentDomain.BaseDirectory; }
            catch { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        /// <summary>Berkas snapshot limit yang dibaca: di mode portabel, yang terbaru antara folder data portabel dan folder standar
        /// (pembungkus statusline selalu menulis ke folder standar).</summary>
        public static string RateLimitsReadFile
        {
            get
            {
                if (!Portable)
                {
                    if (File.Exists(RateLimitsFile)) return RateLimitsFile;
                    string legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudeUsageWidget", "ratelimits.json");
                    if (File.Exists(legacy)) return legacy;
                    return RateLimitsFile;
                }
                string a = RateLimitsFile, b = Path.Combine(StandardDir, "ratelimits.json");
                bool ea = File.Exists(a), eb = File.Exists(b);
                if (ea && eb) return File.GetLastWriteTimeUtc(a) >= File.GetLastWriteTimeUtc(b) ? a : b;
                return eb ? b : a;
            }
        }

        public static string LangDirAppData { get { return Path.Combine(AppDir, "lang"); } }
        public static string LangDirExe { get { return Path.Combine(ExeDir, "lang"); } }
        public static string HooksDir { get { return Path.Combine(AppDir, "hooks"); } }
        public static string NotifyFile { get { return Path.Combine(AppDir, "notify.json"); } }

        public static string ClaudeDir
        {
            get
            {
                string env = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
                if (!string.IsNullOrEmpty(env)) return env;
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
            }
        }

        public static string ProjectsDir { get { return Path.Combine(ClaudeDir, "projects"); } }

        public static string AntigravityDir
        {
            get
            {
                string env = Environment.GetEnvironmentVariable("ANTIGRAVITY_DATA_DIR");
                if (!string.IsNullOrEmpty(env)) return env;
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity");
            }
        }
        public static string AntigravityBrainDir { get { return Path.Combine(AntigravityDir, "brain"); } }
        public static string AntigravityDaemonDir { get { return Path.Combine(AntigravityDir, "daemon"); } }
        public static string CodexDir
        {
            get
            {
                string env = Environment.GetEnvironmentVariable("CODEX_HOME");
                if (!string.IsNullOrEmpty(env)) return env;
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            }
        }
        public static string CodexSessionsDir { get { return Path.Combine(CodexDir, "sessions"); } }
        public static string RateLimitsFile { get { return Path.Combine(AppDir, "ratelimits.json"); } }
        public static string AntigravityRateLimitsFile { get { return Path.Combine(AppDir, "antigravity_ratelimits.json"); } }
        public static string AntigravityRateLimitsReadFile
        {
            get
            {
                if (File.Exists(AntigravityRateLimitsFile)) return AntigravityRateLimitsFile;
                string p1 = Path.Combine(AntigravityDir, "antigravity_ratelimits.json");
                if (File.Exists(p1)) return p1;
                string p2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity_ratelimits.json");
                if (File.Exists(p2)) return p2;
                return AntigravityRateLimitsFile;
            }
        }
        public static string ConfigFile { get { return Path.Combine(AppDir, "config.json"); } }
        public static string PricingFile { get { return Path.Combine(AppDir, "pricing.json"); } }
        public static string CustomPricingFile { get { return Path.Combine(AppDir, "custom_pricing.json"); } }
        public static string ScanCacheFile { get { return Path.Combine(AppDir, "scan_cache.json"); } }
        public static string CommandFile { get { return Path.Combine(AppDir, "command.json"); } }
        public static string GeminiCliTmpDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "tmp"); } }
        public static string OpenCodeDataDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "opencode"); } }
        public static string LogFile { get { return Path.Combine(AppDir, "widget.log"); } }
        public static string StatusExportJson { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ai-usage-status.json"); } }
        public static string AppStatusJson { get { return Path.Combine(AppDir, "status.json"); } }

        public static void Ensure() { Directory.CreateDirectory(AppDir); }
    }

    /// <summary>Log lokal minimal: hanya jenis kejadian/eksepsi, tanpa isi prompt atau path proyek.</summary>
    internal static class Log
    {
        private static readonly object Gate = new object();

        public static void Write(string context, Exception ex)
        {
            Write(context + ": " + (ex == null ? "" : ex.GetType().Name));
        }

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    FileInfo fi = new FileInfo(Paths.LogFile);
                    if (fi.Exists && fi.Length > 256 * 1024) fi.Delete();
                    File.AppendAllText(Paths.LogFile,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " " + message + Environment.NewLine,
                        Encoding.UTF8);
                }
            }
            catch { }
        }
    }

    /// <summary>Akses aman ke hasil JavaScriptSerializer (Dictionary/ArrayList) - toleran terhadap field hilang/berubah.</summary>
    internal static class J
    {
        private static JavaScriptSerializer _ser;
        public static JavaScriptSerializer Ser
        {
            get
            {
                if (_ser == null) { _ser = new JavaScriptSerializer(); _ser.MaxJsonLength = int.MaxValue; _ser.RecursionLimit = 256; }
                return _ser;
            }
        }

        public static Dictionary<string, object> Parse(string json)
        {
            return Ser.DeserializeObject(json) as Dictionary<string, object>;
        }

        public static Dictionary<string, object> Obj(Dictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v)) return v as Dictionary<string, object>;
            return null;
        }

        public static IList Arr(Dictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v)) return v as IList;
            return null;
        }

        public static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null) return Convert.ToString(v, CultureInfo.InvariantCulture);
            return null;
        }

        public static bool Has(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v != null;
        }

        public static long Long(Dictionary<string, object> d, string key)
        {
            double x = Num(d, key);
            return double.IsNaN(x) ? 0 : (long)x;
        }

        /// <summary>Angka atau string angka; NaN bila tidak ada.</summary>
        public static double Num(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return double.NaN;
            return ToNum(v);
        }

        public static double ToNum(object v)
        {
            if (v == null) return double.NaN;
            if (v is int) return (int)v;
            if (v is long) return (long)v;
            if (v is decimal) return (double)(decimal)v;
            if (v is double) return (double)v;
            double r;
            if (double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out r)) return r;
            return double.NaN;
        }
    }

    internal static class Fmt
    {
        public static string Tokens(long n)
        {
            CultureInfo c = L.Culture;
            if (n >= 1000000000) return (n / 1e9).ToString("0.00", c) + " " + L.T("unit.b");
            if (n >= 1000000) return (n / 1e6).ToString("0.0", c) + " " + L.T("unit.m");
            if (n >= 1000) return (n / 1e3).ToString("0.0", c) + " " + L.T("unit.k");
            return n.ToString(c);
        }

        public static string Usd(double v)
        {
            string usd = "$" + v.ToString(v >= 100 ? "N0" : "N2", CultureInfo.InvariantCulture);
            if (!Opts.CurrencyCustomEnabled || Opts.CurrencyRate <= 0) return usd;
            double local = v * Opts.CurrencyRate;
            string sym = string.IsNullOrEmpty(Opts.CurrencySymbol) ? "Rp" : Opts.CurrencySymbol;
            string localStr = sym + " " + local.ToString(local >= 1000 ? "N0" : "N2", L.Culture);
            return Opts.CurrencyShowBoth ? usd + " (" + localStr + ")" : localStr;
        }

        public static string UsdShort(double v)
        {
            string usd = "$" + v.ToString(v >= 100 ? "N0" : "N2", CultureInfo.InvariantCulture);
            if (!Opts.CurrencyCustomEnabled || Opts.CurrencyRate <= 0) return usd;
            double local = v * Opts.CurrencyRate;
            string sym = string.IsNullOrEmpty(Opts.CurrencySymbol) ? "Rp" : Opts.CurrencySymbol;
            return sym + " " + local.ToString(local >= 1000 ? "N0" : "N2", L.Culture);
        }

        public static string Until(DateTime target)
        {
            TimeSpan t = target - DateTime.Now;
            if (t.TotalSeconds <= 0) return L.T("time.now");
            if (t.TotalDays >= 1) return L.T("time.dh", (int)t.TotalDays, t.Hours);
            if (t.TotalHours >= 1) return L.T("time.hm", (int)t.TotalHours, t.Minutes);
            return L.T("time.m", Math.Max(1, (int)Math.Ceiling(t.TotalMinutes)));
        }

        public static string Runway(double minutes)
        {
            if (minutes <= 0) return L.T("time.now");
            if (minutes < 60) return string.Format("{0:0}m", minutes);
            int h = (int)(minutes / 60);
            int m = (int)(minutes % 60);
            return string.Format("{0}h {1:00}m", h, m);
        }

        /// <summary>Durasi pendek: 45s, 2m 10s, 1h 5m.</summary>
        public static string Duration(long seconds)
        {
            if (seconds < 60) return L.T("dur.s", seconds);
            if (seconds < 3600) return L.T("dur.ms", seconds / 60, seconds % 60);
            return L.T("dur.hm", seconds / 3600, (seconds % 3600) / 60);
        }

        public static string Clock(DateTime dt)
        {
            if (dt == DateTime.MinValue) return "-";
            if (dt.Date == DateTime.Today) return dt.ToString("HH:mm", L.Culture);
            return dt.ToString("dd MMM HH:mm", L.Culture);
        }

        public static DateTime FromUnix(double secs)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(secs).ToLocalTime();
        }
    }

    internal static class ProcessHelper
    {
        public static string ExplorerExe
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"); }
        }

        public static string SystemExe(string name)
        {
            return Path.Combine(Environment.SystemDirectory, name);
        }

        public static bool IsSafeSearchDir(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return false;
            string t = dir.Trim();
            if (t.StartsWith("\\\\", StringComparison.Ordinal) || t.StartsWith(".", StringComparison.Ordinal)) return false;
            return Path.IsPathRooted(t);
        }

        public static string FindExecutableOnPath(params string[] names)
        {
            string localApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps");
            string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string localProg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";

            foreach (string name in names)
            {
                string[] extraDirs = new[]
                {
                    Environment.SystemDirectory,
                    localApps,
                    Path.Combine(progFiles, @"Microsoft VS Code\bin"),
                    Path.Combine(localProg, @"Microsoft VS Code\bin"),
                    Path.Combine(localProg, @"cursor\resources\app\bin")
                };
                foreach (string d in extraDirs)
                {
                    try { string p = Path.Combine(d, name); if (File.Exists(p)) return p; } catch { }
                }
                foreach (string dir in pathEnv.Split(';'))
                {
                    try
                    {
                        string t = dir.Trim();
                        if (!IsSafeSearchDir(t)) continue;
                        string p = Path.Combine(t, name);
                        if (File.Exists(p)) return p;
                    }
                    catch { }
                }
            }
            return null;
        }

        public static void OpenFolder(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            System.Diagnostics.Process.Start(ExplorerExe, "\"" + dir + "\"");
        }

        public static void OpenTerminal(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            string wt = FindExecutableOnPath("wt.exe");
            System.Diagnostics.ProcessStartInfo psi;
            if (!string.IsNullOrEmpty(wt))
                psi = new System.Diagnostics.ProcessStartInfo(wt, "-d \"" + dir + "\"");
            else
                psi = new System.Diagnostics.ProcessStartInfo(SystemExe("cmd.exe"), "/K cd /d \"" + dir + "\"");
            psi.WorkingDirectory = dir;
            psi.UseShellExecute = true;
            System.Diagnostics.Process.Start(psi);
        }

        public static bool HasVSCode()
        {
            return FindExecutableOnPath("code.cmd", "code.exe", "cursor.cmd") != null;
        }

        public static bool OpenVSCode(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
            string exe = FindExecutableOnPath("code.cmd", "code.exe", "cursor.cmd");
            if (string.IsNullOrEmpty(exe)) return false;
            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(exe, "\"" + dir + "\"");
            psi.WorkingDirectory = dir;
            psi.UseShellExecute = !exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);
            psi.CreateNoWindow = true;
            System.Diagnostics.Process.Start(psi);
            return true;
        }
    }
}
