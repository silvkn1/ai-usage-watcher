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
                    string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    string vscodeExtDir = Path.Combine(userProfile, ".vscode", "extensions");
                    string cursorExtDir = Path.Combine(userProfile, ".cursor", "extensions");

                    if (Directory.Exists(vscodeExtDir))
                        targets.Add(Path.Combine(vscodeExtDir, "ai-usage-watcher"));
                    if (Directory.Exists(cursorExtDir))
                        targets.Add(Path.Combine(cursorExtDir, "ai-usage-watcher"));

                    if (targets.Count == 0)
                    {
                        targets.Add(Path.Combine(vscodeExtDir, "ai-usage-watcher"));
                    }
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
