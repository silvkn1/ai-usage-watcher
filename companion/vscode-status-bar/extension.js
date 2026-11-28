const vscode = require('vscode');
const fs = require('fs');
const path = require('path');
const os = require('os');

let statusBarItem = null;
let pollTimer = null;
let fileWatcher = null;
let watchDebounce = null;

function getStatusFilePath() {
    const userProfile = os.homedir();
    const primaryPath = path.join(userProfile, '.ai-usage-status.json');
    if (fs.existsSync(primaryPath)) {
        return primaryPath;
    }
    const localAppData = process.env.LOCALAPPDATA || path.join(userProfile, 'AppData', 'Local');
    const appStatusPath = path.join(localAppData, 'AIUsageWatcher', 'status.json');
    if (fs.existsSync(appStatusPath)) {
        return appStatusPath;
    }
    const fallbackPath = path.join(localAppData, 'ClaudeUsageWidget', 'status.json');
    if (fs.existsSync(fallbackPath)) {
        return fallbackPath;
    }
    return primaryPath;
}

function getAppCommandPath() {
    const localAppData = process.env.LOCALAPPDATA || path.join(os.homedir(), 'AppData', 'Local');
    const newDir = path.join(localAppData, 'AIUsageWatcher');
    const oldDir = path.join(localAppData, 'ClaudeUsageWidget');
    if (fs.existsSync(newDir)) return path.join(newDir, 'command.json');
    if (fs.existsSync(oldDir)) return path.join(oldDir, 'command.json');
    return path.join(newDir, 'command.json');
}

function sendWidgetCommand(cmd) {
    try {
        const cmdPath = getAppCommandPath();
        const dir = path.dirname(cmdPath);
        if (!fs.existsSync(dir)) {
            vscode.window.showWarningMessage('AI Usage Watcher data folder not found: ' + dir);
            return false;
        }
        const payload = JSON.stringify({ cmd: cmd, ts: Date.now() });
        const tmpPath = cmdPath + '.tmp.' + process.pid + '.' + Date.now();
        try {
            fs.writeFileSync(tmpPath, payload, 'utf8');
            fs.renameSync(tmpPath, cmdPath);
        } catch (e) {
            try { if (fs.existsSync(tmpPath)) fs.unlinkSync(tmpPath); } catch (_) {}
            fs.writeFileSync(cmdPath, payload, 'utf8');
        }
        return true;
    } catch (e) {
        vscode.window.showErrorMessage('Failed to send command to AI Usage Watcher: ' + e.message);
        return false;
    }
}

function formatTokens(count) {
    if (!count || count <= 0) return '0';
    if (count >= 1000000) return (count / 1000000).toFixed(1) + 'M';
    if (count >= 1000) return (count / 1000).toFixed(1) + 'k';
    return String(count);
}

function escapeMarkdown(value) {
    return String(value == null ? '' : value)
        .replace(/[\r\n]+/g, ' ')
        .replace(/[\\`*_{}[\]()#+\-!|~<>]/g, '\\$&');
}

function updateStatusBar() {
    if (!statusBarItem) return;

    const filePath = getStatusFilePath();
    if (!fs.existsSync(filePath)) {
        statusBarItem.text = '$(dashboard) AI: No Data';
        statusBarItem.tooltip = 'AI Usage Watcher is not running or has not exported status yet.\nRun AI Usage Watcher to see live metrics.';
        statusBarItem.show();
        return;
    }

    try {
        const raw = fs.readFileSync(filePath, 'utf8').replace(/^\uFEFF/, '');
        if (!raw.trim()) {
            statusBarItem.text = '$(dashboard) AI: No Data';
            statusBarItem.tooltip = 'AI Usage Watcher status file is empty.';
            statusBarItem.show();
            return;
        }
        const data = JSON.parse(raw);

        const parts = [];
        const md = new vscode.MarkdownString();
        md.isTrusted = false;
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
        if (typeof data.codex_5h_percent === 'number') {
            parts.push(`CX: ${Math.round(data.codex_5h_percent)}%`);
            md.appendMarkdown(`- **Codex CLI 5h Quota**: ${data.codex_5h_percent}%\n`);
        }
        if (typeof data.codex_7d_percent === 'number') {
            md.appendMarkdown(`- **Codex CLI 7d Quota**: ${data.codex_7d_percent}%\n`);
        }

        if (data.codex_today_tokens > 0 || data.codex_today_cost_usd > 0) {
            if (typeof data.codex_5h_percent !== 'number' && typeof data.claude_5h_percent !== 'number' && typeof data.antigravity_5h_percent !== 'number') {
                parts.push(`CX: ${formatTokens(data.codex_today_tokens || 0)}`);
            }
            const cxCost = Number(data.codex_today_cost_usd || 0).toFixed(2);
            md.appendMarkdown(`- **Codex CLI Today**: $${cxCost} (${formatTokens(data.codex_today_tokens || 0)} tokens)\n`);
        }

        if (data.gemini_cli_today_tokens > 0 || data.gemini_cli_today_cost_usd > 0) {
            md.appendMarkdown(`- **Gemini CLI Today**: $${Number(data.gemini_cli_today_cost_usd || 0).toFixed(2)} (${formatTokens(data.gemini_cli_today_tokens || 0)} tokens)\n`);
        }
        if (data.opencode_today_tokens > 0 || data.opencode_today_cost_usd > 0) {
            md.appendMarkdown(`- **OpenCode Today**: $${Number(data.opencode_today_cost_usd || 0).toFixed(2)} (${formatTokens(data.opencode_today_tokens || 0)} tokens)\n`);
        }

        if (data.today_cost_formatted) {
            parts.push(data.today_cost_formatted);
        } else if (data.today_tokens) {
            parts.push(formatTokens(data.today_tokens) + ' tok');
        }

        if (data.today_cost_formatted) {
            md.appendMarkdown(`- **Today Cost**: ${escapeMarkdown(data.today_cost_formatted)} (${formatTokens(data.today_tokens)} tokens)\n`);
        }
        if (typeof data.today_saved_usd === 'number' && data.today_saved_usd > 0) {
            md.appendMarkdown(`- **⚡ Cache Saved**: $${data.today_saved_usd.toFixed(2)} (${data.cache_hit_percent || 0}% hit)\n`);
        }
        if (data.runway_minutes) {
            md.appendMarkdown(`- **⏳ Estimated Runway**: ~${data.runway_minutes} min remaining\n`);
        }
        if (data.last_active_project) {
            md.appendMarkdown(`- **📁 Active Project**: \`${escapeMarkdown(data.last_active_project)}\`\n`);
        }
        const runSess = data.running_sessions || 0;
        const confSess = data.confirm_sessions || 0;
        const doneSess = data.done_sessions || 0;
        if (confSess > 0) parts.push(`?${confSess}`);
        else if (runSess > 0) parts.push(`●${runSess}`);
        if (runSess > 0 || confSess > 0 || doneSess > 0) {
            md.appendMarkdown(`- **⚡ Sessions**: ${runSess} running · ${confSess} needs confirm · ${doneSess} done\n`);
        }
        if (data.ollama_online) {
            const count = data.ollama_model_count || 0;
            const loaded = data.ollama_loaded_count || 0;
            const vramGb = data.ollama_vram_bytes > 0 ? (data.ollama_vram_bytes / (1024 * 1024 * 1024)).toFixed(1) + ' GB VRAM' : '';
            const loadedModels = Array.isArray(data.ollama_loaded_models) && data.ollama_loaded_models.length > 0
                ? data.ollama_loaded_models.map(escapeMarkdown).join(', ')
                : (Array.isArray(data.ollama_models) ? data.ollama_models.map(escapeMarkdown).join(', ') : '');
            const extra = [loaded > 0 ? `${loaded} loaded` : '', vramGb, loadedModels].filter(Boolean).join(' · ');
            md.appendMarkdown(`- **🦙 Ollama Local**: Online (${count} models${extra ? ' · ' + extra : ''})\n`);
        }

        md.appendMarkdown('\n---\n*Click for Quick Actions menu*');

        statusBarItem.text = `$(dashboard) ${parts.join(' | ') || 'AI Active'}`;
        statusBarItem.tooltip = md;
        statusBarItem.show();
    } catch (err) {
        statusBarItem.text = '$(dashboard) AI: Error';
        statusBarItem.tooltip = `Failed to read status: ${err.message}`;
        statusBarItem.show();
    }
}

function buildStatusClipboardSummary() {
    const filePath = getStatusFilePath();
    if (!fs.existsSync(filePath)) return 'AI Usage Watcher: status file not found.';
    const raw = fs.readFileSync(filePath, 'utf8').replace(/^\uFEFF/, '').trim();
    if (!raw) return 'AI Usage Watcher: status file is empty.';
    const d = JSON.parse(raw);
    if (!d || typeof d !== 'object') return 'AI Usage Watcher: invalid status data.';
    const lines = ['AI Usage Watcher Summary'];
    if (typeof d.claude_5h_percent === 'number') lines.push(`- Claude: 5h ${d.claude_5h_percent}% | 7d ${d.claude_7d_percent ?? 'n/a'}%`);
    if (typeof d.antigravity_5h_percent === 'number') lines.push(`- Antigravity: 5h ${d.antigravity_5h_percent}% | 7d ${d.antigravity_7d_percent ?? 'n/a'}%`);
    if (typeof d.codex_5h_percent === 'number') lines.push(`- Codex: 5h ${d.codex_5h_percent}% | 7d ${d.codex_7d_percent ?? 'n/a'}%`);
    if (d.today_cost_formatted) lines.push(`- Today: ${d.today_cost_formatted} (${formatTokens(d.today_tokens || 0)} tokens)`);
    if (d.last_active_project) lines.push(`- Project: ${d.last_active_project}`);
    return lines.join('\n');
}

function activate(context) {
    statusBarItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right, 100);
    statusBarItem.name = 'AI Usage Watcher';
    statusBarItem.command = 'aiUsageWatcher.menu';
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

    const refreshCmd = vscode.commands.registerCommand('aiUsageWatcher.refresh', () => {
        sendWidgetCommand('refresh');
        updateStatusBar();
    });
    context.subscriptions.push(refreshCmd);

    const menuCmd = vscode.commands.registerCommand('aiUsageWatcher.menu', async () => {
        const pick = await vscode.window.showQuickPick([
            { label: '$(refresh) Force Refresh Widget', description: 'Rescan logs & reload limits', action: 'refresh' },
            { label: '$(graph) Open Project Cost Explorer', description: 'Open desktop explorer window', action: 'open_explorer' },
            { label: '$(terminal) Open Terminal in Active Project', description: 'Launch terminal in latest active directory', action: 'open_terminal' },
            { label: '$(check) Mark Sessions Checked (? / ✓)', description: 'Clear finished & confirmation badges on widget', action: 'ack_sessions' },
            { label: '$(layout) Toggle Compact / Full Mode', description: 'Switch floating pill & card view', action: 'toggle_compact' },
            { label: '$(clippy) Copy Status Summary', description: 'Copy quota & today cost to clipboard', action: 'copy_local' },
            { label: '$(terminal) Copy /compact Command', description: 'Copy /compact for long AI sessions', action: 'copy_compact' },
            { label: '$(json) Open Raw Status JSON', description: 'Inspect status.json in editor', action: 'open_json' }
        ], { placeHolder: 'AI Usage Watcher Actions' });
        if (!pick) return;
        if (pick.action === 'open_json') {
            await vscode.commands.executeCommand('aiUsageWatcher.openStatus');
        } else if (pick.action === 'copy_local') {
            try {
                await vscode.env.clipboard.writeText(buildStatusClipboardSummary());
                vscode.window.showInformationMessage('AI Usage summary copied to clipboard.');
            } catch (e) {
                sendWidgetCommand('copy_status');
            }
        } else if (pick.action === 'copy_compact') {
            await vscode.env.clipboard.writeText('/compact');
            vscode.window.showInformationMessage('Copied /compact to clipboard.');
        } else {
            sendWidgetCommand(pick.action);
            if (pick.action === 'refresh') setTimeout(updateStatusBar, 400);
        }
    });
    context.subscriptions.push(menuCmd);

    let watchedTarget = null;
    function bindWatcher() {
        const targetPath = getStatusFilePath();
        const watchTarget = fs.existsSync(targetPath) ? targetPath : path.dirname(targetPath);
        const isHome = watchTarget === os.homedir();
        if (isHome) return; // Avoid high-churn fs.watch on %USERPROFILE% before status file exists.
        if (fileWatcher && watchedTarget === watchTarget) return;
        if (fileWatcher) {
            try { fileWatcher.close(); } catch (e) {}
            fileWatcher = null;
        }
        try {
            if (fs.existsSync(watchTarget)) {
                watchedTarget = watchTarget;
                fileWatcher = fs.watch(watchTarget, (event, filename) => {
                    if (!filename || (filename.indexOf('status') !== -1 && !filename.endsWith('.tmp'))) {
                        if (watchDebounce) clearTimeout(watchDebounce);
                        watchDebounce = setTimeout(updateStatusBar, 120);
                    }
                });
                fileWatcher.on('error', () => {
                    try { if (fileWatcher) fileWatcher.close(); } catch (e) {}
                    fileWatcher = null;
                    watchedTarget = null;
                });
            }
        } catch (e) {
            fileWatcher = null;
            watchedTarget = null;
        }
    }

    // Initial render
    updateStatusBar();
    bindWatcher();

    // Poll every 5 seconds for live responsiveness & re-bind watcher if status file appears later
    pollTimer = setInterval(() => {
        updateStatusBar();
        bindWatcher();
    }, 5000);
}

function deactivate() {
    if (pollTimer) {
        clearInterval(pollTimer);
        pollTimer = null;
    }
    if (watchDebounce) {
        clearTimeout(watchDebounce);
        watchDebounce = null;
    }
    if (fileWatcher) {
        try { fileWatcher.close(); } catch (e) {}
        fileWatcher = null;
    }
}

module.exports = {
    activate,
    deactivate
};
