using System.Speech.Recognition;

namespace NaukaMatematyki_LO;

internal sealed class VoiceInput : IDisposable
{
    private SpeechRecognitionEngine? _engine;
    private bool _listening;

    public bool IsListening => _listening;
    public event Action<string>? Recognized;
    public event Action<string>? Status;

    public void Toggle()
    {
        if (_listening) Stop();
        else Start();
    }

    public void Start()
    {
        if (_listening) return;
        _engine ??= Create();
        _engine.SetInputToDefaultAudioDevice();
        _engine.RecognizeAsync(RecognizeMode.Multiple);
        _listening = true;
        Status?.Invoke("Słucham… mów, a potem kliknij mikrofon jeszcze raz.");
    }

    public void Stop()
    {
        if (_engine is not null && _listening)
        {
            try { _engine.RecognizeAsyncStop(); }
            catch (Exception) { }
        }
        _listening = false;
        Status?.Invoke("Mikrofon wyłączony.");
    }

    private SpeechRecognitionEngine Create()
    {
        SpeechRecognitionEngine engine;
        try
        {
            engine = new SpeechRecognitionEngine(new System.Globalization.CultureInfo("pl-PL"));
        }
        catch (Exception)
        {
            var installed = SpeechRecognitionEngine.InstalledRecognizers();
            if (installed.Count == 0)
                throw new InvalidOperationException("Windows nie ma rozpoznawania mowy. Włącz funkcję Mowa w ustawieniach systemu albo wpisz pytanie.");
            engine = new SpeechRecognitionEngine(installed[0]);
            Status?.Invoke("Brak polskiego rozpoznawania mowy — używam " + installed[0].Culture.DisplayName + ".");
        }

        engine.LoadGrammar(new DictationGrammar());
        engine.SpeechRecognized += (_, e) =>
        {
            if (e.Result.Confidence < 0.35) return;
            var text = e.Result.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(text)) Recognized?.Invoke(text);
        };
        engine.RecognizeCompleted += (_, e) =>
        {
            _listening = false;
            if (e.Error is not null) Status?.Invoke(e.Error.Message);
        };
        return engine;
    }

    public void Dispose()
    {
        if (_engine is null) return;
        try { if (_listening) _engine.RecognizeAsyncCancel(); }
        catch (Exception) { }
        _engine.Dispose();
        _engine = null;
    }
}
