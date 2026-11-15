using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace AiExamProctoring.Services.SpeechRecognition;

public interface ISpeechRecognitionClient
{
    Task<SpeechRecognitionCallResult> HealthAsync(CancellationToken cancellationToken = default);
    Task<SpeechRecognitionCallResult> TranscribeAsync(SpeechAudio audio, CancellationToken cancellationToken = default);
}

public sealed class SpeechRecognitionClient : ISpeechRecognitionClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly SpeechRecognitionOptions _options;
    private readonly ILogger<SpeechRecognitionClient> _logger;

    public SpeechRecognitionClient(
        HttpClient http,
        IOptions<SpeechRecognitionOptions> options,
        ILogger<SpeechRecognitionClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public Task<SpeechRecognitionCallResult> HealthAsync(CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Get, "api/model4/health", null, cancellationToken);

    public async Task<SpeechRecognitionCallResult> TranscribeAsync(
        SpeechAudio audio,
        CancellationToken cancellationToken = default)
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(audio.Bytes);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(
            string.IsNullOrWhiteSpace(audio.ContentType) ? "audio/webm" : audio.ContentType);
        form.Add(content, "file", string.IsNullOrWhiteSpace(audio.FileName) ? "speech.webm" : audio.FileName);
        var speech = await SendAsync(HttpMethod.Post, audio.DetectionOnly ? "api/model4/detect" : "api/model4/transcribe", form, cancellationToken);
        if (audio.DetectionOnly || !speech.Success || speech.Data is not { } response ||
            audio.Contexts.Count == 0 || !SpeechEvidencePolicy.Intelligible(response, _options.MinimumTranscriptConfidence))
            return speech;

        if (!Uri.TryCreate(_options.ContextBaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var contextUri) ||
            contextUri.Scheme is not ("http" or "https") || (!contextUri.IsLoopback && !_options.AllowRemoteQuestionContext))
            return speech;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var payload = JsonContent.Create(new
        {
            transcript = response.Transcript[..Math.Min(response.Transcript.Length, 4000)],
            contexts = audio.Contexts
        }, options: JsonOptions);
        var context = await SendAsync(HttpMethod.Post, new Uri(contextUri, "api/model5/analyze").AbsoluteUri, payload, timeout.Token);
        cancellationToken.ThrowIfCancellationRequested();
        response.ContextAnalysis = context.Success ? context.Data?.ContextAnalysis : null;
        return speech;
    }

    private async Task<SpeechRecognitionCallResult> SendAsync(
        HttpMethod method,
        string path,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            content?.Dispose();
            return new(false, null, "Model 4 speech recognition is disabled.", true);
        }

        using var request = new HttpRequestMessage(method, path) { Content = content };
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            request.Headers.TryAddWithoutValidation("X-Speech-Api-Key", _options.ApiKey);

        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            SpeechRecognitionResponse? data = null;
            try { data = JsonSerializer.Deserialize<SpeechRecognitionResponse>(json, JsonOptions); } catch { }

            if (response.IsSuccessStatusCode && data?.Ok == true)
                return new(true, data, data.Message ?? "Speech service request completed.");

            return new(
                false,
                data,
                data?.Message ?? data?.Detail?.ToString() ?? $"Speech service returned HTTP {(int)response.StatusCode}.",
                response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable || data?.Ready == false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Speech service request failed: {Path}", path);
            return new(false, null, "The selected speech service is unavailable.", true);
        }
    }
}
