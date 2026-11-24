using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using ClaudeUsageWidget;

namespace Tests
{
    public static class Assert
    {
        public static void True(bool condition, string message = null)
        {
            if (!condition) throw new Exception("Assert.True failed: " + (message ?? "expected true, got false"));
        }

        public static void False(bool condition, string message = null)
        {
            if (condition) throw new Exception("Assert.False failed: " + (message ?? "expected false, got true"));
        }

        public static void Equal<T>(T expected, T actual, string message = null)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new Exception(string.Format("Assert.Equal failed: expected [{0}], got [{1}]. {2}", expected, actual, message ?? ""));
            }
        }

        public static void NotNull(object obj, string message = null)
        {
            if (obj == null) throw new Exception("Assert.NotNull failed: " + (message ?? "object is null"));
        }
    }

    public class UnitTests
    {
        public static void RunAll()
        {
            int passed = 0, failed = 0;
            MethodInfo[] methods = typeof(UnitTests).GetMethods(BindingFlags.Public | BindingFlags.Static);

            foreach (MethodInfo m in methods)
            {
                if (!m.Name.StartsWith("Test_")) continue;
                try
                {
                    m.Invoke(null, null);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write("  [PASS] ");
                    Console.ResetColor();
                    Console.WriteLine(m.Name);
                    passed++;
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write("  [FAIL] ");
                    Console.ResetColor();
                    Console.WriteLine(m.Name + ": " + (ex.InnerException != null ? ex.InnerException.Message : ex.Message));
                    failed++;
                }
            }

            Console.WriteLine();
            Console.WriteLine(new string('-', 60));
            if (failed == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(string.Format("SUCCESS: All {0} unit tests passed!", passed));
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(string.Format("FAILURE: {0} passed, {1} failed.", passed, failed));
            }
            Console.ResetColor();
            Console.WriteLine(new string('-', 60));

            if (failed > 0) Environment.Exit(1);
        }

        public static void Test_Fmt_Tokens()
        {
            L.Set("en");
            Assert.Equal("500", Fmt.Tokens(500));
            Assert.True(Fmt.Tokens(1500).IndexOf("k", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.True(Fmt.Tokens(2500000).IndexOf("m", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.True(Fmt.Tokens(3500000000L).IndexOf("b", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static void Test_Fmt_Currency_Standard()
        {
            Opts.CurrencyCustomEnabled = false;
            string usd = Fmt.Usd(2.50);
            Assert.Equal("$2.50", usd);
            Assert.Equal("$2.50", Fmt.UsdShort(2.50));
        }

        public static void Test_Fmt_Currency_Rupiah()
        {
            Opts.CurrencyCustomEnabled = true;
            Opts.CurrencyRate = 18000;
            Opts.CurrencySymbol = "Rp";
            Opts.CurrencyShowBoth = true;

            string s = Fmt.Usd(1.0);
            Assert.True(s.Contains("$1.00"), "Must contain USD value");
            Assert.True(s.Contains("Rp"), "Must contain Rp symbol");

            string shortStr = Fmt.UsdShort(1.0);
            Assert.True(shortStr.StartsWith("Rp"), "UsdShort must start with Rp");
            Assert.False(shortStr.Contains("$"), "UsdShort should not show dollar symbol");

            // Reset
            Opts.CurrencyCustomEnabled = false;
        }

        public static void Test_Pricing_Lookup()
        {
            Pricing p = Pricing.Load();
            
            // Claude model
            Price pClaude = p.Find("claude-opus-4-6");
            Assert.NotNull(pClaude, "claude-opus-4-6 must be found");
            Assert.True(pClaude.Input > 0);

            // Modern active Claude model
            Price pSonnet35 = p.Find("claude-3-5-sonnet-20241022");
            Assert.NotNull(pSonnet35, "claude-3-5-sonnet must be found");
            Assert.Equal(3.0, pSonnet35.Input);
            Assert.Equal(15.0, pSonnet35.Output);
            Assert.Equal(0.30, pSonnet35.CacheRead);

            // Gemini model in pricing table
            Price pGemini = p.Find("gemini-3.8-flash");
            Assert.NotNull(pGemini, "gemini-3.8-flash must be found");
            Assert.True(pGemini.Input > 0);
            Assert.True(pGemini.Output > 0);

            Price pGemini25 = p.Find("gemini-2.5-flash");
            Assert.NotNull(pGemini25, "gemini-2.5-flash must be found");
            Assert.Equal(0.075, pGemini25.Input);

            // Gemini fallback models
            Price pFallbackFlash = p.Find("gemini-unknown-model");
            Assert.NotNull(pFallbackFlash, "gemini fallback must work");
            Assert.Equal(0.10, pFallbackFlash.Input);

            Price pFallbackPro = p.Find("gemini-custom-pro-exp");
            Assert.NotNull(pFallbackPro, "gemini pro fallback must work");
            Assert.Equal(1.25, pFallbackPro.Input);
        }

        public static void Test_Tally_Calculations()
        {
            Tally t = new Tally();
            t.Input = 10000;
            t.Output = 2000;
            t.CacheRead = 80000;
            t.CacheWrite1h = 5000;

            Assert.Equal(97000L, t.Total);
            Assert.Equal(95000L, t.InputSide);
            Assert.True(t.CacheHit > 0.8 && t.CacheHit < 0.9, "Cache hit ratio should be ~84%");
        }

        public static void Test_Antigravity_Log_Parsing()
        {
            Pricing pricing = Pricing.Load();
            UsageScanner sc = new UsageScanner(pricing);
            sc.TrackProjects = true;

            // Mock Antigravity transcript.jsonl lines
            string tempDir = Path.Combine(Path.GetTempPath(), "cuw_test_ag_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string jsonl = Path.Combine(tempDir, "transcript.jsonl");
                string todayUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

                string line1 = string.Format(
                    "{{\"step_index\":1,\"source\":\"MODEL\",\"type\":\"PLANNER_RESPONSE\",\"status\":\"DONE\",\"created_at\":\"{0}\",\"model\":\"gemini-3.8-flash-medium\",\"input_tokens\":5000,\"cache_read_tokens\":10000,\"output_tokens\":200,\"tool_calls\":[{{\"name\":\"test\"}}]}}",
                    todayUtc
                );
                // Duplicate line should be ignored
                string line1Dup = line1;
                // Second step
                string line2 = string.Format(
                    "{{\"step_index\":2,\"source\":\"MODEL\",\"type\":\"PLANNER_RESPONSE\",\"status\":\"DONE\",\"created_at\":\"{0}\",\"model\":\"gemini-3.8-flash-medium\",\"input_tokens\":3000,\"cache_read_tokens\":5000,\"output_tokens\":100}}",
                    todayUtc
                );

                File.WriteAllLines(jsonl, new[] { line1, line1Dup, line2 });

                sc.Roots = delegate { return new List<string> { tempDir }; };
                sc.Scan();

                UsageSnapshot snap = sc.Snapshot();
                Assert.True(snap.Ready);
                Assert.Equal(2L, snap.TodayTotal.Requests, "Duplicate line must be filtered");
                Assert.Equal(8000L, snap.TodayTotal.Input);
                Assert.Equal(15000L, snap.TodayTotal.CacheRead);
                Assert.Equal(300L, snap.TodayTotal.Output);
                Assert.True(snap.TodayTotal.Cost > 0, "Cost must be calculated");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        public static void Test_Claude_Log_Parsing()
        {
            Pricing pricing = Pricing.Load();
            UsageScanner sc = new UsageScanner(pricing);
            sc.TrackProjects = true;

            string tempDir = Path.Combine(Path.GetTempPath(), "cuw_test_claude_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string jsonl = Path.Combine(tempDir, "sample.jsonl");
                string todayUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

                string line = string.Format(
                    "{{\"type\":\"assistant\",\"timestamp\":\"{0}\",\"requestId\":\"req_123\",\"cwd\":\"C:\\\\TestProj\",\"message\":{{\"id\":\"msg_1\",\"model\":\"claude-opus-4-6\",\"usage\":{{\"input_tokens\":1000,\"output_tokens\":500,\"cache_read_input_tokens\":2000,\"cache_creation_input_tokens\":0}}}}}}",
                    todayUtc
                );

                File.WriteAllLines(jsonl, new[] { line });

                sc.Roots = delegate { return new List<string> { tempDir }; };
                sc.Scan();

                UsageSnapshot snap = sc.Snapshot();
                Assert.True(snap.Ready);
                Assert.Equal(1L, snap.TodayTotal.Requests);
                Assert.Equal(1000L, snap.TodayTotal.Input);
                Assert.Equal(500L, snap.TodayTotal.Output);
                Assert.Equal(2000L, snap.TodayTotal.CacheRead);
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        public static void Test_Alert_Engine_Thresholds()
        {
            AlertEngine engine = new AlertEngine();
            RateLimitSnapshot rl = new RateLimitSnapshot();
            rl.FileFound = true;
            rl.FiveHour.Available = true;
            rl.FiveHour.UsedPercent = 50; // Initial baseline
            engine.Evaluate(rl, new[] { 80, 95 }, false); // Baseline pass (silent)

            rl.FiveHour.UsedPercent = 82; // Above 80%
            List<Alert> alerts = engine.Evaluate(rl, new[] { 80, 95 }, false);
            Assert.Equal(1, alerts.Count, "Alert should fire for 80% threshold");
            Assert.Equal(80, alerts[0].Threshold);
            Assert.False(alerts[0].IsReset);

            // Re-evaluating with same percentage should not re-fire
            List<Alert> alerts2 = engine.Evaluate(rl, new[] { 80, 95 }, false);
            Assert.Equal(0, alerts2.Count, "Alert should not repeat for same threshold");

            // Crossing 95%
            rl.FiveHour.UsedPercent = 96;
            List<Alert> alerts3 = engine.Evaluate(rl, new[] { 80, 95 }, false);
            Assert.Equal(1, alerts3.Count, "Alert should fire when crossing 95%");
            Assert.Equal(95, alerts3[0].Threshold);
        }

        public static void Test_Budget_Engine()
        {
            BudgetEngine be = new BudgetEngine();
            DateTime now = DateTime.Now;

            // Initial baseline pass (silent)
            be.Evaluate(5.0, 0, 10.0, 0, now);

            // Budget daily = $10. Cost climbs to $8.50 (85%) -> should trigger 80%
            List<BudgetAlert> alerts = be.Evaluate(8.50, 0, 10.0, 0, now);
            Assert.Equal(1, alerts.Count, "Should trigger 80% budget alert");
            Assert.Equal(80, alerts[0].Percent);

            // Next call within same threshold -> no repeat
            List<BudgetAlert> alerts2 = be.Evaluate(8.70, 0, 10.0, 0, now);
            Assert.Equal(0, alerts2.Count, "Should not repeat alert");

            // Exceeding 100%
            List<BudgetAlert> alerts3 = be.Evaluate(10.50, 0, 10.0, 0, now);
            Assert.Equal(1, alerts3.Count, "Should trigger 100% budget alert");
            Assert.Equal(100, alerts3[0].Percent);
        }

        public static void Test_Hotkey_Parsing()
        {
            uint mods, vk;
            Assert.True(HotkeyUtil.TryParse("Ctrl+Alt+U", out mods, out vk));
            Assert.Equal((uint)(HotkeyUtil.MOD_CONTROL | HotkeyUtil.MOD_ALT), mods);
            Assert.Equal((uint)'U', vk);

            Assert.True(HotkeyUtil.TryParse("Win+Shift+F9", out mods, out vk));
            Assert.Equal((uint)(HotkeyUtil.MOD_WIN | HotkeyUtil.MOD_SHIFT), mods);
            Assert.Equal((uint)0x78, vk); // VK_F9 = 0x70 + 8

            Assert.False(HotkeyUtil.TryParse("NotAValidHotkey", out mods, out vk));
            Assert.False(HotkeyUtil.TryParse("", out mods, out vk));
        }

        public static void Test_FloatingForm_TaskbarPolicy()
        {
            using (FloatingForm f = new FloatingForm())
            {
                Assert.False(f.ShowInTaskbar, "FloatingForm must NEVER show in taskbar");
                var prop = typeof(FloatingForm).GetProperty("CreateParams", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                CreateParams cp = (CreateParams)prop.GetValue(f, null);
                const int WS_EX_TOOLWINDOW = 0x80;
                Assert.True((cp.ExStyle & WS_EX_TOOLWINDOW) != 0, "WS_EX_TOOLWINDOW style must be set");
            }
        }

        public static void Test_Config_Validation()
        {
            AppConfig c = new AppConfig();
            Assert.False(c.ShowInTaskbar, "ShowInTaskbar must default to false");
            Assert.True(c.ScanGemini, "ScanGemini must default to true");
            Assert.True(c.BurnRateThresholdTokens > 0);
            Assert.NotNull(c.CurrencySymbol);
        }

        public static void Test_OfficialPricing_Integration()
        {
            Pricing p = Pricing.Load();
            Price sonnet = p.Find("claude-3-7-sonnet");
            Assert.NotNull(sonnet, "claude-3-7-sonnet must exist");
            Assert.Equal(3.0, sonnet.Input);
            Assert.Equal(15.0, sonnet.Output);

            Price geminiFlash = p.Find("gemini-2.0-flash");
            Assert.NotNull(geminiFlash, "gemini-2.0-flash must exist");
            Assert.Equal(0.10, geminiFlash.Input);
            Assert.Equal(0.40, geminiFlash.Output);

            Price geminiPro = p.Find("gemini-2.5-pro");
            Assert.NotNull(geminiPro, "gemini-2.5-pro must exist");
            Assert.Equal(1.25, geminiPro.Input);
            Assert.Equal(5.00, geminiPro.Output);
        }

        public static void Test_Split_Usage_Claude_And_Antigravity()
        {
            Assert.True(Palette.ClaudeColor != Palette.AntigravityColor, "Claude and Antigravity must have distinct bar colors");
            Assert.True(UsageScanner.IsAntigravityModel("gemini-2.5-flash"));
            Assert.True(UsageScanner.IsAntigravityModel("gemini-3.8-pro"));

            Pricing pricing = Pricing.Load();
            UsageScanner sc = new UsageScanner(pricing);
            string tempDir = Path.Combine(Path.GetTempPath(), "cuw_test_split_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string nowUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                // Antigravity log running Claude Opus inside Antigravity
                string agLine = string.Format(
                    "{{\"type\":\"PLANNER_RESPONSE\",\"model\":\"claude-opus-4-6-thinking\",\"created_at\":\"{0}\",\"step_index\":1,\"input_tokens\":10000,\"output_tokens\":500,\"cache_read_tokens\":2000}}",
                    nowUtc
                );
                // Claude Code log running Claude 3.7 Sonnet
                string claudeLine = string.Format(
                    "{{\"type\":\"assistant\",\"timestamp\":\"{0}\",\"message\":{{\"id\":\"msg_1\",\"model\":\"claude-3-7-sonnet\",\"usage\":{{\"input_tokens\":5000,\"output_tokens\":250,\"cache_read_input_tokens\":1000}}}}}}",
                    nowUtc
                );

                File.WriteAllLines(Path.Combine(tempDir, "ag.jsonl"), new[] { agLine });
                File.WriteAllLines(Path.Combine(tempDir, "claude.jsonl"), new[] { claudeLine });

                sc.Roots = delegate { return new List<string> { tempDir }; };
                sc.Scan();

                UsageSnapshot snap = sc.Snapshot();
                Assert.Equal(12500L, snap.AntigravityToday.Total, "AntigravityToday must correctly include tokens from Antigravity even with Opus model");
                Assert.Equal(6250L, snap.ClaudeToday.Total, "ClaudeToday must only include Claude Code tokens");
                Assert.Equal(18750L, snap.TodayTotal.Total, "TodayTotal must equal sum of Claude and Antigravity");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        public static void Test_Antigravity_RateLimits_Calculation()
        {
            Pricing pricing = Pricing.Load();
            UsageScanner sc = new UsageScanner(pricing);
            sc.AntigravityQuota5hTokens = 1000000;
            sc.AntigravityQuota7dTokens = 10000000;

            string tempDir = Path.Combine(Path.GetTempPath(), "cuw_test_ag_limits_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string jsonl = Path.Combine(tempDir, "sample_ag.jsonl");
                string nowUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

                string line = string.Format(
                    "{{\"type\":\"PLANNER_RESPONSE\",\"model\":\"gemini-2.5-flash\",\"created_at\":\"{0}\",\"step_index\":1,\"input_tokens\":50000,\"output_tokens\":2000,\"cache_read_tokens\":10000}}",
                    nowUtc
                );

                File.WriteAllLines(jsonl, new[] { line });

                sc.Roots = delegate { return new List<string> { tempDir }; };
                sc.Scan();

                UsageSnapshot snap = sc.Snapshot();
                Assert.True(snap.AntigravityFiveHour.Available, "AntigravityFiveHour must be available");
                Assert.True(snap.AntigravitySevenDay.Available, "AntigravitySevenDay must be available");

                // Total tokens = 50000 + 2000 + 10000 = 62000
                // 5h = 62000 / 1000000 * 100% = 6.2%
                Assert.Equal(6.2, Math.Round(snap.AntigravityFiveHour.UsedPercent, 1), "5h used percent should match quota calculation");
                // 7d = 62000 / 10000000 * 100% = 0.62%
                Assert.Equal(0.62, Math.Round(snap.AntigravitySevenDay.UsedPercent, 2), "7d used percent should match quota calculation");

                Assert.True(snap.AntigravityFiveHour.ResetsAt > DateTime.Now, "5h resetsAt should be in the future");
                Assert.True(snap.AntigravitySevenDay.ResetsAt > DateTime.Now, "7d resetsAt should be in the future");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        public static void Test_Monitoring_Toggles_Selective_Scanning()
        {
            Pricing pricing = Pricing.Load();
            UsageScanner sc = new UsageScanner(pricing);
            string tempDir = Path.Combine(Path.GetTempPath(), "cuw_test_toggles_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string nowUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                string agLine = string.Format(
                    "{{\"type\":\"PLANNER_RESPONSE\",\"model\":\"gemini-3.8-flash\",\"created_at\":\"{0}\",\"step_index\":1,\"input_tokens\":10000,\"output_tokens\":100,\"cache_read_tokens\":0}}",
                    nowUtc
                );
                string claudeLine = string.Format(
                    "{{\"type\":\"assistant\",\"timestamp\":\"{0}\",\"message\":{{\"id\":\"msg_tog\",\"model\":\"claude-3-5-haiku\",\"usage\":{{\"input_tokens\":4000,\"output_tokens\":200,\"cache_read_input_tokens\":0}}}}}}",
                    nowUtc
                );

                File.WriteAllLines(Path.Combine(tempDir, "ag.jsonl"), new[] { agLine });
                File.WriteAllLines(Path.Combine(tempDir, "claude.jsonl"), new[] { claudeLine });

                sc.Roots = delegate { return new List<string> { tempDir }; };

                // 1. Only Claude enabled
                sc.ScanClaude = true;
                sc.ScanGemini = false;
                sc.ResetWithPricing(pricing);
                sc.Scan();
                UsageSnapshot snapClaudeOnly = sc.Snapshot();
                Assert.Equal(4200L, snapClaudeOnly.TodayTotal.Total, "Only Claude tokens should be scanned");
                Assert.Equal(0L, snapClaudeOnly.AntigravityToday.Total, "Gemini tokens must be 0 when ScanGemini is false");

                // 2. Only Gemini enabled
                sc.ScanClaude = false;
                sc.ScanGemini = true;
                sc.ResetWithPricing(pricing);
                sc.Scan();
                UsageSnapshot snapGeminiOnly = sc.Snapshot();
                Assert.Equal(10100L, snapGeminiOnly.TodayTotal.Total, "Only Gemini tokens should be scanned");
                Assert.Equal(0L, snapGeminiOnly.ClaudeToday.Total, "Claude tokens must be 0 when ScanClaude is false");

                // 3. Neither enabled
                sc.ScanClaude = false;
                sc.ScanGemini = false;
                sc.ResetWithPricing(pricing);
                sc.Scan();
                UsageSnapshot snapNone = sc.Snapshot();
                Assert.Equal(0L, snapNone.TodayTotal.Total, "Total tokens must be 0 when both are disabled");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        public static void Test_Antigravity_Zero_Usage_ResetsAt_Is_MinValue()
        {
            Pricing pricing = Pricing.Load();
            UsageScanner sc = new UsageScanner(pricing);
            string tempDir = Path.Combine(Path.GetTempPath(), "cuw_test_zero_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                sc.Roots = delegate { return new List<string> { tempDir }; };
                sc.Scan();
                UsageSnapshot snap = sc.Snapshot();

                Assert.Equal(0L, snap.AntigravityToday.Total);
                Assert.Equal(0L, snap.AntigravityWeek.Total);
                Assert.Equal(DateTime.MinValue, snap.AntigravityFiveHour.ResetsAt, "5h ResetsAt should be MinValue when 0 tokens used");
                Assert.Equal(DateTime.MinValue, snap.AntigravitySevenDay.ResetsAt, "7d ResetsAt should be MinValue when 0 tokens used");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        public static void Test_Real_Antigravity_Brain_Classification()
        {
            Pricing pricing = Pricing.Load();
            UsageScanner sc = new UsageScanner(pricing);
            sc.ScanClaude = true;
            sc.ScanGemini = true;
            sc.Roots = delegate
            {
                List<string> r = new List<string>();
                if (Directory.Exists(Paths.AntigravityBrainDir)) r.Add(Paths.AntigravityBrainDir);
                return r;
            };
            sc.Scan();
            UsageSnapshot snap = sc.Snapshot();

            // When only Antigravity Brain is scanned, ClaudeToday must be 0!
            // Even if Antigravity ran claude-opus-4-6-thinking or claude-3-7-sonnet!
            Assert.Equal(0L, snap.ClaudeToday.Total, "ClaudeToday must be 0 when scanning Antigravity brain");
            Assert.Equal(0L, snap.ClaudeWeek.Total, "ClaudeWeek must be 0 when scanning Antigravity brain");
            Assert.True(snap.AntigravityToday.Total > 0, "AntigravityToday must have tokens from Antigravity activity");

            // Verify opus-4-6-thinking is in TodayAntigravity, NOT in TodayClaude!
            Assert.False(snap.TodayClaude.ContainsKey("claude-opus-4-6-thinking"), "claude-opus-4-6-thinking must NEVER be in TodayClaude when run in Antigravity");
        }

        public static void Test_Antigravity_Alert_Engine()
        {
            AlertEngine engine = new AlertEngine();
            RateLimitSnapshot rl = new RateLimitSnapshot();
            rl.AntigravityFiveHour.Available = true;
            rl.AntigravityFiveHour.UsedPercent = 30; // Baseline pass
            engine.Evaluate(rl, new[] { 80, 95 }, false);

            rl.AntigravityFiveHour.UsedPercent = 88;
            List<Alert> alerts = engine.Evaluate(rl, new[] { 80, 95 }, false);
            Assert.Equal(1, alerts.Count, "Alert should fire for Antigravity 5h limit");
            Assert.Equal("antigravity_five_hour", alerts[0].Window);
            Assert.Equal(80, alerts[0].Threshold);
        }

        public static void Test_Antigravity_TrayText_IconSpec()
        {
            RateLimitSnapshot rl = new RateLimitSnapshot();
            rl.FiveHour.Available = false;
            rl.AntigravityFiveHour.Available = true;
            rl.AntigravityFiveHour.UsedPercent = 75;

            string text;
            Color badge;
            bool weekly;
            TrayText.IconSpec(rl, out text, out badge, out weekly);
            Assert.Equal("75", text, "Tray icon should display Antigravity limit when Claude limit unavailable");
            Assert.Equal(TrayIconPainter.StatusColor(75), badge);
        }

        public static void Test_DualCards_And_Pill_Layout()
        {
            using (FloatingForm f = new FloatingForm())
            {
                ViewModel vm = new ViewModel();
                vm.Usage = new UsageSnapshot();
                vm.Usage.Ready = true;
                vm.Usage.TodayTotal.Cost = 1.25;
                vm.Limits = new RateLimitSnapshot();
                vm.Limits.FiveHour.Available = true;
                vm.Limits.FiveHour.UsedPercent = 40;
                vm.Limits.AntigravityFiveHour.Available = true;
                vm.Limits.AntigravityFiveHour.UsedPercent = 25;

                f.SetData(vm);
                MethodInfo mBuild = typeof(FloatingForm).GetMethod("BuildRows", BindingFlags.Instance | BindingFlags.NonPublic);

                // 1. Pill mode (Compact = true)
                f.Compact = true;
                var rowsPill = (System.Collections.IList)mBuild.Invoke(f, null);
                Assert.True(rowsPill.Count == 1, "Pill mode should return exactly 1 row");
                object r0 = rowsPill[0];
                object kindPill = r0.GetType().GetField("Kind").GetValue(r0);
                Assert.Equal("Pill", kindPill.ToString(), "Row kind must be Pill");

                // 2. Dual-Column Cards mode (Compact = false)
                f.Compact = false;
                var rowsCards = (System.Collections.IList)mBuild.Invoke(f, null);
                Assert.True(rowsCards.Count >= 2, "Expanded mode should have header and dual cards");
                object rDual = rowsCards[1];
                object kindDual = rDual.GetType().GetField("Kind").GetValue(rDual);
                Assert.Equal("DualCards", kindDual.ToString(), "Row kind must be DualCards");

                object leftCol = rDual.GetType().GetField("LeftCol").GetValue(rDual);
                object rightCol = rDual.GetType().GetField("RightCol").GetValue(rDual);
                Assert.NotNull(leftCol, "LeftCol (Claude) must not be null");
                Assert.NotNull(rightCol, "RightCol (Antigravity) must not be null");

                string leftTitle = (string)leftCol.GetType().GetField("Title").GetValue(leftCol);
                string rightTitle = (string)rightCol.GetType().GetField("Title").GetValue(rightCol);
                Assert.Equal("Claude Code", leftTitle);
                Assert.Equal("Antigravity", rightTitle);
            }
        }

        public static void Test_AntigravityRateFetcher_ParseQuotaSummary()
        {
            string sampleJson = "{\n" +
                "  \"response\": {\n" +
                "    \"groups\": [\n" +
                "      {\n" +
                "        \"displayName\": \"Gemini Models\",\n" +
                "        \"buckets\": [\n" +
                "          {\n" +
                "            \"bucketId\": \"gemini-weekly\",\n" +
                "            \"displayName\": \"Weekly Limit Remaining\",\n" +
                "            \"window\": \"weekly\",\n" +
                "            \"remainingFraction\": 0.81384,\n" +
                "            \"resetTime\": \"2026-11-29T09:20:46Z\"\n" +
                "          },\n" +
                "          {\n" +
                "            \"bucketId\": \"gemini-5h\",\n" +
                "            \"displayName\": \"Five Hour Limit Remaining\",\n" +
                "            \"window\": \"5h\",\n" +
                "            \"remainingFraction\": 0.41834,\n" +
                "            \"resetTime\": \"2026-11-22T14:20:46Z\"\n" +
                "          }\n" +
                "        ]\n" +
                "      }\n" +
                "    ]\n" +
                "  }\n" +
                "}";

            bool ok = AntigravityRateFetcher.ParseAndSaveQuotaSummary(sampleJson);
            Assert.True(ok, "ParseAndSaveQuotaSummary must succeed on valid JSON");

            RateLimitSnapshot s = RateLimitReader.Read();
            Assert.True(s.AntigravityFiveHour.Available, "Antigravity 5h must be available");
            Assert.True(Math.Abs(s.AntigravityFiveHour.UsedPercent - 58.17) < 0.1, "5h used must be ~58.17%");
            Assert.True(s.AntigravitySevenDay.Available, "Antigravity 7d must be available");
            Assert.True(Math.Abs(s.AntigravitySevenDay.UsedPercent - 18.62) < 0.1, "7d used must be ~18.62%");
        }

        public static void Test_MiniWidget_SmallerDimensions()
        {
            using (FloatingForm f = new FloatingForm())
            {
                ViewModel vm = new ViewModel();
                vm.Usage = new UsageSnapshot();
                vm.Usage.Ready = true;
                vm.ScanClaude = true;
                vm.ScanGemini = true;
                f.SetData(vm);
                f.Compact = true;
                f.Show();
                Application.DoEvents();

                PropertyInfo pDpi = typeof(FloatingForm).GetProperty("DpiScale", BindingFlags.Instance | BindingFlags.NonPublic);
                float s = (float)pDpi.GetValue(f, null);
                int expectedH = (int)(22 * s);
                Assert.Equal(expectedH, f.Height, "Compact height should be (int)(22 * s)");
                int expectedW = (int)(225 * s);
                Assert.Equal(expectedW, f.Width, "Compact dual-provider width should be (int)(225 * s)");

                // Single provider
                vm.ScanGemini = false;
                f.SetData(vm);
                int expectedSingleW = (int)(150 * s);
                Assert.Equal(expectedSingleW, f.Width, "Compact single-provider width should be (int)(150 * s)");
            }
        }

        public static void Test_MiniWidget_EdgeSnap_And_Anchor()
        {
            using (FloatingForm f = new FloatingForm())
            {
                IntPtr handle = f.Handle;
                f.Compact = true;
                Rectangle wa = Screen.FromRectangle(f.Bounds).WorkingArea;

                // Test Right Edge Snap
                // Place 12px away from right edge (within 20px threshold)
                f.Location = new Point(wa.Right - f.Width - 12, wa.Top + 100);
                f.SnapToEdges();

                Assert.True(f.SnappedRight, "Should be snapped to right edge");
                Assert.Equal(wa.Right, f.Right, "Right edge must touch wa.Right exactly");

                // Test Hover Expand preserves Right edge anchor
                MethodInfo mEnter = typeof(FloatingForm).GetMethod("OnMouseEnter", BindingFlags.Instance | BindingFlags.NonPublic);
                mEnter.Invoke(f, new object[] { EventArgs.Empty });

                Assert.Equal(wa.Right, f.Right, "Expanded window must remain anchored to wa.Right");

                // Test Collapse restores compact width and still touches wa.Right
                FieldInfo fExpanded = typeof(FloatingForm).GetField("_expanded", BindingFlags.Instance | BindingFlags.NonPublic);
                fExpanded.SetValue(f, false);
                MethodInfo mRelayout = typeof(FloatingForm).GetMethod("Relayout", BindingFlags.Instance | BindingFlags.NonPublic);
                mRelayout.Invoke(f, null);

                Assert.Equal(wa.Right, f.Right, "Collapsed window must remain anchored to wa.Right");

                // Test Dragging away unsets snap
                f.Location = new Point(wa.Left + 200, wa.Top + 200);
                f.SnapToEdges();
                Assert.False(f.SnappedRight, "Should not be snapped to right edge when in middle");
                Assert.False(f.SnappedLeft, "Should not be snapped to left edge when in middle");
            }
        }

        public static void Test_TrayText_Tooltip_DualProviders()
        {
            RateLimitSnapshot rl = new RateLimitSnapshot();
            rl.FiveHour.Available = false;
            rl.SevenDay.Available = true;
            rl.SevenDay.UsedPercent = 75;
            rl.SevenDay.ResetsAt = DateTime.Now.AddHours(18);

            rl.AntigravityFiveHour.Available = true;
            rl.AntigravityFiveHour.UsedPercent = 58;
            rl.AntigravityFiveHour.ResetsAt = DateTime.Now.AddHours(4);
            rl.AntigravitySevenDay.Available = true;
            rl.AntigravitySevenDay.UsedPercent = 19;
            rl.AntigravitySevenDay.ResetsAt = DateTime.Now.AddDays(6);

            UsageSnapshot u = new UsageSnapshot();
            u.TodayTotal.Cost = 1.50;

            string tip = TrayText.Tooltip(rl, u, null, 127, true, true);
            Assert.True(tip.Contains("Claude:"), "Dual tooltip must contain 'Claude:'");
            Assert.True(tip.Contains("Antigravity:"), "Dual tooltip must contain 'Antigravity:'");
            Assert.True(tip.Contains("5h") || tip.Contains("5j"), "Must contain 5h/5j");
            Assert.True(tip.Contains("7d") || tip.Contains("7h"), "Must contain 7d/7h");
            Assert.True(tip.Length <= 127, "Must not exceed 127 characters (was " + tip.Length + ")");
        }

        public static void Test_TrayText_Tooltip_Selective_Scanning()
        {
            RateLimitSnapshot rl = new RateLimitSnapshot();
            rl.FiveHour.Available = true;
            rl.FiveHour.UsedPercent = 40;
            rl.SevenDay.Available = true;
            rl.SevenDay.UsedPercent = 60;
            rl.AntigravityFiveHour.Available = true;
            rl.AntigravityFiveHour.UsedPercent = 25;
            rl.AntigravitySevenDay.Available = true;
            rl.AntigravitySevenDay.UsedPercent = 15;

            UsageSnapshot u = new UsageSnapshot();

            // 1. Claude only
            string claudeOnly = TrayText.Tooltip(rl, u, null, 127, true, false);
            Assert.True(claudeOnly.Contains("Claude Code:"), "Should have 'Claude Code:' header");
            Assert.False(claudeOnly.Contains("Antigravity"), "Should not contain Antigravity when scanGemini=false");

            // 2. Antigravity only
            string agOnly = TrayText.Tooltip(rl, u, null, 127, false, true);
            Assert.True(agOnly.Contains("Antigravity:"), "Should have 'Antigravity:' header");
            Assert.False(agOnly.Contains("Claude"), "Should not contain Claude when scanClaude=false");

            // 3. Neither active
            string none = TrayText.Tooltip(rl, u, null, 127, false, false);
            Assert.False(none.Contains("Claude:"), "Should not contain Claude: when none active");
            Assert.False(none.Contains("Antigravity:"), "Should not contain Antigravity: when none active");
            Assert.True(none.Contains(L.T("tip.noproviders")), "Should state no active providers");
        }

        public static void Test_TrayText_Tooltip_ShortPrefix_63Limit()
        {
            RateLimitSnapshot rl = new RateLimitSnapshot();
            rl.FiveHour.Available = true;
            rl.FiveHour.UsedPercent = 40;
            rl.SevenDay.Available = true;
            rl.SevenDay.UsedPercent = 60;
            rl.AntigravityFiveHour.Available = true;
            rl.AntigravityFiveHour.UsedPercent = 25;
            rl.AntigravitySevenDay.Available = true;
            rl.AntigravitySevenDay.UsedPercent = 15;

            UsageSnapshot u = new UsageSnapshot();
            u.TodayTotal.Cost = 2.0;

            string shortTip = TrayText.Tooltip(rl, u, null, 63, true, true);
            Assert.True(shortTip.Contains("C:"), "Must use short prefix C:");
            Assert.True(shortTip.Contains("AG:"), "Must use short prefix AG:");
            Assert.True(shortTip.Length <= 63, "Must not exceed 63 characters (was " + shortTip.Length + ")");
        }

        public static void Test_Probe_Result_Parsing()
        {
            // Claude Code 2.1.330 stream-json result line where "type":"result" is not at the start
            string json = "{\"duration_api_ms\":0,\"is_error\":true,\"subtype\":\"success\",\"api_error_status\":null,\"result\":\"Not logged in · Please run /login\",\"type\":\"result\"}";
            Assert.True(json.IndexOf("\"type\":\"result\"", StringComparison.Ordinal) >= 0, "Must detect type:result substring");

            Dictionary<string, object> o = J.Parse(json);
            Assert.NotNull(o, "Parsed object must not be null");
            Assert.Equal("result", J.Str(o, "type"), "Type must be result");
            
            object isErr;
            Assert.True(o.TryGetValue("is_error", out isErr) && isErr is bool && (bool)isErr, "is_error must be true");
            string resultError = J.Str(o, "api_error_status") ?? J.Str(o, "result") ?? "error";
            Assert.Equal("Not logged in · Please run /login", resultError, "Must extract clear error result");
        }

        public static void Test_AppIcon_Resolution()
        {
            // AppIcon.Get() must not throw and should handle null safely
            Icon ic = AppIcon.Get();
            // File app.ico exists in project root
            Assert.True(File.Exists("app.ico"), "app.ico must exist in project root");
        }

        public static void Test_Widget_Sync_And_Reset_Time_Display()
        {
            DateTime now = DateTime.Now;
            DateTime next = now.AddMinutes(1);

            L.Set("en");
            string enSync = L.T("sync.schedule", Fmt.Clock(now), Fmt.Clock(next));
            Assert.True(enSync.Contains("Updated"), "EN sync text must mention Updated");
            Assert.True(enSync.Contains("Next sync"), "EN sync text must mention Next sync");

            L.Set("id");
            string idSync = L.T("sync.schedule", Fmt.Clock(now), Fmt.Clock(next));
            Assert.True(idSync.Contains("Diperbarui"), "ID sync text must mention Diperbarui");
            Assert.True(idSync.Contains("Sinkronisasi"), "ID sync text must mention Sinkronisasi");

            DateTime resetTarget = now.AddHours(2);
            string until = Fmt.Until(resetTarget);
            string at = Fmt.Clock(resetTarget);
            string fullReset = until + " (" + at + ")";
            Assert.True(fullReset.Contains("("), "Must include clock time in parentheses");
        }

        public static void Test_Runway_Estimator()
        {
            RateLimitSnapshot snap = new RateLimitSnapshot();
            snap.FiveHour = new LimitWindow { Available = true, UsedPercent = 75, ResetsAt = DateTime.Now.AddHours(2) };
            UsageSnapshot usage = new UsageSnapshot();
            usage.TokensLast10m = 25000;

            double runway = RunwayEstimator.EstimateMinutes(snap, usage);
            Assert.True(runway > 0, "Runway should be estimated when tokens are burning");
            Assert.True(runway < 200, "Runway should be within reasonable minutes");

            Assert.Equal("45m", Fmt.Runway(45));
            Assert.Equal("2h 05m", Fmt.Runway(125));
            Assert.Equal("1h 00m", Fmt.Runway(60));
        }

        public static void Test_Status_Formatter()
        {
            RateLimitSnapshot snap = new RateLimitSnapshot();
            snap.FiveHour = new LimitWindow { Available = true, UsedPercent = 40, ResetsAt = DateTime.Now.AddHours(1) };
            snap.SevenDay = new LimitWindow { Available = true, UsedPercent = 80, ResetsAt = DateTime.Now.AddDays(3) };
            snap.AntigravityFiveHour = new LimitWindow { Available = true, UsedPercent = 15, ResetsAt = DateTime.Now.AddHours(4) };
            snap.AntigravitySevenDay = new LimitWindow { Available = true, UsedPercent = 30, ResetsAt = DateTime.Now.AddDays(5) };

            UsageSnapshot usage = new UsageSnapshot();
            usage.TodayTotal = new Tally { Input = 150000, Cost = 0.45 };
            usage.LastRecord = DateTime.Now;

            string md = StatusFormatter.Format(snap, usage, true, true);
            Assert.True(md.Contains("Claude Code:"), "Must format Claude Code row");
            Assert.True(md.Contains("Antigravity:"), "Must format Antigravity row");
            Assert.True(md.Contains("Today:"), "Must format today's stats");
            Assert.True(md.Contains("Last Chat:"), "Must format last chat timestamp");
        }

        public static void Test_Monthly_Report_Exporter()
        {
            HistoryStore store = new HistoryStore();
            store.Merge(new[] {
                new DayStat(new DateTime(2026, 11, 1), 50000, 0.25, 20),
                new DayStat(new DateTime(2026, 11, 2), 75000, 0.40, 35),
                new DayStat(new DateTime(2026, 10, 25), 10000, 0.05, 5)
            });

            List<DayStat> novDays = store.GetMonth(new DateTime(2026, 11, 1));
            Assert.Equal(2, novDays.Count, "Should return only 2 records for November");

            UsageSnapshot u = new UsageSnapshot();
            u.Week = new Dictionary<string, Tally> {
                { "claude-3-7-sonnet", new Tally { Requests = 50, Input = 120000, Cost = 0.60 } }
            };

            string md = ReportExporter.GenerateMonthlyMarkdown(store, u, new DateTime(2026, 11, 1));
            Assert.True(md.Contains("# AI Usage Monthly Report - 2026-11"), "Report must include title with month");
            Assert.True(md.Contains("## Monthly Summary"), "Report must include monthly summary");
            Assert.True(md.Contains("## Active Models"), "Report must include models section");
            Assert.True(md.Contains("claude-3-7-sonnet"), "Report must include active model name");
            Assert.True(md.Contains("## Daily Breakdown"), "Report must include daily breakdown table");
            Assert.True(md.Contains("2026-11-01"), "Report must include date entries");
        }

        public static void Test_StatusExporter_Export()
        {
            UsageSnapshot u = new UsageSnapshot();
            u.TodayTotal = new Tally { Input = 75000, CacheRead = 75000, Output = 2000, Cost = 0.75, Saved = 0.25 };
            u.TokensLast10m = 5000;
            u.LastContextTokens = 12000;
            u.LastActiveProject = "widget-claude";

            RateLimitSnapshot rl = new RateLimitSnapshot();
            rl.FiveHour = new LimitWindow { Available = true, UsedPercent = 42.5, ResetsAt = DateTime.UtcNow.AddHours(2) };
            rl.SevenDay = new LimitWindow { Available = true, UsedPercent = 15.0, ResetsAt = DateTime.UtcNow.AddDays(3) };
            rl.AntigravityFiveHour = new LimitWindow { Available = true, UsedPercent = 20.0 };

            ViewModel vm = new ViewModel();

            StatusExporter.Export(u, rl, vm);

            Assert.True(File.Exists(Paths.AppStatusJson), "Status JSON file must be created");
            string txt = File.ReadAllText(Paths.AppStatusJson, System.Text.Encoding.UTF8);
            Dictionary<string, object> d = J.Parse(txt);

            Assert.Equal(152000L, J.Long(d, "today_tokens"), "Today tokens mismatch");
            Assert.Equal(0.75, J.Num(d, "today_cost_usd"), "Today cost mismatch");
            Assert.Equal(0.25, J.Num(d, "today_saved_usd"), "Today saved mismatch");
            Assert.Equal(50.0, J.Num(d, "cache_hit_percent"), "Cache hit percent mismatch");
            Assert.Equal(42.5, J.Num(d, "five_hour_percent"), "Five hour percent mismatch");
            Assert.Equal(15.0, J.Num(d, "seven_day_percent"), "Seven day percent mismatch");
            Assert.Equal(20.0, J.Num(d, "antigravity_5h_percent"), "Antigravity 5h percent mismatch");
            Assert.Equal("widget-claude", J.Str(d, "last_active_project"), "Last active project mismatch");
        }

        public static void Test_OllamaProbe_Parse()
        {
            string sampleJson = "{\"models\":[{\"name\":\"llama3:latest\",\"size\":4661224676},{\"name\":\"deepseek-coder:6.7b\",\"size\":3800000000}]}";
            OllamaStatus st = OllamaProbe.ParseResponse(sampleJson);

            Assert.True(st.Online, "Ollama must be marked online");
            Assert.Equal(2, st.ModelCount, "Model count must be 2");
            Assert.True(st.ModelNames.Contains("llama3:latest"), "Must contain llama3:latest");
            Assert.True(st.ModelNames.Contains("deepseek-coder:6.7b"), "Must contain deepseek-coder:6.7b");

            OllamaStatus emptySt = OllamaProbe.ParseResponse("");
            Assert.False(emptySt.Online, "Empty response must be offline");
        }

        public static void Test_ProjectExplorer_Markdown()
        {
            List<ProjectRow> rows = new List<ProjectRow>();
            ProjectRow r1 = new ProjectRow();
            r1.Name = "widget-claude";
            r1.TodayCost = 0.45;
            r1.WeekCost = 3.12;
            r1.WeekTokens = 450000;
            r1.WeekRequests = 82;
            r1.Path = @"d:\1.Development\widget-claude";

            ProjectRow r2 = new ProjectRow();
            r2.Name = "ai-usage-watcher";
            r2.TodayCost = 0.10;
            r2.WeekCost = 0.50;
            r2.WeekTokens = 80000;
            r2.WeekRequests = 15;
            r2.Path = @"d:\1.Development\ai-usage-watcher";

            rows.Add(r1);
            rows.Add(r2);

            string md = ProjectExplorerForm.BuildMarkdownTable(rows);
            Assert.True(md.Contains("| Project | Today Cost | 7-Day Cost |"), "Table header missing");
            Assert.True(md.Contains("| widget-claude | $0.45 | $3.12 |"), "Row 1 format missing");
            Assert.True(md.Contains("| ai-usage-watcher | $0.10 | $0.50 |"), "Row 2 format missing");
            Assert.True(md.Contains(@"d:\1.Development\widget-claude"), "Path 1 missing");
        }

        public static void Test_CacheSavings_Calculation()
        {
            Tally t = new Tally();
            t.Input = 50000;
            t.CacheRead = 50000;
            t.Cost = 0.50;
            t.Saved = 0.25;

            Assert.Equal(100000L, t.Total, "Total tokens calculation");
            Assert.Equal(100000L, t.InputSide, "InputSide tokens calculation");
            Assert.Equal(0.5, t.CacheHit, "Cache hit ratio calculation");
            Assert.Equal(0.25, t.Saved, "Cache saved calculation");
        }

        public static void Test_BudgetEngine_ThresholdAlert()
        {
            BudgetEngine engine = new BudgetEngine();
            DateTime now = DateTime.Today;
            engine.Evaluate(0, 0, 10.0, 100.0, now);
            List<BudgetAlert> alerts = engine.Evaluate(10.5, 0, 10.0, 100.0, now);
            Assert.True(alerts.Count > 0, "Should generate daily budget alert");
            Assert.False(alerts[0].Monthly, "Alert should be daily");
            Assert.Equal(100, alerts[0].Percent, "Alert percent should be 100%");
        }

        public static void Test_Config_Ollama_Settings()
        {
            AppConfig cfg = new AppConfig();
            Assert.True(cfg.OllamaEnabled, "Default OllamaEnabled should be true");
            Assert.Equal(11434, cfg.OllamaPort, "Default OllamaPort should be 11434");

            cfg.OllamaPort = 8080;
            cfg.OllamaEnabled = false;
            Assert.Equal(8080, cfg.OllamaPort);
            Assert.False(cfg.OllamaEnabled);
        }
    }

    public static class TestRunner
    {
        [STAThread]
        public static void Main()
        {
            Console.WriteLine("============================================================");
            Console.WriteLine("   AI Usage Watcher - Automated Unit Test Suite (.NET 4.8) ");
            Console.WriteLine("============================================================");
            Console.WriteLine();

            UnitTests.RunAll();
        }
    }
}
