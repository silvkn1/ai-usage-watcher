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

                string pkgJson = GetPackageJson();
                string extJs = GetExtensionJs();

                List<string> successful = new List<string>();
                foreach (string target in targets)
                {
                    Directory.CreateDirectory(target);
                    File.WriteAllText(Path.Combine(target, "package.json"), pkgJson, Encoding.UTF8);
                    File.WriteAllText(Path.Combine(target, "extension.js"), extJs, Encoding.UTF8);
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

        private static string GetPackageJson()
        {
            string diskPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "companion", "vscode-status-bar", "package.json");
            if (File.Exists(diskPath))
            {
                try { return File.ReadAllText(diskPath, Encoding.UTF8); } catch { }
            }
            return @"{
  ""name"": ""ai-usage-watcher"",
  ""displayName"": ""AI Usage Watcher Companion"",
  ""description"": ""Live status bar metrics for Claude Code & Antigravity quotas, cost, and Ollama"",
  ""version"": ""1.1.0"",
  ""publisher"": ""silvkn1"",
  ""repository"": {
    ""type"": ""git"",
    ""url"": ""https://github.com/silvkn1/ai-usage-watcher.git""
  },
  ""engines"": {
    ""vscode"": ""^1.70.0""
  },
  ""categories"": [""Other""],
  ""activationEvents"": [""*""],
  ""main"": ""./extension.js"",
  ""contributes"": {
    ""commands"": [
      {
        ""command"": ""aiUsageWatcher.openStatus"",
        ""title"": ""AI Usage: Open Status File""
      },
      {
        ""command"": ""aiUsageWatcher.refresh"",
        ""title"": ""AI Usage: Refresh Metrics""
      }
    ]
  }
}";
        }

        private static string GetExtensionJs()
        {
            string diskPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "companion", "vscode-status-bar", "extension.js");
            if (File.Exists(diskPath))
            {
                try { return File.ReadAllText(diskPath, Encoding.UTF8); } catch { }
            }
            return @"const vscode = require('vscode');
const fs = require('fs');
const path = require('path');
const os = require('os');

let statusBarItem = null;
let pollTimer = null;
let fileWatcher = null;

function getStatusFilePath() {
    const userProfile = os.homedir();
    const primaryPath = path.join(userProfile, '.ai-usage-status.json');
    if (fs.existsSync(primaryPath)) return primaryPath;
    const localAppData = process.env.LOCALAPPDATA || path.join(userProfile, 'AppData', 'Local');
    const fallbackPath = path.join(localAppData, 'ClaudeUsageWidget', 'status.json');
    if (fs.existsSync(fallbackPath)) return fallbackPath;
    return primaryPath;
}

function formatTokens(count) {
    if (!count || count <= 0) return '0';
    if (count >= 1000000) return (count / 1000000).toFixed(1) + 'M';
    if (count >= 1000) return (count / 1000).toFixed(1) + 'k';
    return String(count);
}

function updateStatusBar() {
    if (!statusBarItem) return;
    const filePath = getStatusFilePath();
    if (!fs.existsSync(filePath)) {
        statusBarItem.text = '$(dashboard) AI: No Data';
        statusBarItem.tooltip = 'AI Usage Watcher is not running or has not exported status yet.';
        statusBarItem.show();
        return;
    }
    try {
        const raw = fs.readFileSync(filePath, 'utf8');
        const data = JSON.parse(raw);
        const parts = [];
        const md = new vscode.MarkdownString();
        md.isTrusted = true;
        md.appendMarkdown('### 📊 AI Usage Watcher Status\n\n');
        if (typeof data.claude_5h_percent === 'number') {
            parts.push(`C: ${Math.round(data.claude_5h_percent)}%`);
            md.appendMarkdown(`- **Claude Code 5h Quota**: ${data.claude_5h_percent}%\n`);
        }
        if (typeof data.claude_7d_percent === 'number') {
            md.appendMarkdown(`- **Claude Code 7d Quota**: ${data.claude_7d_percent}%\n`);
        }
        if (typeof data.antigravity_5h_percent === 'number') {
            parts.push(`AG: ${Math.round(data.antigravity_5h_percent)}%`);
            md.appendMarkdown(`- **Antigravity 5h Quota**: ${data.antigravity_5h_percent}%\n`);
        }
        if (typeof data.antigravity_7d_percent === 'number') {
            md.appendMarkdown(`- **Antigravity 7d Quota**: ${data.antigravity_7d_percent}%\n`);
        }
        if (data.today_cost_formatted) {
            parts.push(data.today_cost_formatted);
            md.appendMarkdown(`- **Today Cost**: ${data.today_cost_formatted} (${formatTokens(data.today_tokens)} tokens)\n`);
        } else if (data.today_tokens) {
            parts.push(formatTokens(data.today_tokens) + ' tok');
        }
        if (typeof data.today_saved_usd === 'number' && data.today_saved_usd > 0) {
            md.appendMarkdown(`- **⚡ Cache Saved**: $${data.today_saved_usd.toFixed(2)} (${data.cache_hit_percent || 0}% hit)\n`);
        }
        if (data.runway_minutes) {
            md.appendMarkdown(`- **⏳ Estimated Runway**: ~${data.runway_minutes} min remaining\n`);
        }
        if (data.last_active_project) {
            md.appendMarkdown(`- **📁 Active Project**: \`${data.last_active_project}\`\n`);
        }
        if (data.ollama_online) {
            const count = data.ollama_model_count || 0;
            const models = Array.isArray(data.ollama_models) ? data.ollama_models.join(', ') : '';
            md.appendMarkdown(`- **🦙 Ollama Local**: Online (${count} models${models ? ': ' + models : ''})\n`);
        }
        md.appendMarkdown('\n---\n*Click to open status file*');
        statusBarItem.text = `$(dashboard) ${parts.join(' | ') || 'AI Active'}`;
        statusBarItem.tooltip = md;
        statusBarItem.show();
    } catch (err) {
        statusBarItem.text = '$(dashboard) AI: Error';
        statusBarItem.tooltip = `Failed to read status: ${err.message}`;
        statusBarItem.show();
    }
}

function activate(context) {
    statusBarItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right, 100);
    statusBarItem.command = 'aiUsageWatcher.openStatus';
    context.subscriptions.push(statusBarItem);
    const openCmd = vscode.commands.registerCommand('aiUsageWatcher.openStatus', async () => {
        const filePath = getStatusFilePath();
        if (fs.existsSync(filePath)) {
            const doc = await vscode.workspace.openTextDocument(filePath);
            await vscode.window.showTextDocument(doc);
        } else {
            vscode.window.showInformationMessage('AI Usage status file not found: ' + filePath);
        }
    });
    context.subscriptions.push(openCmd);
    const refreshCmd = vscode.commands.registerCommand('aiUsageWatcher.refresh', () => { updateStatusBar(); });
    context.subscriptions.push(refreshCmd);
    updateStatusBar();
    pollTimer = setInterval(updateStatusBar, 5000);
    try {
        const targetPath = getStatusFilePath();
        const watchDir = path.dirname(targetPath);
        if (fs.existsSync(watchDir)) {
            fileWatcher = fs.watch(watchDir, (event, filename) => {
                if (filename && filename.includes('status')) updateStatusBar();
            });
        }
    } catch (e) {}
}

function deactivate() {
    if (pollTimer) { clearInterval(pollTimer); pollTimer = null; }
    if (fileWatcher) { try { fileWatcher.close(); } catch (e) {} fileWatcher = null; }
}

module.exports = { activate, deactivate };";
        }
    }
}
