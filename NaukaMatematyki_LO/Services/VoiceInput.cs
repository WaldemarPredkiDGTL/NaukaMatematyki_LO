using System.Net.Http.Headers;
using System.Text.Json;
using NAudio.Wave;

namespace NaukaMatematyki_LO;

internal sealed class VoiceInput : IDisposable
{
    private readonly object _gate = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private WasapiRecorder? _capture;
    private WaveFileWriter? _writer;
    private MemoryStream? _recording;
    private CancellationTokenSource? _prepare;
    private bool _listening;
    private bool _transcribing;
    private bool _disposed;

    public bool IsListening => _listening;
    public event Action<string>? Recognized;
    public event Action<string>? Status;
    public event Action? StateChanged;

    public Func<string>? ApiKey { get; set; }

    public void Toggle()
    {
        if (_transcribing)
        {
            Status?.Invoke("Jeszcze spisuję poprzednie zdanie.");
            return;
        }

        if (_listening)
        {
            _listening = false;
            StateChanged?.Invoke();
            _ = StopAndTranscribeAsync();
            return;
        }

        _listening = true;
        StateChanged?.Invoke();
        _ = StartAsync();
    }

    private async Task StartAsync()
    {
        _prepare?.Cancel();
        _prepare = new CancellationTokenSource();
        var token = _prepare.Token;
        try
        {
            if (string.IsNullOrWhiteSpace(ApiKey?.Invoke()))
                throw new InvalidOperationException("Wpisz klucz xAI — mikrofon wysyła nagranie do Grok.");
            if (!_listening || token.IsCancellationRequested) return;

            var capture = new WasapiRecorderBuilder().WithSharedMode().Build();
            var recording = new MemoryStream();
            lock (_gate)
            {
                if (!_listening || _disposed)
                {
                    recording.Dispose();
                    capture.Dispose();
                    return;
                }

                _recording = recording;
                _writer = new WaveFileWriter(new LeaveOpenStream(recording), capture.WaveFormat);
                _capture = capture;
            }

            capture.DataAvailable += (buffer, _, _, _) =>
            {
                if (buffer.IsEmpty) return;
                var copy = buffer.ToArray();
                lock (_gate) _writer?.Write(copy, 0, copy.Length);
            };
            capture.StartRecording();
            if (_listening) Status?.Invoke("Słucham po polsku… kliknij Stop, żebym spisał.");
        }
        catch (OperationCanceledException)
        {
            StopListening();
        }
        catch (Exception ex)
        {
            StopListening();
            Status?.Invoke(ex.Message);
        }
    }

    private async Task StopAndTranscribeAsync()
    {
        _prepare?.Cancel();
        WasapiRecorder? capture;
        lock (_gate)
        {
            capture = _capture;
            _capture = null;
        }

        try { capture?.StopRecording(); }
        catch (Exception) { }

        byte[]? recorded;
        lock (_gate)
        {
            recorded = TakeRecording();
            capture?.Dispose();
        }

        if (recorded is null || recorded.Length < 8000)
        {
            Status?.Invoke(recorded is null ? "Mikrofon wyłączony." : "Za krótko. Powiedz pytanie i kliknij Stop.");
            return;
        }

        _transcribing = true;
        try
        {
            var apiKey = ApiKey?.Invoke();
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Wpisz klucz xAI — mikrofon wysyła nagranie do Grok.");

            Status?.Invoke("Grok spisuje wypowiedź…");
            var wav = Resample(recorded);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var spoken = await TranscribeAsync(apiKey, wav, cts.Token);
            if (string.IsNullOrWhiteSpace(spoken))
                Status?.Invoke("Nie rozpoznałem mowy. Spróbuj jeszcze raz, bliżej mikrofonu.");
            else
            {
                Recognized?.Invoke(spoken);
                Status?.Invoke("Wpisane z mikrofonu. Możesz wysłać albo poprawić.");
            }
        }
        catch (Exception ex)
        {
            Status?.Invoke(ex.Message);
        }
        finally
        {
            _transcribing = false;
        }
    }

    private async Task<string> TranscribeAsync(string apiKey, byte[] wav, CancellationToken token)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("grok-voice-transcribe-2.0"), "model");
        form.Add(new StringContent("pl"), "language");
        var file = new ByteArrayContent(wav);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(file, "file", "nagranie.wav");

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.x.ai/v1/stt");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        request.Content = form;
        using var response = await _http.SendAsync(request, token);
        var body = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(ReadError(body) ?? $"xAI zwróciło {(int)response.StatusCode}.");

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("text", out var text) ? text.GetString()?.Trim() ?? "" : "";
    }

    private static string? ReadError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var message))
                return message.GetString();
        }
        catch (Exception) { }
        return body.Length > 280 ? body[..280] : body;
    }

    private byte[]? TakeRecording()
    {
        try { _writer?.Dispose(); }
        catch (Exception) { }
        _writer = null;
        var recording = _recording;
        _recording = null;
        if (recording is null) return null;
        try
        {
            return recording.ToArray();
        }
        finally
        {
            recording.Dispose();
        }
    }

    private static byte[] Resample(byte[] source)
    {
        using var input = new MemoryStream(source, writable: false);
        using var reader = new WaveFileReader(input);
        var target = new WaveFormat(16000, 16, 1);
        var sourceFormat = reader.WaveFormat;
        if (sourceFormat.Encoding == WaveFormatEncoding.Pcm && sourceFormat.SampleRate == target.SampleRate && sourceFormat.Channels == 1 && sourceFormat.BitsPerSample == 16)
            return source;

        using var output = new MemoryStream();
        using var resampler = new MediaFoundationResampler(AsPcmOrFloat(reader), target) { ResamplerQuality = 60 };
        using (var writer = new WaveFileWriter(new LeaveOpenStream(output), target))
        {
            var buffer = new byte[target.AverageBytesPerSecond];
            int read;
            while ((read = resampler.Read(buffer.AsSpan())) > 0)
                writer.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static IWaveProvider AsPcmOrFloat(IWaveProvider source)
    {
        var format = source.WaveFormat;
        if (format.Encoding is WaveFormatEncoding.Pcm or WaveFormatEncoding.IeeeFloat)
            return source;

        var subType = format is WaveFormatExtensible extensible ? extensible.SubFormat : Guid.Empty;
        var plain = subType == IeeeFloatSubType || format.BitsPerSample == 32
            ? WaveFormat.CreateIeeeFloatWaveFormat(format.SampleRate, format.Channels)
            : new WaveFormat(format.SampleRate, Math.Max(16, format.BitsPerSample), format.Channels);
        return new WaveFormatAdapter(source, plain);
    }

    private static readonly Guid IeeeFloatSubType = new("00000003-0000-0010-8000-00AA00389B71");

    private sealed class WaveFormatAdapter(IWaveProvider source, WaveFormat format) : IWaveProvider
    {
        public WaveFormat WaveFormat { get; } = format;
        public int Read(Span<byte> buffer) => source.Read(buffer);
    }

    private sealed class LeaveOpenStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        protected override void Dispose(bool disposing) { }
    }

    private void StopListening()
    {
        if (!_listening) return;
        _listening = false;
        StateChanged?.Invoke();
    }

    public void Dispose()
    {
        _disposed = true;
        _listening = false;
        _prepare?.Cancel();
        lock (_gate)
        {
            try { _capture?.StopRecording(); }
            catch (Exception) { }
            TakeRecording();
            _capture?.Dispose();
            _capture = null;
        }
        _http.Dispose();
        _prepare?.Dispose();
    }
}
