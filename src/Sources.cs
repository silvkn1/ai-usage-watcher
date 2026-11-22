using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
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
        public DateTime CapturedAt = DateTime.MinValue;
        public bool FileFound;
        public string Source = "statusline";
    }

    /// <summary>Membaca snapshot rate_limits yang ditulis skrip statusline (cuw-statusline.sh).</summary>
    internal static class RateLimitReader
    {
        public static RateLimitSnapshot Read()
        {
            RateLimitSnapshot s = new RateLimitSnapshot();
            try
            {
                string file = Paths.RateLimitsReadFile;
                if (!File.Exists(file)) return s;
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
}
