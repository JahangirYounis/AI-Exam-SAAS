using System.Text.Json;

namespace AiExamProctoring.Services.Proctoring;

public sealed record SessionRecordingMetadata(string Source, Guid TimelineId, int Sequence,
    long StartedAt, long EndedAt, double MonotonicStart, double MonotonicEnd, string Surface, string Status,
    bool IncludesMicrophone = false)
{
    public const string ModelName = "Session timeline";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public bool HasMedia => Status != "missing";

    public static SessionRecordingMetadata? Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 4096) return null;
        try
        {
            var segment = JsonSerializer.Deserialize<SessionRecordingMetadata>(json, JsonOptions);
            return segment is { Source: "camera" or "screen", Sequence: > 0 and <= 100_000,
                Status: "recorded" or "interrupted" or "missing", Surface: "monitor" or "browser" or "window" or "unknown" } &&
                segment.TimelineId != Guid.Empty && segment.StartedAt > 0 &&
                segment.EndedAt > segment.StartedAt && segment.EndedAt - segment.StartedAt <= 180_000 &&
                double.IsFinite(segment.MonotonicStart) && double.IsFinite(segment.MonotonicEnd) &&
                segment.MonotonicStart >= 0 && segment.MonotonicEnd >= segment.MonotonicStart &&
                Math.Abs((segment.MonotonicEnd - segment.MonotonicStart) - (segment.EndedAt - segment.StartedAt)) < 5 &&
                segment.EndedAt <= DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeMilliseconds() ? segment : null;
        }
        catch (JsonException) { return null; }
    }

    public string Serialize() => JsonSerializer.Serialize(this, JsonOptions);

    public static DateTimeOffset? BrowserMarker(string? json) => BrowserTimestamp(json, "clientStartedAt");

    public static DateTimeOffset? BrowserReturn(string? json, DateTime? receivedEndUtc)
    {
        if (receivedEndUtc is not DateTime received || BrowserTimestamp(json, "clientObservedAt") is not { } returned)
            return null;
        var started = BrowserMarker(json);
        return started.HasValue && returned >= started.Value && returned.UtcDateTime <= DateTime.SpecifyKind(received, DateTimeKind.Utc).AddMinutes(2)
            ? returned : null;
    }

    private static DateTimeOffset? BrowserTimestamp(string? json, string property)
    {
        try
        {
            using var document = JsonDocument.Parse(json ?? "{}");
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (root.TryGetProperty("sourceObservations", out var source) && source.ValueKind == JsonValueKind.String)
            {
                using var observations = JsonDocument.Parse(source.GetString() ?? "{}");
                return ReadTimestamp(observations.RootElement, property);
            }
            return ReadTimestamp(root, property);
        }
        catch (JsonException) { return null; }
    }

    private static DateTimeOffset? ReadTimestamp(JsonElement root, string property) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String && value.TryGetDateTimeOffset(out var time) ? time : null;

    public static string UpdateBrowserMetadata(string? existing, string observation)
    {
        if (existing is null) return observation;
        try
        {
            if (System.Text.Json.Nodes.JsonNode.Parse(existing) is System.Text.Json.Nodes.JsonObject audit &&
                audit.ContainsKey("sourceObservations"))
            {
                audit["sourceObservations"] = observation;
                return audit.ToJsonString();
            }
        }
        catch (JsonException) { }
        return observation;
    }
}
