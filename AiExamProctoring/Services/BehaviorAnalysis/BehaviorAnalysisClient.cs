
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AiExamProctoring.Services.FaceIdentity;
using Microsoft.Extensions.Options;

namespace AiExamProctoring.Services.BehaviorAnalysis;

public interface IBehaviorAnalysisClient
{
    Task<BehaviorCallResult> StartSessionAsync(string key, IReadOnlyList<FaceIdentityImage> frames,
        CancellationToken ct, bool screenCalibration = false, int screenPoints = 4);
    Task<BehaviorCallResult> AnalyzeAsync(string key, FaceIdentityImage frame, CancellationToken ct);
    Task<BehaviorCallResult> AnalyzeFeatureAsync(string feature, string key, FaceIdentityImage frame, CancellationToken ct = default);
    Task<BehaviorCallResult> HealthAsync(CancellationToken ct = default);
    Task<BehaviorCallResult> EndSessionAsync(string key, CancellationToken ct);
}

public sealed class BehaviorAnalysisClient(HttpClient client, IOptions<BehaviorAnalysisOptions> options,
    ILogger<BehaviorAnalysisClient> logger)
    : IBehaviorAnalysisClient
{
    private readonly BehaviorAnalysisOptions _settings = options.Value;

    public Task<BehaviorCallResult> StartSessionAsync(string key, IReadOnlyList<FaceIdentityImage> frames,
        CancellationToken ct, bool screenCalibration = false, int screenPoints = 4) =>
        SendAsync(_settings.BaseUrl, "sessions/start", key, ct, form =>
        {
            form.Add(new StringContent(screenCalibration ? "true" : "false"), "screen_calibration");
            form.Add(new StringContent(screenPoints.ToString(System.Globalization.CultureInfo.InvariantCulture)), "screen_points");
            foreach (var frame in frames)
                AddFrame(form, "files", frame);
        }, timeoutMilliseconds: 18000);

    public Task<BehaviorCallResult> AnalyzeAsync(string key, FaceIdentityImage frame, CancellationToken ct) =>
        AnalyzeFeatureAsync("attention", key, frame, ct);

    public Task<BehaviorCallResult> AnalyzeFeatureAsync(string feature, string key, FaceIdentityImage frame, CancellationToken ct = default)
    {
        var (address, timeout) = feature switch
        {
            "absence" => (_settings.AbsenceBaseUrl, _settings.AbsenceTimeoutMilliseconds),
            "persons" => (_settings.MultiplePeopleBaseUrl, _settings.MultiplePeopleTimeoutMilliseconds),
            _ => (_settings.BaseUrl, _settings.AttentionTimeoutMilliseconds)
        };
        return SendAsync(address, "analyze", key, ct, form => AddFrame(form, "file", frame), timeout);
    }

    public Task<BehaviorCallResult> EndSessionAsync(string key, CancellationToken ct) =>
        SendAsync(_settings.BaseUrl, "sessions/end", key, ct, timeoutMilliseconds: _settings.AttentionTimeoutMilliseconds);

    public Task<BehaviorCallResult> HealthAsync(CancellationToken ct = default) =>
        SendAsync(_settings.BaseUrl, "health", null, ct, timeoutMilliseconds: _settings.AttentionTimeoutMilliseconds);

    private static void AddFrame(MultipartFormDataContent form, string field, FaceIdentityImage frame)
    {
        var image = new ByteArrayContent(frame.Bytes);
        image.Headers.ContentType = new MediaTypeHeaderValue(frame.ContentType);
        form.Add(image, field, frame.FileName);
    }

    private async Task<BehaviorCallResult> SendAsync(string address, string endpoint, string? key, CancellationToken ct,
        Action<MultipartFormDataContent>? fill = null, int? timeoutMilliseconds = null)
    {
        if (!_settings.Enabled || !Uri.TryCreate(address.TrimEnd('/') + "/", UriKind.Absolute, out var root)
            || root.Scheme is not ("http" or "https"))
            return new(false, null, "Monitoring service is unavailable.", true);

        using var request = new HttpRequestMessage(key is null ? HttpMethod.Get : HttpMethod.Post,
            new Uri(root, "api/model2/" + endpoint));
        if (key is not null)
        {
            var form = new MultipartFormDataContent();
            request.Content = form;
            form.Add(new StringContent(key), "session_key");
            fill?.Invoke(form);
        }
        if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
            request.Headers.Add("X-Behavior-Api-Key", _settings.ApiKey);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeoutMilliseconds is int ms)
            timeout.CancelAfter(TimeSpan.FromMilliseconds(Math.Clamp(ms, 200, 45_000)));
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests
                || (int)response.StatusCode >= 500)
                return new(false, null, "Monitoring service is temporarily unavailable. Retrying.", true);
            var data = await response.Content.ReadFromJsonAsync<BehaviorResponse>(cancellationToken: timeout.Token);
            var success = response.IsSuccessStatusCode && data?.Ok == true;
            return new(success, data, success ? "" : data?.Message ?? "The monitoring service could not accept this frame.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(false, null, "The monitoring service took too long to respond. Retrying.", true);
        }
        catch (HttpRequestException error)
        {
            logger.LogDebug(error, "Behavior worker request failed for {Endpoint}", endpoint);
            return new(false, null, "Monitoring service is unavailable.", true);
        }
        catch (System.Text.Json.JsonException)
        {
            return new(false, null, "The monitoring service returned an unreadable response.", true);
        }
    }
}
