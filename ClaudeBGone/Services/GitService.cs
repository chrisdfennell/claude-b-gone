using System.Diagnostics;
using System.IO;

namespace ClaudeBGone.Services;

public record CommitInfo(string Hash, string ShortHash, string Date, string Subject, string FullMessage, string CoAuthorLine);
public record AuthorCommitInfo(string Hash, string ShortHash, string Date, string Subject, string AuthorName, string AuthorEmail);

public class GitService
{
    public static bool IsNetworkPath(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (root != null && root.StartsWith("\\\\")) return true; // UNC path
            if (root != null && root.Length >= 2 && char.IsLetter(root[0]) && root[1] == ':')
            {
                var driveInfo = new DriveInfo(root[0].ToString());
                return driveInfo.DriveType == DriveType.Network;
            }
        }
        catch { }
        return false;
    }

    public async Task<string> GetGitVersionAsync()
    {
        var (exitCode, output, _) = await RunGitAsync(".", "--version");
        return exitCode == 0 ? output.Trim() : "git not found";
    }

    public async Task<bool> IsGitRepoAsync(string path)
    {
        var (exitCode, _, _) = await RunGitAsync(path, "rev-parse --is-inside-work-tree");
        return exitCode == 0;
    }

    public async Task<string> GetCurrentBranchAsync(string repoPath)
    {
        var (exitCode, output, _) = await RunGitAsync(repoPath, "rev-parse --abbrev-ref HEAD");
        return exitCode == 0 ? output.Trim() : "unknown";
    }

    public async Task CheckoutBranchAsync(string repoPath, string branch)
    {
        // Check if already on the right branch
        var current = await GetCurrentBranchAsync(repoPath);
        if (current == branch) return;

        // Try checking out - might be a remote tracking branch
        var (exitCode, _, error) = await RunGitAsync(repoPath, $"checkout {branch}");
        if (exitCode != 0)
        {
            // Try creating from remote
            var (exitCode2, _, error2) = await RunGitAsync(repoPath, $"checkout -b {branch} origin/{branch}");
            if (exitCode2 != 0)
                throw new InvalidOperationException($"Could not checkout branch '{branch}': {error2}");
        }
    }

    public async Task<List<string>> GetBranchesAsync(string repoPath)
    {
        var (exitCode, output, _) = await RunGitAsync(repoPath, "branch --format=%(refname:short)");
        if (exitCode != 0) return [];
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    public async Task<bool> HasUncommittedChangesAsync(string repoPath)
    {
        var (exitCode, output, _) = await RunGitAsync(repoPath, "status --porcelain");
        return exitCode == 0 && !string.IsNullOrWhiteSpace(output);
    }

    public async Task<List<CommitInfo>> GetClaudeCommitsAsync(string repoPath, string? branch = null)
    {
        // Use a unique delimiter to separate commits
        const string delimiter = "---COMMIT-END-7f3a9b---";
        var branchArg = branch ?? "HEAD";
        var format = $"--format=%H%n%h%n%ai%n%s%n%B%n{delimiter}";

        var (exitCode, output, _) = await RunGitAsync(repoPath, $"log {branchArg} {format}");
        if (exitCode != 0) return [];

        var commits = new List<CommitInfo>();
        var blocks = output.Split(delimiter, StringSplitOptions.RemoveEmptyEntries);

        foreach (var block in blocks)
        {
            var lines = block.Split('\n');
            if (lines.Length < 5) continue;

            // Skip leading empty lines
            int start = 0;
            while (start < lines.Length && string.IsNullOrWhiteSpace(lines[start])) start++;
            if (start + 4 >= lines.Length) continue;

            var hash = lines[start].Trim();
            var shortHash = lines[start + 1].Trim();
            var date = lines[start + 2].Trim();
            var subject = lines[start + 3].Trim();
            var fullMessage = string.Join("\n", lines.Skip(start + 4));

            if (hash.Length < 7) continue;

            if (CommitMessageCleaner.HasClaudeCoAuthor(fullMessage))
            {
                var coAuthorLine = CommitMessageCleaner.ExtractCoAuthorLine(fullMessage);
                commits.Add(new CommitInfo(hash, shortHash, date, subject, fullMessage, coAuthorLine));
            }
        }

        return commits;
    }

    public async Task<string> CreateBackupBranchAsync(string repoPath, string branch)
    {
        var backupName = $"pre-claude-b-gone-{branch}-{DateTime.Now:yyyyMMdd-HHmmss}";
        var (exitCode, _, error) = await RunGitAsync(repoPath, $"branch {backupName} {branch}");
        if (exitCode != 0) throw new InvalidOperationException($"Failed to create backup branch: {error}");
        return backupName;
    }

    public async Task<(int rewritten, string output)> RewriteHistoryAsync(
        string repoPath, string branch, IProgress<string>? progress = null)
    {
        // Use filter-branch for message rewriting — filter-repo's callback scoping
        // is too fragile on Windows. filter-branch with sed is reliable and fast on local clones.
        return await RewriteWithFilterBranchAsync(repoPath, branch, progress);
    }

    private static string? FindFilterRepo()
    {
        // Check common locations for git-filter-repo
        var candidates = new[]
        {
            "git-filter-repo", // on PATH
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Python", "Python314", "Scripts", "git-filter-repo.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Python", "Python313", "Scripts", "git-filter-repo.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Python", "Python312", "Scripts", "git-filter-repo.exe"),
        };

        foreach (var candidate in candidates)
        {
            try
            {
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = candidate,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                process.Start();
                process.WaitForExit(5000);
                if (process.ExitCode == 0) return candidate;
            }
            catch { }
        }

        return null;
    }

    private async Task<(int rewritten, string output)> RewriteWithFilterRepoAsync(
        string repoPath, string branch, string filterRepoPath, IProgress<string>? progress)
    {
        progress?.Report("Using git-filter-repo (fast mode)...");

        // Write callback script as a standalone Python file
        var callbackScript = Path.Combine(Path.GetTempPath(), "claude_b_gone_callback.py");
        var escapedPath = callbackScript.Replace("\\", "\\\\");
        var scriptContent = @"
import re
pattern = re.compile(rb'^\s*[Cc]o-[Aa]uthored-[Bb]y:\s*Claude.*@anthropic\.com.*$\r?\n?', re.MULTILINE)
def do_clean(msg):
    cleaned = pattern.sub(b'', msg)
    return cleaned.rstrip() + b'\n' if cleaned.strip() else msg
";
        await File.WriteAllTextAsync(callbackScript, scriptContent);

        try
        {
            // Use --message-callback with inline code that loads our script
            var callback = $"exec(open(r'{escapedPath}').read()); return do_clean(message)";
            var (exitCode, output, error) = await RunProcessAsync(
                filterRepoPath,
                $"--message-callback \"{callback}\" --force",
                repoPath, timeoutSeconds: 1800, lineProgress: progress);

            if (exitCode != 0)
            {
                progress?.Report($"git-filter-repo failed (exit {exitCode}): {error.Trim()}");
                progress?.Report("Falling back to filter-branch...");
                return await RewriteWithFilterBranchAsync(repoPath, branch, progress);
            }

            var lines = $"{output}\n{error}".Split('\n');
            var rewritten = lines.Count(l => l.Contains("New ") || l.Contains("rewritten"));
            return (rewritten, $"{output}\n{error}".Trim());
        }
        finally
        {
            try { File.Delete(callbackScript); } catch { }
        }
    }

    private async Task<(int rewritten, string output)> RewriteWithFilterBranchAsync(
        string repoPath, string branch, IProgress<string>? progress)
    {
        progress?.Report("Rewriting commit messages...");

        // Set env var to squelch the filter-branch warning (it clutters progress output)
        Environment.SetEnvironmentVariable("FILTER_BRANCH_SQUELCH_WARNING", "1");

        var sedPattern = @"/^[[:space:]]*[Cc]o-[Aa]uthored-[Bb]y:.*Claude.*@anthropic\.com/d";
        var filterCmd = $"filter-branch --force --msg-filter \"sed '{sedPattern}'\" -- {branch}";

        // Use direct callback for immediate UI updates (Progress<T> batches/drops)
        var (exitCode, output, error) = await RunProcessAsync(
            "git", filterCmd, repoPath, timeoutSeconds: 1800,
            directLineCallback: line => progress?.Report(line));

        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"git filter-branch failed (exit code {exitCode}).\n\nOutput: {output}\nError: {error}\n\n" +
                "Make sure you have git installed and the repository is clean.");
        }

        var rewritten = output.Split('\n').Count(l => l.Contains("Rewrite"));
        if (rewritten == 0 && error.Contains("Rewrite"))
            rewritten = error.Split('\n').Count(l => l.Contains("Rewrite"));

        return (rewritten, $"{output}\n{error}".Trim());
    }

    public async Task<(bool success, string output)> ForcePushAsync(string repoPath, string branch)
    {
        // Use --force instead of --force-with-lease because after filter-branch/filter-repo
        // the tracking info is stale and --force-with-lease will always reject.
        // Safety is provided by the backup branch created before rewriting.
        var (exitCode, output, error) = await RunGitAsync(repoPath, $"push --force origin {branch}", timeoutSeconds: 120);
        return (exitCode == 0, exitCode == 0 ? output : error);
    }

    public async Task<List<AuthorCommitInfo>> GetClaudeAuthoredCommitsAsync(string repoPath, string? branch = null)
    {
        var branchArg = branch ?? "HEAD";
        var (exitCode, output, _) = await RunGitAsync(repoPath,
            $"log {branchArg} --format=%H%n%h%n%ai%n%s%n%an%n%ae");
        if (exitCode != 0) return [];

        var commits = new List<AuthorCommitInfo>();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i + 5 < lines.Length; i += 6)
        {
            var hash = lines[i].Trim();
            var shortHash = lines[i + 1].Trim();
            var date = lines[i + 2].Trim();
            var subject = lines[i + 3].Trim();
            var authorName = lines[i + 4].Trim();
            var authorEmail = lines[i + 5].Trim();

            if (authorEmail.Contains("anthropic.com", StringComparison.OrdinalIgnoreCase) ||
                authorName.Contains("claude", StringComparison.OrdinalIgnoreCase))
            {
                commits.Add(new AuthorCommitInfo(hash, shortHash, date, subject, authorName, authorEmail));
            }
        }

        return commits;
    }

    public async Task<(int rewritten, string output)> RewriteAuthorAsync(
        string repoPath, string branch, string newName, string newEmail, IProgress<string>? progress = null)
    {
        progress?.Report("Rewriting commit author/committer fields...");

        // Try git-filter-repo first
        var filterRepoPath = FindFilterRepo();
        if (filterRepoPath != null)
        {
            return await RewriteAuthorWithFilterRepoAsync(repoPath, branch, newName, newEmail, filterRepoPath, progress);
        }

        // Fallback to filter-branch
        var envFilter =
            $"if [ \"$GIT_AUTHOR_EMAIL\" = 'noreply@anthropic.com' ] || " +
            $"echo \"$GIT_AUTHOR_NAME\" | grep -qi 'claude'; then " +
            $"export GIT_AUTHOR_NAME='{EscapeShell(newName)}'; " +
            $"export GIT_AUTHOR_EMAIL='{EscapeShell(newEmail)}'; fi; " +
            $"if [ \"$GIT_COMMITTER_EMAIL\" = 'noreply@anthropic.com' ] || " +
            $"echo \"$GIT_COMMITTER_NAME\" | grep -qi 'claude'; then " +
            $"export GIT_COMMITTER_NAME='{EscapeShell(newName)}'; " +
            $"export GIT_COMMITTER_EMAIL='{EscapeShell(newEmail)}'; fi";

        var filterCmd = $"filter-branch --force --env-filter \"{envFilter}\" -- {branch}";
        var (exitCode, output, error) = await RunProcessAsync(
            "git", filterCmd, repoPath, timeoutSeconds: 1800, lineProgress: progress);

        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"git filter-branch --env-filter failed (exit code {exitCode}).\n\nOutput: {output}\nError: {error}");
        }

        var rewritten = output.Split('\n').Count(l => l.Contains("Rewrite"));
        if (rewritten == 0 && error.Contains("Rewrite"))
            rewritten = error.Split('\n').Count(l => l.Contains("Rewrite"));

        return (rewritten, $"{output}\n{error}".Trim());
    }

    private async Task<(int rewritten, string output)> RewriteAuthorWithFilterRepoAsync(
        string repoPath, string branch, string newName, string newEmail,
        string filterRepoPath, IProgress<string>? progress)
    {
        progress?.Report("Rewriting commit authors...");

        // Save origin URL before filter-repo removes it
        var originUrl = await GetRemoteUrlAsync(repoPath);

        // Use mailmap approach - create a mailmap file
        var mailmapPath = Path.Combine(repoPath, ".mailmap-claude-b-gone");
        var mailmapContent = $"{newName} <{newEmail}> Claude <noreply@anthropic.com>\n" +
                             $"{newName} <{newEmail}> claude <noreply@anthropic.com>\n";
        mailmapContent += $"{newName} <{newEmail}> <noreply@anthropic.com>\n";

        await File.WriteAllTextAsync(mailmapPath, mailmapContent);

        try
        {
            var (exitCode, output, error) = await RunProcessAsync(
                filterRepoPath,
                $"--mailmap \"{mailmapPath}\" --force",
                repoPath, timeoutSeconds: 600);

            if (exitCode != 0)
            {
                progress?.Report($"git-filter-repo author rewrite failed (exit {exitCode}): {error.Trim()}");
                progress?.Report("Falling back to filter-branch for author rewrite...");
                throw new InvalidOperationException($"git-filter-repo failed: {error}");
            }

            // filter-repo removes the origin remote by default — restore it
            if (originUrl != null)
            {
                progress?.Report("Restoring origin remote...");
                await RunGitAsync(repoPath, $"remote add origin {originUrl}");
            }

            return (0, $"{output}\n{error}".Trim());
        }
        finally
        {
            try { File.Delete(mailmapPath); } catch { }
        }
    }

    private static string EscapeShell(string value) => value.Replace("'", "'\\''");

    public async Task<string> CleanupFilterBranchLeftoversAsync(string repoPath, IProgress<string>? progress = null)
    {
        var cleaned = new List<string>();

        // Remove .git-rewrite directory
        var rewriteDir = Path.Combine(repoPath, ".git-rewrite");
        if (Directory.Exists(rewriteDir))
        {
            Directory.Delete(rewriteDir, true);
            cleaned.Add(".git-rewrite");
            progress?.Report("Removed .git-rewrite directory");
        }

        // Remove refs/original (backup refs created by filter-branch)
        var (exitCode, output, _) = await RunGitAsync(repoPath, "for-each-ref --format=%(refname) refs/original/");
        if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
        {
            var refs = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var refName in refs)
            {
                await RunGitAsync(repoPath, $"update-ref -d {refName}");
            }
            cleaned.Add($"refs/original ({refs.Length} ref(s))");
            progress?.Report($"Removed {refs.Length} refs/original backup ref(s)");
        }

        // Remove backup branches created by claude-b-gone
        var (branchExit, branchOutput, _) = await RunGitAsync(repoPath, "branch --list pre-claude-b-gone-*");
        if (branchExit == 0 && !string.IsNullOrWhiteSpace(branchOutput))
        {
            var branches = branchOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var b in branches)
            {
                var branchName = b.TrimStart('*', ' ');
                await RunGitAsync(repoPath, $"branch -D {branchName}");
            }
            cleaned.Add($"backup branches ({branches.Length})");
            progress?.Report($"Removed {branches.Length} backup branch(es)");
        }

        // Run gc to clean up
        progress?.Report("Running git gc...");
        await RunGitAsync(repoPath, "reflog expire --expire=now --all", timeoutSeconds: 60);
        await RunGitAsync(repoPath, "gc --prune=now --aggressive", timeoutSeconds: 120);
        cleaned.Add("git gc");

        return cleaned.Count > 0
            ? $"Cleaned: {string.Join(", ", cleaned)}"
            : "Nothing to clean up.";
    }

    public async Task<string?> GetRemoteUrlAsync(string repoPath)
    {
        var (exitCode, output, _) = await RunGitAsync(repoPath, "remote get-url origin");
        return exitCode == 0 ? output.Trim() : null;
    }

    public async Task<string> CloneRepoAsync(string url, string targetDir, IProgress<string>? progress = null)
    {
        // Extract repo name from URL for the target folder
        var repoName = url.Split('/').Last().Replace(".git", "");
        var clonePath = Path.Combine(targetDir, repoName);

        // Clean up if it already exists (git objects are often read-only)
        if (Directory.Exists(clonePath))
        {
            progress?.Report($"Removing existing directory: {clonePath}");
            ForceDeleteDirectory(clonePath);
        }

        Directory.CreateDirectory(targetDir);

        progress?.Report($"Cloning {url} to {clonePath}...");
        var (exitCode, output, error) = await RunGitAsync(targetDir, $"clone {url} {repoName}", timeoutSeconds: 300);

        if (exitCode != 0)
            throw new InvalidOperationException($"git clone failed (exit code {exitCode}).\nOutput: {output}\nError: {error}");

        progress?.Report("Clone complete.");
        return clonePath;
    }

    public static void ForceDeleteDirectory(string path)
    {
        // Clear read-only attributes on all files first (git objects are read-only)
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            var attrs = File.GetAttributes(file);
            if (attrs.HasFlag(FileAttributes.ReadOnly))
                File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
        }
        Directory.Delete(path, true);
    }

    private static Task<(int exitCode, string output, string error)> RunGitAsync(
        string workingDir, string arguments, int timeoutSeconds = 30)
    {
        return RunProcessAsync("git", arguments, workingDir, timeoutSeconds);
    }

    private static async Task<(int exitCode, string output, string error)> RunProcessAsync(
        string fileName, string arguments, string workingDir, int timeoutSeconds = 30,
        IProgress<string>? lineProgress = null, Action<string>? directLineCallback = null)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        process.Start();

        var callback = directLineCallback ?? (lineProgress != null ? lineProgress.Report : null);

        // If we have a callback, stream stderr char-by-char
        // (git filter-branch uses \r not \n for progress lines)
        if (callback != null)
        {
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorLines = new System.Text.StringBuilder();

            var stderrTask = Task.Run(async () =>
            {
                var buffer = new char[1];
                var lineBuffer = new System.Text.StringBuilder();
                var reader = process.StandardError;

                while (true)
                {
                    var charsRead = await reader.ReadAsync(buffer, 0, 1);
                    if (charsRead == 0) break;

                    var ch = buffer[0];
                    if (ch == '\r' || ch == '\n')
                    {
                        if (lineBuffer.Length > 0)
                        {
                            var line = lineBuffer.ToString();
                            errorLines.AppendLine(line);
                            callback(line);
                            lineBuffer.Clear();
                        }
                    }
                    else
                    {
                        lineBuffer.Append(ch);
                    }
                }

                if (lineBuffer.Length > 0)
                {
                    var line = lineBuffer.ToString();
                    errorLines.AppendLine(line);
                    callback(line);
                }
            });

            var completed = await Task.Run(() => process.WaitForExit(timeoutSeconds * 1000));
            if (!completed)
            {
                process.Kill();
                return (-1, "", "Process timed out");
            }

            await stderrTask; // Ensure we've read all stderr before returning
            var output = await outputTask;
            return (process.ExitCode, output, errorLines.ToString());
        }
        else
        {
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            var completed = await Task.Run(() => process.WaitForExit(timeoutSeconds * 1000));
            if (!completed)
            {
                process.Kill();
                return (-1, "", "Process timed out");
            }

            var output = await outputTask;
            var error = await errorTask;
            return (process.ExitCode, output, error);
        }
    }
}
