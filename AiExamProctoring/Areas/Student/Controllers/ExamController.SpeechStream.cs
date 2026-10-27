using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using AiExamProctoring.Services.SpeechRecognition;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace AiExamProctoring.Areas.Student.Controllers;

public partial class ExamController
{
    private static readonly ConcurrentDictionary<string, byte> SpeechConnections = new();
    private static readonly JsonSerializerOptions StreamJson = new(JsonSerializerDefaults.Web);
    private sealed record AudioChunk(string Audio, int? QuestionId);

    [HttpGet]
    public async Task SpeechStream(int attemptId, int sessionAttemptId)
    {
        if (StudentId is null || !HttpContext.WebSockets.IsWebSocketRequest || !_speechOptions.Enabled)
        { Response.StatusCode = 400; return; }
        if (!string.Equals(Request.Headers.Origin.ToString(), $"{Request.Scheme}://{Request.Host}", StringComparison.OrdinalIgnoreCase))
        { Response.StatusCode = 403; return; }
        if (await FindActiveSessionAsync(attemptId, sessionAttemptId) is null)
        { Response.StatusCode = 403; return; }
        var key = StudentId + ":" + sessionAttemptId;
        if (!SpeechConnections.TryAdd(key, 0)) { Response.StatusCode = 409; return; }
        try
        {
            using var browser = await HttpContext.WebSockets.AcceptWebSocketAsync();
            using var service = new ClientWebSocket();
            try
            {
                using var handshake = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
                handshake.CancelAfter(TimeSpan.FromSeconds(10));
                var csrf = await ReceiveChunkAsync(browser, 4096, handshake.Token);
                Request.Headers["RequestVerificationToken"] = System.Text.Encoding.UTF8.GetString(csrf);
                await HttpContext.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(HttpContext);
                var uri = new UriBuilder(new Uri(new Uri(_speechOptions.BaseUrl), "api/model4/stream"));
                uri.Scheme = uri.Scheme == "https" ? "wss" : "ws";
                if (!string.IsNullOrWhiteSpace(_speechOptions.ApiKey)) service.Options.SetRequestHeader("X-Speech-Api-Key", _speechOptions.ApiKey);
                await service.ConnectAsync(uri.Uri, handshake.Token);
                await browser.SendAsync(System.Text.Encoding.UTF8.GetBytes("{\"ready\":true}"), WebSocketMessageType.Text, true, handshake.Token);
                using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
                var chunks = Channel.CreateBounded<(AudioChunk Packet, byte[] Wav, DateTime ReceivedAt)>(new BoundedChannelOptions(4)
                { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
                // One sender and one receiver per socket. Only the downstream task uses
                // this controller's scoped DbContext; inference cannot block microphone intake.
                async Task UploadAsync()
                {
                    try
                    {
                        while (!lifetime.IsCancellationRequested)
                        {
                            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                            timeout.CancelAfter(TimeSpan.FromSeconds(30));
                            var packet = JsonSerializer.Deserialize<AudioChunk>(await ReceiveChunkAsync(browser, 48000, timeout.Token), StreamJson);
                            if (packet is null) throw new InvalidDataException();
                            var wav = Convert.FromBase64String(packet.Audio);
                            if (wav.Length is < 44 or > 32044) throw new InvalidDataException();
                            if (!chunks.Writer.TryWrite((packet, wav, DateTime.Now))) throw new InvalidDataException("Audio backlog exceeded two seconds");
                            await service.SendAsync(wav, WebSocketMessageType.Binary, true, timeout.Token);
                        }
                    }
                    finally { chunks.Writer.TryComplete(); }
                }
                async Task DownloadAsync()
                {
                    await foreach (var chunk in chunks.Reader.ReadAllAsync(lifetime.Token))
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                        timeout.CancelAfter(TimeSpan.FromSeconds(10));
                        var raw = await ReceiveChunkAsync(service, 8192, timeout.Token);
                        var detection = JsonSerializer.Deserialize<SpeechRecognitionResponse>(raw, StreamJson);
                        if (detection?.Ok != true) throw new InvalidDataException();
                        using var bytes = new MemoryStream(chunk.Wav);
                        var request = new SpeechRecognitionRequest { AttemptId = attemptId, SessionAttemptId = sessionAttemptId,
                            QuestionId = chunk.Packet.QuestionId, Audio = new FormFile(bytes, 0, chunk.Wav.Length, "Audio", "speech.wav")
                            { Headers = new HeaderDictionary(), ContentType = "audio/wav" } };
                        // Other HTTP video requests update mouth evidence between audio packets.
                        // Do not reuse the socket-scoped EF identity map for those rows.
                        _context.ChangeTracker.Clear();
                        var result = await ProcessSpeechAsync(request, true, new(true, detection, "Streaming VAD"), chunk.ReceivedAt);
                        if (result is not JsonResult json) throw new InvalidDataException("Session is no longer available");
                        await browser.SendAsync(JsonSerializer.SerializeToUtf8Bytes(json.Value, StreamJson), WebSocketMessageType.Text, true, timeout.Token);
                    }
                }
                var upload = UploadAsync(); var download = DownloadAsync();
                await Task.WhenAny(upload, download);
                lifetime.Cancel();
                await Task.WhenAll(upload, download);
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or InvalidDataException or FormatException or JsonException or AntiforgeryValidationException)
            {
                HttpContext.RequestServices.GetRequiredService<ILogger<ExamController>>().LogWarning("Speech stream stopped: {Type}", ex.GetType().Name);
                if (browser.State == WebSocketState.Open)
                    await browser.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, "Speech stream reconnect required", CancellationToken.None);
            }
        }
        finally { SpeechConnections.TryRemove(key, out _); }
    }

    private static async Task<byte[]> ReceiveChunkAsync(WebSocket socket, int limit, CancellationToken token)
    {
        using var message = new MemoryStream();
        var buffer = new byte[4096];
        WebSocketReceiveResult part;
        do
        {
            part = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
            if (part.MessageType == WebSocketMessageType.Close) throw new WebSocketException("Closed");
            if (message.Length + part.Count > limit) throw new InvalidDataException("Message too large");
            message.Write(buffer, 0, part.Count);
        } while (!part.EndOfMessage);
        return message.ToArray();
    }
}
