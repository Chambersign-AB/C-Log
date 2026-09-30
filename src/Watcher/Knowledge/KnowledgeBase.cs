using Microsoft.Extensions.Logging;

namespace Watcher.Knowledge;

/// <summary>
/// Serves known-errors.md to the model. The file is re-read when it changes on disk, so a
/// person can add a known error and have the next cycle use it without a restart.
/// </summary>
public sealed class KnowledgeBase : IKnowledgeSource
{
    private readonly string _path;
    private readonly ILogger<KnowledgeBase> _logger;
    private readonly Lock _gate = new();

    private string _cached = "";
    private DateTime _cachedWriteTimeUtc = DateTime.MinValue;
    private bool _warnedMissing;

    public KnowledgeBase(string path, ILogger<KnowledgeBase> logger)
    {
        _path = path;
        _logger = logger;
    }

    public string Read()
    {
        try
        {
            if (!File.Exists(_path))
            {
                if (!_warnedMissing)
                {
                    _logger.LogWarning("Knowledge file {Path} not found; judging without known errors", _path);
                    _warnedMissing = true;
                }

                return "";
            }

            _warnedMissing = false;
            var writeTime = File.GetLastWriteTimeUtc(_path);

            lock (_gate)
            {
                if (writeTime != _cachedWriteTimeUtc)
                {
                    _cached = File.ReadAllText(_path);
                    _cachedWriteTimeUtc = writeTime;
                    _logger.LogInformation("Loaded knowledge file {Path} ({Length} characters)", _path, _cached.Length);
                }

                return _cached;
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not read knowledge file {Path}; judging without known errors", _path);
            return _cached;
        }
    }
}
