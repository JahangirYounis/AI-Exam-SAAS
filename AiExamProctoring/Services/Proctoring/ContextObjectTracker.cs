using AiExamProctoring.Services.ObjectDetection;
namespace AiExamProctoring.Services.Proctoring;
public sealed class ObjectTrack
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N"); public string Label { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
    public DateTime First { get; set; }
    public DateTime Last { get; set; }
    public DateTime? MissingSince { get; set; }
    public int Samples { get; set; }
    public decimal MaxConfidence { get; set; }
    public int? LogId { get; set; }
    public bool Closed { get; set; }
    public bool Dismissed { get; set; }
    public bool ReviewOnly { get; set; }
    public bool Uncertain { get; set; }
    public string Context { get; set; } = "";
}
public sealed class ObjectSceneState
{
    public List<ObjectTrack> Tracks { get; set; } = new(); public List<double> Reference { get; set; } = new();
    public DateTime? LastFrame { get; set; }
    public DateTime? LastUsableFrame { get; set; }
    public string? LastFingerprint { get; set; }
    public int ReferenceSamples { get; set; }
    public int Revision { get; set; }
    public string? Model { get; set; }
}
public static class ContextObjectTracker
{
    public static bool Update(ObjectSceneState state, ObjectDetectionResponse result, ExamObjectPolicy policy, DateTime now)
    {
        if (state.LastFrame >= now) return false;
        state.LastFrame = now;
        var replay = result.FrameFingerprint is not null && result.FrameFingerprint == state.LastFingerprint;
        if (result.FrameUsable == false || replay || result.FrameWidth <= 0 || result.FrameHeight <= 0)
        {
            foreach (var track in state.Tracks.Where(t => !t.Closed))
            {
                MarkUncertain(track);
            }
            return false;
        }
        if (state.LastUsableFrame is DateTime last && now - last > TimeSpan.FromSeconds(4))
            foreach (var track in state.Tracks) track.MissingSince = null;
        state.LastUsableFrame = now;
        state.LastFingerprint = result.FrameFingerprint;
        state.Tracks.RemoveAll(t => t.Closed && (now - t.Last).TotalSeconds > 30);
        var signature = result.SceneSignature;
        var valid = signature.Count == 64 && signature.All(x => double.IsFinite(x) && x >= 0 && x <= 1);
        var changed = state.Model is not null && state.Model != result.ModelSha256 || valid && state.Reference.Count == 64 && signature.Zip(state.Reference, (a, b) => Math.Abs(a - b)).Average() > .22;
        if (changed)
        {
            state.Revision++;
            state.ReferenceSamples = 0;
            foreach (var track in state.Tracks.Where(t => !t.Closed)) MarkUncertain(track);
        }
        if (valid) { state.Reference = signature; state.ReferenceSamples = Math.Min(100, state.ReferenceSamples + 1); }
        state.Model = result.ModelSha256;
        var candidates = new List<ObjectTrack>();
        var uncertainBoxes = new List<ObjectTrack>();
        var supported = result.Classes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var prohibited = policy.Prohibited.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var d in result.Detections.Where(d => supported.Contains(d.Label) && prohibited.Contains(d.Label) &&
            d.Confidence is > 0 and <= 1).OrderByDescending(d => d.Confidence).Take(20))
        {
            if (d.X < 0 || d.Y < 0 || d.Width <= 0 || d.Height <= 0 || (long)d.X + d.Width > result.FrameWidth || (long)d.Y + d.Height > result.FrameHeight) continue;
            var candidate = new ObjectTrack { Label = d.Label.ToLowerInvariant(), X = (double)d.X / result.FrameWidth, Y = (double)d.Y / result.FrameHeight, W = (double)d.Width / result.FrameWidth, H = (double)d.Height / result.FrameHeight, First = now, Last = now, Samples = 1, MaxConfidence = d.Confidence, Context = d.ContextReason ?? "uncertain" };
            var reviewContext = d.ContextReason is "foreground_context_unverified" or "primary_device_or_room_display_unverified";
            candidate.ReviewOnly = !ProctoringObjectPolicy.CanConfirmAutomatically(candidate.Label) || !d.AlertEligible;
            if (d.Confidence < ProctoringObjectPolicy.Threshold(policy, d.Label) ||
                !d.AlertEligible && !reviewContext)
            {
                uncertainBoxes.Add(candidate);
                continue;
            }
            if (!candidates.Any(c => c.Label == candidate.Label && IoU(c, candidate) >= .45))
                candidates.Add(candidate);
        }
        foreach (var track in state.Tracks.Where(t => !t.Closed))
        {
            if (!supported.Contains(track.Label) || !prohibited.Contains(track.Label) ||
                HasUncertainBox(track) || !track.ReviewOnly && candidates.Any(box =>
                    box.ReviewOnly && box.Label == track.Label && IoU(box, track) >= .25))
                MarkUncertain(track);
            else if (track.MissingSince is DateTime missing && (now - missing).TotalSeconds >= policy.RecoverySeconds)
                track.Closed = true;
        }
        var open = state.Tracks.Where(t => !t.Closed).ToList();
        var used = new HashSet<string>();
        var matched = new HashSet<ObjectTrack>();
        var spatialMatches = from track in open
                             from candidate in candidates
                             where track.Label == candidate.Label
                             let overlap = IoU(track, candidate)
                             where overlap >= .25
                             orderby overlap descending
                             select (track, candidate);
        foreach (var (track, candidate) in spatialMatches.ToArray())
        {
            if (used.Contains(track.Id) || matched.Contains(candidate)) continue;
            Observe(track, candidate);
        }
        foreach (var group in candidates.Where(c => !matched.Contains(c)).GroupBy(c => c.Label))
        {
            var remainingTracks = open.Where(t => t.Label == group.Key && !used.Contains(t.Id)).ToArray();
            var remainingBoxes = group.ToArray();
            // With one unmatched box and track, a jump in position can be the same object.
            if (remainingTracks.Length == 1 && remainingBoxes.Length == 1)
            {
                Observe(remainingTracks[0], remainingBoxes[0]);
                continue;
            }
            // Multiple unmatched boxes while prior objects are still open are
            // ambiguous after a camera move or occlusion. Require recovery
            // before creating replacement incidents for that class.
            if (remainingTracks.Length > 0)
            {
                foreach (var track in remainingTracks)
                {
                    MarkUncertain(track);
                    used.Add(track.Id);
                }
                continue;
            }
            foreach (var candidate in remainingBoxes)
            {
                if (state.Tracks.Count >= 32) break;
                state.Tracks.Add(candidate);
                used.Add(candidate.Id);
            }
        }
        foreach (var track in state.Tracks.Where(t => !t.Closed && !used.Contains(t.Id)))
        {
            if (!supported.Contains(track.Label) || !prohibited.Contains(track.Label) ||
                HasUncertainBox(track))
                continue;
            track.Uncertain = false;
            track.MissingSince ??= now;
        }
        return changed;

        bool HasUncertainBox(ObjectTrack track) =>
            uncertainBoxes.Any(box => box.Label == track.Label && IoU(box, track) >= .25) &&
            !candidates.Any(box => box.Label == track.Label && IoU(box, track) >= .25);

        void Observe(ObjectTrack track, ObjectTrack candidate)
        {
            used.Add(track.Id);
            matched.Add(candidate);
            if (track.LogId.HasValue && !track.ReviewOnly && candidate.ReviewOnly)
            {
                MarkUncertain(track);
                return;
            }
            if (track.Samples == 0 || (now - track.Last).TotalSeconds > 4 ||
                (track.LogId is null && (track.MissingSince.HasValue || track.ReviewOnly != candidate.ReviewOnly)))
            {
                track.First = now;
                track.Samples = 0;
            }
            track.X = candidate.X; track.Y = candidate.Y; track.W = candidate.W; track.H = candidate.H;
            track.Last = now; track.Samples++; track.MissingSince = null;
            track.Uncertain = false;
            if (!track.LogId.HasValue) track.ReviewOnly = candidate.ReviewOnly;
            track.MaxConfidence = Math.Max(track.MaxConfidence, candidate.MaxConfidence);
            track.Context = candidate.Context;
        }
    }
    private static void MarkUncertain(ObjectTrack track)
    {
        track.Uncertain = true;
        track.MissingSince = null;
        if (track.LogId is null) track.Samples = 0;
    }
    public static bool Ready(ObjectTrack t, ExamObjectPolicy p) => !t.Closed && !t.Uncertain && !t.MissingSince.HasValue &&
        t.Samples >= p.ConfirmationSamples && (t.Last - t.First).TotalSeconds >= p.ConfirmationSeconds;
    private static double IoU(ObjectTrack a, ObjectTrack b) { var area = Math.Max(0, Math.Min(a.X + a.W, b.X + b.W) - Math.Max(a.X, b.X)) * Math.Max(0, Math.Min(a.Y + a.H, b.Y + b.H) - Math.Max(a.Y, b.Y)); return area / Math.Max(.000001, a.W * a.H + b.W * b.H - area); }
}

