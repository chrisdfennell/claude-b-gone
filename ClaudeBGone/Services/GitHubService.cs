using Octokit;

namespace ClaudeBGone.Services;

public record GitHubCommitInfo(string Sha, string ShortSha, string Date, string Subject, string CoAuthorLine);

public class GitHubService
{
    private readonly GitHubClient _client;

    public GitHubService(string? token = null)
    {
        _client = new GitHubClient(new ProductHeaderValue("claude-b-gone"));
        if (!string.IsNullOrWhiteSpace(token))
            _client.Credentials = new Credentials(token);
    }

    public async Task<List<GitHubCommitInfo>> FindClaudeCommitsAsync(
        string owner, string repo, string? branch = null, IProgress<string>? progress = null)
    {
        var results = new List<GitHubCommitInfo>();
        var request = new CommitRequest();
        if (!string.IsNullOrWhiteSpace(branch))
            request.Sha = branch;

        var options = new ApiOptions { PageSize = 100, PageCount = 1 };
        int page = 0;

        while (true)
        {
            page++;
            options.StartPage = page;
            progress?.Report($"Fetching page {page} of commits...");

            IReadOnlyList<GitHubCommit> commits;
            try
            {
                commits = await _client.Repository.Commit.GetAll(owner, repo, request, options);
            }
            catch (RateLimitExceededException)
            {
                progress?.Report("Rate limited by GitHub API. Provide a token for higher limits.");
                break;
            }

            if (commits.Count == 0) break;

            foreach (var commit in commits)
            {
                var message = commit.Commit.Message ?? "";
                if (CommitMessageCleaner.HasClaudeCoAuthor(message))
                {
                    var coAuthorLine = CommitMessageCleaner.ExtractCoAuthorLine(message);
                    results.Add(new GitHubCommitInfo(
                        commit.Sha,
                        commit.Sha[..7],
                        commit.Commit.Author?.Date.ToString("yyyy-MM-dd") ?? "unknown",
                        message.Split('\n')[0],
                        coAuthorLine));
                }
            }

            if (commits.Count < 100) break;
        }

        return results;
    }

    public async Task<(string? rateRemaining, string? rateLimit)> GetRateLimitAsync()
    {
        try
        {
            var misc = await _client.RateLimit.GetRateLimits();
            var core = misc.Resources.Core;
            return (core.Remaining.ToString(), core.Limit.ToString());
        }
        catch
        {
            return (null, null);
        }
    }
}
