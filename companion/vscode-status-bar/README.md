# AI Usage Watcher Companion

Live status bar monitor and quick actions controller for **AI Usage Watcher** on Windows — displaying real-time AI coding assistant quotas, daily token spend, active session badges, and local Ollama telemetry directly inside VS Code, Cursor, Antigravity, and Windsurf.

## Features

- **Live Status Bar Metrics**: Shows compact rolling 5h/7d quotas and daily cost right in the bottom status bar (for example `$(dashboard) C: 42% | AG: 20% | $0.85`).
- **Multi-Provider Hover Tooltip**: Hover over the status bar item to inspect:
  - **Claude Code** & **Antigravity** 5-hour / 7-day rolling quota usage
  - **Codex CLI**, **Gemini CLI**, and **OpenCode** today tokens and estimated spend
  - **Prompt Cache Savings** and cache hit rate
  - **Estimated Quota Runway** and **Active Project**
  - **Session Activity Counts** (`●` running, `?` needs confirmation, `✓` done)
  - **Ollama Local** status (loaded models and active VRAM usage)
- **Quick Actions Menu** *(click the status bar item)*:
  - **Force Refresh Widget** — trigger an immediate rescan and limit reload
  - **Open Project Cost Explorer** — open the desktop 35-day heatmap & cost analytics window
  - **Open Terminal in Active Project** — launch a terminal in the latest active project folder
  - **Mark Sessions Checked (`?` / `✓`)** — acknowledge pending session badges
  - **Toggle Compact / Full Mode** — switch floating HUD pill vs card view
  - **Copy Status Summary** / **Copy `/compact` Command** — copy metrics or `/compact` directly to clipboard
  - **Open Raw Status JSON** — inspect the exported status JSON in the editor

## Requirements

This companion reads status snapshots written locally by the **AI Usage Watcher** desktop application (`AIUsageWatcher.exe`):

- Status file locations checked automatically:
  1. `%USERPROFILE%\.ai-usage-status.json`
  2. `%LOCALAPPDATA%\AIUsageWatcher\status.json`
- Bidirectional desktop actions write a short command trigger to `%LOCALAPPDATA%\AIUsageWatcher\command.json`.

If the desktop app is not running yet, the status bar shows `AI: No Data` and updates automatically as soon as metrics are exported.

## Commands

| Command ID | Title | Description |
|---|---|---|
| `aiUsageWatcher.menu` | AI Usage: Quick Actions Menu | Open quick actions picker for desktop HUD & clipboard |
| `aiUsageWatcher.openStatus` | AI Usage: Open Status File | Open the raw local status JSON file |
| `aiUsageWatcher.refresh` | AI Usage: Refresh Metrics | Request widget refresh and update the status bar |

## Privacy & License

- **100% Local**: Reads only local JSON status files on your machine. No telemetry, analytics, or network requests are sent by this extension.
- **License**: [MIT License](https://github.com/silvkn1/ai-usage-watcher/blob/main/LICENSE)
