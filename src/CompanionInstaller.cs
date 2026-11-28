using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace ClaudeUsageWidget
{
    internal static class CompanionInstaller
    {
        public static bool Install(out string installedPaths, out string error)
        {
            return InstallTo(null, out installedPaths, out error);
        }

        private static string[] IdeRoots()
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return new[]
            {
                Path.Combine(userProfile, ".vscode", "extensions"),
                Path.Combine(userProfile, ".cursor", "extensions"),
                Path.Combine(userProfile, ".antigravity", "extensions"),
                Path.Combine(userProfile, ".windsurf", "extensions")
            };
        }

        public static bool InstallTo(string customDir, out string installedPaths, out string error)
        {
            installedPaths = "";
            error = null;
            try
            {
                string pkgJson = ReadAsset("companion-package.json", Path.Combine("companion", "vscode-status-bar", "package.json"));
                string extJs = ReadAsset("companion-extension.js", Path.Combine("companion", "vscode-status-bar", "extension.js"));
                string readmeMd = ReadAsset("companion-readme.md", Path.Combine("companion", "vscode-status-bar", "README.md"));
                byte[] iconPng = ReadAssetBytes("companion-icon.png", Path.Combine("companion", "vscode-status-bar", "icon.png"));
                if (string.IsNullOrEmpty(pkgJson) || string.IsNullOrEmpty(extJs))
                {
                    error = "Companion extension assets not found.";
                    return false;
                }

                // Direct folder install (used by tests or custom folder caller).
                if (!string.IsNullOrEmpty(customDir))
                {
                    WriteUnpackedFiles(customDir, pkgJson, extJs, readmeMd, iconPng);
                    installedPaths = customDir;
                    return true;
                }

                ExtMeta meta = ParseMeta(pkgJson);
                List<string> roots = DetectTargetRoots();
                byte[] vsixBytes = BuildVsix(meta, pkgJson, extJs, readmeMd, iconPng);
                string tempVsix = Path.Combine(Path.GetTempPath(), "ai-usage-watcher-" + Guid.NewGuid().ToString("N") + ".vsix");
                List<string> successful = new List<string>();

                try
                {
                    File.WriteAllBytes(tempVsix, vsixBytes);

                    foreach (string root in roots)
                    {
                        Directory.CreateDirectory(root);
                        string targetDir = Path.Combine(root, meta.FolderName);
                        string cli = FindCliForRoot(root);
                        bool cliOk = !string.IsNullOrEmpty(cli) && RunCliInstall(cli, tempVsix);

                        if (!cliOk || !Directory.Exists(targetDir))
                        {
                            // Fallback when IDE CLI is unavailable or skipped registration: write versioned folder + update extensions.json.
                            WriteUnpackedFiles(targetDir, pkgJson, extJs, readmeMd, iconPng);
                            RegisterInExtensionsJson(root, meta, targetDir);
                        }
                        else
                        {
                            // Keep embedded extension files identical to current build even if CLI kept a previous copy.
                            WriteUnpackedFiles(targetDir, pkgJson, extJs, readmeMd, iconPng);
                        }

                        CleanupObsoleteFolders(root, meta);
                        successful.Add(targetDir);
                    }
                }
                finally
                {
                    try { if (File.Exists(tempVsix)) File.Delete(tempVsix); } catch { }
                }

                installedPaths = string.Join("\n", successful.ToArray());
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log.Write("companion.install", ex);
                return false;
            }
        }

        public static void SyncIfInstalled()
        {
            string pkgJson = ReadAsset("companion-package.json", Path.Combine("companion", "vscode-status-bar", "package.json"));
            string extJs = ReadAsset("companion-extension.js", Path.Combine("companion", "vscode-status-bar", "extension.js"));
            if (string.IsNullOrEmpty(pkgJson) || string.IsNullOrEmpty(extJs)) return;
            string readmeMd = ReadAsset("companion-readme.md", Path.Combine("companion", "vscode-status-bar", "README.md"));
            byte[] iconPng = ReadAssetBytes("companion-icon.png", Path.Combine("companion", "vscode-status-bar", "icon.png"));
            SyncInRoots(IdeRoots(), pkgJson, extJs, readmeMd, iconPng);
        }

        internal static void SyncInRoots(string[] roots, string pkgJson, string extJs)
        {
            SyncInRoots(roots, pkgJson, extJs, null, null);
        }

        internal static void SyncInRoots(string[] roots, string pkgJson, string extJs, string readmeMd, byte[] iconPng)
        {
            if (roots == null || string.IsNullOrEmpty(pkgJson) || string.IsNullOrEmpty(extJs)) return;
            try
            {
                ExtMeta meta = ParseMeta(pkgJson);
                Encoding utf8NoBom = new UTF8Encoding(false);
                Version ownVer;
                Version.TryParse(meta.Version, out ownVer);
                foreach (string root in roots)
                {
                    if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;

                    List<string> dirs = new List<string>();
                    try
                    {
                        string[] versioned = Directory.GetDirectories(root, meta.ExtensionId + "-*");
                        for (int i = 0; i < versioned.Length; i++)
                        {
                            string fn = Path.GetFileName(versioned[i]);
                            string verPart = fn.Length > meta.ExtensionId.Length + 1 ? fn.Substring(meta.ExtensionId.Length + 1) : "";
                            Version folderVer;
                            if (ownVer != null && Version.TryParse(verPart, out folderVer) && folderVer > ownVer)
                                continue; // Do not overwrite newer versions installed externally.
                            dirs.Add(versioned[i]);
                        }
                    }
                    catch { }

                    string legacy = Path.Combine(root, meta.Name);
                    if (Directory.Exists(legacy)) dirs.Add(legacy);

                    foreach (string dir in dirs)
                    {
                        SyncTextFile(Path.Combine(dir, "package.json"), pkgJson, utf8NoBom);
                        SyncTextFile(Path.Combine(dir, "extension.js"), extJs, utf8NoBom);
                        if (!string.IsNullOrEmpty(readmeMd))
                            SyncTextFile(Path.Combine(dir, "README.md"), readmeMd, utf8NoBom);
                        if (iconPng != null && iconPng.Length > 0)
                            SyncBinaryFile(Path.Combine(dir, "icon.png"), iconPng);
                    }
                }
            }
            catch { }
        }

        private static void SyncTextFile(string path, string content, Encoding enc)
        {
            try
            {
                if (!File.Exists(path) || File.ReadAllText(path, Encoding.UTF8) != content)
                    File.WriteAllText(path, content, enc);
            }
            catch { }
        }

        private static void SyncBinaryFile(string path, byte[] bytes)
        {
            try
            {
                if (File.Exists(path))
                {
                    byte[] cur = File.ReadAllBytes(path);
                    if (cur.Length == bytes.Length)
                    {
                        bool eq = true;
                        for (int i = 0; i < cur.Length; i++)
                        {
                            if (cur[i] != bytes[i]) { eq = false; break; }
                        }
                        if (eq) return;
                    }
                }
                File.WriteAllBytes(path, bytes);
            }
            catch { }
        }

        internal static bool InstallRootUnpacked(string root, string pkgJson, string extJs)
        {
            return InstallRootUnpacked(root, pkgJson, extJs, null, null);
        }

        internal static bool InstallRootUnpacked(string root, string pkgJson, string extJs, string readmeMd, byte[] iconPng)
        {
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(pkgJson) || string.IsNullOrEmpty(extJs)) return false;
            ExtMeta meta = ParseMeta(pkgJson);
            Directory.CreateDirectory(root);
            string targetDir = Path.Combine(root, meta.FolderName);
            WriteUnpackedFiles(targetDir, pkgJson, extJs, readmeMd, iconPng);
            bool registered = RegisterInExtensionsJson(root, meta, targetDir);
            CleanupObsoleteFolders(root, meta);
            return registered;
        }

        internal static byte[] BuildVsix(string pkgJson, string extJs)
        {
            return BuildVsix(pkgJson, extJs, null, null);
        }

        internal static byte[] BuildVsix(string pkgJson, string extJs, string readmeMd, byte[] iconPng)
        {
            return BuildVsix(ParseMeta(pkgJson), pkgJson, extJs, readmeMd, iconPng);
        }

        private sealed class ExtMeta
        {
            public string Name;
            public string Publisher;
            public string Version;
            public string DisplayName;
            public string Description;
            public string Engine;
            public string ExtensionId { get { return Publisher + "." + Name; } }
            public string FolderName { get { return ExtensionId + "-" + Version; } }
        }

        private static ExtMeta ParseMeta(string pkgJson)
        {
            ExtMeta m = new ExtMeta
            {
                Name = "ai-usage-watcher",
                Publisher = "silvkn1",
                Version = "1.2.2",
                DisplayName = "AI Usage Watcher Companion",
                Description = "Live status bar metrics for Claude Code, Antigravity, Codex CLI, Gemini CLI, OpenCode quotas/cost, and Ollama",
                Engine = "^1.70.0"
            };
            try
            {
                Dictionary<string, object> d = J.Parse(pkgJson);
                if (d != null)
                {
                    string v = J.Str(d, "name"); if (!string.IsNullOrEmpty(v)) m.Name = v;
                    v = J.Str(d, "publisher"); if (!string.IsNullOrEmpty(v)) m.Publisher = v;
                    v = J.Str(d, "version"); if (!string.IsNullOrEmpty(v)) m.Version = v;
                    v = J.Str(d, "displayName"); if (!string.IsNullOrEmpty(v)) m.DisplayName = v;
                    v = J.Str(d, "description"); if (!string.IsNullOrEmpty(v)) m.Description = v;
                    Dictionary<string, object> eng = J.Obj(d, "engines");
                    if (eng != null)
                    {
                        v = J.Str(eng, "vscode");
                        if (!string.IsNullOrEmpty(v)) m.Engine = v;
                    }
                }
            }
            catch { }
            return m;
        }

        private static List<string> DetectTargetRoots()
        {
            List<string> targets = new List<string>();
            string[] roots = IdeRoots();
            for (int i = 0; i < roots.Length; i++)
            {
                if (Directory.Exists(roots[i])) targets.Add(roots[i]);
            }
            if (targets.Count == 0) targets.Add(roots[0]);
            return targets;
        }

        private static void WriteUnpackedFiles(string dir, string pkgJson, string extJs, string readmeMd, byte[] iconPng)
        {
            Directory.CreateDirectory(dir);
            Encoding utf8NoBom = new UTF8Encoding(false);
            File.WriteAllText(Path.Combine(dir, "package.json"), pkgJson, utf8NoBom);
            File.WriteAllText(Path.Combine(dir, "extension.js"), extJs, utf8NoBom);
            if (!string.IsNullOrEmpty(readmeMd))
                File.WriteAllText(Path.Combine(dir, "README.md"), readmeMd, utf8NoBom);
            if (iconPng != null && iconPng.Length > 0)
                File.WriteAllBytes(Path.Combine(dir, "icon.png"), iconPng);
        }

        private static byte[] BuildVsix(ExtMeta meta, string pkgJson, string extJs, string readmeMd, byte[] iconPng)
        {
            bool hasReadme = !string.IsNullOrEmpty(readmeMd);
            bool hasIcon = iconPng != null && iconPng.Length > 0;

            const string contentTypes =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">\n" +
                "  <Default Extension=\"vsixmanifest\" ContentType=\"text/xml\" />\n" +
                "  <Default Extension=\"json\" ContentType=\"application/json\" />\n" +
                "  <Default Extension=\"js\" ContentType=\"text/javascript\" />\n" +
                "  <Default Extension=\"md\" ContentType=\"text/markdown\" />\n" +
                "  <Default Extension=\"png\" ContentType=\"image/png\" />\n" +
                "</Types>\n";

            StringBuilder man = new StringBuilder();
            man.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
            man.Append("<PackageManifest Version=\"2.0.0\" xmlns=\"http://schemas.microsoft.com/developer/vsx-schema/2011\" xmlns:d=\"http://schemas.microsoft.com/developer/vsx-schema-design/2011\">\n");
            man.Append("  <Metadata>\n");
            man.Append("    <Identity Language=\"en-US\" Id=\"").Append(SecurityElement.Escape(meta.Name)).Append("\" Version=\"").Append(SecurityElement.Escape(meta.Version)).Append("\" Publisher=\"").Append(SecurityElement.Escape(meta.Publisher)).Append("\" />\n");
            man.Append("    <DisplayName>").Append(SecurityElement.Escape(meta.DisplayName)).Append("</DisplayName>\n");
            man.Append("    <Description xml:space=\"preserve\">").Append(SecurityElement.Escape(meta.Description)).Append("</Description>\n");
            man.Append("    <Categories>Other</Categories>\n");
            if (hasIcon) man.Append("    <Icon>extension/icon.png</Icon>\n");
            man.Append("    <Properties>\n");
            man.Append("      <Property Id=\"Microsoft.VisualStudio.Code.Engine\" Value=\"").Append(SecurityElement.Escape(meta.Engine)).Append("\" />\n");
            man.Append("    </Properties>\n");
            man.Append("  </Metadata>\n");
            man.Append("  <Installation>\n");
            man.Append("    <InstallationTarget Id=\"Microsoft.VisualStudio.Code\" />\n");
            man.Append("  </Installation>\n");
            man.Append("  <Dependencies />\n");
            man.Append("  <Assets>\n");
            man.Append("    <Asset Type=\"Microsoft.VisualStudio.Code.Manifest\" Path=\"extension/package.json\" Addressable=\"true\" />\n");
            if (hasReadme) man.Append("    <Asset Type=\"Microsoft.VisualStudio.Services.Content.Details\" Path=\"extension/README.md\" Addressable=\"true\" />\n");
            if (hasIcon) man.Append("    <Asset Type=\"Microsoft.VisualStudio.Services.Icons.Default\" Path=\"extension/icon.png\" Addressable=\"true\" />\n");
            man.Append("  </Assets>\n");
            man.Append("</PackageManifest>\n");

            Encoding utf8NoBom = new UTF8Encoding(false);
            using (MemoryStream ms = new MemoryStream())
            {
                using (ZipArchive zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    AddZipBytes(zip, "[Content_Types].xml", utf8NoBom.GetBytes(contentTypes));
                    AddZipBytes(zip, "extension.vsixmanifest", utf8NoBom.GetBytes(man.ToString()));
                    AddZipBytes(zip, "extension/package.json", utf8NoBom.GetBytes(pkgJson));
                    AddZipBytes(zip, "extension/extension.js", utf8NoBom.GetBytes(extJs));
                    if (hasReadme) AddZipBytes(zip, "extension/README.md", utf8NoBom.GetBytes(readmeMd));
                    if (hasIcon) AddZipBytes(zip, "extension/icon.png", iconPng);
                }
                return ms.ToArray();
            }
        }

        private static void AddZipBytes(ZipArchive zip, string entryName, byte[] bytes)
        {
            ZipArchiveEntry entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using (Stream es = entry.Open())
            {
                es.Write(bytes, 0, bytes.Length);
            }
        }

        private static string FindCliForRoot(string root)
        {
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string lower = (root ?? "").ToLowerInvariant();

            List<string> candidates = new List<string>();
            if (lower.Contains(".cursor"))
            {
                candidates.Add(Path.Combine(localApp, "Programs", "cursor", "resources", "app", "bin", "cursor.cmd"));
                candidates.Add(Path.Combine(pf, "cursor", "resources", "app", "bin", "cursor.cmd"));
                FindOnPath("cursor.cmd", candidates);
            }
            else if (lower.Contains(".antigravity"))
            {
                candidates.Add(Path.Combine(localApp, "Programs", "Antigravity", "bin", "antigravity.cmd"));
                candidates.Add(Path.Combine(pf, "Antigravity", "bin", "antigravity.cmd"));
                FindOnPath("antigravity.cmd", candidates);
            }
            else if (lower.Contains(".windsurf"))
            {
                candidates.Add(Path.Combine(localApp, "Programs", "Windsurf", "bin", "windsurf.cmd"));
                candidates.Add(Path.Combine(pf, "Windsurf", "bin", "windsurf.cmd"));
                FindOnPath("windsurf.cmd", candidates);
            }
            else
            {
                candidates.Add(Path.Combine(localApp, "Programs", "Microsoft VS Code", "bin", "code.cmd"));
                candidates.Add(Path.Combine(pf, "Microsoft VS Code", "bin", "code.cmd"));
                candidates.Add(Path.Combine(pf86, "Microsoft VS Code", "bin", "code.cmd"));
                FindOnPath("code.cmd", candidates);
            }

            foreach (string c in candidates)
            {
                if (!string.IsNullOrEmpty(c) && File.Exists(c)) return c;
            }
            return null;
        }

        private static void FindOnPath(string exeName, List<string> list)
        {
            try
            {
                string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
                string[] parts = pathEnv.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < parts.Length; i++)
                {
                    string p = parts[i].Trim().Trim('"');
                    if (!ProcessHelper.IsSafeSearchDir(p)) continue;
                    list.Add(Path.Combine(p, exeName));
                }
            }
            catch { }
        }

        private static bool RunCliInstall(string cliPath, string vsixPath)
        {
            try
            {
                if (ProcessHelper.HasCmdMeta(cliPath) || ProcessHelper.HasCmdMeta(vsixPath))
                    return false;
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = ProcessHelper.SystemExe("cmd.exe"),
                    Arguments = "/d /c \"\"" + cliPath + "\" --install-extension \"" + vsixPath + "\" --force\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (Process proc = Process.Start(psi))
                {
                    if (proc == null) return false;
                    // Drain async streams before waiting to prevent buffer deadlock.
                    proc.OutputDataReceived += delegate { };
                    proc.ErrorDataReceived += delegate { };
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();
                    if (!proc.WaitForExit(20000))
                    {
                        ProcessHelper.KillTree(proc);
                        return false;
                    }
                    return proc.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                Log.Write("companion.cli", ex);
                return false;
            }
        }

        private static bool RegisterInExtensionsJson(string root, ExtMeta meta, string targetDir)
        {
            try
            {
                string ejPath = Path.Combine(root, "extensions.json");
                List<object> entries = new List<object>();

                if (File.Exists(ejPath))
                {
                    string raw = File.ReadAllText(ejPath, Encoding.UTF8).Trim();
                    if (!string.IsNullOrEmpty(raw))
                    {
                        object parsed = J.Ser.DeserializeObject(raw);
                        object[] arr = parsed as object[];
                        if (arr == null)
                        {
                            ArrayList al = parsed as ArrayList;
                            if (al != null) arr = al.ToArray();
                        }
                        if (arr == null)
                        {
                            Log.Write("companion.ej.root");
                            return false;
                        }
                        for (int i = 0; i < arr.Length; i++)
                        {
                            Dictionary<string, object> item = arr[i] as Dictionary<string, object>;
                            if (item != null && IsOwnEntry(item, meta)) continue;
                            entries.Add(arr[i]);
                        }
                    }
                }

                string full = Path.GetFullPath(targetDir);
                string unixSlash = full.Replace('\\', '/');
                if (unixSlash.Length >= 2 && unixSlash[1] == ':')
                    unixSlash = char.ToLowerInvariant(unixSlash[0]) + unixSlash.Substring(1);
                string uriPath = unixSlash.StartsWith("/") ? unixSlash : "/" + unixSlash;

                Dictionary<string, object> identifier = new Dictionary<string, object>();
                identifier["id"] = meta.ExtensionId;

                Dictionary<string, object> location = new Dictionary<string, object>();
                location["$mid"] = 1;
                location["fsPath"] = full;
                location["external"] = "file://" + uriPath;
                location["path"] = uriPath;
                location["scheme"] = "file";

                Dictionary<string, object> metadata = new Dictionary<string, object>();
                long nowMs = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
                metadata["installedTimestamp"] = nowMs;
                metadata["source"] = "vsix";

                Dictionary<string, object> entry = new Dictionary<string, object>();
                entry["identifier"] = identifier;
                entry["version"] = meta.Version;
                entry["location"] = location;
                entry["relativeLocation"] = meta.FolderName;
                entry["metadata"] = metadata;

                entries.Add(entry);

                string serialized = J.Ser.Serialize(entries);
                AtomicFile.Write(ejPath, serialized);
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("companion.ej", ex);
                return false;
            }
        }

        private static bool IsOwnEntry(Dictionary<string, object> item, ExtMeta meta)
        {
            Dictionary<string, object> ident = J.Obj(item, "identifier");
            string id = ident != null ? J.Str(ident, "id") : null;
            if (!string.IsNullOrEmpty(id) && string.Equals(id, meta.ExtensionId, StringComparison.OrdinalIgnoreCase))
                return true;
            string rel = J.Str(item, "relativeLocation");
            if (!string.IsNullOrEmpty(rel) &&
                (string.Equals(rel, meta.Name, StringComparison.OrdinalIgnoreCase) ||
                 rel.StartsWith(meta.ExtensionId + "-", StringComparison.OrdinalIgnoreCase)))
                return true;
            return false;
        }

        private static void CleanupObsoleteFolders(string root, ExtMeta meta)
        {
            try
            {
                string legacy = Path.Combine(root, meta.Name);
                if (Directory.Exists(legacy)) Directory.Delete(legacy, true);

                string[] oldVersioned = Directory.GetDirectories(root, meta.ExtensionId + "-*");
                for (int i = 0; i < oldVersioned.Length; i++)
                {
                    if (!string.Equals(Path.GetFileName(oldVersioned[i]), meta.FolderName, StringComparison.OrdinalIgnoreCase))
                        Directory.Delete(oldVersioned[i], true);
                }
            }
            catch { }
        }

        private static string ReadAsset(string resourceName, string relativeDiskPath)
        {
            byte[] data = ReadAssetBytes(resourceName, relativeDiskPath);
            return data == null ? null : Encoding.UTF8.GetString(data);
        }

        private static byte[] ReadAssetBytes(string resourceName, string relativeDiskPath)
        {
            try
            {
                using (Stream s = typeof(CompanionInstaller).Assembly.GetManifestResourceStream(resourceName))
                {
                    if (s != null)
                    {
                        using (MemoryStream ms = new MemoryStream())
                        {
                            s.CopyTo(ms);
                            return ms.ToArray();
                        }
                    }
                }
            }
            catch { }

            string diskPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativeDiskPath);
            if (File.Exists(diskPath))
            {
                try { return File.ReadAllBytes(diskPath); } catch { }
            }
            return null;
        }
    }
}
