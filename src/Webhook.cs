using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace ClaudeUsageWidget
{
    /// <summary>Pengirim webhook async (kompatibel dengan Discord, Slack, Telegram, dan endpoint HTTPS eksternal).</summary>
    internal static class WebhookSender
    {
        private static readonly HttpClient Http = CreateClient();

        public static int FailCount { get; private set; }
        public static string LastError { get; private set; }

        private static HttpClient CreateClient()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            HttpClientHandler handler = new HttpClientHandler { AllowAutoRedirect = false };
            HttpClient c = new HttpClient(handler);
            c.Timeout = TimeSpan.FromSeconds(10);
            return c;
        }

        private static bool HostMatches(string host, string domain)
        {
            return string.Equals(host, domain, StringComparison.OrdinalIgnoreCase) ||
                   host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPrivateOrReservedIp(IPAddress ip)
        {
            if (ip == null) return true;
            if (IPAddress.IsLoopback(ip)) return true;
            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (ip.IsIPv6LinkLocal || ip.IsIPv6Multicast || ip.IsIPv6SiteLocal) return true;
                byte[] b6 = ip.GetAddressBytes();
                if ((b6[0] & 0xFE) == 0xFC) return true; // fc00::/7 ULA
                if (ip.IsIPv4MappedToIPv6) return IsPrivateOrReservedIp(ip.MapToIPv4());
                return ip.Equals(IPAddress.IPv6Any);
            }
            byte[] b = ip.GetAddressBytes();
            if (b.Length != 4) return false;
            if (b[0] == 0) return true;                                  // 0.0.0.0/8
            if (b[0] == 10) return true;                                 // 10.0.0.0/8
            if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return true;   // 100.64.0.0/10 CGNAT
            if (b[0] == 127) return true;                                // 127.0.0.0/8 loopback
            if (b[0] == 169 && b[1] == 254) return true;                 // 169.254.0.0/16 link-local / IMDS
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;    // 172.16.0.0/12
            if (b[0] == 192 && b[1] == 168) return true;                 // 192.168.0.0/16
            if (b[0] >= 224) return true;                                // multicast / reserved
            return false;
        }

        public static bool IsSafeWebhookUrl(string url, out Uri uri, out string error)
        {
            uri = null;
            if (string.IsNullOrWhiteSpace(url)) { error = "Webhook URL is empty."; return false; }
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out uri))
            {
                error = "Invalid webhook URI.";
                return false;
            }
            if (uri.Scheme != Uri.UriSchemeHttps)
            {
                error = "Webhook URL must use HTTPS.";
                return false;
            }
            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                error = "Webhook URL must not embed credentials.";
                return false;
            }
            string host = uri.DnsSafeHost ?? "";
            if (string.IsNullOrEmpty(host) || uri.IsLoopback ||
                string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
            {
                error = "Webhook host cannot target localhost or internal names.";
                return false;
            }
            IPAddress literalIp;
            if (IPAddress.TryParse(host, out literalIp) && IsPrivateOrReservedIp(literalIp))
            {
                error = "Webhook cannot target private or loopback IP addresses.";
                return false;
            }
            error = null;
            return true;
        }

        public static string BuildPayload(string url, string title, string message)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            Uri uri;
            string host = "";
            string path = "";
            if (!string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url.Trim(), UriKind.Absolute, out uri))
            {
                host = uri.DnsSafeHost ?? "";
                path = uri.AbsolutePath ?? "";
            }

            if (HostMatches(host, "hooks.slack.com"))
            {
                d["text"] = "*[" + title + "]* " + message;
            }
            else if (HostMatches(host, "api.telegram.org"))
            {
                d["text"] = "[" + title + "] " + message;
            }
            else if ((HostMatches(host, "discord.com") || HostMatches(host, "discordapp.com")) &&
                     path.StartsWith("/api/webhooks", StringComparison.OrdinalIgnoreCase))
            {
                d["content"] = "**[" + title + "]** " + message;
                d["username"] = "AI Usage Watcher";
            }
            else
            {
                d["text"] = "[" + title + "] " + message;
                d["content"] = "**[" + title + "]** " + message;
                d["username"] = "AI Usage Watcher";
                d["title"] = title;
                d["message"] = message;
            }
            return J.Ser.Serialize(d);
        }

        public static async Task<string> SendAsync(string url, string title, string message)
        {
            Uri uri;
            string valErr;
            if (!IsSafeWebhookUrl(url, out uri, out valErr)) return valErr;
            try
            {
                IPAddress[] addrs = await Dns.GetHostAddressesAsync(uri.DnsSafeHost).ConfigureAwait(false);
                for (int i = 0; i < addrs.Length; i++)
                {
                    if (IsPrivateOrReservedIp(addrs[i]))
                    {
                        string blocked = "Blocked private/loopback webhook destination IP.";
                        FailCount++;
                        LastError = blocked;
                        Log.Write("webhook: " + blocked);
                        return blocked;
                    }
                }

                string json = BuildPayload(uri.AbsoluteUri, title, message);
                using (StringContent content = new StringContent(json, Encoding.UTF8, "application/json"))
                using (HttpResponseMessage resp = await Http.PostAsync(uri.AbsoluteUri, content).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        string err = "HTTP " + (int)resp.StatusCode;
                        FailCount++;
                        LastError = err;
                        Log.Write("webhook: " + err);
                        return err;
                    }
                    LastError = null;
                    return null;
                }
            }
            catch (Exception ex)
            {
                string err = ex.GetType().Name + ": " + ex.Message;
                FailCount++;
                LastError = err;
                Log.Write("webhook", ex);
                return err;
            }
        }

        public static void Send(string url, string title, string message)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            Task.Run(delegate { return SendAsync(url, title, message); });
        }
    }
}
