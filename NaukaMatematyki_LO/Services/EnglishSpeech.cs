using NAudio.Wave;

namespace NaukaMatematyki_LO;

internal sealed class EnglishSpeech : IDisposable
{
    private readonly object _gate = new();
    private WaveOut? _wave;

    public string? Current { get; private set; }
    public bool IsPlaying { get; private set; }

    public async Task PlayAsync(byte[] mp3, string text)
    {
        Stop();
        var path = Path.Combine(Path.GetTempPath(), "pracownia-en-" + Guid.NewGuid().ToString("N") + ".mp3");
        await File.WriteAllBytesAsync(path, mp3);
        var reader = new MediaFoundationReader(path);
        var wave = new WaveOut();
        wave.PlaybackStopped += (_, _) =>
        {
            lock (_gate)
            {
                if (ReferenceEquals(_wave, wave))
                {
                    _wave = null;
                    IsPlaying = false;
                    Current = null;
                }
            }
            try { wave.Dispose(); } catch (Exception) { }
            try { reader.Dispose(); } catch (Exception) { }
            try { File.Delete(path); } catch (Exception) { }
        };
        wave.Init(reader);
        lock (_gate)
        {
            _wave = wave;
            Current = text;
            IsPlaying = true;
        }
        wave.Play();
    }

    public void Stop()
    {
        WaveOut? wave;
        lock (_gate)
        {
            wave = _wave;
            IsPlaying = false;
            Current = null;
        }
        try { wave?.Stop(); } catch (Exception) { }
    }

    public void Dispose() => Stop();
}
