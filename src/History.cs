using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace ClaudeUsageWidget
{
    /// <summary>Total satu hari (waktu lokal). Hanya agregat: tanggal, token, biaya, jumlah request.</summary>
    internal class DayStat
    {
        public DateTime Date;
        public long Tokens;
        public double Cost;
        public long Requests;
        public DayStat(DateTime date, long tokens, double cost, long requests) { Date = date.Date; Tokens = tokens; Cost = cost; Requests = requests; }
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
        private string _lastSaved = "";

        private static string Key(DateTime d) { return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }

        public static HistoryStore Load()
        {
            HistoryStore h = new HistoryStore();
            try
            {
                string f = Path.Combine(Paths.AppDir, "history.json");
                if (!File.Exists(f)) return h;
                string txt = File.ReadAllText(f, Encoding.UTF8);
                Dictionary<string, object> root = J.Parse(txt);
                System.Collections.IList arr = J.Arr(root, "days");
                if (arr != null)
                    foreach (object o in arr)
                    {
                        Dictionary<string, object> e = o as Dictionary<string, object>;
                        DateTime dt;
                        if (!DateTime.TryParseExact(J.Str(e, "d"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)) continue;
                        double cost = J.Num(e, "c");
                        h._d[Key(dt)] = new DayStat(dt, J.Long(e, "t"), double.IsNaN(cost) ? 0 : cost, J.Long(e, "r"));
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
                      .Append(",\"r\":").Append(s.Requests.ToString(CultureInfo.InvariantCulture)).Append('}');
                }
                sb.Append("]}");
                string txt = sb.ToString();
                if (txt == _lastSaved) return;
                Paths.Ensure();
                AtomicFile.Write(Path.Combine(Paths.AppDir, "history.json"), txt);
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
        private static DateTime _at = DateTime.MinValue;
        private static readonly object Gate = new object();

        public static List<string> Get()
        {
            lock (Gate)
            {
                if ((DateTime.Now - _at).TotalMinutes < 10) return new List<string>(_cache);
                _at = DateTime.Now;   // jangan ulangi terus saat gagal
            }
            List<string> found = new List<string>();
            try
            {
                Task t = Task.Run(delegate { Discover(found); });
                if (!t.Wait(8000)) Log.Write("wsl: discovery timeout");
            }
            catch (Exception ex) { Log.Write("wsl", ex); }
            lock (Gate) { _cache = new List<string>(found); return new List<string>(_cache); }
        }

        private static void Discover(List<string> found)
        {
            string wsl = Path.Combine(Environment.SystemDirectory, "wsl.exe");
            if (!File.Exists(wsl)) return;
            ProcessStartInfo psi = new ProcessStartInfo(wsl, "-l --running -q");
            psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true;
            psi.StandardOutputEncoding = Encoding.Unicode;   // wsl.exe menulis UTF-16
            string output;
            using (Process p = Process.Start(psi))
            {
                if (!p.WaitForExit(5000)) { try { p.Kill(); } catch { } return; }
                output = p.StandardOutput.ReadToEnd();
            }
            foreach (string raw in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string distro = raw.Replace("\0", "").Trim();
                if (distro.Length == 0 || distro.IndexOfAny(new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' }) >= 0) continue;
                string root = @"\\wsl.localhost\" + distro;
                try
                {
                    string rootProjects = Path.Combine(root, @"root\.claude\projects");
                    if (Directory.Exists(rootProjects)) found.Add(rootProjects);
                    string home = Path.Combine(root, "home");
                    if (Directory.Exists(home))
                        foreach (string user in Directory.GetDirectories(home))
                        {
                            string p2 = Path.Combine(user, @".claude\projects");
                            if (Directory.Exists(p2)) found.Add(p2);
                        }
                }
                catch (Exception ex) { Log.Write("wsl.distro", ex); }
            }
        }
    }
}
