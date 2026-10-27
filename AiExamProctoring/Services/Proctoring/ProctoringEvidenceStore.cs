using Microsoft.Extensions.Options;

namespace AiExamProctoring.Services.Proctoring;

public sealed record ProctoringEvidenceFile(Stream Stream, string ContentType);

public interface IProctoringEvidenceStore
{
    Task<string?> SaveDataUrlAsync(string candidateKey, int examId, int attemptId, string? dataUrl, CancellationToken cancellationToken = default);
    Task<string?> SaveUploadAsync(string candidateKey, int examId, int attemptId, IFormFile? file, CancellationToken cancellationToken = default);
    Task<ProctoringEvidenceFile?> OpenReadAsync(string? storageKey, CancellationToken cancellationToken = default);
    Task DeleteAsync(string? storageKey, CancellationToken cancellationToken = default);
}

/// <summary>
/// Stores confirmed-event images/clips below App_Data, never below wwwroot. Evidence is
/// available only through authenticated, ownership-aware controller actions.
/// </summary>
public sealed class LocalProctoringEvidenceStore : IProctoringEvidenceStore
{
    private const string JpegPrefix = "data:image/jpeg;base64,";
    private const string PngPrefix = "data:image/png;base64,";

    private readonly string _evidenceDirectory;
    private readonly ProctoringOptions _options;

    public LocalProctoringEvidenceStore(
        IWebHostEnvironment environment,
        IOptions<ProctoringOptions> options)
    {
        _options = options.Value;
        _evidenceDirectory = Path.Combine(AiExamProctoring.Services.Web.PrivateStorage.Root(environment),
            "ProctoringEvidence");
    }

    public async Task<string?> SaveDataUrlAsync(
        string candidateKey,
        int examId,
        int attemptId,
        string? dataUrl,
        CancellationToken cancellationToken = default)
    {
        if (!_options.CaptureEvidence || string.IsNullOrWhiteSpace(dataUrl))
            return null;

        var isJpeg = dataUrl.StartsWith(JpegPrefix, StringComparison.OrdinalIgnoreCase);
        var isPng = dataUrl.StartsWith(PngPrefix, StringComparison.OrdinalIgnoreCase);
        if (!isJpeg && !isPng)
            return null;

        var commaIndex = dataUrl.IndexOf(',');
        if (commaIndex < 0)
            return null;

        // Base64 expands bytes by roughly 4/3. Reject oversized text before decoding.
        var maxEvidenceBytes = Math.Clamp(_options.MaxEvidenceBytes, 10_000, 2_000_000);
        var maximumBase64Length = ((maxEvidenceBytes + 2) / 3 * 4) + 8;
        var base64 = dataUrl[(commaIndex + 1)..];
        if (base64.Length > maximumBase64Length)
            return null;

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return null;
        }

        if (bytes.Length == 0 || bytes.Length > maxEvidenceBytes)
            return null;

        if (isJpeg && !HasJpegSignature(bytes))
            return null;
        if (isPng && !HasPngSignature(bytes))
            return null;

        try
        {
            var extension = isJpeg ? ".jpg" : ".png";
            var storageKey = BuildStorageKey(candidateKey, examId, attemptId, extension);
            var path = ResolveStoragePath(storageKey);
            if (path is null)
                return null;

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            await File.WriteAllBytesAsync(path, bytes, cancellationToken);
            return storageKey;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public async Task<string?> SaveUploadAsync(
        string candidateKey,
        int examId,
        int attemptId,
        IFormFile? file,
        CancellationToken cancellationToken = default)
    {
        if (!_options.CaptureEvidence || file is null || file.Length <= 0)
            return null;

        var maxBytes = Math.Clamp(_options.MaxVideoEvidenceBytes, 250_000, 20_000_000);
        var mediaType = file.ContentType.Split(';', 2)[0].Trim();
        if (file.Length > maxBytes ||
            !(mediaType.Equals("video/webm", StringComparison.OrdinalIgnoreCase) ||
              mediaType.Equals("audio/webm", StringComparison.OrdinalIgnoreCase) || mediaType.Equals("audio/wav", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        byte[] bytes;
        try
        {
            await using var input = file.OpenReadStream();
            using var buffer = new MemoryStream(capacity: checked((int)file.Length));
            await input.CopyToAsync(buffer, cancellationToken);
            bytes = buffer.ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OverflowException)
        {
            return null;
        }

        var wav = mediaType.Equals("audio/wav", StringComparison.OrdinalIgnoreCase);
        var validWav = bytes.Length >= 44 && System.Text.Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(bytes, 8, 4) == "WAVE";
        if (bytes.Length == 0 || bytes.Length > maxBytes || (wav ? !validWav : !HasWebmSignature(bytes)))
            return null;

        try
        {
            var storageKey = BuildStorageKey(candidateKey, examId, attemptId, wav ? ".wav" : ".webm");
            var path = ResolveStoragePath(storageKey);
            if (path is null)
                return null;

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, bytes, cancellationToken);
            return storageKey;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public Task<ProctoringEvidenceFile?> OpenReadAsync(
        string? storageKey,
        CancellationToken cancellationToken = default)
    {
        if (!IsSafeStorageKey(storageKey))
            return Task.FromResult<ProctoringEvidenceFile?>(null);

        var path = ResolveStoragePath(storageKey!);
        if (path is null)
            return Task.FromResult<ProctoringEvidenceFile?>(null);
        if (!File.Exists(path))
            return Task.FromResult<ProctoringEvidenceFile?>(null);

        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);

        var extension = Path.GetExtension(storageKey!);
        var contentType = extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            ? "image/png"
            : extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) ? "audio/wav" : extension.Equals(".webm", StringComparison.OrdinalIgnoreCase)
                ? "video/webm"
                : "image/jpeg";

        return Task.FromResult<ProctoringEvidenceFile?>(new(stream, contentType));
    }

    public Task DeleteAsync(
        string? storageKey,
        CancellationToken cancellationToken = default)
    {
        if (!IsSafeStorageKey(storageKey))
            return Task.CompletedTask;

        var path = ResolveStoragePath(storageKey!);
        if (path is null)
            return Task.CompletedTask;
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // A transient file lock must not break the database review workflow;
            // the retention cleanup service will retry later.
        }
        catch (UnauthorizedAccessException)
        {
            // Deployment permissions are surfaced by server logging/operations,
            // while the authorized review action remains deterministic.
        }

        return Task.CompletedTask;
    }

    private static string BuildStorageKey(
        string candidateKey,
        int examId,
        int attemptId,
        string extension)
    {
        var safeCandidate = SanitizeSegment(candidateKey);
        var safeExamId = Math.Max(0, examId);
        var safeAttemptId = Math.Max(0, attemptId);
        var fileName = $"attempt-{safeAttemptId}-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}{extension}";
        return $"candidate-{safeCandidate}/exam-{safeExamId}/{fileName}";
    }

    private static string SanitizeSegment(string? value)
    {
        var source = string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
        var safe = new string(source
            .Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_')
            .Take(64)
            .ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "unknown" : safe;
    }

    private string? ResolveStoragePath(string storageKey)
    {
        if (!IsSafeStorageKey(storageKey))
            return null;

        var normalizedKey = storageKey.Replace('/', Path.DirectorySeparatorChar);
        var root = Path.GetFullPath(_evidenceDirectory);
        var path = Path.GetFullPath(Path.Combine(root, normalizedKey));
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    private static bool IsSafeStorageKey(string? storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey) ||
            storageKey.Length > 320 ||
            Path.IsPathRooted(storageKey) ||
            storageKey.Contains(':') ||
            storageKey.Contains('\\'))
        {
            return false;
        }

        var segments = storageKey.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length is < 1 or > 3 || segments.Any(x => x is "." or ".."))
            return false;

        return storageKey.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
               storageKey.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
               storageKey.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) || storageKey.EndsWith(".wav", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasJpegSignature(byte[] bytes) =>
        bytes.Length >= 4 &&
        bytes[0] == 0xFF && bytes[1] == 0xD8 &&
        bytes[^2] == 0xFF && bytes[^1] == 0xD9;

    private static bool HasPngSignature(byte[] bytes) =>
        bytes.Length >= 8 &&
        bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
        bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A;

    private static bool HasWebmSignature(byte[] bytes) =>
        bytes.Length >= 4 &&
        bytes[0] == 0x1A && bytes[1] == 0x45 && bytes[2] == 0xDF && bytes[3] == 0xA3;
}
