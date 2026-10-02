namespace CLog.Configuration;

/// <summary>
/// Notices about errors that need somebody: a line per error judged ANALYZE or KNOWN, sent
/// through the channels named here. Off while no channel is named.
/// </summary>
public sealed class NotifyOptions
{
    public const string Mail = "Mail";
    public const string Slack = "Slack";

    /// <summary>Comma-separated channel names, such as "Mail". Empty switches notices off.</summary>
    public string Channels { get; set; } = "";

    /// <summary>
    /// The least time between two notices about one fingerprint. An error that keeps
    /// happening is fetched every cycle; without this it would send a notice every cycle.
    /// </summary>
    public int RepeatMinutes { get; set; } = 1440;

    public MailgunOptions Mailgun { get; set; } = new();

    public IReadOnlyList<string> ChannelNames() =>
        Channels.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public bool Enabled => ChannelNames().Count > 0;

    public bool Uses(string channel) => ChannelNames().Contains(channel, StringComparer.OrdinalIgnoreCase);

    /// <summary>What stops the named channels from working, or null when notices are off or fully configured.</summary>
    public string? Problem()
    {
        foreach (var name in ChannelNames())
        {
            if (name.Equals(Slack, StringComparison.OrdinalIgnoreCase))
            {
                // Named in the configuration format already, so a silent no-op would look
                // like notices that are on and never arrive.
                return "CLog:Notify:Channels names Slack, which is not available yet; use Mail";
            }

            if (!name.Equals(Mail, StringComparison.OrdinalIgnoreCase))
            {
                return $"CLog:Notify:Channels names \"{name}\"; the only channel is Mail";
            }
        }

        if (!Uses(Mail))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(Mailgun.ApiKey))
        {
            return "CLog:Notify:Mailgun:ApiKey is not set; supply it through user-secrets or the environment";
        }

        if (string.IsNullOrWhiteSpace(Mailgun.Domain))
        {
            return "CLog:Notify:Mailgun:Domain is not set";
        }

        if (string.IsNullOrWhiteSpace(Mailgun.To))
        {
            return "CLog:Notify:Mailgun:To is not set";
        }

        return string.IsNullOrWhiteSpace(Mailgun.From) ? "CLog:Notify:Mailgun:From is not set" : null;
    }
}

public sealed class MailgunOptions
{
    /// <summary>The US region. A domain in the EU region is reached at https://api.eu.mailgun.net.</summary>
    public string ApiUrl { get; set; } = "https://api.mailgun.net";

    /// <summary>Mailgun API key. Supply via environment variable or user-secrets, never in a committed file.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>The sending domain registered with Mailgun.</summary>
    public string Domain { get; set; } = "";

    /// <summary>Recipient, or several separated by commas.</summary>
    public string To { get; set; } = "";

    public string From { get; set; } = "";
}
