using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CLog.Configuration;

namespace CLog.Notifications;

/// <summary>Sends a notice as a plain-text e-mail through Mailgun's HTTP API.</summary>
public sealed class MailgunNotifier : INotifier
{
    private readonly HttpClient _http;
    private readonly MailgunOptions _options;
    private readonly ILogger<MailgunNotifier> _logger;

    public MailgunNotifier(HttpClient http, IOptions<CLogOptions> options, ILogger<MailgunNotifier> logger)
    {
        _options = options.Value.Notify.Mailgun;
        _logger = logger;
        _http = http;

        _http.BaseAddress = new Uri(_options.ApiUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("api:" + _options.ApiKey)));
    }

    public string Name => NotifyOptions.Mail;

    public async Task<bool> SendAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["from"] = _options.From,
            ["to"] = _options.To,
            ["subject"] = notification.Subject,
            ["text"] = notification.Text
        });

        try
        {
            using var response = await _http.PostAsync(
                $"v3/{Uri.EscapeDataString(_options.Domain)}/messages", form, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            // The domain is named, never the recipients: an address is personal data.
            _logger.LogWarning(
                "Mailgun answered {StatusCode} for domain {Domain}; the notice was not sent",
                (int)response.StatusCode,
                _options.Domain);
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Could not reach Mailgun at {Url}; the notice was not sent", _options.ApiUrl);
            return false;
        }
    }
}
