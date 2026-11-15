using System.Text.Json;
namespace AiExamProctoring.Services.Proctoring;

public sealed record LiveTileStatus(string State, string Label)
{
    public static LiveTileStatus Evaluate(string status, string? incidents, string? health, DateTime now, string? objectTracking = null)
    {
        if (status == "ProctoringBlocked") return new("blocked", "Blocked — open review for the reason and authorized action");
        if (status != "InProgress") return new("uncertain", "Examination is not active");
        if (!string.IsNullOrEmpty(health))
        {
            try
            {
                var observed = JsonSerializer.Deserialize<DeviceHealthEpisode>(health);
                if (observed is null) return new("uncertain", "Monitoring state unavailable");
                if (observed.IsTechnicalHold)
                    return new("blocked", "Technical device hold — no cheating strike; check recovery evidence");
                if (now.ToUniversalTime() - observed.Seen.ToUniversalTime() > TimeSpan.FromSeconds(15))
                    return new("uncertain", "Camera/microphone telemetry stale — checking connection");
                if (observed.Issue != "healthy")
                    return new("uncertain", $"Technical device issue: {observed.Issue} — check connection");
            }
            catch (JsonException) { return new("uncertain", "Monitoring state unreadable"); }
        }
        try
        {
            var objectsUncertain = false;
            if (!string.IsNullOrWhiteSpace(objectTracking))
            {
                var scene = JsonSerializer.Deserialize<ObjectSceneState>(objectTracking);
                if ((scene?.LastUsableFrame ?? scene?.LastFrame) is DateTime last && now - last <= TimeSpan.FromSeconds(15))
                {
                    var prohibited = scene!.Tracks.Where(track => track.LogId.HasValue && !track.Closed &&
                            !track.Dismissed && !track.ReviewOnly && !track.Uncertain && !track.MissingSince.HasValue)
                        .Select(track => ProctoringObjectPolicy.Title(track.Label)).Distinct().ToArray();
                    if (prohibited.Length > 0)
                        return new("active", string.Join(", ", prohibited));
                }
                objectsUncertain = scene?.Tracks.Any(t => t.LogId.HasValue && !t.Closed && !t.Dismissed &&
                    (t.Uncertain || now - t.Last > TimeSpan.FromSeconds(15))) == true;
            }
            var episodes = JsonSerializer.Deserialize<Dictionary<string, IncidentEpisode>>(incidents ?? "{}") ?? [];
            // Object episodes are keyed "object:<track>" (not "objects").
            // Include active speech and face-visibility review so these modules
            // cannot silently show a green tile when their incident is current.
            var visual = episodes.Where(p => p.Key is "attention" or "persons" or "absence").Select(p => p.Value).ToArray();
            if (visual.Length == 0 || visual.All(e => now - e.LastSampleUtc > TimeSpan.FromSeconds(15)))
                return new("uncertain", "Monitoring updates unavailable");
            if (episodes.TryGetValue("absence", out var absence) && absence.Emitted &&
                absence.LastActive == true && now - absence.LastSampleUtc <= TimeSpan.FromSeconds(15))
                return new("active", "Student absent from camera view");
            if (episodes.TryGetValue("attention", out var attention) && attention.Emitted &&
                attention.StartedUtc.HasValue && now - attention.LastSampleUtc <= TimeSpan.FromSeconds(15))
                return new("active", "Looking Away — review required");
            if (episodes.Any(p => p.Key != "mouth" && p.Value.Emitted && p.Value.StartedUtc.HasValue && now - p.Value.LastSampleUtc <= TimeSpan.FromSeconds(15)))
                return new("active", "Active rule observation — review required");
            if (objectsUncertain || visual.Any(e => now - e.LastSampleUtc > TimeSpan.FromSeconds(15)))
                return new("uncertain", "Monitoring updates partially unavailable");
            if (visual.Any(e => e.Emitted || e.StartedUtc is null && e.ClearSinceUtc is null))
                return new("uncertain", "Monitoring uncertain — awaiting reliable observations");
            return string.IsNullOrEmpty(health) ? new("uncertain", "Waiting for camera/microphone health")
                : new("normal", "No current incident flagged");
        }
        catch (JsonException) { return new("uncertain", "Monitoring state unavailable"); }
    }
}

