using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace ClaudeUsageWidget
{
    public class ReleaseInfo
    {
        public string TagName { get; set; }
        public Version Version { get; set; }
        public string Title { get; set; }
        public string Body { get; set; }
        public string HtmlUrl { get; set; }
        public string AssetDownloadUrl { get; set; }
        public string AssetName { get; set; }
        public long AssetSize { get; set; }
        public bool IsNewer { get; set; }
    }

    public static class AutoUpdater
    {
        public const string DefaultRepo = "silvkn1/ai-usage-watcher";

        public static Version CurrentVersion
        {
            get
            {
                try
                {
                    Version v = typeof(AutoUpdater).Assembly.GetName().Version;
                    if (v != null) return v;
                }
                catch { }
                return new Version(1, 0, 0, 0);
            }
        }

        public static string CurrentVersionStr
        {
            get
            {
                Version v = CurrentVersion;
                return string.Format("{0}.{1}.{2}", v.Major, v.Minor, Math.Max(0, v.Build));
            }
        }

        public static Version ParseVersion(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return null;
            string clean = tag.Trim().TrimStart('v', 'V');
            Match m = Regex.Match(clean, @"^(\d+)\.(\d+)(?:\.(\d+))?(?:\.(\d+))?");
            if (!m.Success) return null;
            int maj = int.Parse(m.Groups[1].Value);
            int min = int.Parse(m.Groups[2].Value);
            int build = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0;
            int rev = m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : 0;
            return new Version(maj, min, build, rev);
        }

        public static ReleaseInfo ParseReleaseJson(string json, Version currentVersion = null)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                JavaScriptSerializer ser = new JavaScriptSerializer();
                Dictionary<string, object> dict = ser.Deserialize<Dictionary<string, object>>(json);
                if (dict == null) return null;

                ReleaseInfo info = new ReleaseInfo();
                info.TagName = dict.ContainsKey("tag_name") && dict["tag_name"] != null ? dict["tag_name"].ToString() : "";
                info.Title = dict.ContainsKey("name") && dict["name"] != null ? dict["name"].ToString() : info.TagName;
                info.Body = dict.ContainsKey("body") && dict["body"] != null ? dict["body"].ToString() : "";
                info.HtmlUrl = dict.ContainsKey("html_url") && dict["html_url"] != null ? dict["html_url"].ToString() : "";

                info.Version = ParseVersion(info.TagName);
                if (info.Version == null) return null;

                Version cur = currentVersion ?? CurrentVersion;
                info.IsNewer = (info.Version > cur);

                if (dict.ContainsKey("assets") && dict["assets"] is ArrayList)
                {
                    ArrayList assets = (ArrayList)dict["assets"];
                    Dictionary<string, object> chosenAsset = null;

                    foreach (object item in assets)
                    {
                        if (item is Dictionary<string, object>)
                        {
                            Dictionary<string, object> a = (Dictionary<string, object>)item;
                            string name = a.ContainsKey("name") && a["name"] != null ? a["name"].ToString() : "";
                            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            {
                                chosenAsset = a;
                                break;
                            }
                            if (chosenAsset == null && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            {
                                chosenAsset = a;
                            }
                        }
                    }

                    if (chosenAsset != null)
                    {
                        info.AssetName = chosenAsset.ContainsKey("name") && chosenAsset["name"] != null ? chosenAsset["name"].ToString() : "";
                        info.AssetDownloadUrl = chosenAsset.ContainsKey("browser_download_url") && chosenAsset["browser_download_url"] != null
                            ? chosenAsset["browser_download_url"].ToString() : "";
                        if (chosenAsset.ContainsKey("size"))
                        {
                            try { info.AssetSize = Convert.ToInt64(chosenAsset["size"]); } catch { }
                        }
                    }
                }

                if (string.IsNullOrEmpty(info.AssetDownloadUrl))
                {
                    if (dict.ContainsKey("zipball_url") && dict["zipball_url"] != null)
                        info.AssetDownloadUrl = dict["zipball_url"].ToString();
                    else
                        info.AssetDownloadUrl = info.HtmlUrl;
                }

                return info;
            }
            catch (Exception ex)
            {
                Log.Write("updater.parse", ex);
                return null;
            }
        }

        public static ReleaseInfo CheckForUpdates(string repo = DefaultRepo)
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                string url = "https://api.github.com/repos/" + (repo ?? DefaultRepo) + "/releases/latest";
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(15);
                    client.DefaultRequestHeaders.Add("User-Agent", "AIUsageWatcher-AutoUpdater");
                    client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");

                    HttpResponseMessage resp = client.GetAsync(url).Result;
                    if (!resp.IsSuccessStatusCode) return null;

                    string json = resp.Content.ReadAsStringAsync().Result;
                    return ParseReleaseJson(json);
                }
            }
            catch (Exception ex)
            {
                Log.Write("updater.check", ex);
                return null;
            }
        }

        public static void CheckForUpdatesAsync(Action<ReleaseInfo> onCompleted, string repo = DefaultRepo)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                ReleaseInfo info = CheckForUpdates(repo);
                if (onCompleted != null)
                {
                    try { onCompleted(info); } catch { }
                }
            });
        }

        public static bool DownloadAndApplyUpdate(ReleaseInfo release, string targetExePath = null, Action<int, string> onProgress = null)
        {
            if (release == null || string.IsNullOrEmpty(release.AssetDownloadUrl))
                throw new ArgumentException("Release asset download URL is required.");

            string target = targetExePath;
            if (string.IsNullOrEmpty(target))
            {
                try { target = typeof(AutoUpdater).Assembly.Location; } catch { }
                if (string.IsNullOrEmpty(target) || !File.Exists(target))
                {
                    using (Process p = Process.GetCurrentProcess())
                    {
                        target = p.MainModule.FileName;
                    }
                }
            }

            if (string.IsNullOrEmpty(target) || !File.Exists(target))
                throw new FileNotFoundException("Cannot locate target executable path to update: " + target);

            string targetDir = Path.GetDirectoryName(target);
            string tempFile = Path.Combine(targetDir, "update-" + Guid.NewGuid().ToString("N") + ".tmp");
            string downloadFile = tempFile;
            string exeToInstall = null;

            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                if (onProgress != null) onProgress(10, L.T("update.downloading", release.AssetName ?? "update"));

                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(3);
                    client.DefaultRequestHeaders.Add("User-Agent", "AIUsageWatcher-AutoUpdater");

                    using (HttpResponseMessage resp = client.GetAsync(release.AssetDownloadUrl, HttpCompletionOption.ResponseHeadersRead).Result)
                    {
                        resp.EnsureSuccessStatusCode();
                        long totalBytes = resp.Content.Headers.ContentLength ?? -1;

                        using (Stream input = resp.Content.ReadAsStreamAsync().Result)
                        using (FileStream output = new FileStream(downloadFile, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            byte[] buffer = new byte[8192];
                            int read;
                            long totalRead = 0;
                            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                output.Write(buffer, 0, read);
                                totalRead += read;
                                if (totalBytes > 0 && onProgress != null)
                                {
                                    int pct = 10 + (int)(totalRead * 70 / totalBytes);
                                    onProgress(pct, L.T("update.downloading", string.Format("{0}%", pct)));
                                }
                            }
                        }
                    }
                }

                exeToInstall = downloadFile;
                if (release.AssetName != null && release.AssetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    if (onProgress != null) onProgress(85, "Extracting update...");
                    string extractedExe = Path.Combine(targetDir, "update-extracted-" + Guid.NewGuid().ToString("N") + ".exe");
                    using (ZipArchive zip = ZipFile.OpenRead(downloadFile))
                    {
                        ZipArchiveEntry entry = null;
                        foreach (ZipArchiveEntry e in zip.Entries)
                        {
                            if (e.Name.Equals("AIUsageWatcher.exe", StringComparison.OrdinalIgnoreCase) ||
                                e.Name.Equals(Path.GetFileName(target), StringComparison.OrdinalIgnoreCase))
                            {
                                entry = e;
                                break;
                            }
                        }
                        if (entry == null)
                            throw new Exception("ZIP archive does not contain target executable.");
                        entry.ExtractToFile(extractedExe, true);
                    }
                    try { File.Delete(downloadFile); } catch { }
                    exeToInstall = extractedExe;
                }

                if (!IsValidExecutable(exeToInstall))
                    throw new InvalidDataException("Downloaded file is not a valid Windows executable.");

                if (onProgress != null) onProgress(95, "Applying update...");

                string targetBackup = target + ".old";
                if (File.Exists(targetBackup))
                {
                    try { File.Delete(targetBackup); }
                    catch
                    {
                        try
                        {
                            string uniqueAside = target + ".old-" + Guid.NewGuid().ToString("N");
                            File.Move(targetBackup, uniqueAside);
                        }
                        catch { }
                    }
                }

                if (File.Exists(targetBackup))
                {
                    targetBackup = target + ".old-" + Guid.NewGuid().ToString("N");
                }

                File.Move(target, targetBackup);
                try
                {
                    File.Move(exeToInstall, target);
                }
                catch
                {
                    try { if (File.Exists(targetBackup)) File.Move(targetBackup, target); } catch { }
                    throw;
                }

                if (onProgress != null) onProgress(100, "Restarting...");

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = target;
                psi.WorkingDirectory = targetDir;
                psi.UseShellExecute = true;
                Process.Start(psi);

                return true;
            }
            catch
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
                if (!string.IsNullOrEmpty(exeToInstall) && exeToInstall != tempFile)
                {
                    try { if (File.Exists(exeToInstall)) File.Delete(exeToInstall); } catch { }
                }
                throw;
            }
        }

        public static bool IsValidExecutable(string path)
        {
            if (!File.Exists(path)) return false;
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (fs.Length < 10240) return false;
                    byte[] header = new byte[2];
                    if (fs.Read(header, 0, 2) < 2) return false;
                    return (header[0] == 0x4D && header[1] == 0x5A);
                }
            }
            catch
            {
                return false;
            }
        }

        public static void CleanupOldVersion(string exePath = null)
        {
            try
            {
                string target = exePath;
                if (string.IsNullOrEmpty(target))
                {
                    try { target = typeof(AutoUpdater).Assembly.Location; } catch { }
                    if (string.IsNullOrEmpty(target))
                    {
                        try
                        {
                            using (Process p = Process.GetCurrentProcess())
                            {
                                target = p.MainModule.FileName;
                            }
                        }
                        catch { }
                    }
                }
                if (string.IsNullOrEmpty(target)) return;

                string dir = Path.GetDirectoryName(target);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

                string baseName = Path.GetFileName(target);

                string oldExe = target + ".old";
                if (File.Exists(oldExe))
                {
                    try { File.Delete(oldExe); } catch { }
                }

                string[] oldFiles = Directory.GetFiles(dir, baseName + ".old*");
                foreach (string f in oldFiles)
                {
                    try { File.Delete(f); } catch { }
                }

                string[] tmpFiles = Directory.GetFiles(dir, "update-*.tmp");
                foreach (string f in tmpFiles)
                {
                    try { File.Delete(f); } catch { }
                }
            }
            catch (Exception ex)
            {
                Log.Write("updater.cleanup", ex);
            }
        }
    }
}
