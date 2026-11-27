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
            byte[] rawBytes = File.ReadAllBytes(Paths.AppStatusJson);
            Assert.False(rawBytes.Length >= 3 && rawBytes[0] == 0xEF && rawBytes[1] == 0xBB && rawBytes[2] == 0xBF,
                "Status JSON must not start with UTF-8 BOM (breaks Node.js JSON.parse)");
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

        public static void Test_Backdrop_Configuration_And_Validation()
        {
            AppConfig cfg = new AppConfig();
            Assert.NotNull(cfg.Backdrop, "Backdrop must not be null");
            Assert.True(DwmBackdrop.IsBackdropActive("acrylic"), "acrylic must be active");
            Assert.True(DwmBackdrop.IsBackdropActive("mica"), "mica must be active");
            Assert.True(DwmBackdrop.IsBackdropActive("mica-alt"), "mica-alt must be active");
            Assert.False(DwmBackdrop.IsBackdropActive("solid"), "solid must not be active backdrop");
            Assert.False(DwmBackdrop.IsBackdropActive("none"), "none must not be active backdrop");
            Assert.False(DwmBackdrop.IsBackdropActive(""), "empty must not be active backdrop");
        }

        public static void Test_Corner_Snap_Docking_Coordinates()
        {
            using (FloatingForm f = new FloatingForm())
            {
                IntPtr h = f.Handle;
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;

                f.SnapTo("top-left", "");
                Assert.True(f.Left >= wa.Left, "top-left X within screen");
                Assert.True(f.Top >= wa.Top, "top-left Y within screen");

                f.SnapTo("top-right", "");
                Assert.True(f.Right <= wa.Right, "top-right X within screen");
                Assert.True(f.Top >= wa.Top, "top-right Y within screen");

                f.SnapTo("bottom-left", "");
                Assert.True(f.Left >= wa.Left, "bottom-left X within screen");
                Assert.True(f.Bottom <= wa.Bottom, "bottom-left Y within screen");

                f.SnapTo("bottom-right", "");
                Assert.True(f.Right <= wa.Right, "bottom-right X within screen");
                Assert.True(f.Bottom <= wa.Bottom, "bottom-right Y within screen");
            }
        }

        public static void Test_Companion_Extension_Manifest()
        {
            string paths, error;
            string testRoot = Path.Combine(Path.GetTempPath(), "cuw_comp_test_" + Guid.NewGuid().ToString("N"));
            string testDir = Path.Combine(testRoot, "ai-usage-watcher");
            try
            {
                bool ok = CompanionInstaller.InstallTo(testDir, out paths, out error);
                Assert.True(ok, "Companion installer should succeed: " + error);
                Assert.True(!string.IsNullOrEmpty(paths), "Installed paths should not be empty");

                string pkgFile = Path.Combine(testDir, "package.json");
                Assert.True(File.Exists(pkgFile), "package.json must be written");
                byte[] pkgBytes = File.ReadAllBytes(pkgFile);
                Assert.False(pkgBytes.Length >= 3 && pkgBytes[0] == 0xEF && pkgBytes[1] == 0xBB && pkgBytes[2] == 0xBF, "package.json must have no UTF-8 BOM");
                string json = File.ReadAllText(pkgFile);
                Assert.True(json.Contains("ai-usage-watcher"), "Package json must contain extension name");
                Assert.True(json.Contains("aiUsageWatcher.openStatus"), "Package json must contain openStatus command");

                string extFile = Path.Combine(testDir, "extension.js");
                Assert.True(File.Exists(extFile), "extension.js must be written");
                string js = File.ReadAllText(extFile);
                Assert.True(js.Contains("createStatusBarItem"), "extension.js must create status bar item");
            }
            finally
            {
                try { if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true); } catch { }
            }
        }

        public static void Test_Setup_Wizard_Initialization_And_Config()
        {
            AppConfig cfg = new AppConfig();
            Assert.False(cfg.FirstRunCompleted, "New config should have FirstRunCompleted false");

            using (SetupWizardForm wiz = new SetupWizardForm(cfg))
            {
                IntPtr h = wiz.Handle;
                Assert.NotNull(wiz.Text, "Wizard title should not be null");
                Assert.True(wiz.Controls.Count > 0, "Wizard should have controls");
            }
        }

        public static void Test_Setup_Wizard_Layout_No_Overlap()
        {
            AppConfig cfg = new AppConfig();
            using (SetupWizardForm wiz = new SetupWizardForm(cfg))
            {
                wiz.StartPosition = FormStartPosition.Manual;
                wiz.Location = new Point(-3000, -3000);
                wiz.Show();
                Application.DoEvents();

                Assert.True(wiz.HeaderPanel.Height > 0, "Header height must be positive");
                Assert.True(wiz.BodyPanel.Height > 0, "Body height must be positive");
                Assert.True(wiz.FooterPanel.Height > 0, "Footer height must be positive");

                // Header and Body must never overlap!
                Assert.True(wiz.HeaderPanel.Bottom <= wiz.BodyPanel.Top,
                    string.Format("Header bottom ({0}) must be <= Body top ({1})", wiz.HeaderPanel.Bottom, wiz.BodyPanel.Top));

                // Body and Footer must never overlap!
                Assert.True(wiz.BodyPanel.Bottom <= wiz.FooterPanel.Top,
                    string.Format("Body bottom ({0}) must be <= Footer top ({1})", wiz.BodyPanel.Bottom, wiz.FooterPanel.Top));

                // UseMnemonic must be false on buttons to preserve '&' in 'Finish & Start!'
                Assert.False(wiz.NextButton.UseMnemonic, "NextButton.UseMnemonic must be false");

                for (int step = 0; step < 3; step++)
                {
                    wiz.GoToStep(step);
                    Application.DoEvents();

                    if (step == 2)
                    {
                        Assert.True(wiz.NextButton.Text.Contains("&"), "Step 3 finish button text must contain '&'");
                    }

                    using (Bitmap bmp = new Bitmap(wiz.Width, wiz.Height))
                    {
                        wiz.DrawToBitmap(bmp, new Rectangle(0, 0, wiz.Width, wiz.Height));
                    }
                }
                wiz.Close();
            }
        }

        public static void Test_SettingsForm_Apply_Button_And_LiveTheme()
        {
            AppConfig cfg = new AppConfig();
            bool applied = false;
            SettingsCallbacks cb = new SettingsCallbacks();
            cb.Apply = delegate { applied = true; };

            using (SettingsForm sf = new SettingsForm(cfg, cb))
            {
                IntPtr h = sf.Handle;
                Assert.NotNull(sf.Text, "Settings form title not null");

                MethodInfo mi = typeof(SettingsForm).GetMethod("OnApply", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.NotNull(mi, "OnApply method must exist in SettingsForm");
                mi.Invoke(sf, new object[] { null, EventArgs.Empty });

                Assert.True(applied, "cb.Apply must be invoked when OnApply runs");
            }
        }

        public static void Test_AutoUpdater_Version_Parsing()
        {
            Version v1 = AutoUpdater.ParseVersion("v1.0.1");
            Assert.NotNull(v1, "v1.0.1 must parse");
            Assert.Equal(1, v1.Major);
            Assert.Equal(0, v1.Minor);
            Assert.Equal(1, v1.Build);

            Version v2 = AutoUpdater.ParseVersion("2.5.3.4");
            Assert.NotNull(v2, "2.5.3.4 must parse");
            Assert.Equal(2, v2.Major);
            Assert.Equal(5, v2.Minor);
            Assert.Equal(3, v2.Build);
            Assert.Equal(4, v2.Revision);

            Version v3 = AutoUpdater.ParseVersion("v3.0");
            Assert.NotNull(v3, "v3.0 must parse");
            Assert.Equal(3, v3.Major);
            Assert.Equal(0, v3.Minor);

            Assert.True(AutoUpdater.ParseVersion(null) == null, "null returns null");
            Assert.True(AutoUpdater.ParseVersion("not-a-version") == null, "invalid returns null");
        }

        public static void Test_AutoUpdater_ReleaseJson_Parsing()
        {
            string json = "{" +
                "\"tag_name\": \"v1.1.0\"," +
                "\"name\": \"v1.1.0 Feature Release\"," +
                "\"body\": \"New auto-update feature!\"," +
                "\"html_url\": \"https://github.com/silvkn1/ai-usage-watcher/releases/tag/v1.1.0\"," +
                "\"assets\": [" +
                    "{\"name\": \"AIUsageWatcher-1.1.0.0.zip\", \"browser_download_url\": \"https://github.com/silvkn1/ai-usage-watcher/releases/download/v1.1.0/AIUsageWatcher-1.1.0.0.zip\", \"size\": 500000}," +
                    "{\"name\": \"AIUsageWatcher.exe\", \"browser_download_url\": \"https://github.com/silvkn1/ai-usage-watcher/releases/download/v1.1.0/AIUsageWatcher.exe\", \"size\": 380000}" +
                "]" +
            "}";

            // When current version is 1.0.0.0, 1.1.0 is newer
            ReleaseInfo info = AutoUpdater.ParseReleaseJson(json, new Version(1, 0, 0, 0));
            Assert.NotNull(info, "ParseReleaseJson should succeed");
            Assert.True(info.IsNewer, "v1.1.0 must be recognized as newer than 1.0.0.0");
            Assert.Equal("v1.1.0", info.TagName);
            Assert.Equal("AIUsageWatcher.exe", info.AssetName);
            Assert.Equal("https://github.com/silvkn1/ai-usage-watcher/releases/download/v1.1.0/AIUsageWatcher.exe", info.AssetDownloadUrl);
            Assert.Equal(380000L, info.AssetSize);

            // When current version is 1.2.0.0, 1.1.0 is NOT newer
            ReleaseInfo infoOld = AutoUpdater.ParseReleaseJson(json, new Version(1, 2, 0, 0));
            Assert.NotNull(infoOld, "ParseReleaseJson should succeed");
            Assert.False(infoOld.IsNewer, "v1.1.0 must NOT be recognized as newer than 1.2.0.0");

            // When only zip asset exists
            string jsonZipOnly = "{" +
                "\"tag_name\": \"v2.0.0\"," +
                "\"name\": \"v2.0.0\"," +
                "\"assets\": [" +
                    "{\"name\": \"AIUsageWatcher-2.0.0.0.zip\", \"browser_download_url\": \"https://example.com/asset.zip\", \"size\": 450000}" +
                "]" +
            "}";
            ReleaseInfo infoZip = AutoUpdater.ParseReleaseJson(jsonZipOnly, new Version(1, 0, 0, 0));
            Assert.NotNull(infoZip);
            Assert.True(infoZip.IsNewer);
            Assert.Equal("AIUsageWatcher-2.0.0.0.zip", infoZip.AssetName);
        }

        public static void Test_AutoUpdater_Executable_Validation()
        {
            string currentExe = typeof(AutoUpdater).Assembly.Location;
            if (File.Exists(currentExe))
            {
                Assert.True(AutoUpdater.IsValidExecutable(currentExe), "Current assembly should be valid PE executable");
            }

            Assert.False(AutoUpdater.IsValidExecutable("non-existent-path-12345.exe"), "Non existent file is not valid");

            string fakeFile = Path.Combine(Path.GetTempPath(), "fake_exe_" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(fakeFile, "Hello world, this is a plain text file, not a PE executable!");
            try
            {
                Assert.False(AutoUpdater.IsValidExecutable(fakeFile), "Text file must not be validated as executable");
            }
            finally
            {
                try { File.Delete(fakeFile); } catch { }
            }
        }

        public static void Test_AutoUpdater_Config_Persistence()
        {
            AppConfig cfg = new AppConfig();
            Assert.True(cfg.AutoUpdateCheck, "AutoUpdateCheck should default to true");

            cfg.AutoUpdateCheck = false;
            string json = J.Ser.Serialize(cfg);
            AppConfig restored = J.Ser.Deserialize<AppConfig>(json);
            Assert.False(restored.AutoUpdateCheck, "AutoUpdateCheck should deserialize as false");

            cfg.AutoUpdateCheck = true;
            string json2 = J.Ser.Serialize(cfg);
            AppConfig restored2 = J.Ser.Deserialize<AppConfig>(json2);
            Assert.True(restored2.AutoUpdateCheck, "AutoUpdateCheck should deserialize as true");
        }

        public static void Test_AutoUpdater_Cleanup_Old_Versions()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "au_clean_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string targetExe = Path.Combine(tempDir, "AIUsageWatcher.exe");
                File.WriteAllText(targetExe, "MZ_dummy_target");

                string oldExe = targetExe + ".old";
                File.WriteAllText(oldExe, "MZ_dummy_old");

                string oldGuidExe = targetExe + ".old-" + Guid.NewGuid().ToString("N");
                File.WriteAllText(oldGuidExe, "MZ_dummy_old_guid");

                string tmpFile = Path.Combine(tempDir, "update-test.tmp");
                File.WriteAllText(tmpFile, "dummy_tmp");

                Assert.True(File.Exists(oldExe), "oldExe exists before cleanup");
                Assert.True(File.Exists(oldGuidExe), "oldGuidExe exists before cleanup");
                Assert.True(File.Exists(tmpFile), "tmpFile exists before cleanup");

                AutoUpdater.CleanupOldVersion(targetExe);

                Assert.True(File.Exists(targetExe), "targetExe must be preserved");
                Assert.False(File.Exists(oldExe), "oldExe should be cleaned up");
                Assert.False(File.Exists(oldGuidExe), "oldGuidExe should be cleaned up");
                Assert.False(File.Exists(tmpFile), "tmpFile should be cleaned up");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        public static void Test_WebhookUrl_DPAPI_Encryption()
        {
            AppConfig cfg = new AppConfig();
            string secretUrl = "https://discord.com/api/webhooks/123456/secret-token";
            cfg.SetWebhookUrl(secretUrl);

            Assert.True(string.IsNullOrEmpty(cfg.WebhookUrl), "Plaintext WebhookUrl property must be cleared");
            Assert.False(string.IsNullOrEmpty(cfg.WebhookUrlProtected), "WebhookUrlProtected must hold DPAPI ciphertext");
            Assert.False(cfg.WebhookUrlProtected.Contains("discord.com"), "Ciphertext must not expose raw URL");
            Assert.Equal(secretUrl, cfg.GetWebhookUrl(), "GetWebhookUrl must decrypt back to original URL");

            // Legacy plaintext migration path
            AppConfig legacy = new AppConfig();
            legacy.WebhookUrl = "https://hooks.slack.com/services/A/B/C";
            Assert.Equal("https://hooks.slack.com/services/A/B/C", legacy.GetWebhookUrl());
        }

        public static void Test_UsageScanner_Claude_Mentions_PlannerResponse()
        {
            Pricing pricing = Pricing.Load();
            UsageScanner sc = new UsageScanner(pricing);
            sc.ScanClaude = true;
            sc.ScanGemini = true;

            string tempDir = Path.Combine(Path.GetTempPath(), "cuw_test_pr_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string jsonl = Path.Combine(tempDir, "claude_quote.jsonl");
                string todayUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                string line = string.Format(
                    "{{\"type\":\"assistant\",\"timestamp\":\"{0}\",\"requestId\":\"req_pr\",\"message\":{{\"id\":\"msg_pr\",\"model\":\"claude-opus-4-6\",\"content\":[{{\"type\":\"text\",\"text\":\"Checking \\\"PLANNER_RESPONSE\\\" parser\"}}],\"usage\":{{\"input_tokens\":2500,\"output_tokens\":300,\"cache_read_input_tokens\":1000,\"cache_creation_input_tokens\":0}}}}}}",
                    todayUtc
                );
                File.WriteAllLines(jsonl, new[] { line });
                sc.Roots = delegate { return new List<string> { tempDir }; };
                sc.Scan();

                UsageSnapshot snap = sc.Snapshot();
                Assert.Equal(1L, snap.ClaudeToday.Requests, "Claude record quoting PLANNER_RESPONSE must still be counted");
                Assert.Equal(2500L, snap.ClaudeToday.Input);
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        public static void Test_AutoUpdater_Sha256_Digest_Parsing()
        {
            string json = "{" +
                "\"tag_name\": \"v1.2.0\"," +
                "\"name\": \"v1.2.0\"," +
                "\"assets\": [" +
                    "{\"name\": \"AIUsageWatcher.exe\", \"browser_download_url\": \"https://example.com/AIUsageWatcher.exe\", \"size\": 400000, \"digest\": \"sha256:ABCDEF123456\"}," +
                    "{\"name\": \"SHA256SUMS.txt\", \"browser_download_url\": \"https://example.com/SHA256SUMS.txt\", \"size\": 180}" +
                "]" +
            "}";
            ReleaseInfo info = AutoUpdater.ParseReleaseJson(json, new Version(1, 0, 0, 0));
            Assert.NotNull(info);
            Assert.Equal("abcdef123456", info.ExpectedSha256, "ExpectedSha256 must strip sha256: prefix and normalize lowercase");
            Assert.Equal("https://example.com/SHA256SUMS.txt", info.ChecksumsUrl);

            string tmp = Path.Combine(Path.GetTempPath(), "sha_test_" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                File.WriteAllText(tmp, "abc");
                string sha = AutoUpdater.ComputeFileSha256(tmp);
                Assert.Equal(64, sha.Length, "SHA-256 hex digest must be 64 characters");
            }
            finally
            {
                try { File.Delete(tmp); } catch { }
            }
        }

        public static void Test_TrayApp_Menu_Grouping()
        {
            using (TrayApp app = new TrayApp())
            {
                FieldInfo fiMenu = typeof(TrayApp).GetField("_menu", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.NotNull(fiMenu, "_menu field must exist");
                ContextMenuStrip menu = fiMenu.GetValue(app) as ContextMenuStrip;
                Assert.NotNull(menu, "_menu must not be null");

                FieldInfo fiView = typeof(TrayApp).GetField("_miViewGroup", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.NotNull(fiView, "_miViewGroup field must exist");
                ToolStripMenuItem miView = fiView.GetValue(app) as ToolStripMenuItem;
                Assert.NotNull(miView, "_miViewGroup must not be null");
                Assert.True(miView.DropDownItems.Count >= 5, "View group should contain view & layout items");

                FieldInfo fiTools = typeof(TrayApp).GetField("_miToolsGroup", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.NotNull(fiTools, "_miToolsGroup field must exist");
                ToolStripMenuItem miTools = fiTools.GetValue(app) as ToolStripMenuItem;
                Assert.NotNull(miTools, "_miToolsGroup must not be null");
                Assert.True(miTools.DropDownItems.Count >= 5, "Tools group should contain report & tool items");

                FieldInfo fiHelp = typeof(TrayApp).GetField("_miHelpGroup", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.NotNull(fiHelp, "_miHelpGroup field must exist");
                ToolStripMenuItem miHelp = fiHelp.GetValue(app) as ToolStripMenuItem;
                Assert.NotNull(miHelp, "_miHelpGroup must not be null");
                Assert.True(miHelp.DropDownItems.Count >= 2, "Help group should contain update and wizard");

                Assert.True(menu.Items.Count <= 15, "Top-level menu must be compact and grouped (<= 15 items)");
            }
        }

        public static void Test_Webhook_MultiFormat_Payload()
        {
            string discord = WebhookSender.BuildPayload("https://discord.com/api/webhooks/123/tok", "Alert", "Body");
            Assert.True(discord.Contains("\"content\""), "Discord payload must contain content field");
            Assert.True(discord.Contains("\"username\""), "Discord payload must contain username field");

            string slack = WebhookSender.BuildPayload("https://hooks.slack.com/services/T/B/X", "Alert", "Body");
            Assert.True(slack.Contains("\"text\""), "Slack payload must contain text field");
            Assert.False(slack.Contains("\"content\""), "Slack payload must not emit Discord content field");

            string telegram = WebhookSender.BuildPayload("https://api.telegram.org/bot123/sendMessage?chat_id=1", "Alert", "Body");
            Assert.True(telegram.Contains("\"text\""), "Telegram payload must contain text field");

            string generic = WebhookSender.BuildPayload("https://example.com/webhook", "Alert", "Body");
            Assert.True(generic.Contains("\"text\"") && generic.Contains("\"content\""), "Generic webhook payload must include text and content");
        }

        public static void Test_UsageScanner_Codex_Log_Parsing()
        {
            Pricing pricing = Pricing.Load();
            UsageScanner sc = new UsageScanner(pricing);
            sc.TrackProjects = true;

            string tempDir = Path.Combine(Path.GetTempPath(), "cuw_test_codex_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string nowUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                string metaLine = string.Format(
                    "{{\"timestamp\":\"{0}\",\"type\":\"session_meta\",\"payload\":{{\"id\":\"codex_1\",\"cwd\":\"C:\\\\CodexProj\"}}}}",
                    nowUtc
                );
                string ctxLine = string.Format(
                    "{{\"timestamp\":\"{0}\",\"type\":\"turn_context\",\"payload\":{{\"model\":\"gpt-5.3-codex\",\"cwd\":\"C:\\\\CodexProj\"}}}}",
                    nowUtc
                );
                string tokLine = string.Format(
                    "{{\"timestamp\":\"{0}\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"token_count\",\"info\":{{\"total_token_usage\":{{\"input_tokens\":5000,\"cached_input_tokens\":1000,\"output_tokens\":500}},\"last_token_usage\":{{\"input_tokens\":1000,\"cached_input_tokens\":200,\"output_tokens\":300}}}}}}}}",
                    nowUtc
                );
                string claudeLine = string.Format(
                    "{{\"type\":\"assistant\",\"timestamp\":\"{0}\",\"cwd\":\"C:\\\\ClaudeProj\",\"message\":{{\"id\":\"msg_cdx_1\",\"model\":\"claude-3-5-haiku\",\"usage\":{{\"input_tokens\":100,\"output_tokens\":50,\"cache_read_input_tokens\":0}}}}}}",
                    nowUtc
                );

                File.WriteAllLines(Path.Combine(tempDir, "rollout-2026-test.jsonl"), new[] { metaLine, ctxLine, tokLine });
                File.WriteAllLines(Path.Combine(tempDir, "claude.jsonl"), new[] { claudeLine });

                sc.Roots = delegate { return new List<string> { tempDir }; };
                sc.Scan();

                UsageSnapshot snap = sc.Snapshot();
                Assert.Equal(800L, snap.CodexToday.Input, "Codex uncached input must equal input_tokens - cached_input_tokens");
                Assert.Equal(200L, snap.CodexToday.CacheRead, "Codex cache_read must record cached_input_tokens");
                Assert.Equal(300L, snap.CodexToday.Output, "Codex output must record last_token_usage output_tokens");
                Assert.Equal(1300L, snap.CodexToday.Total, "Codex must sum last_token_usage, not total_token_usage");
                Assert.True(snap.CodexToday.Cost > 0, "Codex gpt-5.3-codex cost must be positive");
                Assert.True(snap.WeekProjects.ContainsKey("CodexProj"), "CodexProj must appear in WeekProjects");
                Assert.True(snap.WeekProjects.ContainsKey("ClaudeProj"), "ClaudeProj must appear in WeekProjects when ScanClaude=true");

                sc.ScanClaude = false;
                UsageSnapshot snapNoClaude = sc.Snapshot();
                Assert.False(snapNoClaude.WeekProjects.ContainsKey("ClaudeProj"), "Disabling ScanClaude must immediately exclude Claude projects from Snapshot");
                Assert.True(snapNoClaude.WeekProjects.ContainsKey("CodexProj"), "CodexProj must remain when ScanClaude is disabled");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        public static void Test_ProcessHelper_Terminal_And_VSCode_Discovery()
        {
            string cmdPath = ProcessHelper.FindExecutableOnPath("cmd.exe");
            Assert.NotNull(cmdPath, "cmd.exe must be resolved on PATH");
            Assert.True(File.Exists(cmdPath), "Resolved cmd.exe path must exist on disk");
            Assert.True(ProcessHelper.FindExecutableOnPath("definitely_missing_bin_xyz_9876.exe") == null, "Missing executable must return null");
            bool hasEditor = ProcessHelper.HasVSCode();
            Assert.True(hasEditor || !hasEditor, "HasVSCode must execute safely without throwing");
        }

        public static void Test_SettingsForm_Live_I18n_Refresh_And_Wizard_Codex()
        {
            string origLang = L.Lang;
            try
            {
                L.Set("en");
                AppConfig cfg = new AppConfig();
                cfg.Language = "en";
                using (SettingsForm sf = new SettingsForm(cfg, null))
                {
                    IntPtr h = sf.Handle;
                    string enTitle = sf.Text;
                    Assert.Equal(L.T("set.title"), enTitle, "Initial Settings title must be English");

                    L.Set("id");
                    sf.RefreshI18n();
                    Assert.Equal(L.T("set.title"), sf.Text, "Settings title must update live to Indonesian");
                    Assert.True(sf.Text != enTitle, "Indonesian Settings title must differ from English");

                    FieldInfo fiTheme = typeof(SettingsForm).GetField("_theme", BindingFlags.NonPublic | BindingFlags.Instance);
                    ComboBox cboTheme = fiTheme.GetValue(sf) as ComboBox;
                    Assert.NotNull(cboTheme, "_theme ComboBox must exist");
                    Assert.Equal(L.T("set.theme.dark"), cboTheme.Items[0].ToString(), "Theme dropdown items must re-translate live");
                    Assert.True(cboTheme.SelectedIndex >= 0, "ComboBox SelectedIndex must be preserved on i18n refresh");

                    FieldInfo fiOpacityLbl = typeof(SettingsForm).GetField("_opacityLbl", BindingFlags.NonPublic | BindingFlags.Instance);
                    Label lblOp = fiOpacityLbl.GetValue(sf) as Label;
                    Assert.NotNull(lblOp, "_opacityLbl must exist");
                    Assert.Equal(L.T("set.opacity", cfg.OpacityPercent), lblOp.Text, "Parameterized _opacityLbl must re-translate live");
                }

                using (SetupWizardForm wiz = new SetupWizardForm(new AppConfig()))
                {
                    IntPtr wh = wiz.Handle;
                    FieldInfo fiCodex = typeof(SetupWizardForm).GetField("_chkCodex", BindingFlags.NonPublic | BindingFlags.Instance);
                    FieldInfo fiPort = typeof(SetupWizardForm).GetField("_numOllamaPort", BindingFlags.NonPublic | BindingFlags.Instance);
                    Assert.NotNull(fiCodex != null ? fiCodex.GetValue(wiz) : null, "SetupWizard must expose Codex checkbox");
                    NumericUpDown numPort = fiPort != null ? fiPort.GetValue(wiz) as NumericUpDown : null;
                    Assert.NotNull(numPort, "SetupWizard must expose Ollama port control");
                    Assert.Equal(65535m, numPort.Maximum, "Ollama port NumericUpDown max must be 65535");
                }
            }
            finally
            {
                L.Set(origLang);
            }
        }

        public static void Test_Codex_RateLimits_And_TrayText_And_Alerts()
        {
            Pricing pricing = Pricing.Load();
            UsageScanner sc = new UsageScanner(pricing);
            string tempDir = Path.Combine(Path.GetTempPath(), "cuw_test_cxrl_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string nowUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                long resetUnix = (long)(DateTime.UtcNow.AddHours(3) - new DateTime(1970, 1, 1)).TotalSeconds;
                string tokLine = string.Format(
                    "{{\"timestamp\":\"{0}\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"token_count\",\"rate_limits\":{{\"primary\":{{\"used_percent\":42,\"resets_at\":{1}}},\"secondary\":{{\"used_percent\":85,\"resets_at\":{1}}}}},\"info\":{{\"last_token_usage\":{{\"input_tokens\":500,\"cached_input_tokens\":100,\"output_tokens\":200}}}}}}}}",
                    nowUtc, resetUnix
                );
                File.WriteAllLines(Path.Combine(tempDir, "rollout-rl.jsonl"), new[] { tokLine });
                sc.Roots = delegate { return new List<string> { tempDir }; };
                sc.Scan();
                UsageSnapshot snap = sc.Snapshot();

                Assert.True(snap.CodexFiveHour.Available, "Codex 5h limit must be parsed");
                Assert.Equal(42.0, snap.CodexFiveHour.UsedPercent, "Codex 5h percent must match primary.used_percent");
                Assert.True(snap.CodexSevenDay.Available, "Codex 7d limit must be parsed");
                Assert.Equal(85.0, snap.CodexSevenDay.UsedPercent, "Codex 7d percent must match secondary.used_percent");

                RateLimitSnapshot rl = new RateLimitSnapshot();
                rl.CodexFiveHour = snap.CodexFiveHour;
                rl.CodexSevenDay = snap.CodexSevenDay;

                string iconTxt; Color badge; bool weekly;
                bool prevRem = Opts.ShowRemaining;
                Opts.ShowRemaining = false;
                try
                {
                    TrayText.IconSpec(rl, false, false, true, out iconTxt, out badge, out weekly);
                    Assert.Equal("42", iconTxt, "Codex-only tray icon must show 5h percent");
                    string tip = TrayText.Tooltip(rl, snap, null, 127, false, false, true);
                    Assert.True(tip.Contains("Codex CLI:"), "Codex-only tray tooltip must display Codex header");
                    Assert.False(tip.Contains(L.T("tip.noproviders")), "Codex-only tooltip must not report no active providers");
                }
                finally { Opts.ShowRemaining = prevRem; }

                AlertEngine ae = new AlertEngine();
                rl.CodexFiveHour.UsedPercent = 10;
                ae.Evaluate(rl, new[] { 80, 95 }, true); // baseline
                rl.CodexFiveHour.UsedPercent = 82;
                List<Alert> alerts = ae.Evaluate(rl, new[] { 80, 95 }, true);
                Assert.True(alerts.Count == 1 && alerts[0].Window == "codex_five_hour", "AlertEngine must fire on codex_five_hour threshold");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        public static void Test_ProjectExplorer_Provider_Sort_And_Summary()
        {
            UsageSnapshot snap = new UsageSnapshot();
            snap.WeekProjects["AlphaApp"] = new Tally { Cost = 1.25, Input = 50000, Requests = 10 };
            snap.TodayProjects["AlphaApp"] = new Tally { Cost = 0.25, Input = 10000, Requests = 2 };
            snap.ProjectProviders["AlphaApp"] = "Claude";
            snap.WeekProjects["BetaCodex"] = new Tally { Cost = 2.50, Input = 90000, Requests = 20 };
            snap.TodayProjects["BetaCodex"] = new Tally { Cost = 0.50, Input = 20000, Requests = 5 };
            snap.ProjectProviders["BetaCodex"] = "Codex";

            using (ProjectExplorerForm form = new ProjectExplorerForm(snap, null))
            {
                IntPtr h = form.Handle;
                FieldInfo fiList = typeof(ProjectExplorerForm).GetField("_list", BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo fiSearch = typeof(ProjectExplorerForm).GetField("_searchBox", BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo fiSummary = typeof(ProjectExplorerForm).GetField("_summaryLabel", BindingFlags.NonPublic | BindingFlags.Instance);

                ListView lv = fiList.GetValue(form) as ListView;
                TextBox search = fiSearch.GetValue(form) as TextBox;
                Label summary = fiSummary.GetValue(form) as Label;

                Assert.NotNull(lv, "_list must exist");
                Assert.Equal(7, lv.Columns.Count, "Explorer ListView must have 7 columns including Provider");
                Assert.Equal(3, lv.Items.Count, "Initial list must contain both projects plus Grand Total row");
                Assert.Equal(L.T("proj.grandtotal"), lv.Items[2].Text, "Last row must be Grand Total");
                Assert.True(lv.Items[2].Tag == null, "Grand Total row must have null Tag");
                Assert.Equal(Fmt.Tokens(140000), lv.Items[2].SubItems[4].Text, "Grand Total row must sum WeekTokens");
                Assert.Equal("30", lv.Items[2].SubItems[5].Text, "Grand Total row must sum WeekRequests");
                Assert.True(lv.Columns[3].Text.Contains("▼"), "Default sorted column header must show descending arrow");
                Assert.True(summary.Text.Contains("2"), "Summary label must show 2 projects");

                search.Text = "Beta";
                Assert.Equal(2, lv.Items.Count, "Search filter must narrow list to BetaCodex plus Grand Total row");
                Assert.Equal("Codex", lv.Items[0].SubItems[1].Text, "Filtered row must preserve Codex provider");
                Assert.Equal(Fmt.Tokens(90000), lv.Items[1].SubItems[4].Text, "Filtered Grand Total row must sum only visible rows");
                Assert.True(summary.Text.Contains("1"), "Summary label must update dynamically on filter");
                Assert.True(ProjectExplorerForm.BuildMarkdownTable(new List<ProjectRow> {
                    new ProjectRow { Name = "A", TodayCost = 0.45, WeekCost = 3.12, WeekTokens = 450000, WeekRequests = 82 },
                    new ProjectRow { Name = "B", TodayCost = 0.10, WeekCost = 0.50, WeekTokens = 80000, WeekRequests = 15 }
                }).Contains("| **Grand Total** | $0.55 | $3.62 |"), "Markdown table must include Grand Total row");
            }
        }

        public static void Test_FloatingForm_CodexOnly_Card_And_Pill()
        {
            UsageSnapshot snap = new UsageSnapshot();
            snap.Ready = true;
            snap.CodexToday = new Tally { Input = 1200, Output = 300, Cost = 0.05, Requests = 1 };
            snap.CodexWeek = new Tally { Input = 1200, Output = 300, Cost = 0.05, Requests = 1 };
            snap.TodayTotal = snap.CodexToday;
            snap.WeekTotal = snap.CodexWeek;

            RateLimitSnapshot rl = new RateLimitSnapshot();
            rl.CodexFiveHour = new LimitWindow { Available = true, UsedPercent = 30 };
            rl.CodexSevenDay = new LimitWindow { Available = true, UsedPercent = 12 };

            ViewModel vm = new ViewModel();
            vm.Usage = snap;
            vm.Limits = rl;
            vm.ScanClaude = false;
            vm.ScanGemini = false;
            vm.ScanCodex = true;

            using (FloatingForm f = new FloatingForm())
            {
                IntPtr h = f.Handle;
                f.SetData(vm);
                MethodInfo buildRows = typeof(FloatingForm).GetMethod("BuildRows", BindingFlags.NonPublic | BindingFlags.Instance);
                System.Collections.IList fullRows = buildRows.Invoke(f, null) as System.Collections.IList;
                Assert.NotNull(fullRows);
                bool foundCodexCard = false;
                foreach (object r in fullRows)
                {
                    FieldInfo fiLeft = r.GetType().GetField("LeftCol");
                    object lc = fiLeft != null ? fiLeft.GetValue(r) : null;
                    if (lc != null)
                    {
                        FieldInfo fiTitle = lc.GetType().GetField("Title");
                        if (fiTitle != null && "Codex CLI".Equals(fiTitle.GetValue(lc))) foundCodexCard = true;
                    }
                }
                Assert.True(foundCodexCard, "Codex-only full mode must promote Codex CLI into card layout");

                f.Compact = true;
                System.Collections.IList pillRows = buildRows.Invoke(f, null) as System.Collections.IList;
                Assert.Equal(1, pillRows.Count, "Compact Codex-only mode must render single Pill row");
            }
        }

        public static void Test_Diag_Includes_Providers_And_Codex()
        {
            AppConfig cfg = new AppConfig();
            cfg.ScanClaude = false;
            cfg.ScanCodex = true;
            cfg.OllamaEnabled = true;
            cfg.OllamaPort = 11434;
            RateLimitSnapshot rl = new RateLimitSnapshot();
            rl.CodexFiveHour = new LimitWindow { Available = true, UsedPercent = 25 };

            string report = Diag.Build(cfg, rl, new UsageSnapshot(), null, null, 3);
            Assert.True(report.Contains("providers: claude=no"), "Diagnostics must report provider states");
            Assert.True(report.Contains("codex=yes"), "Diagnostics must report ScanCodex state");
            Assert.True(report.Contains("codex 5h: 25%"), "Diagnostics must report Codex 5h limit window");
        }

        public static void Test_MiniWidget_Background_Progress_Render()
        {
            using (FloatingForm f = new FloatingForm())
            {
                IntPtr h = f.Handle;
                ViewModel vm = new ViewModel();
                vm.Usage = new UsageSnapshot { Ready = true };
                vm.Usage.TodayTotal.Cost = 1.00;
                vm.BudgetDaily = 2.00;
                vm.Limits = new RateLimitSnapshot();
                vm.Limits.FiveHour = new LimitWindow { Available = true, UsedPercent = 0 };
                vm.Limits.AntigravityFiveHour = new LimitWindow { Available = true, UsedPercent = 0 };

                f.Compact = true;
                f.SetData(vm);

                MethodInfo onPaint = typeof(FloatingForm).GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic);
                Color emptyPixel;
                using (Bitmap b0 = new Bitmap(f.Width, f.Height))
                {
                    using (Graphics g = Graphics.FromImage(b0))
                        onPaint.Invoke(f, new object[] { new PaintEventArgs(g, new Rectangle(0, 0, f.Width, f.Height)) });
                    emptyPixel = b0.GetPixel(10, f.Height - 2);
                }

                vm.Limits.FiveHour.UsedPercent = 85;
                f.SetData(vm);
                Color activePixel;
                using (Bitmap b1 = new Bitmap(f.Width, f.Height))
                {
                    using (Graphics g = Graphics.FromImage(b1))
                        onPaint.Invoke(f, new object[] { new PaintEventArgs(g, new Rectangle(0, 0, f.Width, f.Height)) });
                    activePixel = b1.GetPixel(10, f.Height - 2);
                }
                Assert.True(emptyPixel.ToArgb() != activePixel.ToArgb(), "Mini widget bottom progress stripe/background must visually render when usage > 0%");
            }
        }

        public static void Test_Session_Running_Done_And_Acknowledge_Lifecycle()
        {
            Pricing pricing = Pricing.Load();
            UsageScanner sc = new UsageScanner(pricing);
            sc.TrackProjects = true;

            string tempDir = Path.Combine(Path.GetTempPath(), "cuw_test_sess_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                sc.Roots = delegate { return new List<string> { tempDir }; };
                sc.Scan(); // _ready = true

                string claudeFile = Path.Combine(tempDir, "sess-abc-123.jsonl");
                string t1 = DateTime.UtcNow.AddSeconds(-5).ToString("yyyy-MM-ddTHH:mm:ssZ");
                string userLine = string.Format(
                    "{{\"type\":\"user\",\"timestamp\":\"{0}\",\"cwd\":\"C:\\\\MyApp\",\"message\":{{\"content\":\"hello\"}}}}",
                    t1
                );
                File.WriteAllLines(claudeFile, new[] { userLine });
                sc.Scan();
                UsageSnapshot s1 = sc.Snapshot();
                Assert.Equal(1, s1.Sessions.RunningCount, "User prompt must mark Claude session as running");
                Assert.True(s1.Sessions.ClaudeRunning, "ClaudeRunning flag must be true");
                Assert.Equal(0, s1.Sessions.DoneCount, "No done sessions expected yet");

                string t2 = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                string doneLine = string.Format(
                    "{{\"type\":\"assistant\",\"timestamp\":\"{0}\",\"requestId\":\"r1\",\"cwd\":\"C:\\\\MyApp\",\"message\":{{\"id\":\"m1\",\"model\":\"claude-opus-4-6\",\"stop_reason\":\"end_turn\",\"usage\":{{\"input_tokens\":500,\"output_tokens\":200,\"cache_read_input_tokens\":0}}}}}}",
                    t2
                );
                File.WriteAllLines(claudeFile, new[] { userLine, doneLine });
                sc.Scan();
                UsageSnapshot s2 = sc.Snapshot();
                Assert.Equal(0, s2.Sessions.RunningCount, "end_turn must clear running state");
                Assert.Equal(1, s2.Sessions.DoneCount, "Completed turn must mark session as done to check");
                Assert.True(s2.Sessions.ClaudeDone, "ClaudeDone flag must be true");

                string agFile = Path.Combine(tempDir, "transcript.jsonl");
                string agTool = string.Format(
                    "{{\"type\":\"PLANNER_RESPONSE\",\"model\":\"gemini-3.8-flash\",\"created_at\":\"{0}\",\"step_index\":1,\"tool_calls\":[{{\"name\":\"read_file\"}}],\"input_tokens\":200,\"output_tokens\":50,\"cache_read_tokens\":0}}",
                    t1
                );
                File.WriteAllLines(agFile, new[] { agTool });
                sc.Scan();
                Assert.Equal(1, sc.Snapshot().Sessions.RunningCount, "Antigravity step with tool_calls must mark running");

                string agDone = string.Format(
                    "{{\"type\":\"PLANNER_RESPONSE\",\"model\":\"gemini-3.8-flash\",\"created_at\":\"{0}\",\"step_index\":2,\"tool_calls\":[],\"input_tokens\":200,\"output_tokens\":100,\"cache_read_tokens\":0}}",
                    t2
                );
                File.WriteAllLines(agFile, new[] { agTool, agDone });
                sc.Scan();
                Assert.Equal(2, sc.Snapshot().Sessions.DoneCount, "Finished Antigravity turn must add second done session");

                sc.SyncHookSessions(new[] { "hook_only_1" });
                Assert.Equal(1, sc.Snapshot().Sessions.RunningCount, "SyncHookSessions must track active hook session");
                sc.MarkHookFinished("hook_only_1");
                Assert.Equal(0, sc.Snapshot().Sessions.RunningCount, "MarkHookFinished must clear running state");
                Assert.Equal(3, sc.Snapshot().Sessions.DoneCount, "MarkHookFinished must add done session to check");

                sc.AcknowledgeDoneSessions();
                Assert.Equal(0, sc.Snapshot().Sessions.DoneCount, "AcknowledgeDoneSessions must clear all done indicators");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        public static void Test_MiniWidget_Session_Badge_And_Click_Acknowledge()
        {
            using (FloatingForm f = new FloatingForm())
            {
                IntPtr h = f.Handle;
                ViewModel vm = new ViewModel();
                vm.Usage = new UsageSnapshot { Ready = true };
                vm.Usage.TodayTotal.Cost = 0.75;
                vm.Usage.Sessions.RunningCount = 1;
                vm.Usage.Sessions.ClaudeRunning = true;
                vm.Usage.Sessions.RunningLabels.Add("Claude · MyApp");
                vm.Usage.Sessions.DoneCount = 2;
                vm.Usage.Sessions.AntigravityDone = true;
                vm.Usage.Sessions.DoneLabels.Add("Antigravity");

                f.Compact = true;
                f.SetData(vm);

                PropertyInfo pDpi = typeof(FloatingForm).GetProperty("DpiScale", BindingFlags.Instance | BindingFlags.NonPublic);
                float dpi = (float)pDpi.GetValue(f, null);
                Assert.True(f.Width > (int)(225 * dpi), "Compact pill width must expand cleanly when session badges are active");

                MethodInfo onPaint = typeof(FloatingForm).GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic);
                using (Bitmap bmp = new Bitmap(f.Width, f.Height))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    onPaint.Invoke(f, new object[] { new PaintEventArgs(g, new Rectangle(0, 0, f.Width, f.Height)) });
                }

                FieldInfo fiDoneRect = typeof(FloatingForm).GetField("_pillDoneRect", BindingFlags.Instance | BindingFlags.NonPublic);
                Rectangle doneRect = (Rectangle)fiDoneRect.GetValue(f);
                Assert.False(doneRect.IsEmpty, "Mini widget must expose clickable done badge rectangle");

                bool clicked = false;
                f.DoneSessionsClicked += delegate { clicked = true; };
                MethodInfo onMouseDown = typeof(FloatingForm).GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic);
                onMouseDown.Invoke(f, new object[] { new MouseEventArgs(MouseButtons.Left, 1, doneRect.Left + doneRect.Width / 2, doneRect.Top + doneRect.Height / 2, 0) });
                Assert.True(clicked, "Clicking done session badge on mini widget must fire DoneSessionsClicked");
            }
        }

        public static void Test_Antigravity_Chunk_Exclusion_And_Confirm_Question_Indicator()
        {
            Pricing pricing = Pricing.Load();
            UsageScanner sc = new UsageScanner(pricing);
            string tempDir = Path.Combine(Path.GetTempPath(), "cuw_test_agchunks_" + Guid.NewGuid().ToString("N"));
            try
            {
                string convDir = Path.Combine(tempDir, "brain", "conv-uuid-123", ".system_generated", "logs");
                string chunkDir = Path.Combine(convDir, "chunks", "transcript");
                string chunkFullDir = Path.Combine(convDir, "chunks", "transcript_full");
                Directory.CreateDirectory(chunkDir);
                Directory.CreateDirectory(chunkFullDir);

                sc.Roots = delegate { return new List<string> { Path.Combine(tempDir, "brain") }; };
                sc.Scan(); // _ready = true

                string t1 = DateTime.UtcNow.AddSeconds(-4).ToString("yyyy-MM-ddTHH:mm:ssZ");
                string t2 = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                string runLine = string.Format(
                    "{{\"type\":\"PLANNER_RESPONSE\",\"model\":\"gemini-3.8-flash\",\"created_at\":\"{0}\",\"step_index\":1,\"tool_calls\":[{{\"name\":\"run_command\"}}],\"input_tokens\":500,\"output_tokens\":50,\"cache_read_tokens\":0}}",
                    t1
                );

                // Put same/chunked entries inside chunks dirs as well as main transcript.jsonl
                File.WriteAllLines(Path.Combine(convDir, "transcript.jsonl"), new[] { runLine });
                File.WriteAllLines(Path.Combine(chunkDir, "00000001.jsonl"), new[] { runLine });
                File.WriteAllLines(Path.Combine(chunkFullDir, "00000002.jsonl"), new[] { runLine });

                // Background observer Claude file must not inflate running session count
                string obsDir = Path.Combine(tempDir, "brain", "claude-mem-observer-sessions--1");
                Directory.CreateDirectory(obsDir);
                File.WriteAllLines(Path.Combine(obsDir, "obs1.jsonl"), new[] {
                    string.Format("{{\"type\":\"user\",\"timestamp\":\"{0}\",\"message\":{{\"content\":\"bg\"}}}}", t1)
                });

                sc.Scan();
                UsageSnapshot s1 = sc.Snapshot();
                Assert.Equal(1, s1.Sessions.RunningCount, "Chunks and observer dirs must not inflate Antigravity active session count");
                Assert.Equal(550L, s1.AntigravityToday.Total, "Chunks directory must not duplicate Antigravity token totals");

                // Antigravity ask_question triggers Confirm (?) indicator
                string askLine = string.Format(
                    "{{\"type\":\"PLANNER_RESPONSE\",\"model\":\"gemini-3.8-flash\",\"created_at\":\"{0}\",\"step_index\":2,\"tool_calls\":[{{\"name\":\"ask_question\"}}],\"input_tokens\":300,\"output_tokens\":50,\"cache_read_tokens\":0}}",
                    t2
                );
                File.WriteAllLines(Path.Combine(convDir, "transcript.jsonl"), new[] { runLine, askLine });
                sc.Scan();
                UsageSnapshot s2 = sc.Snapshot();
                Assert.Equal(0, s2.Sessions.RunningCount, "Session asking question must move out of Running");
                Assert.Equal(1, s2.Sessions.ConfirmCount, "ask_question must trigger NeedsConfirm (?) indicator");
                Assert.True(s2.Sessions.AntigravityConfirm, "AntigravityConfirm flag must be true");

                // Hook input notification also triggers Confirm (?)
                sc.MarkHookNeedsConfirm("claude_q_1");
                UsageSnapshot s3 = sc.Snapshot();
                Assert.Equal(2, s3.Sessions.ConfirmCount, "Hook input notification must trigger Claude confirmation indicator");
                Assert.True(s3.Sessions.ClaudeConfirm, "ClaudeConfirm flag must be true");

                sc.AcknowledgeDoneSessions();
                Assert.Equal(0, sc.Snapshot().Sessions.ConfirmCount, "AcknowledgeDoneSessions must clear ? confirmation badges");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        public static void Test_MiniWidget_Confirm_Question_Badge()
        {
            using (FloatingForm f = new FloatingForm())
            {
                IntPtr h = f.Handle;
                ViewModel vm = new ViewModel();
                vm.Usage = new UsageSnapshot { Ready = true };
                vm.Usage.Sessions.ConfirmCount = 1;
                vm.Usage.Sessions.AntigravityConfirm = true;
                vm.Usage.Sessions.ConfirmLabels.Add("Antigravity");

                f.Compact = true;
                f.SetData(vm);

                PropertyInfo pDpi = typeof(FloatingForm).GetProperty("DpiScale", BindingFlags.Instance | BindingFlags.NonPublic);
                float dpi = (float)pDpi.GetValue(f, null);
                Assert.True(f.Width > (int)(225 * dpi), "Mini widget must expand when ? confirmation badge is active");

                MethodInfo onPaint = typeof(FloatingForm).GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic);
                using (Bitmap bmp = new Bitmap(f.Width, f.Height))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    onPaint.Invoke(f, new object[] { new PaintEventArgs(g, new Rectangle(0, 0, f.Width, f.Height)) });
                }

                FieldInfo fiDoneRect = typeof(FloatingForm).GetField("_pillDoneRect", BindingFlags.Instance | BindingFlags.NonPublic);
                Rectangle ackRect = (Rectangle)fiDoneRect.GetValue(f);
                Assert.False(ackRect.IsEmpty, "Mini widget must expose clickable ? badge rectangle");
            }
        }

        public static void Test_FloatingForm_MultiProvider_3Col_And_Switcher()
        {
            string prevLayout = Opts.CardLayout;
            try
            {
                ViewModel vm = new ViewModel();
                vm.ScanClaude = true; vm.ScanGemini = true; vm.ScanCodex = true; vm.ScanGeminiCli = true;
                vm.Usage = new UsageSnapshot { Ready = true };
                vm.Usage.CodexWeek.Input = 100; vm.Usage.CodexToday.Input = 100;
                vm.Usage.GeminiCliWeek.Input = 100; vm.Usage.GeminiCliToday.Input = 100;
                vm.Limits = new RateLimitSnapshot();

                Opts.CardLayout = "2col";
                using (FloatingForm f = new FloatingForm())
                {
                    IntPtr h = f.Handle;
                    f.SetData(vm);
                    MethodInfo onPaint = typeof(FloatingForm).GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic);
                    using (Bitmap bmp = new Bitmap(f.Width, f.Height))
                    using (Graphics g = Graphics.FromImage(bmp))
                        onPaint.Invoke(f, new object[] { new PaintEventArgs(g, new Rectangle(0, 0, f.Width, f.Height)) });

                    FieldInfo fiSw = typeof(FloatingForm).GetField("_cardsSwitchRect", BindingFlags.Instance | BindingFlags.NonPublic);
                    Rectangle swRect = (Rectangle)fiSw.GetValue(f);
                    Assert.False(swRect.IsEmpty, "2col mode with 3+ providers must expose card page switcher rect");

                    MethodInfo onMouseDown = typeof(FloatingForm).GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic);
                    onMouseDown.Invoke(f, new object[] { new MouseEventArgs(MouseButtons.Left, 1, swRect.Left + 2, swRect.Top + 2, 0) });
                    Assert.Equal(1, f.CardPage, "Clicking card switcher badge must advance CardPage");
                }

                Opts.CardLayout = "3col";
                using (FloatingForm f3 = new FloatingForm())
                {
                    IntPtr h = f3.Handle;
                    f3.CardPage = 0;
                    f3.SetData(vm);
                    PropertyInfo pDpi = typeof(FloatingForm).GetProperty("DpiScale", BindingFlags.Instance | BindingFlags.NonPublic);
                    float dpi = (float)pDpi.GetValue(f3, null);
                    Assert.True(f3.Width >= (int)(490 * dpi), "3col mode with 3+ providers must expand width for 3 columns");
                }
            }
            finally
            {
                Opts.CardLayout = prevLayout;
            }
        }

        public static void Test_FloatingForm_ContextBar_CopyCompact_And_Terminal()
        {
            bool prevCtx = Opts.ShowContextBar;
            try
            {
                Opts.ShowContextBar = true;
                using (FloatingForm f = new FloatingForm())
                {
                    IntPtr h = f.Handle;
                    ViewModel vm = new ViewModel();
                    vm.Usage = new UsageSnapshot { Ready = true, LastContextTokens = 160000, LastActiveCwd = "d:\\dummy\\proj" };
                    f.SetData(vm);

                    MethodInfo onPaint = typeof(FloatingForm).GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic);
                    using (Bitmap bmp = new Bitmap(f.Width, f.Height))
                    using (Graphics g = Graphics.FromImage(bmp))
                        onPaint.Invoke(f, new object[] { new PaintEventArgs(g, new Rectangle(0, 0, f.Width, f.Height)) });

                    FieldInfo fiCopy = typeof(FloatingForm).GetField("_ctxCopyRect", BindingFlags.Instance | BindingFlags.NonPublic);
                    FieldInfo fiTerm = typeof(FloatingForm).GetField("_ctxTermRect", BindingFlags.Instance | BindingFlags.NonPublic);
                    Rectangle copyRect = (Rectangle)fiCopy.GetValue(f);
                    Rectangle termRect = (Rectangle)fiTerm.GetValue(f);
                    Assert.False(copyRect.IsEmpty, "Context bar must render /compact copy rect");
                    Assert.False(termRect.IsEmpty, "Context bar must render terminal jump rect when cwd set");

                    bool copyFired = false, termFired = false;
                    f.CopyCompactRequested += delegate { copyFired = true; };
                    f.OpenActiveTerminalRequested += delegate { termFired = true; };
                    MethodInfo onMouseDown = typeof(FloatingForm).GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic);
                    onMouseDown.Invoke(f, new object[] { new MouseEventArgs(MouseButtons.Left, 1, copyRect.Left + 2, copyRect.Top + 2, 0) });
                    onMouseDown.Invoke(f, new object[] { new MouseEventArgs(MouseButtons.Left, 1, termRect.Left + 2, termRect.Top + 2, 0) });
                    Assert.True(copyFired, "Clicking /compact chip must fire CopyCompactRequested");
                    Assert.True(termFired, "Clicking >_ chip must fire OpenActiveTerminalRequested");
                }
            }
            finally
            {
                Opts.ShowContextBar = prevCtx;
            }
        }

        public static void Test_FloatingForm_MiniPillModes()
        {
            string prevMode = Opts.MiniPillMode;
            try
            {
                ViewModel vm = new ViewModel();
                vm.Usage = new UsageSnapshot { Ready = true, TokensLast10m = 40000 };
                vm.Usage.TodayTotal.Cost = 1.45;
                vm.Usage.TodayTotal.Input = 25000;
                vm.BudgetDaily = 5.0;
                vm.Limits = new RateLimitSnapshot
                {
                    FiveHour = new LimitWindow { Available = true, UsedPercent = 42, ResetsAt = DateTime.Now.AddHours(3) },
                    AntigravityFiveHour = new LimitWindow { Available = true, UsedPercent = 85, ResetsAt = DateTime.Now.AddHours(2) }
                };

                foreach (string mode in new[] { "highest", "cost_runway" })
                {
                    Opts.MiniPillMode = mode;
                    using (FloatingForm f = new FloatingForm())
                    {
                        IntPtr h = f.Handle;
                        f.Compact = true;
                        f.SetData(vm);
                        Assert.True(f.Height < 39, "Mini pill height must remain compact in mode " + mode);
                        MethodInfo onPaint = typeof(FloatingForm).GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic);
                        using (Bitmap bmp = new Bitmap(f.Width, f.Height))
                        using (Graphics g = Graphics.FromImage(bmp))
                            onPaint.Invoke(f, new object[] { new PaintEventArgs(g, new Rectangle(0, 0, f.Width, f.Height)) });
                    }
                }
            }
            finally
            {
                Opts.MiniPillMode = prevMode;
            }
        }

        public static void Test_OllamaProbe_Ps_Vram_Parsing()
        {
            OllamaStatus st = OllamaProbe.ParseResponse("{\"models\":[{\"name\":\"qwen2.5-coder:7b\"},{\"name\":\"llama3.2:3b\"}]}");
            Assert.True(st.Online);
            Assert.Equal(2, st.ModelCount);
            OllamaProbe.ParsePsResponse("{\"models\":[{\"name\":\"qwen2.5-coder:7b\",\"size_vram\":4294967296}]}", st);
            Assert.Equal(1, st.LoadedCount);
            Assert.Equal(4294967296L, st.TotalVramBytes);
            string formatted = StatusFormatter.Format(new RateLimitSnapshot(), new UsageSnapshot(), false, false, false, st);
            Assert.True(formatted.IndexOf("1 loaded", StringComparison.OrdinalIgnoreCase) >= 0, "StatusFormatter must report loaded Ollama model count");
            Assert.True(formatted.IndexOf("4.0 GB VRAM", StringComparison.OrdinalIgnoreCase) >= 0, "StatusFormatter must report VRAM usage");
        }

        public static void Test_CustomPricing_Override_Priority()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "cuw_custom_price_" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(tmp, "{\"models\":[{\"prefix\":\"claude-sonnet-4\",\"input\":9.9,\"output\":19.8,\"cache_write_5m\":12.0,\"cache_write_1h\":15.0,\"cache_read\":0.9}]}");
                Pricing p = Pricing.LoadFromFiles(null, tmp);
                Assert.True(p.HasCustomPricing);
                Price found = p.Find("claude-sonnet-4-6");
                Assert.NotNull(found);
                Assert.True(Math.Abs(found.Input - 9.9) < 0.001, "Custom pricing prefix must override built-in model");
            }
            finally
            {
                try { File.Delete(tmp); } catch { }
            }
        }

        public static void Test_ScanCache_SkipUnchanged_And_AppendDelta()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "cuw_test_cache_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string cacheFile = Path.Combine(tempDir, "scan_cache.json");
                string logDir = Path.Combine(tempDir, "proj1");
                Directory.CreateDirectory(logDir);
                string logFile = Path.Combine(logDir, "sess1.jsonl");
                string ts1 = DateTime.UtcNow.AddMinutes(-45).ToString("yyyy-MM-ddTHH:mm:ssZ");
                string line1 = string.Format(
                    "{{\"type\":\"assistant\",\"timestamp\":\"{0}\",\"requestId\":\"r1\",\"cwd\":\"d:\\\\proj1\",\"message\":{{\"id\":\"m1\",\"model\":\"claude-sonnet-4-6\",\"stop_reason\":\"end_turn\",\"usage\":{{\"input_tokens\":100,\"output_tokens\":50,\"cache_read_input_tokens\":0,\"cache_creation_input_tokens\":0}}}}}}",
                    ts1);
                File.WriteAllLines(logFile, new[] { line1 });

                Pricing pr = Pricing.Load();
                UsageScanner sc1 = new UsageScanner(pr);
                sc1.CacheFilePath = cacheFile;
                sc1.ScanGeminiCli = false; sc1.ScanOpenCode = false;
                sc1.Roots = delegate { return new List<string> { tempDir }; };
                sc1.Scan();
                Assert.Equal(1, sc1.DiskReadFiles);
                Assert.Equal(0, sc1.CacheHitFiles);
                Assert.Equal(150L, sc1.Snapshot().ClaudeToday.Total);

                UsageScanner sc2 = new UsageScanner(pr);
                sc2.CacheFilePath = cacheFile;
                sc2.ScanGeminiCli = false; sc2.ScanOpenCode = false;
                sc2.Roots = delegate { return new List<string> { tempDir }; };
                sc2.Scan();
                Assert.Equal(1, sc2.CacheHitFiles, "Unchanged JSONL file on restart must hit scan cache");
                Assert.Equal(0, sc2.DiskReadFiles, "Unchanged JSONL file must skip disk read on restart");
                Assert.Equal(150L, sc2.Snapshot().ClaudeToday.Total, "Cached replay must restore exact tokens");

                string ts2 = DateTime.UtcNow.AddMinutes(-40).ToString("yyyy-MM-ddTHH:mm:ssZ");
                string line2 = string.Format(
                    "{{\"type\":\"assistant\",\"timestamp\":\"{0}\",\"requestId\":\"r2\",\"cwd\":\"d:\\\\proj1\",\"message\":{{\"id\":\"m2\",\"model\":\"claude-sonnet-4-6\",\"stop_reason\":\"end_turn\",\"usage\":{{\"input_tokens\":200,\"output_tokens\":25,\"cache_read_input_tokens\":0,\"cache_creation_input_tokens\":0}}}}}}",
                    ts2);
                File.AppendAllLines(logFile, new[] { line2 });

                UsageScanner sc3 = new UsageScanner(pr);
                sc3.CacheFilePath = cacheFile;
                sc3.ScanGeminiCli = false; sc3.ScanOpenCode = false;
                sc3.Roots = delegate { return new List<string> { tempDir }; };
                sc3.Scan();
                Assert.Equal(375L, sc3.Snapshot().ClaudeToday.Total, "Appended JSONL after cache hit must accumulate delta accurately");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        public static void Test_GeminiCli_And_OpenCode_Session_Parsers()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "cuw_test_cliext_" + Guid.NewGuid().ToString("N"));
            try
            {
                string gemChats = Path.Combine(tempDir, "gemtmp", "projhash", "chats");
                string ocMsgDir = Path.Combine(tempDir, "opencode", "storage", "message", "sess_oc1");
                Directory.CreateDirectory(gemChats);
                Directory.CreateDirectory(ocMsgDir);

                string nowIso = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                File.WriteAllText(Path.Combine(gemChats, "session-2026.json"), string.Format(
                    "{{\"sessionId\":\"g1\",\"cwd\":\"d:\\\\mygemproj\",\"messages\":[{{\"id\":\"gm1\",\"timestamp\":\"{0}\",\"type\":\"gemini\",\"model\":\"gemini-2.5-pro\",\"tokens\":{{\"input\":1000,\"output\":200,\"cached\":400,\"thoughts\":50,\"total\":1650}}}}]}}",
                    nowIso));

                long epochMs = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
                File.WriteAllText(Path.Combine(ocMsgDir, "msg_1.json"), string.Format(CultureInfo.InvariantCulture,
                    "{{\"id\":\"m_oc1\",\"sessionID\":\"sess_oc1\",\"role\":\"assistant\",\"modelID\":\"claude-sonnet-4-6\",\"path\":{{\"cwd\":\"d:\\\\myocproj\"}},\"time\":{{\"completed\":{0}}},\"tokens\":{{\"input\":500,\"output\":100,\"cache\":{{\"read\":300,\"write\":0}}}},\"cost\":0.02}}",
                    epochMs));

                UsageScanner sc = new UsageScanner(Pricing.Load());
                sc.CacheFilePath = null;
                sc.ScanClaude = false; sc.ScanGemini = false; sc.ScanCodex = false;
                sc.ScanGeminiCli = true; sc.ScanOpenCode = true;
                sc.Roots = delegate { return new List<string>(); };
                sc.GeminiCliRoots = delegate { return new List<string> { Path.Combine(tempDir, "gemtmp") }; };
                sc.OpenCodeDirs = delegate { return new List<string> { Path.Combine(tempDir, "opencode") }; };
                sc.Scan();

                UsageSnapshot snap = sc.Snapshot();
                Assert.Equal(1650L, snap.GeminiCliToday.Total, "Gemini CLI session tokens must include input+cached+output+thoughts");
                Assert.Equal(900L, snap.OpenCodeToday.Total, "OpenCode message JSON tokens must parse accurately");
                Assert.True(snap.ExpensiveSessions.Count >= 2, "ExpensiveSessions must collect Gemini CLI and OpenCode sessions");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        public static void Test_HistoryStore_ProviderBreakdown_And_JsonExport()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "cuw_test_hist_" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                HistoryStore hs = new HistoryStore(tmp);
                Dictionary<string, double> pc = new Dictionary<string, double>();
                pc["Claude"] = 1.25;
                pc["Codex"] = 0.35;
                hs.Merge(new[] { new DayStat(DateTime.Today, 5000, 1.60, 10, pc) });

                HistoryStore reloaded = HistoryStore.LoadFrom(tmp);
                DayStat[] ser = reloaded.Series(1);
                Assert.True(Math.Abs(ser[0].CostFor("Claude") - 1.25) < 0.001, "HistoryStore must persist Claude provider cost breakdown");
                Assert.True(Math.Abs(ser[0].CostFor("Codex") - 0.35) < 0.001, "HistoryStore must persist Codex provider cost breakdown");

                string json = reloaded.ToJson();
                Assert.True(json.Contains("\"providers\"") && json.Contains("\"Claude\""), "HistoryStore.ToJson must export provider breakdown");
            }
            finally
            {
                try { File.Delete(tmp); } catch { }
            }
        }

        public static void Test_ExpensiveSessions_Anomaly_Detection()
        {
            L.Set("en");
            SessionCostRecord lowCache = new SessionCostRecord { Input = 20000, CacheRead = 1000, Output = 500, Cost = 0.40 };
            string anomLow = ProjectExplorerForm.SessionAnomalyLabel(lowCache, 0.5, 4);
            Assert.True(anomLow.IndexOf("Low cache", StringComparison.OrdinalIgnoreCase) >= 0, "Low cache ratio must flag anomaly");

            SessionCostRecord highSpend = new SessionCostRecord { Input = 1000, CacheRead = 9000, Output = 500, Cost = 6.50 };
            string anomHigh = ProjectExplorerForm.SessionAnomalyLabel(highSpend, 1.0, 4);
            Assert.True(anomHigh.IndexOf("High spend", StringComparison.OrdinalIgnoreCase) >= 0, "High spend must flag anomaly");
        }

        public static void Test_QuietHours_And_DigestScheduler()
        {
            AppConfig cfg = new AppConfig();
            cfg.QuietHoursEnabled = true;
            cfg.QuietStartHour = 22;
            cfg.QuietEndHour = 7;

            Assert.True(QuietHours.IsActive(cfg, new DateTime(2026, 11, 25, 23, 30, 0)), "23:30 must be inside 22..7 quiet hours");
            Assert.True(QuietHours.IsActive(cfg, new DateTime(2026, 11, 25, 6, 15, 0)), "06:15 must be inside 22..7 quiet hours");
            Assert.False(QuietHours.IsActive(cfg, new DateTime(2026, 11, 25, 7, 0, 0)), "07:00 ends quiet hours");
            Assert.False(QuietHours.IsActive(cfg, new DateTime(2026, 11, 25, 18, 0, 0)), "18:00 must be outside 22..7 quiet hours");

            cfg.WebhookEnabled = true;
            cfg.DigestEnabled = true;
            cfg.DigestSchedule = "daily";
            cfg.DigestHour = 18;
            DateTime t18 = new DateTime(2026, 11, 25, 18, 5, 0);
            Assert.True(DigestScheduler.ShouldSend(cfg, t18), "Daily digest should fire at DigestHour outside quiet hours");
            cfg.LastDigestKey = DigestScheduler.ComputePeriodKey("daily", t18);
            Assert.False(DigestScheduler.ShouldSend(cfg, t18), "Digest should not fire twice on the same period key");
        }

        public static void Test_Config_ExportImport_Excludes_Secrets()
        {
            AppConfig src = new AppConfig();
            src.CardLayout = "3col";
            src.MiniPillMode = "cost_runway";
            src.AlertCooldownMinutes = 42;
            src.QuietHoursEnabled = true;
            src.AdminKeyProtected = "SECRET_DPAPI_ADMIN_BLOB";
            src.WebhookUrlProtected = "SECRET_DPAPI_WEBHOOK_BLOB";

            string exported = src.ExportProfileJson();
            Assert.False(exported.Contains("SECRET_DPAPI_ADMIN_BLOB"), "Profile export must exclude AdminKeyProtected");
            Assert.False(exported.Contains("SECRET_DPAPI_WEBHOOK_BLOB"), "Profile export must exclude WebhookUrlProtected");

            AppConfig existing = new AppConfig();
            existing.AdminKeyProtected = "LOCAL_ADMIN_KEY";
            existing.WebhookUrlProtected = "LOCAL_WEBHOOK";
            AppConfig imported = AppConfig.ImportProfileJson(exported, existing);
            existing.ApplyProfileFrom(imported);
            Assert.Equal("3col", existing.CardLayout);
            Assert.Equal("cost_runway", existing.MiniPillMode);
            Assert.Equal(42, existing.AlertCooldownMinutes);
            Assert.True(existing.QuietHoursEnabled);
            Assert.Equal("LOCAL_ADMIN_KEY", existing.AdminKeyProtected, "Profile import must preserve existing local DPAPI admin key");
            Assert.Equal("LOCAL_WEBHOOK", existing.WebhookUrlProtected, "Profile import must preserve existing local DPAPI webhook key");
        }

        public static void Test_CommandParser_IPC()
        {
            Assert.Equal("toggle_compact", CommandParser.ParseCommandName("{\"cmd\":\"toggle_compact\",\"ts\":1700000000}"));
            Assert.Equal("refresh", CommandParser.ParseCommandName("{\"command\":\"Refresh\"}"));
            Assert.Equal((string)null, CommandParser.ParseCommandName("{broken"));
        }

        public static void Test_ContextLimits_PerModel()
        {
            Assert.Equal(1000000L, ContextLimits.WindowForModel("gemini-2.5-pro"));
            Assert.Equal(1000000L, ContextLimits.WindowForModel("claude-sonnet-4-6[1m]"));
            Assert.Equal(400000L, ContextLimits.WindowForModel("gpt-5-codex"));
            Assert.Equal(200000L, ContextLimits.WindowForModel("claude-sonnet-4-6"));
            Assert.Equal("1M", ContextLimits.FormatCapShort(1000000L));
            Assert.Equal("400k", ContextLimits.FormatCapShort(400000L));
            Assert.Equal("200k", ContextLimits.FormatCapShort(200000L));
        }

        public static void Test_UsageScanner_SessionCwd_And_SingleAcknowledge()
        {
            UsageScanner sc = new UsageScanner(Pricing.Load());
            sc.CacheFilePath = null;
            sc.ScanGeminiCli = false; sc.ScanOpenCode = false;
            string tempDir = Path.Combine(Path.GetTempPath(), "cuw_test_scwd_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                sc.Roots = delegate { return new List<string> { tempDir }; };
                sc.Scan(); // _ready = true
                string tNow = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                File.WriteAllLines(Path.Combine(tempDir, "s1.jsonl"), new[] {
                    string.Format("{{\"type\":\"assistant\",\"timestamp\":\"{0}\",\"cwd\":\"d:\\\\projA\",\"requestId\":\"r1\",\"message\":{{\"id\":\"m1\",\"model\":\"claude-sonnet-4-6\",\"stop_reason\":\"end_turn\",\"usage\":{{\"input_tokens\":100,\"output_tokens\":20,\"cache_read_input_tokens\":0}}}}}}", tNow)
                });
                File.WriteAllLines(Path.Combine(tempDir, "s2.jsonl"), new[] {
                    string.Format("{{\"type\":\"assistant\",\"timestamp\":\"{0}\",\"cwd\":\"d:\\\\projB\",\"requestId\":\"r2\",\"message\":{{\"id\":\"m2\",\"model\":\"claude-sonnet-4-6\",\"stop_reason\":\"tool_use\",\"usage\":{{\"input_tokens\":100,\"output_tokens\":20,\"cache_read_input_tokens\":0}}}}}}", tNow)
                });
                sc.Scan();
                UsageSnapshot snap = sc.Snapshot();
                Assert.Equal(2, snap.Sessions.Items.Count, "Both sessions should be listed in Items");
                SessionActivity s1 = snap.Sessions.Items.Find(delegate(SessionActivity x) { return x.Id == "s1"; });
                Assert.NotNull(s1);
                Assert.Equal("d:\\projA", s1.Cwd);
                Assert.Equal("done", s1.State);

                sc.AcknowledgeSession("s1");
                UsageSnapshot after = sc.Snapshot();
                Assert.Equal(0, after.Sessions.DoneCount, "AcknowledgeSession must clear only targeted session");
                Assert.Equal(1, after.Sessions.RunningCount, "Running session s2 must stay active");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        public static void Test_Pricing_ValidateCustomJson()
        {
            Assert.Equal((string)null, Pricing.ValidateCustomJson("{\"models\":[{\"prefix\":\"my-model\",\"input\":1.5,\"output\":2.5}]}"));
            Assert.NotNull(Pricing.ValidateCustomJson("{bad json"), "Malformed JSON must return validation error");
            Assert.NotNull(Pricing.ValidateCustomJson("{\"models\":[{\"prefix\":\"bad\",\"input\":-1}]}"), "Negative rate must return validation error");
        }

        public static void Test_Diag_Includes_NewFields()
        {
            AppConfig cfg = new AppConfig();
            cfg.CardLayout = "3col";
            cfg.QuietHoursEnabled = true;
            UsageSnapshot u = new UsageSnapshot { Ready = true, OpenCodeSqliteMissingCli = true, CacheHitFiles = 4, DiskReadFiles = 1 };
            string report = Diag.Build(cfg, new RateLimitSnapshot(), u, null, null, 10);
            Assert.True(report.Contains("sqliteMissingCli=yes"), "Diag must expose sqliteMissingCli");
            Assert.True(report.Contains("cardLayout=3col"), "Diag must expose CardLayout");
            Assert.True(report.Contains("cacheHits=4"), "Diag must expose CacheHitFiles");
        }

        public static void Test_FloatingForm_DynamicContext_And_SessionMenu()
        {
            bool prevCtx = Opts.ShowContextBar;
            try
            {
                Opts.ShowContextBar = true;
                using (FloatingForm f = new FloatingForm())
                {
                    IntPtr h = f.Handle;
                    ViewModel vm = new ViewModel();
                    vm.Usage = new UsageSnapshot { Ready = true, LastContextTokens = 250000, LastContextModel = "gemini-2.5-pro" };
                    vm.Usage.Sessions.RunningCount = 1;
                    vm.Usage.Sessions.Items.Add(new SessionActivity { Id = "srun", Provider = "Claude", Project = "projA", State = "running" });
                    f.Compact = false;
                    f.SetData(vm);

                    MethodInfo mBuild = typeof(FloatingForm).GetMethod("BuildRows", BindingFlags.Instance | BindingFlags.NonPublic);
                    var rows = (System.Collections.IList)mBuild.Invoke(f, null);
                    bool found1M = false;
                    foreach (object r in rows)
                    {
                        string k = r.GetType().GetField("Kind").GetValue(r).ToString();
                        string left = r.GetType().GetField("Left").GetValue(r) as string;
                        if (k == "ContextBar" && left != null && left.Contains("1M")) found1M = true;
                    }
                    Assert.True(found1M, "Gemini model ContextBar must show / 1M cap instead of hardcoded 200k");

                    MethodInfo onPaint = typeof(FloatingForm).GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic);
                    using (Bitmap bmp = new Bitmap(f.Width, f.Height))
                    using (Graphics g = Graphics.FromImage(bmp))
                        onPaint.Invoke(f, new object[] { new PaintEventArgs(g, new Rectangle(0, 0, f.Width, f.Height)) });

                    FieldInfo fiSessRow = typeof(FloatingForm).GetField("_sessRowRect", BindingFlags.Instance | BindingFlags.NonPublic);
                    Rectangle sr = (Rectangle)fiSessRow.GetValue(f);
                    Assert.False(sr.IsEmpty, "Sessions row rect must be interactive");

                    bool menuFired = false;
                    f.SessionMenuRequested += delegate { menuFired = true; };
                    MethodInfo onMouseDown = typeof(FloatingForm).GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic);
                    onMouseDown.Invoke(f, new object[] { new MouseEventArgs(MouseButtons.Left, 1, sr.Left + 5, sr.Top + 2, 0) });
                    Assert.True(menuFired, "Clicking Sessions row must fire SessionMenuRequested");
                }
            }
            finally
            {
                Opts.ShowContextBar = prevCtx;
            }
        }

        public static void Test_Security_AutoUpdater_FailClosed_And_PE()
        {
            Assert.False(AutoUpdater.IsTrustedUpdateUrl("http://github.com/a/b"), "HTTP must be rejected");
            Assert.False(AutoUpdater.IsTrustedUpdateUrl("https://evil.com/AIUsageWatcher.exe"), "Untrusted host must be rejected");
            Assert.False(AutoUpdater.IsTrustedUpdateUrl("https://github.com.evil.com/a"), "Suffix spoofing host must be rejected");
            Assert.True(AutoUpdater.IsTrustedUpdateUrl("https://github.com/silvkn1/ai-usage-watcher/releases/latest"));
            Assert.True(AutoUpdater.IsTrustedUpdateUrl("https://objects.githubusercontent.com/github-production-release-asset/123"));

            string httpJson = "{\"tag_name\":\"v9.0.0\",\"html_url\":\"http://evil.com/h\",\"assets\":[{\"name\":\"AIUsageWatcher.exe\",\"browser_download_url\":\"http://evil.com/x.exe\",\"size\":100}]}";
            ReleaseInfo ri = AutoUpdater.ParseReleaseJson(httpJson, new Version(1, 0, 0, 0));
            Assert.NotNull(ri);
            Assert.Equal("", ri.HtmlUrl, "Non-HTTPS html_url must be stripped");
            Assert.Equal("", ri.AssetDownloadUrl, "Non-HTTPS download url must be stripped");

            string fakeMz = Path.Combine(Path.GetTempPath(), "fake_mz_" + Guid.NewGuid().ToString("N") + ".exe");
            byte[] mzOnly = new byte[12288];
            mzOnly[0] = 0x4D; mzOnly[1] = 0x5A;
            File.WriteAllBytes(fakeMz, mzOnly);
            try
            {
                Assert.False(AutoUpdater.IsValidExecutable(fakeMz), "Dummy MZ bytes without valid PE signature must fail");
            }
            finally
            {
                try { File.Delete(fakeMz); } catch { }
            }

            bool threw = false;
            try
            {
                AutoUpdater.DownloadAndApplyUpdate(new ReleaseInfo
                {
                    AssetName = "AIUsageWatcher.exe",
                    AssetDownloadUrl = "https://github.com/silvkn1/ai-usage-watcher/releases/download/v9.9.9/AIUsageWatcher.exe",
                    ExpectedSha256 = null
                }, typeof(AutoUpdater).Assembly.Location);
            }
            catch (InvalidDataException) { threw = true; }
            Assert.True(threw, "Fail-closed updater must reject update with missing SHA-256");
        }

        public static void Test_Security_Webhook_Host_And_SSRF()
        {
            Uri u; string err;
            Assert.False(WebhookSender.IsSafeWebhookUrl("http://hooks.slack.com/services/a/b", out u, out err), "HTTP webhook must be rejected");
            Assert.False(WebhookSender.IsSafeWebhookUrl("https://127.0.0.1/hook", out u, out err), "Loopback IP must be rejected");
            Assert.False(WebhookSender.IsSafeWebhookUrl("https://169.254.169.254/latest", out u, out err), "IMDS link-local IP must be rejected");
            Assert.False(WebhookSender.IsSafeWebhookUrl("https://10.0.0.5/hook", out u, out err), "RFC1918 IP must be rejected");
            Assert.False(WebhookSender.IsSafeWebhookUrl("https://localhost:8443/hook", out u, out err), "Localhost must be rejected");
            Assert.True(WebhookSender.IsSafeWebhookUrl("https://hooks.slack.com/services/T00/B00/XXX", out u, out err), "Public Slack HTTPS must pass");

            string confused = WebhookSender.BuildPayload("https://evil.attacker/collect?ref=hooks.slack.com", "T", "M");
            Assert.True(confused.Contains("\"username\"") && confused.Contains("\"content\""), "Query substring hooks.slack.com must NOT select Slack-only formatting");
        }

        public static void Test_Security_Companion_NoTrustedMarkdown()
        {
            using (Stream s = typeof(CompanionInstaller).Assembly.GetManifestResourceStream("companion-extension.js"))
            {
                Assert.NotNull(s, "Embedded companion-extension.js must exist");
                using (StreamReader sr = new StreamReader(s))
                {
                    string js = sr.ReadToEnd();
                    Assert.False(js.Contains("isTrusted = true"), "VS Code MarkdownString must not enable isTrusted = true");
                    Assert.True(js.Contains("escapeMarkdown("), "VS Code extension must escape interpolated status fields");
                }
            }
        }

        public static void Test_Security_Config_Import_Allowlist_And_ProcessHelper()
        {
            AppConfig local = new AppConfig();
            local.AutoUpdateCheck = true;
            local.MirrorEnabled = false;
            local.MirrorFilePath = "";
            local.ExtraClaudeDirs = "";
            local.ScanWsl = false;
            local.OllamaPort = 11434;

            string crafted = "{\"Theme\":\"light\",\"AutoUpdateCheck\":false,\"MirrorEnabled\":true,\"MirrorFilePath\":\"C:\\\\Windows\\\\hosts\",\"ExtraClaudeDirs\":\"\\\\\\\\evil\\\\share\",\"ScanWsl\":true,\"OllamaPort\":9999}";
            AppConfig imp = AppConfig.ImportProfileJson(crafted, local);
            local.ApplyProfileFrom(imp);

            Assert.Equal("light", local.Theme);
            Assert.True(local.AutoUpdateCheck, "Imported profile must not disable AutoUpdateCheck");
            Assert.False(local.MirrorEnabled, "Imported profile must not enable MirrorEnabled");
            Assert.Equal("", local.MirrorFilePath, "Imported profile must not overwrite MirrorFilePath");
            Assert.Equal("", local.ExtraClaudeDirs, "Imported profile must not inject ExtraClaudeDirs");
            Assert.False(local.ScanWsl, "Imported profile must not alter ScanWsl");
            Assert.Equal(11434, local.OllamaPort, "Imported profile must not alter OllamaPort");

            Assert.False(ProcessHelper.IsSafeSearchDir("."), "Relative PATH dir must be rejected");
            Assert.False(ProcessHelper.IsSafeSearchDir("\\\\server\\share"), "UNC PATH dir must be rejected");
            Assert.True(ProcessHelper.IsSafeSearchDir(Environment.SystemDirectory));
            Assert.True(File.Exists(ProcessHelper.SystemExe("cmd.exe")));
            Assert.True(File.Exists(ProcessHelper.ExplorerExe));
            Assert.True(CliProbe.IsValidModelName("claude-haiku-4-5"));
            Assert.False(CliProbe.IsValidModelName("claude\necho pwn"), "Newline injection in model name must be rejected");
            Assert.False(CliProbe.IsValidModelName("claude&whoami"), "Shell metacharacter in model name must be rejected");
        }

        public static void Test_AboutMenu_And_ConfirmBadge_Fixes()
        {
            // 1. About menu wired inside Help submenu of shared right-click ContextMenuStrip
            using (TrayApp app = new TrayApp())
            {
                FieldInfo fiHelp = typeof(TrayApp).GetField("_miHelpGroup", BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo fiAbout = typeof(TrayApp).GetField("_miAbout", BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo fiMenu = typeof(TrayApp).GetField("_menu", BindingFlags.NonPublic | BindingFlags.Instance);
                ToolStripMenuItem miHelp = fiHelp.GetValue(app) as ToolStripMenuItem;
                ToolStripMenuItem miAbout = fiAbout.GetValue(app) as ToolStripMenuItem;
                ContextMenuStrip menu = fiMenu.GetValue(app) as ContextMenuStrip;
                Assert.NotNull(miAbout, "_miAbout menu item must exist");
                Assert.True(miHelp.DropDownItems.Contains(miAbout), "Help submenu must contain About item");
                Assert.True(menu.Items.Count <= 15, "Top-level right-click menu count must stay <= 15");
            }

            // 2. Antigravity & Claude ? confirmation persistence fixes
            Pricing pricing = Pricing.Load();
            UsageScanner sc = new UsageScanner(pricing);
            sc.CacheFilePath = null;
            sc.ScanCodex = false; sc.ScanGeminiCli = false; sc.ScanOpenCode = false;
            string tempDir = Path.Combine(Path.GetTempPath(), "cuw_test_confirmfix_" + Guid.NewGuid().ToString("N"));
            try
            {
                string agDir = Path.Combine(tempDir, "brain", "conv-fix-1", ".system_generated", "logs");
                string clDir = Path.Combine(tempDir, "claude", "proj");
                Directory.CreateDirectory(agDir);
                Directory.CreateDirectory(clDir);
                sc.Roots = delegate { return new List<string> { Path.Combine(tempDir, "brain"), clDir }; };
                sc.Scan(); // _ready = true

                string t1 = DateTime.UtcNow.AddSeconds(-5).ToString("yyyy-MM-ddTHH:mm:ssZ");
                string t2 = DateTime.UtcNow.AddSeconds(-3).ToString("yyyy-MM-ddTHH:mm:ssZ");
                string t3 = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

                // Antigravity notify_user with stringified "BlockedOnUser":"true" followed by MODEL GENERIC step
                string agNotifyLine = string.Format(
                    "{{\"type\":\"PLANNER_RESPONSE\",\"model\":\"gemini-3.8-flash\",\"created_at\":\"{0}\",\"step_index\":10,\"tool_calls\":[{{\"name\":\"default_api:notify_user\",\"args\":{{\"BlockedOnUser\":\"true\"}}}}],\"input_tokens\":200,\"output_tokens\":40,\"cache_read_tokens\":0}}",
                    t1
                );
                string agModelGeneric = string.Format(
                    "{{\"type\":\"GENERIC\",\"source\":\"MODEL\",\"status\":\"RUNNING\",\"created_at\":\"{0}\",\"content\":\"wait\"}}",
                    t2
                );
                File.WriteAllLines(Path.Combine(agDir, "transcript.jsonl"), new[] { agNotifyLine, agModelGeneric });

                // Claude multi-chunk same message.id|requestId: thinking block then AskUserQuestion block
                string clThink = string.Format(
                    "{{\"type\":\"assistant\",\"timestamp\":\"{0}\",\"requestId\":\"req_c1\",\"cwd\":\"d:\\\\demo\",\"message\":{{\"id\":\"msg_c1\",\"model\":\"claude-sonnet-4-6\",\"stop_reason\":null,\"content\":[{{\"type\":\"thinking\",\"thinking\":\"...\"}}],\"usage\":{{\"input_tokens\":500,\"output_tokens\":20,\"cache_read_input_tokens\":0}}}}}}",
                    t1
                );
                string clAsk = string.Format(
                    "{{\"type\":\"assistant\",\"timestamp\":\"{0}\",\"requestId\":\"req_c1\",\"cwd\":\"d:\\\\demo\",\"message\":{{\"id\":\"msg_c1\",\"model\":\"claude-sonnet-4-6\",\"stop_reason\":\"tool_use\",\"content\":[{{\"type\":\"tool_use\",\"name\":\"AskUserQuestion\",\"input\":{{}}}}],\"usage\":{{\"input_tokens\":500,\"output_tokens\":20,\"cache_read_input_tokens\":0}}}}}}",
                    t2
                );
                File.WriteAllLines(Path.Combine(clDir, "sess-claude-1.jsonl"), new[] { clThink, clAsk });

                sc.Scan();
                UsageSnapshot snap = sc.Snapshot();
                Assert.Equal(2, snap.Sessions.ConfirmCount, "Both Antigravity BlockedOnUser:'true' and Claude multi-chunk AskUserQuestion must trigger ? badge");
                Assert.True(snap.Sessions.AntigravityConfirm, "AntigravityConfirm must survive MODEL GENERIC step");
                Assert.True(snap.Sessions.ClaudeConfirm, "ClaudeConfirm must trigger even when thinking block shared same id|requestId");
                Assert.Equal(520L, snap.ClaudeToday.Total, "Claude tokens must NOT be double-counted across chunks sharing id|requestId");

                // Stale SyncHookSessions timestamp must NOT clear Confirm (?)
                sc.MarkHookNeedsConfirm("hook_sid_1");
                sc.SyncHookSessions(new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase) {
                    { "hook_sid_1", DateTime.Now.AddSeconds(-10) }
                });
                UsageSnapshot snapHook = sc.Snapshot();
                Assert.Equal(3, snapHook.Sessions.ConfirmCount, "Older hook file timestamp must NOT overwrite NeedsConfirm (?) back to Running");

                // Only a newer USER_INPUT clears Antigravity ? back to Running
                string agReply = string.Format(
                    "{{\"type\":\"USER_INPUT\",\"source\":\"USER_EXPLICIT\",\"created_at\":\"{0}\",\"content\":\"ok\"}}",
                    DateTime.UtcNow.AddSeconds(2).ToString("yyyy-MM-ddTHH:mm:ssZ")
                );
                File.AppendAllLines(Path.Combine(agDir, "transcript.jsonl"), new[] { agReply });
                sc.Scan();
                UsageSnapshot afterReply = sc.Snapshot();
                Assert.False(afterReply.Sessions.AntigravityConfirm, "USER_INPUT reply must clear AntigravityConfirm");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        public static void Test_V122_Pools_Scale_Tools_Companion_And_ConfirmBadge()
        {
            // 1. Localization placeholder parity across all EN & ID entries
            foreach (KeyValuePair<string, string[]> kv in L.All)
            {
                Assert.True(kv.Value != null && kv.Value.Length >= 2, "Key " + kv.Key + " must have EN and ID strings");
                for (int p = 0; p <= 4; p++)
                {
                    string ph = "{" + p + "}";
                    Assert.Equal(kv.Value[0].Contains(ph), kv.Value[1].Contains(ph),
                        "Placeholder " + ph + " mismatch in key: " + kv.Key);
                }
            }

            // 2. Multi-pool Antigravity Connect-RPC parsing
            string multiGroupJson = "{" +
                "\"response\":{\"groups\":[" +
                "{\"displayName\":\"Gemini Pro\",\"buckets\":[{\"window\":\"5h\",\"remainingFraction\":0.38,\"resetTime\":\"2026-11-26T18:00:00Z\"}]}," +
                "{\"displayName\":\"Gemini Flash\",\"buckets\":[{\"window\":\"5h\",\"remainingFraction\":0.82,\"resetTime\":\"2026-11-26T18:00:00Z\"}]}," +
                "{\"displayName\":\"Claude\",\"buckets\":[{\"window\":\"5h\",\"remainingFraction\":0.60,\"resetTime\":\"2026-11-26T18:00:00Z\"}]}" +
                "]}}";
            Assert.True(AntigravityRateFetcher.ParseAndSaveQuotaSummary(multiGroupJson), "Multi-group quota summary must parse");
            RateLimitSnapshot snapMulti = RateLimitReader.Read();
            Assert.True(snapMulti.AntigravityPools.Count >= 3, "RateLimitReader must populate all 3 Antigravity pools");
            Assert.True(snapMulti.AntigravityPools.ContainsKey("Flash"), "Flash pool must be present");

            string userStatusJson = "{" +
                "\"userStatus\":{\"cascadeModelConfigData\":{\"clientModelConfigs\":[" +
                "{\"label\":\"Gemini Pro\",\"quotaInfo\":{\"remainingFraction\":0.50,\"resetTime\":\"2026-11-26T19:00:00Z\"}}," +
                "{\"label\":\"Gemini Flash\",\"quotaInfo\":{\"remainingFraction\":0.90,\"resetTime\":\"2026-11-26T19:00:00Z\"}}," +
                "{\"label\":\"Claude 4.5\",\"quotaInfo\":{\"remainingFraction\":0.25,\"resetTime\":\"2026-11-26T19:00:00Z\"}}" +
                "]}}}";
            Assert.True(AntigravityRateFetcher.ParseAndSaveUserStatus(userStatusJson), "Multi-model GetUserStatus fallback must parse");
            RateLimitSnapshot snapStatus = RateLimitReader.Read();
            Assert.True(snapStatus.AntigravityPools.ContainsKey("Claude"), "Claude pool must be parsed from GetUserStatus");
            Assert.True(snapStatus.AntigravityFiveHour.UsedPercent >= 74.0, "Tightest remaining fraction (25% left = 75% used) must be surfaced");

            // 3. AppConfig.WidgetScale normalization & profile export/import + FloatingForm scaling
            AppConfig cfg = new AppConfig();
            cfg.WidgetScale = 9.0;
            cfg.Normalize();
            Assert.Equal(1.0, cfg.WidgetScale, "Out-of-range WidgetScale must reset to 1.0");
            cfg.WidgetScale = 1.15;
            cfg.Normalize();
            Assert.Equal(1.15, cfg.WidgetScale, "1.15 WidgetScale must be preserved");
            AppConfig imp = AppConfig.ImportProfileJson(cfg.ExportProfileJson(), new AppConfig());
            Assert.Equal(1.15, imp.WidgetScale, "WidgetScale must survive profile Export/Import");

            double prevScale = Opts.WidgetScale;
            try
            {
                using (FloatingForm fSmall = new FloatingForm())
                using (FloatingForm fLarge = new FloatingForm())
                {
                    ViewModel vm = new ViewModel { Usage = new UsageSnapshot { Ready = true }, Limits = new RateLimitSnapshot() };
                    Opts.WidgetScale = 1.0;
                    IntPtr h1 = fSmall.Handle;
                    fSmall.SetData(vm);
                    Opts.WidgetScale = 1.30;
                    IntPtr h2 = fLarge.Handle;
                    fLarge.SetData(vm);
                    Assert.True(fLarge.Width > fSmall.Width, "1.30 WidgetScale must render wider HUD than 1.0");
                }
            }
            finally { Opts.WidgetScale = prevScale; }

            // 4. TrayIconPainter purple confirm dot
            using (Bitmap bNo = new Bitmap(16, 16))
            using (Bitmap bYes = new Bitmap(16, 16))
            {
                using (Graphics g1 = Graphics.FromImage(bNo)) TrayIconPainter.Draw(g1, 16, "20", TrayIconPainter.StGreen, false, false);
                using (Graphics g2 = Graphics.FromImage(bYes)) TrayIconPainter.Draw(g2, 16, "20", TrayIconPainter.StGreen, false, true);
                bool diff = false;
                for (int px = 9; px < 16; px++)
                    for (int py = 0; py < 7; py++)
                        if (bNo.GetPixel(px, py).ToArgb() != bYes.GetPixel(px, py).ToArgb()) diff = true;
                Assert.True(diff, "Confirm tray dot must alter top-right pixels");
            }

            // 5. Tool Analytics parsing + Cache v3 persistence + Companion ack_sessions + ProjectExplorer Tab 4
            string tempDir = Path.Combine(Path.GetTempPath(), "cuw_test_v122_" + Guid.NewGuid().ToString("N"));
            try
            {
                string agDir = Path.Combine(tempDir, "brain", "conv-tool", ".system_generated", "logs");
                string clDir = Path.Combine(tempDir, "claude", "proj");
                string cachePath = Path.Combine(tempDir, "scan_cache.json");
                string compDir = Path.Combine(tempDir, "comp_ext");
                Directory.CreateDirectory(agDir);
                Directory.CreateDirectory(clDir);

                string nowUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                string clLine = string.Format(
                    "{{\"type\":\"assistant\",\"timestamp\":\"{0}\",\"requestId\":\"req_t1\",\"cwd\":\"d:\\\\demo\",\"message\":{{\"id\":\"msg_t1\",\"model\":\"claude-sonnet-4-6\",\"stop_reason\":\"tool_use\",\"content\":[{{\"type\":\"tool_use\",\"name\":\"Bash\",\"input\":{{}}}},{{\"type\":\"tool_use\",\"name\":\"mcp__github__search\",\"input\":{{}}}}],\"usage\":{{\"input_tokens\":400,\"output_tokens\":200,\"cache_read_input_tokens\":0}}}}}}",
                    nowUtc);
                File.WriteAllLines(Path.Combine(clDir, "sess-t1.jsonl"), new[] { clLine });

                string agLine = string.Format(
                    "{{\"type\":\"PLANNER_RESPONSE\",\"model\":\"gemini-3.8-flash\",\"created_at\":\"{0}\",\"step_index\":1,\"tool_calls\":[{{\"name\":\"default_api:read_file\",\"args\":{{}}}}],\"input_tokens\":300,\"output_tokens\":100,\"cache_read_tokens\":0}}",
                    nowUtc);
                File.WriteAllLines(Path.Combine(agDir, "transcript.jsonl"), new[] { agLine });

                Pricing pr = Pricing.Load();
                UsageScanner sc1 = new UsageScanner(pr) { CacheFilePath = cachePath, ScanCodex = false, ScanGeminiCli = false, ScanOpenCode = false };
                sc1.Roots = delegate { return new List<string> { Path.Combine(tempDir, "brain"), clDir }; };
                sc1.Scan();
                UsageSnapshot s1 = sc1.Snapshot();
                Assert.True(s1.ToolBreakdown.Count >= 3, "Must extract Bash, mcp:github/search, and read_file tool stats");

                // Replay strictly from Cache v3 without re-reading JSONL
                UsageScanner sc2 = new UsageScanner(pr) { CacheFilePath = cachePath, ScanCodex = false, ScanGeminiCli = false, ScanOpenCode = false };
                sc2.Roots = delegate { return new List<string> { Path.Combine(tempDir, "brain"), clDir }; };
                sc2.Scan();
                UsageSnapshot s2 = sc2.Snapshot();
                Assert.Equal(s1.ToolBreakdown.Count, s2.ToolBreakdown.Count, "ScanCache v3 must restore ToolBreakdown count cleanly");
                Assert.True(sc2.CacheHitFiles >= 2, "Unchanged files must hit disk cache");

                using (ProjectExplorerForm pe = new ProjectExplorerForm(s2, new HistoryStore(Path.Combine(tempDir, "h.json"))))
                {
                    IntPtr peH = pe.Handle;
                    Assert.NotNull(peH);
                }

                string instPaths, instErr;
                Assert.True(CompanionInstaller.InstallTo(compDir, out instPaths, out instErr), "Companion install must succeed: " + instErr);
                string extJs = File.ReadAllText(Path.Combine(compDir, "extension.js"));
                Assert.True(extJs.Contains("ack_sessions"), "Companion extension.js must include ack_sessions Quick Pick action");
                Assert.Equal("ack_sessions", CommandParser.ParseCommandName("{\"cmd\":\"ack_sessions\"}"), "CommandParser must accept ack_sessions");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        public static void Test_Redesign_MaxWidth_Chips_Zen_Worktree_Allowance()
        {
            bool oldZen = Opts.ZenMode;
            long oldCtxOverride = Opts.ContextWindowOverride;
            double oldScale = Opts.WidgetScale;
            string oldLayout = Opts.CardLayout;
            try
            {
                Opts.ZenMode = false;
                Opts.ContextWindowOverride = 0;
                Opts.WidgetScale = 1.0;
                Opts.CardLayout = "2col";

                // 1. Auto 1M detection (>205k tokens) + manual override
                Assert.Equal(1000000L, ContextLimits.WindowForModel("claude-sonnet-4-6", 308200), "Observed >205k tokens must auto-promote window to 1M");
                Assert.Equal(200000L, ContextLimits.WindowForModel("claude-sonnet-4-6", 190000), "<=205k tokens must stay 200k");
                Opts.ContextWindowOverride = 400000L;
                Assert.Equal(400000L, ContextLimits.WindowForModel("claude-sonnet-4-6", 308200), "Explicit ContextWindowOverride must take priority");
                Opts.ContextWindowOverride = 0;

                // 2. Pace.DailyAllowance on 7-day quota >= 50% with >12h left
                DateTime now = DateTime.Now;
                LimitWindow w7Hi = new LimitWindow { Available = true, UsedPercent = 85, ResetsAt = now.AddDays(3) };
                string allow = Pace.DailyAllowance(w7Hi, now);
                Assert.NotNull(allow, "7d >= 50% with >12h left must produce daily allowance hint");
                Assert.True(allow.StartsWith("~") && allow.Contains("%/"), "Daily allowance must match ~X%/d format, got: " + allow);
                LimitWindow w7Lo = new LimitWindow { Available = true, UsedPercent = 25, ResetsAt = now.AddDays(3) };
                Assert.Equal((string)null, Pace.DailyAllowance(w7Lo, now), "7d < 50% must not emit allowance hint");
                LimitWindow w7Soon = new LimitWindow { Available = true, UsedPercent = 85, ResetsAt = now.AddHours(6) };
                Assert.Equal((string)null, Pace.DailyAllowance(w7Soon, now), "7d < 12h remaining must not emit allowance hint");

                // 3. Worktree ProjectName collapsing & per-session cost/cache stats
                string tempDir = Path.Combine(Path.GetTempPath(), "cuw_test_redesign_" + Guid.NewGuid().ToString("N"));
                try
                {
                    string clDir = Path.Combine(tempDir, "claude", "proj");
                    Directory.CreateDirectory(clDir);
                    string wtCwd = "d:\\\\code\\\\widget-claude\\\\.claude\\\\worktrees\\\\keen-kilby-aa2b73";
                    string ts = DateTime.UtcNow.AddSeconds(-15).ToString("yyyy-MM-ddTHH:mm:ssZ");
                    UsageScanner sc = new UsageScanner(Pricing.Load()) { CacheFilePath = null, ScanCodex = false, ScanGemini = false, ScanGeminiCli = false, ScanOpenCode = false };
                    sc.Roots = delegate { return new List<string> { clDir }; };
                    sc.Scan(); // baseline _ready = true
                    string clLine = string.Format(
                        "{{\"type\":\"assistant\",\"timestamp\":\"{0}\",\"requestId\":\"req_wt1\",\"cwd\":\"{1}\",\"message\":{{\"id\":\"msg_wt1\",\"model\":\"claude-sonnet-4-6\",\"stop_reason\":\"end_turn\",\"content\":[],\"usage\":{{\"input_tokens\":1000,\"output_tokens\":200,\"cache_read_input_tokens\":9000}}}}}}",
                        ts, wtCwd);
                    File.WriteAllLines(Path.Combine(clDir, "sess-wt-1.jsonl"), new[] { clLine });

                    sc.Scan();
                    UsageSnapshot snap = sc.Snapshot();
                    Assert.Equal("widget-claude (wt)", snap.LastActiveProject, "LastActiveProject must collapse worktree path");
                    Assert.True(snap.Sessions != null && snap.Sessions.Items.Count >= 1, "Worktree JSONL must produce SessionActivity");
                    SessionActivity wtSess = snap.Sessions.Items[0];
                    Assert.Equal("widget-claude (wt)", wtSess.Project, "Worktree path must collapse to '<repo> (wt)'");
                    Assert.True(wtSess.SessionTokens >= 10200, "SessionActivity must track SessionTokens");
                    Assert.True(wtSess.SessionCost > 0, "SessionActivity must track SessionCost");
                    Assert.True(wtSess.SessionCacheHit > 0.85, "SessionActivity must track SessionCacheHit");
                }
                finally
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }

                // 4. FloatingForm maxW clamp, SessionChips click hit-rects, Header buttons, and Zen Mode height reduction
                ViewModel vm = new ViewModel();
                vm.ScanClaude = true;
                vm.ScanGemini = true;
                vm.Limits = new RateLimitSnapshot
                {
                    FileFound = true,
                    FiveHour = new LimitWindow { Available = true, UsedPercent = 27, ResetsAt = now.AddHours(2) },
                    SevenDay = new LimitWindow { Available = true, UsedPercent = 85, ResetsAt = now.AddDays(3).AddHours(16) },
                    AntigravityFiveHour = new LimitWindow { Available = true, UsedPercent = 12, ResetsAt = now.AddHours(3) },
                    AntigravitySevenDay = new LimitWindow { Available = true, UsedPercent = 21, ResetsAt = now.AddDays(5) }
                };
                vm.Usage = new UsageSnapshot
                {
                    Ready = true,
                    LastContextTokens = 308200,
                    LastContextModel = "claude-sonnet-4-6",
                    Sessions = new SessionStatusSummary
                    {
                        RunningCount = 3,
                        ConfirmCount = 1,
                        DoneCount = 3,
                        Items = new List<SessionActivity>
                        {
                            new SessionActivity { Id = "s1", Provider = "Claude", Project = "very-long-worktree-repository-name-alpha-beta-gamma (wt)", State = "confirm", NeedsConfirm = true },
                            new SessionActivity { Id = "s2", Provider = "Claude", Project = "another-super-long-project-directory-name-01 (wt)", State = "running", IsRunning = true },
                            new SessionActivity { Id = "s3", Provider = "Claude", Project = "third-very-long-directory-name-for-stress-test", State = "done" }
                        }
                    }
                };
                vm.History = new DayStat[]
                {
                    new DayStat(now.Date.AddDays(-1), 120000, 1.5, 10),
                    new DayStat(now.Date, 240000, 3.0, 20)
                };
                vm.BudgetDaily = 10.0;

                using (FloatingForm f = new FloatingForm())
                {
                    f.Backdrop = "solid";
                    f.SetData(vm);
                    IntPtr h = f.Handle;
                    Assert.NotNull(h);
                    int fullWidth = f.Width;
                    int fullHeight = f.Height;
                    Assert.True(fullWidth <= 570, "FloatingForm must clamp width even with long session labels, got " + fullWidth);

                    using (Bitmap bmp = new Bitmap(f.Width, f.Height))
                    {
                        f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                    }

                    FieldInfo fiSess = typeof(FloatingForm).GetField("_sessRowRect", BindingFlags.NonPublic | BindingFlags.Instance);
                    FieldInfo fiDone = typeof(FloatingForm).GetField("_pillDoneRect", BindingFlags.NonPublic | BindingFlags.Instance);
                    FieldInfo fiMode = typeof(FloatingForm).GetField("_titleModeRect", BindingFlags.NonPublic | BindingFlags.Instance);
                    FieldInfo fiSet = typeof(FloatingForm).GetField("_titleSettingsRect", BindingFlags.NonPublic | BindingFlags.Instance);
                    Rectangle sessR = (Rectangle)fiSess.GetValue(f);
                    Rectangle doneR = (Rectangle)fiDone.GetValue(f);
                    Rectangle modeR = (Rectangle)fiMode.GetValue(f);
                    Rectangle setR = (Rectangle)fiSet.GetValue(f);
                    Assert.False(sessR.IsEmpty, "_sessRowRect must be populated after painting SessionChips");
                    Assert.False(doneR.IsEmpty, "_pillDoneRect must be populated when confirm/done sessions exist");
                    Assert.False(modeR.IsEmpty, "_titleModeRect header button must be painted");
                    Assert.False(setR.IsEmpty, "_titleSettingsRect header button must be painted");

                    bool menuFired = false, doneFired = false, modeFired = false, setFired = false;
                    f.SessionMenuRequested += delegate { menuFired = true; };
                    f.DoneSessionsClicked += delegate { doneFired = true; };
                    f.PresetCycleRequested += delegate { modeFired = true; };
                    f.SettingsRequested += delegate { setFired = true; };

                    MethodInfo miDown = typeof(FloatingForm).GetMethod("OnMouseDown", BindingFlags.NonPublic | BindingFlags.Instance);
                    miDown.Invoke(f, new object[] { new MouseEventArgs(MouseButtons.Left, 1, doneR.Left + 2, doneR.Top + 2, 0) });
                    miDown.Invoke(f, new object[] { new MouseEventArgs(MouseButtons.Left, 1, sessR.Left + 4, sessR.Top + 2, 0) });
                    miDown.Invoke(f, new object[] { new MouseEventArgs(MouseButtons.Left, 1, modeR.Left + 2, modeR.Top + 2, 0) });
                    miDown.Invoke(f, new object[] { new MouseEventArgs(MouseButtons.Left, 1, setR.Left + 2, setR.Top + 2, 0) });
                    Assert.True(doneFired, "Clicking _pillDoneRect must fire DoneSessionsClicked");
                    Assert.True(menuFired, "Clicking _sessRowRect must fire SessionMenuRequested");
                    Assert.True(modeFired, "Clicking _titleModeRect must fire PresetCycleRequested");
                    Assert.True(setFired, "Clicking _titleSettingsRect must fire SettingsRequested");

                    Opts.ZenMode = true;
                    f.SetData(vm);
                    Assert.True(f.Height < fullHeight, "ZenMode must shrink widget height cleanly");
                }

                // 5. TrayApp _miZen wiring in View & Layout submenu
                using (TrayApp app = new TrayApp())
                {
                    FieldInfo fiView = typeof(TrayApp).GetField("_miViewGroup", BindingFlags.NonPublic | BindingFlags.Instance);
                    FieldInfo fiZen = typeof(TrayApp).GetField("_miZen", BindingFlags.NonPublic | BindingFlags.Instance);
                    FieldInfo fiMenu = typeof(TrayApp).GetField("_menu", BindingFlags.NonPublic | BindingFlags.Instance);
                    ToolStripMenuItem miView = fiView.GetValue(app) as ToolStripMenuItem;
                    ToolStripMenuItem miZen = fiZen.GetValue(app) as ToolStripMenuItem;
                    ContextMenuStrip menu = fiMenu.GetValue(app) as ContextMenuStrip;
                    Assert.NotNull(miZen, "_miZen menu item must exist");
                    Assert.True(miView.DropDownItems.Contains(miZen), "View submenu must contain _miZen");
                    Assert.True(menu.Items.Count <= 15, "Top-level right-click menu count must stay <= 15");
                }
            }
            finally
            {
                Opts.ZenMode = oldZen;
                Opts.ContextWindowOverride = oldCtxOverride;
                Opts.WidgetScale = oldScale;
                Opts.CardLayout = oldLayout;
            }
        }

        public static void Test_SettingsForm_LazyTabs_And_SavePreservation()
        {
            string origLang = L.Lang;
            try
            {
                L.Set("en");
                AppConfig cfg = new AppConfig();
                cfg.Language = "en";
                cfg.AlertThresholds = "70,90";
                cfg.ProbeEnabled = true;
                cfg.ProbeModel = "claude-sonnet-4-6";
                cfg.HotkeyEnabled = false;
                cfg.Hotkey = "Ctrl+Alt+U";
                cfg.AdminEnabled = false;

                bool applied = false;
                SettingsCallbacks cb = new SettingsCallbacks();
                cb.Apply = delegate { applied = true; };

                using (SettingsForm sf = new SettingsForm(cfg, cb))
                {
                    IntPtr h = sf.Handle;
                    Assert.NotNull(h);

                    bool[] built = typeof(SettingsForm).GetField("_tabBuilt", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(sf) as bool[];
                    Assert.NotNull(built, "_tabBuilt array must exist");
                    Assert.True(built[0], "Display tab (0) must be built eagerly");
                    Assert.False(built[1] || built[2] || built[3] || built[4] || built[5], "Tabs 1..5 must defer construction until clicked");

                    MethodInfo miApply = typeof(SettingsForm).GetMethod("OnApply", BindingFlags.NonPublic | BindingFlags.Instance);
                    miApply.Invoke(sf, new object[] { null, EventArgs.Empty });
                    Assert.True(applied, "OnApply with unbuilt tabs 1..5 must succeed");
                    Assert.Equal("70,90", cfg.AlertThresholds, "Unbuilt Alerts tab must keep existing thresholds");
                    Assert.Equal("claude-sonnet-4-6", cfg.ProbeModel, "Unbuilt Probe tab must keep existing ProbeModel");
                    Assert.Equal("Ctrl+Alt+U", cfg.Hotkey, "Unbuilt Behavior tab must keep existing Hotkey");

                    MethodInfo miSel = typeof(SettingsForm).GetMethod("SelectTab", BindingFlags.NonPublic | BindingFlags.Instance);
                    miSel.Invoke(sf, new object[] { 2 });
                    Assert.True(built[2], "Selecting Alerts tab must lazily build tab 2");
                    TextBox tbTh = typeof(SettingsForm).GetField("_alertTh", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(sf) as TextBox;
                    Assert.Equal("70,90", tbTh.Text, "Lazily built Alerts tab must populate controls from AppConfig");

                    L.Set("id");
                    sf.RefreshI18n();
                    miSel.Invoke(sf, new object[] { 3 });
                    Assert.True(built[3], "Selecting Data tab must lazily build tab 3");
                    CheckBox chkAdminData = typeof(SettingsForm).GetField("_adminData", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(sf) as CheckBox;
                    chkAdminData.Checked = true;

                    miSel.Invoke(sf, new object[] { 5 });
                    Assert.True(built[5], "Selecting Admin tab must lazily build tab 5");
                    CheckBox chkAdmin = typeof(SettingsForm).GetField("_admin", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(sf) as CheckBox;
                    Assert.True(chkAdmin.Checked, "_adminData toggle on Data tab must stay synced when Admin tab builds");
                    Assert.Equal(L.T("set.admin.enable"), chkAdmin.Text, "Lazy Admin tab must build with active Indonesian strings");

                    L.Set("en");
                    sf.RefreshI18n();
                    Assert.Equal(L.T("set.admin.enable"), chkAdmin.Text, "Lazily built Admin tab must also refresh live on language change");

                    sf.PrewarmHandles();
                    Assert.True(sf.IsHandleCreated && !sf.Visible, "PrewarmHandles must create HWND while staying hidden");
                    CheckBox chkZen = typeof(SettingsForm).GetField("_zenMode", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(sf) as CheckBox;
                    chkZen.Checked = !cfg.ZenMode;
                    cfg.ZenMode = !cfg.ZenMode;
                    sf.PrepareForOpen();
                    Assert.Equal(cfg.ZenMode, chkZen.Checked, "PrepareForOpen must re-sync controls from external config changes");
                }
            }
            finally
            {
                L.Set(origLang);
            }
        }

        public static void Test_Common_J_Ser_ThreadIsolation_And_ConcurrentParse()
        {
            object serMain = J.Ser;
            object serWorker = null;
            Exception workerErr = null;

            System.Threading.Thread tProbe = new System.Threading.Thread(delegate()
            {
                try
                {
                    serWorker = J.Ser;
                }
                catch (Exception ex) { workerErr = ex; }
            });
            tProbe.Start();
            tProbe.Join();

            Assert.True(workerErr == null, "Worker J.Ser access must succeed");
            Assert.NotNull(serWorker);
            Assert.False(object.ReferenceEquals(serMain, serWorker), "J.Ser must return distinct per-thread instances");

            const int ThreadCount = 8;
            const int Iterations = 50;
            System.Threading.Tasks.Task[] tasks = new System.Threading.Tasks.Task[ThreadCount];
            for (int t = 0; t < ThreadCount; t++)
            {
                int tid = t;
                tasks[t] = System.Threading.Tasks.Task.Run(delegate
                {
                    for (int i = 0; i < Iterations; i++)
                    {
                        Dictionary<string, object> o = J.Parse("{\"t\":" + tid + ",\"i\":" + i + "}");
                        Assert.NotNull(o);
                        Assert.Equal((long)tid, J.Long(o, "t"), "Thread id mismatch");
                        Assert.Equal((long)i, J.Long(o, "i"), "Iteration mismatch");
                    }
                });
            }
            System.Threading.Tasks.Task.WaitAll(tasks);
        }

        public static void Test_Security_Webhook_EmptyDns_And_AutoUpdater_Hardening()
        {
            Assert.False(WebhookSender.AreResolvedAddressesSafe(null), "Null DNS result must fail closed");
            Assert.False(WebhookSender.AreResolvedAddressesSafe(new System.Net.IPAddress[0]), "Empty DNS result must fail closed");
            Assert.False(WebhookSender.AreResolvedAddressesSafe(new[] { System.Net.IPAddress.Loopback }), "Loopback DNS result must fail closed");
            Assert.False(WebhookSender.AreResolvedAddressesSafe(new[] { System.Net.IPAddress.Parse("169.254.169.254") }), "IMDS DNS result must fail closed");
            Assert.True(WebhookSender.AreResolvedAddressesSafe(new[] { System.Net.IPAddress.Parse("93.184.216.34") }), "Public IPv4 must pass");

            Assert.True(AutoUpdater.ParseVersion("v9999999999999.1.0") == null, "Oversized major version must return null cleanly without OverflowException");
            Assert.True(AutoUpdater.ParseVersion("1.2.3.4") == new Version(1, 2, 3, 4));

            Uri curUri = new Uri("https://github.com/silvkn1/ai-usage-watcher/releases/download/v1.2.3/a.zip");
            Assert.NotNull(AutoUpdater.ResolveTrustedRedirectUrl(curUri, new Uri("https://objects.githubusercontent.com/abc/a.zip")));
            Assert.True(AutoUpdater.ResolveTrustedRedirectUrl(curUri, new Uri("https://evil.example.com/payload.exe")) == null, "Untrusted redirect host must be rejected");
            Assert.True(AutoUpdater.ResolveTrustedRedirectUrl(curUri, new Uri("http://objects.githubusercontent.com/abc/a.zip")) == null, "HTTP downgrade redirect must be rejected");
            Assert.NotNull(AutoUpdater.ResolveTrustedRedirectUrl(curUri, new Uri("/silvkn1/ai-usage-watcher/releases/v2", UriKind.Relative)));

            string tmpDir = Path.Combine(Path.GetTempPath(), "cuw_zip_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmpDir);
            try
            {
                string keepFile = Path.Combine(tmpDir, "config.txt");
                File.WriteAllText(keepFile, "ORIGINAL_KEEP");
                string badZip = Path.Combine(tmpDir, "no_exe.zip");
                using (System.IO.Compression.ZipArchive za = System.IO.Compression.ZipFile.Open(badZip, System.IO.Compression.ZipArchiveMode.Create))
                {
                    System.IO.Compression.ZipArchiveEntry e = za.CreateEntry("config.txt");
                    using (StreamWriter sw = new StreamWriter(e.Open())) sw.Write("OVERWRITTEN");
                }
                bool threwNoExe = false;
                try { AutoUpdater.ExtractUpdateZip(badZip, Path.Combine(tmpDir, "AIUsageWatcher.exe"), tmpDir); }
                catch (InvalidDataException) { threwNoExe = true; }
                Assert.True(threwNoExe, "ZIP without target exe must reject");
                Assert.Equal("ORIGINAL_KEEP", File.ReadAllText(keepFile), "Missing-exe ZIP must NOT overwrite existing auxiliary files");

                string invalidPeZip = Path.Combine(tmpDir, "bad_pe.zip");
                using (System.IO.Compression.ZipArchive za = System.IO.Compression.ZipFile.Open(invalidPeZip, System.IO.Compression.ZipArchiveMode.Create))
                {
                    System.IO.Compression.ZipArchiveEntry e1 = za.CreateEntry("config.txt");
                    using (StreamWriter sw = new StreamWriter(e1.Open())) sw.Write("OVERWRITTEN_BY_BAD_PE");
                    System.IO.Compression.ZipArchiveEntry e2 = za.CreateEntry("AIUsageWatcher.exe");
                    using (Stream s = e2.Open()) { byte[] junk = new byte[64]; s.Write(junk, 0, junk.Length); }
                }
                bool threwBadPe = false;
                try { AutoUpdater.ExtractUpdateZip(invalidPeZip, Path.Combine(tmpDir, "AIUsageWatcher.exe"), tmpDir); }
                catch (InvalidDataException) { threwBadPe = true; }
                Assert.True(threwBadPe, "ZIP with non-PE executable must reject");
                Assert.Equal("ORIGINAL_KEEP", File.ReadAllText(keepFile), "Invalid-PE ZIP must NOT overwrite existing auxiliary files");
            }
            finally
            {
                try { Directory.Delete(tmpDir, true); } catch { }
            }
        }

        public static void Test_LangPacks_Fallback_UsageToolFilter_And_SessionChipsWidth()
        {
            string tmpDir = Path.Combine(Path.GetTempPath(), "cuw_c3_test_" + Guid.NewGuid().ToString("N"));
            string appLang = Path.Combine(tmpDir, "app");
            string exeLang = Path.Combine(tmpDir, "exe");
            Directory.CreateDirectory(appLang);
            Directory.CreateDirectory(exeLang);
            try
            {
                // Corrupt primary JSON must NOT mark code as seen before fallback dir is scanned
                File.WriteAllText(Path.Combine(appLang, "fr.json"), "{broken json!!!");
                File.WriteAllText(Path.Combine(exeLang, "fr.json"), "{\"_name\":\"Français\",\"ok\":\"OK\"}");
                File.WriteAllText(Path.Combine(appLang, "es.json"), "null");

                List<LangPackInfo> found = LangPacks.DiscoverInDirs(new[] { appLang, exeLang });
                Assert.Equal(1, found.Count, "Corrupt fr in primary dir must fall back to valid exe fr; literal null es must be skipped");
                Assert.Equal("fr", found[0].Code);
                Assert.Equal("Français", found[0].Name);
                Assert.Equal(Path.Combine(exeLang, "fr.json"), found[0].Path);

                Dictionary<string, string> loadedStrings;
                CultureInfo loadedCult;
                Assert.False(LangPacks.Load(new LangPackInfo { Code = "es", Path = Path.Combine(appLang, "es.json") }, out loadedStrings, out loadedCult),
                    "LangPacks.Load with null JSON object must return false cleanly");
            }
            finally
            {
                try { Directory.Delete(tmpDir, true); } catch { }
            }

            UsageScanner sc = new UsageScanner(Pricing.Load());
            sc.ScanClaude = true;
            sc.ScanGemini = true;
            sc.ScanCodex = true;
            sc.ScanGeminiCli = false;
            sc.ScanOpenCode = false;
            Assert.False(sc.IsProviderScanned("Gemini CLI"), "Disabled Gemini CLI must filter out tool & session breakdown");
            Assert.False(sc.IsProviderScanned("OpenCode"), "Disabled OpenCode must filter out tool & session breakdown");
            Assert.True(sc.IsProviderScanned("Claude"));

            using (Bitmap bmp = new Bitmap(400, 64))
            using (Graphics g = Graphics.FromImage(bmp))
            using (Font f = new Font("Segoe UI", 8.5f))
            {
                List<string> labels = new List<string> { "? Confirm (12)", "● Running (34)", "✓ Done (56)" };
                Assert.True(FloatingForm.ChipsExceedWidth(g, f, labels, 150, 18, 10, 5), "Wide verbose chips must report overflow cleanly");
                Assert.False(FloatingForm.ChipsExceedWidth(g, f, labels, 800, 18, 10, 5), "Sufficient width must not force compact fallback");
            }
        }

        public static void Test_TrayApp_Lifecycle_SnapMenu_And_DiagFeatures()
        {
            AppConfig cfg = new AppConfig { ZenMode = true, ContextWindowOverride = 500000 };
            string diag = Diag.Build(cfg, new RateLimitSnapshot(), new UsageSnapshot(), null, null, 0);
            Assert.True(diag.Contains("zenMode=yes"), "Diag features must include zenMode");
            Assert.True(diag.Contains("ctxOverride=500000"), "Diag features must include ctxOverride");

            bool hadCfg = File.Exists(Paths.ConfigFile);
            byte[] prevCfgBytes = hadCfg ? File.ReadAllBytes(Paths.ConfigFile) : null;
            try
            {
                using (TrayApp app = new TrayApp())
                {
                    FieldInfo fiCompact = typeof(TrayApp).GetField("_miCompact", BindingFlags.NonPublic | BindingFlags.Instance);
                    FieldInfo fiClick = typeof(TrayApp).GetField("_miClickThrough", BindingFlags.NonPublic | BindingFlags.Instance);
                    FieldInfo fiDock = typeof(TrayApp).GetField("_miDock", BindingFlags.NonPublic | BindingFlags.Instance);
                    ToolStripMenuItem miCompact = fiCompact.GetValue(app) as ToolStripMenuItem;
                    ToolStripMenuItem miClick = fiClick.GetValue(app) as ToolStripMenuItem;
                    ToolStripMenuItem miDock = fiDock.GetValue(app) as ToolStripMenuItem;

                    MethodInfo miSnap = typeof(TrayApp).GetMethod("SnapToPosition", BindingFlags.NonPublic | BindingFlags.Instance);
                    miSnap.Invoke(app, new object[] { "top-right", "" });
                    Assert.True(miDock.Checked, "SnapToPosition must mark Dock checked");
                    Assert.False(miCompact.Enabled, "SnapToPosition must disable Compact menu item while docked");
                    Assert.False(miClick.Enabled, "SnapToPosition must disable ClickThrough menu item while docked");

                    MethodInfo miExit = typeof(TrayApp).GetMethod("ExitThreadCore", BindingFlags.NonPublic | BindingFlags.Instance);
                    miExit.Invoke(app, null);
                    MethodInfo miPostUi = typeof(TrayApp).GetMethod("PostUi", BindingFlags.NonPublic | BindingFlags.Instance);
                    bool callbackRanAfterDispose = false;
                    miPostUi.Invoke(app, new object[] { (Action)delegate { callbackRanAfterDispose = true; } });
                    Application.DoEvents();
                    Assert.False(callbackRanAfterDispose, "PostUi must ignore callbacks queued after disposal");
                }
            }
            finally
            {
                try
                {
                    if (hadCfg) File.WriteAllBytes(Paths.ConfigFile, prevCfgBytes);
                    else if (File.Exists(Paths.ConfigFile)) File.Delete(Paths.ConfigFile);
                }
                catch { }
            }
        }

        public static void Test_SetupWizard_FontReuse_And_Disposal()
        {
            AppConfig cfg = new AppConfig();
            SetupWizardForm wiz = new SetupWizardForm(cfg);
            FieldInfo fiFonts = typeof(SetupWizardForm).GetField("_fonts", BindingFlags.NonPublic | BindingFlags.Instance);
            List<Font> fonts = fiFonts.GetValue(wiz) as List<Font>;
            Assert.NotNull(fonts);
            int initialCount = fonts.Count;
            Assert.True(initialCount > 0, "SetupWizardForm must track created GDI Font handles");

            MethodInfo miApply = typeof(SetupWizardForm).GetMethod("ApplyTheme", BindingFlags.NonPublic | BindingFlags.Instance);
            miApply.Invoke(wiz, null);
            miApply.Invoke(wiz, null);
            Assert.Equal(initialCount, fonts.Count, "Reapplying SetupWizardForm theme must reuse cached button Font without allocating new GDI fonts");

            wiz.Dispose();
            Assert.Equal(0, fonts.Count, "Disposing SetupWizardForm must clear and dispose all tracked Font objects");
        }

        public static void Test_NewFeatures_Profiles_CacheDrop_Forecast_Audio_And_FocusScore()
        {
            Assert.True(AppConfig.SanitizeProfileName("../evil") == null, "Path traversal profile name must be rejected");
            Assert.Equal("Work-1", AppConfig.SanitizeProfileName("  Work-1  "));

            string profDir = Path.Combine(Path.GetTempPath(), "cuw_prof_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(profDir);
            try
            {
                AppConfig cfg = new AppConfig { Theme = "light", Compact = false, BudgetDaily = 12.5 };
                cfg.SetAdminKey("secret-keep");
                Assert.True(cfg.SaveNamedProfileInDir("WorkMain", profDir), "Named profile save must succeed");
                List<string> names = AppConfig.ListProfileNamesInDir(profDir);
                Assert.Equal(1, names.Count);
                Assert.Equal("WorkMain", names[0]);

                AppConfig target = new AppConfig { Theme = "dark" };
                target.SetAdminKey("local-secret");
                AppConfig loaded = target.LoadNamedProfileInDir("WorkMain", profDir);
                Assert.NotNull(loaded);
                Assert.Equal("light", loaded.Theme);
                Assert.Equal(12.5, loaded.BudgetDaily);
                Assert.Equal("local-secret", loaded.GetAdminKey(), "Loading profile must keep local secrets intact");
                Assert.True(AppConfig.DeleteNamedProfileInDir("WorkMain", profDir), "Deleting named profile must remove file");
                Assert.Equal(0, AppConfig.ListProfileNamesInDir(profDir).Count);

                AppConfig focus = AppConfig.BuildPresetProfile("focus", cfg);
                Assert.True(focus.ZenMode && focus.Compact && focus.MiniPillMode == "highest");

                string fakeMp3 = Path.Combine(profDir, "beep.mp3");
                File.WriteAllBytes(fakeMp3, new byte[64]);
                Assert.False(AudioAlert.IsValidWavFile(fakeMp3), "Non-WAV file must be rejected");
                string goodWav = Path.Combine(profDir, "ok.wav");
                File.WriteAllBytes(goodWav, new byte[64]);
                Assert.True(AudioAlert.IsValidWavFile(goodWav), "Valid sized .wav file must pass");
            }
            finally
            {
                try { Directory.Delete(profDir, true); } catch { }
            }

            UsageSnapshot snap = new UsageSnapshot();
            snap.ExpensiveSessions.Add(new SessionCostRecord
            {
                SessionId = "s-low", Provider = "Claude", Project = "alpha",
                Input = 60000, CacheRead = 6000, Output = 1000, Cost = 2.1, LastActivity = DateTime.Now
            });
            snap.ExpensiveSessions.Add(new SessionCostRecord
            {
                SessionId = "s-high", Provider = "Claude", Project = "beta",
                Input = 10000, CacheRead = 90000, Output = 1000, Cost = 4.0, LastActivity = DateTime.Now
            });
            SessionCostRecord drop = CacheEfficiencyAdvisor.FindCacheDrop(snap, 30);
            Assert.NotNull(drop, "Heavy session below 30% cache hit must trigger CacheEfficiencyAdvisor");
            Assert.Equal("s-low", drop.SessionId);

            DayStat[] days = new[]
            {
                new DayStat(DateTime.Today.AddDays(-1), 1000, 4.0, 10),
                new DayStat(DateTime.Today, 2000, 6.0, 20)
            };
            double eom = BudgetForecast.EstimateMonthEnd(50.0, days, new DateTime(2026, 11, 10));
            Assert.Equal(50.0 + 5.0 * 20, eom, "EOM projection must equal MTD + 7d daily pace * remaining days");
            Assert.Equal(50.0, BudgetForecast.EstimateMonthEnd(50.0, days, new DateTime(2026, 11, 30)), "Last day of month must return MTD");

            int ideScore = ProcessHelper.ScoreWindowMatch("main.cs - widget-claude - Visual Studio Code", "widget-claude", @"d:\dev\widget-claude", "Cline");
            int genericScore = ProcessHelper.ScoreWindowMatch("widget-claude - Notepad", "widget-claude", @"d:\dev\widget-claude", "Cline");
            int wtScore = ProcessHelper.ScoreWindowMatch("myproj - Windows Terminal", "", @"d:\dev\myproj\.worktrees\feat-1", "Claude");
            Assert.True(ideScore > genericScore, "Matching IDE window must rank higher than generic window");
            Assert.True(wtScore >= 3, "Worktree grandparent name must match terminal host window");
        }

        public static void Test_NewFeatures_Cline_Copilot_CustomJsonl_And_ExplorerFilters()
        {
            Pricing pricing = Pricing.Load();
            UsageScanner sc = new UsageScanner(pricing);
            sc.CacheFilePath = null;
            sc.ScanCline = true;
            sc.ScanCopilot = true;
            sc.ScanClaude = false; sc.ScanGemini = false; sc.ScanCodex = false; sc.ScanGeminiCli = false; sc.ScanOpenCode = false;

            string tmp = Path.Combine(Path.GetTempPath(), "cuw_newscan_" + Guid.NewGuid().ToString("N"));
            string clineTaskDir = Path.Combine(tmp, "cline", "task123");
            string cpDir = Path.Combine(tmp, ".copilot");
            string customDir = Path.Combine(tmp, "custom");
            Directory.CreateDirectory(clineTaskDir);
            Directory.CreateDirectory(cpDir);
            Directory.CreateDirectory(customDir);
            try
            {
                long tsMs = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
                string clineFile = Path.Combine(clineTaskDir, "ui_messages.json");
                File.WriteAllText(clineFile, string.Format(CultureInfo.InvariantCulture,
                    "[{{\"ts\":{0},\"type\":\"say\",\"say\":\"api_req_started\",\"text\":\"{{\\\"tokensIn\\\":2000,\\\"tokensOut\\\":500,\\\"cacheReads\\\":1000,\\\"cacheWrites\\\":200,\\\"cost\\\":0.12,\\\"model\\\":\\\"claude-sonnet-4-5\\\"}}\"}}]",
                    tsMs));

                string cpFile = Path.Combine(cpDir, "session.json");
                File.WriteAllText(cpFile, string.Format(CultureInfo.InvariantCulture,
                    "{{\"sessionId\":\"cp1\",\"cwd\":\"d:\\\\repos\\\\appX\",\"requests\":[{{\"requestId\":\"r1\",\"modelId\":\"gpt-4.1\",\"timestamp\":{0},\"result\":{{\"usage\":{{\"promptTokens\":3000,\"completionTokens\":400,\"cachedTokens\":500}}}}}}]}}",
                    tsMs));

                string customFile = Path.Combine(customDir, "aider.jsonl");
                File.WriteAllText(customFile, string.Format(CultureInfo.InvariantCulture,
                    "{{\"timestamp\":\"{0}\",\"provider\":\"Custom\",\"model\":\"gpt-5\",\"project\":\"customProj\",\"usage\":{{\"input_tokens\":1000,\"output_tokens\":200,\"cached_tokens\":100}},\"cost\":0.05}}",
                    DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")));

                sc.ClineRoots = delegate { return new List<string> { Path.Combine(tmp, "cline") }; };
                sc.CopilotDirs = delegate { return new List<string> { cpDir }; };
                sc.CustomJsonlDirs = customDir;
                sc.Roots = delegate { return new List<string>(); };
                sc.Scan();

                UsageSnapshot snap = sc.Snapshot();
                Assert.Equal(3700L, snap.ClineToday.Total, "Cline tokensIn+Out+CacheReads+Writes must total 3700");
                Assert.Equal(3400L, snap.CopilotToday.Total, "Copilot tokens total must equal 3400");
                Assert.Equal(8400L, snap.TodayTotal.Total, "TodayTotal must sum Cline + Copilot + Custom");

                // Verify ProjectExplorerForm cross-tab provider + search filters
                snap.ToolBreakdown.Add(new ToolUsageRecord { Provider = "Cline", Tool = "edit_file", Calls = 3, Tokens = 1200, Cost = 0.05 });
                snap.ToolBreakdown.Add(new ToolUsageRecord { Provider = "Copilot", Tool = "search_grep", Calls = 2, Tokens = 900, Cost = 0.02 });
                using (ProjectExplorerForm form = new ProjectExplorerForm(snap, new HistoryStore(Path.Combine(tmp, "hist.json")), 10.0, 100.0, 45.0))
                {
                    IntPtr h = form.Handle;
                    ComboBox cbProv = typeof(ProjectExplorerForm).GetField("_providerFilter", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form) as ComboBox;
                    ListView lvProj = typeof(ProjectExplorerForm).GetField("_list", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form) as ListView;
                    ListView lvSess = typeof(ProjectExplorerForm).GetField("_sessionList", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form) as ListView;
                    ListView lvTools = typeof(ProjectExplorerForm).GetField("_toolList", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form) as ListView;

                    cbProv.SelectedIndex = Array.IndexOf((string[])typeof(ProjectExplorerForm).GetField("FilterProviders", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null), "Copilot");
                    Assert.Equal(1, lvTools.Items.Count, "Copilot provider filter must narrow Tools list to Copilot");
                    Assert.Equal("search_grep", lvTools.Items[0].Text);
                    Assert.True(lvSess.Items.Count >= 1 && lvSess.Items[0].SubItems[1].Text == "Copilot", "Provider filter must also filter Sessions list");
                    Assert.True(lvProj.Items.Count >= 1, "Projects list must stay populated for Copilot");
                }
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch { }
            }
        }

        public static void Test_Pass2_CopilotFullCache_CustomCase_SettingsPreservation_And_Exports()
        {
            Pricing pricing = Pricing.Load();
            UsageScanner sc = new UsageScanner(pricing);
            sc.CacheFilePath = null;
            sc.ScanClaude = true;
            sc.ScanGemini = false; sc.ScanCodex = true; sc.ScanGeminiCli = false; sc.ScanOpenCode = false;
            sc.ScanCline = true; sc.ScanCopilot = true;

            string tmp = Path.Combine(Path.GetTempPath(), "cuw_pass2_test_" + Guid.NewGuid().ToString("N"));
            string dirA = Path.Combine(tmp, "a");
            string dirB = Path.Combine(tmp, "b");
            string cpDir = Path.Combine(tmp, ".copilot");
            Directory.CreateDirectory(dirA);
            Directory.CreateDirectory(dirB);
            Directory.CreateDirectory(cpDir);
            try
            {
                long tsMs = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
                // 100% cached Copilot turn: inTok == cRead (1000 == 1000) must yield Input=0, CacheRead=1000, Total=1200 (not double-counted 2200)
                File.WriteAllText(Path.Combine(cpDir, "fullcache.json"), string.Format(CultureInfo.InvariantCulture,
                    "{{\"sessionId\":\"cp-fc\",\"requests\":[{{\"requestId\":\"r1\",\"modelId\":\"gpt-4.1\",\"timestamp\":{0},\"result\":{{\"usage\":{{\"promptTokens\":1000,\"completionTokens\":200,\"cachedTokens\":1000}}}}}}]}}",
                    tsMs));

                // Same basename usage.jsonl in two directories with lowercase providers
                string nowIso = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                File.WriteAllText(Path.Combine(dirA, "usage.jsonl"), string.Format(CultureInfo.InvariantCulture,
                    "{{\"timestamp\":\"{0}\",\"provider\":\"claude\",\"model\":\"claude-sonnet-4-5\",\"project\":\"pA\",\"usage\":{{\"input_tokens\":500,\"output_tokens\":100}}}}",
                    nowIso));
                File.WriteAllText(Path.Combine(dirB, "usage.jsonl"), string.Format(CultureInfo.InvariantCulture,
                    "{{\"timestamp\":\"{0}\",\"provider\":\"codex\",\"model\":\"gpt-5-codex\",\"project\":\"pB\",\"usage\":{{\"input_tokens\":600,\"output_tokens\":50}}}}",
                    nowIso));

                sc.Roots = delegate { return new List<string>(); };
                sc.CopilotDirs = delegate { return new List<string> { cpDir }; };
                sc.CustomJsonlRoots = delegate { return new List<string> { dirA, dirB }; };
                sc.Scan();

                UsageSnapshot s1 = sc.Snapshot();
                Assert.Equal(0L, s1.CopilotToday.Input, "100%-cached Copilot turn must not double-count cached promptTokens into Input");
                Assert.Equal(1000L, s1.CopilotToday.CacheRead);
                Assert.Equal(1200L, s1.CopilotToday.Total);
                Assert.Equal(600L, s1.ClaudeToday.Total, "Lowercase 'claude' provider in Custom JSONL must normalize canonically to ClaudeToday");
                Assert.Equal(650L, s1.CodexToday.Total, "Same-filename usage.jsonl in second folder must not collide on dedup key");

                // StatusFormatter must include Cline/Copilot when week total > 0
                s1.ClineToday.Input = 100; s1.ClineWeek.Input = 100;
                string statusTxt = StatusFormatter.Format(new RateLimitSnapshot(), s1, true, false, true, null);
                Assert.True(statusTxt.Contains("Copilot:"), "StatusFormatter must include Copilot summary");
                Assert.True(statusTxt.Contains("Cline:"), "StatusFormatter must include Cline summary");

                // FloatingForm budget EOM forecast & daily target line must render cleanly
                bool oldZen = Opts.ZenMode;
                try
                {
                    Opts.ZenMode = false;
                    using (FloatingForm ff = new FloatingForm())
                    {
                        IntPtr fh = ff.Handle;
                        ViewModel vm = new ViewModel
                        {
                            Usage = s1,
                            Limits = new RateLimitSnapshot(),
                            BudgetDaily = 5.0,
                            BudgetMonthly = 100.0,
                            MonthCost = 40.0,
                            History = new[] { new DayStat(DateTime.Today.AddDays(-1), 500, 2.0, 2), new DayStat(DateTime.Today, 1200, 3.0, 3) }
                        };
                        ff.Compact = false;
                        ff.SetData(vm);
                        using (Bitmap bmp = new Bitmap(Math.Max(10, ff.Width), Math.Max(10, ff.Height)))
                            ff.DrawToBitmap(bmp, new Rectangle(0, 0, ff.Width, ff.Height));
                    }
                }
                finally { Opts.ZenMode = oldZen; }

                // SettingsForm unbuilt tabs must preserve new config fields (Cline, Copilot, CacheDrop, CustomSoundPath)
                bool hadCfg = File.Exists(Paths.ConfigFile);
                byte[] prevCfg = hadCfg ? File.ReadAllBytes(Paths.ConfigFile) : null;
                try
                {
                    AppConfig cfg = new AppConfig
                    {
                        CacheDropAlertEnabled = false,
                        CacheDropMinPercent = 42,
                        CustomSoundPath = @"C:\custom\ping.wav",
                        ScanCline = false,
                        ScanCopilot = false,
                        CustomJsonlDirs = @"C:\logs"
                    };
                    SettingsCallbacks cb = new SettingsCallbacks();
                    using (SettingsForm sf = new SettingsForm(cfg, cb))
                    {
                        IntPtr sh = sf.Handle;
                        MethodInfo miApply = typeof(SettingsForm).GetMethod("OnApply", BindingFlags.NonPublic | BindingFlags.Instance);
                        miApply.Invoke(sf, new object[] { null, EventArgs.Empty });
                        Assert.False(cfg.CacheDropAlertEnabled, "Unbuilt Alerts tab must preserve CacheDropAlertEnabled");
                        Assert.Equal(42, cfg.CacheDropMinPercent, "Unbuilt Alerts tab must preserve CacheDropMinPercent");
                        Assert.Equal(@"C:\custom\ping.wav", cfg.CustomSoundPath, "Unbuilt Alerts tab must preserve CustomSoundPath");
                        Assert.False(cfg.ScanCline, "Unbuilt Data tab must preserve ScanCline");
                        Assert.False(cfg.ScanCopilot, "Unbuilt Data tab must preserve ScanCopilot");
                        Assert.Equal(@"C:\logs", cfg.CustomJsonlDirs, "Unbuilt Data tab must preserve CustomJsonlDirs");
                    }
                }
                finally
                {
                    try
                    {
                        if (hadCfg) File.WriteAllBytes(Paths.ConfigFile, prevCfg);
                        else if (File.Exists(Paths.ConfigFile)) File.Delete(Paths.ConfigFile);
                    }
                    catch { }
                }
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch { }
            }
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
