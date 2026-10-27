namespace AiExamProctoring.Services.Proctoring;

// Persisted inside the existing session, never supplied by the browser.
public sealed class IncidentEpisode
{
    public DateTime LastSampleUtc { get; set; }
    public DateTime? StartedUtc { get; set; }
    public DateTime? ClearSinceUtc { get; set; }
    public DateTime? LastEmittedUtc { get; set; }
    public bool Emitted { get; set; }
    public int Samples { get; set; }
}

public static class IncidentLifecycle
{
    public static bool Observe(IncidentEpisode state, bool? active, DateTime now,
        double persistenceSeconds, double recoverySeconds, double maximumGapSeconds, double cooldownSeconds)
    {
        if (now <= state.LastSampleUtc) return false; // stale/duplicate inference completion
        var gap = (now - state.LastSampleUtc).TotalSeconds > maximumGapSeconds;
        state.LastSampleUtc = now;
        if (gap || active is null)
        {
            state.StartedUtc = null;
            state.ClearSinceUtc = null;
            state.Samples = 0;
            // Lost frames, low quality and service outages are NOT evidence of recovery.
            if (active is null) return false;
        }
        if (active == false)
        {
            state.StartedUtc = null;
            state.Samples = 0;
            state.ClearSinceUtc ??= now;
            if ((now - state.ClearSinceUtc.Value).TotalSeconds >= recoverySeconds)
                state.Emitted = false;
            return false;
        }
        state.ClearSinceUtc = null;
        state.StartedUtc ??= now;
        state.Samples++;
        if (state.Emitted || (persistenceSeconds > 0 && state.Samples < 2) ||
            (now - state.StartedUtc.Value).TotalSeconds < persistenceSeconds ||
            state.LastEmittedUtc.HasValue && (now - state.LastEmittedUtc.Value).TotalSeconds < cooldownSeconds)
            return false;
        state.Emitted = true;
        state.LastEmittedUtc = now;
        return true;
    }
}

public sealed record IncidentObservation(string Key, bool? Active, Models.ProctoringLog Log);
