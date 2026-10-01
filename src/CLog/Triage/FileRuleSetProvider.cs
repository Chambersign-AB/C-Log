using Microsoft.Extensions.Logging;

namespace CLog.Triage;

/// <summary>
/// Reads rules.json and re-reads it when it changes, so a rule can be added to silence a
/// noisy error without restarting the service. A broken file keeps the last good rules.
/// </summary>
public sealed class FileRuleSetProvider : IRuleSetProvider
{
    private readonly string _path;
    private readonly ILogger<FileRuleSetProvider> _logger;
    private readonly Lock _gate = new();

    private RuleSet _cached = RuleSet.Empty;
    private DateTime _cachedWriteTimeUtc = DateTime.MinValue;
    private bool _loadedOnce;

    public FileRuleSetProvider(string path, ILogger<FileRuleSetProvider> logger)
    {
        _path = path;
        _logger = logger;
    }

    public RuleSet Current
    {
        get
        {
            try
            {
                if (!File.Exists(_path))
                {
                    if (!_loadedOnce)
                    {
                        _logger.LogWarning("Rules file {Path} not found; no errors will be filtered", _path);
                        _loadedOnce = true;
                    }

                    return _cached;
                }

                var writeTime = File.GetLastWriteTimeUtc(_path);

                lock (_gate)
                {
                    if (writeTime != _cachedWriteTimeUtc)
                    {
                        _cached = RuleSet.Parse(File.ReadAllText(_path));
                        _cachedWriteTimeUtc = writeTime;
                        _loadedOnce = true;
                        _logger.LogInformation(
                            "Loaded {Count} ignore rule(s) from {Path}", _cached.Ignore.Count, _path);
                    }

                    return _cached;
                }
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
            {
                // Keeping the last good rules beats filtering nothing because of a stray comma.
                _logger.LogError(ex, "Could not read {Path}; keeping the previously loaded rules", _path);
                return _cached;
            }
        }
    }
}
