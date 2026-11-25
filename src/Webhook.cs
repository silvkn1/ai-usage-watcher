using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace ClaudeUsageWidget
{
    /// <summary>Pengirim webhook async (kompatibel dengan Discord, Slack, Telegram, dan endpoint HTTP generic).</summary>
    internal static class WebhookSender
    {
        private static readonly HttpClient Http = CreateClient();

        public static int FailCount { get; private set; }
        public static string LastError { get; private set; }

        private static HttpClient CreateClient()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            HttpClient c = new HttpClient();
            c.Timeout = TimeSpan.FromSeconds(10);
            return c;
        }

        public static string BuildPayload(string url, string title, string message)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            string u = url ?? "";
            if (u.IndexOf("hooks.slack.com", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                d["text"] = "*[" + title + "]* " + message;
            }
            else if (u.IndexOf("api.telegram.org", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                d["text"] = "[" + title + "] " + message;
            }
            else if (u.IndexOf("discord.com/api/webhooks", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     u.IndexOf("discordapp.com/api/webhooks", StringComparison.OrdinalIgnoreCase) >= 0)
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
            if (string.IsNullOrWhiteSpace(url)) return "Webhook URL is empty.";
            try
            {
                string json = BuildPayload(url, title, message);
                using (StringContent content = new StringContent(json, Encoding.UTF8, "application/json"))
                using (HttpResponseMessage resp = await Http.PostAsync(url.Trim(), content).ConfigureAwait(false))
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
