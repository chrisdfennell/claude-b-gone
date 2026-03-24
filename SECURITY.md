# Security Policy

## Supported Versions

| Version | Supported          |
|---------|--------------------|
| 1.0.x   | Yes                |

## Reporting a Vulnerability

If you discover a security vulnerability in Claude-B-Gone, please report it responsibly:

1. **Do not** open a public GitHub issue for security vulnerabilities.
2. Email the maintainer directly or use GitHub's private vulnerability reporting feature.
3. Include a description of the vulnerability and steps to reproduce it.

## Security Considerations

This tool handles:

- **GitHub personal access tokens** -- Tokens are held in memory only for the current session. They are never written to disk or logged. When using the Clone, Clean & Push flow, tokens may be included in the clone URL passed to `git clone`.
- **Git repository access** -- The tool executes `git` commands with the same permissions as the current user. It does not escalate privileges.
- **Force pushing** -- This tool force-pushes to remote repositories. Ensure you have appropriate authorization before use.

## Dependencies

- [Octokit](https://github.com/octokit/octokit.net) -- GitHub API client (MIT license)
- .NET 10 runtime (self-contained in published builds)
