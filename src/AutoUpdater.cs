using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
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
        public string ExpectedSha256 { get; set; }
        public string ChecksumsUrl { get; set; }
        public bool IsNewer { get; set; }
    }

    public static class AutoUpdater
    {
        public const string DefaultRepo = "silvkn1/ai-usage-watcher";
        private static readonly HttpClient Http = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            // Disable automatic redirects so every hop is validated against IsTrustedUpdateUrl before connecting.
            HttpClientHandler handler = new HttpClientHandler { AllowAutoRedirect = false };
            HttpClient c = new HttpClient(handler);
            c.Timeout = TimeSpan.FromMinutes(3);
            c.DefaultRequestHeaders.Add("User-Agent", "AIUsageWatcher-AutoUpdater");
            return c;
        }

        internal static string ResolveTrustedRedirectUrl(Uri currentUri, Uri location)
        {
            if (location == null) return null;
            Uri next;
            if (location.IsAbsoluteUri) next = location;
            else if (currentUri != null) next = new Uri(currentUri, location);
            else return null;
            return IsTrustedUpdateUrl(next.AbsoluteUri) ? next.AbsoluteUri : null;
        }

        private static HttpResponseMessage SendWithTrustedRedirects(string url, HttpCompletionOption option, CancellationToken ct, string acceptHeader = null, int maxHops = 5)
        {
            string cur = url;
            for (int hop = 0; hop <= maxHops; hop++)
            {
                if (!IsTrustedUpdateUrl(cur))
                    throw new InvalidDataException("Redirected update URL is untrusted or non-HTTPS.");
                HttpResponseMessage resp;
                using (HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, cur))
                {
                    if (!string.IsNullOrEmpty(acceptHeader))
                        req.Headers.Add("Accept", acceptHeader);
                    resp = Http.SendAsync(req, option, ct).Result;
                    int code = (int)resp.StatusCode;
                    if (code >= 300 && code < 400 && resp.Headers.Location != null)
                    {
                        string nextUrl = ResolveTrustedRedirectUrl(req.RequestUri, resp.Headers.Location);
                        resp.Dispose();
                        if (nextUrl == null)
                            throw new InvalidDataException("Redirected update URL is untrusted or non-HTTPS.");
                        cur = nextUrl;
                        continue;
                    }
                    return resp;
                }
            }
            throw new InvalidDataException("Too many HTTP redirects during update request.");
        }

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
            int maj, min, build = 0, rev = 0;
            if (!int.TryParse(m.Groups[1].Value, out maj) || !int.TryParse(m.Groups[2].Value, out min))
                return null;
            if (m.Groups[3].Success && !int.TryParse(m.Groups[3].Value, out build))
                return null;
            if (m.Groups[4].Success && !int.TryParse(m.Groups[4].Value, out rev))
                return null;
            return new Version(maj, min, build, rev);
        }

        public static bool IsValidHttpsUrl(string url)
        {
            Uri uri;
            return !string.IsNullOrWhiteSpace(url) &&
                   Uri.TryCreate(url.Trim(), UriKind.Absolute, out uri) &&
                   uri.Scheme == Uri.UriSchemeHttps &&
                   string.IsNullOrEmpty(uri.UserInfo);
        }

        public static bool IsTrustedUpdateUrl(string url)
        {
            Uri uri;
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out uri))
                return false;
            if (uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
                return false;
            string host = uri.DnsSafeHost ?? "";
            return HostEqualsOrSubdomain(host, "github.com") ||
                   HostEqualsOrSubdomain(host, "githubusercontent.com");
        }

        private static bool HostEqualsOrSubdomain(string host, string domain)
        {
            return string.Equals(host, domain, StringComparison.OrdinalIgnoreCase) ||
                   host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsValidSha256Hex(string sha)
        {
            if (string.IsNullOrEmpty(sha) || sha.Length != 64) return false;
            for (int i = 0; i < sha.Length; i++)
            {
                char c = sha[i];
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
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
                string htmlUrl = dict.ContainsKey("html_url") && dict["html_url"] != null ? dict["html_url"].ToString() : "";
                info.HtmlUrl = IsValidHttpsUrl(htmlUrl) ? htmlUrl.Trim() : "";

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
                            if (string.Equals(name, "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase) &&
                                a.ContainsKey("browser_download_url") && a["browser_download_url"] != null)
                            {
                                string cUrl = a["browser_download_url"].ToString();
                                if (IsValidHttpsUrl(cUrl)) info.ChecksumsUrl = cUrl.Trim();
                                continue;
                            }
                            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            {
                                chosenAsset = a;
                            }
                            else if (chosenAsset == null && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            {
                                chosenAsset = a;
                            }
                        }
                    }

                    if (chosenAsset != null)
                    {
                        info.AssetName = chosenAsset.ContainsKey("name") && chosenAsset["name"] != null ? chosenAsset["name"].ToString() : "";
                        string dlUrl = chosenAsset.ContainsKey("browser_download_url") && chosenAsset["browser_download_url"] != null
                            ? chosenAsset["browser_download_url"].ToString() : "";
                        info.AssetDownloadUrl = IsValidHttpsUrl(dlUrl) ? dlUrl.Trim() : "";
                        if (chosenAsset.ContainsKey("size"))
                        {
                            try { info.AssetSize = Convert.ToInt64(chosenAsset["size"]); } catch { }
                        }
                        if (chosenAsset.ContainsKey("digest") && chosenAsset["digest"] != null)
                        {
                            string dig = chosenAsset["digest"].ToString().Trim();
                            if (dig.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                                info.ExpectedSha256 = dig.Substring(7).Trim().ToLowerInvariant();
                        }
                    }
                }

                if (string.IsNullOrEmpty(info.AssetDownloadUrl))
                {
                    string zipUrl = dict.ContainsKey("zipball_url") && dict["zipball_url"] != null ? dict["zipball_url"].ToString() : "";
                    if (IsValidHttpsUrl(zipUrl))
                        info.AssetDownloadUrl = zipUrl.Trim();
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
                string useRepo = (repo ?? DefaultRepo).Trim();
                if (!Regex.IsMatch(useRepo, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$"))
                    return null;
                string url = "https://api.github.com/repos/" + useRepo + "/releases/latest";
                using (CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                using (HttpResponseMessage resp = SendWithTrustedRedirects(url, HttpCompletionOption.ResponseContentRead, cts.Token, "application/vnd.github.v3+json"))
                {
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

        internal static string ExtractUpdateZip(string zipPath, string targetExePath, string targetDir)
        {
            string extractedExe = Path.Combine(targetDir, "update-extracted-" + Guid.NewGuid().ToString("N") + ".exe");
            string fullTargetDir = Path.GetFullPath(targetDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string targetExeName = Path.GetFileName(targetExePath);
            try
            {
                using (ZipArchive zip = ZipFile.OpenRead(zipPath))
                {
                    ZipArchiveEntry exeEntry = null;
                    List<KeyValuePair<ZipArchiveEntry, string>> auxEntries = new List<KeyValuePair<ZipArchiveEntry, string>>();
                    foreach (ZipArchiveEntry e in zip.Entries)
                    {
                        if (string.IsNullOrEmpty(e.Name)) continue; // directory entry
                        string relDest = e.FullName.Replace('/', Path.DirectorySeparatorChar);
                        string destPath = Path.GetFullPath(Path.Combine(fullTargetDir, relDest));
                        if (!destPath.StartsWith(fullTargetDir, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("Archive entry attempts path traversal: " + e.FullName);

                        string normName = e.FullName.TrimStart('/', '\\');
                        if (string.Equals(normName, "AIUsageWatcher.exe", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(normName, targetExeName, StringComparison.OrdinalIgnoreCase))
                        {
                            exeEntry = e;
                        }
                        else
                        {
                            auxEntries.Add(new KeyValuePair<ZipArchiveEntry, string>(e, destPath));
                        }
                    }
                    if (exeEntry == null)
                        throw new InvalidDataException("ZIP archive does not contain target executable.");

                    // Validate extracted executable BEFORE writing any auxiliary files into targetDir.
                    exeEntry.ExtractToFile(extractedExe, true);
                    if (!IsValidExecutable(extractedExe))
                        throw new InvalidDataException("Downloaded file is not a valid Windows executable.");

                    for (int i = 0; i < auxEntries.Count; i++)
                    {
                        string dest = auxEntries[i].Value;
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        auxEntries[i].Key.ExtractToFile(dest, true);
                    }
                }
                return extractedExe;
            }
            catch
            {
                try { if (File.Exists(extractedExe)) File.Delete(extractedExe); } catch { }
                throw;
            }
        }

        public static bool DownloadAndApplyUpdate(ReleaseInfo release, string targetExePath = null, Action<int, string> onProgress = null)
        {
            if (release == null || string.IsNullOrEmpty(release.AssetDownloadUrl))
                throw new ArgumentException("Release asset download URL is required.");
            if (!IsTrustedUpdateUrl(release.AssetDownloadUrl))
                throw new InvalidDataException("Untrusted update download host or non-HTTPS URL blocked.");

            string expectedSha = release.ExpectedSha256;
            if (string.IsNullOrEmpty(expectedSha) && !string.IsNullOrEmpty(release.ChecksumsUrl) && !string.IsNullOrEmpty(release.AssetName))
            {
                if (!IsTrustedUpdateUrl(release.ChecksumsUrl))
                    throw new InvalidDataException("Untrusted checksums URL host or non-HTTPS URL blocked.");
                expectedSha = FetchExpectedSha256(release.ChecksumsUrl, release.AssetName);
            }
            if (!IsValidSha256Hex(expectedSha))
                throw new InvalidDataException("Update aborted: valid SHA-256 digest or SHA256SUMS.txt is required.");

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
                if (onProgress != null) onProgress(10, L.T("update.downloading", release.AssetName ?? "update"));

                using (HttpResponseMessage resp = SendWithTrustedRedirects(release.AssetDownloadUrl, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None))
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

                // Fail-closed SHA-256 checksum verification before extracting or launching
                string actualSha = ComputeFileSha256(downloadFile);
                if (!string.Equals(actualSha, expectedSha, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Downloaded update checksum mismatch (expected " + expectedSha + ", got " + actualSha + ").");

                exeToInstall = downloadFile;
                if (release.AssetName != null && release.AssetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    if (onProgress != null) onProgress(85, "Extracting update...");
                    exeToInstall = ExtractUpdateZip(downloadFile, target, targetDir);
                    try { File.Delete(downloadFile); } catch { }
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

                // Keep legacy alias executable in sync if present in the same install directory
                try
                {
                    string legacyAlias = Path.Combine(targetDir, "ClaudeUsageWidget.exe");
                    if (!string.Equals(target, legacyAlias, StringComparison.OrdinalIgnoreCase) && File.Exists(legacyAlias))
                        File.Copy(target, legacyAlias, true);
                }
                catch { }

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

        public static string ComputeFileSha256(string path)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(fs);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private static string FetchExpectedSha256(string checksumsUrl, string assetName)
        {
            if (!IsTrustedUpdateUrl(checksumsUrl)) return null;
            try
            {
                using (CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                using (HttpResponseMessage resp = SendWithTrustedRedirects(checksumsUrl, HttpCompletionOption.ResponseContentRead, cts.Token))
                {
                    if (!resp.IsSuccessStatusCode) return null;
                    string txt = resp.Content.ReadAsStringAsync().Result;
                    foreach (string rawLine in txt.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string[] parts = rawLine.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2 && string.Equals(parts[parts.Length - 1].TrimStart('*'), assetName, StringComparison.OrdinalIgnoreCase))
                        {
                            string cand = parts[0].ToLowerInvariant();
                            if (IsValidSha256Hex(cand)) return cand;
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Write("updater.checksum", ex); }
            return null;
        }

        public static bool IsValidExecutable(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (fs.Length < 10240) return false;
                    byte[] dos = new byte[64];
                    if (fs.Read(dos, 0, 64) < 64) return false;
                    if (dos[0] != 0x4D || dos[1] != 0x5A) return false; // MZ

                    uint peOffset = BitConverter.ToUInt32(dos, 0x3C);
                    if (peOffset < 64 || (long)peOffset > fs.Length - 28) return false;

                    fs.Seek((long)peOffset, SeekOrigin.Begin);
                    byte[] pe = new byte[28];
                    if (fs.Read(pe, 0, 28) < 28) return false;
                    if (pe[0] != 0x50 || pe[1] != 0x45 || pe[2] != 0x00 || pe[3] != 0x00) return false; // PE\0\0

                    ushort optMagic = BitConverter.ToUInt16(pe, 24);
                    return optMagic == 0x010B || optMagic == 0x020B; // PE32 or PE32+
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
