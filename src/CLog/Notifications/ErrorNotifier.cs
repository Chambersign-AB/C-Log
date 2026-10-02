using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CLog.Configuration;
using CLog.Model;
using CLog.Storage;
using CLog.Triage;

namespace CLog.Notifications;

/// <summary>
/// Decides which errors a cycle should tell people about and sends one notification for all
/// of them: errors judged ANALYZE or KNOWN, when they get that verdict and when they come
/// back, at most once per fingerprint per repeat interval.
/// </summary>
public sealed partial class ErrorNotifier(
    IEnumerable<INotifier> channels,
    IFingerprintStore store,
    IOptions<CLogOptions> options,
    ILogger<ErrorNotifier> logger)
{
    private const int MaxSolutionLength = 300;
    private const int MaxMessageLength = 100;

    private readonly INotifier[] _channels = [.. channels];
    private readonly GitHubOptions _gitHub = options.Value.Analysis.GitHub;
    private readonly TimeSpan _repeat = TimeSpan.FromMinutes(Math.Max(1, options.Value.Notify.RepeatMinutes));

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>
    /// The notice for an error that has just been given a verdict, or null when the verdict
    /// calls for none or one went out too recently.
    /// </summary>
    public async Task<Notice?> ForOutcomeAsync(
        ErrorGroup group,
        TriageVerdict verdict,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (verdict.Verdict is not (Verdict.Analyze or Verdict.Known))
        {
            return null;
        }

        // Saved before anything is sent: if the send fails, the error is still known to be
        // worth a notice and gets one the next time it is fetched.
        var earlier = await store.GetNoticeAsync(group.Fingerprint.Hash, cancellationToken);
        var state = new NoticeState(verdict.Verdict, verdict.KnownSolution, earlier?.LastNotifiedAt);
        await store.SaveNoticeAsync(group.Fingerprint.Hash, state, cancellationToken);

        return await BuildIfDueAsync(group, state, now, cancellationToken);
    }

    /// <summary>The notice for an already judged error that has happened again, or null when none is due.</summary>
    public async Task<Notice?> ForRecurrenceAsync(
        ErrorGroup group,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var state = await store.GetNoticeAsync(group.Fingerprint.Hash, cancellationToken);
        return state is null ? null : await BuildIfDueAsync(group, state, now, cancellationToken);
    }

    /// <summary>
    /// Sends the cycle's notices as one notification. Never throws for a channel that fails:
    /// a notice is a courtesy on top of the triage log and the issue, and must not stop either.
    /// </summary>
    public async Task SendAsync(IReadOnlyList<Notice> notices, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (notices.Count == 0)
        {
            return;
        }

        var notification = new Notification(Subject(notices), [.. notices.Select(n => n.Line)]);
        var delivered = false;

        foreach (var channel in _channels)
        {
            try
            {
                if (await channel.SendAsync(notification, cancellationToken))
                {
                    delivered = true;
                }
                else
                {
                    logger.LogWarning(
                        "The notice about {Count} error(s) could not be sent through {Channel}; the cycle goes on",
                        notices.Count, channel.Name);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "The notice about {Count} error(s) failed in {Channel}; the cycle goes on",
                    notices.Count, channel.Name);
            }
        }

        if (!delivered)
        {
            return;
        }

        foreach (var notice in notices)
        {
            await store.SaveNoticeAsync(
                notice.Fingerprint, notice.State with { LastNotifiedAt = now }, CancellationToken.None);
        }
    }

    private async Task<Notice?> BuildIfDueAsync(
        ErrorGroup group,
        NoticeState state,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (state.LastNotifiedAt is { } last && now - last < _repeat)
        {
            return null;
        }

        var issue = await store.GetIssueAsync(group.Fingerprint.Hash, cancellationToken);
        var link = issue is null || string.IsNullOrWhiteSpace(_gitHub.Repository)
            ? null
            : _gitHub.IssueUrl(issue.Number);

        return new Notice(group.Fingerprint.Hash, state, Line(group, state, link));
    }

    /// <summary>"[ANALYZE] InvalidOperationException i SigningSessionsController.GetReceipt — ×2 — link".</summary>
    internal static string Line(ErrorGroup group, NoticeState state, string? issueUrl)
    {
        var parts = new List<string>
        {
            $"[{state.Verdict.ToString().ToUpperInvariant()}] {What(group)}",
            $"×{group.Count}"
        };

        if (state.Verdict == Verdict.Known && !string.IsNullOrWhiteSpace(state.Solution))
        {
            parts.Add("Lösning: " + Shorten(state.Solution, MaxSolutionLength));
        }

        if (issueUrl is not null)
        {
            parts.Add(issueUrl);
        }

        return string.Join(" — ", parts);
    }

    private static string What(ErrorGroup group)
    {
        var fingerprint = group.Fingerprint;
        var type = fingerprint.ExceptionType[(fingerprint.ExceptionType.LastIndexOf('.') + 1)..];
        var place = ShortFrame(fingerprint.TopFrame);

        if (type.Length > 0 && place.Length > 0)
        {
            return $"{type} i {place}";
        }

        // No stack trace to name a place: the message is what identifies the error.
        var message = Shorten(
            string.IsNullOrWhiteSpace(fingerprint.NormalizedTemplate)
                ? group.Sample.RenderedMessage ?? ""
                : fingerprint.NormalizedTemplate,
            MaxMessageLength);
        return type.Length > 0 ? $"{type}: {message}" : message;
    }

    /// <summary>"CSign.Web.Controllers.SigningSessionsController.GetReceipt(Guid id)" becomes "SigningSessionsController.GetReceipt".</summary>
    private static string ShortFrame(string frame)
    {
        var parameters = frame.IndexOf('(');
        var segments = (parameters < 0 ? frame : frame[..parameters])
            .Split('.', StringSplitOptions.RemoveEmptyEntries);
        return string.Join('.', segments.TakeLast(2));
    }

    /// <summary>One line, however the text was laid out: a notice is a line per error.</summary>
    private static string Shorten(string text, int max)
    {
        var line = Whitespace().Replace(text, " ").Trim();
        return line.Length <= max ? line : line[..max] + "…";
    }

    private static string Subject(IReadOnlyList<Notice> notices)
    {
        var counts = notices
            .GroupBy(n => n.State.Verdict)
            .OrderByDescending(g => g.Key)
            .Select(g => $"{g.Count()} {g.Key.ToString().ToUpperInvariant()}");
        return $"CLog: {notices.Count} fel ({string.Join(", ", counts)})";
    }
}
