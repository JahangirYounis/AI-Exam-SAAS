namespace AiExamProctoring.Services.Proctoring;

public sealed record LiveSignal(int AttemptId, string Viewer, string Kind, string Data, DateTime Expires)
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
}

// Signalling only. Camera media travels over encrypted WebRTC, not through this queue.
public sealed class LiveSignalStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Queue<LiveSignal>> _queues = new();

    private readonly Dictionary<string, (string Teacher, DateTime Expires)> _viewers = new();
    public bool BindViewer(int attempt, string viewer, string teacher) {
        lock (_gate) {
            foreach(var k in _viewers.Where(p => p.Value.Expires < DateTime.UtcNow).Select(p => p.Key).ToArray()) _viewers.Remove(k);
            var key = $"{attempt}:{viewer}";
            if (_viewers.TryGetValue(key, out var old) && old.Teacher != teacher) return false;
            if (!_viewers.ContainsKey(key) && _viewers.Count >= 4096) return false;
            _viewers[key] = (teacher, DateTime.UtcNow.AddHours(12)); return true;
        }
    }
    public string? ViewerTeacher(int attempt, string viewer) {
        lock (_gate) return _viewers.TryGetValue($"{attempt}:{viewer}", out var v) && v.Expires > DateTime.UtcNow ? v.Teacher : null;
    }
    public bool Send(string channel, LiveSignal signal)
    {
        lock (_gate)
        {
            Clean();
            if (!_queues.TryGetValue(channel, out var queue))
            {
                if (_queues.Count >= 2000) return false;
                _queues[channel] = queue = new();
            }
            if (queue.Count >= 128) return false;
            queue.Enqueue(signal);
            return true;
        }
    }

    public LiveSignal[] Read(string channel)
    {
        lock (_gate)
        {
            Clean();
            return _queues.Remove(channel, out var queue) ? queue.ToArray() : [];
        }
    }

    private void Clean()
    {
        var now = DateTime.UtcNow;
        foreach (var key in _queues.Keys.ToArray())
        {
            var queue = _queues[key];
            while (queue.TryPeek(out var signal) && signal.Expires < now) queue.Dequeue();
            if (queue.Count == 0) _queues.Remove(key);
        }
    }
}
