using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace ClaudeUsageWidget
{
    /// <summary>Pengirim webhook async (kompatibel dengan Discord, Slack, dan endpoint HTTP generic).</summary>
    internal static class WebhookSender
    {
        private static readonly HttpClient Http = new HttpClient();

        static WebhookSender()
        {
            Http.Timeout = TimeSpan.FromSeconds(10);
        }

        public static void Send(string url, string title, string message)
        {
            if (string.IsNullOrEmpty(url)) return;
            Task.Run(delegate
            {
                try
                {
                    Dictionary<string, object> d = new Dictionary<string, object>();
                    d["content"] = "**[" + title + "]** " + message;
                    d["username"] = "Claude Usage";
                    string json = J.Ser.Serialize(d);
                    StringContent content = new StringContent(json, Encoding.UTF8, "application/json");
                    Http.PostAsync(url, content).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Log.Write("webhook", ex);
                }
            });
        }
    }
}
