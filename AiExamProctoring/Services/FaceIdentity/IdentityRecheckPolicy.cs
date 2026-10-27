using System.Text.Json;
namespace AiExamProctoring.Services.FaceIdentity;

public sealed record IdentityRecheck(int Count, string[] FrameHashes);
public static class IdentityRecheckPolicy
{
    private const string Prefix = "Identity recheck pending: ";
    public static IdentityRecheck Advance(string? previousDescription, DateTime? previousAt,
        DateTime now, string frameHash)
    {
        IdentityRecheck? previous = null;
        if (previousAt is not null && now >= previousAt.Value && now - previousAt.Value < TimeSpan.FromSeconds(45) &&
            previousDescription?.StartsWith(Prefix, StringComparison.Ordinal) == true)
        {
            try { previous = JsonSerializer.Deserialize<IdentityRecheck>(previousDescription[Prefix.Length..]); }
            catch (JsonException) { }
        }
        if (previous is null || previous.Count is < 1 or > 2 || previous.FrameHashes is null || previous.FrameHashes.Length != previous.Count)
            return new(1, new[] { frameHash });
        if (now - previousAt!.Value < TimeSpan.FromMilliseconds(400) || previous.FrameHashes.SelectMany(x => x.Split(':')).Intersect(frameHash.Split(':'), StringComparer.Ordinal).Any()) return previous;
        return new(previous.Count + 1, previous.FrameHashes.Append(frameHash).ToArray());
    }
    public static string Describe(IdentityRecheck value) => Prefix + JsonSerializer.Serialize(value);
}
