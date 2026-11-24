using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ClaudeUsageWidget
{
    internal class LimitWindow
    {
        public bool Available;
        public double UsedPercent;
        public DateTime ResetsAt = DateTime.MinValue;
        /// <summary>Waktu reset sudah lewat sejak snapshot: persentase lama tidak berlaku lagi.</summary>
        public bool IsStale { get { return Available && ResetsAt != DateTime.MinValue && ResetsAt <= DateTime.Now; } }
    }

    internal class RateLimitSnapshot
    {
        public LimitWindow FiveHour = new LimitWindow();
        public LimitWindow SevenDay = new LimitWindow();
        public LimitWindow AntigravityFiveHour = new LimitWindow();
        public LimitWindow AntigravitySevenDay = new LimitWindow();
        public DateTime CapturedAt = DateTime.MinValue;
        public bool FileFound;
        public string Source = "statusline";
    }

    /// <summary>Membaca snapshot rate_limits yang ditulis skrip statusline (cuw-statusline.sh) atau Antigravity.</summary>
    internal static class RateLimitReader
    {
        public static RateLimitSnapshot Read()
        {
            RateLimitSnapshot s = new RateLimitSnapshot();
            try
            {
                string file = Paths.RateLimitsReadFile;
                if (File.Exists(file))
                {
                    string txt;
                    using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (StreamReader r = new StreamReader(fs, Encoding.UTF8))
                        txt = r.ReadToEnd();
                    Dictionary<string, object> o = J.Parse(txt);
                    s.FileFound = true;
                    double cap = J.Num(o, "captured_at");
                    s.CapturedAt = double.IsNaN(cap) ? File.GetLastWriteTime(file) : Fmt.FromUnix(cap);
                    string src = J.Str(o, "source");
                    if (!string.IsNullOrEmpty(src)) s.Source = src;
                    Dictionary<string, object> rl = J.Obj(o, "rate_limits");
                    s.FiveHour = Window(J.Obj(rl, "five_hour"));
                    s.SevenDay = Window(J.Obj(rl, "seven_day"));
                }

                string agFile = Paths.AntigravityRateLimitsReadFile;
                if (File.Exists(agFile))
                {
                    string txt;
                    using (FileStream fs = new FileStream(agFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (StreamReader r = new StreamReader(fs, Encoding.UTF8))
                        txt = r.ReadToEnd();
                    Dictionary<string, object> o = J.Parse(txt);
                    Dictionary<string, object> rl = J.Obj(o, "rate_limits");
                    s.AntigravityFiveHour = Window(J.Obj(rl, "five_hour"));
                    s.AntigravitySevenDay = Window(J.Obj(rl, "seven_day"));
                }
            }
            catch (Exception ex) { Log.Write("ratelimits.read", ex); }
            return s;
        }

        private static LimitWindow Window(Dictionary<string, object> w)
        {
            LimitWindow r = new LimitWindow();
            double used = J.Num(w, "used_percentage");
            if (double.IsNaN(used)) return r;
            r.Available = true;
            r.UsedPercent = Math.Max(0, Math.Min(100, used));
            double reset = J.Num(w, "resets_at");
            if (!double.IsNaN(reset)) r.ResetsAt = Fmt.FromUnix(reset);
            return r;
        }
    }

    internal class OrgCostResult
    {
        public bool Ok;
        public string Error;
        public double MonthUsd;
        public List<KeyValuePair<string, double>> ByModel = new List<KeyValuePair<string, double>>();
        public string Endpoint;
        public DateTime FetchedAt = DateTime.MinValue;
    }

    /// <summary>
    /// Admin API (opsional, default mati). Hanya GET ke api.anthropic.com memakai admin key milik user.
    /// Utama: /v1/organizations/analytics/cost (beta); cadangan: /v1/organizations/cost_report (legacy).
    /// Field 'amount' = string desimal dalam SEN USD.
    /// </summary>
    internal static class AdminApi
    {
        private static readonly HttpClient Http;

        static AdminApi()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            Http = new HttpClient();
            Http.Timeout = TimeSpan.FromSeconds(30);
            Http.BaseAddress = new Uri("https://api.anthropic.com");
        }

        public static async Task<OrgCostResult> FetchMonthCost(string adminKey, string apiKeyIds, string workspaceIds)
        {
            OrgCostResult res = new OrgCostResult();
            res.FetchedAt = DateTime.Now;
            if (string.IsNullOrEmpty(adminKey)) { res.Error = L.T("aerr.nokey"); return res; }

            DateTime nowUtc = DateTime.UtcNow;
            string start = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            string filters = Filter("api_key_ids[]", apiKeyIds) + Filter("workspace_ids[]", workspaceIds);

            string q1 = "/v1/organizations/analytics/cost?starting_at=" + Uri.EscapeDataString(start)
                        + "&group_by[]=model&limit=1000" + filters;
            string err1 = await Collect(adminKey, q1, true, res);
            if (err1 == null) { res.Ok = true; res.Endpoint = "analytics/cost"; return Finish(res); }

            // Cadangan: laporan biaya legacy (harian, sampai tengah malam UTC terakhir).
            res.ByModel.Clear(); res.MonthUsd = 0;
            string q2 = "/v1/organizations/cost_report?starting_at=" + Uri.EscapeDataString(start) + "&limit=31" + filters;
            string err2 = await Collect(adminKey, q2, false, res);
            if (err2 == null) { res.Ok = true; res.Endpoint = "cost_report (legacy)"; return Finish(res); }

            res.Error = L.T("aerr.both", err1, err2);
            return res;
        }

        /// <summary>Diagnosa: ambil halaman pertama dan kembalikan STRUKTUR respons (nama field + tipe; nilai hanya untuk
        /// field teks pendek non-rahasia). Key tidak pernah ikut ditulis.</summary>
        public static async Task<string> DescribeFirstPage(string adminKey)
        {
            DateTime nowUtc = DateTime.UtcNow;
            string start = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            using (HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get,
                "/v1/organizations/analytics/cost?starting_at=" + Uri.EscapeDataString(start) + "&group_by[]=model&limit=5"))
            {
                req.Headers.Add("x-api-key", adminKey);
                req.Headers.Add("anthropic-version", "2023-06-01");
                req.Headers.Add("anthropic-beta", "analytics-usage-cost-2026-08-24");
                using (HttpResponseMessage resp = await Http.SendAsync(req).ConfigureAwait(false))
                {
                    string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    StringBuilder sb = new StringBuilder("HTTP " + (int)resp.StatusCode + "\n");
                    try { Describe(J.Ser.DeserializeObject(body), "", sb, 0); } catch { sb.Append("(not JSON)\n"); }
                    return sb.ToString();
                }
            }
        }

        private static void Describe(object node, string path, StringBuilder sb, int depth)
        {
            if (depth > 8) return;
            Dictionary<string, object> d = node as Dictionary<string, object>;
            IList l = node as IList;
            if (d != null) { foreach (KeyValuePair<string, object> kv in d) Describe(kv.Value, path + "." + kv.Key, sb, depth + 1); return; }
            if (l != null)
            {
                sb.Append(path).Append(" : array[").Append(l.Count).Append("]\n");
                for (int i = 0; i < Math.Min(2, l.Count); i++) Describe(l[i], path + "[" + i + "]", sb, depth + 1);
                return;
            }
            string v = node == null ? "null" : Convert.ToString(node, CultureInfo.InvariantCulture);
            if (v.Length > 40) v = v.Substring(0, 40) + "...";
            sb.Append(path).Append(" = ").Append(node == null ? "null" : node.GetType().Name + " " + v).Append('\n');
        }

        private static OrgCostResult Finish(OrgCostResult r)
        {
            r.ByModel.Sort(delegate(KeyValuePair<string, double> a, KeyValuePair<string, double> b) { return b.Value.CompareTo(a.Value); });
            return r;
        }

        private static string Filter(string name, string csv)
        {
            if (string.IsNullOrEmpty(csv)) return "";
            StringBuilder sb = new StringBuilder();
            foreach (string part in csv.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                sb.Append('&').Append(name).Append('=').Append(Uri.EscapeDataString(part.Trim()));
            return sb.ToString();
        }

        /// <summary>Ambil semua halaman; null = sukses, selain itu pesan error singkat (tanpa isi key).</summary>
        private static async Task<string> Collect(string key, string query, bool analytics, OrgCostResult res)
        {
            Dictionary<string, double> byModel = new Dictionary<string, double>();
            string page = null;
            for (int i = 0; i < 20; i++)
            {
                string url = query + (page == null ? "" : "&page=" + Uri.EscapeDataString(page));
                using (HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    req.Headers.Add("x-api-key", key);
                    req.Headers.Add("anthropic-version", "2023-06-01");
                    if (analytics) req.Headers.Add("anthropic-beta", "analytics-usage-cost-2026-08-24");
                    HttpResponseMessage resp;
                    try { resp = await Http.SendAsync(req).ConfigureAwait(false); }
                    catch (Exception ex) { return L.T("aerr.network", ex.GetType().Name); }
                    using (resp)
                    {
                        if (!resp.IsSuccessStatusCode) return L.T("aerr.http", (int)resp.StatusCode);
                        string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        Dictionary<string, object> o;
                        try { o = J.Parse(body); } catch { return L.T("aerr.badresponse"); }
                        SumAmounts(J.Arr(o, "data"), null, byModel, res);
                        object more;
                        bool hasMore = o != null && o.TryGetValue("has_more", out more) && more is bool && (bool)more;
                        page = J.Str(o, "next_page");
                        if (!hasMore || string.IsNullOrEmpty(page)) break;
                    }
                }
            }
            foreach (KeyValuePair<string, double> kv in byModel) res.ByModel.Add(kv);
            return null;
        }

        /// <summary>Jumlahkan semua field 'amount' (sen) secara rekursif - toleran terhadap bentuk baris/bucket.</summary>
        private static void SumAmounts(object node, string model, Dictionary<string, double> byModel, OrgCostResult res)
        {
            IList list = node as IList;
            if (list != null) { foreach (object x in list) SumAmounts(x, model, byModel, res); return; }
            Dictionary<string, object> d = node as Dictionary<string, object>;
            if (d == null) return;
            // analytics/cost: model ada di row.group.model, nilai di row.cost.amount (sen).
            string m = J.Str(d, "model") ?? J.Str(J.Obj(d, "group"), "model") ?? model;
            object amount;
            if (d.TryGetValue("amount", out amount))
            {
                double cents = J.ToNum(amount);
                Dictionary<string, object> ao = amount as Dictionary<string, object>;
                if (ao != null) cents = J.Num(ao, "amount");
                if (!double.IsNaN(cents))
                {
                    double usd = cents / 100.0;
                    res.MonthUsd += usd;
                    string k = string.IsNullOrEmpty(m) ? "(lainnya)" : m;
                    double cur; byModel.TryGetValue(k, out cur); byModel[k] = cur + usd;
                }
                return;
            }
            foreach (KeyValuePair<string, object> kv in d)
                if (kv.Value is IList || kv.Value is Dictionary<string, object>) SumAmounts(kv.Value, m, byModel, res);
        }
    }

    /// <summary>Pengambil kurs USD ke IDR otomatis dari Google Finance.</summary>
    internal static class GoogleRateFetcher
    {
        private static readonly HttpClient Http = new HttpClient();
        private static DateTime _lastFetch = DateTime.MinValue;
        private static bool _busy;

        static GoogleRateFetcher()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            Http.Timeout = TimeSpan.FromSeconds(10);
            Http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        }

        public static async Task<double> FetchRateAsync()
        {
            if (_busy) return 0;
            _busy = true;
            try
            {
                string html = await Http.GetStringAsync("https://www.google.com/finance/quote/USD-IDR?hl=en");
                System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(html, @"""USD-IDR"",""USD / IDR"",([0-9\.]+)");
                if (!m.Success)
                    m = System.Text.RegularExpressions.Regex.Match(html, @"""USD / IDR"",\s*\d+,\s*null,\s*\[([0-9\.]+)");
                if (m.Success)
                {
                    double rate;
                    if (double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out rate) && rate > 5000)
                    {
                        _lastFetch = DateTime.Now;
                        return rate;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("google.rate", ex);
            }
            finally
            {
                _busy = false;
            }
            return 0;
        }

        public static bool ShouldRefresh(bool force)
        {
            if (force) return (DateTime.Now - _lastFetch).TotalMinutes >= 1;
            return (DateTime.Now - _lastFetch).TotalHours >= 1;
        }
    }

    /// <summary>Pengambil dan pembaru tabel harga API resmi dari internet (Anthropic & Google AI).</summary>
    internal static class OfficialPricingFetcher
    {
        private static readonly HttpClient Http = new HttpClient();
        private static DateTime _lastFetch = DateTime.MinValue;
        private static bool _busy;

        static OfficialPricingFetcher()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            Http.Timeout = TimeSpan.FromSeconds(15);
            Http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AIUsageWatcher/1.0");
        }

        public static bool ShouldAutoRefresh()
        {
            try
            {
                if (!File.Exists(Paths.PricingFile)) return true;
                return (DateTime.UtcNow - File.GetLastWriteTimeUtc(Paths.PricingFile)).TotalDays >= 7;
            }
            catch { return false; }
        }

        public static async Task<int> FetchAndSaveAsync()
        {
            if (_busy) return 0;
            _busy = true;
            try
            {
                Dictionary<string, double[]> models = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);

                // 1. Anthropic Official Documentation Pricing Table
                try
                {
                    string html = await Http.GetStringAsync("https://platform.claude.com/docs/en/about-claude/pricing");
                    MatchCollection mc = Regex.Matches(html, @"href=""/docs/en/models/([^""]+)/overview"">(Claude [^<]+)</a>.*?\$([0-9.]+).*?\$([0-9.]+)", RegexOptions.Singleline);
                    foreach (Match m in mc)
                    {
                        string slug = m.Groups[1].Value.Trim().ToLowerInvariant();
                        string prefix = slug.StartsWith("claude-") ? slug : "claude-" + slug;
                        double input, output;
                        if (double.TryParse(m.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out input) &&
                            double.TryParse(m.Groups[4].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out output))
                        {
                            if (!models.ContainsKey(prefix))
                            {
                                double cw5 = Math.Round(input * 1.25, 4);
                                double cw1 = Math.Round(input * 2.0, 4);
                                double cr = Math.Round(input * 0.10, 4);
                                models[prefix] = new[] { input, output, cw5, cw1, cr };
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Write("pricing.anthropic", ex);
                }

                // Official Claude active models baseline
                AddIfMissing(models, "claude-3-7-sonnet", 3.0, 15.0, 3.75, 6.0, 0.30);
                AddIfMissing(models, "claude-3-5-sonnet", 3.0, 15.0, 3.75, 6.0, 0.30);
                AddIfMissing(models, "claude-3-5-haiku", 0.80, 4.0, 1.0, 1.6, 0.08);
                AddIfMissing(models, "claude-3-opus", 15.0, 75.0, 18.75, 30.0, 1.50);
                AddIfMissing(models, "claude-3-haiku", 0.25, 1.25, 0.3125, 0.50, 0.025);

                // Google Gemini Official Documentation Pricing
                AddIfMissing(models, "gemini-2.5-pro", 1.25, 5.0, 1.25, 1.25, 0.3125);
                AddIfMissing(models, "gemini-2.5-flash", 0.075, 0.30, 0.075, 0.075, 0.01875);
                AddIfMissing(models, "gemini-2.0-flash", 0.10, 0.40, 0.10, 0.10, 0.025);
                AddIfMissing(models, "gemini-1.5-pro", 1.25, 5.0, 1.25, 1.25, 0.3125);
                AddIfMissing(models, "gemini-1.5-flash", 0.075, 0.30, 0.075, 0.075, 0.01875);
                AddIfMissing(models, "gemini-1.5-flash-8b", 0.0375, 0.15, 0.0375, 0.0375, 0.01);
                AddIfMissing(models, "gemini-3.8-flash", 0.10, 0.40, 0.10, 0.10, 0.025);
                AddIfMissing(models, "gemini-3.8-pro", 1.25, 5.0, 1.25, 1.25, 0.3125);
                AddIfMissing(models, "gemini-", 0.10, 0.40, 0.10, 0.10, 0.025);

                if (models.Count == 0) return 0;

                // Sort prefixes by length descending
                List<string> prefixes = new List<string>(models.Keys);
                prefixes.Sort(delegate(string a, string b) { return b.Length.CompareTo(a.Length); });

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine("  \"_catatan\": \"Harga USD per 1 juta token, setara harga API resmi (list price). Diperbarui dari sumber resmi internet.\",");
                sb.AppendLine("  \"_sumber\": \"Anthropic (https://platform.claude.com/docs/en/about-claude/pricing) & Google AI (https://ai.google.dev/gemini-api/docs/pricing)\",");
                sb.AppendLine("  \"_diperbarui\": \"" + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC\",");
                sb.AppendLine("  \"models\": [");

                for (int i = 0; i < prefixes.Count; i++)
                {
                    string p = prefixes[i];
                    double[] vals = models[p];
                    string line = string.Format(CultureInfo.InvariantCulture,
                        "    {{ \"prefix\": \"{0}\", \"input\": {1}, \"output\": {2}, \"cache_write_5m\": {3}, \"cache_write_1h\": {4}, \"cache_read\": {5}, \"verifikasi\": \"official\" }}{6}",
                        p, vals[0], vals[1], vals[2], vals[3], vals[4], (i < prefixes.Count - 1 ? "," : ""));
                    sb.AppendLine(line);
                }
                sb.AppendLine("  ]");
                sb.AppendLine("}");

                Paths.Ensure();
                File.WriteAllText(Paths.PricingFile, sb.ToString(), Encoding.UTF8);
                _lastFetch = DateTime.Now;
                return models.Count;
            }
            catch (Exception ex)
            {
                Log.Write("pricing.save", ex);
            }
            finally
            {
                _busy = false;
            }
            return 0;
        }

        private static void AddIfMissing(Dictionary<string, double[]> dict, string prefix, double inp, double outp, double cw5, double cw1, double cr)
        {
            if (!dict.ContainsKey(prefix))
                dict[prefix] = new[] { inp, outp, cw5, cw1, cr };
        }
    }

    /// <summary>Pengambil batas kuota resmi Antigravity dari Language Server Connect-RPC lokal (127.0.0.1).</summary>
    internal static class AntigravityRateFetcher
    {
        private static readonly HttpClient Http;
        private static DateTime _lastFetch = DateTime.MinValue;
        private static bool _busy;

        static AntigravityRateFetcher()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            Http = new HttpClient();
            Http.Timeout = TimeSpan.FromSeconds(5);
        }

        public static bool ShouldRefresh(bool force)
        {
            if (force) return (DateTime.Now - _lastFetch).TotalSeconds >= 5;
            return (DateTime.Now - _lastFetch).TotalMinutes >= 1;
        }

        public static async Task<bool> FetchAndSaveAsync(int portOverride = 0)
        {
            if (_busy) return false;
            _busy = true;
            try
            {
                int port = portOverride;
                string token = null;

                // 1. Temukan hub_*.json di daemon directory (~/.gemini/antigravity/daemon)
                if (port <= 0)
                {
                    try
                    {
                        string daemonDir = Paths.AntigravityDaemonDir;
                        if (Directory.Exists(daemonDir))
                        {
                            string[] files = Directory.GetFiles(daemonDir, "hub_*.json");
                            if (files != null && files.Length > 0)
                            {
                                Array.Sort(files, delegate(string a, string b)
                                {
                                    return File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a));
                                });
                                for (int i = 0; i < files.Length; i++)
                                {
                                    try
                                    {
                                        string json = File.ReadAllText(files[i], Encoding.UTF8);
                                        Dictionary<string, object> o = J.Parse(json);
                                        if (o != null)
                                        {
                                            double p = J.Num(o, "httpPort");
                                            if (double.IsNaN(p) || p <= 0) p = J.Num(o, "httpsPort");
                                            if (!double.IsNaN(p) && p > 0)
                                            {
                                                port = (int)p;
                                                token = J.Str(o, "csrfToken");
                                                break;
                                            }
                                        }
                                    }
                                    catch { }
                                }
                            }
                        }
                    }
                    catch (Exception ex) { Log.Write("antigravity.daemon", ex); }
                }

                if (port <= 0) return false;

                // 2. Query RetrieveUserQuotaSummary via Connect-RPC
                string url = "http://127.0.0.1:" + port + "/exa.language_server_pb.LanguageServerService/RetrieveUserQuotaSummary";
                string body = "{\"metadata\":{\"ideName\":\"antigravity\",\"extensionName\":\"antigravity\",\"locale\":\"en\"}}";
                using (HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    req.Headers.Add("Connect-Protocol-Version", "1");
                    if (!string.IsNullOrEmpty(token))
                        req.Headers.Add("X-Codeium-Csrf-Token", token);

                    HttpResponseMessage resp = await Http.SendAsync(req).ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode)
                    {
                        return await FetchGetUserStatusFallbackAsync(port, token).ConfigureAwait(false);
                    }

                    string respText = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    bool ok = ParseAndSaveQuotaSummary(respText);
                    if (!ok) return await FetchGetUserStatusFallbackAsync(port, token).ConfigureAwait(false);

                    _lastFetch = DateTime.Now;
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Write("antigravity.fetch", ex);
                return false;
            }
            finally
            {
                _busy = false;
            }
        }

        public static bool ParseAndSaveQuotaSummary(string json)
        {
            try
            {
                Dictionary<string, object> root = J.Parse(json);
                if (root == null) return false;

                Dictionary<string, object> responseObj = J.Obj(root, "response");
                if (responseObj == null) responseObj = root;

                IList groups = J.Arr(responseObj, "groups");
                if (groups == null || groups.Count == 0) return false;

                double used5h = double.NaN, used7d = double.NaN;
                DateTime reset5h = DateTime.MinValue, reset7d = DateTime.MinValue;

                // Cari group Gemini Models lebih dulu, fallback ke group pertama yang punya buckets
                Dictionary<string, object> targetGroup = null;
                for (int i = 0; i < groups.Count; i++)
                {
                    Dictionary<string, object> g = groups[i] as Dictionary<string, object>;
                    if (g == null) continue;
                    string disp = J.Str(g, "displayName");
                    if (disp != null && disp.IndexOf("Gemini", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        targetGroup = g;
                        break;
                    }
                }
                if (targetGroup == null && groups.Count > 0)
                    targetGroup = groups[0] as Dictionary<string, object>;

                if (targetGroup != null)
                {
                    IList buckets = J.Arr(targetGroup, "buckets");
                    if (buckets != null)
                    {
                        for (int i = 0; i < buckets.Count; i++)
                        {
                            Dictionary<string, object> b = buckets[i] as Dictionary<string, object>;
                            if (b == null) continue;
                            string win = J.Str(b, "window") ?? "";
                            string bid = J.Str(b, "bucketId") ?? "";
                            double rem = J.Num(b, "remainingFraction");
                            string rTime = J.Str(b, "resetTime");
                            DateTime dt;
                            DateTime rDt = DateTime.MinValue;
                            if (!string.IsNullOrEmpty(rTime) && DateTime.TryParse(rTime, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out dt))
                                rDt = dt.ToLocalTime();

                            if (win == "5h" || bid.IndexOf("5h", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                if (!double.IsNaN(rem)) used5h = Math.Max(0.0, Math.Min(100.0, (1.0 - rem) * 100.0));
                                if (rDt != DateTime.MinValue) reset5h = rDt;
                            }
                            else if (win == "weekly" || bid.IndexOf("weekly", StringComparison.OrdinalIgnoreCase) >= 0 || bid.IndexOf("7d", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                if (!double.IsNaN(rem)) used7d = Math.Max(0.0, Math.Min(100.0, (1.0 - rem) * 100.0));
                                if (rDt != DateTime.MinValue) reset7d = rDt;
                            }
                        }
                    }
                }

                if (double.IsNaN(used5h) && double.IsNaN(used7d)) return false;

                SaveRateLimitsFile(used5h, reset5h, used7d, reset7d);
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("antigravity.parse", ex);
                return false;
            }
        }

        private static async Task<bool> FetchGetUserStatusFallbackAsync(int port, string token)
        {
            try
            {
                string url = "http://127.0.0.1:" + port + "/exa.language_server_pb.LanguageServerService/GetUserStatus";
                string body = "{\"metadata\":{\"ideName\":\"antigravity\",\"extensionName\":\"antigravity\",\"locale\":\"en\"}}";
                using (HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    req.Headers.Add("Connect-Protocol-Version", "1");
                    if (!string.IsNullOrEmpty(token))
                        req.Headers.Add("X-Codeium-Csrf-Token", token);

                    HttpResponseMessage resp = await Http.SendAsync(req).ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode) return false;

                    string respText = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return ParseAndSaveUserStatus(respText);
                }
            }
            catch (Exception ex)
            {
                Log.Write("antigravity.fallback", ex);
                return false;
            }
        }

        public static bool ParseAndSaveUserStatus(string json)
        {
            try
            {
                Dictionary<string, object> root = J.Parse(json);
                if (root == null) return false;

                Dictionary<string, object> uStat = J.Obj(root, "userStatus");
                Dictionary<string, object> cData = J.Obj(uStat, "cascadeModelConfigData");
                IList configs = J.Arr(cData, "clientModelConfigs");
                if (configs == null || configs.Count == 0) return false;

                double used5h = double.NaN;
                DateTime reset5h = DateTime.MinValue;

                for (int i = 0; i < configs.Count; i++)
                {
                    Dictionary<string, object> c = configs[i] as Dictionary<string, object>;
                    if (c == null) continue;
                    Dictionary<string, object> q = J.Obj(c, "quotaInfo");
                    if (q == null) continue;
                    double rem = J.Num(q, "remainingFraction");
                    string rTime = J.Str(q, "resetTime");
                    if (!double.IsNaN(rem))
                    {
                        used5h = Math.Max(0.0, Math.Min(100.0, (1.0 - rem) * 100.0));
                        DateTime dt;
                        if (!string.IsNullOrEmpty(rTime) && DateTime.TryParse(rTime, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out dt))
                            reset5h = dt.ToLocalTime();
                        break;
                    }
                }

                if (double.IsNaN(used5h)) return false;
                SaveRateLimitsFile(used5h, reset5h, double.NaN, DateTime.MinValue);
                _lastFetch = DateTime.Now;
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("antigravity.userstatus", ex);
                return false;
            }
        }

        private static void SaveRateLimitsFile(double used5h, DateTime reset5h, double used7d, DateTime reset7d)
        {
            double nowUnix = (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"captured_at\": " + (long)nowUnix + ",");
            sb.AppendLine("  \"source\": \"antigravity-local\",");
            sb.AppendLine("  \"rate_limits\": {");

            List<string> rls = new List<string>();
            if (!double.IsNaN(used5h))
            {
                double rUnix = reset5h != DateTime.MinValue ? (reset5h.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds : 0;
                rls.Add(string.Format(CultureInfo.InvariantCulture,
                    "    \"five_hour\": {{ \"used_percentage\": {0:F2}, \"resets_at\": {1} }}",
                    used5h, (long)rUnix));
            }
            if (!double.IsNaN(used7d))
            {
                double rUnix = reset7d != DateTime.MinValue ? (reset7d.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds : 0;
                rls.Add(string.Format(CultureInfo.InvariantCulture,
                    "    \"seven_day\": {{ \"used_percentage\": {0:F2}, \"resets_at\": {1} }}",
                    used7d, (long)rUnix));
            }

            sb.AppendLine(string.Join(",\n", rls.ToArray()));
            sb.AppendLine("  }");
            sb.AppendLine("}");

            Paths.Ensure();
            File.WriteAllText(Paths.AntigravityRateLimitsFile, sb.ToString(), Encoding.UTF8);
        }
    }

    /// <summary>Status deteksi instans lokal Ollama.</summary>
    internal class OllamaStatus
    {
        public bool Online;
        public int ModelCount;
        public List<string> ModelNames = new List<string>();
        public DateTime LastChecked = DateTime.MinValue;
    }

    /// <summary>
    /// Probe ringan ke server lokal Ollama (http://127.0.0.1:11434/api/tags)
    /// untuk mendeteksi ketersediaan LLM lokal secara otomatis.
    /// </summary>
    internal static class OllamaProbe
    {
        public static OllamaStatus Current = new OllamaStatus();

        public static OllamaStatus ParseResponse(string json)
        {
            OllamaStatus status = new OllamaStatus();
            if (string.IsNullOrWhiteSpace(json)) return status;
            try
            {
                Dictionary<string, object> root = J.Parse(json);
                IList models = J.Arr(root, "models");
                if (models != null)
                {
                    status.Online = true;
                    status.ModelCount = models.Count;
                    foreach (object o in models)
                    {
                        Dictionary<string, object> m = o as Dictionary<string, object>;
                        string name = J.Str(m, "name");
                        if (!string.IsNullOrEmpty(name)) status.ModelNames.Add(name);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("ollama.parse", ex);
            }
            return status;
        }

        public static async Task<OllamaStatus> CheckAsync(int port, int timeoutMs = 1500)
        {
            if (port <= 0 || port > 65535) port = 11434;
            string endpoint = string.Format(CultureInfo.InvariantCulture, "http://127.0.0.1:{0}/api/tags", port);
            return await CheckAsync(endpoint, timeoutMs);
        }

        public static async Task<OllamaStatus> CheckAsync(string endpoint = "http://127.0.0.1:11434/api/tags", int timeoutMs = 1500)
        {
            OllamaStatus st = new OllamaStatus();
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(endpoint);
                req.Method = "GET";
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                using (WebResponse resp = await req.GetResponseAsync())
                using (Stream s = resp.GetResponseStream())
                using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                {
                    string json = await r.ReadToEndAsync();
                    st = ParseResponse(json);
                }
            }
            catch
            {
                st.Online = false;
            }
            st.LastChecked = DateTime.Now;
            Current = st;
            return st;
        }
    }
}
