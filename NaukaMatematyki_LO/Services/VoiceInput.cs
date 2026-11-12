using System.Text;
using NAudio.Wave;
using Whisper.net;
using Whisper.net.Ggml;

namespace NaukaMatematyki_LO;

internal sealed class VoiceInput : IDisposable
{
    private readonly object _gate = new();
    private WasapiRecorder? _capture;
    private WaveFileWriter? _writer;
    private MemoryStream? _recording;
    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private CancellationTokenSource? _prepare;
    private bool _listening;
    private bool _transcribing;
    private bool _disposed;

    public bool IsListening => _listening;
    public event Action<string>? Recognized;
    public event Action<string>? Status;
    public event Action? StateChanged;

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
            await EnsureModelAsync(token);
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
            Status?.Invoke("Spisuję wypowiedź…");
            var wav = Resample(recorded);
            if (wav.Length < 8000)
            {
                Status?.Invoke("Za krótko. Powiedz pytanie i kliknij Stop.");
                return;
            }

            EnsureProcessor();
            await using var stream = new MemoryStream(wav);
            var text = new StringBuilder();
            await foreach (var segment in _processor!.ProcessAsync(stream))
            {
                var piece = segment.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(piece)) text.Append(piece).Append(' ');
            }

            var spoken = text.ToString().Trim();
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

    private async Task<string> EnsureModelAsync(CancellationToken token)
    {
        var path = ModelPath();
        if (File.Exists(path) && new FileInfo(path).Length > 1_000_000) return path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Status?.Invoke("Pobieram polski model mowy, około 150 MB. To tylko raz.");
        var part = path + ".part";
        await using (var model = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(GgmlType.Base, QuantizationType.NoQuantization, token))
        await using (var file = File.Create(part))
            await model.CopyToAsync(file, token);
        File.Move(part, path, overwrite: true);
        Status?.Invoke("Model gotowy. Słucham…");
        return path;
    }

    private void EnsureProcessor()
    {
        if (_processor is not null) return;
        var threads = Math.Clamp(Environment.ProcessorCount, 2, 8);
        _factory ??= WhisperFactory.FromPath(ModelPath());
        _processor = _factory.CreateBuilder()
            .WithLanguage("pl")
            .WithThreads(threads)
            .WithNoContext()
            .WithSingleSegment()
            .WithTemperature(0)
            .WithPrompt("Pytanie z matematyki, fizyki albo chemii po polsku. Wielomiany, równanie, funkcja, wzór, jednostka.")
            .Build();
    }

    private static string ModelPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NaukaMatematyki_LO",
        "ggml-base.bin");

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
        _processor?.Dispose();
        _processor = null;
        _factory?.Dispose();
        _factory = null;
        _prepare?.Dispose();
    }
}
