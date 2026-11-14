namespace NaukaMatematyki_LO;

internal sealed class LessonPreview : Form
{
    private readonly LessonBoard _board = new();
    private readonly Label _status = new();
    private readonly Button _again = new();
    private readonly Button _keep = new();
    private readonly string _shown;
    private readonly string _subject;
    private readonly string _level;
    private readonly Func<CancellationToken, Task<string>> _compose;
    private CancellationTokenSource? _cts;
    private string? _raw;
    private bool _busy;
    private bool _closing;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string? Lesson { get; private set; }

    public LessonPreview(string title, string shown, string subject, string level, string? existing, Func<CancellationToken, Task<string>> compose)
    {
        _shown = shown;
        _subject = subject;
        _level = level;
        _compose = compose;
        _raw = string.IsNullOrWhiteSpace(existing) ? null : existing.Trim();

        Text = string.IsNullOrWhiteSpace(title) ? "Podgląd lekcji" : "Podgląd lekcji — " + title;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(960, 640);
        Size = new Size(1180, 780);
        BackColor = Theme.Ink;
        Font = Theme.Body;
        ForeColor = Theme.Chalk;

        var bar = new Panel { Dock = DockStyle.Bottom, Height = 108, BackColor = Theme.Sidebar, Padding = new Padding(16, 8, 16, 12) };
        _status.Dock = DockStyle.Top;
        _status.Height = 44;
        _status.AutoSize = false;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.Padding = new Padding(8, 4, 8, 0);
        _status.ForeColor = Theme.Muted;
        _status.Font = Theme.Small;
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = Theme.Sidebar,
            Padding = new Padding(0, 8, 0, 0)
        };
        _keep.Text = "Zostaw w zagadnieniu";
        Style(_keep);
        _keep.Width = 200;
        _keep.BackColor = Theme.Copper;
        _keep.ForeColor = Color.White;
        _keep.Click += (_, _) => Keep();
        _again.Text = "Ułóż jeszcze raz";
        Style(_again);
        _again.Width = 160;
        _again.Click += async (_, _) =>
        {
            if (_busy) _cts?.Cancel();
            else await GenerateAsync();
        };
        var close = new Button { Text = "Zamknij" };
        Style(close);
        close.Width = 110;
        close.Click += (_, _) => Close();
        buttons.Controls.AddRange([_keep, _again, close]);
        bar.Controls.Add(_status);
        bar.Controls.Add(buttons);
        _board.Dock = DockStyle.Fill;
        Controls.Add(_board);
        Controls.Add(bar);
        Shown += async (_, _) =>
        {
            if (Owner?.Icon != null) Icon = Owner.Icon;
            if (_raw is null) await GenerateAsync();
            else ShowRaw(_raw, "Taką lekcję zobaczy uczeń. Dalej może iść po swojemu.");
        };
        FormClosing += (_, _) =>
        {
            _closing = true;
            _cts?.Cancel();
        };
    }

    private void Keep()
    {
        if (_busy || string.IsNullOrWhiteSpace(_raw)) return;
        Lesson = _raw;
        DialogResult = DialogResult.OK;
        Close();
    }

    private async Task GenerateAsync()
    {
        if (_busy) return;
        _busy = true;
        _keep.Enabled = false;
        _again.Text = "Stop";
        _status.Height = 52;
        _status.BackColor = Theme.Copper;
        _status.ForeColor = Color.White;
        _status.Font = Theme.SmallBold;
        _status.Text = "Grok opracowuje lekcję do tego zagadnienia.\nTo zwykle trwa minutę albo dwie. Nie zamykaj okna. Stop przerywa.";
        _board.Show([], includeWelcome: false, working: _shown);
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        try
        {
            var raw = await _compose(_cts.Token);
            if (_closing || IsDisposed) return;
            _raw = raw.Trim();
            ShowRaw(_raw, "Obejrzyj lekcję. Zostaw ją w zagadnieniu albo ułóż jeszcze raz.");
        }
        catch (OperationCanceledException)
        {
            if (_closing || IsDisposed) return;
            if (_raw is null) _board.Show([], includeWelcome: false);
            else ShowRaw(_raw, "Przerwane. Została poprzednia lekcja.");
            PaintIdle("Przerwane.");
        }
        catch (Exception ex)
        {
            if (_closing || IsDisposed) return;
            AppLog.Error("Podgląd", ex);
            if (_raw is null) _board.Show([], includeWelcome: false);
            else ShowRaw(_raw, "Została poprzednia lekcja.");
            PaintIdle(ex.Message);
        }
        finally
        {
            _busy = false;
            if (!_closing && !IsDisposed)
            {
                _again.Text = "Ułóż jeszcze raz";
                _keep.Enabled = !string.IsNullOrWhiteSpace(_raw);
            }
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void ShowRaw(string raw, string status)
    {
        var turn = new StudyTurn
        {
            Question = _shown,
            Subject = string.IsNullOrWhiteSpace(_subject) ? "Matematyka" : _subject,
            Level = _level,
            At = DateTime.Now,
            RawLesson = raw,
            Prepared = true,
            Lesson = LessonDocument.Parse(raw)
        };
        _board.Show([turn], includeWelcome: false);
        _board.ScrollToEnd();
        PaintIdle(status);
        _keep.Enabled = true;
    }

    private void PaintIdle(string text)
    {
        _status.Height = 44;
        _status.BackColor = Theme.Sidebar;
        _status.ForeColor = Theme.Chalk;
        _status.Font = Theme.Small;
        _status.Text = text;
    }

    private static void Style(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = Theme.InkDeep;
        button.ForeColor = Theme.Chalk;
        button.Font = Theme.SmallBold;
        button.Height = 34;
        button.Cursor = Cursors.Hand;
        button.FlatAppearance.BorderColor = Theme.Line;
    }
}
