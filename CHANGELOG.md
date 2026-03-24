# Changelog

All notable changes to this project will be documented in this file.

## [1.0.0] - 2026-03-24

### Added
- Initial release
- Local Repository mode: scan, clean, and force push git history
- GitHub Repository mode: scan remote repos via Octokit API
- Clone, Clean & Push flow for GitHub repos
- Branch auto-detection with dropdown and "All Branches" option
- Co-author line removal via `git filter-branch --msg-filter`
- Author/committer rewriting via `git-filter-repo` mailmap (with `git filter-branch --env-filter` fallback)
- Real-time progress bar streaming filter-branch output
- Network drive detection with automatic local cloning for large repos (50+ commits)
- Automatic cleanup of filter-branch leftovers after force push
- Manual "Cleanup Leftovers" button
- Dark-themed WPF UI (Catppuccin Mocha palette)
- Backup branch creation before every rewrite (local only, never pushed)
- Dirty working tree protection
- Confirmation dialogs before all destructive operations
- Single-file self-contained EXE publishing
- Custom app icon
