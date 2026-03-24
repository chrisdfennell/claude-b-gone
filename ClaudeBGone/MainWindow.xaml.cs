using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ClaudeBGone.Services;
using Microsoft.Win32;

namespace ClaudeBGone;

public partial class MainWindow : Window
{
    private readonly GitService _git = new();
    private readonly ObservableCollection<CommitDisplayItem> _commits = [];
    private string? _lastScannedRepoPath;

    public MainWindow()
    {
        InitializeComponent();
        CommitList.ItemsSource = _commits;
        LoadIcon();
    }

    private void LoadIcon()
    {
        try
        {
            var stream = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/app.ico"))?.Stream;
            if (stream != null)
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                Icon = decoder.Frames[0];
            }
        }
        catch
        {
            // Icon not found - not critical, just skip
        }
    }

    private void Mode_Changed(object sender, RoutedEventArgs e)
    {
        if (LocalPanel == null || GitHubPanel == null) return;

        var isLocal = LocalModeRadio.IsChecked == true;
        LocalPanel.Visibility = isLocal ? Visibility.Visible : Visibility.Collapsed;
        GitHubPanel.Visibility = isLocal ? Visibility.Collapsed : Visibility.Visible;
        CleanButton.IsEnabled = false;
        ForcePushButton.IsEnabled = false;
        CloneCleanButton.IsEnabled = false;
        CloneCleanButton.Visibility = isLocal ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RewriteAuthor_Changed(object sender, RoutedEventArgs e)
    {
        if (AuthorPanel == null) return;
        AuthorPanel.Visibility = RewriteAuthorCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Git Repository Folder"
        };

        if (dialog.ShowDialog() == true)
        {
            RepoPathBox.Text = dialog.FolderName;
        }
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        _commits.Clear();
        CleanButton.IsEnabled = false;
        ForcePushButton.IsEnabled = false;
        SetStatus("Scanning...");
        SetButtons(false);

        try
        {
            if (LocalModeRadio.IsChecked == true)
                await ScanLocalAsync();
            else
                await ScanGitHubAsync();
        }
        catch (Exception ex)
        {
            Log($"Error: {ex.Message}");
            SetStatus("Scan failed.");
        }
        finally
        {
            SetButtons(true);
        }
    }

    private async Task ScanLocalAsync()
    {
        var path = RepoPathBox.Text.Trim();
        if (string.IsNullOrEmpty(path))
        {
            SetStatus("Please enter a repository path.");
            return;
        }

        if (!await _git.IsGitRepoAsync(path))
        {
            SetStatus("Not a valid git repository.");
            return;
        }

        var branch = string.IsNullOrWhiteSpace(BranchBox.Text) ? null : BranchBox.Text.Trim();
        Log($"Scanning local repo: {path} (branch: {branch ?? "current"})...");

        var commits = await _git.GetClaudeCommitsAsync(path, branch);
        var authorCommits = await _git.GetClaudeAuthoredCommitsAsync(path, branch);
        _lastScannedRepoPath = path;

        foreach (var c in commits)
        {
            _commits.Add(new CommitDisplayItem(c.ShortHash, c.Date, c.Subject, c.CoAuthorLine));
        }

        // Add author commits that aren't already in the list
        var existingHashes = commits.Select(c => c.ShortHash).ToHashSet();
        foreach (var c in authorCommits.Where(a => !existingHashes.Contains(a.ShortHash)))
        {
            _commits.Add(new CommitDisplayItem(c.ShortHash, c.Date, c.Subject, $"Author: {c.AuthorName} <{c.AuthorEmail}>"));
        }

        var totalFound = _commits.Count;
        var summary = $"Found {commits.Count} co-author commit(s) and {authorCommits.Count} Claude-authored commit(s).";
        SummaryText.Text = summary;
        Log(summary);
        SetStatus(totalFound > 0 ? $"{totalFound} commits found" : "No Claude commits found.");

        if (totalFound > 0)
        {
            CleanButton.IsEnabled = true;
        }
    }

    private async Task ScanGitHubAsync()
    {
        var repoInput = GitHubRepoBox.Text.Trim();
        if (string.IsNullOrEmpty(repoInput) || !repoInput.Contains('/'))
        {
            SetStatus("Please enter owner/repo (e.g. octocat/hello-world).");
            return;
        }

        var parts = repoInput.Split('/', 2);
        var token = GitHubTokenBox.Password;
        var github = new GitHubService(string.IsNullOrWhiteSpace(token) ? null : token);
        var branch = string.IsNullOrWhiteSpace(BranchBox.Text) ? null : BranchBox.Text.Trim();

        Log($"Scanning GitHub repo: {repoInput} (branch: {branch ?? "default"})...");
        var progress = new Progress<string>(msg => Log(msg));

        var commits = await github.FindClaudeCommitsAsync(parts[0], parts[1], branch, progress);

        foreach (var c in commits)
        {
            _commits.Add(new CommitDisplayItem(c.ShortSha, c.Date, c.Subject, c.CoAuthorLine));
        }

        var summary = $"Found {commits.Count} commit(s) with Claude co-author attribution on GitHub.";
        SummaryText.Text = summary;
        Log(summary);

        if (commits.Count > 0)
            CloneCleanButton.IsEnabled = true;

        SetStatus(commits.Count > 0 ? $"{commits.Count} commits found" : "No Claude commits found.");
    }

    private async void Clean_Click(object sender, RoutedEventArgs e)
    {
        if (_lastScannedRepoPath == null) return;

        var result = MessageBox.Show(
            $"This will rewrite git history in:\n{_lastScannedRepoPath}\n\n" +
            "A backup branch will be created first.\n\n" +
            "Are you sure you want to continue?",
            "Confirm History Rewrite",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        SetButtons(false);
        SetStatus("Rewriting history...");

        try
        {
            // Check for uncommitted changes
            if (await _git.HasUncommittedChangesAsync(_lastScannedRepoPath))
            {
                SetStatus("Aborted: uncommitted changes detected. Commit or stash first.");
                Log("Cannot rewrite history with uncommitted changes.");
                return;
            }

            var branch = string.IsNullOrWhiteSpace(BranchBox.Text)
                ? await _git.GetCurrentBranchAsync(_lastScannedRepoPath)
                : BranchBox.Text.Trim();

            // Create backup
            var backupBranch = await _git.CreateBackupBranchAsync(_lastScannedRepoPath, branch);
            Log($"Created backup branch: {backupBranch}");

            // Rewrite co-author lines
            var progress = new Progress<string>(msg => Log(msg));
            var (rewritten, output) = await _git.RewriteHistoryAsync(_lastScannedRepoPath, branch, progress);

            Log(output);
            Log($"Co-author rewrite complete. {rewritten} commit(s) processed.");

            // Rewrite author/committer if checked
            if (RewriteAuthorCheck.IsChecked == true)
            {
                var name = AuthorNameBox.Text.Trim();
                var email = AuthorEmailBox.Text.Trim();
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(email))
                {
                    SetStatus("Please enter your name and email for author rewrite.");
                    return;
                }

                SetStatus("Rewriting commit authors...");
                var (authorRewritten, authorOutput) = await _git.RewriteAuthorAsync(
                    _lastScannedRepoPath, branch, name, email, progress);
                Log(authorOutput);
                Log($"Author rewrite complete. {authorRewritten} commit(s) processed.");
            }

            SetStatus($"Done! Backup: {backupBranch}");
            SummaryText.Text = $"History cleaned. Backup branch: {backupBranch}. Use Force Push to update remote.";

            ForcePushButton.IsEnabled = true;
            CleanButton.IsEnabled = false;
        }
        catch (Exception ex)
        {
            Log($"Error during rewrite: {ex.Message}");
            SetStatus("Rewrite failed. Check log for details.");
        }
        finally
        {
            SetButtons(true);
        }
    }

    private async void ForcePush_Click(object sender, RoutedEventArgs e)
    {
        if (_lastScannedRepoPath == null) return;

        var branch = string.IsNullOrWhiteSpace(BranchBox.Text)
            ? await _git.GetCurrentBranchAsync(_lastScannedRepoPath)
            : BranchBox.Text.Trim();

        var result = MessageBox.Show(
            $"This will FORCE PUSH branch '{branch}' to origin.\n\n" +
            "This rewrites remote history and cannot be undone.\n" +
            "Make sure all collaborators are aware.\n\n" +
            "Continue?",
            "Confirm Force Push",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        SetButtons(false);
        SetStatus("Force pushing...");

        try
        {
            var (success, output) = await _git.ForcePushAsync(_lastScannedRepoPath, branch);
            Log(output);

            if (success)
            {
                SetStatus("Force push successful!");
                SummaryText.Text = $"Branch '{branch}' has been force-pushed to origin. Claude co-author lines removed.";
                ForcePushButton.IsEnabled = false;
            }
            else
            {
                SetStatus("Force push failed. Check log.");
            }
        }
        catch (Exception ex)
        {
            Log($"Error: {ex.Message}");
            SetStatus("Force push failed.");
        }
        finally
        {
            SetButtons(true);
        }
    }

    private async void CloneAndClean_Click(object sender, RoutedEventArgs e)
    {
        var repoInput = GitHubRepoBox.Text.Trim();
        if (string.IsNullOrEmpty(repoInput) || !repoInput.Contains('/'))
        {
            SetStatus("Please enter owner/repo.");
            return;
        }

        var parts = repoInput.Split('/', 2);
        var owner = parts[0];
        var repo = parts[1];
        var token = GitHubTokenBox.Password;
        var branch = string.IsNullOrWhiteSpace(BranchBox.Text) ? null : BranchBox.Text.Trim();

        // Confirmation dialog
        var confirm = MessageBox.Show(
            $"This will:\n\n" +
            $"1. Clone {owner}/{repo} to a temp folder\n" +
            $"2. Create a backup branch\n" +
            $"3. Rewrite history to remove Claude co-author lines\n" +
            $"4. Force push the cleaned branch to GitHub\n\n" +
            "This rewrites remote history and cannot be undone.\n" +
            "Make sure all collaborators are aware.\n\n" +
            "Continue?",
            "Confirm Clone, Clean & Push",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        SetButtons(false);
        string? clonePath = null;

        try
        {
            // Build clone URL
            var cloneUrl = string.IsNullOrWhiteSpace(token)
                ? $"https://github.com/{owner}/{repo}.git"
                : $"https://{token}@github.com/{owner}/{repo}.git";

            var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "claude-b-gone");
            var progress = new Progress<string>(msg => Log(msg));

            // Step 1: Clone
            SetStatus("Cloning repository...");
            clonePath = await _git.CloneRepoAsync(cloneUrl, tempDir, progress);
            Log($"Cloned to: {clonePath}");

            // Determine branch
            var targetBranch = branch ?? await _git.GetCurrentBranchAsync(clonePath);
            Log($"Target branch: {targetBranch}");

            // Step 2: Create backup branch
            SetStatus("Creating backup branch...");
            var backupBranch = await _git.CreateBackupBranchAsync(clonePath, targetBranch);
            Log($"Created backup branch: {backupBranch}");

            // Step 3: Rewrite co-author lines
            SetStatus("Rewriting co-author lines...");
            var (rewritten, output) = await _git.RewriteHistoryAsync(clonePath, targetBranch, progress);
            Log(output);
            Log($"Co-author rewrite complete. {rewritten} commit(s) processed.");

            // Step 3b: Rewrite author/committer if checked
            if (RewriteAuthorCheck.IsChecked == true)
            {
                var name = AuthorNameBox.Text.Trim();
                var email = AuthorEmailBox.Text.Trim();
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(email))
                {
                    SetStatus("Please enter your name and email for author rewrite.");
                    return;
                }

                SetStatus("Rewriting commit authors...");
                var (authorRewritten, authorOutput) = await _git.RewriteAuthorAsync(
                    clonePath, targetBranch, name, email, progress);
                Log(authorOutput);
                Log($"Author rewrite complete. {authorRewritten} commit(s) processed.");
            }

            // Step 4: Confirm force push
            var pushConfirm = MessageBox.Show(
                $"History has been rewritten locally ({rewritten} commits processed).\n\n" +
                $"Force push branch '{targetBranch}' to GitHub?\n\n" +
                "This will overwrite the remote branch.",
                "Confirm Force Push",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (pushConfirm != MessageBoxResult.Yes)
            {
                SetStatus("Rewrite complete. Force push skipped.");
                Log("User skipped force push. Temp clone preserved at: " + clonePath);
                return;
            }

            // Step 5: Force push
            SetStatus("Force pushing...");
            var (success, pushOutput) = await _git.ForcePushAsync(clonePath, targetBranch);
            Log(pushOutput);

            if (success)
            {
                SetStatus("Clone, Clean & Push complete!");
                SummaryText.Text = $"Successfully cleaned and force-pushed '{targetBranch}' for {owner}/{repo}.";
                Log("Force push successful.");
            }
            else
            {
                SetStatus("Force push failed. Check log.");
                return;
            }

            // Step 6: Offer to delete temp clone
            var deleteConfirm = MessageBox.Show(
                $"Operation complete. Delete the temporary clone?\n\n{clonePath}",
                "Delete Temp Clone",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (deleteConfirm == MessageBoxResult.Yes)
            {
                try
                {
                    System.IO.Directory.Delete(clonePath, true);
                    Log("Temporary clone deleted.");
                }
                catch (Exception delEx)
                {
                    Log($"Could not delete temp clone: {delEx.Message}");
                }
            }
            else
            {
                Log($"Temp clone preserved at: {clonePath}");
            }
        }
        catch (Exception ex)
        {
            Log($"Error during Clone & Clean: {ex.Message}");
            SetStatus("Clone & Clean failed. Check log for details.");

            if (clonePath != null)
                Log($"Temp clone may remain at: {clonePath}");
        }
        finally
        {
            SetButtons(true);
            CloneCleanButton.IsEnabled = false;
        }
    }

    private void SetStatus(string text)
    {
        StatusText.Text = text;
    }

    private void SetButtons(bool enabled)
    {
        ScanButton.IsEnabled = enabled;
        BrowseButton.IsEnabled = enabled;
        if (!enabled)
        {
            CleanButton.IsEnabled = false;
            ForcePushButton.IsEnabled = false;
            CloneCleanButton.IsEnabled = false;
        }
    }

    private void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        LogBox.AppendText($"[{timestamp}] {message}\n");
        LogBox.ScrollToEnd();
    }
}

public record CommitDisplayItem(string ShortHash, string Date, string Subject, string CoAuthorLine);
