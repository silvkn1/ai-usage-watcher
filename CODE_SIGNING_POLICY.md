# Code Signing Policy

## Signing Service

Free code signing provided by [SignPath.io](https://signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

## Source Repository & Trusted Builds

- **Repository**: `https://github.com/silvkn1/ai-usage-watcher`
- **Build System**: GitHub Actions ([`.github/workflows/ci.yml`](.github/workflows/ci.yml))
- Only binaries compiled directly from version-controlled source on GitHub Actions from tagged releases (`v*`) are submitted for release code signing. Locally built binaries are never submitted.

## Team Roles

- **Authors / Maintainers**: `silvkn1`
- **Reviewers**: `silvkn1`
- **Approvers**: `silvkn1`

Pull requests and release tags are reviewed by the project maintainer prior to release signing.

## Privacy Policy

AI Usage Watcher runs locally on the user's machine and includes no analytics, crash telemetry, or tracking servers operated by the author.

- Local usage metrics are read directly from local configuration/session files (Claude Code / OpenCode).
- Optional external requests (such as Claude usage API sync, user-configured Discord/Slack webhooks, or GitHub release update checks) are sent directly to the respective third-party endpoint only when explicitly configured or triggered by the user.
- For additional details on local data handling, see [`README.md`](README.md).
