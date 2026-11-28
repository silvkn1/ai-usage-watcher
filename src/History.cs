using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace ClaudeUsageWidget
{
    /// <summary>Total satu hari (waktu lokal). Hanya agregat: tanggal, token, biaya, jumlah request, dan rincian biaya per provider.</summary>
    internal class DayStat
    {
        public DateTime Date;
        public long Tokens;
        public double Cost;
        public long Requests;
        public Dictionary<string, double> ProviderCosts;

        public DayStat(DateTime date, long tokens, double cost, long requests)
            : this(date, tokens, cost, requests, null) { }

        public DayStat(DateTime date, long tokens, double cost, long requests, Dictionary<string, double> providerCosts)
        {
            Date = date.Date; Tokens = tokens; Cost = cost; Requests = requests;
            ProviderCosts = providerCosts;
        }

        public double CostFor(string provider)
        {
            if (ProviderCosts == null || string.IsNullOrEmpty(provider)) return 0;
            double v;
            return ProviderCosts.TryGetValue(provider, out v) ? v : 0;
        }
    }

    /// <summary>
    /// Riwayat harian yang bertahan walau log Claude Code dihapus (history.json, hanya agregat).
    /// Hari di dalam jendela pindai ditimpa (bukan dijumlahkan) dengan hasil pindai, kecuali hasil pindai punya
    /// lebih sedikit request daripada yang tersimpan (log sebagian terhapus). Biaya hari yang tersimpan
    /// "membeku" pada harga saat disimpan.
    /// </summary>
    internal class HistoryStore
    {
        private const int KeepDays = 120;
        private readonly Dictionary<string, DayStat> _d = new Dictionary<string, DayStat>();
        private readonly object _gate = new object();
        private readonly string _filePath;
        private string _lastSaved = "";

        public HistoryStore() : this(Path.Combine(Paths.AppDir, "history.json")) { }
        internal HistoryStore(string filePath) { _filePath = filePath; }

        private static string Key(DateTime d) { return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }

        public static HistoryStore Load() { return LoadFrom(Path.Combine(Paths.AppDir, "history.json")); }

        public static HistoryStore LoadFrom(string path)
        {
            HistoryStore h = new HistoryStore(path);
            try
            {
                if (!File.Exists(path)) return h;
                string txt = Paths.ReadAllTextShared(path);
                Dictionary<string, object> root = J.Parse(txt);
                System.Collections.IList arr = J.Arr(root, "days");
                if (arr != null)
                    foreach (object o in arr)
                    {
                        Dictionary<string, object> e = o as Dictionary<string, object>;
                        DateTime dt;
                        if (!DateTime.TryParseExact(J.Str(e, "d"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)) continue;
                        double cost = J.Num(e, "c");
                        Dictionary<string, object> pRaw = J.Obj(e, "p");
                        Dictionary<string, double> pCosts = null;
                        if (pRaw != null && pRaw.Count > 0)
                        {
                            pCosts = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                            foreach (KeyValuePair<string, object> kv in pRaw)
                            {
                                double pv = J.ToNum(kv.Value);
                                if (!double.IsNaN(pv) && pv > 0) pCosts[kv.Key] = pv;
                            }
                        }
                        h._d[Key(dt)] = new DayStat(dt, J.Long(e, "t"), double.IsNaN(cost) ? 0 : cost, J.Long(e, "r"), pCosts);
                    }
                h._lastSaved = txt;
            }
            catch (Exception ex) { Log.Write("history.load", ex); }
            return h;
        }

        /// <summary>Gabungkan hasil pindai dan simpan bila berubah. Aman dipanggil dari thread mana pun.</summary>
        public void Merge(IEnumerable<DayStat> scanned)
        {
            lock (_gate)
            {
                foreach (DayStat s in scanned)
                {
                    if (s.Requests <= 0 && s.Tokens <= 0) continue;
                    DayStat old;
                    if (_d.TryGetValue(Key(s.Date), out old) && s.Requests < old.Requests) continue;   // log sebagian terhapus: pertahankan
                    _d[Key(s.Date)] = s;
                }
                DateTime min = DateTime.Today.AddDays(-KeepDays);
                List<string> drop = new List<string>();
                foreach (KeyValuePair<string, DayStat> kv in _d) if (kv.Value.Date < min) drop.Add(kv.Key);
                foreach (string k in drop) _d.Remove(k);
                Save();
            }
        }

        private void Save()
        {
            try
            {
                List<string> keys = new List<string>(_d.Keys);
                keys.Sort(StringComparer.Ordinal);
                StringBuilder sb = new StringBuilder("{\"days\":[");
                for (int i = 0; i < keys.Count; i++)
                {
                    DayStat s = _d[keys[i]];
                    if (i > 0) sb.Append(',');
                    sb.Append("{\"d\":\"").Append(keys[i]).Append("\",\"t\":").Append(s.Tokens.ToString(CultureInfo.InvariantCulture))
                      .Append(",\"c\":").Append(s.Cost.ToString("0.####", CultureInfo.InvariantCulture))
                      .Append(",\"r\":").Append(s.Requests.ToString(CultureInfo.InvariantCulture));
                    if (s.ProviderCosts != null && s.ProviderCosts.Count > 0)
                    {
                        sb.Append(",\"p\":{");
                        bool firstP = true;
                        List<string> pKeys = new List<string>(s.ProviderCosts.Keys);
                        pKeys.Sort(StringComparer.Ordinal);
                        for (int pi = 0; pi < pKeys.Count; pi++)
                        {
                            double pv = s.ProviderCosts[pKeys[pi]];
                            if (pv <= 0) continue;
                            if (!firstP) sb.Append(',');
                            firstP = false;
                            sb.Append(J.Ser.Serialize(pKeys[pi])).Append(':').Append(pv.ToString("0.####", CultureInfo.InvariantCulture));
                        }
                        sb.Append('}');
                    }
                    sb.Append('}');
                }
                sb.Append("]}");
                string txt = sb.ToString();
                if (txt == _lastSaved) return;
                string dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                AtomicFile.Write(_filePath, txt);
                _lastSaved = txt;
            }
            catch (Exception ex) { Log.Write("history.save", ex); }
        }

        public int Count { get { lock (_gate) return _d.Count; } }

        /// <summary>Total estimasi biaya bulan berjalan (termasuk hari ini), dari riwayat tersimpan.</summary>
        public double MonthToDateCost(DateTime asOf)
        {
            lock (_gate)
            {
                double sum = 0;
                foreach (DayStat s in _d.Values) if (s.Date.Year == asOf.Year && s.Date.Month == asOf.Month && s.Date <= asOf.Date) sum += s.Cost;
                return sum;
            }
        }

        /// <summary>Daftar statistik harian untuk bulan tertentu (terurut kronologis).</summary>
        public List<DayStat> GetMonth(DateTime month)
        {
            lock (_gate)
            {
                List<DayStat> r = new List<DayStat>();
                foreach (DayStat s in _d.Values)
                {
                    if (s.Date.Year == month.Year && s.Date.Month == month.Month)
                        r.Add(s);
                }
                r.Sort(delegate(DayStat a, DayStat b) { return a.Date.CompareTo(b.Date); });
                return r;
            }
        }

        /// <summary>
        /// CSV riwayat harian: tanggal, token, estimasi biaya (USD, harga API), jumlah request. Hanya agregat.
        /// sep ',' memakai titik desimal; sep ';' memakai koma desimal (untuk Excel dengan pengaturan regional yang memakai ';').
        /// </summary>
        public string ToCsv(char sep)
        {
            lock (_gate)
            {
                List<string> keys = new List<string>(_d.Keys);
                keys.Sort(StringComparer.Ordinal);
                CultureInfo ci = sep == ';' ? CultureInfo.GetCultureInfo("id-ID") : CultureInfo.InvariantCulture;
                StringBuilder sb = new StringBuilder();
                sb.Append("date").Append(sep).Append("tokens").Append(sep).Append("estimated_cost_usd").Append(sep).Append("requests").Append("\r\n");
                foreach (string k in keys)
                {
                    DayStat s = _d[k];
                    sb.Append(k).Append(sep).Append(s.Tokens.ToString(CultureInfo.InvariantCulture)).Append(sep)
                      .Append(s.Cost.ToString("0.0000", ci)).Append(sep).Append(s.Requests.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
                }
                return sb.ToString();
            }
        }

        public string ToJson()
        {
            lock (_gate)
            {
                List<string> keys = new List<string>(_d.Keys);
                keys.Sort(StringComparer.Ordinal);
                List<object> rows = new List<object>();
                foreach (string k in keys)
                {
                    DayStat s = _d[k];
                    Dictionary<string, object> row = new Dictionary<string, object>();
                    row["date"] = k;
                    row["tokens"] = s.Tokens;
                    row["estimated_cost_usd"] = Math.Round(s.Cost, 4);
                    row["requests"] = s.Requests;
                    if (s.ProviderCosts != null && s.ProviderCosts.Count > 0)
                    {
                        Dictionary<string, object> pMap = new Dictionary<string, object>();
                        foreach (KeyValuePair<string, double> kv in s.ProviderCosts)
                            pMap[kv.Key] = Math.Round(kv.Value, 4);
                        row["providers"] = pMap;
                    }
                    rows.Add(row);
                }
                Dictionary<string, object> root = new Dictionary<string, object>();
                root["exported_at"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
                root["days"] = rows;
                return J.Ser.Serialize(root);
            }
        }

        /// <summary>n hari terakhir berakhir hari ini (terlama dulu); hari kosong bernilai nol.</summary>
        public DayStat[] Series(int n)
        {
            lock (_gate)
            {
                DayStat[] r = new DayStat[n];
                for (int i = 0; i < n; i++)
                {
                    DateTime d = DateTime.Today.AddDays(-(n - 1 - i));
                    DayStat s;
                    r[i] = _d.TryGetValue(Key(d), out s) ? s : new DayStat(d, 0, 0, 0);
                }
                return r;
            }
        }
    }

    /// <summary>
    /// Folder 'projects' milik distro WSL yang SEDANG BERJALAN (tidak membangunkan distro yang mati).
    /// Dijalankan di luar thread UI dengan batas waktu dan di-cache 10 menit.
    /// </summary>
    internal static class WslRoots
    {
        private static List<string> _cache = new List<string>();
        private static List<string> _codexCache = new List<string>();
        private static DateTime _at = DateTime.MinValue;
        private static readonly object Gate = new object();

        public static List<string> Get()
        {
            EnsureRefreshed();
            lock (Gate) return new List<string>(_cache);
        }

        public static List<string> GetCodex()
        {
            EnsureRefreshed();
            lock (Gate) return new List<string>(_codexCache);
        }

        private static Task _inFlight;

        private static void EnsureRefreshed()
        {
            Task waitTask;
            lock (Gate)
            {
                if (_inFlight != null && !_inFlight.IsCompleted)
                {
                    waitTask = _inFlight;
                }
                else
                {
                    if ((DateTime.UtcNow - _at).TotalMinutes < 10) return;
                    _at = DateTime.UtcNow;   // jangan ulangi terus saat gagal
                    _inFlight = Task.Run(delegate { DiscoverAndPublish(); });
                    waitTask = _inFlight;
                }
            }
            try
            {
                if (!waitTask.Wait(8000)) Log.Write("wsl: discovery timeout");
            }
            catch (Exception ex) { Log.Write("wsl", ex); }
        }

        private static void DiscoverAndPublish()
        {
            List<string> foundClaude = new List<string>();
            List<string> foundCodex = new List<string>();
            Discover(foundClaude, foundCodex);
            // Only publish complete lists under Gate so a timed-out Wait never races with Discover mutating them.
            lock (Gate)
            {
                _cache = foundClaude;
                _codexCache = foundCodex;
            }
        }

        private static void Discover(List<string> foundClaude, List<string> foundCodex)
        {
            string wsl = Path.Combine(Environment.SystemDirectory, "wsl.exe");
            if (!File.Exists(wsl)) return;
            ProcessStartInfo psi = new ProcessStartInfo(wsl, "-l --running -q");
            psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true;
            psi.StandardOutputEncoding = Encoding.Unicode;   // wsl.exe menulis UTF-16
            string output = "";
            using (Process p = Process.Start(psi))
            {
                Task<string> readTask = Task.Run(delegate { try { return p.StandardOutput.ReadToEnd(); } catch { return ""; } });
                if (!p.WaitForExit(5000)) { try { p.Kill(); } catch { } readTask.Wait(200); return; }
                output = readTask.Wait(1000) ? readTask.Result : "";
            }
            foreach (string raw in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string distro = raw.Replace("\0", "").Trim();
                if (distro.Length == 0 || distro.IndexOfAny(new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' }) >= 0) continue;
                string root = @"\\wsl.localhost\" + distro;
                try
                {
                    string rootProjects = Path.Combine(root, @"root\.claude\projects");
                    if (Directory.Exists(rootProjects)) foundClaude.Add(rootProjects);
                    string rootCodex = Path.Combine(root, @"root\.codex\sessions");
                    if (Directory.Exists(rootCodex)) foundCodex.Add(rootCodex);
                    string home = Path.Combine(root, "home");
                    if (Directory.Exists(home))
                        foreach (string user in Directory.GetDirectories(home))
                        {
                            string p2 = Path.Combine(user, @".claude\projects");
                            if (Directory.Exists(p2)) foundClaude.Add(p2);
                            string c2 = Path.Combine(user, @".codex\sessions");
                            if (Directory.Exists(c2)) foundCodex.Add(c2);
                        }
                }
                catch (Exception ex) { Log.Write("wsl.distro", ex); }
            }
        }
    }
}
