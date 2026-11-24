# AI Usage Watcher - VS Code & Cursor Companion

Lightweight status bar companion for **AI Usage Watcher**. Reads `~/.ai-usage-status.json` and renders real-time quota and cost metrics directly in your editor's bottom status bar.

## Features
- **Live Status Bar**: Displays Claude Code quota, Antigravity quota, today's cost, and Ollama status.
  Example: `$(dashboard) C: 42% | AG: 20% | $0.85`
- **Rich Hover Tooltip**: Shows 5h/7d breakdown, cache savings, runway minutes, and active project.
- **1-Click Status Inspection**: Clicking the status bar opens `~/.ai-usage-status.json` in VS Code/Cursor.
- **Zero Config**: Automatically picks up data exported by AI Usage Watcher.

## Installation
Run `install.ps1` or install directly from AI Usage Watcher's system tray menu:
**Right-click Tray Icon -> Install VS Code / Cursor Status Bar...**

Or install via PowerShell:
```powershell
powershell -ExecutionPolicy Bypass -File companion\install.ps1
```
Then reload your VS Code or Cursor window (`Ctrl+Shift+P` -> `Developer: Reload Window`).
