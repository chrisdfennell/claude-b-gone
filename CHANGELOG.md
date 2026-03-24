# Changelog

All notable changes to this project will be documented in this file.

## [1.1.0] - 2026-03-24

### Added
- Branch auto-detection dropdown with "All Branches" option to scan/clean/push every branch at once
- Real-time progress bar with live commit counter during history rewriting
- Network drive detection -- repos on NAS/mapped drives with 50+ commits are auto-cloned locally for speed
- Automatic cleanup after force push (removes .git-rewrite, refs/original, backup branches, runs git gc)
- Manual "Cleanup Leftovers" button for on-demand cleanup
- git-filter-repo support for faster author/committer rewriting via mailmap (with filter-branch fallback)
- Custom app icon (Claude sunburst with red X)
- Single-file self-contained EXE publishing (~135MB, no .NET install needed)
- Version displayed in window title bar

### Fixed
- filter-branch progress now streams correctly by reading \r-delimited output character-by-character
- Progress updates use direct Dispatcher.Invoke instead of Progress<T> to prevent dropped/batched updates
- Read-only git objects no longer cause "Access denied" errors when cleaning temp directories
- Backup branches are never pushed to remote during "All Branches" operations
- Force push uses --force instead of --force-with-lease (which always fails after history rewrite)
- Empty push output no longer logs blank lines
- Branch selection no longer resets when scanning
- Cloned repos properly checkout the target branch before rewriting
- filter-repo origin remote is restored after author rewriting

### Changed
- Timeouts increased to 30 minutes for rewrite operations
- FILTER_BRANCH_SQUELCH_WARNING set to suppress deprecation warning

---

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
