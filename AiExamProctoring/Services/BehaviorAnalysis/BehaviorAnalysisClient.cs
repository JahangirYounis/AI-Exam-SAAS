using AiExamProctoring.Services.FaceIdentity;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AiExamProctoring.Services.BehaviorAnalysis;

public interface IBehaviorAnalysisClient
{
    Task<BehaviorCallResult> HealthAsync(CancellationToken cancellationToken = default);
    Task<BehaviorCallResult> PreCheckAsync(IReadOnlyCollection<FaceIdentityImage> images, CancellationToken cancellationToken = default);
    Task<BehaviorCallResult> StartSessionAsync(string sessionKey, IReadOnlyCollection<FaceIdentityImage> images, CancellationToken cancellationToken = default, bool screenCalibration = false);
    Task<BehaviorCallResult> AnalyzeAsync(string sessionKey, FaceIdentityImage image, CancellationToken cancellationToken = default);
    Task<BehaviorCallResult> EndSessionAsync(string sessionKey, CancellationToken cancellationToken = default);
}

public sealed class BehaviorAnalysisClient : IBehaviorAnalysisClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly BehaviorAnalysisOptions _options;
    private readonly ILogger<BehaviorAnalysisClient> _logger;

    public BehaviorAnalysisClient(HttpClient http, IOptions<BehaviorAnalysisOptions> options, ILogger<BehaviorAnalysisClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public Task<BehaviorCallResult> HealthAsync(CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Get, "api/model2/health", null, cancellationToken);

    public Task<BehaviorCallResult> PreCheckAsync(IReadOnlyCollection<FaceIdentityImage> images, CancellationToken cancellationToken = default) =>
        SendImagesAsync("api/model2/precheck", null, images, cancellationToken);

    public Task<BehaviorCallResult> StartSessionAsync(string sessionKey, IReadOnlyCollection<FaceIdentityImage> images, CancellationToken cancellationToken = default, bool screenCalibration = false) =>
        SendImagesAsync("api/model2/sessions/start", sessionKey, images, cancellationToken, screenCalibration);

    public Task<BehaviorCallResult> AnalyzeAsync(string sessionKey, FaceIdentityImage image, CancellationToken cancellationToken = default)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(sessionKey), "session_key");
        var content = new ByteArrayContent(image.Bytes);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(string.IsNullOrWhiteSpace(image.ContentType) ? "image/jpeg" : image.ContentType);
        form.Add(content, "file", string.IsNullOrWhiteSpace(image.FileName) ? "frame.jpg" : image.FileName);
        return SendAsync(HttpMethod.Post, "api/model2/analyze", form, cancellationToken);
    }

    public Task<BehaviorCallResult> EndSessionAsync(string sessionKey, CancellationToken cancellationToken = default)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["session_key"] = sessionKey });
        return SendAsync(HttpMethod.Post, "api/model2/sessions/end", form, cancellationToken);
    }

    private Task<BehaviorCallResult> SendImagesAsync(string path, string? sessionKey, IReadOnlyCollection<FaceIdentityImage> images, CancellationToken cancellationToken, bool screenCalibration = false)
    {
        var form = new MultipartFormDataContent();
        if (!string.IsNullOrWhiteSpace(sessionKey)) form.Add(new StringContent(sessionKey), "session_key");
        if (screenCalibration) form.Add(new StringContent("true"), "screen_calibration");
        foreach (var image in images)
        {
            var content = new ByteArrayContent(image.Bytes);
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(string.IsNullOrWhiteSpace(image.ContentType) ? "image/jpeg" : image.ContentType);
            form.Add(content, "files", string.IsNullOrWhiteSpace(image.FileName) ? "frame.jpg" : image.FileName);
        }
        return SendAsync(HttpMethod.Post, path, form, cancellationToken);
    }

    private async Task<BehaviorCallResult> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            content?.Dispose();
            return new(false, null, "Behavior Analysis is disabled.", true);
        }
        using var request = new HttpRequestMessage(method, path) { Content = content };
        if (!string.IsNullOrWhiteSpace(_options.ApiKey)) request.Headers.Add("X-Behavior-Api-Key", _options.ApiKey);
        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            BehaviorResponse? data = null;
            try { data = JsonSerializer.Deserialize<BehaviorResponse>(json, JsonOptions); } catch { }
            if (response.IsSuccessStatusCode && data?.Ok == true)
                return new(true, data, data.Message ?? "Behavior request completed.");
            var detail = data?.Detail?.ToString();
            return new(false, data, data?.Message ?? detail ?? $"Behavior service returned HTTP {(int)response.StatusCode}.", response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Model 2 service request failed: {Path}", path);
            return new(false, null, "Behavior service is not running. Start AIProctoringService Model 2 on port 8002.", true);
        }
    }
}
