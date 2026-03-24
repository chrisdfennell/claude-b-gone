# Contributing to Claude-B-Gone

Thanks for your interest in contributing! Here's how to get started.

## Getting Started

1. Fork the repository
2. Clone your fork: `git clone https://github.com/your-username/claude-b-gone.git`
3. Create a feature branch: `git checkout -b my-feature`
4. Make your changes
5. Build and test: `dotnet build`
6. Commit your changes with a clear message
7. Push to your fork: `git push origin my-feature`
8. Open a pull request

## Development Setup

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Git](https://git-scm.com/)
- Windows (WPF is Windows-only)

### Building

```bash
cd claude-b-gone
dotnet build
```

### Running

```bash
dotnet run --project ClaudeBGone
```

### Publishing

```bash
dotnet publish ClaudeBGone -c Release
```

## Areas for Contribution

- **Cross-platform support** -- Port to Avalonia or MAUI
- **Other AI patterns** -- Support for GitHub Copilot, Cursor, and other AI co-author patterns
- **Unit tests** -- Tests for `CommitMessageCleaner` and `GitService`
- **CI/CD pipeline** -- GitHub Actions for build, test, and release
- **CLI mode** -- Command-line interface for scripting and automation
- **Performance** -- Investigate `git filter-repo` Python callback scoping issues on Windows

## Code Style

- Follow standard C# conventions
- Use file-scoped namespaces
- Keep the UI code-behind thin where possible
- Prefer async/await for all git operations

## Reporting Issues

- Use GitHub Issues to report bugs or request features
- Include your OS version, .NET version, and git version
- Include the full log output from the app if reporting a bug
