using AiExamProctoring.Services.FaceIdentity;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AiExamProctoring.Services.ObjectDetection;

public interface IObjectDetectionClient
{
    Task<ObjectDetectionCallResult> HealthAsync(CancellationToken cancellationToken = default);
    Task<ObjectDetectionCallResult> AnalyzeAsync(FaceIdentityImage image, CancellationToken cancellationToken = default);
}

public sealed class ObjectDetectionClient : IObjectDetectionClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly ObjectDetectionOptions _options;
    private readonly ILogger<ObjectDetectionClient> _logger;

    public ObjectDetectionClient(
        HttpClient http,
        IOptions<ObjectDetectionOptions> options,
        ILogger<ObjectDetectionClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public Task<ObjectDetectionCallResult> HealthAsync(CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Get, "api/model3/health", null, cancellationToken);

    public Task<ObjectDetectionCallResult> AnalyzeAsync(
        FaceIdentityImage image,
        CancellationToken cancellationToken = default)
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(image.Bytes);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(
            string.IsNullOrWhiteSpace(image.ContentType) ? "image/jpeg" : image.ContentType);
        form.Add(content, "file", string.IsNullOrWhiteSpace(image.FileName) ? "frame.jpg" : image.FileName);
        return SendAsync(HttpMethod.Post, "api/model3/analyze", form, cancellationToken);
    }

    private async Task<ObjectDetectionCallResult> SendAsync(
        HttpMethod method,
        string path,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            content?.Dispose();
            return new(false, null, "Model 3 is disabled until its trained ONNX file is installed.", true);
        }

        using var request = new HttpRequestMessage(method, path) { Content = content };
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            request.Headers.TryAddWithoutValidation("X-Object-Api-Key", _options.ApiKey);

        try
        {
            using var response = await _http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            ObjectDetectionResponse? data = null;
            try { data = JsonSerializer.Deserialize<ObjectDetectionResponse>(json, JsonOptions); } catch { }

            if (response.IsSuccessStatusCode && data?.Ok == true)
                return new(true, data, data.Message ?? "Model 3 request completed.");

            var detail = data?.Detail?.ToString();
            return new(
                false,
                data,
                data?.Message ?? detail ?? $"Model 3 returned HTTP {(int)response.StatusCode}.",
                response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable || data?.Ready == false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Model 3 service request failed: {Path}", path);
            return new(false, null, "Model 3 is not running on port 8003 or its training is still pending.", true);
        }
    }
}
