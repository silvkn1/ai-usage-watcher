using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace ClaudeUsageWidget
{
    /// <summary>Penulis JSON berindentasi yang mempertahankan urutan kunci dan tidak mengubah karakter (&lt;, &amp;, ' tetap apa adanya).</summary>
    internal static class PrettyJson
    {
        public static string Write(object o)
        {
            StringBuilder sb = new StringBuilder();
            W(sb, o, 0);
            sb.Append('\n');
            return sb.ToString();
        }

        private static void W(StringBuilder sb, object o, int ind)
        {
            Dictionary<string, object> d = o as Dictionary<string, object>;
            IList l = o as IList;
            if (d != null)
            {
                if (d.Count == 0) { sb.Append("{}"); return; }
                sb.Append("{\n");
                int i = 0;
                foreach (KeyValuePair<string, object> kv in d)
                {
                    sb.Append(' ', ind + 2).Append(Str(kv.Key)).Append(": ");
                    W(sb, kv.Value, ind + 2);
                    sb.Append(++i < d.Count ? ",\n" : "\n");
                }
                sb.Append(' ', ind).Append('}');
            }
            else if (l != null)
            {
                if (l.Count == 0) { sb.Append("[]"); return; }
                sb.Append("[\n");
                for (int i = 0; i < l.Count; i++)
                {
                    sb.Append(' ', ind + 2);
                    W(sb, l[i], ind + 2);
                    sb.Append(i < l.Count - 1 ? ",\n" : "\n");
                }
                sb.Append(' ', ind).Append(']');
            }
            else if (o == null) sb.Append("null");
            else if (o is bool) sb.Append((bool)o ? "true" : "false");
            else if (o is string) sb.Append(Str((string)o));
            else if (o is double) sb.Append(((double)o).ToString("R", CultureInfo.InvariantCulture));
            else sb.Append(Convert.ToString(o, CultureInfo.InvariantCulture));
        }

        private static string Str(string s)
        {
            StringBuilder b = new StringBuilder("\"");
            foreach (char c in s)
            {
                if (c == '"') b.Append("\\\"");
                else if (c == '\\') b.Append("\\\\");
                else if (c == '\n') b.Append("\\n");
                else if (c == '\r') b.Append("\\r");
                else if (c == '\t') b.Append("\\t");
                else if (c < 0x20) b.Append("\\u").Append(((int)c).ToString("x4"));
                else b.Append(c);
            }
            return b.Append('"').ToString();
        }

        /// <summary>Bentuk kanonik (kunci diurutkan) untuk membandingkan dua nilai JSON tanpa peduli urutan.</summary>
        public static string Canon(object o)
        {
            Dictionary<string, object> d = o as Dictionary<string, object>;
            IList l = o as IList;
            if (d != null)
            {
                List<string> keys = new List<string>(d.Keys);
                keys.Sort(StringComparer.Ordinal);
                StringBuilder sb = new StringBuilder("{");
                foreach (string k in keys) sb.Append(Str(k)).Append(':').Append(Canon(d[k])).Append(',');
                return sb.Append('}').ToString();
            }
            if (l != null)
            {
                StringBuilder sb = new StringBuilder("[");
                foreach (object x in l) sb.Append(Canon(x)).Append(',');
                return sb.Append(']').ToString();
            }
            if (o == null) return "null";
            if (o is string) return Str((string)o);
            if (o is bool) return (bool)o ? "true" : "false";
            return Convert.ToString(o, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Memasang/melepas hook Claude Code untuk notifikasi (UserPromptSubmit = catat waktu prompt, Stop = respons selesai,
    /// Notification = butuh input). Bentuk exec ('command' + 'args') dipakai agar tidak bergantung pada shell dan quoting.
    /// Entri milik orang lain tidak disentuh; berkas dicadangkan dan hasilnya diverifikasi sebelum dianggap berhasil.
    /// </summary>
    internal static class HookInstaller
    {
        private static readonly string[][] Events = { new[] { "UserPromptSubmit", "prompt" }, new[] { "Stop", "stop" }, new[] { "Notification", "input" } };
        private const string ExeName = "ClaudeUsageWidget";

        public static string SettingsPath { get { return Path.Combine(Paths.ClaudeDir, "settings.json"); } }

        /// <summary>Entri kita: handler dengan 'command' yang memuat nama exe ini dan args[0] == "--notify".</summary>
        private static bool IsOurs(object handler)
        {
            Dictionary<string, object> h = handler as Dictionary<string, object>;
            if (h == null) return false;
            string cmd = J.Str(h, "command");
            IList args = J.Arr(h, "args");
            return cmd != null && cmd.IndexOf(ExeName, StringComparison.OrdinalIgnoreCase) >= 0
                   && args != null && args.Count > 0 && Convert.ToString(args[0]) == "--notify";
        }

        /// <summary>Salinan daftar grup tanpa handler kita; grup yang menjadi kosong karenanya dibuang.</summary>
        private static ArrayList Strip(IList groups)
        {
            ArrayList r = new ArrayList();
            if (groups == null) return r;
            foreach (object g in groups)
            {
                Dictionary<string, object> gd = g as Dictionary<string, object>;
                IList hs = gd == null ? null : J.Arr(gd, "hooks");
                if (gd == null || hs == null) { r.Add(g); continue; }
                ArrayList keep = new ArrayList();
                foreach (object h in hs) if (!IsOurs(h)) keep.Add(h);
                if (keep.Count == hs.Count) { r.Add(g); continue; }
                if (keep.Count == 0) continue;
                Dictionary<string, object> copy = new Dictionary<string, object>(gd);
                copy["hooks"] = keep;
                r.Add(copy);
            }
            return r;
        }

        public static bool IsInstalled()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return false;
                Dictionary<string, object> hooks = J.Obj(J.Parse(File.ReadAllText(SettingsPath, Encoding.UTF8)), "hooks");
                if (hooks == null) return false;
                foreach (string[] ev in Events)
                {
                    IList groups = J.Arr(hooks, ev[0]);
                    if (groups == null || Strip(groups).Count == groups.Count) return false;
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>Null bila sukses; selain itu pesan galat (berkas tidak diubah).</summary>
        public static string Install(string exePath)
        {
            return Modify(true, exePath);
        }

        public static string Uninstall()
        {
            return Modify(false, null);
        }

        private static string Modify(bool install, string exePath)
        {
            try
            {
                string path = SettingsPath;
                bool existed = File.Exists(path);
                if (!existed && !install) return null;
                string orig = existed ? File.ReadAllText(path, Encoding.UTF8) : "{}";
                Dictionary<string, object> root = J.Parse(orig);
                Dictionary<string, object> before = J.Parse(orig);
                if (root == null || before == null) return "settings.json is not a JSON object";
                object hooksObj;
                Dictionary<string, object> hooks = null;
                if (root.TryGetValue("hooks", out hooksObj))
                {
                    hooks = hooksObj as Dictionary<string, object>;
                    if (hooks == null) return "settings.json: 'hooks' has an unexpected shape";
                }

                bool touched = false;
                foreach (string[] ev in Events)
                {
                    IList cur = hooks == null ? null : J.Arr(hooks, ev[0]);
                    ArrayList list = Strip(cur);
                    bool had = cur != null && list.Count != cur.Count;
                    if (install)
                    {
                        if (hooks == null) { hooks = new Dictionary<string, object>(); root["hooks"] = hooks; }
                        Dictionary<string, object> handler = new Dictionary<string, object>();
                        handler["type"] = "command";
                        handler["command"] = exePath;
                        handler["args"] = new ArrayList { "--notify", ev[1] };
                        handler["timeout"] = 10;
                        Dictionary<string, object> group = new Dictionary<string, object>();
                        group["hooks"] = new ArrayList { handler };
                        list.Add(group);
                        hooks[ev[0]] = list;
                        touched = true;
                    }
                    else if (had)
                    {
                        if (list.Count == 0) hooks.Remove(ev[0]); else hooks[ev[0]] = list;
                        touched = true;
                    }
                }
                if (!install)
                {
                    if (!touched) return null;
                    if (hooks != null && hooks.Count == 0) root.Remove("hooks");
                }

                string text = PrettyJson.Write(root);
                Dictionary<string, object> after = J.Parse(text);
                // Verifikasi: semua kunci selain 'hooks' tidak berubah; di 'hooks', semua selain entri kita tidak berubah.
                foreach (KeyValuePair<string, object> kv in before)
                {
                    if (kv.Key == "hooks") continue;
                    object av;
                    if (!after.TryGetValue(kv.Key, out av) || PrettyJson.Canon(kv.Value) != PrettyJson.Canon(av)) return "verification failed on key '" + kv.Key + "'; nothing was changed";
                }
                if (after.Count - (after.ContainsKey("hooks") ? 1 : 0) != before.Count - (before.ContainsKey("hooks") ? 1 : 0)) return "verification failed (extra keys); nothing was changed";
                Dictionary<string, object> bh = J.Obj(before, "hooks") ?? new Dictionary<string, object>();
                Dictionary<string, object> ah = J.Obj(after, "hooks") ?? new Dictionary<string, object>();
                HashSet<string> names = new HashSet<string>(bh.Keys); foreach (string k in ah.Keys) names.Add(k);
                foreach (string n in names)
                {
                    object bv, av;
                    bh.TryGetValue(n, out bv); ah.TryGetValue(n, out av);
                    if (PrettyJson.Canon(Strip(bv as IList)) != PrettyJson.Canon(Strip(av as IList)))
                        return "verification failed on hooks." + n + "; nothing was changed";
                }

                if (existed && text == orig) return null;   // tidak ada perubahan: jangan tulis ulang atau buat cadangan
                Directory.CreateDirectory(Paths.ClaudeDir);
                if (existed)
                {
                    // Nama cadangan unik (milidetik + penghitung) agar dua operasi beruntun tidak bentrok.
                    string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
                    string bak = path + ".bak-cuw-" + stamp;
                    for (int n = 1; File.Exists(bak); n++) bak = path + ".bak-cuw-" + stamp + "-" + n;
                    File.Copy(path, bak, false);
                }
                AtomicFile.Write(path, text);
                return null;
            }
            catch (Exception ex) { Log.Write("hooks.modify", ex); return ex.GetType().Name + ": " + ex.Message; }
        }
    }

    /// <summary>Peristiwa dari hook: berkas notify.json kecil (jenis, lama respons, cap waktu). Tanpa isi percakapan.</summary>
    internal static class NotifyEvents
    {
        public static void Write(string kind, long elapsedSeconds, long tsUnix)
        {
            Paths.Ensure();
            AtomicFile.Write(Paths.NotifyFile,
                "{\"kind\":\"" + kind + "\",\"elapsed\":" + elapsedSeconds.ToString(CultureInfo.InvariantCulture)
                + ",\"ts\":" + tsUnix.ToString(CultureInfo.InvariantCulture) + "}");
        }

        /// <summary>Baca lalu hapus peristiwa; null bila tidak ada atau rusak.</summary>
        public static Dictionary<string, object> TryTake()
        {
            try
            {
                string f = Paths.NotifyFile;
                if (!File.Exists(f)) return null;
                string txt;
                using (FileStream fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader r = new StreamReader(fs, Encoding.UTF8)) txt = r.ReadToEnd();
                try { File.Delete(f); } catch { }
                return J.Parse(txt);
            }
            catch { return null; }
        }
    }

    /// <summary>
    /// Dipanggil oleh hook Claude Code: 'ClaudeUsageWidget.exe --notify prompt|stop|input'. HARUS selalu keluar 0 tanpa
    /// keluaran apa pun (pada Stop, exit 2 akan mencegah Claude berhenti; pada UserPromptSubmit, stdout masuk ke prompt).
    /// Lama respons dihitung dari cap waktu prompt per sesi (tanpa membaca transkrip percakapan).
    /// </summary>
    internal static class NotifyCli
    {
        public static int Run(string kind)
        {
            try
            {
                string stdin = ReadStdin(1500);
                Dictionary<string, object> o = null;
                try { o = J.Parse(stdin); } catch { }
                string sid = Sanitize(J.Str(o, "session_id"));
                long now = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
                if (kind == "prompt")
                {
                    if (sid != null)
                    {
                        Directory.CreateDirectory(Paths.HooksDir);
                        File.WriteAllText(Path.Combine(Paths.HooksDir, sid + ".txt"), now.ToString(CultureInfo.InvariantCulture));
                        Prune();
                    }
                }
                else if (kind == "stop")
                {
                    long elapsed = -1;
                    if (sid != null)
                    {
                        string f = Path.Combine(Paths.HooksDir, sid + ".txt");
                        if (File.Exists(f))
                        {
                            long start;
                            if (long.TryParse(File.ReadAllText(f).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out start)) elapsed = Math.Max(0, now - start);
                            try { File.Delete(f); } catch { }
                        }
                    }
                    NotifyEvents.Write("stop", elapsed, now);
                }
                else if (kind == "input") NotifyEvents.Write("input", -1, now);
            }
            catch { }
            return 0;
        }

        private static string ReadStdin(int timeoutMs)
        {
            try
            {
                Task<string> t = Task.Run(delegate
                {
                    using (Stream s = Console.OpenStandardInput())
                    using (StreamReader r = new StreamReader(s, new UTF8Encoding(false))) return r.ReadToEnd();
                });
                return t.Wait(timeoutMs) ? t.Result : "";
            }
            catch { return ""; }
        }

        private static string Sanitize(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 80) return null;
            foreach (char c in id) if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_')) return null;
            return id;
        }

        private static void Prune()
        {
            try
            {
                foreach (string f in Directory.GetFiles(Paths.HooksDir, "*.txt"))
                    if ((DateTime.UtcNow - File.GetLastWriteTimeUtc(f)).TotalDays > 2) { try { File.Delete(f); } catch { } }
            }
            catch { }
        }
    }
}
