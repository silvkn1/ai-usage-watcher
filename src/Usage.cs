using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ClaudeUsageWidget
{
    internal class Price
    {
        public string Prefix;
        public double Input, Output, CacheWrite5m, CacheWrite1h, CacheRead;
    }

    /// <summary>Tabel harga dari pricing.json dan custom_pricing.json (override opsional pengguna).</summary>
    internal class Pricing
    {
        private readonly List<Price> _list = new List<Price>();
        private readonly List<Price> _customList = new List<Price>();
        public DateTime LoadedFileTime { get; private set; }
        public DateTime LoadedCustomFileTime { get; private set; }
        public bool HasCustomPricing { get { return _customList.Count > 0; } }
        public string LastCustomError { get; private set; }

        public static string ValidateCustomJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return "Empty JSON";
            Dictionary<string, object> root;
            try { root = J.Parse(json); }
            catch (Exception ex) { return ex.Message; }
            if (root == null) return "Root must be a JSON object";
            IList models = J.Arr(root, "models");
            if (models == null) return "Missing 'models' array";
            for (int i = 0; i < models.Count; i++)
            {
                Dictionary<string, object> m = models[i] as Dictionary<string, object>;
                if (m == null) return string.Format("Entry #{0} is not an object", i + 1);
                string prefix = J.Str(m, "prefix");
                if (string.IsNullOrEmpty(prefix)) return string.Format("Entry #{0} missing 'prefix'", i + 1);
                string[] fields = { "input", "output", "cache_write_5m", "cache_write_1h", "cache_read" };
                for (int fi = 0; fi < fields.Length; fi++)
                {
                    double val = J.Num(m, fields[fi]);
                    if (!double.IsNaN(val) && val < 0)
                        return string.Format("Negative '{0}' rate on '{1}'", fields[fi], prefix);
                }
            }
            return null;
        }

        public string Stamp()
        {
            return LoadedFileTime.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
                   LoadedCustomFileTime.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public static Pricing Load()
        {
            return LoadFromFiles(Paths.PricingFile, Paths.CustomPricingFile);
        }

        internal static Pricing LoadFromFiles(string mainPath, string customPath)
        {
            Pricing p = new Pricing();
            try
            {
                if (mainPath == Paths.PricingFile) Paths.Ensure();
                if (mainPath == Paths.PricingFile && !File.Exists(mainPath))
                {
                    using (Stream s = typeof(Pricing).Assembly.GetManifestResourceStream("pricing.json"))
                    using (FileStream f = File.Create(mainPath))
                        if (s != null) s.CopyTo(f);
                }
                if (!string.IsNullOrEmpty(mainPath) && File.Exists(mainPath))
                {
                    p.LoadedFileTime = File.GetLastWriteTimeUtc(mainPath);
                    LoadList(mainPath, p._list);
                }
                if (!string.IsNullOrEmpty(customPath) && File.Exists(customPath))
                {
                    p.LoadedCustomFileTime = File.GetLastWriteTimeUtc(customPath);
                    string rawCustom = Paths.ReadAllTextShared(customPath);
                    string err = ValidateCustomJson(rawCustom);
                    if (err != null)
                    {
                        p.LastCustomError = err;
                        Log.Write("pricing.custom_invalid: " + err);
                    }
                    else
                    {
                        LoadList(customPath, p._customList);
                    }
                }
            }
            catch (Exception ex)
            {
                p.LastCustomError = ex.Message;
                Log.Write("pricing.load", ex);
            }
            return p;
        }

        private static void LoadList(string path, List<Price> dest)
        {
            Dictionary<string, object> root = J.Parse(Paths.ReadAllTextShared(path));
            IList models = J.Arr(root, "models");
            if (models != null)
                foreach (object o in models)
                {
                    Dictionary<string, object> m = o as Dictionary<string, object>;
                    string prefix = J.Str(m, "prefix");
                    if (string.IsNullOrEmpty(prefix)) continue;
                    Price pr = new Price();
                    pr.Prefix = prefix;
                    pr.Input = Z(J.Num(m, "input"));
                    pr.Output = Z(J.Num(m, "output"));
                    pr.CacheWrite5m = Z(J.Num(m, "cache_write_5m"));
                    pr.CacheWrite1h = Z(J.Num(m, "cache_write_1h"));
                    pr.CacheRead = Z(J.Num(m, "cache_read"));
                    dest.Add(pr);
                }
            dest.Sort(delegate(Price a, Price b) { return b.Prefix.Length.CompareTo(a.Prefix.Length); });
        }

        public static void EnsureCustomTemplate()
        {
            try
            {
                Paths.Ensure();
                if (File.Exists(Paths.CustomPricingFile)) return;
                string tpl = "{\r\n" +
                    "  \"_comment\": \"Custom model price overrides (USD per 1M tokens). Takes priority over pricing.json.\",\r\n" +
                    "  \"models\": [\r\n" +
                    "    {\r\n" +
                    "      \"prefix\": \"openrouter/example-model\",\r\n" +
                    "      \"input\": 1.0,\r\n" +
                    "      \"output\": 3.0,\r\n" +
                    "      \"cache_write_5m\": 1.25,\r\n" +
                    "      \"cache_write_1h\": 2.0,\r\n" +
                    "      \"cache_read\": 0.1\r\n" +
                    "    }\r\n" +
                    "  ]\r\n" +
                    "}\r\n";
                AtomicFile.Write(Paths.CustomPricingFile, tpl);
            }
            catch (Exception ex) { Log.Write("pricing.custom_tpl", ex); }
        }

        private static double Z(double v) { return double.IsNaN(v) ? 0 : v; }

        private static Price FindInList(List<Price> list, string model)
        {
            foreach (Price p in list)
            {
                if (p.Prefix == "gemini-" && model.IndexOf("pro", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                if (model.StartsWith(p.Prefix, StringComparison.OrdinalIgnoreCase)) return p;
            }
            return null;
        }

        public Price Find(string model)
        {
            if (string.IsNullOrEmpty(model)) return null;
            Price cp = FindInList(_customList, model);
            if (cp != null) return cp;
            Price bp = FindInList(_list, model);
            if (bp != null) return bp;
            if (model.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase))
            {
                bool isPro = model.IndexOf("pro", StringComparison.OrdinalIgnoreCase) >= 0;
                Price def = new Price();
                def.Prefix = model;
                def.Input = isPro ? 1.25 : 0.10;
                def.Output = isPro ? 5.00 : 0.40;
                def.CacheRead = isPro ? 0.3125 : 0.025;
                return def;
            }
            return null;
        }
    }

    internal class Tally
    {
        public long Input, Output, CacheWrite5m, CacheWrite1h, CacheRead, Requests, FastRequests, UnpricedRequests;
        public double Cost;
        /// <summary>Estimasi penghematan cache bersih (USD, harga list): hemat membaca cache dikurangi biaya tambahan menulis cache.</summary>
        public double Saved;

        public long Total { get { return Input + Output + CacheWrite5m + CacheWrite1h + CacheRead; } }
        /// <summary>Token sisi input: input biasa + baca cache + tulis cache.</summary>
        public long InputSide { get { return Input + CacheRead + CacheWrite5m + CacheWrite1h; } }
        /// <summary>Porsi token sisi input yang dilayani dari cache (0..1).</summary>
        public double CacheHit { get { return InputSide <= 0 ? 0 : (double)CacheRead / InputSide; } }

        public void Add(Tally t)
        {
            Input += t.Input; Output += t.Output; CacheWrite5m += t.CacheWrite5m; CacheWrite1h += t.CacheWrite1h;
            CacheRead += t.CacheRead; Requests += t.Requests; FastRequests += t.FastRequests;
            UnpricedRequests += t.UnpricedRequests; Cost += t.Cost; Saved += t.Saved;
        }
    }

    internal class SessionCostRecord
    {
        public string SessionId;
        public string Provider;
        public string Project;
        public string TopModel;
        public long Input;
        public long CacheRead;
        public long CacheWrite;
        public long Output;
        public long Requests;
        public double Cost;
        public double Saved;
        public DateTime LastActivity = DateTime.MinValue;

        public long TotalTokens { get { return Input + CacheRead + CacheWrite + Output; } }
        public long InputSide { get { return Input + CacheRead + CacheWrite; } }
        public double CacheHit { get { return InputSide <= 0 ? 0 : (double)CacheRead / InputSide; } }
    }

    internal class ToolUsageRecord
    {
        public string Provider;
        public string Tool;
        public long Calls;
        public long Tokens;
        public double Cost;
    }

    internal class UsageSnapshot
    {
        public Dictionary<string, Tally> Today = new Dictionary<string, Tally>();
        public Dictionary<string, Tally> Week = new Dictionary<string, Tally>();
        public Dictionary<string, Tally> TodayClaude = new Dictionary<string, Tally>();
        public Dictionary<string, Tally> TodayAntigravity = new Dictionary<string, Tally>();
        public Dictionary<string, Tally> TodayCodex = new Dictionary<string, Tally>();
        public Dictionary<string, Tally> TodayGeminiCli = new Dictionary<string, Tally>();
        public Dictionary<string, Tally> TodayOpenCode = new Dictionary<string, Tally>();
        public Dictionary<string, Tally> TodayCline = new Dictionary<string, Tally>();
        public Dictionary<string, Tally> TodayCopilot = new Dictionary<string, Tally>();
        public Dictionary<string, Tally> WeekClaude = new Dictionary<string, Tally>();
        public Dictionary<string, Tally> WeekAntigravity = new Dictionary<string, Tally>();
        public Dictionary<string, Tally> WeekCodex = new Dictionary<string, Tally>();
        public Dictionary<string, Tally> WeekGeminiCli = new Dictionary<string, Tally>();
        public Dictionary<string, Tally> WeekOpenCode = new Dictionary<string, Tally>();
        public Dictionary<string, Tally> WeekCline = new Dictionary<string, Tally>();
        public Dictionary<string, Tally> WeekCopilot = new Dictionary<string, Tally>();
        public Tally TodayTotal = new Tally();
        public Tally WeekTotal = new Tally();
        public Tally ClaudeToday = new Tally();
        public Tally AntigravityToday = new Tally();
        public Tally CodexToday = new Tally();
        public Tally GeminiCliToday = new Tally();
        public Tally OpenCodeToday = new Tally();
        public Tally ClineToday = new Tally();
        public Tally CopilotToday = new Tally();
        public Tally ClaudeWeek = new Tally();
        public Tally AntigravityWeek = new Tally();
        public Tally CodexWeek = new Tally();
        public Tally GeminiCliWeek = new Tally();
        public Tally OpenCodeWeek = new Tally();
        public Tally ClineWeek = new Tally();
        public Tally CopilotWeek = new Tally();
        public Dictionary<string, Tally> TodayProjects = new Dictionary<string, Tally>();
        public Dictionary<string, Tally> WeekProjects = new Dictionary<string, Tally>();
        public Dictionary<string, string> ProjectPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> ProjectProviders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<SessionCostRecord> ExpensiveSessions = new List<SessionCostRecord>();
        public List<ToolUsageRecord> ToolBreakdown = new List<ToolUsageRecord>();
        public bool OpenCodeSqliteMissingCli;
        public int CacheHitFiles, DiskReadFiles;
        public LimitWindow AntigravityFiveHour = new LimitWindow();
        public LimitWindow AntigravitySevenDay = new LimitWindow();
        public LimitWindow CodexFiveHour = new LimitWindow();
        public LimitWindow CodexSevenDay = new LimitWindow();
        public long SkippedLines;
        public DateTime LastRecord = DateTime.MinValue;
        public DateTime ScannedAt = DateTime.MinValue;
        public bool Ready;
        /// <summary>Total per hari (jendela pindai) untuk riwayat; hanya agregat.</summary>
        public List<DayStat> Days = new List<DayStat>();
        /// <summary>Ukuran konteks sesi terakhir (prompt + cache tokens).</summary>
        public long LastContextTokens;
        public string LastContextModel;
        public DateTime LastContextTime = DateTime.MinValue;
        /// <summary>Path direktori kerja terakhir dan nama proyeknya.</summary>
        public string LastActiveCwd;
        public string LastActiveProject;
        /// <summary>Jumlah token yang dipakai dalam 10 menit terakhir (burn rate).</summary>
        public long TokensLast10m;
        public SessionStatusSummary Sessions = new SessionStatusSummary();
    }

    internal class SessionActivity
    {
        public string Id;
        public string Provider;
        public string Project;
        public string Cwd;
        public string State;
        public bool IsRunning;
        public bool NeedsConfirm;
        public bool FromHook;
        public DateTime UpdatedAt = DateTime.MinValue;
        public DateTime ConfirmAt = DateTime.MinValue;
        public DateTime DoneAt = DateTime.MinValue;
        public DateTime AckAt = DateTime.MinValue;
        public double SessionCost;
        public long SessionTokens;
        public double SessionCacheHit;
    }

    internal class SessionStatusSummary
    {
        public int RunningCount;
        public int ConfirmCount;
        public int DoneCount;
        public bool ClaudeRunning, ClaudeConfirm, ClaudeDone;
        public bool AntigravityRunning, AntigravityConfirm, AntigravityDone;
        public bool CodexRunning, CodexConfirm, CodexDone;
        public bool GeminiCliRunning, GeminiCliConfirm, GeminiCliDone;
        public bool OpenCodeRunning, OpenCodeConfirm, OpenCodeDone;
        public List<string> RunningLabels = new List<string>();
        public List<string> ConfirmLabels = new List<string>();
        public List<string> DoneLabels = new List<string>();
        public List<SessionActivity> Items = new List<SessionActivity>();
    }

    /// <summary>
    /// Membaca log Claude Code (~/.claude/projects/**/*.jsonl) secara inkremental per offset file.
    /// Dedup per message.id + requestId (lintas file), sama seperti ccusage. Hari = waktu lokal.
    /// Hanya field usage/model/timestamp yang dibaca; isi percakapan tidak disimpan.
    /// </summary>
    internal class UsageScanner
    {
        private const int KeepDays = 30;   // jendela pindai; "7 hari" dan "hari ini" dihitung dari subsetnya
        private const int CacheFormatVersion = 4;
        /// <summary>Folder 'projects' yang dipindai (default + folder tambahan + WSL). Diisi TrayApp.</summary>
        public Func<List<string>> Roots;
        public Func<List<string>> GeminiCliRoots;
        public Func<List<string>> OpenCodeDirs;
        public Func<List<string>> ClineRoots;
        public Func<List<string>> CopilotDirs;
        public Func<List<string>> CustomJsonlRoots;
        private readonly Dictionary<string, long> _offsets = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _loadedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _seen = new Dictionary<string, DateTime>();
        private readonly Dictionary<DateTime, Dictionary<string, Tally>> _days = new Dictionary<DateTime, Dictionary<string, Tally>>();
        private readonly Dictionary<DateTime, Dictionary<string, Tally>> _daysAg = new Dictionary<DateTime, Dictionary<string, Tally>>();
        private readonly Dictionary<DateTime, Dictionary<string, Tally>> _daysCodex = new Dictionary<DateTime, Dictionary<string, Tally>>();
        private readonly Dictionary<DateTime, Dictionary<string, Tally>> _daysGeminiCli = new Dictionary<DateTime, Dictionary<string, Tally>>();
        private readonly Dictionary<DateTime, Dictionary<string, Tally>> _daysOpenCode = new Dictionary<DateTime, Dictionary<string, Tally>>();
        private readonly Dictionary<DateTime, Dictionary<string, Tally>> _daysCline = new Dictionary<DateTime, Dictionary<string, Tally>>();
        private readonly Dictionary<DateTime, Dictionary<string, Tally>> _daysCopilot = new Dictionary<DateTime, Dictionary<string, Tally>>();
        private readonly Dictionary<DateTime, Dictionary<string, Tally>> _daysCustom = new Dictionary<DateTime, Dictionary<string, Tally>>();
        private readonly Dictionary<DateTime, Dictionary<string, Tally>> _proj = new Dictionary<DateTime, Dictionary<string, Tally>>();
        private readonly Dictionary<DateTime, Dictionary<string, Tally>> _projAg = new Dictionary<DateTime, Dictionary<string, Tally>>();
        private readonly Dictionary<DateTime, Dictionary<string, Tally>> _projCodex = new Dictionary<DateTime, Dictionary<string, Tally>>();
        private readonly Dictionary<DateTime, Dictionary<string, Tally>> _projGeminiCli = new Dictionary<DateTime, Dictionary<string, Tally>>();
        private readonly Dictionary<DateTime, Dictionary<string, Tally>> _projOpenCode = new Dictionary<DateTime, Dictionary<string, Tally>>();
        private readonly Dictionary<DateTime, Dictionary<string, Tally>> _projCline = new Dictionary<DateTime, Dictionary<string, Tally>>();
        private readonly Dictionary<DateTime, Dictionary<string, Tally>> _projCopilot = new Dictionary<DateTime, Dictionary<string, Tally>>();
        private readonly Dictionary<DateTime, Dictionary<string, Tally>> _projCustom = new Dictionary<DateTime, Dictionary<string, Tally>>();
        private readonly Dictionary<string, string> _projPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _codexFileModels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _codexFileCwds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SessionActivity> _sessions = new Dictionary<string, SessionActivity>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SessionCostRecord> _sessionCosts = new Dictionary<string, SessionCostRecord>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ToolUsageRecord> _tools = new Dictionary<string, ToolUsageRecord>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, CachedFileEntry> _diskCache = new Dictionary<string, CachedFileEntry>(StringComparer.OrdinalIgnoreCase);
        private CachedFileEntry _activeCache;
        private bool _cacheLoaded, _cacheDirty;
        private DateTime _openCodeDbStamp = DateTime.MinValue;
        private bool _openCodeSqliteMissingCli;
        /// <summary>Lacak total per proyek (nama folder terakhir dari 'cwd'). Default mati.</summary>
        public bool TrackProjects;
        public string CacheFilePath = Paths.ScanCacheFile;
        public int CacheHitFiles { get; private set; }
        public int DiskReadFiles { get; private set; }

        private static string ProjectName(string cwd)
        {
            if (string.IsNullOrWhiteSpace(cwd)) return "(unknown)";
            string norm = cwd.TrimEnd('\\', '/').Replace('/', '\\');
            string[] parts = norm.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3)
            {
                for (int k = parts.Length - 2; k >= 1; k--)
                {
                    if (string.Equals(parts[k], "worktrees", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(parts[k], ".worktrees", StringComparison.OrdinalIgnoreCase))
                    {
                        int parentIdx = (k >= 2 && string.Equals(parts[k - 1], ".claude", StringComparison.OrdinalIgnoreCase))
                            ? k - 2
                            : k - 1;
                        if (parentIdx >= 0 && parts[parentIdx].IndexOf(':') < 0)
                        {
                            string baseName = parts[parentIdx] + " (wt)";
                            return baseName.Length > 40 ? baseName.Substring(0, 40) : baseName;
                        }
                    }
                }
            }
            string leaf = parts.Length > 0 ? parts[parts.Length - 1] : norm;
            if (leaf.Length == 0) return "(unknown)";
            return leaf.Length > 40 ? leaf.Substring(0, 40) : leaf;
        }

        private void ClearAggregates()
        {
            _offsets.Clear(); _loadedFiles.Clear(); _seen.Clear();
            _days.Clear(); _daysAg.Clear(); _daysCodex.Clear(); _daysGeminiCli.Clear(); _daysOpenCode.Clear(); _daysCline.Clear(); _daysCopilot.Clear(); _daysCustom.Clear();
            _proj.Clear(); _projAg.Clear(); _projCodex.Clear(); _projGeminiCli.Clear(); _projOpenCode.Clear(); _projCline.Clear(); _projCopilot.Clear(); _projCustom.Clear();
            _projPaths.Clear(); _codexFileModels.Clear(); _codexFileCwds.Clear();
            _sessions.Clear(); _sessionCosts.Clear(); _tools.Clear();
            _diskCache.Clear(); _activeCache = null; _cacheLoaded = false; _cacheDirty = false;
            _openCodeDbStamp = DateTime.MinValue; _openCodeSqliteMissingCli = false;
            _codexLimitTime = DateTime.MinValue; _codex5h = new LimitWindow(); _codex7d = new LimitWindow();
            _recentTokens.Clear(); _antigravityRecentTokens.Clear();
            _lastContextTokens = 0; _lastContextModel = null; _lastContextTime = DateTime.MinValue;
            _lastActiveCwd = null; _lastActiveProject = null; _skipped = 0; _ready = false;
            CacheHitFiles = 0; DiskReadFiles = 0;
        }

        /// <summary>Nyalakan/matikan pelacakan proyek; perubahan memicu pindai ulang dari awal.</summary>
        public void SetTrackProjects(bool on)
        {
            lock (_gate)
            {
                if (TrackProjects == on) return;
                TrackProjects = on;
                ClearAggregates();
            }
        }
        private readonly object _gate = new object();
        private readonly byte[] _readBuf = new byte[1 << 20];
        private Pricing _pricing;
        private long _skipped;
        private DateTime _lastRecord = DateTime.MinValue;
        private bool _ready;
        private long _lastContextTokens;
        private string _lastContextModel;
        private DateTime _lastContextTime = DateTime.MinValue;
        private string _lastActiveCwd;
        private string _lastActiveProject;
        private DateTime _codexLimitTime = DateTime.MinValue;
        private LimitWindow _codex5h = new LimitWindow();
        private LimitWindow _codex7d = new LimitWindow();
        public bool ScanClaude = true;
        public bool ScanGemini = true;
        public bool ScanCodex = true;
        public bool ScanGeminiCli = true;
        public bool ScanOpenCode = true;
        public bool ScanCline = false;
        public bool ScanCopilot = false;
        public string CustomJsonlDirs = "";
        public long AntigravityQuota5hTokens = 1000000;
        public long AntigravityQuota7dTokens = 10000000;
        private readonly List<KeyValuePair<DateTime, long>> _recentTokens = new List<KeyValuePair<DateTime, long>>();
        private readonly List<KeyValuePair<DateTime, long>> _antigravityRecentTokens = new List<KeyValuePair<DateTime, long>>();

        public UsageScanner(Pricing pricing)
        {
            _pricing = pricing;
            Roots = delegate
            {
                List<string> r = new List<string>();
                if (ScanClaude && Directory.Exists(Paths.ProjectsDir)) r.Add(Paths.ProjectsDir);
                if (ScanGemini && Directory.Exists(Paths.AntigravityBrainDir)) r.Add(Paths.AntigravityBrainDir);
                if (ScanCodex && Directory.Exists(Paths.CodexSessionsDir)) r.Add(Paths.CodexSessionsDir);
                return r;
            };
            GeminiCliRoots = delegate
            {
                List<string> r = new List<string>();
                if (ScanGeminiCli && Directory.Exists(Paths.GeminiCliTmpDir)) r.Add(Paths.GeminiCliTmpDir);
                return r;
            };
            OpenCodeDirs = delegate
            {
                List<string> r = new List<string>();
                if (ScanOpenCode && Directory.Exists(Paths.OpenCodeDataDir)) r.Add(Paths.OpenCodeDataDir);
                return r;
            };
            ClineRoots = delegate { return ScanCline ? Paths.ClineTaskRoots() : new List<string>(); };
            CopilotDirs = delegate { return ScanCopilot ? Paths.CopilotDataDirs() : new List<string>(); };
            CustomJsonlRoots = delegate
            {
                List<string> r = new List<string>();
                if (!string.IsNullOrWhiteSpace(CustomJsonlDirs))
                {
                    foreach (string raw in CustomJsonlDirs.Split(';'))
                    {
                        string t = raw.Trim();
                        if (t.Length > 0 && Directory.Exists(t)) r.Add(t);
                    }
                }
                return r;
            };
        }

        /// <summary>Ganti tabel harga: hitung ulang semua dengan membaca ulang dari awal.</summary>
        public void ResetWithPricing(Pricing pricing)
        {
            lock (_gate)
            {
                _pricing = pricing;
                ClearAggregates();
            }
        }

        public void Scan()
        {
            lock (_gate)
            {
                if (!_cacheLoaded) LoadCache();
                DateTime cutoffUtc = DateTime.UtcNow.AddDays(-KeepDays);
                List<string> roots;
                try { roots = Roots(); } catch (Exception ex) { Log.Write("scan.roots", ex); roots = new List<string> { Paths.ProjectsDir }; }
                foreach (string root in roots)
                {
                    try
                    {
                        if (!Directory.Exists(root)) continue;
                        foreach (string f in SafeEnumerateFiles(root, "*.jsonl")) ScanOne(f, cutoffUtc);
                    }
                    catch (Exception ex) { Log.Write("scan.enumerate", ex); }
                }
                if (ScanGeminiCli) ScanGeminiCliDirs(cutoffUtc);
                if (ScanOpenCode) ScanOpenCodeDirs(cutoffUtc);
                if (ScanCline) ScanClineDirs(cutoffUtc);
                if (ScanCopilot) ScanCopilotDirs(cutoffUtc);
                ScanCustomJsonlDirs(cutoffUtc);
                Prune();
                _ready = true;
                if (_cacheDirty) SaveCache();
            }
        }

        private static IEnumerable<string> SafeEnumerateFiles(string root, string pattern)
        {
            Stack<string> stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                string dir = stack.Pop();
                string[] files = null, subdirs = null;
                try { files = Directory.GetFiles(dir, pattern); } catch { }
                if (files != null)
                {
                    for (int i = 0; i < files.Length; i++) yield return files[i];
                }
                try { subdirs = Directory.GetDirectories(dir); } catch { }
                if (subdirs != null)
                {
                    for (int i = 0; i < subdirs.Length; i++)
                    {
                        string name = Path.GetFileName(subdirs[i]);
                        if (string.Equals(name, "chunks", StringComparison.OrdinalIgnoreCase)) continue;
                        stack.Push(subdirs[i]);
                    }
                }
            }
        }

        /// <summary>Scan hanya file yang dilaporkan berubah oleh FileSystemWatcher (murah saat Claude Code aktif).</summary>
        public void ScanFiles(ICollection<string> paths)
        {
            lock (_gate)
            {
                if (!_cacheLoaded) LoadCache();
                DateTime cutoffUtc = DateTime.UtcNow.AddDays(-KeepDays);
                foreach (string f in paths)
                {
                    if (!File.Exists(f)) continue;
                    if (f.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)) ScanOne(f, cutoffUtc);
                    else if (ScanGeminiCli && Path.GetFileName(f).StartsWith("session-", StringComparison.OrdinalIgnoreCase) &&
                             f.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                        ScanWholeJsonFile(f, cutoffUtc, IngestGeminiCliJson);
                    else if (ScanOpenCode && f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                             (f.IndexOf("\\opencode\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              f.IndexOf("/opencode/", StringComparison.OrdinalIgnoreCase) >= 0))
                        ScanWholeJsonFile(f, cutoffUtc, IngestOpenCodeJson);
                    else if (ScanCline && string.Equals(Path.GetFileName(f), "ui_messages.json", StringComparison.OrdinalIgnoreCase))
                        ScanWholeJsonFile(f, cutoffUtc, IngestClineUiMessagesJson);
                    else if (ScanCopilot && f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                             (f.IndexOf("\\chatSessions\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              f.IndexOf("/chatSessions/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              f.IndexOf("\\.copilot\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              f.IndexOf("/.copilot/", StringComparison.OrdinalIgnoreCase) >= 0))
                        ScanWholeJsonFile(f, cutoffUtc, IngestCopilotFile);
                }
                if (_cacheDirty) SaveCache();
            }
        }

        private void ScanOne(string f, DateTime cutoffUtc)
        {
            try
            {
                if (f.EndsWith("transcript_full.jsonl", StringComparison.OrdinalIgnoreCase)) return;
                if (f.IndexOf("\\chunks\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    f.IndexOf("/chunks/", StringComparison.OrdinalIgnoreCase) >= 0)
                    return;
                FileInfo fi = new FileInfo(f);
                long off = 0;
                bool known = _offsets.TryGetValue(f, out off);
                if (!known && fi.LastWriteTimeUtc < cutoffUtc) return;   // file lama: abaikan

                CachedFileEntry ce;
                bool hasCached = _diskCache.TryGetValue(f, out ce);
                if (!known && !_loadedFiles.Contains(f) && hasCached)
                {
                    if (ce.Length == fi.Length && ce.LastWriteUtcTicks == fi.LastWriteTimeUtc.Ticks)
                    {
                        ReplayCacheEntry(f, ce);
                        _loadedFiles.Add(f);
                        _offsets[f] = ce.Offset;
                        CacheHitFiles++;
                        return;
                    }
                    if (ce.Offset > 0 && ce.Offset <= fi.Length)
                    {
                        ReplayCacheEntry(f, ce);
                        _loadedFiles.Add(f);
                        off = ce.Offset;
                    }
                    else
                    {
                        ce = new CachedFileEntry();
                        _diskCache[f] = ce;
                    }
                }

                if (fi.Length < off)
                {
                    off = 0;
                    ce = new CachedFileEntry();
                    _diskCache[f] = ce;
                }
                if (fi.Length == off || (known && hasCached && ce != null && ce.Length == fi.Length && ce.LastWriteUtcTicks == fi.LastWriteTimeUtc.Ticks))
                {
                    _offsets[f] = off;
                    return;
                }

                if (ce == null) { ce = new CachedFileEntry(); _diskCache[f] = ce; }
                _activeCache = ce;
                long newOff;
                try
                {
                    DiskReadFiles++;
                    newOff = ReadFrom(f, off);
                }
                finally
                {
                    _activeCache = null;
                }
                _offsets[f] = newOff;
                _loadedFiles.Add(f);
                ce.Length = fi.Length;
                ce.LastWriteUtcTicks = fi.LastWriteTimeUtc.Ticks;
                ce.Offset = newOff;
                string tmp;
                if (_codexFileModels.TryGetValue(f, out tmp)) ce.CodexModel = tmp;
                if (_codexFileCwds.TryGetValue(f, out tmp)) ce.CodexCwd = tmp;
                _cacheDirty = true;
            }
            catch (Exception ex) { Log.Write("scan.file", ex); }
        }

        private static string StripField(string line, string fieldName)
        {
            int c = line.IndexOf(fieldName, StringComparison.Ordinal);
            if (c < 0) return line;
            int v = c + fieldName.Length;
            while (v < line.Length && char.IsWhiteSpace(line[v])) v++;
            int end = SkipValue(line, v);
            if (end <= v) return line;
            return line.Substring(0, v) + "null" + line.Substring(end);
        }

        /// <summary>Buang nilai message.content (isi percakapan, bagian terbesar) sebelum parse JSON:
        /// lebih hemat memori/CPU dan isi percakapan tidak pernah dimuat sebagai objek.</summary>
        private static string StripContent(string line)
        {
            int m = line.IndexOf("\"message\":{", StringComparison.Ordinal);
            if (m < 0) return line;
            int c = line.IndexOf("\"content\":", m, StringComparison.Ordinal);
            if (c < 0) return line;
            int v = c + 10;
            while (v < line.Length && char.IsWhiteSpace(line[v])) v++;
            int end = SkipValue(line, v);
            if (end <= v) return line;
            return line.Substring(0, v) + "null" + line.Substring(end);
        }

        private static string StripAntigravity(string line)
        {
            line = StripField(line, "\"tool_calls\":");
            line = StripField(line, "\"content\":");
            line = StripField(line, "\"thinking\":");
            return line;
        }

        /// <summary>Indeks setelah satu nilai JSON yang dimulai di i (string/array/objek/primitif); -1 bila rusak.</summary>
        internal static int SkipValue(string s, int i)
        {
            if (i >= s.Length) return -1;
            char ch = s[i];
            if (ch == '"')
            {
                for (int k = i + 1; k < s.Length; k++)
                {
                    if (s[k] == '\\') { k++; continue; }
                    if (s[k] == '"') return k + 1;
                }
                return -1;
            }
            if (ch == '[' || ch == '{')
            {
                int depth = 0;
                for (int k = i; k < s.Length; k++)
                {
                    char x = s[k];
                    if (x == '"')
                    {
                        for (k = k + 1; k < s.Length; k++)
                        {
                            if (s[k] == '\\') { k++; continue; }
                            if (s[k] == '"') break;
                        }
                        continue;
                    }
                    if (x == '[' || x == '{') depth++;
                    else if (x == ']' || x == '}') { depth--; if (depth == 0) return k + 1; }
                }
                return -1;
            }
            int e = i;
            while (e < s.Length && s[e] != ',' && s[e] != '}' && s[e] != ']') e++;
            return e;
        }

        private long ReadFrom(string path, long offset)
        {
            const int Chunk = 1 << 20;
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan))
            using (MemoryStream carry = new MemoryStream())
            {
                fs.Seek(offset, SeekOrigin.Begin);
                byte[] buf = _readBuf;
                long consumed = offset;          // posisi setelah baris lengkap terakhir
                int n;
                while ((n = fs.Read(buf, 0, buf.Length)) > 0)
                {
                    int start = 0;
                    for (int i = 0; i < n; i++)
                    {
                        if (buf[i] != (byte)'\n') continue;
                        string line;
                        if (carry.Length > 0)
                        {
                            carry.Write(buf, start, i - start);
                            line = Encoding.UTF8.GetString(carry.GetBuffer(), 0, (int)carry.Length);
                            consumed += carry.Length + 1;
                            carry.SetLength(0);
                        }
                        else
                        {
                            line = Encoding.UTF8.GetString(buf, start, i - start);
                            consumed += (i - start) + 1;
                        }
                        ProcessLine(line, path);
                        start = i + 1;
                    }
                    if (start < n) carry.Write(buf, start, n - start);
                    if (carry.Capacity > 64 * Chunk && carry.Length == 0) carry.Capacity = 4096;
                }
                return consumed;   // baris terakhir yang belum lengkap akan dibaca lagi nanti
            }
        }

        private void ProcessLine(string line, string filePath)
        {
            if (line.IndexOf("\"PLANNER_RESPONSE\"", StringComparison.Ordinal) >= 0)
            {
                if (ScanGemini && ProcessAntigravityLine(line, filePath)) return;
                if (!ScanClaude || line.IndexOf("\"assistant\"", StringComparison.Ordinal) < 0) return;
            }
            else if (ScanGemini && (line.IndexOf("\"USER_INPUT\"", StringComparison.Ordinal) >= 0 ||
                                    line.IndexOf("\"GENERIC\"", StringComparison.Ordinal) >= 0))
            {
                if (ProcessAntigravityActivity(line, filePath)) return;
            }

            if (ScanCodex && (line.IndexOf("\"session_meta\"", StringComparison.Ordinal) >= 0 ||
                              line.IndexOf("\"turn_context\"", StringComparison.Ordinal) >= 0 ||
                              line.IndexOf("\"token_count\"", StringComparison.Ordinal) >= 0 ||
                              line.IndexOf("\"user_message\"", StringComparison.Ordinal) >= 0 ||
                              line.IndexOf("\"agent_reasoning\"", StringComparison.Ordinal) >= 0 ||
                              line.IndexOf("\"task_complete\"", StringComparison.Ordinal) >= 0 ||
                              line.IndexOf("\"turn_aborted\"", StringComparison.Ordinal) >= 0 ||
                              line.IndexOf("\"agent_message\"", StringComparison.Ordinal) >= 0 ||
                              line.IndexOf("\"exec_approval_request\"", StringComparison.Ordinal) >= 0 ||
                              line.IndexOf("\"apply_patch_approval_request\"", StringComparison.Ordinal) >= 0 ||
                              line.IndexOf("\"request_user_input\"", StringComparison.Ordinal) >= 0))
            {
                if (ProcessCodexLine(line, filePath)) return;
            }

            if (!ScanClaude) return;

            if (line.IndexOf("\"user\"", StringComparison.Ordinal) >= 0 &&
                (line.IndexOf("\"type\":\"user\"", StringComparison.Ordinal) >= 0 || line.IndexOf("\"type\": \"user\"", StringComparison.Ordinal) >= 0))
            {
                if (ProcessClaudeUserLine(line, filePath)) return;
            }

            // Saring murah sebelum parse JSON.
            bool noUsageConfirm = line.IndexOf("\"assistant\"", StringComparison.Ordinal) >= 0 &&
                                  line.IndexOf("\"usage\"", StringComparison.Ordinal) < 0 &&
                                  HasClaudeConfirmTool(line);
            if (line.IndexOf("\"usage\"", StringComparison.Ordinal) < 0 && !noUsageConfirm) return;
            if (line.IndexOf("\"assistant\"", StringComparison.Ordinal) < 0) return;

            List<string> turnTools = line.IndexOf("\"tool_use\"", StringComparison.Ordinal) >= 0
                ? ExtractClaudeToolNames(line)
                : null;

            Dictionary<string, object> o = null;
            try { o = J.Parse(StripContent(line)); }
            catch
            {
                try { o = J.Parse(line); } catch { o = null; }
            }
            if (o == null) { _skipped++; return; }
            if (J.Str(o, "type") != "assistant") return;

            Dictionary<string, object> msg = J.Obj(o, "message");
            DateTime ts;
            string tsRaw = J.Str(o, "timestamp");
            if (tsRaw == null || !DateTime.TryParse(tsRaw, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out ts))
            { _skipped++; return; }
            DateTime local = DateTime.SpecifyKind(ts, DateTimeKind.Utc).ToLocalTime();
            if (local.Date < DateTime.Today.AddDays(-(KeepDays - 1))) return;

            string rawCwd = J.Str(o, "cwd");
            string proj = ProjectName(rawCwd);
            string skey = SessionKey(filePath);

            Dictionary<string, object> u = J.Obj(msg, "usage");
            string model = J.Str(msg, "model");
            if (u != null && !string.IsNullOrEmpty(model) && model != "<synthetic>")
            {
                string id = J.Str(msg, "id");
                string req = J.Str(o, "requestId");
                bool alreadyCounted = false;
                if (id != null || req != null)
                {
                    string key = (id ?? "") + "|" + (req ?? "");
                    if (_seen.ContainsKey(key)) alreadyCounted = true;
                    else MarkSeen(key, local);
                }

                if (!alreadyCounted)
                {
                    Tally t = new Tally();
                    t.Requests = 1;
                    t.Input = J.Long(u, "input_tokens");
                    t.Output = J.Long(u, "output_tokens");
                    t.CacheRead = J.Long(u, "cache_read_input_tokens");
                    long cw = J.Long(u, "cache_creation_input_tokens");
                    Dictionary<string, object> cc = J.Obj(u, "cache_creation");
                    // Total cache_creation_input_tokens jadi acuan (rinciannya kadang tidak menjumlah ke total).
                    long cw1h = cc != null ? J.Long(cc, "ephemeral_1h_input_tokens") : 0;
                    if (cw == 0 && cc != null) cw = cw1h + J.Long(cc, "ephemeral_5m_input_tokens");
                    t.CacheWrite1h = Math.Min(cw1h, cw);
                    t.CacheWrite5m = cw - t.CacheWrite1h;
                    if (J.Str(u, "speed") == "fast") t.FastRequests = 1;

                    Price p = _pricing.Find(model);
                    if (p == null) t.UnpricedRequests = 1;
                    else
                    {
                        t.Cost = (t.Input * p.Input + t.Output * p.Output + t.CacheWrite5m * p.CacheWrite5m
                                  + t.CacheWrite1h * p.CacheWrite1h + t.CacheRead * p.CacheRead) / 1e6;
                        double saved = t.CacheRead * (p.Input - p.CacheRead);
                        if (p.CacheWrite5m > p.Input) saved -= t.CacheWrite5m * (p.CacheWrite5m - p.Input);
                        if (p.CacheWrite1h > p.Input) saved -= t.CacheWrite1h * (p.CacheWrite1h - p.Input);
                        t.Saved = saved / 1e6;
                    }

                    RecordUsage("Claude", _days, _proj, local, model, proj, rawCwd, t, skey, false);
                    UpdateContext(t.Input + t.CacheRead + cw, model, local, rawCwd);
                    RecordRecentToken(local, t.Total, false);
                    if (turnTools != null && turnTools.Count > 0)
                        RecordTurnTools("Claude", turnTools, t.Total, t.Cost, false);
                }
                else if (turnTools != null && turnTools.Count > 0)
                {
                    RecordTurnTools("Claude", turnTools, 0, 0, false);
                }
            }
            else if (turnTools != null && turnTools.Count > 0)
            {
                RecordTurnTools("Claude", turnTools, 0, 0, false);
            }

            if ((DateTime.Now - local).TotalMinutes <= 30 && !IsBackgroundHelperSession(filePath))
            {
                string stopReason = J.Str(msg, "stop_reason");
                bool asksUser = HasClaudeConfirmTool(line);
                if (asksUser)
                    RecordSessionConfirm(skey, "Claude", proj, local, false, rawCwd);
                else if (stopReason == "tool_use" || string.IsNullOrEmpty(stopReason))
                    RecordSessionRunning(skey, "Claude", proj, local, false, rawCwd);
                else
                    RecordSessionDone(skey, "Claude", proj, local, false, rawCwd);
            }
        }

        private static bool HasClaudeConfirmTool(string line)
        {
            if (string.IsNullOrEmpty(line)) return false;
            if (line.IndexOf("\"tool_use\"", StringComparison.Ordinal) < 0) return false;
            return line.IndexOf("\"AskUserQuestion\"", StringComparison.Ordinal) >= 0 ||
                   line.IndexOf("\"ExitPlanMode\"", StringComparison.Ordinal) >= 0;
        }

        private bool ProcessClaudeUserLine(string line, string filePath)
        {
            Dictionary<string, object> o = null;
            try { o = J.Parse(StripField(line, "\"content\":")); } catch { o = null; }
            if (o == null || J.Str(o, "type") != "user") return false;
            if (IsBackgroundHelperSession(filePath)) return true;
            DateTime ts;
            string tsRaw = J.Str(o, "timestamp");
            if (tsRaw == null || !DateTime.TryParse(tsRaw, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out ts))
                return true;
            DateTime local = DateTime.SpecifyKind(ts, DateTimeKind.Utc).ToLocalTime();
            if ((DateTime.Now - local).TotalMinutes <= 30)
                RecordSessionRunning(SessionKey(filePath), "Claude", ProjectName(J.Str(o, "cwd")), local, false);
            return true;
        }

        private bool ProcessAntigravityActivity(string line, string filePath)
        {
            if (IsBackgroundHelperSession(filePath)) return true;
            Dictionary<string, object> o = null;
            try { o = J.Parse(StripAntigravity(line)); } catch { o = null; }
            if (o == null) return false;
            string t = J.Str(o, "type");
            if (t != "USER_INPUT" && t != "GENERIC") return false;
            string source = J.Str(o, "source") ?? "";
            bool isUser = (t == "USER_INPUT") || source.StartsWith("USER", StringComparison.OrdinalIgnoreCase);
            string status = J.Str(o, "status") ?? "";
            if (!isUser && !string.Equals(status, "RUNNING", StringComparison.OrdinalIgnoreCase))
                return true;

            DateTime ts;
            string tsRaw = J.Str(o, "created_at");
            if (tsRaw != null && DateTime.TryParse(tsRaw, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out ts))
            {
                DateTime local = DateTime.SpecifyKind(ts, DateTimeKind.Utc).ToLocalTime();
                if ((DateTime.Now - local).TotalMinutes <= 30)
                {
                    string skey = SessionKey(filePath);
                    if (!isUser)
                    {
                        SessionActivity cur;
                        if (_sessions.TryGetValue(skey, out cur) && cur.NeedsConfirm && cur.ConfirmAt > cur.AckAt)
                            return true; // tool status GENERIC dari MODEL tidak boleh menghapus badge ?
                    }
                    RecordSessionRunning(skey, "Antigravity", "Antigravity", local, false);
                }
            }
            return true;
        }

        private static string ExtractFieldSlice(string line, string fieldName)
        {
            if (string.IsNullOrEmpty(line)) return null;
            int idx = line.IndexOf(fieldName, StringComparison.Ordinal);
            if (idx < 0) return null;
            int v = idx + fieldName.Length;
            while (v < line.Length && char.IsWhiteSpace(line[v])) v++;
            int end = SkipValue(line, v);
            if (end <= v) return null;
            return line.Substring(v, end - v);
        }

        private static bool ContainsToolName(string tcSlice, string toolName)
        {
            if (string.IsNullOrEmpty(tcSlice)) return false;
            return tcSlice.IndexOf("\"" + toolName + "\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   tcSlice.IndexOf(":" + toolName + "\"", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ContainsJsonBoolTrue(string slice, string key)
        {
            if (string.IsNullOrEmpty(slice)) return false;
            int pos = 0;
            string bare = "\"" + key + "\"";
            string esc = "\\\"" + key + "\\\"";
            while (pos < slice.Length)
            {
                int i1 = slice.IndexOf(bare, pos, StringComparison.OrdinalIgnoreCase);
                int i2 = slice.IndexOf(esc, pos, StringComparison.OrdinalIgnoreCase);
                int idx = (i1 >= 0 && i2 >= 0) ? Math.Min(i1, i2) : (i1 >= 0 ? i1 : i2);
                if (idx < 0) return false;
                int v = idx + (idx == i2 ? esc.Length : bare.Length);
                while (v < slice.Length && char.IsWhiteSpace(slice[v])) v++;
                if (v < slice.Length && slice[v] == ':')
                {
                    v++;
                    while (v < slice.Length && char.IsWhiteSpace(slice[v])) v++;
                    if (v < slice.Length && slice[v] == '\\') v++;
                    if (v < slice.Length && slice[v] == '"') v++;
                    if (v + 4 <= slice.Length && string.Compare(slice, v, "true", 0, 4, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        int after = v + 4;
                        if (after >= slice.Length || (!char.IsLetterOrDigit(slice[after]) && slice[after] != '_'))
                            return true;
                    }
                }
                pos = idx + bare.Length;
            }
            return false;
        }

        private static string FindFullTranscriptStepLine(string compactPath, long stepIndex)
        {
            if (string.IsNullOrEmpty(compactPath) || stepIndex <= 0) return null;
            try
            {
                string fullPath = Path.Combine(Path.GetDirectoryName(compactPath), "transcript_full.jsonl");
                if (!File.Exists(fullPath)) return null;
                string stepDigits = stepIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
                string needleCompact = "\"step_index\":" + stepDigits;
                string needleSpaced = "\"step_index\": " + stepDigits;
                FileInfo fi = new FileInfo(fullPath);
                long start = Math.Max(0, fi.Length - (2L << 20)); // periksa maks 2 MB terakhir
                using (FileStream fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (start > 0) fs.Seek(start, SeekOrigin.Begin);
                    using (StreamReader sr = new StreamReader(fs, Encoding.UTF8))
                    {
                        if (start > 0) sr.ReadLine(); // lewati baris terpotong
                        string match = null;
                        string l;
                        while ((l = sr.ReadLine()) != null)
                        {
                            if (l.IndexOf(needleCompact, StringComparison.Ordinal) >= 0 ||
                                l.IndexOf(needleSpaced, StringComparison.Ordinal) >= 0)
                                match = l;
                        }
                        return match;
                    }
                }
            }
            catch { return null; }
        }

        private bool ProcessAntigravityLine(string line, string filePath)
        {
            if (line.IndexOf("\"model\"", StringComparison.Ordinal) < 0) return false;

            Dictionary<string, object> o = null;
            try { o = J.Parse(StripAntigravity(line)); }
            catch
            {
                try { o = J.Parse(line); } catch { o = null; }
            }
            if (o == null) { _skipped++; return false; }
            if (J.Str(o, "type") != "PLANNER_RESPONSE") return false;

            string model = J.Str(o, "model");
            if (string.IsNullOrEmpty(model) || model == "<synthetic>") return true;

            DateTime ts;
            string tsRaw = J.Str(o, "created_at");
            if (tsRaw == null || !DateTime.TryParse(tsRaw, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out ts))
            { _skipped++; return true; }
            DateTime local = DateTime.SpecifyKind(ts, DateTimeKind.Utc).ToLocalTime();
            if (local.Date < DateTime.Today.AddDays(-(KeepDays - 1))) return true;

            long stepIndex = J.Long(o, "step_index");
            string key = "ag|" + (tsRaw ?? "") + "|" + stepIndex + "|" + model;
            if (_seen.ContainsKey(key)) return true;
            MarkSeen(key, local);

            Tally t = new Tally();
            t.Requests = 1;
            t.Input = J.Long(o, "input_tokens");
            t.Output = J.Long(o, "output_tokens");
            t.CacheRead = J.Long(o, "cache_read_tokens");

            Price p = _pricing.Find(model);
            if (p == null) t.UnpricedRequests = 1;
            else
            {
                t.Cost = (t.Input * p.Input + t.Output * p.Output + t.CacheRead * p.CacheRead) / 1e6;
                t.Saved = (t.CacheRead * (p.Input - p.CacheRead)) / 1e6;
            }

            string agSkey = SessionKey(filePath);
            RecordUsage("Antigravity", _daysAg, _projAg, local, model, "Antigravity", null, t, agSkey, false);
            UpdateContext(t.Input + t.CacheRead, model, local, null);
            RecordRecentToken(local, t.Total, true);
            string tcSlice = ExtractFieldSlice(line, "\"tool_calls\":");
            bool hasToolCalls = !string.IsNullOrEmpty(tcSlice) && HasNonEmptyArrayField(line, "\"tool_calls\":");
            if (!hasToolCalls && line.IndexOf("\"truncated_fields\"", StringComparison.Ordinal) >= 0 &&
                line.IndexOf("\"tool_calls\"", StringComparison.Ordinal) >= 0)
            {
                string fullLine = FindFullTranscriptStepLine(filePath, stepIndex);
                if (fullLine != null)
                {
                    tcSlice = ExtractFieldSlice(fullLine, "\"tool_calls\":");
                    hasToolCalls = !string.IsNullOrEmpty(tcSlice) && HasNonEmptyArrayField(fullLine, "\"tool_calls\":");
                }
            }
            if (hasToolCalls)
            {
                List<string> agTools = ExtractAntigravityToolNames(tcSlice);
                if (agTools.Count > 0) RecordTurnTools("Antigravity", agTools, t.Total, t.Cost, false);
            }
            if ((DateTime.Now - local).TotalMinutes <= 30 && !IsBackgroundHelperSession(filePath))
            {
                bool asksConfirm = hasToolCalls && (
                    ContainsToolName(tcSlice, "ask_question") ||
                    ContainsToolName(tcSlice, "request_user_input") ||
                    (ContainsToolName(tcSlice, "notify_user") && ContainsJsonBoolTrue(tcSlice, "BlockedOnUser")));

                if (asksConfirm)
                    RecordSessionConfirm(agSkey, "Antigravity", "Antigravity", local, false, Paths.AntigravityDir);
                else if (hasToolCalls)
                    RecordSessionRunning(agSkey, "Antigravity", "Antigravity", local, false, Paths.AntigravityDir);
                else
                    RecordSessionDone(agSkey, "Antigravity", "Antigravity", local, false, Paths.AntigravityDir);
            }
            return true;
        }

        private bool ProcessCodexLine(string line, string filePath)
        {
            Dictionary<string, object> o;
            try { o = J.Parse(line); }
            catch { return false; }
            if (o == null) return false;

            string type = J.Str(o, "type");
            if (type == "session_meta" || type == "turn_context")
            {
                Dictionary<string, object> p = J.Obj(o, "payload");
                string m = J.Str(p, "model") ?? J.Str(o, "model");
                string c = J.Str(p, "cwd") ?? J.Str(o, "cwd");
                if (!string.IsNullOrEmpty(filePath))
                {
                    if (!string.IsNullOrEmpty(m)) _codexFileModels[filePath] = m;
                    if (!string.IsNullOrEmpty(c)) _codexFileCwds[filePath] = c;
                }
                return true;
            }

            if (type != "event_msg") return false;
            Dictionary<string, object> payload = J.Obj(o, "payload");
            string ptype = J.Str(payload, "type");
            if (ptype == "user_message" || ptype == "agent_reasoning" || ptype == "task_complete" || ptype == "turn_aborted" || ptype == "agent_message" ||
                ptype == "exec_approval_request" || ptype == "apply_patch_approval_request" || ptype == "request_user_input")
            {
                DateTime evTs;
                string evTsRaw = J.Str(o, "timestamp");
                if (evTsRaw != null && DateTime.TryParse(evTsRaw, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out evTs))
                {
                    DateTime evLocal = DateTime.SpecifyKind(evTs, DateTimeKind.Utc).ToLocalTime();
                    if ((DateTime.Now - evLocal).TotalMinutes <= 30 && !IsBackgroundHelperSession(filePath))
                    {
                        string evCwd = null;
                        if (!string.IsNullOrEmpty(filePath)) _codexFileCwds.TryGetValue(filePath, out evCwd);
                        string evProj = string.IsNullOrEmpty(evCwd) ? "Codex CLI" : ProjectName(evCwd);
                        if (ptype == "exec_approval_request" || ptype == "apply_patch_approval_request" || ptype == "request_user_input")
                            RecordSessionConfirm(SessionKey(filePath), "Codex", evProj, evLocal, false, evCwd);
                        else if (ptype == "user_message" || ptype == "agent_reasoning")
                            RecordSessionRunning(SessionKey(filePath), "Codex", evProj, evLocal, false, evCwd);
                        else
                            RecordSessionDone(SessionKey(filePath), "Codex", evProj, evLocal, false, evCwd);
                    }
                }
                return true;
            }
            if (ptype != "token_count") return false;

            DateTime ts;
            string tsRaw = J.Str(o, "timestamp");
            if (tsRaw == null || !DateTime.TryParse(tsRaw, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out ts))
            { _skipped++; return true; }
            DateTime local = DateTime.SpecifyKind(ts, DateTimeKind.Utc).ToLocalTime();
            if (local.Date < DateTime.Today.AddDays(-(KeepDays - 1))) return true;

            Dictionary<string, object> info = J.Obj(payload, "info");

            Dictionary<string, object> rl = J.Obj(payload, "rate_limits") ?? J.Obj(info, "rate_limits");
            if (rl != null && local >= _codexLimitTime)
            {
                LimitWindow w5 = ParseCodexLimitWindow(J.Obj(rl, "primary"));
                LimitWindow w7 = ParseCodexLimitWindow(J.Obj(rl, "secondary"));
                if (w5.Available || w7.Available)
                {
                    _codexLimitTime = local;
                    if (w5.Available) _codex5h = w5;
                    if (w7.Available) _codex7d = w7;
                    if (_activeCache != null)
                    {
                        _activeCache.CodexLimitTicks = local.Ticks;
                        _activeCache.Codex5hAvail = _codex5h != null && _codex5h.Available;
                        if (_activeCache.Codex5hAvail)
                        {
                            _activeCache.Codex5hPct = _codex5h.UsedPercent;
                            _activeCache.Codex5hResetTicks = _codex5h.ResetsAt.Ticks;
                        }
                        _activeCache.Codex7dAvail = _codex7d != null && _codex7d.Available;
                        if (_activeCache.Codex7dAvail)
                        {
                            _activeCache.Codex7dPct = _codex7d.UsedPercent;
                            _activeCache.Codex7dResetTicks = _codex7d.ResetsAt.Ticks;
                        }
                    }
                }
            }

            Dictionary<string, object> last = J.Obj(info, "last_token_usage");
            if (last == null) return true;

            long rawInput = J.Long(last, "input_tokens");
            long cachedInput = J.Long(last, "cached_input_tokens");
            long outputTok = J.Long(last, "output_tokens");
            if (rawInput <= 0 && outputTok <= 0 && cachedInput <= 0) return true;

            string fKey = string.IsNullOrEmpty(filePath) ? "" : Path.GetFileName(filePath);
            string dedupKey = "codex|" + fKey + "|" + tsRaw + "|" + rawInput + "|" + outputTok + "|" + cachedInput;
            if (_seen.ContainsKey(dedupKey)) return true;
            MarkSeen(dedupKey, local);

            string model = null;
            if (!string.IsNullOrEmpty(filePath)) _codexFileModels.TryGetValue(filePath, out model);
            if (string.IsNullOrEmpty(model)) model = "gpt-5-codex";

            Tally t = new Tally();
            t.Requests = 1;
            t.Input = Math.Max(0, rawInput - cachedInput);
            t.CacheRead = cachedInput;
            t.Output = outputTok;

            Price pr = _pricing.Find(model);
            if (pr == null) t.UnpricedRequests = 1;
            else
            {
                t.Cost = (t.Input * pr.Input + t.Output * pr.Output + t.CacheRead * pr.CacheRead) / 1e6;
                t.Saved = (t.CacheRead * (pr.Input - pr.CacheRead)) / 1e6;
            }

            string rawCwd = null;
            if (!string.IsNullOrEmpty(filePath)) _codexFileCwds.TryGetValue(filePath, out rawCwd);
            string proj = string.IsNullOrEmpty(rawCwd) ? "Codex CLI" : ProjectName(rawCwd);
            string cxSkey = SessionKey(filePath);
            RecordUsage("Codex", _daysCodex, _projCodex, local, model, proj, rawCwd, t, cxSkey, false);
            UpdateContext(rawInput, model, local, rawCwd);
            RecordRecentToken(local, t.Total, false);
            return true;
        }

        private static LimitWindow ParseCodexLimitWindow(Dictionary<string, object> w)
        {
            LimitWindow r = new LimitWindow();
            if (w == null) return r;
            double used = J.Num(w, "used_percent");
            if (double.IsNaN(used)) used = J.Num(w, "used_percentage");
            if (double.IsNaN(used)) return r;
            r.Available = true;
            r.UsedPercent = Math.Max(0, Math.Min(100, used));
            double reset = J.Num(w, "resets_at");
            if (!double.IsNaN(reset))
            {
                r.ResetsAt = Fmt.FromUnix(reset);
            }
            else
            {
                string resetStr = J.Str(w, "resets_at");
                DateTime dt;
                if (!string.IsNullOrEmpty(resetStr) && DateTime.TryParse(resetStr, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out dt))
                {
                    r.ResetsAt = DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToLocalTime();
                }
            }
            return r;
        }

        private static void PruneDaysMap(Dictionary<DateTime, Dictionary<string, Tally>> map, DateTime min)
        {
            List<DateTime> old = new List<DateTime>();
            foreach (DateTime d in map.Keys) if (d < min) old.Add(d);
            foreach (DateTime d in old) map.Remove(d);
        }

        private void Prune()
        {
            DateTime min = DateTime.Today.AddDays(-(KeepDays - 1));
            PruneDaysMap(_days, min);
            PruneDaysMap(_daysAg, min);
            PruneDaysMap(_daysCodex, min);
            PruneDaysMap(_daysGeminiCli, min);
            PruneDaysMap(_daysOpenCode, min);
            PruneDaysMap(_daysCline, min);
            PruneDaysMap(_daysCopilot, min);
            PruneDaysMap(_daysCustom, min);
            PruneDaysMap(_proj, min);
            PruneDaysMap(_projAg, min);
            PruneDaysMap(_projCodex, min);
            PruneDaysMap(_projGeminiCli, min);
            PruneDaysMap(_projOpenCode, min);
            PruneDaysMap(_projCline, min);
            PruneDaysMap(_projCopilot, min);
            PruneDaysMap(_projCustom, min);
            if (_sessionCosts.Count > 0)
            {
                List<string> oldSess = new List<string>();
                foreach (KeyValuePair<string, SessionCostRecord> kv in _sessionCosts)
                    if (kv.Value.LastActivity.Date < min) oldSess.Add(kv.Key);
                foreach (string k in oldSess) _sessionCosts.Remove(k);
            }
            if (_seen.Count > 50000)
            {
                List<string> old = new List<string>();
                foreach (KeyValuePair<string, DateTime> kv in _seen) if (kv.Value.Date < min) old.Add(kv.Key);
                foreach (string k in old) _seen.Remove(k);
            }
            DateTime minRecent = DateTime.Now.AddMinutes(-20);
            _recentTokens.RemoveAll(delegate(KeyValuePair<DateTime, long> x) { return x.Key < minRecent; });
            DateTime minRecentAg = DateTime.Now.AddHours(-6);
            _antigravityRecentTokens.RemoveAll(delegate(KeyValuePair<DateTime, long> x) { return x.Key < minRecentAg; });
        }

        public static bool IsAntigravityModel(string model)
        {
            if (string.IsNullOrEmpty(model)) return false;
            return model.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase) ||
                   model.IndexOf("antigravity", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void AggregateProviderSnapshot(
            bool enabled,
            Dictionary<DateTime, Dictionary<string, Tally>> daysMap,
            DateTime weekStart,
            DateTime today,
            UsageSnapshot s,
            Dictionary<string, Tally> todayMap,
            Dictionary<string, Tally> weekMap,
            Tally todayTally,
            Tally weekTally,
            out DateTime oldest7d)
        {
            oldest7d = DateTime.MaxValue;
            if (!enabled) return;
            foreach (KeyValuePair<DateTime, Dictionary<string, Tally>> day in daysMap)
            {
                if (day.Key < weekStart) continue;
                long dTokens = 0;
                foreach (Tally t in day.Value.Values) dTokens += t.Total;
                if (dTokens > 0 && day.Key < oldest7d) oldest7d = day.Key;
                foreach (KeyValuePair<string, Tally> m in day.Value)
                {
                    AddTo(s.Week, m.Key, m.Value); s.WeekTotal.Add(m.Value);
                    AddTo(weekMap, m.Key, m.Value); weekTally.Add(m.Value);
                    if (day.Key == today)
                    {
                        AddTo(s.Today, m.Key, m.Value); s.TodayTotal.Add(m.Value);
                        AddTo(todayMap, m.Key, m.Value); todayTally.Add(m.Value);
                    }
                }
            }
        }

        private static double SumDayCost(Dictionary<DateTime, Dictionary<string, Tally>> map, DateTime date)
        {
            Dictionary<string, Tally> d;
            if (!map.TryGetValue(date, out d)) return 0;
            double sum = 0;
            foreach (Tally t in d.Values) sum += t.Cost;
            return sum;
        }

        public UsageSnapshot Snapshot()
        {
            lock (_gate)
            {
                UsageSnapshot s = new UsageSnapshot();
                DateTime today = DateTime.Today, weekStart = today.AddDays(-6);

                DateTime tmp;
                AggregateProviderSnapshot(ScanClaude, _days, weekStart, today, s, s.TodayClaude, s.WeekClaude, s.ClaudeToday, s.ClaudeWeek, out tmp);
                DateTime oldest7dAg;
                AggregateProviderSnapshot(ScanGemini, _daysAg, weekStart, today, s, s.TodayAntigravity, s.WeekAntigravity, s.AntigravityToday, s.AntigravityWeek, out oldest7dAg);
                AggregateProviderSnapshot(ScanCodex, _daysCodex, weekStart, today, s, s.TodayCodex, s.WeekCodex, s.CodexToday, s.CodexWeek, out tmp);
                AggregateProviderSnapshot(ScanGeminiCli, _daysGeminiCli, weekStart, today, s, s.TodayGeminiCli, s.WeekGeminiCli, s.GeminiCliToday, s.GeminiCliWeek, out tmp);
                AggregateProviderSnapshot(ScanOpenCode, _daysOpenCode, weekStart, today, s, s.TodayOpenCode, s.WeekOpenCode, s.OpenCodeToday, s.OpenCodeWeek, out tmp);
                AggregateProviderSnapshot(ScanCline, _daysCline, weekStart, today, s, s.TodayCline, s.WeekCline, s.ClineToday, s.ClineWeek, out tmp);
                AggregateProviderSnapshot(ScanCopilot, _daysCopilot, weekStart, today, s, s.TodayCopilot, s.WeekCopilot, s.CopilotToday, s.CopilotWeek, out tmp);
                AggregateProviderSnapshot(true, _daysCustom, weekStart, today, s, new Dictionary<string, Tally>(), new Dictionary<string, Tally>(), new Tally(), new Tally(), out tmp);

                HashSet<DateTime> allDates = new HashSet<DateTime>();
                if (ScanClaude) foreach (DateTime d in _days.Keys) allDates.Add(d);
                if (ScanGemini) foreach (DateTime d in _daysAg.Keys) allDates.Add(d);
                if (ScanCodex) foreach (DateTime d in _daysCodex.Keys) allDates.Add(d);
                if (ScanGeminiCli) foreach (DateTime d in _daysGeminiCli.Keys) allDates.Add(d);
                if (ScanOpenCode) foreach (DateTime d in _daysOpenCode.Keys) allDates.Add(d);
                if (ScanCline) foreach (DateTime d in _daysCline.Keys) allDates.Add(d);
                if (ScanCopilot) foreach (DateTime d in _daysCopilot.Keys) allDates.Add(d);
                foreach (DateTime d in _daysCustom.Keys) allDates.Add(d);
                foreach (DateTime d in allDates)
                {
                    Tally dt = new Tally();
                    Dictionary<string, Tally> md;
                    if (ScanClaude && _days.TryGetValue(d, out md)) foreach (Tally t in md.Values) dt.Add(t);
                    if (ScanGemini && _daysAg.TryGetValue(d, out md)) foreach (Tally t in md.Values) dt.Add(t);
                    if (ScanCodex && _daysCodex.TryGetValue(d, out md)) foreach (Tally t in md.Values) dt.Add(t);
                    if (ScanGeminiCli && _daysGeminiCli.TryGetValue(d, out md)) foreach (Tally t in md.Values) dt.Add(t);
                    if (ScanOpenCode && _daysOpenCode.TryGetValue(d, out md)) foreach (Tally t in md.Values) dt.Add(t);
                    if (ScanCline && _daysCline.TryGetValue(d, out md)) foreach (Tally t in md.Values) dt.Add(t);
                    if (ScanCopilot && _daysCopilot.TryGetValue(d, out md)) foreach (Tally t in md.Values) dt.Add(t);
                    if (_daysCustom.TryGetValue(d, out md)) foreach (Tally t in md.Values) dt.Add(t);

                    Dictionary<string, double> pCosts = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    double pc;
                    if (ScanClaude && (pc = SumDayCost(_days, d)) > 0) pCosts["Claude"] = pc;
                    if (ScanGemini && (pc = SumDayCost(_daysAg, d)) > 0) pCosts["Antigravity"] = pc;
                    if (ScanCodex && (pc = SumDayCost(_daysCodex, d)) > 0) pCosts["Codex"] = pc;
                    if (ScanGeminiCli && (pc = SumDayCost(_daysGeminiCli, d)) > 0) pCosts["Gemini CLI"] = pc;
                    if (ScanOpenCode && (pc = SumDayCost(_daysOpenCode, d)) > 0) pCosts["OpenCode"] = pc;
                    if (ScanCline && (pc = SumDayCost(_daysCline, d)) > 0) pCosts["Cline"] = pc;
                    if (ScanCopilot && (pc = SumDayCost(_daysCopilot, d)) > 0) pCosts["Copilot"] = pc;
                    if ((pc = SumDayCost(_daysCustom, d)) > 0) pCosts["Custom"] = pc;
                    s.Days.Add(new DayStat(d, dt.Total, dt.Cost, dt.Requests, pCosts.Count > 0 ? pCosts : null));
                }

                if (TrackProjects)
                {
                    if (ScanClaude) CollectProjects(_proj, weekStart, today, s, "Claude");
                    if (ScanGemini) CollectProjects(_projAg, weekStart, today, s, "Antigravity");
                    if (ScanCodex) CollectProjects(_projCodex, weekStart, today, s, "Codex");
                    if (ScanGeminiCli) CollectProjects(_projGeminiCli, weekStart, today, s, "Gemini CLI");
                    if (ScanOpenCode) CollectProjects(_projOpenCode, weekStart, today, s, "OpenCode");
                    if (ScanCline) CollectProjects(_projCline, weekStart, today, s, "Cline");
                    if (ScanCopilot) CollectProjects(_projCopilot, weekStart, today, s, "Copilot");
                    CollectProjects(_projCustom, weekStart, today, s, "Custom");
                }
                List<SessionCostRecord> scList = new List<SessionCostRecord>();
                foreach (SessionCostRecord sr in _sessionCosts.Values)
                {
                    if (!IsProviderScanned(sr.Provider)) continue;
                    if (sr.TotalTokens > 0 || sr.Cost > 0)
                        scList.Add(new SessionCostRecord
                        {
                            SessionId = sr.SessionId,
                            Provider = sr.Provider,
                            Project = sr.Project,
                            TopModel = sr.TopModel,
                            Input = sr.Input,
                            CacheRead = sr.CacheRead,
                            CacheWrite = sr.CacheWrite,
                            Output = sr.Output,
                            Requests = sr.Requests,
                            Cost = sr.Cost,
                            Saved = sr.Saved,
                            LastActivity = sr.LastActivity
                        });
                }
                scList.Sort(delegate(SessionCostRecord a, SessionCostRecord b)
                {
                    int c = b.Cost.CompareTo(a.Cost);
                    return c != 0 ? c : b.TotalTokens.CompareTo(a.TotalTokens);
                });
                if (scList.Count > 50) scList.RemoveRange(50, scList.Count - 50);
                s.ExpensiveSessions = scList;

                List<ToolUsageRecord> tList = new List<ToolUsageRecord>();
                foreach (ToolUsageRecord tr in _tools.Values)
                {
                    if (!IsProviderScanned(tr.Provider)) continue;
                    if (tr.Calls > 0)
                        tList.Add(new ToolUsageRecord { Provider = tr.Provider, Tool = tr.Tool, Calls = tr.Calls, Tokens = tr.Tokens, Cost = tr.Cost });
                }
                tList.Sort(delegate(ToolUsageRecord a, ToolUsageRecord b)
                {
                    int c = b.Tokens.CompareTo(a.Tokens);
                    return c != 0 ? c : b.Calls.CompareTo(a.Calls);
                });
                if (tList.Count > 60) tList.RemoveRange(60, tList.Count - 60);
                s.ToolBreakdown = tList;
                s.OpenCodeSqliteMissingCli = _openCodeSqliteMissingCli;
                s.CacheHitFiles = CacheHitFiles;
                s.DiskReadFiles = DiskReadFiles;

                s.ProjectPaths = new Dictionary<string, string>(_projPaths, StringComparer.OrdinalIgnoreCase);
                s.SkippedLines = _skipped;
                s.LastRecord = _lastRecord;
                s.ScannedAt = DateTime.Now;
                s.Ready = _ready;
                s.LastContextTokens = _lastContextTokens;
                s.LastContextModel = _lastContextModel;
                s.LastContextTime = _lastContextTime;
                s.LastActiveCwd = _lastActiveCwd;
                s.LastActiveProject = _lastActiveProject;
                DateTime cut10 = DateTime.Now.AddMinutes(-10);
                long t10 = 0;
                for (int i = 0; i < _recentTokens.Count; i++)
                {
                    if (_recentTokens[i].Key >= cut10) t10 += _recentTokens[i].Value;
                }
                s.TokensLast10m = t10;

                if (ScanGemini)
                {
                    DateTime cut5h = DateTime.Now.AddHours(-5);
                    long agTokens5h = 0;
                    DateTime oldest5h = DateTime.MaxValue;
                    for (int i = 0; i < _antigravityRecentTokens.Count; i++)
                    {
                        if (_antigravityRecentTokens[i].Key >= cut5h)
                        {
                            agTokens5h += _antigravityRecentTokens[i].Value;
                            if (_antigravityRecentTokens[i].Key < oldest5h)
                                oldest5h = _antigravityRecentTokens[i].Key;
                        }
                    }

                    if (agTokens5h > 0 || s.AntigravityWeek.Total > 0 || s.AntigravityToday.Total > 0)
                    {
                        long q5 = AntigravityQuota5hTokens > 0 ? AntigravityQuota5hTokens : 1000000;
                        s.AntigravityFiveHour.Available = true;
                        s.AntigravityFiveHour.UsedPercent = Math.Min(100.0, Math.Max(0.0, (double)agTokens5h / q5 * 100.0));
                        if (agTokens5h > 0 && oldest5h != DateTime.MaxValue)
                            s.AntigravityFiveHour.ResetsAt = oldest5h.AddHours(5);
                        else
                            s.AntigravityFiveHour.ResetsAt = DateTime.MinValue;

                        long q7 = AntigravityQuota7dTokens > 0 ? AntigravityQuota7dTokens : 10000000;
                        s.AntigravitySevenDay.Available = true;
                        s.AntigravitySevenDay.UsedPercent = Math.Min(100.0, Math.Max(0.0, (double)s.AntigravityWeek.Total / q7 * 100.0));
                        if (s.AntigravityWeek.Total > 0 && oldest7dAg != DateTime.MaxValue)
                            s.AntigravitySevenDay.ResetsAt = oldest7dAg.AddDays(7);
                        else
                            s.AntigravitySevenDay.ResetsAt = DateTime.MinValue;
                    }
                }

                if (ScanCodex)
                {
                    if (_codex5h != null && _codex5h.Available)
                    {
                        s.CodexFiveHour = new LimitWindow
                        {
                            Available = _codex5h.Available,
                            UsedPercent = _codex5h.UsedPercent,
                            ResetsAt = _codex5h.ResetsAt
                        };
                    }
                    if (_codex7d != null && _codex7d.Available)
                    {
                        s.CodexSevenDay = new LimitWindow
                        {
                            Available = _codex7d.Available,
                            UsedPercent = _codex7d.UsedPercent,
                            ResetsAt = _codex7d.ResetsAt
                        };
                    }
                }

                s.Sessions = BuildSessionSummary();
                return s;
            }
        }

        public void SyncHookSessions(ICollection<string> activeHookSids)
        {
            Dictionary<string, DateTime> map = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            if (activeHookSids != null)
            {
                DateTime now = DateTime.Now;
                foreach (string sid in activeHookSids)
                    if (!string.IsNullOrEmpty(sid)) map[sid] = now;
            }
            SyncHookSessions(map);
        }

        public void SyncHookSessions(IDictionary<string, DateTime> activeHooksLocal)
        {
            lock (_gate)
            {
                HashSet<string> active = activeHooksLocal != null
                    ? new HashSet<string>(activeHooksLocal.Keys, StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                DateTime now = DateTime.Now;
                if (activeHooksLocal != null)
                {
                    foreach (KeyValuePair<string, DateTime> kv in activeHooksLocal)
                    {
                        string sid = kv.Key;
                        if (string.IsNullOrEmpty(sid)) continue;
                        SessionActivity existing;
                        if (_sessions.TryGetValue(sid, out existing) && existing.NeedsConfirm && existing.ConfirmAt > existing.AckAt)
                        {
                            // Pertahankan badge ? sampai ada UserPromptSubmit baru setelah event konfirmasi
                            if (kv.Value <= existing.ConfirmAt.AddSeconds(1)) continue;
                        }
                        string proj = (existing != null && !string.IsNullOrEmpty(existing.Project))
                            ? existing.Project
                            : "Claude Code";
                        RecordSessionRunning(sid, "Claude", proj, now, true);
                    }
                }
                foreach (SessionActivity sa in _sessions.Values)
                {
                    if (sa.FromHook && sa.IsRunning && !active.Contains(sa.Id))
                    {
                        sa.IsRunning = false;
                        if (sa.DoneAt < sa.UpdatedAt) sa.DoneAt = now;
                    }
                }
            }
        }

        public void MarkHookFinished(string sid)
        {
            MarkHookState(sid, false);
        }

        public void MarkHookNeedsConfirm(string sid)
        {
            MarkHookState(sid, true);
        }

        private void MarkHookState(string sid, bool needsConfirm)
        {
            lock (_gate)
            {
                DateTime now = DateTime.Now;
                string key = string.IsNullOrEmpty(sid) ? null : sid;
                if (key == null)
                {
                    SessionActivity latest = null;
                    foreach (SessionActivity sa in _sessions.Values)
                    {
                        if (sa.Provider == "Claude" && (sa.IsRunning || sa.NeedsConfirm) && (latest == null || sa.UpdatedAt > latest.UpdatedAt))
                            latest = sa;
                    }
                    key = latest != null ? latest.Id : "__hook_latest";
                }
                SessionActivity existing;
                string proj = (_sessions.TryGetValue(key, out existing) && !string.IsNullOrEmpty(existing.Project))
                    ? existing.Project
                    : "Claude Code";
                if (needsConfirm) RecordSessionConfirm(key, "Claude", proj, now, true);
                else RecordSessionDone(key, "Claude", proj, now, true);
            }
        }

        public void AcknowledgeDoneSessions()
        {
            lock (_gate)
            {
                DateTime now = DateTime.Now;
                foreach (SessionActivity sa in _sessions.Values)
                {
                    if (!sa.IsRunning && sa.DoneAt > sa.AckAt) sa.AckAt = now;
                    if (sa.NeedsConfirm) { sa.NeedsConfirm = false; sa.AckAt = now; }
                }
            }
        }

        public void AcknowledgeSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return;
            lock (_gate)
            {
                SessionActivity sa;
                if (_sessions.TryGetValue(sessionId, out sa))
                {
                    DateTime now = DateTime.Now;
                    if (!sa.IsRunning && sa.DoneAt > sa.AckAt) sa.AckAt = now;
                    if (sa.NeedsConfirm) { sa.NeedsConfirm = false; sa.AckAt = now; }
                }
            }
        }

        private static bool IsBackgroundHelperSession(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            return filePath.IndexOf("observer-sessions", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   filePath.IndexOf("\\subagents\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   filePath.IndexOf("/subagents/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string SessionKey(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return "";
            string norm = filePath.Replace('/', '\\');
            int bIdx = norm.IndexOf("\\brain\\", StringComparison.OrdinalIgnoreCase);
            if (bIdx >= 0)
            {
                string rest = norm.Substring(bIdx + 7);
                int slash = rest.IndexOf('\\');
                string convId = slash > 0 ? rest.Substring(0, slash) : rest;
                if (!string.IsNullOrEmpty(convId)) return "ag:" + convId;
            }
            string stem = Path.GetFileNameWithoutExtension(norm);
            if (!string.IsNullOrEmpty(stem) && !string.Equals(stem, "transcript", StringComparison.OrdinalIgnoreCase))
                return stem;
            return norm;
        }

        private static bool HasNonEmptyArrayField(string line, string field)
        {
            int idx = line.IndexOf(field, StringComparison.Ordinal);
            if (idx < 0) return false;
            int i = idx + field.Length;
            while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
            if (i >= line.Length || line[i] != '[') return false;
            i++;
            while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
            return i < line.Length && line[i] != ']';
        }

        private SessionActivity GetOrCreateSession(string key, string provider, string project, string cwd)
        {
            SessionActivity sa;
            if (!_sessions.TryGetValue(key, out sa))
            {
                sa = new SessionActivity();
                sa.Id = key;
                _sessions[key] = sa;
            }
            if (!string.IsNullOrEmpty(provider)) sa.Provider = provider;
            if (!string.IsNullOrEmpty(project) && project != "(unknown)") sa.Project = project;
            else if (string.IsNullOrEmpty(sa.Project)) sa.Project = provider;
            if (!string.IsNullOrEmpty(cwd)) sa.Cwd = cwd;
            return sa;
        }

        private void RecordSessionRunning(string key, string provider, string project, DateTime localTs, bool fromHook, string cwd = null)
        {
            if (string.IsNullOrEmpty(key)) return;
            DateTime now = DateTime.Now;
            if (!fromHook)
            {
                double age = (now - localTs).TotalMinutes;
                if (age > (_ready ? 30 : 3)) return;
            }
            SessionActivity sa = GetOrCreateSession(key, provider, project, cwd);
            if (fromHook) sa.FromHook = true;
            if ((localTs >= sa.DoneAt && localTs >= sa.ConfirmAt) || fromHook)
            {
                sa.IsRunning = true;
                sa.NeedsConfirm = false;
                if (localTs > sa.UpdatedAt) sa.UpdatedAt = localTs;
                if (fromHook) sa.UpdatedAt = now;
                if (_activeCache != null)
                {
                    _activeCache.LastSessState = "running";
                    _activeCache.LastSessTicks = sa.UpdatedAt.Ticks;
                    _activeCache.LastSessProvider = sa.Provider;
                    _activeCache.LastSessProj = sa.Project;
                    _activeCache.LastSessCwd = sa.Cwd;
                }
            }
        }

        private void RecordSessionConfirm(string key, string provider, string project, DateTime localTs, bool fromHook, string cwd = null)
        {
            if (string.IsNullOrEmpty(key)) return;
            DateTime now = DateTime.Now;
            if (!fromHook && (now - localTs).TotalMinutes > 30) return;

            SessionActivity sa = GetOrCreateSession(key, provider, project, cwd);
            if (fromHook) sa.FromHook = false;
            DateTime stamp = fromHook ? now : (localTs > sa.ConfirmAt ? localTs : sa.ConfirmAt);
            if (stamp == DateTime.MinValue) stamp = now;
            if (fromHook || sa.UpdatedAt <= DateTime.MinValue.AddSeconds(60) || localTs >= sa.UpdatedAt.AddSeconds(-60))
            {
                sa.IsRunning = false;
                sa.NeedsConfirm = true;
                if (stamp > sa.ConfirmAt) sa.ConfirmAt = stamp;
                if (stamp > sa.UpdatedAt) sa.UpdatedAt = stamp;
                if (_activeCache != null)
                {
                    _activeCache.LastSessState = "confirm";
                    _activeCache.LastSessTicks = stamp.Ticks;
                    _activeCache.LastSessProvider = sa.Provider;
                    _activeCache.LastSessProj = sa.Project;
                    _activeCache.LastSessCwd = sa.Cwd;
                }
            }
        }

        private void RecordSessionDone(string key, string provider, string project, DateTime localTs, bool fromHook, string cwd = null)
        {
            if (string.IsNullOrEmpty(key)) return;
            DateTime now = DateTime.Now;
            if (!fromHook && (now - localTs).TotalMinutes > 30) return;

            SessionActivity sa = GetOrCreateSession(key, provider, project, cwd);
            if (fromHook) sa.FromHook = false;

            DateTime stamp = fromHook ? now : (localTs > sa.DoneAt ? localTs : sa.DoneAt);
            if (stamp == DateTime.MinValue) stamp = now;
            if (localTs >= sa.UpdatedAt || fromHook)
            {
                sa.IsRunning = false;
                sa.NeedsConfirm = false;
                if (stamp > sa.DoneAt) sa.DoneAt = stamp;
                // On startup historical backfill (before first Scan() finishes), don't surface old finished turns as unread checkmarks.
                if (!_ready && !fromHook && stamp > sa.AckAt) sa.AckAt = stamp;
                if (_activeCache != null)
                {
                    _activeCache.LastSessState = "done";
                    _activeCache.LastSessTicks = stamp.Ticks;
                    _activeCache.LastSessProvider = sa.Provider;
                    _activeCache.LastSessProj = sa.Project;
                    _activeCache.LastSessCwd = sa.Cwd;
                }
            }
        }

        private SessionActivity CopySessionActivity(SessionActivity sa, string state)
        {
            SessionCostRecord sr;
            double sCost = 0; long sTok = 0; double sCache = 0;
            if (_sessionCosts.TryGetValue((sa.Provider ?? "") + "|" + (sa.Id ?? ""), out sr))
            {
                sCost = sr.Cost;
                sTok = sr.TotalTokens;
                sCache = sr.CacheHit;
            }
            return new SessionActivity
            {
                Id = sa.Id,
                Provider = sa.Provider,
                Project = sa.Project,
                Cwd = sa.Cwd,
                State = state,
                IsRunning = state == "running",
                NeedsConfirm = state == "confirm",
                FromHook = sa.FromHook,
                UpdatedAt = sa.UpdatedAt,
                ConfirmAt = sa.ConfirmAt,
                DoneAt = sa.DoneAt,
                AckAt = sa.AckAt,
                SessionCost = sCost,
                SessionTokens = sTok,
                SessionCacheHit = sCache
            };
        }

        private static int StatePriority(string state)
        {
            if (state == "confirm") return 0;
            if (state == "running") return 1;
            return 2;
        }

        private SessionStatusSummary BuildSessionSummary()
        {
            SessionStatusSummary sum = new SessionStatusSummary();
            DateTime now = DateTime.Now;
            List<string> expired = null;

            foreach (SessionActivity sa in _sessions.Values)
            {
                if (sa.Provider == "Claude" && !ScanClaude) continue;
                if (sa.Provider == "Antigravity" && !ScanGemini) continue;
                if (sa.Provider == "Codex" && !ScanCodex) continue;
                if (sa.Provider == "Gemini CLI" && !ScanGeminiCli) continue;
                if (sa.Provider == "OpenCode" && !ScanOpenCode) continue;

                double runMins = (now - sa.UpdatedAt).TotalMinutes;
                double confirmMins = sa.ConfirmAt > DateTime.MinValue ? (now - sa.ConfirmAt).TotalMinutes : 9999;
                double doneMins = sa.DoneAt > DateTime.MinValue ? (now - sa.DoneAt).TotalMinutes : 9999;

                if (sa.NeedsConfirm && confirmMins <= 30 && sa.ConfirmAt > sa.AckAt)
                {
                    sum.ConfirmCount++;
                    if (sa.Provider == "Claude") sum.ClaudeConfirm = true;
                    else if (sa.Provider == "Antigravity") sum.AntigravityConfirm = true;
                    else if (sa.Provider == "Codex") sum.CodexConfirm = true;
                    else if (sa.Provider == "Gemini CLI") sum.GeminiCliConfirm = true;
                    else if (sa.Provider == "OpenCode") sum.OpenCodeConfirm = true;
                    sum.ConfirmLabels.Add(FormatSessionLabel(sa));
                    sum.Items.Add(CopySessionActivity(sa, "confirm"));
                }
                else if (sa.IsRunning)
                {
                    if (runMins <= (sa.FromHook ? 15 : 4))
                    {
                        sum.RunningCount++;
                        if (sa.Provider == "Claude") sum.ClaudeRunning = true;
                        else if (sa.Provider == "Antigravity") sum.AntigravityRunning = true;
                        else if (sa.Provider == "Codex") sum.CodexRunning = true;
                        else if (sa.Provider == "Gemini CLI") sum.GeminiCliRunning = true;
                        else if (sa.Provider == "OpenCode") sum.OpenCodeRunning = true;
                        sum.RunningLabels.Add(FormatSessionLabel(sa));
                        sum.Items.Add(CopySessionActivity(sa, "running"));
                    }
                    else
                    {
                        sa.IsRunning = false;
                    }
                }
                else if (sa.DoneAt > sa.AckAt && doneMins <= 30)
                {
                    sum.DoneCount++;
                    if (sa.Provider == "Claude") sum.ClaudeDone = true;
                    else if (sa.Provider == "Antigravity") sum.AntigravityDone = true;
                    else if (sa.Provider == "Codex") sum.CodexDone = true;
                    else if (sa.Provider == "Gemini CLI") sum.GeminiCliDone = true;
                    else if (sa.Provider == "OpenCode") sum.OpenCodeDone = true;
                    sum.DoneLabels.Add(FormatSessionLabel(sa));
                    sum.Items.Add(CopySessionActivity(sa, "done"));
                }
                else if (runMins > 60 && doneMins > 60 && confirmMins > 60)
                {
                    if (expired == null) expired = new List<string>();
                    expired.Add(sa.Id);
                }
            }
            if (expired != null) foreach (string k in expired) _sessions.Remove(k);
            sum.Items.Sort(delegate(SessionActivity a, SessionActivity b)
            {
                int p = StatePriority(a.State).CompareTo(StatePriority(b.State));
                return p != 0 ? p : b.UpdatedAt.CompareTo(a.UpdatedAt);
            });
            return sum;
        }

        private static string FormatSessionLabel(SessionActivity sa)
        {
            if (string.IsNullOrEmpty(sa.Project) || string.Equals(sa.Project, sa.Provider, StringComparison.OrdinalIgnoreCase))
                return sa.Provider;
            return sa.Provider + " · " + sa.Project;
        }

        private static void CollectProjects(Dictionary<DateTime, Dictionary<string, Tally>> src, DateTime weekStart, DateTime today, UsageSnapshot s, string provider)
        {
            foreach (KeyValuePair<DateTime, Dictionary<string, Tally>> pd in src)
            {
                if (pd.Key < weekStart) continue;
                foreach (KeyValuePair<string, Tally> m in pd.Value)
                {
                    AddTo(s.WeekProjects, m.Key, m.Value);
                    if (pd.Key == today) AddTo(s.TodayProjects, m.Key, m.Value);
                    string existing;
                    if (!s.ProjectProviders.TryGetValue(m.Key, out existing) || string.IsNullOrEmpty(existing))
                        s.ProjectProviders[m.Key] = provider;
                    else if (existing.IndexOf(provider, StringComparison.OrdinalIgnoreCase) < 0)
                        s.ProjectProviders[m.Key] = existing + " + " + provider;
                }
            }
        }

        private static void AddTo(Dictionary<string, Tally> d, string model, Tally t)
        {
            Tally acc;
            if (!d.TryGetValue(model, out acc)) { acc = new Tally(); d[model] = acc; }
            acc.Add(t);
        }

        private void RecordUsage(
            string provider,
            Dictionary<DateTime, Dictionary<string, Tally>> dayMap,
            Dictionary<DateTime, Dictionary<string, Tally>> projMap,
            DateTime local,
            string model,
            string proj,
            string rawCwd,
            Tally t,
            string sessionKey,
            bool fromReplay)
        {
            Dictionary<string, Tally> day;
            if (!dayMap.TryGetValue(local.Date, out day)) { day = new Dictionary<string, Tally>(); dayMap[local.Date] = day; }
            AddTo(day, model, t);

            if (TrackProjects)
            {
                string useProj = string.IsNullOrEmpty(proj) ? provider : proj;
                if (!string.IsNullOrEmpty(rawCwd) && !_projPaths.ContainsKey(useProj)) _projPaths[useProj] = rawCwd;
                Dictionary<string, Tally> pd;
                if (!projMap.TryGetValue(local.Date, out pd)) { pd = new Dictionary<string, Tally>(); projMap[local.Date] = pd; }
                AddTo(pd, useProj, t);
            }
            if (local > _lastRecord) _lastRecord = local;

            RecordSessionCost(sessionKey, provider, proj, model, t, local);

            if (!fromReplay && _activeCache != null)
            {
                _activeCache.MergeRow(provider, local.Date, model, proj, rawCwd, t);
                if (local.Ticks > _activeCache.LastRecordTicks) _activeCache.LastRecordTicks = local.Ticks;
            }
        }

        private static string ShortToolName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string s = raw.Trim();
            if (s.StartsWith("default_api:", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(12);
            if (s.StartsWith("mcp__", StringComparison.OrdinalIgnoreCase))
                s = "mcp:" + s.Substring(5).Replace("__", "/");
            if (s.Length > 48) s = s.Substring(0, 48);
            return s;
        }

        private static string ReadQuotedStringAfter(string text, int quoteStart)
        {
            if (string.IsNullOrEmpty(text) || quoteStart < 0 || quoteStart >= text.Length || text[quoteStart] != '"') return null;
            StringBuilder sb = new StringBuilder();
            for (int i = quoteStart + 1; i < text.Length && sb.Length < 100; i++)
            {
                char ch = text[i];
                if (ch == '\\' && i + 1 < text.Length) { i++; sb.Append(text[i]); continue; }
                if (ch == '"') return sb.ToString();
                sb.Append(ch);
            }
            return null;
        }

        private static List<string> ExtractClaudeToolNames(string line)
        {
            List<string> list = new List<string>();
            if (string.IsNullOrEmpty(line)) return list;
            int pos = 0;
            while (pos < line.Length && list.Count < 16)
            {
                int idx = line.IndexOf("\"tool_use\"", pos, StringComparison.Ordinal);
                if (idx < 0) break;
                int searchStart = Math.Max(0, idx - 60);
                int searchEnd = Math.Min(line.Length, idx + 280);
                int nIdx = line.IndexOf("\"name\"", idx, StringComparison.Ordinal);
                if (nIdx < 0 || nIdx >= searchEnd) nIdx = line.IndexOf("\"name\"", searchStart, idx - searchStart, StringComparison.Ordinal);
                if (nIdx >= 0 && nIdx < searchEnd)
                {
                    int colon = nIdx + 6;
                    while (colon < searchEnd && char.IsWhiteSpace(line[colon])) colon++;
                    if (colon < searchEnd && line[colon] == ':')
                    {
                        colon++;
                        while (colon < searchEnd && char.IsWhiteSpace(line[colon])) colon++;
                        string name = ShortToolName(ReadQuotedStringAfter(line, colon));
                        if (!string.IsNullOrEmpty(name)) list.Add(name);
                    }
                }
                pos = idx + 10;
            }
            return list;
        }

        private static List<string> ExtractAntigravityToolNames(string tcSlice)
        {
            List<string> list = new List<string>();
            if (string.IsNullOrEmpty(tcSlice)) return list;
            int pos = 0;
            while (pos < tcSlice.Length && list.Count < 16)
            {
                int nIdx = tcSlice.IndexOf("\"name\"", pos, StringComparison.Ordinal);
                if (nIdx < 0) break;
                int colon = nIdx + 6;
                while (colon < tcSlice.Length && char.IsWhiteSpace(tcSlice[colon])) colon++;
                if (colon < tcSlice.Length && tcSlice[colon] == ':')
                {
                    colon++;
                    while (colon < tcSlice.Length && char.IsWhiteSpace(tcSlice[colon])) colon++;
                    string name = ShortToolName(ReadQuotedStringAfter(tcSlice, colon));
                    if (!string.IsNullOrEmpty(name)) list.Add(name);
                }
                pos = nIdx + 6;
            }
            return list;
        }

        private void RecordTurnTools(string provider, List<string> tools, long turnTokens, double turnCost, bool fromReplay)
        {
            if (tools == null || tools.Count == 0) return;
            int n = tools.Count;
            long tokPer = n > 0 ? turnTokens / n : 0;
            double costPer = n > 0 ? turnCost / n : 0;
            for (int i = 0; i < n; i++)
            {
                long tok = tokPer + (i == 0 ? (turnTokens % n) : 0);
                double cost = costPer;
                RecordToolAggregate(provider, tools[i], 1, tok, cost, fromReplay);
            }
        }

        private void RecordToolAggregate(string provider, string toolName, long calls, long tokens, double cost, bool fromReplay)
        {
            if (string.IsNullOrEmpty(toolName)) return;
            string mapKey = provider + "|" + toolName;
            ToolUsageRecord tr;
            if (!_tools.TryGetValue(mapKey, out tr))
            {
                tr = new ToolUsageRecord { Provider = provider, Tool = toolName };
                _tools[mapKey] = tr;
            }
            tr.Calls += calls;
            tr.Tokens += tokens;
            tr.Cost += cost;
            if (!fromReplay && _activeCache != null)
                _activeCache.MergeTool(provider, toolName, calls, tokens, cost);
        }

        private void RecordSessionCost(string sessionKey, string provider, string project, string model, Tally t, DateTime local)
        {
            if (string.IsNullOrEmpty(sessionKey)) sessionKey = provider;
            string mapKey = provider + "|" + sessionKey;
            SessionCostRecord rec;
            if (!_sessionCosts.TryGetValue(mapKey, out rec))
            {
                rec = new SessionCostRecord();
                rec.SessionId = sessionKey;
                rec.Provider = provider;
                rec.Project = string.IsNullOrEmpty(project) ? provider : project;
                rec.TopModel = model;
                _sessionCosts[mapKey] = rec;
            }
            rec.Input += t.Input;
            rec.CacheRead += t.CacheRead;
            rec.CacheWrite += t.CacheWrite5m + t.CacheWrite1h;
            rec.Output += t.Output;
            rec.Requests += t.Requests;
            rec.Cost += t.Cost;
            rec.Saved += t.Saved;
            if (local >= rec.LastActivity)
            {
                rec.LastActivity = local;
                if (!string.IsNullOrEmpty(model)) rec.TopModel = model;
                if (!string.IsNullOrEmpty(project) && project != "(unknown)") rec.Project = project;
            }
        }

        private void UpdateContext(long ctxTokens, string model, DateTime local, string cwd)
        {
            if (local >= _lastContextTime)
            {
                _lastContextTokens = ctxTokens;
                _lastContextModel = model;
                _lastContextTime = local;
                if (!string.IsNullOrEmpty(cwd))
                {
                    _lastActiveCwd = cwd;
                    _lastActiveProject = ProjectName(cwd);
                }
                if (_activeCache != null)
                {
                    _activeCache.LastCtxTokens = ctxTokens;
                    _activeCache.LastCtxModel = model;
                    _activeCache.LastCtxCwd = cwd;
                    _activeCache.LastCtxTicks = local.Ticks;
                }
            }
        }

        private void MarkSeen(string key, DateTime local)
        {
            _seen[key] = local;
            if (_activeCache != null) _activeCache.AddSeen(key);
        }

        private static void AppendRecentCapped(List<long[]> list, long cutoffTicks, long ticks, long tokens)
        {
            list.RemoveAll(delegate(long[] x) { return x.Length < 2 || x[0] < cutoffTicks; });
            while (list.Count >= 500) list.RemoveAt(0);
            list.Add(new[] { ticks, tokens });
        }

        private void RecordRecentToken(DateTime local, long tokens, bool isAntigravity)
        {
            if (tokens <= 0) return;
            _recentTokens.Add(new KeyValuePair<DateTime, long>(local, tokens));
            if (isAntigravity) _antigravityRecentTokens.Add(new KeyValuePair<DateTime, long>(local, tokens));
            if (_activeCache != null && (DateTime.Now - local).TotalHours <= 6.0)
            {
                long cutoffTicks = DateTime.Now.AddHours(-6).Ticks;
                AppendRecentCapped(_activeCache.RecentTokens, cutoffTicks, local.Ticks, tokens);
                if (isAntigravity) AppendRecentCapped(_activeCache.RecentAgTokens, cutoffTicks, local.Ticks, tokens);
            }
        }

        private void ScanGeminiCliDirs(DateTime cutoffUtc)
        {
            List<string> roots = null;
            try { if (GeminiCliRoots != null) roots = GeminiCliRoots(); } catch (Exception ex) { Log.Write("gemcli.roots", ex); }
            if (roots == null) return;
            foreach (string root in roots)
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (string f in SafeEnumerateFiles(root, "session-*.json"))
                        ScanWholeJsonFile(f, cutoffUtc, IngestGeminiCliJson);
                }
                catch (Exception ex) { Log.Write("gemcli.enum", ex); }
            }
        }

        private void ScanOpenCodeDirs(DateTime cutoffUtc)
        {
            List<string> dirs = null;
            try { if (OpenCodeDirs != null) dirs = OpenCodeDirs(); } catch (Exception ex) { Log.Write("opencode.dirs", ex); }
            if (dirs == null) return;
            foreach (string dir in dirs)
            {
                try
                {
                    if (!Directory.Exists(dir)) continue;
                    string msgDir = Path.Combine(dir, Path.Combine("storage", "message"));
                    if (Directory.Exists(msgDir))
                    {
                        foreach (string f in SafeEnumerateFiles(msgDir, "*.json"))
                            ScanWholeJsonFile(f, cutoffUtc, IngestOpenCodeJson);
                    }
                    string dbPath = Path.Combine(dir, "opencode.db");
                    if (File.Exists(dbPath))
                        ScanOpenCodeSqlite(dbPath, cutoffUtc);
                }
                catch (Exception ex) { Log.Write("opencode.enum", ex); }
            }
        }

        private Tally BuildUsageTally(long inTok, long outTok, long cacheRead, long cacheWrite, double explicitCost, string model)
        {
            Tally t = new Tally();
            t.Requests = 1;
            t.Input = Math.Max(0, inTok);
            t.Output = Math.Max(0, outTok);
            t.CacheRead = Math.Max(0, cacheRead);
            t.CacheWrite5m = Math.Max(0, cacheWrite);
            Price pr = _pricing.Find(model);
            if (!double.IsNaN(explicitCost) && explicitCost > 0)
            {
                t.Cost = explicitCost;
                if (pr != null && t.CacheRead > 0 && pr.Input > pr.CacheRead)
                    t.Saved = (t.CacheRead * (pr.Input - pr.CacheRead)) / 1e6;
            }
            else if (pr == null)
            {
                t.UnpricedRequests = 1;
            }
            else
            {
                t.Cost = (t.Input * pr.Input + t.Output * pr.Output + t.CacheRead * pr.CacheRead + t.CacheWrite5m * pr.CacheWrite5m) / 1e6;
                if (t.CacheRead > 0 && pr.Input > pr.CacheRead)
                    t.Saved = (t.CacheRead * (pr.Input - pr.CacheRead)) / 1e6;
            }
            return t;
        }

        private static bool TryParseTimestampFlexible(Dictionary<string, object> obj, DateTime fallbackLocal, out DateTime local)
        {
            local = fallbackLocal;
            if (obj == null) return fallbackLocal != DateTime.MinValue;
            string[] sk = { "timestamp", "created_at", "creationDate", "time" };
            for (int i = 0; i < sk.Length; i++)
            {
                string raw = J.Str(obj, sk[i]);
                DateTime dt;
                if (!string.IsNullOrEmpty(raw) && DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out dt))
                {
                    local = DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToLocalTime();
                    return true;
                }
            }
            string[] nk = { "ts", "timestamp", "creationDate", "created" };
            for (int i = 0; i < nk.Length; i++)
            {
                double num = J.Num(obj, nk[i]);
                if (!double.IsNaN(num) && num > 1000000000)
                {
                    local = Fmt.FromUnix(num > 1e11 ? num / 1000.0 : num);
                    return true;
                }
            }
            return fallbackLocal != DateTime.MinValue;
        }

        private void ScanClineDirs(DateTime cutoffUtc)
        {
            List<string> roots = null;
            try { if (ClineRoots != null) roots = ClineRoots(); } catch (Exception ex) { Log.Write("cline.roots", ex); }
            if (roots == null) return;
            foreach (string root in roots)
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (string f in SafeEnumerateFiles(root, "ui_messages.json"))
                        ScanWholeJsonFile(f, cutoffUtc, IngestClineUiMessagesJson);
                }
                catch (Exception ex) { Log.Write("cline.enum", ex); }
            }
        }

        private void IngestClineUiMessagesJson(string json, string filePath)
        {
            System.Collections.IList arr = null;
            try { arr = J.Ser.DeserializeObject(json) as System.Collections.IList; } catch { _skipped++; return; }
            if (arr == null || arr.Count == 0) return;

            string taskId = Path.GetFileName(Path.GetDirectoryName(filePath)) ?? Path.GetFileNameWithoutExtension(filePath);
            string label = filePath.IndexOf("roo-cline", StringComparison.OrdinalIgnoreCase) >= 0 ? "Roo Code" : "Cline";
            DateTime minDate = DateTime.Today.AddDays(-(KeepDays - 1));
            DateTime fileLocal = DateTime.Now;
            try { fileLocal = File.GetLastWriteTime(filePath); } catch { }

            for (int i = 0; i < arr.Count; i++)
            {
                Dictionary<string, object> m = arr[i] as Dictionary<string, object>;
                if (m == null) continue;
                if (!string.Equals(J.Str(m, "say"), "api_req_started", StringComparison.OrdinalIgnoreCase)) continue;
                string rawText = J.Str(m, "text");
                if (string.IsNullOrWhiteSpace(rawText)) continue;
                Dictionary<string, object> meta = null;
                try { meta = J.Parse(rawText); } catch { }
                if (meta == null) continue;

                long inTok = J.Long(meta, "tokensIn");
                long outTok = J.Long(meta, "tokensOut");
                long cWrite = J.Long(meta, "cacheWrites");
                long cRead = J.Long(meta, "cacheReads");
                if (inTok <= 0 && outTok <= 0 && cWrite <= 0 && cRead <= 0) continue;

                DateTime local;
                if (!TryParseTimestampFlexible(m, fileLocal, out local)) continue;
                if (local.Date < minDate) continue;

                string model = J.Str(meta, "model") ?? J.Str(meta, "apiModelId") ?? "claude-sonnet-4-5";
                double explicitCost = J.Num(meta, "cost");
                string dedupKey = "cline|" + taskId + "|" + i + "|" + local.Ticks + "|" + inTok + "|" + outTok;
                if (_seen.ContainsKey(dedupKey)) continue;
                MarkSeen(dedupKey, local);

                Tally t = BuildUsageTally(inTok, outTok, cRead, cWrite, explicitCost, model);
                RecordUsage("Cline", _daysCline, _projCline, local, model, label, null, t, "cline:" + taskId, false);
                RecordRecentToken(local, t.Total, false);
            }
        }

        private void ScanCopilotDirs(DateTime cutoffUtc)
        {
            List<string> dirs = null;
            try { if (CopilotDirs != null) dirs = CopilotDirs(); } catch (Exception ex) { Log.Write("copilot.dirs", ex); }
            if (dirs == null) return;
            foreach (string dir in dirs)
            {
                try
                {
                    if (!Directory.Exists(dir)) continue;
                    string leaf = Path.GetFileName(dir.TrimEnd('\\', '/'));
                    if (string.Equals(leaf, ".copilot", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (string f in SafeEnumerateFiles(dir, "*.jsonl")) ScanWholeJsonFile(f, cutoffUtc, IngestCopilotFile);
                        foreach (string f in SafeEnumerateFiles(dir, "*.json")) ScanWholeJsonFile(f, cutoffUtc, IngestCopilotFile);
                    }
                    else
                    {
                        foreach (string ws in Directory.GetDirectories(dir))
                        {
                            string csDir = Path.Combine(ws, "chatSessions");
                            if (!Directory.Exists(csDir)) continue;
                            foreach (string f in SafeEnumerateFiles(csDir, "*.json"))
                                ScanWholeJsonFile(f, cutoffUtc, IngestCopilotFile);
                        }
                    }
                }
                catch (Exception ex) { Log.Write("copilot.enum", ex); }
            }
        }

        private static string TryReadVsCodeWorkspaceFolder(string sessionJsonPath)
        {
            try
            {
                string chatDir = Path.GetDirectoryName(sessionJsonPath);
                string wsDir = !string.IsNullOrEmpty(chatDir) ? Path.GetDirectoryName(chatDir) : null;
                if (string.IsNullOrEmpty(wsDir)) return null;
                string wsFile = Path.Combine(wsDir, "workspace.json");
                if (!File.Exists(wsFile)) return null;
                Dictionary<string, object> o = J.Parse(Paths.ReadAllTextShared(wsFile));
                string folderUri = J.Str(o, "folder");
                Uri u;
                if (!string.IsNullOrEmpty(folderUri) && Uri.TryCreate(folderUri, UriKind.Absolute, out u) && u.IsFile)
                    return u.LocalPath;
            }
            catch { }
            return null;
        }

        private void IngestCopilotFile(string content, string filePath)
        {
            if (string.IsNullOrWhiteSpace(content)) return;
            DateTime fileLocal = DateTime.Now;
            try { fileLocal = File.GetLastWriteTime(filePath); } catch { }
            DateTime minDate = DateTime.Today.AddDays(-(KeepDays - 1));
            string trimmed = content.TrimStart();
            if (trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                Dictionary<string, object> root = null;
                try { root = J.Parse(content); } catch { root = null; }
                if (root != null && J.Arr(root, "requests") != null)
                {
                    string sessId = J.Str(root, "sessionId") ?? Path.GetFileNameWithoutExtension(filePath);
                    string cwd = J.Str(root, "cwd") ?? TryReadVsCodeWorkspaceFolder(filePath);
                    string proj = !string.IsNullOrEmpty(cwd) ? ProjectName(cwd) : "GitHub Copilot";
                    System.Collections.IList reqs = J.Arr(root, "requests");
                    DateTime rootDt = fileLocal;
                    TryParseTimestampFlexible(root, fileLocal, out rootDt);
                    for (int i = 0; i < reqs.Count; i++)
                    {
                        Dictionary<string, object> req = reqs[i] as Dictionary<string, object>;
                        if (req == null) continue;
                        Dictionary<string, object> res = J.Obj(req, "result");
                        Dictionary<string, object> u = J.Obj(res, "usage") ?? J.Obj(req, "usage");
                        if (u == null) continue;
                        IngestCopilotUsageDict(u, req, rootDt, minDate, sessId + "|" + (J.Str(req, "requestId") ?? i.ToString(System.Globalization.CultureInfo.InvariantCulture)), proj, cwd, "cp:" + sessId);
                    }
                    return;
                }
            }
            string sidFallback = Path.GetFileNameWithoutExtension(filePath);
            string[] lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                Dictionary<string, object> d = null;
                try { d = J.Parse(lines[i]); } catch { continue; }
                if (d == null) continue;
                Dictionary<string, object> u = J.Obj(d, "usage") ?? J.Obj(d, "tokens");
                if (u == null) continue;
                string cwd = J.Str(d, "cwd");
                string proj = !string.IsNullOrEmpty(cwd) ? ProjectName(cwd) : "Copilot CLI";
                IngestCopilotUsageDict(u, d, fileLocal, minDate, sidFallback + "|" + i, proj, cwd, "cp:" + sidFallback);
            }
        }

        private void IngestCopilotUsageDict(Dictionary<string, object> u, Dictionary<string, object> meta, DateTime fallbackDt, DateTime minDate, string itemKey, string proj, string cwd, string sessKey)
        {
            long inTok = FirstPositive(u, "promptTokens", "prompt_tokens", "input_tokens", "input");
            long outTok = FirstPositive(u, "completionTokens", "completion_tokens", "output_tokens", "output");
            long cRead = FirstPositive(u, "cachedTokens", "cached_tokens", "cache_read_input_tokens", "cache_read");
            if (inTok <= 0 && outTok <= 0 && cRead <= 0) return;

            DateTime local;
            if (!TryParseTimestampFlexible(meta, fallbackDt, out local)) return;
            if (local.Date < minDate) return;

            string model = J.Str(meta, "modelId") ?? J.Str(meta, "model") ?? "gpt-4.1";
            double cost = J.Num(meta, "cost");
            string dedupKey = "copilot|" + itemKey + "|" + local.Ticks + "|" + inTok + "|" + outTok;
            if (_seen.ContainsKey(dedupKey)) return;
            MarkSeen(dedupKey, local);

            long netIn = (cRead > 0 && inTok >= cRead) ? inTok - cRead : inTok;
            Tally t = BuildUsageTally(netIn, outTok, cRead, 0, cost, model);
            RecordUsage("Copilot", _daysCopilot, _projCopilot, local, model, proj, cwd, t, sessKey, false);
            RecordRecentToken(local, t.Total, false);
        }

        private static long FirstPositive(Dictionary<string, object> d, params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                long v = J.Long(d, keys[i]);
                if (v > 0) return v;
            }
            return 0;
        }

        private void ScanCustomJsonlDirs(DateTime cutoffUtc)
        {
            List<string> dirs = null;
            try { if (CustomJsonlRoots != null) dirs = CustomJsonlRoots(); } catch (Exception ex) { Log.Write("customjsonl.roots", ex); }
            if (dirs == null) return;
            foreach (string dir in dirs)
            {
                try
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (string f in SafeEnumerateFiles(dir, "*.jsonl"))
                        ScanWholeJsonFile(f, cutoffUtc, IngestCustomJsonlFile);
                }
                catch (Exception ex) { Log.Write("customjsonl.enum", ex); }
            }
        }

        private void IngestCustomJsonlFile(string content, string filePath)
        {
            if (string.IsNullOrWhiteSpace(content)) return;
            DateTime fileLocal = DateTime.Now;
            try { fileLocal = File.GetLastWriteTime(filePath); } catch { }
            DateTime minDate = DateTime.Today.AddDays(-(KeepDays - 1));
            string stem = Path.GetFileNameWithoutExtension(filePath);
            string[] lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                Dictionary<string, object> d = null;
                try { d = J.Parse(lines[i]); } catch { _skipped++; continue; }
                if (d == null) continue;
                Dictionary<string, object> u = J.Obj(d, "usage") ?? J.Obj(d, "tokens") ?? d;
                long inTok = FirstPositive(u, "input_tokens", "prompt_tokens", "input", "tokensIn");
                long outTok = FirstPositive(u, "output_tokens", "completion_tokens", "output", "tokensOut");
                long cRead = FirstPositive(u, "cache_read_input_tokens", "cached_tokens", "cached_input_tokens", "cache_read", "cacheReads");
                long cWrite = FirstPositive(u, "cache_creation_input_tokens", "cache_write", "cacheWrites");
                if (inTok <= 0 && outTok <= 0 && cRead <= 0 && cWrite <= 0) continue;

                DateTime local;
                if (!TryParseTimestampFlexible(d, fileLocal, out local)) continue;
                if (local.Date < minDate) continue;

                string rawProv = J.Str(d, "provider");
                string prov = "Custom";
                if (string.Equals(rawProv, "Claude", StringComparison.OrdinalIgnoreCase)) prov = "Claude";
                else if (string.Equals(rawProv, "Antigravity", StringComparison.OrdinalIgnoreCase)) prov = "Antigravity";
                else if (string.Equals(rawProv, "Codex", StringComparison.OrdinalIgnoreCase)) prov = "Codex";
                else if (string.Equals(rawProv, "Gemini CLI", StringComparison.OrdinalIgnoreCase)) prov = "Gemini CLI";
                else if (string.Equals(rawProv, "OpenCode", StringComparison.OrdinalIgnoreCase)) prov = "OpenCode";
                else if (string.Equals(rawProv, "Cline", StringComparison.OrdinalIgnoreCase)) prov = "Cline";
                else if (string.Equals(rawProv, "Copilot", StringComparison.OrdinalIgnoreCase)) prov = "Copilot";

                string model = J.Str(d, "model") ?? J.Str(d, "modelId") ?? "custom";
                string cwd = J.Str(d, "cwd");
                string proj = J.Str(d, "project") ?? (!string.IsNullOrEmpty(cwd) ? ProjectName(cwd) : stem);
                double cost = J.Num(d, "cost");
                if (double.IsNaN(cost)) cost = J.Num(d, "cost_usd");
                string pathKey = string.IsNullOrEmpty(filePath) ? stem : filePath.Replace('/', '\\').ToLowerInvariant();
                string dedupKey = "custom|" + pathKey + "|" + i + "|" + local.Ticks + "|" + inTok + "|" + outTok;
                if (_seen.ContainsKey(dedupKey)) continue;
                MarkSeen(dedupKey, local);

                Tally t = BuildUsageTally(inTok, outTok, cRead, cWrite, cost, model);
                RecordUsage(prov, DaysMapForProvider(prov), ProjMapForProvider(prov), local, model, proj, cwd, t, "custom:" + pathKey, false);
                RecordRecentToken(local, t.Total, false);
            }
        }

        private void ScanWholeJsonFile(string f, DateTime cutoffUtc, Action<string, string> parser)
        {
            try
            {
                FileInfo fi = new FileInfo(f);
                if (!fi.Exists || fi.LastWriteTimeUtc < cutoffUtc) return;
                CachedFileEntry ce;
                bool hasCached = _diskCache.TryGetValue(f, out ce);
                bool alreadyLoaded = _loadedFiles.Contains(f);
                if (hasCached && ce.Length == fi.Length && ce.LastWriteUtcTicks == fi.LastWriteTimeUtc.Ticks)
                {
                    if (!alreadyLoaded)
                    {
                        ReplayCacheEntry(f, ce);
                        _loadedFiles.Add(f);
                        CacheHitFiles++;
                    }
                    return;
                }
                if (!alreadyLoaded || ce == null)
                {
                    ce = new CachedFileEntry();
                    _diskCache[f] = ce;
                }
                _activeCache = ce;
                try
                {
                    DiskReadFiles++;
                    string content = Paths.ReadAllTextShared(f);
                    parser(content, f);
                }
                finally
                {
                    _activeCache = null;
                }
                _loadedFiles.Add(f);
                ce.Length = fi.Length;
                ce.LastWriteUtcTicks = fi.LastWriteTimeUtc.Ticks;
                _cacheDirty = true;
            }
            catch (Exception ex) { Log.Write("scan.jsonfile", ex); }
        }

        private void IngestGeminiCliJson(string json, string filePath)
        {
            Dictionary<string, object> root = null;
            try { root = J.Parse(json); } catch { _skipped++; return; }
            if (root == null) return;
            System.Collections.IList msgs = J.Arr(root, "messages");
            if (msgs == null || msgs.Count == 0) return;

            string sessId = J.Str(root, "sessionId") ?? Path.GetFileNameWithoutExtension(filePath);
            string rootCwd = J.Str(root, "cwd") ?? J.Str(root, "workspace");
            string defaultProj = !string.IsNullOrEmpty(rootCwd) ? ProjectName(rootCwd) : "Gemini CLI";
            DateTime minDate = DateTime.Today.AddDays(-(KeepDays - 1));

            for (int i = 0; i < msgs.Count; i++)
            {
                Dictionary<string, object> m = msgs[i] as Dictionary<string, object>;
                if (m == null) continue;
                string mType = J.Str(m, "type");
                if (!string.Equals(mType, "gemini", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(mType, "model", StringComparison.OrdinalIgnoreCase)) continue;
                Dictionary<string, object> tok = J.Obj(m, "tokens");
                if (tok == null) continue;

                string model = J.Str(m, "model") ?? J.Str(root, "model") ?? "gemini-2.5-flash";
                string tsRaw = J.Str(m, "timestamp") ?? J.Str(root, "lastUpdated") ?? J.Str(root, "startTime");
                DateTime ts;
                if (tsRaw == null || !DateTime.TryParse(tsRaw, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out ts))
                    continue;
                DateTime local = DateTime.SpecifyKind(ts, DateTimeKind.Utc).ToLocalTime();
                if (local.Date < minDate) continue;

                long rawIn = J.Long(tok, "input");
                long cached = J.Long(tok, "cached");
                long outputTok = J.Long(tok, "output") + J.Long(tok, "thoughts");
                long totalTok = J.Long(tok, "total");
                if (rawIn <= 0 && outputTok <= 0 && cached <= 0) continue;
                long inTok = (totalTok > 0 && rawIn + cached + outputTok > totalTok + 10) ? Math.Max(0, rawIn - cached) : rawIn;

                string msgId = J.Str(m, "id");
                string dedupKey = "gemcli|" + sessId + "|" + (msgId ?? (i + "|" + tsRaw + "|" + outputTok));
                if (_seen.ContainsKey(dedupKey)) continue;
                MarkSeen(dedupKey, local);

                Tally t = new Tally();
                t.Requests = 1;
                t.Input = inTok;
                t.CacheRead = cached;
                t.Output = outputTok;

                Price pr = _pricing.Find(model);
                if (pr == null) t.UnpricedRequests = 1;
                else
                {
                    t.Cost = (t.Input * pr.Input + t.Output * pr.Output + t.CacheRead * pr.CacheRead) / 1e6;
                    t.Saved = (t.CacheRead * (pr.Input - pr.CacheRead)) / 1e6;
                }

                RecordUsage("Gemini CLI", _daysGeminiCli, _projGeminiCli, local, model, defaultProj, rootCwd, t, "gemcli:" + sessId, false);
                UpdateContext(inTok + cached, model, local, rootCwd);
                RecordRecentToken(local, t.Total, false);
            }
        }

        private void IngestOpenCodeJson(string json, string filePath)
        {
            Dictionary<string, object> d = null;
            try { d = J.Parse(json); } catch { _skipped++; return; }
            IngestOpenCodeMessageDict(d, filePath, 0);
        }

        private void IngestOpenCodeMessageDict(Dictionary<string, object> d, string sourceKey, long fallbackEpochMs)
        {
            if (d == null) return;
            string role = J.Str(d, "role");
            if (!string.IsNullOrEmpty(role) && !string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase)) return;

            Dictionary<string, object> tok = J.Obj(d, "tokens");
            if (tok == null) return;

            long inTok = J.Long(tok, "input");
            long outTok = J.Long(tok, "output") + J.Long(tok, "reasoning");
            Dictionary<string, object> cacheObj = J.Obj(tok, "cache");
            long cacheRead = cacheObj != null ? J.Long(cacheObj, "read") : 0;
            long cacheWrite = cacheObj != null ? J.Long(cacheObj, "write") : 0;
            if (inTok <= 0 && outTok <= 0 && cacheRead <= 0 && cacheWrite <= 0) return;

            double epochVal = double.NaN;
            Dictionary<string, object> time = J.Obj(d, "time");
            if (time != null)
            {
                epochVal = J.Num(time, "completed");
                if (double.IsNaN(epochVal)) epochVal = J.Num(time, "created");
            }
            if (double.IsNaN(epochVal) && fallbackEpochMs > 0) epochVal = fallbackEpochMs;
            if (double.IsNaN(epochVal)) return;
            double epochSecs = epochVal > 1e11 ? epochVal / 1000.0 : epochVal;
            DateTime local = Fmt.FromUnix(epochSecs);
            if (local.Date < DateTime.Today.AddDays(-(KeepDays - 1))) return;

            string sessId = J.Str(d, "sessionID") ?? J.Str(d, "session_id") ?? Path.GetFileNameWithoutExtension(sourceKey);
            string msgId = J.Str(d, "id");
            string dedupKey = "opencode|" + sessId + "|" + (msgId ?? (local.Ticks + "|" + inTok + "|" + outTok));
            if (_seen.ContainsKey(dedupKey)) return;
            MarkSeen(dedupKey, local);

            string model = J.Str(d, "modelID") ?? J.Str(J.Obj(d, "model"), "id") ?? J.Str(d, "model") ?? "opencode";
            Dictionary<string, object> pathObj = J.Obj(d, "path");
            string cwd = J.Str(pathObj, "cwd") ?? J.Str(d, "cwd");
            string proj = string.IsNullOrEmpty(cwd) ? "OpenCode" : ProjectName(cwd);

            double explicitCost = J.Num(d, "cost");
            Tally t = BuildUsageTally(inTok, outTok, cacheRead, cacheWrite, explicitCost, model);

            RecordUsage("OpenCode", _daysOpenCode, _projOpenCode, local, model, proj, cwd, t, "oc:" + sessId, false);
            UpdateContext(inTok + cacheRead + cacheWrite, model, local, cwd);
            RecordRecentToken(local, t.Total, false);
        }

        private void ScanOpenCodeSqlite(string dbPath, DateTime cutoffUtc)
        {
            if (string.IsNullOrWhiteSpace(dbPath) || dbPath.IndexOfAny(new[] { '"', '\r', '\n', '\0' }) >= 0)
                return;
            string fullDb;
            try { fullDb = Path.GetFullPath(dbPath); } catch { return; }
            if (fullDb.StartsWith("-", StringComparison.Ordinal) ||
                (!fullDb.EndsWith(".db", StringComparison.OrdinalIgnoreCase) &&
                 !fullDb.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase) &&
                 !fullDb.EndsWith(".sqlite3", StringComparison.OrdinalIgnoreCase)))
                return;

            string sqlite = ProcessHelper.FindExecutableOnPath("sqlite3.exe");
            if (string.IsNullOrEmpty(sqlite))
            {
                _openCodeSqliteMissingCli = true;
                return;
            }
            _openCodeSqliteMissingCli = false;
            try
            {
                DateTime stamp = File.GetLastWriteTimeUtc(fullDb);
                string wal = fullDb + "-wal";
                if (File.Exists(wal))
                {
                    DateTime w = File.GetLastWriteTimeUtc(wal);
                    if (w > stamp) stamp = w;
                }
                if (stamp <= _openCodeDbStamp) return;
                _openCodeDbStamp = stamp;

                long cutoffMs = (long)(cutoffUtc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
                string sql = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "SELECT id, session_id, time_created, data FROM message WHERE time_created >= {0} ORDER BY time_created DESC LIMIT 5000;",
                    cutoffMs);
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(
                    sqlite, "-readonly -json \"" + fullDb + "\" \"" + sql + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                using (System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi))
                {
                    System.Threading.Tasks.Task<string> readTask = System.Threading.Tasks.Task.Run(delegate
                    {
                        try { return p.StandardOutput.ReadToEnd(); } catch { return ""; }
                    });
                    if (!p.WaitForExit(5000)) { try { p.Kill(); } catch { } readTask.Wait(200); return; }
                    string jsonOut = readTask.Wait(1000) ? readTask.Result : "";
                    if (p.ExitCode != 0 || string.IsNullOrWhiteSpace(jsonOut)) return;
                    System.Collections.IList rows = J.Ser.DeserializeObject(jsonOut) as System.Collections.IList;
                    if (rows == null) return;
                    foreach (object o in rows)
                    {
                        Dictionary<string, object> r = o as Dictionary<string, object>;
                        string rawData = J.Str(r, "data");
                        if (string.IsNullOrEmpty(rawData)) continue;
                        Dictionary<string, object> msg = null;
                        try { msg = J.Parse(rawData); } catch { }
                        if (msg == null) continue;
                        if (!msg.ContainsKey("id") && J.Has(r, "id")) msg["id"] = J.Str(r, "id");
                        if (!msg.ContainsKey("sessionID") && J.Has(r, "session_id")) msg["sessionID"] = J.Str(r, "session_id");
                        IngestOpenCodeMessageDict(msg, dbPath, J.Long(r, "time_created"));
                    }
                }
            }
            catch (Exception ex) { Log.Write("opencode.sqlite", ex); }
        }

        internal bool IsProviderScanned(string provider)
        {
            if (provider == "Claude") return ScanClaude;
            if (provider == "Antigravity") return ScanGemini;
            if (provider == "Codex") return ScanCodex;
            if (provider == "Gemini CLI") return ScanGeminiCli;
            if (provider == "OpenCode") return ScanOpenCode;
            if (provider == "Cline") return ScanCline;
            if (provider == "Copilot") return ScanCopilot;
            return true;
        }

        private class CachedTallyRow
        {
            public string DateKey, Provider, Model, Proj, RawCwd;
            public long Input, Output, Cw5, Cw1h, CacheRead, Req, Fast, Unp;
            public double Cost, Saved;
        }

        private class CachedToolRow
        {
            public string Provider, Tool;
            public long Calls, Tokens;
            public double Cost;
        }

        private class CachedFileEntry
        {
            public long Length, LastWriteUtcTicks, Offset, LastRecordTicks;
            public string CodexModel, CodexCwd;
            public long LastCtxTokens, LastCtxTicks;
            public string LastCtxModel, LastCtxCwd;
            public long CodexLimitTicks;
            public double Codex5hPct, Codex7dPct;
            public long Codex5hResetTicks, Codex7dResetTicks;
            public bool Codex5hAvail, Codex7dAvail;
            public string LastSessState, LastSessProvider, LastSessProj, LastSessCwd;
            public long LastSessTicks;
            public List<CachedTallyRow> Rows = new List<CachedTallyRow>();
            public List<CachedToolRow> Tools = new List<CachedToolRow>();
            public List<long[]> RecentTokens = new List<long[]>();
            public List<long[]> RecentAgTokens = new List<long[]>();
            public List<string> SeenKeys = new List<string>();

            public void AddSeen(string key)
            {
                if (string.IsNullOrEmpty(key)) return;
                if (SeenKeys.Count >= 4000) SeenKeys.RemoveAt(0);
                SeenKeys.Add(key);
            }

            public void MergeTool(string provider, string tool, long calls, long tokens, double cost)
            {
                for (int i = 0; i < Tools.Count; i++)
                {
                    CachedToolRow tr = Tools[i];
                    if (tr.Provider == provider && tr.Tool == tool)
                    {
                        tr.Calls += calls; tr.Tokens += tokens; tr.Cost += cost;
                        return;
                    }
                }
                Tools.Add(new CachedToolRow { Provider = provider, Tool = tool, Calls = calls, Tokens = tokens, Cost = cost });
            }

            public void MergeRow(string provider, DateTime date, string model, string proj, string rawCwd, Tally t)
            {
                string dk = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                string useProj = proj ?? "";
                for (int i = 0; i < Rows.Count; i++)
                {
                    CachedTallyRow r = Rows[i];
                    if (r.DateKey == dk && r.Provider == provider && r.Model == model && r.Proj == useProj)
                    {
                        r.Input += t.Input; r.Output += t.Output; r.Cw5 += t.CacheWrite5m; r.Cw1h += t.CacheWrite1h;
                        r.CacheRead += t.CacheRead; r.Req += t.Requests; r.Fast += t.FastRequests; r.Unp += t.UnpricedRequests;
                        r.Cost += t.Cost; r.Saved += t.Saved;
                        if (string.IsNullOrEmpty(r.RawCwd) && !string.IsNullOrEmpty(rawCwd)) r.RawCwd = rawCwd;
                        return;
                    }
                }
                CachedTallyRow nr = new CachedTallyRow();
                nr.DateKey = dk; nr.Provider = provider; nr.Model = model; nr.Proj = useProj; nr.RawCwd = rawCwd;
                nr.Input = t.Input; nr.Output = t.Output; nr.Cw5 = t.CacheWrite5m; nr.Cw1h = t.CacheWrite1h;
                nr.CacheRead = t.CacheRead; nr.Req = t.Requests; nr.Fast = t.FastRequests; nr.Unp = t.UnpricedRequests;
                nr.Cost = t.Cost; nr.Saved = t.Saved;
                Rows.Add(nr);
            }
        }

        private class ScanCacheRoot
        {
            public int Version;
            public string PricingStamp;
            public bool TrackProjects;
            public int ScanFlags;
            public Dictionary<string, CachedFileEntry> Files = new Dictionary<string, CachedFileEntry>(StringComparer.OrdinalIgnoreCase);
        }

        private int CurrentScanFlags()
        {
            return (ScanClaude ? 1 : 0) | (ScanGemini ? 2 : 0) | (ScanCodex ? 4 : 0) |
                   (ScanGeminiCli ? 8 : 0) | (ScanOpenCode ? 16 : 0) |
                   (ScanCline ? 32 : 0) | (ScanCopilot ? 64 : 0);
        }

        private Dictionary<DateTime, Dictionary<string, Tally>> DaysMapForProvider(string provider)
        {
            if (provider == "Antigravity") return _daysAg;
            if (provider == "Codex") return _daysCodex;
            if (provider == "Gemini CLI") return _daysGeminiCli;
            if (provider == "OpenCode") return _daysOpenCode;
            if (provider == "Cline") return _daysCline;
            if (provider == "Copilot") return _daysCopilot;
            if (provider == "Custom") return _daysCustom;
            return _days;
        }

        private Dictionary<DateTime, Dictionary<string, Tally>> ProjMapForProvider(string provider)
        {
            if (provider == "Antigravity") return _projAg;
            if (provider == "Codex") return _projCodex;
            if (provider == "Gemini CLI") return _projGeminiCli;
            if (provider == "OpenCode") return _projOpenCode;
            if (provider == "Cline") return _projCline;
            if (provider == "Copilot") return _projCopilot;
            if (provider == "Custom") return _projCustom;
            return _proj;
        }

        private void ReplayCacheEntry(string filePath, CachedFileEntry ce)
        {
            if (ce == null) return;
            DateTime minDate = DateTime.Today.AddDays(-(KeepDays - 1));
            string skey = SessionKey(filePath);
            for (int i = 0; i < ce.Rows.Count; i++)
            {
                CachedTallyRow r = ce.Rows[i];
                DateTime dt;
                if (!DateTime.TryParseExact(r.DateKey, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out dt) || dt.Date < minDate)
                    continue;
                Tally t = new Tally();
                t.Input = r.Input; t.Output = r.Output; t.CacheWrite5m = r.Cw5; t.CacheWrite1h = r.Cw1h;
                t.CacheRead = r.CacheRead; t.Requests = r.Req; t.FastRequests = r.Fast; t.UnpricedRequests = r.Unp;
                t.Cost = r.Cost; t.Saved = r.Saved;
                RecordUsage(r.Provider, DaysMapForProvider(r.Provider), ProjMapForProvider(r.Provider),
                    dt.Date, r.Model, r.Proj, r.RawCwd, t, skey, true);
            }
            if (ce.Tools != null)
            {
                for (int i = 0; i < ce.Tools.Count; i++)
                {
                    CachedToolRow tr = ce.Tools[i];
                    RecordToolAggregate(tr.Provider, tr.Tool, tr.Calls, tr.Tokens, tr.Cost, true);
                }
            }
            if (!string.IsNullOrEmpty(ce.CodexModel)) _codexFileModels[filePath] = ce.CodexModel;
            if (!string.IsNullOrEmpty(ce.CodexCwd)) _codexFileCwds[filePath] = ce.CodexCwd;
            if (ce.LastRecordTicks > 0)
            {
                DateTime lr = new DateTime(ce.LastRecordTicks);
                if (lr > _lastRecord) _lastRecord = lr;
            }
            if (ce.LastCtxTicks > 0)
            {
                DateTime ctxDt = new DateTime(ce.LastCtxTicks);
                if (ctxDt >= _lastContextTime)
                {
                    _lastContextTokens = ce.LastCtxTokens;
                    _lastContextModel = ce.LastCtxModel;
                    _lastContextTime = ctxDt;
                    if (!string.IsNullOrEmpty(ce.LastCtxCwd))
                    {
                        _lastActiveCwd = ce.LastCtxCwd;
                        _lastActiveProject = ProjectName(ce.LastCtxCwd);
                    }
                }
            }
            if (!string.IsNullOrEmpty(ce.LastSessState) && ce.LastSessTicks > 0 && !IsBackgroundHelperSession(filePath))
            {
                DateTime sdt = new DateTime(ce.LastSessTicks);
                if (ce.LastSessState == "confirm")
                    RecordSessionConfirm(skey, ce.LastSessProvider, ce.LastSessProj, sdt, false, ce.LastSessCwd);
                else if (ce.LastSessState == "running")
                    RecordSessionRunning(skey, ce.LastSessProvider, ce.LastSessProj, sdt, false, ce.LastSessCwd);
            }
            if (ce.CodexLimitTicks > 0)
            {
                DateTime cdt = new DateTime(ce.CodexLimitTicks);
                if (cdt >= _codexLimitTime)
                {
                    _codexLimitTime = cdt;
                    if (ce.Codex5hAvail)
                    {
                        _codex5h = new LimitWindow
                        {
                            Available = true,
                            UsedPercent = ce.Codex5hPct,
                            ResetsAt = ce.Codex5hResetTicks > 0 ? new DateTime(ce.Codex5hResetTicks) : DateTime.MinValue
                        };
                    }
                    if (ce.Codex7dAvail)
                    {
                        _codex7d = new LimitWindow
                        {
                            Available = true,
                            UsedPercent = ce.Codex7dPct,
                            ResetsAt = ce.Codex7dResetTicks > 0 ? new DateTime(ce.Codex7dResetTicks) : DateTime.MinValue
                        };
                    }
                }
            }
            long minR = DateTime.Now.AddMinutes(-20).Ticks;
            long minAg = DateTime.Now.AddHours(-6).Ticks;
            if (ce.RecentTokens != null)
                for (int i = 0; i < ce.RecentTokens.Count; i++)
                    if (ce.RecentTokens[i].Length >= 2 && ce.RecentTokens[i][0] >= minR)
                        _recentTokens.Add(new KeyValuePair<DateTime, long>(new DateTime(ce.RecentTokens[i][0]), ce.RecentTokens[i][1]));
            if (ce.RecentAgTokens != null)
                for (int i = 0; i < ce.RecentAgTokens.Count; i++)
                    if (ce.RecentAgTokens[i].Length >= 2 && ce.RecentAgTokens[i][0] >= minAg)
                        _antigravityRecentTokens.Add(new KeyValuePair<DateTime, long>(new DateTime(ce.RecentAgTokens[i][0]), ce.RecentAgTokens[i][1]));
            if (ce.SeenKeys != null)
            {
                DateTime seenAt = DateTime.Now;
                for (int i = 0; i < ce.SeenKeys.Count; i++)
                {
                    string k = ce.SeenKeys[i];
                    if (!string.IsNullOrEmpty(k) && !_seen.ContainsKey(k))
                        _seen[k] = seenAt;
                }
            }
        }

        private void LoadCache()
        {
            _cacheLoaded = true;
            if (string.IsNullOrEmpty(CacheFilePath) || !File.Exists(CacheFilePath)) return;
            try
            {
                ScanCacheRoot root = J.Ser.Deserialize<ScanCacheRoot>(Paths.ReadAllTextShared(CacheFilePath));
                if (root == null || root.Version != CacheFormatVersion ||
                    !string.Equals(root.PricingStamp, _pricing.Stamp(), StringComparison.Ordinal) ||
                    root.TrackProjects != TrackProjects || root.ScanFlags != CurrentScanFlags() || root.Files == null)
                    return;
                _diskCache = new Dictionary<string, CachedFileEntry>(root.Files, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex) { Log.Write("scancache.load", ex); }
        }

        private void SaveCache()
        {
            _cacheDirty = false;
            if (string.IsNullOrEmpty(CacheFilePath)) return;
            try
            {
                long cutTicks = DateTime.UtcNow.AddDays(-(KeepDays + 1)).Ticks;
                long minAg = DateTime.Now.AddHours(-6).Ticks;
                List<string> stale = new List<string>();
                foreach (KeyValuePair<string, CachedFileEntry> kv in _diskCache)
                {
                    if (kv.Value == null || kv.Value.LastWriteUtcTicks < cutTicks) { stale.Add(kv.Key); continue; }
                    kv.Value.RecentTokens.RemoveAll(delegate(long[] x) { return x.Length < 2 || x[0] < minAg; });
                    kv.Value.RecentAgTokens.RemoveAll(delegate(long[] x) { return x.Length < 2 || x[0] < minAg; });
                }
                foreach (string k in stale) _diskCache.Remove(k);

                ScanCacheRoot root = new ScanCacheRoot();
                root.Version = CacheFormatVersion;
                root.PricingStamp = _pricing.Stamp();
                root.TrackProjects = TrackProjects;
                root.ScanFlags = CurrentScanFlags();
                root.Files = _diskCache;
                string dir = Path.GetDirectoryName(CacheFilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                AtomicFile.Write(CacheFilePath, J.Ser.Serialize(root));
            }
            catch (Exception ex) { Log.Write("scancache.save", ex); }
        }
    }
}
