using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AiExamProctoring.Services.FaceIdentity;

public interface IFaceIdentityClient
{
    Task<FaceIdentityImage?> EnrollmentPhotoAsync(string studentId, int index, CancellationToken cancellationToken = default);
    Task<FaceIdentityCallResult> AddEnrollmentPhotosAsync(string studentId, IReadOnlyCollection<FaceIdentityImage> images, CancellationToken cancellationToken = default);
    Task<FaceIdentityCallResult> DeleteEnrollmentPhotoAsync(string studentId, int index, CancellationToken cancellationToken = default);
    Task<FaceIdentityCallResult> HealthAsync(CancellationToken cancellationToken = default);
    Task<FaceIdentityCallResult> TemplateStatusAsync(string studentId, CancellationToken cancellationToken = default);
    Task<FaceIdentityCallResult> EnrollAsync(string studentId, IReadOnlyCollection<FaceIdentityImage> images, CancellationToken cancellationToken = default);
    Task<FaceIdentityCallResult> VerifyAsync(string studentId, IReadOnlyCollection<FaceIdentityImage> images, CancellationToken cancellationToken = default);
    Task<FaceIdentityCallResult> DeleteTemplateAsync(string studentId, CancellationToken cancellationToken = default);
}

public sealed class FaceIdentityClient : IFaceIdentityClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly FaceIdentityOptions _options;
    private readonly ILogger<FaceIdentityClient> _logger;

    public FaceIdentityClient(HttpClient http, IOptions<FaceIdentityOptions> options, ILogger<FaceIdentityClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public Task<FaceIdentityCallResult> HealthAsync(CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Get, "api/model1/health", null, cancellationToken);

    public Task<FaceIdentityCallResult> TemplateStatusAsync(string studentId, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Get, $"api/model1/templates/{Uri.EscapeDataString(studentId)}", null, cancellationToken);

    public Task<FaceIdentityCallResult> DeleteTemplateAsync(string studentId, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"api/model1/templates/{Uri.EscapeDataString(studentId)}", null, cancellationToken);

    public Task<FaceIdentityCallResult> AddEnrollmentPhotosAsync(string studentId, IReadOnlyCollection<FaceIdentityImage> images, CancellationToken cancellationToken = default) =>
        SendImagesAsync($"api/model1/templates/{Uri.EscapeDataString(studentId)}/photos", studentId, images, cancellationToken, includeStudentId: false);

    public Task<FaceIdentityCallResult> DeleteEnrollmentPhotoAsync(string studentId, int index, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"api/model1/templates/{Uri.EscapeDataString(studentId)}/photos/{index}", null, cancellationToken);

    public Task<FaceIdentityCallResult> EnrollAsync(string studentId, IReadOnlyCollection<FaceIdentityImage> images, CancellationToken cancellationToken = default) =>
        SendImagesAsync("api/model1/enroll", studentId, images, cancellationToken);

    public Task<FaceIdentityCallResult> VerifyAsync(string studentId, IReadOnlyCollection<FaceIdentityImage> images, CancellationToken cancellationToken = default) =>
        SendImagesAsync("api/model1/verify", studentId, images, cancellationToken);

    public async Task<FaceIdentityImage?> EnrollmentPhotoAsync(string studentId, int index, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || index < 0 || index >= 10) return null;
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/model1/templates/{Uri.EscapeDataString(studentId)}/photos/{index}");
        if (!string.IsNullOrWhiteSpace(_options.ApiKey)) request.Headers.TryAddWithoutValidation("X-Face-Api-Key", _options.ApiKey);
        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            var type = response.Content.Headers.ContentType?.MediaType;
            if (type is not ("image/jpeg" or "image/png")) return null;
            return new FaceIdentityImage("enrollment", type, await response.Content.ReadAsByteArrayAsync(cancellationToken));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Enrollment photo service unavailable");
            return null;
        }
    }

    private Task<FaceIdentityCallResult> SendImagesAsync(string path, string studentId, IReadOnlyCollection<FaceIdentityImage> images, CancellationToken cancellationToken, bool includeStudentId = true)
    {
        var form = new MultipartFormDataContent();
        if (includeStudentId) form.Add(new StringContent(studentId), "student_id");
        foreach (var image in images)
        {
            var content = new ByteArrayContent(image.Bytes);
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(string.IsNullOrWhiteSpace(image.ContentType) ? "image/jpeg" : image.ContentType);
            form.Add(content, "files", string.IsNullOrWhiteSpace(image.FileName) ? "frame.jpg" : image.FileName);
        }
        return SendAsync(HttpMethod.Post, path, form, cancellationToken);
    }

    private async Task<FaceIdentityCallResult> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            content?.Dispose();
            return new(false, null, "Face Identity Verification is disabled.", true);
        }

        using var request = new HttpRequestMessage(method, path) { Content = content };
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            request.Headers.TryAddWithoutValidation("X-Face-Api-Key", _options.ApiKey);
        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            FaceIdentityResponse? data = null;
            try { data = JsonSerializer.Deserialize<FaceIdentityResponse>(json, JsonOptions); } catch { }
            if (response.IsSuccessStatusCode && data?.Ok == true)
                return new(true, data, data.Message ?? "Face identity request completed.");
            var message = data?.Message ?? data?.Detail ?? $"Face service returned HTTP {(int)response.StatusCode}.";
            return new(false, data, message, response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Face Identity service request failed: {Path}", path);
            return new(false, null, "Face Identity service is not running. Start AIProctoringService on port 8001.", true);
        }
    }
}
