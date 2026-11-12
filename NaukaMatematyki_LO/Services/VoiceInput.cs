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
    private FileStream? _recordStream;
    private WhisperFactory? _factory;
    private CancellationTokenSource? _prepare;
    private string? _rawPath;
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

            var raw = Path.Combine(Path.GetTempPath(), "pracownia-mic-" + Guid.NewGuid().ToString("N") + ".wav");
            var capture = new WasapiRecorderBuilder().WithSharedMode().Build();
            var file = File.Create(raw);
            lock (_gate)
            {
                if (!_listening || _disposed)
                {
                    file.Dispose();
                    capture.Dispose();
                    return;
                }

                _recordStream = file;
                _writer = new WaveFileWriter(file, capture.WaveFormat);
                _capture = capture;
                _rawPath = raw;
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
        string? raw;
        WasapiRecorder? capture;
        lock (_gate)
        {
            raw = _rawPath;
            _rawPath = null;
            capture = _capture;
            _capture = null;
        }

        try { capture?.StopRecording(); }
        catch (Exception) { }

        lock (_gate)
        {
            CloseRecording();
            capture?.Dispose();
        }

        if (string.IsNullOrWhiteSpace(raw) || !File.Exists(raw))
        {
            Status?.Invoke("Mikrofon wyłączony.");
            return;
        }

        _transcribing = true;
        var wav = Path.Combine(Path.GetTempPath(), "pracownia-mic-16k-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            Status?.Invoke("Spisuję wypowiedź…");
            Resample(raw, wav);
            if (new FileInfo(wav).Length < 8000)
            {
                Status?.Invoke("Za krótko. Powiedz pytanie i kliknij Stop.");
                return;
            }

            var model = ModelPath();
            _factory ??= WhisperFactory.FromPath(model);
            using var processor = _factory.CreateBuilder().WithLanguage("pl").Build();
            await using var stream = File.OpenRead(wav);
            var text = new StringBuilder();
            await foreach (var segment in processor.ProcessAsync(stream))
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
            TryDelete(raw);
            TryDelete(wav);
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

    private static string ModelPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NaukaMatematyki_LO",
        "ggml-base.bin");

    private void CloseRecording()
    {
        try { _writer?.Dispose(); }
        catch (Exception) { }
        try { _recordStream?.Dispose(); }
        catch (Exception) { }
        _writer = null;
        _recordStream = null;
    }

    private static void Resample(string source, string target)
    {
        using var input = new MemoryStream(ReadReleased(source));
        using var reader = new WaveFileReader(input);
        var format = new WaveFormat(16000, 16, 1);
        if (reader.WaveFormat.SampleRate == format.SampleRate && reader.WaveFormat.Channels == 1 && reader.WaveFormat.BitsPerSample == 16)
        {
            File.WriteAllBytes(target, input.ToArray());
            return;
        }

        using var output = new MemoryStream();
        using var resampler = new MediaFoundationResampler(reader, format) { ResamplerQuality = 60 };
        using (var writer = new WaveFileWriter(new LeaveOpenStream(output), format))
        {
            var buffer = new byte[format.AverageBytesPerSecond];
            int read;
            while ((read = resampler.Read(buffer.AsSpan())) > 0)
                writer.Write(buffer, 0, read);
        }
        File.WriteAllBytes(target, output.ToArray());
    }

    private static byte[] ReadReleased(string path)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                return memory.ToArray();
            }
            catch (IOException) when (attempt < 7)
            {
                Thread.Sleep(40);
            }
        }

        throw new IOException("Nie mogę odczytać nagrania, plik WAV jest jeszcze zajęty. Spróbuj powiedzieć pytanie jeszcze raz.");
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

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception) { }
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
            CloseRecording();
            _capture?.Dispose();
            _capture = null;
        }
        _factory?.Dispose();
        _factory = null;
        _prepare?.Dispose();
    }
}
