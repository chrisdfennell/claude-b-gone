# Claude-B-Gone

**Scrub AI co-author fingerprints from your git history.**

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![WPF](https://img.shields.io/badge/UI-WPF-blue)](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows-0078D6?logo=windows)](https://www.microsoft.com/windows)

---

## What It Does

Claude-B-Gone is a desktop tool that finds and removes `Co-Authored-By: Claude` lines from git commit messages. It rewrites your repository history so that AI co-author attributions inserted by tools like Claude Code are cleanly stripped out.

It works on local repositories and can also scan remote GitHub repositories via the GitHub API.

## Why

When using AI coding assistants like Claude Code, every commit automatically includes a co-author trailer such as:

```
Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>
```

Some developers prefer a clean commit history without these attribution lines visible to the public. Claude-B-Gone makes it easy to remove them in bulk rather than manually editing each commit.

## Screenshots

> _Screenshots coming soon._

<!-- TODO: Add screenshots of the main window, scan results, and clean confirmation dialog -->

## Features

- **Two operating modes** -- Local Repository and GitHub Repository scanning
- **Dark-themed WPF interface** using the [Catppuccin Mocha](https://github.com/catppuccin/catppuccin) color palette
- **Scan commits** for Claude co-author attributions across your entire branch history
- **Preview affected commits** in a sortable list before making any changes (hash, date, subject, co-author line)
- **Rewrite git history** to remove co-author lines using `git filter-branch`
- **Automatic backup branch creation** before any destructive operation (named `pre-claude-b-gone-<branch>-<timestamp>`)
- **Safer force push** using `--force-with-lease` instead of `--force`
- **GitHub API integration** via [Octokit](https://github.com/octokit/octokit.net) for remote repository scanning
- **Clone, Clean & Push flow** -- clone a remote repo to a temp directory, clean it, push, and clean up
- **Real-time log output** with timestamps in the bottom panel
- **Author/committer rewrite** -- optionally rewrite commits authored by Claude to your own identity, removing Claude from GitHub's Contributors list
- **Dirty working tree protection** -- refuses to rewrite history if uncommitted changes are detected
- **Confirmation dialogs** before every destructive operation

## Prerequisites

| Requirement | Details |
|---|---|
| **.NET 10 SDK** | Target framework is `net10.0-windows`. Download from [dotnet.microsoft.com](https://dotnet.microsoft.com/download). |
| **Git** | Must be on your PATH. The tool shells out to `git` for all repository operations. |
| **Windows** | WPF is Windows-only. No cross-platform support. |
| **GitHub Token** _(optional)_ | A [personal access token](https://github.com/settings/tokens) with `repo` scope. Only needed for GitHub mode. Without one, you are limited by the unauthenticated API rate limit (60 requests/hour). |

## Installation

```bash
git clone https://github.com/your-username/claude-b-gone.git
cd claude-b-gone
dotnet build
```

To run:

```bash
dotnet run --project ClaudeBGone
```

Or publish a standalone executable:

```bash
dotnet publish ClaudeBGone -c Release -r win-x64 --self-contained
```

## Usage

### Local Repository Mode

1. Launch the application.
2. Select **Local Repository** (the default mode).
3. Enter the path to your git repository, or click **Browse...** to pick the folder.
4. Optionally specify a branch name. Leave blank to use the current branch.
5. Click **Scan for Claude Commits**.
6. Review the list of commits that contain Claude co-author attributions.
7. **Optional**: Check **"Also rewrite commit author"** to replace Claude's authorship with your own name/email. Enter your name and email in the fields that appear. This removes Claude from GitHub's Contributors list.
8. Click **Clean History** to rewrite the commit messages. A backup branch is created automatically.
9. After cleaning, click **Force Push** to push the rewritten history to your remote.

### GitHub Repository Mode

1. Select **GitHub Repository**.
2. Enter the repository in `owner/repo` format (e.g. `octocat/hello-world`).
3. Optionally enter a GitHub personal access token to increase API rate limits.
4. Optionally specify a branch name. Leave blank to use the default branch.
5. Click **Scan for Claude Commits**.
6. Review the results. GitHub mode is **read-only** -- it identifies affected commits but cannot rewrite remote history directly.

To clean a GitHub repo, clone it locally and use Local Repository mode, or use the Clone, Clean & Push flow.

### Clone, Clean & Push

For GitHub repositories, the recommended workflow is:

1. Scan the repo in GitHub mode to confirm Claude commits exist.
2. Clone the repo locally using `git clone`.
3. Switch to Local Repository mode, point to the cloned directory.
4. Scan, Clean, and Force Push from there.
5. Delete the local clone when finished.

## How It Works

### Detection

The tool uses a compiled .NET regex to match co-author trailer lines:

```
^\s*[Cc]o-[Aa]uthored-[Bb]y:\s*Claude.*<.*@anthropic\.com>.*$
```

This pattern is case-flexible on the `Co-Authored-By` prefix and matches any line where:
- The author name starts with `Claude` (covers Claude, Claude Opus, Claude Sonnet, etc.)
- The email domain is `anthropic.com`

### Local Scanning

Commits are retrieved via `git log` with a custom format delimiter. Each commit's full message body is checked against the regex. Matching commits are displayed in the UI.

### History Rewriting

The tool invokes `git filter-branch --force --msg-filter` with a `sed` command to strip matching lines:

```bash
git filter-branch --force --msg-filter \
  "sed '/^[[:space:]]*[Cc]o-[Aa]uthored-[Bb]y:.*Claude.*@anthropic\.com/d'" \
  -- <branch>
```

This processes every commit on the branch and removes matching lines from commit messages.

### Author/Committer Rewriting

When the **"Also rewrite commit author"** option is checked, the tool runs a second `git filter-branch` pass with an `--env-filter` that replaces the `GIT_AUTHOR_NAME`, `GIT_AUTHOR_EMAIL`, `GIT_COMMITTER_NAME`, and `GIT_COMMITTER_EMAIL` fields on any commit where:

- The author/committer email is `noreply@anthropic.com`
- The author/committer name contains "claude" (case-insensitive)

These are replaced with the name and email you provide. This removes Claude from GitHub's **Contributors** widget, which is derived from commit authorship — not from co-author trailer lines.

### Force Push

After rewriting, the cleaned history is pushed with:

```bash
git push --force-with-lease origin <branch>
```

`--force-with-lease` is safer than `--force` because it refuses to push if the remote branch has been updated since your last fetch, preventing accidental overwrites of other people's work.

## Co-Author Patterns Matched

The following co-author lines are detected and removed:

```
Co-Authored-By: Claude <noreply@anthropic.com>
Co-authored-by: Claude Opus 4.6 (1M context) <noreply@anthropic.com>
Co-Authored-By: Claude Sonnet 4 <noreply@anthropic.com>
co-authored-by: Claude <claude@anthropic.com>
  Co-Authored-By: Claude Haiku <support@anthropic.com>
```

Any line matching the general pattern `Co-Authored-By: Claude...<...@anthropic.com>` will be removed regardless of:
- Capitalization of `Co-Authored-By`
- Specific Claude model name or version
- Specific `@anthropic.com` email address
- Leading whitespace

## Safety Features

| Feature | Description |
|---|---|
| **Backup branches** | Before any rewrite, a branch named `pre-claude-b-gone-<branch>-<YYYYMMDD-HHmmss>` is created. You can always restore your original history from this backup. |
| **Dirty tree check** | The tool refuses to rewrite history if there are uncommitted changes in the working directory. You must commit or stash first. |
| **Force-with-lease** | Uses `--force-with-lease` instead of `--force` to prevent overwriting remote changes made by others since your last fetch. |
| **Confirmation dialogs** | Both "Clean History" and "Force Push" require explicit user confirmation through a dialog box. |
| **Read-only GitHub mode** | Scanning via GitHub API never modifies anything. It only reads commit data. |
| **Process timeouts** | Git commands have configurable timeouts (default 30s, up to 300s for long operations) to prevent the app from hanging. |

## Configuration

### GitHub Token

To scan GitHub repositories with higher rate limits:

1. Go to [GitHub Settings > Developer settings > Personal access tokens](https://github.com/settings/tokens).
2. Create a token with `repo` scope (or `public_repo` for public repositories only).
3. Paste the token into the **Token** field in GitHub Repository mode.

The token is not stored anywhere -- it is only held in memory for the current session.

### Branch Selection

- **Local mode**: Leave the branch field blank to use the currently checked-out branch. Enter a branch name to target a specific branch.
- **GitHub mode**: Leave blank to use the repository's default branch. Enter a branch name to scan a specific branch.

## Troubleshooting

### "Not a valid git repository"
Make sure the path you entered points to the root of a git repository (the directory containing the `.git` folder).

### "git filter-branch failed"
- Ensure `git` is installed and on your PATH.
- Make sure the working tree is clean (no uncommitted changes).
- On Windows, `git filter-branch` requires the `sed` command, which is bundled with Git for Windows. If you installed git without the Unix tools, this may fail.

### "Rate limited by GitHub API"
Provide a personal access token to increase the rate limit from 60 to 5,000 requests per hour.

### Force push rejected
- `--force-with-lease` will reject the push if the remote branch has been updated since your last fetch. Run `git fetch` and try again.
- Make sure you have push access to the remote repository.

### Backup branch already exists
Backup branches include a timestamp down to the second, so collisions are extremely unlikely. If it does happen, wait a second and try again.

### The app hangs during rewrite
Large repositories with thousands of commits may take a long time to process with `git filter-branch`. The timeout is set to 300 seconds (5 minutes). For very large repos, consider using `git filter-repo` manually as a faster alternative.

## Contributing

Contributions are welcome. To get started:

1. Fork the repository.
2. Create a feature branch: `git checkout -b my-feature`.
3. Make your changes and test them.
4. Submit a pull request with a clear description of what you changed and why.

Areas where contributions would be especially useful:
- Adding `git filter-repo` as a faster alternative backend
- Cross-platform support (Avalonia or MAUI)
- Support for other AI co-author patterns (GitHub Copilot, Cursor, etc.)
- Unit tests for `CommitMessageCleaner`
- CI/CD pipeline

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE) for details.

## Disclaimer

**This tool rewrites git history.** Rewriting history is a destructive operation that changes commit hashes. Be aware of the following:

- **Force pushing** overwrites the remote branch. All collaborators will need to re-sync their local copies (e.g., `git fetch && git reset --hard origin/<branch>`).
- **Backup branches are local.** If you delete the local repository, the backups are gone. Push backup branches to the remote if you want to preserve them.
- **CI/CD pipelines** that reference specific commit SHAs may break after a history rewrite.
- **Signed commits** will lose their signatures after rewriting.
- **Use at your own risk.** Always verify the results before force pushing, and make sure all collaborators are informed.

This tool is not affiliated with Anthropic.
