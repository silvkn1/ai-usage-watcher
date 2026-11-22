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

    /// <summary>Tabel harga dari pricing.json (bisa diedit pengguna). Bawaan disalin dari resource exe.</summary>
    internal class Pricing
    {
        private readonly List<Price> _list = new List<Price>();
        public DateTime LoadedFileTime { get; private set; }

        public static Pricing Load()
        {
            Pricing p = new Pricing();
            try
            {
                Paths.Ensure();
                if (!File.Exists(Paths.PricingFile))
                {
                    using (Stream s = typeof(Pricing).Assembly.GetManifestResourceStream("pricing.json"))
                    using (FileStream f = File.Create(Paths.PricingFile))
                        if (s != null) s.CopyTo(f);
                }
                p.LoadedFileTime = File.GetLastWriteTimeUtc(Paths.PricingFile);
                Dictionary<string, object> root = J.Parse(File.ReadAllText(Paths.PricingFile, Encoding.UTF8));
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
                        p._list.Add(pr);
                    }
                p._list.Sort(delegate(Price a, Price b) { return b.Prefix.Length.CompareTo(a.Prefix.Length); });
            }
            catch (Exception ex) { Log.Write("pricing.load", ex); }
            return p;
        }

        private static double Z(double v) { return double.IsNaN(v) ? 0 : v; }

        public Price Find(string model)
        {
            if (string.IsNullOrEmpty(model)) return null;
            foreach (Price p in _list)
                if (model.StartsWith(p.Prefix, StringComparison.OrdinalIgnoreCase)) return p;
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

    internal class UsageSnapshot
    {
        public Dictionary<string, Tally> Today = new Dictionary<string, Tally>();
        public Dictionary<string, Tally> Week = new Dictionary<string, Tally>();
        public Dictionary<string, Tally> TodayProjects = new Dictionary<string, Tally>();
        public Dictionary<string, Tally> WeekProjects = new Dictionary<string, Tally>();
        public Tally TodayTotal = new Tally();
        public Tally WeekTotal = new Tally();
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
    }

    /// <summary>
    /// Membaca log Claude Code (~/.claude/projects/**/*.jsonl) secara inkremental per offset file.
    /// Dedup per message.id + requestId (lintas file), sama seperti ccusage. Hari = waktu lokal.
    /// Hanya field usage/model/timestamp yang dibaca; isi percakapan tidak disimpan.
    /// </summary>
    internal class UsageScanner
    {
        private const int KeepDays = 30;   // jendela pindai; "7 hari" dan "hari ini" dihitung dari subsetnya
        /// <summary>Folder 'projects' yang dipindai (default + folder tambahan + WSL). Diisi TrayApp.</summary>
        public Func<List<string>> Roots;
        private readonly Dictionary<string, long> _offsets = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _seen = new Dictionary<string, DateTime>();
        private readonly Dictionary<DateTime, Dictionary<string, Tally>> _days = new Dictionary<DateTime, Dictionary<string, Tally>>();
        private readonly Dictionary<DateTime, Dictionary<string, Tally>> _proj = new Dictionary<DateTime, Dictionary<string, Tally>>();
        /// <summary>Lacak total per proyek (nama folder terakhir dari 'cwd'). Default mati.</summary>
        public bool TrackProjects;

        private static string ProjectName(string cwd)
        {
            if (string.IsNullOrWhiteSpace(cwd)) return "(unknown)";
            string leaf = cwd.TrimEnd('\\', '/');
            int i = Math.Max(leaf.LastIndexOf('\\'), leaf.LastIndexOf('/'));
            if (i >= 0) leaf = leaf.Substring(i + 1);
            if (leaf.Length == 0) return "(unknown)";
            return leaf.Length > 40 ? leaf.Substring(0, 40) : leaf;
        }

        /// <summary>Nyalakan/matikan pelacakan proyek; perubahan memicu pindai ulang dari awal.</summary>
        public void SetTrackProjects(bool on)
        {
            lock (_gate)
            {
                if (TrackProjects == on) return;
                TrackProjects = on;
                _offsets.Clear(); _seen.Clear(); _days.Clear(); _proj.Clear(); _recentTokens.Clear();
                _lastContextTokens = 0; _lastContextModel = null; _lastContextTime = DateTime.MinValue;
                _lastActiveCwd = null; _lastActiveProject = null; _skipped = 0; _ready = false;
            }
        }
        private readonly object _gate = new object();
        private Pricing _pricing;
        private long _skipped;
        private DateTime _lastRecord = DateTime.MinValue;
        private bool _ready;
        private long _lastContextTokens;
        private string _lastContextModel;
        private DateTime _lastContextTime = DateTime.MinValue;
        private string _lastActiveCwd;
        private string _lastActiveProject;
        private readonly List<KeyValuePair<DateTime, long>> _recentTokens = new List<KeyValuePair<DateTime, long>>();

        public UsageScanner(Pricing pricing)
        {
            _pricing = pricing;
            Roots = delegate
            {
                List<string> r = new List<string> { Paths.ProjectsDir };
                if (Directory.Exists(Paths.AntigravityBrainDir)) r.Add(Paths.AntigravityBrainDir);
                return r;
            };
        }

        /// <summary>Ganti tabel harga: hitung ulang semua dengan membaca ulang dari awal.</summary>
        public void ResetWithPricing(Pricing pricing)
        {
            lock (_gate)
            {
                _pricing = pricing;
                _offsets.Clear(); _seen.Clear(); _days.Clear(); _proj.Clear(); _recentTokens.Clear();
                _lastContextTokens = 0; _lastContextModel = null; _lastContextTime = DateTime.MinValue;
                _lastActiveCwd = null; _lastActiveProject = null; _skipped = 0; _ready = false;
            }
        }

        public void Scan()
        {
            lock (_gate)
            {
                DateTime cutoffUtc = DateTime.UtcNow.AddDays(-KeepDays);
                List<string> roots;
                try { roots = Roots(); } catch (Exception ex) { Log.Write("scan.roots", ex); roots = new List<string> { Paths.ProjectsDir }; }
                foreach (string root in roots)
                {
                    try
                    {
                        if (!Directory.Exists(root)) continue;
                        foreach (string f in SafeEnumerateJsonl(root)) ScanOne(f, cutoffUtc);
                    }
                    catch (Exception ex) { Log.Write("scan.enumerate", ex); }
                }
                Prune();
                _ready = true;
            }
        }

        private static IEnumerable<string> SafeEnumerateJsonl(string root)
        {
            Stack<string> stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                string dir = stack.Pop();
                string[] files = null, subdirs = null;
                try { files = Directory.GetFiles(dir, "*.jsonl"); } catch { }
                if (files != null)
                {
                    for (int i = 0; i < files.Length; i++) yield return files[i];
                }
                try { subdirs = Directory.GetDirectories(dir); } catch { }
                if (subdirs != null)
                {
                    for (int i = 0; i < subdirs.Length; i++) stack.Push(subdirs[i]);
                }
            }
        }

        /// <summary>Scan hanya file yang dilaporkan berubah oleh FileSystemWatcher (murah saat Claude Code aktif).</summary>
        public void ScanFiles(ICollection<string> paths)
        {
            lock (_gate)
            {
                DateTime cutoffUtc = DateTime.UtcNow.AddDays(-KeepDays);
                foreach (string f in paths)
                    if (f.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) && File.Exists(f)) ScanOne(f, cutoffUtc);
            }
        }

        private void ScanOne(string f, DateTime cutoffUtc)
        {
            try
            {
                if (f.EndsWith("transcript_full.jsonl", StringComparison.OrdinalIgnoreCase)) return;
                FileInfo fi = new FileInfo(f);
                long off;
                bool known = _offsets.TryGetValue(f, out off);
                if (!known && fi.LastWriteTimeUtc < cutoffUtc) return;   // file lama: abaikan
                if (fi.Length < off) off = 0;                              // file dipotong/ditulis ulang
                if (fi.Length == off) { _offsets[f] = off; return; }
                _offsets[f] = ReadFrom(f, off);
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
            {
                fs.Seek(offset, SeekOrigin.Begin);
                byte[] buf = new byte[Chunk];
                MemoryStream carry = new MemoryStream();
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
                        ProcessLine(line);
                        start = i + 1;
                    }
                    if (start < n) carry.Write(buf, start, n - start);
                    if (carry.Capacity > 64 * Chunk && carry.Length == 0) carry = new MemoryStream();
                }
                return consumed;   // baris terakhir yang belum lengkap akan dibaca lagi nanti
            }
        }

        private void ProcessLine(string line)
        {
            if (line.IndexOf("\"PLANNER_RESPONSE\"", StringComparison.Ordinal) >= 0)
            {
                ProcessAntigravityLine(line);
                return;
            }

            // Saring murah sebelum parse JSON.
            if (line.IndexOf("\"usage\"", StringComparison.Ordinal) < 0) return;
            if (line.IndexOf("\"assistant\"", StringComparison.Ordinal) < 0) return;

            Dictionary<string, object> o;
            try { o = J.Parse(StripContent(line)); }
            catch { _skipped++; return; }
            if (o == null) { _skipped++; return; }
            if (J.Str(o, "type") != "assistant") return;

            Dictionary<string, object> msg = J.Obj(o, "message");
            Dictionary<string, object> u = J.Obj(msg, "usage");
            if (u == null) return;
            string model = J.Str(msg, "model");
            if (string.IsNullOrEmpty(model) || model == "<synthetic>") return;

            DateTime ts;
            string tsRaw = J.Str(o, "timestamp");
            if (tsRaw == null || !DateTime.TryParse(tsRaw, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out ts))
            { _skipped++; return; }
            DateTime local = DateTime.SpecifyKind(ts, DateTimeKind.Utc).ToLocalTime();
            if (local.Date < DateTime.Today.AddDays(-(KeepDays - 1))) return;

            string id = J.Str(msg, "id");
            string req = J.Str(o, "requestId");
            if (id != null || req != null)
            {
                string key = (id ?? "") + "|" + (req ?? "");
                if (_seen.ContainsKey(key)) return;
                _seen[key] = local;
            }

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
                // Penghematan cache BERSIH (estimasi): hemat dari membaca cache dikurangi biaya tambahan menulis cache
                // dibanding harga input biasa.
                t.Saved = (t.CacheRead * (p.Input - p.CacheRead)
                           - t.CacheWrite5m * (p.CacheWrite5m - p.Input)
                           - t.CacheWrite1h * (p.CacheWrite1h - p.Input)) / 1e6;
            }

            Dictionary<string, Tally> day;
            if (!_days.TryGetValue(local.Date, out day)) { day = new Dictionary<string, Tally>(); _days[local.Date] = day; }
            Tally acc;
            if (!day.TryGetValue(model, out acc)) { acc = new Tally(); day[model] = acc; }
            acc.Add(t);

            if (TrackProjects)   // opsional; nama proyek hanya di memori, tidak disimpan ke berkas mana pun
            {
                string proj = ProjectName(J.Str(o, "cwd"));
                Dictionary<string, Tally> pd;
                if (!_proj.TryGetValue(local.Date, out pd)) { pd = new Dictionary<string, Tally>(); _proj[local.Date] = pd; }
                Tally pa;
                if (!pd.TryGetValue(proj, out pa)) { pa = new Tally(); pd[proj] = pa; }
                pa.Add(t);
            }
            if (local > _lastRecord) _lastRecord = local;

            long ctxTokens = t.Input + t.CacheRead + cw;
            if (local >= _lastContextTime)
            {
                _lastContextTokens = ctxTokens;
                _lastContextModel = model;
                _lastContextTime = local;
                string cwd = J.Str(o, "cwd");
                if (!string.IsNullOrEmpty(cwd))
                {
                    _lastActiveCwd = cwd;
                    _lastActiveProject = ProjectName(cwd);
                }
            }
            _recentTokens.Add(new KeyValuePair<DateTime, long>(local, t.Total));
        }

        private void ProcessAntigravityLine(string line)
        {
            if (line.IndexOf("\"model\"", StringComparison.Ordinal) < 0) return;

            Dictionary<string, object> o;
            try { o = J.Parse(StripAntigravity(line)); }
            catch { _skipped++; return; }
            if (o == null) { _skipped++; return; }
            if (J.Str(o, "type") != "PLANNER_RESPONSE") return;

            string model = J.Str(o, "model");
            if (string.IsNullOrEmpty(model) || model == "<synthetic>") return;

            DateTime ts;
            string tsRaw = J.Str(o, "created_at");
            if (tsRaw == null || !DateTime.TryParse(tsRaw, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out ts))
            { _skipped++; return; }
            DateTime local = DateTime.SpecifyKind(ts, DateTimeKind.Utc).ToLocalTime();
            if (local.Date < DateTime.Today.AddDays(-(KeepDays - 1))) return;

            long stepIndex = J.Long(o, "step_index");
            string key = "ag|" + (tsRaw ?? "") + "|" + stepIndex + "|" + model;
            if (_seen.ContainsKey(key)) return;
            _seen[key] = local;

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

            Dictionary<string, Tally> day;
            if (!_days.TryGetValue(local.Date, out day)) { day = new Dictionary<string, Tally>(); _days[local.Date] = day; }
            Tally acc;
            if (!day.TryGetValue(model, out acc)) { acc = new Tally(); day[model] = acc; }
            acc.Add(t);

            if (TrackProjects)
            {
                string proj = "Antigravity";
                Dictionary<string, Tally> pd;
                if (!_proj.TryGetValue(local.Date, out pd)) { pd = new Dictionary<string, Tally>(); _proj[local.Date] = pd; }
                Tally pa;
                if (!pd.TryGetValue(proj, out pa)) { pa = new Tally(); pd[proj] = pa; }
                pa.Add(t);
            }
            if (local > _lastRecord) _lastRecord = local;

            long ctxTokens = t.Input + t.CacheRead;
            if (local >= _lastContextTime)
            {
                _lastContextTokens = ctxTokens;
                _lastContextModel = model;
                _lastContextTime = local;
            }
            _recentTokens.Add(new KeyValuePair<DateTime, long>(local, t.Total));
        }

        private void Prune()
        {
            DateTime min = DateTime.Today.AddDays(-(KeepDays - 1));
            List<DateTime> oldDays = new List<DateTime>();
            foreach (DateTime d in _days.Keys) if (d < min) oldDays.Add(d);
            foreach (DateTime d in oldDays) _days.Remove(d);
            List<DateTime> oldProj = new List<DateTime>();
            foreach (DateTime d in _proj.Keys) if (d < min) oldProj.Add(d);
            foreach (DateTime d in oldProj) _proj.Remove(d);
            if (_seen.Count > 50000)
            {
                List<string> old = new List<string>();
                foreach (KeyValuePair<string, DateTime> kv in _seen) if (kv.Value.Date < min) old.Add(kv.Key);
                foreach (string k in old) _seen.Remove(k);
            }
            DateTime minRecent = DateTime.Now.AddMinutes(-20);
            _recentTokens.RemoveAll(delegate(KeyValuePair<DateTime, long> x) { return x.Key < minRecent; });
        }

        public UsageSnapshot Snapshot()
        {
            lock (_gate)
            {
                UsageSnapshot s = new UsageSnapshot();
                DateTime today = DateTime.Today, weekStart = today.AddDays(-6);
                foreach (KeyValuePair<DateTime, Dictionary<string, Tally>> day in _days)
                {
                    Tally dayTotal = new Tally();
                    foreach (Tally t in day.Value.Values) dayTotal.Add(t);
                    s.Days.Add(new DayStat(day.Key, dayTotal.Total, dayTotal.Cost, dayTotal.Requests));
                    if (day.Key < weekStart) continue;
                    foreach (KeyValuePair<string, Tally> m in day.Value)
                    {
                        AddTo(s.Week, m.Key, m.Value); s.WeekTotal.Add(m.Value);
                        if (day.Key == today) { AddTo(s.Today, m.Key, m.Value); s.TodayTotal.Add(m.Value); }
                    }
                }
                if (TrackProjects)
                    foreach (KeyValuePair<DateTime, Dictionary<string, Tally>> pd in _proj)
                    {
                        if (pd.Key < weekStart) continue;
                        foreach (KeyValuePair<string, Tally> m in pd.Value)
                        {
                            AddTo(s.WeekProjects, m.Key, m.Value);
                            if (pd.Key == today) AddTo(s.TodayProjects, m.Key, m.Value);
                        }
                    }
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
                return s;
            }
        }

        private static void AddTo(Dictionary<string, Tally> d, string model, Tally t)
        {
            Tally acc;
            if (!d.TryGetValue(model, out acc)) { acc = new Tally(); d[model] = acc; }
            acc.Add(t);
        }
    }
}
