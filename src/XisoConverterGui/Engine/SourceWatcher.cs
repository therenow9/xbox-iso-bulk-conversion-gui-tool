namespace XisoConverterGui;

/// <summary>
/// Watches the source folder and reports when it has settled after images were added,
/// removed or renamed.
///
/// The subtlety this exists for: a download in progress is a partial file. Rescanning
/// the instant a .iso appears would read a truncated image, fail the Xbox media
/// signature check, and label a brand new game NotXbox with a nonsense size. So a
/// change is only reported once every file it saw has stopped growing and is no longer
/// held open by whatever was writing it.
///
/// Clients that download to a temporary name and rename on completion settle almost
/// immediately; clients that grow the .iso in place are covered by the size check.
/// </summary>
public sealed class SourceWatcher : IDisposable
{
    /// <summary>Raised on a timer thread once the folder is quiet and stable.</summary>
    public event Action? Settled;

    /// <summary>
    /// False when the folder could not be watched at all. Worth surfacing: change
    /// notifications are unreliable over SMB, and a library on a NAS may silently
    /// never report anything, leaving Refresh as the only way to see new images.
    /// </summary>
    public bool IsActive => _watcher is not null;

    private const int PollMs = 1000;
    private const int StableTicksNeeded = 3;                     // unchanged for ~3s
    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Stall = TimeSpan.FromMinutes(10);

    private static readonly string[] Extensions = { ".iso", ".xiso" };

    private sealed class Candidate
    {
        public long Length = -1;
        public int StableTicks;
        public DateTime FirstSeen = DateTime.UtcNow;
    }

    private readonly Dictionary<string, Candidate> _candidates = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private readonly System.Timers.Timer _timer = new(PollMs) { AutoReset = true };

    private FileSystemWatcher? _watcher;
    private bool _dirty;
    private DateTime _lastEvent = DateTime.MinValue;

    public SourceWatcher()
    {
        _timer.Elapsed += (_, _) => Tick();
    }

    /// <summary>
    /// Points the watcher at a folder, or stops watching when it is null or missing.
    /// Failure is never fatal: a folder that cannot be watched just falls back to the
    /// Refresh button.
    /// </summary>
    public void Watch(string? folder)
    {
        Stop();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;

        try
        {
            var watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                InternalBufferSize = 64 * 1024,
            };
            foreach (var extension in Extensions) watcher.Filters.Add("*" + extension);

            watcher.Created += OnTouched;
            watcher.Changed += OnTouched;
            watcher.Renamed += OnRenamed;
            watcher.Deleted += OnRemoved;
            // A dropped event would leave the list stale; a full rescan recovers.
            watcher.Error += (_, _) => MarkDirty();

            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
            _timer.Start();
        }
        catch (Exception)
        {
            // Network shares and exotic filesystems can refuse to be watched.
            Stop();
        }
    }

    private void Stop()
    {
        _timer.Stop();
        if (_watcher is not null)
        {
            try { _watcher.EnableRaisingEvents = false; } catch { }
            _watcher.Dispose();
            _watcher = null;
        }
        lock (_gate)
        {
            _candidates.Clear();
            _dirty = false;
        }
    }

    private static bool IsWatched(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private void OnTouched(object sender, FileSystemEventArgs e)
    {
        if (!IsWatched(e.FullPath)) return;
        lock (_gate)
        {
            if (!_candidates.ContainsKey(e.FullPath)) _candidates[e.FullPath] = new Candidate();
            _dirty = true;
            _lastEvent = DateTime.UtcNow;
        }
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        // The common "download to a temp name, rename when done" case lands here.
        lock (_gate) { _candidates.Remove(e.OldFullPath); }
        if (IsWatched(e.FullPath)) OnTouched(sender, e); else MarkDirty();
    }

    private void OnRemoved(object sender, FileSystemEventArgs e)
    {
        lock (_gate) { _candidates.Remove(e.FullPath); }
        MarkDirty();
    }

    private void MarkDirty()
    {
        lock (_gate)
        {
            _dirty = true;
            _lastEvent = DateTime.UtcNow;
        }
    }

    private void Tick()
    {
        lock (_gate)
        {
            if (!_dirty) return;
            if (DateTime.UtcNow - _lastEvent < Quiet) return;

            foreach (var path in _candidates.Keys.ToList())
            {
                var candidate = _candidates[path];

                long length;
                try
                {
                    var info = new FileInfo(path);
                    if (!info.Exists) { _candidates.Remove(path); continue; }
                    length = info.Length;
                }
                catch
                {
                    _candidates.Remove(path);
                    continue;
                }

                if (length != candidate.Length)
                {
                    candidate.Length = length;      // still growing
                    candidate.StableTicks = 0;
                }
                else if (CanOpen(path))
                {
                    candidate.StableTicks++;
                }
                else
                {
                    candidate.StableTicks = 0;      // still held open by the writer
                }

                // One dead download must not block the list forever.
                if (candidate.StableTicks < StableTicksNeeded &&
                    DateTime.UtcNow - candidate.FirstSeen > Stall)
                {
                    _candidates.Remove(path);
                }
            }

            if (_candidates.Values.Any(c => c.StableTicks < StableTicksNeeded)) return;

            _candidates.Clear();
            _dirty = false;
        }

        Settled?.Invoke();
    }

    private static bool CanOpen(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        Stop();
        _timer.Dispose();
    }
}
