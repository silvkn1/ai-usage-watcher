using System;
using System.Collections.Generic;
using System.IO;
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
                List<string> targets = new List<string>();
                if (!string.IsNullOrEmpty(customDir))
                {
                    targets.Add(customDir);
                }
                else
                {
                    string[] roots = IdeRoots();
                    for (int i = 0; i < roots.Length; i++)
                    {
                        if (Directory.Exists(roots[i]))
                            targets.Add(Path.Combine(roots[i], "ai-usage-watcher"));
                    }
                    if (targets.Count == 0)
                        targets.Add(Path.Combine(roots[0], "ai-usage-watcher"));
                }

                string pkgJson = ReadAsset("companion-package.json", Path.Combine("companion", "vscode-status-bar", "package.json"));
                string extJs = ReadAsset("companion-extension.js", Path.Combine("companion", "vscode-status-bar", "extension.js"));
                if (string.IsNullOrEmpty(pkgJson) || string.IsNullOrEmpty(extJs))
                {
                    error = "Companion extension assets not found.";
                    return false;
                }

                Encoding utf8NoBom = new UTF8Encoding(false);
                List<string> successful = new List<string>();
                foreach (string target in targets)
                {
                    Directory.CreateDirectory(target);
                    File.WriteAllText(Path.Combine(target, "package.json"), pkgJson, utf8NoBom);
                    File.WriteAllText(Path.Combine(target, "extension.js"), extJs, utf8NoBom);
                    successful.Add(target);
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
            try
            {
                string pkgJson = ReadAsset("companion-package.json", Path.Combine("companion", "vscode-status-bar", "package.json"));
                string extJs = ReadAsset("companion-extension.js", Path.Combine("companion", "vscode-status-bar", "extension.js"));
                if (string.IsNullOrEmpty(pkgJson) || string.IsNullOrEmpty(extJs)) return;

                string[] roots = IdeRoots();
                Encoding utf8NoBom = new UTF8Encoding(false);
                foreach (string root in roots)
                {
                    string dir = Path.Combine(root, "ai-usage-watcher");
                    if (!Directory.Exists(dir)) continue;
                    string pkgPath = Path.Combine(dir, "package.json");
                    string extPath = Path.Combine(dir, "extension.js");
                    try
                    {
                        if (!File.Exists(pkgPath) || File.ReadAllText(pkgPath, Encoding.UTF8) != pkgJson)
                            File.WriteAllText(pkgPath, pkgJson, utf8NoBom);
                        if (!File.Exists(extPath) || File.ReadAllText(extPath, Encoding.UTF8) != extJs)
                            File.WriteAllText(extPath, extJs, utf8NoBom);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static string ReadAsset(string resourceName, string relativeDiskPath)
        {
            try
            {
                using (Stream s = typeof(CompanionInstaller).Assembly.GetManifestResourceStream(resourceName))
                {
                    if (s != null)
                    {
                        using (StreamReader sr = new StreamReader(s, Encoding.UTF8))
                            return sr.ReadToEnd();
                    }
                }
            }
            catch { }

            string diskPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativeDiskPath);
            if (File.Exists(diskPath))
            {
                try { return File.ReadAllText(diskPath, Encoding.UTF8); } catch { }
            }
            return null;
        }
    }
}
