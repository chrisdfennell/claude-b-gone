using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
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

    private const string AllBranchesItem = "-- All Branches --";

    private string? GetSelectedBranch()
    {
        var selected = BranchCombo.SelectedItem?.ToString();
        if (selected == null || selected == AllBranchesItem)
            return null;
        return selected;
    }

    private bool IsAllBranchesSelected => BranchCombo.SelectedItem?.ToString() == AllBranchesItem;

    private async void RefreshBranches_Click(object sender, RoutedEventArgs e)
    {
        if (LocalModeRadio.IsChecked == true)
        {
            var path = RepoPathBox.Text.Trim();
            if (string.IsNullOrEmpty(path) || !await _git.IsGitRepoAsync(path))
            {
                SetStatus("Enter a valid repo path first.");
                return;
            }
            await LoadBranchesAsync(path);
        }
        else
        {
            SetStatus("Use Scan to detect branches for GitHub repos.");
        }
    }

    private async Task LoadBranchesAsync(string repoPath)
    {
        var branches = await _git.GetBranchesAsync(repoPath);
        var currentBranch = await _git.GetCurrentBranchAsync(repoPath);

        BranchCombo.Items.Clear();
        BranchCombo.Items.Add(AllBranchesItem);
        foreach (var b in branches)
            BranchCombo.Items.Add(b);

        // Select current branch by default
        var idx = BranchCombo.Items.IndexOf(currentBranch);
        BranchCombo.SelectedIndex = idx >= 0 ? idx : (branches.Count > 0 ? 1 : 0);
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

        // Save selection before loading branches (loading resets the combo)
        var wasAllBranches = IsAllBranchesSelected;
        var savedBranch = GetSelectedBranch();

        // Auto-load branches into combo box (only if empty)
        if (BranchCombo.Items.Count == 0)
            await LoadBranchesAsync(path);

        // Scan selected branch or all branches
        var branches = wasAllBranches
            ? await _git.GetBranchesAsync(path)
            : new List<string> { savedBranch ?? "HEAD" };

        var allCommits = new List<CommitInfo>();
        var allAuthorCommits = new List<AuthorCommitInfo>();
        var scannedBranches = new List<string>();

        foreach (var branch in branches)
        {
            Log($"Scanning local repo: {path} (branch: {branch ?? "current"})...");
            var commits = await _git.GetClaudeCommitsAsync(path, branch);
            var authorCommits = await _git.GetClaudeAuthoredCommitsAsync(path, branch);
            allCommits.AddRange(commits);
            allAuthorCommits.AddRange(authorCommits);
            if (branch != null) scannedBranches.Add(branch);
        }

        // Deduplicate by hash
        var seenHashes = new HashSet<string>();
        var commits2 = allCommits.Where(c => seenHashes.Add(c.Hash)).ToList();
        var authorCommits2 = allAuthorCommits.Where(c => seenHashes.Add(c.Hash)).ToList();

        _lastScannedRepoPath = path;

        foreach (var c in commits2)
        {
            _commits.Add(new CommitDisplayItem(c.ShortHash, c.Date, c.Subject, c.CoAuthorLine));
        }

        foreach (var c in authorCommits2)
        {
            _commits.Add(new CommitDisplayItem(c.ShortHash, c.Date, c.Subject, $"Author: {c.AuthorName} <{c.AuthorEmail}>"));
        }

        var totalFound = _commits.Count;
        var summary = $"Found {commits2.Count} co-author commit(s) and {authorCommits2.Count} Claude-authored commit(s).";
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
        var branch = GetSelectedBranch();

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

        var repoPath = _lastScannedRepoPath;
        string? localClonePath = null;
        // Only clone locally for network drives with many commits (50+)
        var isNetwork = GitService.IsNetworkPath(repoPath) && _commits.Count >= 50;

        var warningMsg = $"This will rewrite git history in:\n{repoPath}\n\n" +
            "A backup branch will be created first.\n\n";
        if (isNetwork)
            warningMsg += "NOTE: Repo is on a network drive with many commits. It will be cloned locally for speed, " +
                          "then pushed back.\n\n";
        warningMsg += "Are you sure you want to continue?";

        var result = MessageBox.Show(warningMsg, "Confirm History Rewrite",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        SetButtons(false);
        SetStatus("Rewriting history...");

        try
        {
            // Check for uncommitted changes
            if (await _git.HasUncommittedChangesAsync(repoPath))
            {
                SetStatus("Aborted: uncommitted changes detected. Commit or stash first.");
                Log("Cannot rewrite history with uncommitted changes.");
                return;
            }

            // Determine branches to process
            var branchesToProcess = (IsAllBranchesSelected
                ? await _git.GetBranchesAsync(repoPath)
                : new List<string> { GetSelectedBranch() ?? await _git.GetCurrentBranchAsync(repoPath) })
                .Where(b => !b.StartsWith("pre-claude-b-gone-")).ToList();

            // If on a network drive, clone locally first for speed
            var workPath = repoPath;
            if (isNetwork)
            {
                SetStatus("Cloning to local drive for speed...");
                var remoteUrl = await _git.GetRemoteUrlAsync(repoPath);
                if (remoteUrl == null)
                    remoteUrl = repoPath;
                var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "claude-b-gone");
                var logProgress = new Progress<string>(msg => Log(msg));
                localClonePath = await _git.CloneRepoAsync(remoteUrl, tempDir, logProgress);
                workPath = localClonePath;
                Log($"Working on local clone: {workPath}");
            }

            // Create backup on original repo (first branch)
            var backupBranch = await _git.CreateBackupBranchAsync(repoPath, branchesToProcess[0]);
            Log($"Created backup branch: {backupBranch}");

            ShowProgress(true);
            var progress = CreateStreamingProgress();
            var totalRewritten = 0;

            foreach (var branch in branchesToProcess)
            {
                Log($"Processing branch: {branch}");

                // Checkout the branch if working on a clone
                if (isNetwork && localClonePath != null)
                    await _git.CheckoutBranchAsync(workPath, branch);

                // Rewrite co-author lines
                SetStatus($"Rewriting co-author lines on {branch}...");
                var (rewritten, _) = await _git.RewriteHistoryAsync(workPath, branch, progress);
                Log($"Co-author rewrite on {branch}: {rewritten} commit(s) processed.");
                totalRewritten += rewritten;

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

                    SetStatus($"Rewriting commit authors on {branch}...");
                    ProgressBar.Value = 0;
                    var (authorRewritten, _) = await _git.RewriteAuthorAsync(
                        workPath, branch, name, email, progress);
                    Log($"Author rewrite on {branch}: {authorRewritten} commit(s) processed.");
                }
            }

            // If we worked on a local clone, push back to origin
            if (isNetwork && localClonePath != null)
            {
                var pushFailed = false;
                foreach (var branch in branchesToProcess)
                {
                    SetStatus($"Pushing {branch} back to origin...");
                    var (pushOk, pushOut) = await _git.ForcePushAsync(localClonePath, branch);
                    if (!string.IsNullOrWhiteSpace(pushOut))
                        Log(pushOut);
                    if (pushOk)
                        Log($"Pushed {branch} successfully.");
                    else
                    {
                        Log($"Warning: push failed for {branch}");
                        pushFailed = true;
                    }
                }

                if (!pushFailed)
                {
                    Log("All branches pushed. Running automatic cleanup...");
                    SetStatus("Cleaning up...");
                    var cleanupResult = await _git.CleanupFilterBranchLeftoversAsync(
                        repoPath, new Progress<string>(msg => Log(msg)));
                    Log(cleanupResult);
                }
            }

            var branchList = string.Join(", ", branchesToProcess);
            ShowProgress(false);
            SetStatus($"Done! {totalRewritten} commits rewritten across {branchesToProcess.Count} branch(es).");
            SummaryText.Text = isNetwork
                ? $"History cleaned and pushed across [{branchList}]."
                : $"History cleaned on [{branchList}]. Backup: {backupBranch}. Use Force Push to update remote.";

            if (!isNetwork)
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
            ShowProgress(false);
            SetButtons(true);

            // Clean up temp clone
            if (localClonePath != null)
            {
                try { GitService.ForceDeleteDirectory(localClonePath); }
                catch { Log($"Temp clone remains at: {localClonePath}"); }
            }
        }
    }

    private async void ForcePush_Click(object sender, RoutedEventArgs e)
    {
        if (_lastScannedRepoPath == null) return;

        var branchesToPush = (IsAllBranchesSelected
            ? await _git.GetBranchesAsync(_lastScannedRepoPath)
            : new List<string> { GetSelectedBranch() ?? await _git.GetCurrentBranchAsync(_lastScannedRepoPath) })
            .Where(b => !b.StartsWith("pre-claude-b-gone-")).ToList();

        var branchList = string.Join(", ", branchesToPush);
        var result = MessageBox.Show(
            $"This will FORCE PUSH branch(es): {branchList}\n\n" +
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
            var allSuccess = true;
            foreach (var branch in branchesToPush)
            {
                SetStatus($"Force pushing {branch}...");
                var (success, output) = await _git.ForcePushAsync(_lastScannedRepoPath, branch);
                Log(output);
                if (!success)
                {
                    Log($"Warning: Force push failed for {branch}");
                    allSuccess = false;
                }
            }

            if (allSuccess)
            {
                Log("All branches pushed. Running automatic cleanup...");
                SetStatus("Cleaning up filter-branch leftovers...");
                var cleanupResult = await _git.CleanupFilterBranchLeftoversAsync(
                    _lastScannedRepoPath, new Progress<string>(msg => Log(msg)));
                Log(cleanupResult);

                SetStatus("Force push successful! Cleanup complete.");
                SummaryText.Text = $"Pushed [{branchList}] to origin. Claude co-author lines removed. Leftovers cleaned.";
                ForcePushButton.IsEnabled = false;
            }
            else
            {
                SetStatus("Some branches failed to push. Check log.");
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
        var branch = GetSelectedBranch();

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
            var logProgress = new Progress<string>(msg => Log(msg));
            var streamProgress = CreateStreamingProgress();

            // Step 1: Clone
            SetStatus("Cloning repository...");
            clonePath = await _git.CloneRepoAsync(cloneUrl, tempDir, logProgress);
            Log($"Cloned to: {clonePath}");

            // Determine branch and checkout
            var targetBranch = branch ?? await _git.GetCurrentBranchAsync(clonePath);
            await _git.CheckoutBranchAsync(clonePath, targetBranch);
            Log($"Target branch: {targetBranch}");

            // Step 2: Create backup branch
            SetStatus("Creating backup branch...");
            var backupBranch = await _git.CreateBackupBranchAsync(clonePath, targetBranch);
            Log($"Created backup branch: {backupBranch}");

            // Step 3: Rewrite co-author lines
            ShowProgress(true);
            SetStatus("Rewriting co-author lines...");
            var (rewritten, output) = await _git.RewriteHistoryAsync(clonePath, targetBranch, streamProgress);
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
                ProgressBar.Value = 0;
                var (authorRewritten, authorOutput) = await _git.RewriteAuthorAsync(
                    clonePath, targetBranch, name, email, streamProgress);
                Log($"Author rewrite complete. {authorRewritten} commit(s) processed.");
            }

            ShowProgress(false);

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
                    GitService.ForceDeleteDirectory(clonePath);
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
            ShowProgress(false);
            SetButtons(true);
            CloneCleanButton.IsEnabled = false;
        }
    }

    private async void Cleanup_Click(object sender, RoutedEventArgs e)
    {
        var path = LocalModeRadio.IsChecked == true ? RepoPathBox.Text.Trim() : _lastScannedRepoPath;
        if (string.IsNullOrEmpty(path))
        {
            SetStatus("Enter a repo path first.");
            return;
        }

        if (!await _git.IsGitRepoAsync(path))
        {
            SetStatus("Not a valid git repository.");
            return;
        }

        var confirm = MessageBox.Show(
            "This will remove:\n\n" +
            "- .git-rewrite directory\n" +
            "- refs/original backup refs\n" +
            "- pre-claude-b-gone-* backup branches\n" +
            "- Run git gc to reclaim space\n\n" +
            "Continue?",
            "Confirm Cleanup",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        SetButtons(false);
        SetStatus("Cleaning up...");

        try
        {
            var result = await _git.CleanupFilterBranchLeftoversAsync(
                path, new Progress<string>(msg => Log(msg)));
            Log(result);
            SetStatus("Cleanup complete.");
            SummaryText.Text = result;
        }
        catch (Exception ex)
        {
            Log($"Cleanup error: {ex.Message}");
            SetStatus("Cleanup failed.");
        }
        finally
        {
            SetButtons(true);
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
        Dispatcher.Invoke(() =>
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            LogBox.AppendText($"[{timestamp}] {message}\n");
            LogBox.ScrollToEnd();
        });
    }

    private void ShowProgress(bool visible)
    {
        Dispatcher.Invoke(() =>
        {
            ProgressPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (!visible)
            {
                ProgressBar.Value = 0;
                ProgressText.Text = "";
            }
        });
    }

    private void UpdateProgress(string line)
    {
        // Parse filter-branch output like "Rewrite abc1234 (42/217) (3 seconds passed, remaining ...)"
        var match = Regex.Match(line, @"\((\d+)/(\d+)\)");
        if (match.Success)
        {
            var current = int.Parse(match.Groups[1].Value);
            var total = int.Parse(match.Groups[2].Value);
            var pct = total > 0 ? (double)current / total * 100 : 0;

            Dispatcher.Invoke(() =>
            {
                ProgressPanel.Visibility = Visibility.Visible;
                ProgressBar.Value = pct;
                ProgressText.Text = $"{current}/{total}";
                SetStatus($"Rewriting... {current}/{total} ({pct:F0}%)");
            });
        }
    }

    private IProgress<string> CreateStreamingProgress()
    {
        // Use a custom IProgress that dispatches directly to the UI thread
        // instead of Progress<T> which uses SynchronizationContext.Post (can drop/batch updates)
        return new DirectProgress(line =>
        {
            Dispatcher.Invoke(() =>
            {
                UpdateProgress(line);
                if (!line.TrimStart().StartsWith("Rewrite"))
                    Log(line);
            });
        });
    }

    private class DirectProgress(Action<string> callback) : IProgress<string>
    {
        public void Report(string value) => callback(value);
    }
}

public record CommitDisplayItem(string ShortHash, string Date, string Subject, string CoAuthorLine);
