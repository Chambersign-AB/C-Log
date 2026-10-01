namespace CLog.Analysis;

/// <summary>Where an analysed error is filed for people to act on.</summary>
public interface IIssueTracker
{
    /// <summary>Files an issue and returns its number, or null when the tracker could not be reached.</summary>
    Task<int?> CreateIssueAsync(string title, string body, CancellationToken cancellationToken = default);

    /// <summary>Adds a comment to an existing issue. False when the tracker could not be reached.</summary>
    Task<bool> AddCommentAsync(int issueNumber, string body, CancellationToken cancellationToken = default);
}
