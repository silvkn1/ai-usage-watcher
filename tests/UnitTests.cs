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
            Assert.False(UsageScanner.IsAntigravityModel("claude-3-5-sonnet"));
            Assert.False(UsageScanner.IsAntigravityModel("claude-opus-4-6"));
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
