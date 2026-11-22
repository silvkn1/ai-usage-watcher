using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ClaudeUsageWidget
{
    internal class LangPackInfo
    {
        public string Code, Name, Path;
    }

    /// <summary>
    /// Paket bahasa: berkas JSON '{kode}.json' di folder 'lang' (di samping exe atau di folder data).
    /// Format: {"_name": "Nama bahasa", "_culture": "ms-MY", "kunci": "teks", ...}. Kunci yang tidak ada memakai teks Inggris.
    /// Bahasa bawaan (en, id) tidak bisa ditimpa. Berkas dibatasi 256 KB.
    /// </summary>
    internal static class LangPacks
    {
        private static readonly Regex CodeRe = new Regex(@"^[a-z]{2,3}(-[A-Za-z0-9]{2,8})?$", RegexOptions.Compiled);
        private const long MaxBytes = 256 * 1024;

        public static List<LangPackInfo> Discover()
        {
            List<LangPackInfo> r = new List<LangPackInfo>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "en", "id" };
            foreach (string dir in new[] { Paths.LangDirAppData, Paths.LangDirExe })
            {
                try
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (string f in Directory.GetFiles(dir, "*.json"))
                    {
                        string code = System.IO.Path.GetFileNameWithoutExtension(f);
                        if (!CodeRe.IsMatch(code) || seen.Contains(code)) continue;
                        FileInfo fi = new FileInfo(f);
                        if (fi.Length > MaxBytes) { Log.Write("langpack: file too large"); continue; }
                        seen.Add(code);
                        LangPackInfo i = new LangPackInfo();
                        i.Code = code; i.Path = f; i.Name = code;
                        try
                        {
                            Dictionary<string, object> o = J.Parse(File.ReadAllText(f, Encoding.UTF8));
                            string n = J.Str(o, "_name");
                            if (!string.IsNullOrWhiteSpace(n)) i.Name = n.Trim();
                        }
                        catch { continue; }   // JSON rusak: lewati
                        r.Add(i);
                    }
                }
                catch (Exception ex) { Log.Write("langpack.discover", ex); }
            }
            r.Sort(delegate(LangPackInfo a, LangPackInfo b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
            return r;
        }

        public static LangPackInfo Find(string code)
        {
            foreach (LangPackInfo i in Discover()) if (string.Equals(i.Code, code, StringComparison.OrdinalIgnoreCase)) return i;
            return null;
        }

        /// <summary>"en", "id", atau kode paket yang ada.</summary>
        public static bool Exists(string code)
        {
            if (code == "en" || code == "id") return true;
            return !string.IsNullOrEmpty(code) && Find(code) != null;
        }

        public static bool Load(LangPackInfo info, out Dictionary<string, string> strings, out CultureInfo culture)
        {
            strings = new Dictionary<string, string>();
            culture = null;
            try
            {
                Dictionary<string, object> o = J.Parse(File.ReadAllText(info.Path, Encoding.UTF8));
                foreach (KeyValuePair<string, object> kv in o)
                {
                    if (kv.Key.StartsWith("_", StringComparison.Ordinal)) continue;
                    string s = kv.Value as string;
                    if (s != null) strings[kv.Key] = s;
                }
                string cn = J.Str(o, "_culture");
                if (!string.IsNullOrEmpty(cn))
                    try { culture = CultureInfo.GetCultureInfo(cn); } catch (CultureNotFoundException) { }
                return true;
            }
            catch (Exception ex) { Log.Write("langpack.load", ex); return false; }
        }
    }
}
