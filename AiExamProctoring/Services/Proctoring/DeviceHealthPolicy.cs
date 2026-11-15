using System;

namespace AiExamProctoring.Services.Proctoring;

// Stored in ExamSessionAttempt.DeviceHealthJson. Optional appended arguments keep
// previously saved DeviceHealthEpisode JSON backward-compatible.
public sealed record DeviceHealthEpisode(
    string Issue, DateTime Started, DateTime Seen, int Warnings,
    bool CameraAffected = false, bool MicrophoneAffected = false,
    bool IsTechnicalHold = false, DateTime? ClientSeen = null,
    DateTime? RecoveryStarted = null, DateTime? RecoveryLast = null,
    int RecoverySamples = 0, string? LastFrameFingerprint = null);

public sealed record DeviceHealthDecision(DeviceHealthEpisode? Episode, bool Warn, bool Hold);

public sealed record DeviceReadinessSample(
    DateTime CapturedAtUtc, bool CameraTrackLive, bool MicrophoneTrackLive,
    bool CameraFreshFrame, bool MicrophoneAudioFramesAvailable,
    int CameraWidth, int CameraHeight, string? CameraFrameFingerprint);

public sealed record DeviceRecoveryDecision(DeviceHealthEpisode Episode, bool Accepted, bool Recovered, string Message);

public static class DeviceHealthPolicy
{
    public const string NetworkIssue = "network_interruption";

    public static bool IsIssue(string issue) => issue is
        "healthy" or "camera_disconnected" or "microphone_disconnected" or
        "camera_and_microphone_disconnected" or "camera_denied" or "microphone_denied" or
        "camera_muted" or "microphone_muted" or "camera_frozen" or "microphone_frozen" or
        "camera_dark" or "camera_unclear" or NetworkIssue;

    public static bool CameraAffected(string issue) => issue is
        "camera_disconnected" or "camera_and_microphone_disconnected" or "camera_denied" or
        "camera_muted" or "camera_frozen" or "camera_dark" or "camera_unclear" or NetworkIssue;

    public static bool MicrophoneAffected(string issue) => issue is
        "microphone_disconnected" or "camera_and_microphone_disconnected" or
        "microphone_denied" or "microphone_muted" or "microphone_frozen" or NetworkIssue;

    public static DeviceHealthDecision Sample(DeviceHealthEpisode? old, string issue, DateTime now,
        double holdSeconds = 9)
    {
        if (!IsIssue(issue)) throw new ArgumentException("Unknown device-health issue.", nameof(issue));
        holdSeconds = double.IsFinite(holdSeconds) ? Math.Clamp(holdSeconds, 5, 60) : 9;
        // A persisted technical hold is NOT cleared by an ordinary browser 'healthy'
        // string. Only the separate fresh-readiness endpoint may release it.
        if (old?.IsTechnicalHold == true)
        {
            var held = old with
            {
                Seen = now,
                ClientSeen = issue == NetworkIssue ? old.ClientSeen : now,
                Issue = issue == "healthy" ? old.Issue : issue,
                CameraAffected = old.CameraAffected || CameraAffected(issue),
                MicrophoneAffected = old.MicrophoneAffected || MicrophoneAffected(issue)
            };
            return new(held, false, false);
        }
        if (issue == "healthy")
        {
            if (old is not null && old.Issue != "healthy" &&
                now >= old.Seen && now - old.Seen <= TimeSpan.FromSeconds(8))
            {
                // A single normal browser sample cannot erase repeatedly
                // unusable frames from Model 2. Require two independent normal
                // samples spanning at least two seconds with no intervening fault.
                var recoverStarted = old.RecoveryStarted ?? now;
                var recoverSamples = old.RecoverySamples + 1;
                if (recoverSamples < 2 || (now - recoverStarted).TotalSeconds < 2)
                    return new(old with
                    {
                        Seen = now,
                        ClientSeen = now,
                        RecoveryStarted = recoverStarted,
                        RecoverySamples = recoverSamples
                    }, false, false);
            }
            return new(new("healthy", now, now, 0, ClientSeen: now), false, false);
        }

        // The previous browser heartbeat may be stale; retain its timestamp so a
        // server watchdog can safely keep counting consecutive missed heartbeats.
        var previousClientSeen = old?.ClientSeen;
        var continuous = old is not null && old.Issue != "healthy" &&
            now >= old.Seen && now - old.Seen <= TimeSpan.FromSeconds(8);
        var first = continuous ? old!.Started : now;
        var elapsed = (now - first).TotalSeconds;
        var warnings = elapsed >= Math.Min(holdSeconds - 1, 6) ? 3 : elapsed >= 3 ? 2 : 1;
        var episode = new DeviceHealthEpisode(issue, first, now, warnings,
            (continuous && old!.CameraAffected) || CameraAffected(issue),
            (continuous && old!.MicrophoneAffected) || MicrophoneAffected(issue),
            false, issue == NetworkIssue ? previousClientSeen : now);
        return new(episode, !continuous || warnings > old!.Warnings, elapsed >= holdSeconds);
    }

    // Browser reports are *technical readiness observations*, not cryptographic
    // proof of a physical device. Require fresh samples, a running microphone
    // processor and changing camera frames over time before self-release.
    public static DeviceRecoveryDecision Recover(DeviceHealthEpisode held, DeviceReadinessSample sample,
        DateTime now, double minimumSeconds = 2, int minimumSamples = 3)
    {
        if (!held.IsTechnicalHold) return new(held, false, false, "No technical hold is active.");
        minimumSeconds = double.IsFinite(minimumSeconds) ? Math.Clamp(minimumSeconds, 2, 10) : 2;
        minimumSamples = Math.Clamp(minimumSamples, 3, 8);
        var captured = sample.CapturedAtUtc.ToUniversalTime();
        if (captured < now.AddSeconds(-5) || captured > now.AddSeconds(2))
            return new(ResetRecovery(held), false, false, "Capture sample is stale. Retry using live devices.");
        if (held.CameraAffected && (!sample.CameraTrackLive || !sample.CameraFreshFrame ||
            sample.CameraWidth < 160 || sample.CameraHeight < 120 ||
            sample.CameraFrameFingerprint is not { Length: 8 } ||
            !System.Text.RegularExpressions.Regex.IsMatch(sample.CameraFrameFingerprint, "^[0-9a-f]{8}$")))
            return new(ResetRecovery(held), false, false, "Camera is not yet producing usable fresh frames.");
        if (held.MicrophoneAffected && (!sample.MicrophoneTrackLive || !sample.MicrophoneAudioFramesAvailable))
            return new(ResetRecovery(held), false, false, "Microphone permission or audio processing has not recovered.");
        var sequential = held.RecoveryLast.HasValue && now - held.RecoveryLast.Value >= TimeSpan.FromMilliseconds(700) &&
            now - held.RecoveryLast.Value <= TimeSpan.FromSeconds(5);
        if (held.RecoveryLast.HasValue && !sequential)
            held = ResetRecovery(held);
        if (held.CameraAffected && held.LastFrameFingerprint == sample.CameraFrameFingerprint)
            return new(ResetRecovery(held), false, false, "Camera frames appear frozen. Move slightly and retry.");
        var start = held.RecoveryStarted ?? now;
        var samples = held.RecoverySamples + 1;
        var updated = held with
        {
            RecoveryStarted = start,
            RecoveryLast = now,
            RecoverySamples = samples,
            LastFrameFingerprint = sample.CameraFrameFingerprint
        };
        var recovered = samples >= minimumSamples && (now - start).TotalSeconds >= minimumSeconds;
        return new(updated, true, recovered,
            recovered ? "Device readiness verified for this technical hold." :
            $"Fresh device checks {samples}/{minimumSamples}; keep both required devices active.");
    }

    private static DeviceHealthEpisode ResetRecovery(DeviceHealthEpisode held) =>
        held with { RecoveryStarted = null, RecoveryLast = null, RecoverySamples = 0, LastFrameFingerprint = null };
}

public sealed record DeviceHealthReply(bool Warning, string Issue, int WarningCount, bool NeedsReview, ProctoringState State);
public sealed record DeviceRecoveryReply(bool TechnicalHold, bool Recovered, int VerifiedSamples,
    string Message, ProctoringState? State);