using System;
using System.Threading;
using System.Windows.Forms;

namespace ClaudeUsageWidget
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            // Dipanggil oleh hook Claude Code: selalu keluar 0, tanpa keluaran, tanpa jendela, tanpa mutex.
            if (args.Length == 2 && args[0] == "--notify") { Environment.Exit(NotifyCli.Run(args[1])); return; }
            // Dipakai skrip uninstall: lepas hook milik widget dari settings.json Claude Code.
            if (args.Length == 1 && args[0] == "--remove-hooks") { Environment.Exit(HookInstaller.Uninstall() == null ? 0 : 1); return; }
            // Mode uji: ClaudeUsageWidget.exe --dump <file>  -> tulis ringkasan JSONL & limit lalu keluar.
            if (args.Length >= 2 && args[0] == "--dump") { Dump(args[1], args.Length > 2 ? args[2] : null); return; }
            // Mode uji: --probe <hasil.txt> [fileCermin]  -> satu probe CLI (+ salin ke file cermin) lalu keluar.
            if (args.Length >= 2 && args[0] == "--probe") { ProbeTest(args[1], args.Length > 2 ? args[2] : null); return; }
            // Diagnosa: --org-debug <hasil.txt>  -> struktur respons Admin API (tanpa key).
            if (args.Length == 2 && args[0] == "--org-debug")
            {
                string key = AppConfig.Load().GetAdminKey();
                string txt = key == null ? "admin key not stored" : AdminApi.DescribeFirstPage(key).GetAwaiter().GetResult();
                System.IO.File.WriteAllText(args[1], txt);
                return;
            }

            bool created;
            using (Mutex mutex = new Mutex(true, @"Local\AIUsageWatcher.SingleInstance", out created))
            {
                if (!created) return;   // sudah berjalan (mis. autostart + buka manual)

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { Log.Write("ui", e.Exception); };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e) { Log.Write("fatal", e.ExceptionObject as Exception); };
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

                Application.Run(new TrayApp());
                GC.KeepAlive(mutex);
            }
        }

        private static void Dump(string outFile, string extraRoot)
        {
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            UsageScanner sc = new UsageScanner(Pricing.Load());
            if (extraRoot != null) sc.Roots = delegate { return new System.Collections.Generic.List<string> { Paths.ProjectsDir, extraRoot }; };
            sc.TrackProjects = true;   // mode uji: ikut menghitung per proyek
            sc.Scan();
            long firstMs = sw.ElapsedMilliseconds;
            sc.Scan();   // scan kedua harus hampir instan (inkremental)
            UsageSnapshot s = sc.Snapshot();
            RateLimitSnapshot rl = RateLimitReader.Read();
            System.Text.StringBuilder b = new System.Text.StringBuilder();
            b.AppendLine("first_scan_ms=" + firstMs + " second_scan_ms=" + (sw.ElapsedMilliseconds - firstMs)
                + " working_set_mb=" + (Environment.WorkingSet / 1048576) + " skipped=" + s.SkippedLines);
            Append(b, "TODAY", s.TodayTotal);
            foreach (var kv in s.Today) Append(b, "  " + kv.Key, kv.Value);
            Append(b, "WEEK", s.WeekTotal);
            foreach (var kv in s.Week) Append(b, "  " + kv.Key, kv.Value);
            b.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "CACHE today: hit={0:F4} saved={1:F4} | week: hit={2:F4} saved={3:F4}",
                s.TodayTotal.CacheHit, s.TodayTotal.Saved, s.WeekTotal.CacheHit, s.WeekTotal.Saved));
            var projs = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, Tally>>(s.WeekProjects);
            projs.Sort(delegate(System.Collections.Generic.KeyValuePair<string, Tally> x, System.Collections.Generic.KeyValuePair<string, Tally> y) { return y.Value.Cost.CompareTo(x.Value.Cost); });
            foreach (var kv in projs) b.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "PROJ week {0}: total={1} req={2} cost={3:F4}", kv.Key, kv.Value.Total, kv.Value.Requests, kv.Value.Cost));
            b.AppendLine("5h=" + rl.FiveHour.UsedPercent + " avail=" + rl.FiveHour.Available + " stale=" + rl.FiveHour.IsStale + " reset=" + rl.FiveHour.ResetsAt);
            b.AppendLine("7d=" + rl.SevenDay.UsedPercent + " avail=" + rl.SevenDay.Available + " stale=" + rl.SevenDay.IsStale + " reset=" + rl.SevenDay.ResetsAt);
            System.IO.File.WriteAllText(outFile, b.ToString());
        }

        private static void ProbeTest(string outFile, string mirror)
        {
            Paths.Ensure();
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            string rl, merr = "-";
            string err = CliProbe.Run("claude-haiku-5-5", out rl);
            if (err == null && mirror != null) merr = Mirror.Write(mirror, rl) ?? "ok";
            System.IO.File.WriteAllText(outFile, "cli=" + CliProbe.FindClaude() + "\nms=" + sw.ElapsedMilliseconds
                + "\nerr=" + (err ?? "none") + "\nrl=" + rl + "\nmirror=" + merr + "\n");
        }

        private static void Append(System.Text.StringBuilder b, string name, Tally t)
        {
            b.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0}: in={1} out={2} cw5m={3} cw1h={4} cr={5} total={6} req={7} cost={8:F4} unpriced={9} fast={10}",
                name, t.Input, t.Output, t.CacheWrite5m, t.CacheWrite1h, t.CacheRead, t.Total, t.Requests, t.Cost, t.UnpricedRequests, t.FastRequests));
        }
    }
}
