using CLog.Analysis;

namespace CLog.Tests.Fakes;

/// <summary>An issue tracker that only remembers what it was asked to file.</summary>
public sealed class FakeIssueTracker : IIssueTracker
{
    public const int FirstIssueNumber = 101;

    private readonly List<(int Number, string Title, string Body)> _issues = [];
    private readonly List<(int IssueNumber, string Body)> _comments = [];

    public IReadOnlyList<(int Number, string Title, string Body)> Issues => _issues;
    public IReadOnlyList<(int IssueNumber, string Body)> Comments => _comments;

    /// <summary>True stands for GitHub being unreachable: nothing is filed and the caller is told so.</summary>
    public bool Down { get; set; }

    public Task<int?> CreateIssueAsync(string title, string body, CancellationToken cancellationToken = default)
    {
        if (Down)
        {
            return Task.FromResult<int?>(null);
        }

        var number = FirstIssueNumber + _issues.Count;
        _issues.Add((number, title, body));
        return Task.FromResult<int?>(number);
    }

    public Task<bool> AddCommentAsync(int issueNumber, string body, CancellationToken cancellationToken = default)
    {
        if (Down)
        {
            return Task.FromResult(false);
        }

        _comments.Add((issueNumber, body));
        return Task.FromResult(true);
    }
}
