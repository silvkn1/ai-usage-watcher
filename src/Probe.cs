using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace ClaudeUsageWidget
{
    /// <summary>
    /// Probe limit: menjalankan Claude Code CLI headless dengan satu pesan kecil ("ok"),
    /// lalu membaca event 'rate_limit_event' dari output stream-json.
    /// Terisolasi: hook dimatikan (via file settings), tanpa MCP, tanpa tool, tanpa menyimpan sesi (tidak menulis JSONL).
    /// Catatan: format event ini tidak terdokumentasi resmi dan bisa berubah. Diuji pada Claude Code 2.1.330.
    /// </summary>
    internal static class CliProbe
    {
        /// <summary>claude.exe (installer native) atau claude.cmd (npm). Null bila tidak ada.</summary>
        public static string FindClaude()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string local = Path.Combine(home, @".local\bin\claude.exe");
            if (File.Exists(local)) return local;
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string name in new[] { "claude.exe", "claude.cmd" })
                foreach (string dir in path.Split(';'))
                {
                    try
                    {
                        string t = dir.Trim().Trim('"');
                        if (!ProcessHelper.IsSafeSearchDir(t)) continue;
                        string p = Path.Combine(t, name);
                        if (File.Exists(p)) return p;
                    }
                    catch { }
                }
            string npm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"npm\claude.cmd");
            if (File.Exists(npm)) return npm;
            return FindDesktopBundled();
        }

        /// <summary>Cadangan: Claude Code yang dibundel Claude desktop app
        /// (%LOCALAPPDATA%\Claude-Data\claude_*\claude-code\&lt;versi&gt;\&lt;hash&gt;\claude.exe), versi terbaru.
        /// Diuji pada bundel 2.1.326: memakai login langganan yang sama.</summary>
        private static string FindDesktopBundled()
        {
            try
            {
                string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Claude-Data");
                if (!Directory.Exists(root)) return null;
                string best = null;
                Version bestV = null;
                foreach (string pkg in Directory.GetDirectories(root, "claude_*"))
                {
                    string cc = Path.Combine(pkg, "claude-code");
                    if (!Directory.Exists(cc)) continue;
                    foreach (string verDir in Directory.GetDirectories(cc))
                    {
                        Version v;
                        if (!Version.TryParse(Path.GetFileName(verDir), out v)) continue;
                        foreach (string hashDir in Directory.GetDirectories(verDir))
                        {
                            string exe = Path.Combine(hashDir, "claude.exe");
                            if (File.Exists(exe) && (bestV == null || v > bestV)) { best = exe; bestV = v; }
                        }
                    }
                }
                return best;
            }
            catch { return null; }
        }

        private static string SettingsFile()
        {
            string f = Path.Combine(Paths.AppDir, "probe-settings.json");
            if (!File.Exists(f)) File.WriteAllText(f, "{\"disableAllHooks\":true}", new UTF8Encoding(false));
            return f;
        }

        public static bool IsValidModelName(string model)
        {
            if (string.IsNullOrEmpty(model) || model.Length > 80) return false;
            for (int i = 0; i < model.Length; i++)
            {
                char c = model[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ||
                          c == '-' || c == '_' || c == '.' || c == ':' || c == '/' || c == '[' || c == ']';
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>Null bila sukses: ratelimits.json ditulis dan rateLimitsJson berisi objek rate_limits
        /// (format sama dengan statusline). Selain itu pesan error singkat yang menjelaskan penyebab.</summary>
        public static string Run(string model, out string rateLimitsJson)
        {
            rateLimitsJson = null;
            string exe = FindClaude();
            if (exe == null) return L.T("perr.notfound");
            if (!IsValidModelName(model)) model = "claude-haiku-5-5";
            string settingsPath = SettingsFile();
            if (ProcessHelper.HasCmdMeta(settingsPath) || exe.IndexOfAny(new[] { '"', '\r', '\n' }) >= 0 ||
                (exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) && ProcessHelper.HasCmdMeta(exe)))
                return L.T("perr.start", "InvalidPath");

            string args = "-p ok --settings \"" + settingsPath + "\" --strict-mcp-config --tools \"\" --model " + model
                          + " --output-format stream-json --verbose --no-session-persistence";
            ProcessStartInfo psi;
            if (exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
                psi = new ProcessStartInfo(ProcessHelper.SystemExe("cmd.exe"), "/d /s /c \"\"" + exe + "\" " + args + "\"");
            else
                psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.RedirectStandardInput = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.WorkingDirectory = Paths.AppDir;

            // Hanya baris yang relevan yang disimpan (bukan isi jawaban model).
            Dictionary<string, object> windows = null;
            string authSource = null, version = null, resultError = null;
            bool sawResult = false;
            object gate = new object();
            List<string> keep = new List<string>();
            using (Process p = new Process())
            {
                p.StartInfo = psi;
                p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data == null) return;
                    if (e.Data.IndexOf("\"rate_limit_event\"", StringComparison.Ordinal) >= 0
                        || e.Data.IndexOf("\"type\":\"result\"", StringComparison.Ordinal) >= 0
                        || e.Data.IndexOf("\"type\": \"result\"", StringComparison.Ordinal) >= 0
                        || e.Data.IndexOf("\"subtype\":\"init\"", StringComparison.Ordinal) >= 0
                        || e.Data.IndexOf("\"subtype\": \"init\"", StringComparison.Ordinal) >= 0)
                        lock (gate) keep.Add(e.Data);
                };
                p.ErrorDataReceived += delegate { };
                try { p.Start(); }
                catch (Exception ex) { return L.T("perr.start", ex.GetType().Name); }
                try { p.StandardInput.Close(); } catch { }
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                if (!p.WaitForExit(90 * 1000))
                {
                    ProcessHelper.KillTree(p);
                    return L.T("perr.timeout");
                }
                p.WaitForExit();
            }

            foreach (string line in keep)
            {
                Dictionary<string, object> o;
                try { o = J.Parse(line); } catch { continue; }
                string type = J.Str(o, "type");
                if (type == "rate_limit_event")
                {
                    Dictionary<string, object> uw = J.Obj(J.Obj(o, "rate_limit_info"), "unifiedWindows");
                    if (uw != null) windows = uw;   // ambil event terakhir
                }
                else if (type == "system")
                {
                    authSource = J.Str(o, "apiKeySource") ?? authSource;
                    version = J.Str(o, "claude_code_version") ?? version;
                }
                else if (type == "result")
                {
                    sawResult = true;
                    object isErr;
                    if (o.TryGetValue("is_error", out isErr) && isErr is bool && (bool)isErr)
                        resultError = J.Str(o, "api_error_status") ?? J.Str(o, "result") ?? "error";
                }
            }

            if (windows == null)
            {
                if (!sawResult) return L.T("perr.unrecognized", version ?? "?");
                if (resultError != null) return L.T("perr.cli", resultError);
                if (authSource != null && authSource != "none")
                    return L.T("perr.auth", authSource);
                return L.T("perr.nolimit", version ?? "?");
            }

            StringBuilder rl = new StringBuilder("{");
            foreach (string name in new[] { "five_hour", "seven_day" })
            {
                Dictionary<string, object> w = J.Obj(windows, name);
                double util = J.Num(w, "utilization");
                if (double.IsNaN(util)) continue;
                if (rl.Length > 1) rl.Append(',');
                // Bulatkan seperti statusline (integer) agar format sama bagi pembaca lain (mis. mod Windhawk).
                rl.Append('"').Append(name).Append("\":{\"used_percentage\":")
                  .Append(((int)Math.Round(util * 100)).ToString(CultureInfo.InvariantCulture));
                double reset = J.Num(w, "resetsAt");
                if (!double.IsNaN(reset)) rl.Append(",\"resets_at\":").Append(((long)reset).ToString(CultureInfo.InvariantCulture));
                rl.Append('}');
            }
            rl.Append('}');
            if (rl.Length <= 2) return L.T("perr.nowindows");
            rateLimitsJson = rl.ToString();

            string snapshot = "{\"captured_at\":" + (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds
                              + ",\"source\":\"cli-probe\",\"rate_limits\":" + rateLimitsJson + "}";
            AtomicFile.Write(Paths.RateLimitsFile, snapshot);
            return null;
        }
    }

    internal static class AtomicFile
    {
        /// <summary>Tulis ke file sementara unik di folder yang sama lalu tukar (File.Replace) agar pembaca tidak melihat file setengah jadi.</summary>
        public static void Write(string path, string content)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            Encoding utf8NoBom = new UTF8Encoding(false);
            int pid;
            using (Process cur = Process.GetCurrentProcess()) pid = cur.Id;
            string tmp = path + ".cuw." + pid + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tmp, content, utf8NoBom);
            try
            {
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    try
                    {
                        if (File.Exists(path)) File.Replace(tmp, path, null);
                        else File.Move(tmp, path);
                        return;
                    }
                    catch (IOException)
                    {
                        if (attempt < 3) System.Threading.Thread.Sleep(20);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        if (attempt < 3) System.Threading.Thread.Sleep(20);
                    }
                }
                // Fallback when OS/security filter (e.g. Defender/AppContainer ACL) blocks rename/replace.
                File.WriteAllText(path, content, utf8NoBom);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }
    }

    /// <summary>
    /// Cermin rate_limits ke file JSON lain (mis. file status untuk mod Windhawk) setelah probe berhasil.
    /// Hanya mengganti nilai "rate_limits" di root objek JSON; sisanya tetap identik byte demi byte.
    /// File tidak pernah dibuat baru, dan dilewati bila isinya tidak bisa di-parse (mungkin sedang ditulis).
    /// </summary>
    internal static class Mirror
    {
        public static string Write(string path, string rateLimitsJson)
        {
            if (string.IsNullOrEmpty(path)) return "empty path";
            if (!File.Exists(path)) return "file does not exist";
            try { if (J.Parse(rateLimitsJson) == null) return "invalid rate_limits JSON"; }
            catch { return "invalid rate_limits JSON"; }

            string text = Paths.ReadAllTextShared(path);
            try { if (J.Parse(text) == null) return "not a JSON object"; }
            catch { return "invalid JSON (maybe being written)"; }

            int valStart, valEnd;
            if (!FindRootPropertyValueSpan(text, "rate_limits", out valStart, out valEnd))
            {
                int last = text.LastIndexOf('}');
                if (last < 0) return "not a JSON object";
                string before = text.Substring(0, last).TrimEnd();
                string sep = before.EndsWith("{", StringComparison.Ordinal) ? "" : ",";
                string appended = before + sep + "\"rate_limits\":" + rateLimitsJson + text.Substring(last);
                try { J.Parse(appended); } catch { return "spliced result is invalid"; }
                AtomicFile.Write(path, appended);
                return null;
            }
            if (valEnd <= valStart) return "malformed rate_limits value";
            string updated = text.Substring(0, valStart) + rateLimitsJson + text.Substring(valEnd);
            try { J.Parse(updated); } catch { return "spliced result is invalid"; }
            AtomicFile.Write(path, updated);
            return null;
        }

        private static bool FindRootPropertyValueSpan(string s, string targetKey, out int valStart, out int valEnd)
        {
            valStart = -1; valEnd = -1;
            int i = 0;
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            if (i >= s.Length || s[i] != '{') return false;
            int depth = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '"')
                {
                    int keyEnd = UsageScanner.SkipValue(s, i);
                    string rawKey = s.Substring(i, Math.Max(0, keyEnd - i));
                    bool isTarget = false;
                    if (depth == 1)
                    {
                        try
                        {
                            object dec = J.Ser.DeserializeObject(rawKey);
                            isTarget = string.Equals(dec as string, targetKey, StringComparison.Ordinal);
                        }
                        catch { }
                    }
                    int j = keyEnd;
                    while (j < s.Length && char.IsWhiteSpace(s[j])) j++;
                    if (isTarget && j < s.Length && s[j] == ':')
                    {
                        j++;
                        while (j < s.Length && char.IsWhiteSpace(s[j])) j++;
                        int end = UsageScanner.SkipValue(s, j);
                        if (end > j) { valStart = j; valEnd = end; return true; }
                        return false;
                    }
                    i = keyEnd;
                    continue;
                }
                if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']') depth--;
                i++;
            }
            return false;
        }
    }
}
