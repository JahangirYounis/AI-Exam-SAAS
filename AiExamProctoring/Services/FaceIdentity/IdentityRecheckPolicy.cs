using System.Text.Json;
namespace AiExamProctoring.Services.FaceIdentity;

public sealed record IdentityRecheck(int Count, string[] FrameHashes, Guid? SequenceId = null);
public static class IdentityRecheckPolicy
{
    // Initial observation plus one independent follow-up batch.
    public const int RequiredMismatchBatches = 2;
    public const int MinimumIntervalSeconds = 2;
    public const int SuggestedIntervalSeconds = 3;
    public const int MaximumSuggestedIntervalSeconds = 10;
    public const string ConfirmationMarker = "two_fresh_batches";
    private const string Prefix = "Identity recheck pending: ";
    public static IdentityRecheck Advance(string? previousDescription, DateTime? previousAt,
        DateTime now, string frameHash, Guid? sequenceId = null)
    {
        if (string.IsNullOrWhiteSpace(frameHash)) throw new ArgumentException("A fresh frame digest is required.", nameof(frameHash));
        IdentityRecheck? previous = null;
        if (previousAt is not null && now >= previousAt.Value && now - previousAt.Value < TimeSpan.FromSeconds(70) &&
            previousDescription?.StartsWith(Prefix, StringComparison.Ordinal) == true)
        {
            try { previous = JsonSerializer.Deserialize<IdentityRecheck>(previousDescription[Prefix.Length..]); }
            catch (JsonException) { }
        }
        if (previous is null || previous.SequenceId != sequenceId || previous.Count is < 1 or > RequiredMismatchBatches || previous.FrameHashes is null || previous.FrameHashes.Length != previous.Count)
            return new(1, new[] { frameHash }, sequenceId);
        if (previous.Count == RequiredMismatchBatches || now - previousAt!.Value < TimeSpan.FromSeconds(MinimumIntervalSeconds) || previous.FrameHashes.SelectMany(x => x.Split(':')).Intersect(frameHash.Split(':'), StringComparer.Ordinal).Any()) return previous;
        return new(previous.Count + 1, previous.FrameHashes.Append(frameHash).ToArray(), sequenceId);
    }
    public static bool IsConfirmed(IdentityRecheck value) => value.Count >= RequiredMismatchBatches;
    public static string Describe(IdentityRecheck value) => Prefix + JsonSerializer.Serialize(value);
}