using System.Text.Json.Nodes;
namespace AiExamProctoring.Services.Proctoring;
public static class AbsenceEvidenceMetadata
{
    public static string SegmentLabel(string? json)
    {
        try
        {
            var node = JsonNode.Parse(json ?? "{}");
            var start = node?["startedAt"]?.GetValue<long>();
            var end = node?["endedAt"]?.GetValue<long>();
            var reason = node?["stopReason"]?.GetValue<string>();
            if (start is long from && end is long until)
                return $"{DateTimeOffset.FromUnixTimeMilliseconds(from):u} – {DateTimeOffset.FromUnixTimeMilliseconds(until):u} · " +
                    (reason switch {
                        "physical_return" => "Ended on reported return",
                        "segment_rollover" => "Recording continues in the next available segment",
                        "exam_blocked" => "Ended at exam block",
                        "camera_lost" => "Camera lost",
                        _ => "Coverage may be incomplete"
                    });
        }
        catch (Exception error) when (error is System.Text.Json.JsonException or InvalidOperationException or ArgumentException) { }
        return "Segment timing unavailable";
    }
    private static readonly string[] Reasons = ["physical_return", "exam_blocked", "exam_ended", "camera_lost", "recorder_error", "recording_limit"];
    public static string? Merge(string? existing, string? metadata)
    {
        if (metadata is null) return existing;
        if (metadata.Length > 2000) throw new ArgumentException("Evidence metadata too large.");
        try {
            var input = JsonNode.Parse(metadata) as JsonObject ?? throw new ArgumentException("Invalid evidence metadata.");
            var reason = input["stopReason"]?.GetValue<string>();
            if (!Reasons.Contains(reason)) throw new ArgumentException("Invalid evidence stop reason.");
            var root = existing is null ? new JsonObject() : JsonNode.Parse(existing) as JsonObject ?? new JsonObject();
            root["evidenceCapture"] = new JsonObject { ["stopReason"] = reason,
                ["incomplete"] = reason != "physical_return", ["reportedBy"] = "browser",
                ["audioVerified"] = false };
            return root.ToJsonString();
        } catch (System.Text.Json.JsonException e) { throw new ArgumentException("Invalid evidence metadata.", e); }
          catch (InvalidOperationException e) { throw new ArgumentException("Invalid evidence metadata.", e); }
    }
    public static string Label(string? json)
    {
        try {
            var reason = JsonNode.Parse(json ?? "{}")?["evidenceCapture"]?["stopReason"]?.GetValue<string>();
            return reason switch {
                "physical_return" => "Recorded until reported return; playback and audio require review.",
                "recording_limit" => "Incomplete clip: recording limit reached.",
                "camera_lost" => "Incomplete clip: camera stream ended.",
                "recorder_error" => "Incomplete clip: recorder error.",
                "exam_blocked" => "Incomplete clip: exam blocked before return.",
                "exam_ended" => "Incomplete clip: recording ended before return.",
                _ => "Recording completeness not established." };
        } catch { return "Recording completeness not established."; }
    }
}
