using System.Drawing.Imaging;

namespace NaukaMatematyki_LO;

internal sealed class MainForm : Form
{
    private readonly GrokTeacherClient _teacher = new();
    private readonly VoiceInput _voice = new();
    private readonly LessonBoard _board = new();
    private readonly TextBox _input = new();
    private readonly TextBox _key = new();
    private readonly ComboBox _model = new();
    private readonly ComboBox _level = new();
    private readonly FlowLayoutPanel _topics = new();
    private readonly ListBox _history = new();
    private readonly Panel _thumbs = new();
    private readonly Label _status = new();
    private readonly Button _send = new();
    private readonly Button _mic = new();
    private readonly Button _math = new();
    private readonly Button _physics = new();
    private readonly Button _chemistry = new();
    private readonly List<AttachedImage> _images = [];
    private readonly List<StudyTurn> _historyItems = [];
    private readonly List<StudyTurn> _session = [];
    private UserSettings _settings = new();
    private CancellationTokenSource? _cts;
    private bool _busy;
    private bool _historyLoading;

    public MainForm()
    {
        Text = "Pracownia — matematyka, fizyka i chemia";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1100, 720);
        Size = new Size(1360, 860);
        BackColor = Theme.Ink;
        Font = Theme.Body;
        DoubleBuffered = true;
        AllowDrop = true;
        BuildUi();
        LoadState();
        Wire();
        ShowBoard(scrollEnd: false);
    }

    private void BuildUi()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = Theme.Sidebar, Padding = new Padding(18, 12, 18, 10) };
        var mark = new Label
        {
            Text = "Σ",
            Font = Theme.Title,
            ForeColor = Theme.Copper,
            AutoSize = true,
            Location = new Point(18, 14)
        };
        var title = new Label
        {
            Text = "Pracownia",
            Font = Theme.Title,
            ForeColor = Theme.Chalk,
            AutoSize = true,
            Location = new Point(52, 12)
        };
        var subtitle = new Label
        {
            Text = "Grok prowadzi lekcję. Ty pytasz tekstem, głosem albo zdjęciem.",
            Font = Theme.Small,
            ForeColor = Theme.Muted,
            AutoSize = true,
            Location = new Point(54, 44)
        };
        _key.UseSystemPasswordChar = true;
        _key.PlaceholderText = "Klucz API xAI";
        _key.Width = 220;
        _key.Location = new Point(520, 22);
        StyleField(_key);
        _model.Width = 180;
        _model.Location = new Point(752, 22);
        _model.FlatStyle = FlatStyle.Flat;
        _model.DropDownStyle = ComboBoxStyle.DropDown;
        _model.Items.AddRange(["grok-4", "grok-4-fast", "grok-3", "grok-2-vision-1212"]);
        StyleField(_model);
        var keyLink = new LinkLabel
        {
            Text = "console.x.ai",
            AutoSize = true,
            LinkColor = Theme.Copper,
            ActiveLinkColor = Theme.Chalk,
            Location = new Point(944, 26)
        };
        keyLink.LinkClicked += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://console.x.ai/") { UseShellExecute = true });
        header.Controls.AddRange([mark, title, subtitle, _key, _model, keyLink]);
        header.Resize += (_, _) =>
        {
            keyLink.Left = header.Width - keyLink.Width - 20;
            _model.Left = keyLink.Left - _model.Width - 12;
            _key.Left = _model.Left - _key.Width - 12;
        };

        var sidebar = new Panel { Dock = DockStyle.Left, Width = 336, BackColor = Theme.Sidebar, Padding = new Padding(14) };
        _math.Text = "Matematyka";
        _physics.Text = "Fizyka";
        _chemistry.Text = "Chemia";
        StyleChoice(_math);
        StyleChoice(_physics);
        StyleChoice(_chemistry);
        _math.Location = new Point(14, 14);
        _physics.Location = new Point(122, 14);
        _chemistry.Location = new Point(230, 14);
        _math.Width = 100;
        _physics.Width = 100;
        _chemistry.Width = 92;
        var levelLabel = new Label { Text = "POZIOM", ForeColor = Theme.Muted, Font = Theme.TinyBold, Location = new Point(16, 60), AutoSize = true };
        _level.DropDownStyle = ComboBoxStyle.DropDownList;
        _level.FlatStyle = FlatStyle.Flat;
        _level.Items.AddRange(["Podstawowy", "Rozszerzony", "Dopasuj do pytania"]);
        _level.Location = new Point(16, 80);
        _level.Width = 304;
        StyleField(_level);
        var topicLabel = new Label { Text = "TEMATY", ForeColor = Theme.Muted, Font = Theme.TinyBold, Location = new Point(16, 116), AutoSize = true };
        _topics.Location = new Point(14, 138);
        _topics.Size = new Size(308, 250);
        _topics.FlowDirection = FlowDirection.TopDown;
        _topics.WrapContents = false;
        _topics.AutoScroll = true;
        _topics.BackColor = Theme.Sidebar;
        var historyLabel = new Label { Text = "OSTATNIE LEKCJE", ForeColor = Theme.Muted, Font = Theme.TinyBold, Location = new Point(16, 398), AutoSize = true };
        _history.Location = new Point(14, 420);
        _history.Size = new Size(308, 220);
        _history.BorderStyle = BorderStyle.None;
        _history.BackColor = Theme.InkDeep;
        _history.ForeColor = Theme.Chalk;
        _history.Font = Theme.Small;
        _history.IntegralHeight = false;
        sidebar.Controls.AddRange([_math, _physics, _chemistry, levelLabel, _level, topicLabel, _topics, historyLabel, _history]);
        sidebar.Resize += (_, _) =>
        {
            _history.Height = Math.Max(80, sidebar.Height - _history.Top - 16);
            _topics.Width = sidebar.Width - 28;
            _history.Width = sidebar.Width - 28;
            _level.Width = sidebar.Width - 32;
            LayoutSubjects(sidebar.Width);
        };

        var composer = new Panel { Dock = DockStyle.Bottom, Height = 200, BackColor = Theme.Sidebar, Padding = new Padding(16, 8, 16, 12) };
        _thumbs.Dock = DockStyle.Top;
        _thumbs.Height = 0;
        _thumbs.BackColor = Theme.Sidebar;
        _input.Dock = DockStyle.Fill;
        _input.Multiline = true;
        _input.ScrollBars = ScrollBars.Vertical;
        _input.BorderStyle = BorderStyle.FixedSingle;
        _input.PlaceholderText = "Zapytaj o zadanie, wzór albo wykres. Ctrl+Enter wysyła, Ctrl+V wkleja obraz.";
        StyleField(_input);
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 78,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Theme.Sidebar,
            Padding = new Padding(0, 8, 0, 0)
        };
        _mic.Text = "Mów";
        var paste = MakeButton("Wklej obraz");
        var file = MakeButton("Z pliku");
        var clear = MakeButton("Wyczyść");
        var reset = MakeButton("Wyzeruj");
        var sample = MakeButton("Przykład");
        var fresh = MakeButton("Nowa lekcja");
        var save = MakeButton("Zapisz tablicę");
        _send.Text = "Wyślij";
        StyleChoice(_send);
        _send.BackColor = Theme.Copper;
        _send.ForeColor = Color.White;
        _send.Width = 120;
        _send.Height = 34;
        StyleChoice(_mic);
        _mic.Width = 84;
        buttons.Controls.AddRange([_mic, paste, file, clear, reset, sample, fresh, save, _send]);
        _status.Dock = DockStyle.Bottom;
        _status.Height = 22;
        _status.ForeColor = Theme.Muted;
        _status.Font = Theme.Tiny;
        _status.Text = "Klucz zostaje na tym koncie Windows.";
        composer.Controls.Add(_input);
        composer.Controls.Add(_thumbs);
        composer.Controls.Add(buttons);
        composer.Controls.Add(_status);

        _board.Dock = DockStyle.Fill;
        Controls.Add(_board);
        Controls.Add(sidebar);
        Controls.Add(composer);
        Controls.Add(header);

        paste.Click += (_, _) => PasteImage();
        file.Click += (_, _) => PickImage();
        clear.Click += (_, _) => { _input.Clear(); ClearImages(); };
        reset.Click += (_, _) => ResetBoard();
        sample.Click += (_, _) => ShowSample();
        fresh.Click += (_, _) => StartFresh();
        save.Click += (_, _) => SaveBoard();
        _send.Click += async (_, _) => await SendAsync();
        _mic.Click += (_, _) => ToggleMic();
        _math.Click += (_, _) => SetSubject("Matematyka");
        _physics.Click += (_, _) => SetSubject("Fizyka");
        _chemistry.Click += (_, _) => SetSubject("Chemia");
        _history.SelectedIndexChanged += (_, _) => OpenSelectedHistory();
        _input.KeyDown += InputKeyDown;
        DragEnter += (_, e) => { if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy; };
        DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files)
                foreach (var path in files) TryAddFile(path);
        };
    }

    private void Wire()
    {
        _voice.Recognized += text => BeginInvoke(() =>
        {
            if (_input.TextLength > 0 && !_input.Text.EndsWith(' ')) _input.AppendText(" ");
            _input.AppendText(text);
        });
        _voice.Status += text => BeginInvoke(() => _status.Text = text);
        FormClosed += (_, _) =>
        {
            _voice.Dispose();
            _teacher.Dispose();
            ClearImages();
        };
    }

    private void LoadState()
    {
        _settings = SettingsStore.Load();
        _model.Text = string.IsNullOrWhiteSpace(_settings.Model) ? "grok-4" : _settings.Model;
        _level.SelectedItem = _settings.Level;
        if (_level.SelectedIndex < 0) _level.SelectedIndex = 1;
        try { _key.Text = SettingsStore.Unprotect(_settings.ProtectedKey); }
        catch (Exception) { _key.Text = ""; }
        _historyItems.AddRange(SettingsStore.LoadHistory());
        BindHistory();
        SetSubject(string.IsNullOrWhiteSpace(_settings.Subject) ? "Matematyka" : _settings.Subject);
        _status.Text = string.IsNullOrWhiteSpace(_key.Text)
            ? "Wpisz klucz z console.x.ai — bez niego Grok nie odpowie."
            : "Klucz odczytany lokalnie. Możesz pytać.";
    }

    private void LayoutSubjects(int sidebarWidth)
    {
        var gap = 8;
        var width = Math.Max(72, (sidebarWidth - 28 - gap * 2) / 3);
        _math.SetBounds(14, 14, width, 34);
        _physics.SetBounds(14 + width + gap, 14, width, 34);
        _chemistry.SetBounds(14 + (width + gap) * 2, 14, width, 34);
    }

    private void SetSubject(string subject)
    {
        _settings.Subject = subject;
        PaintSubject(_math, subject == "Matematyka", Theme.Copper);
        PaintSubject(_physics, subject == "Fizyka", Theme.Sky);
        PaintSubject(_chemistry, subject == "Chemia", Theme.Sage);
        RebuildTopics();
    }

    private static void PaintSubject(Button button, bool selected, Color accent)
    {
        button.BackColor = selected ? accent : Theme.InkDeep;
        button.ForeColor = selected ? Color.White : Theme.Chalk;
    }

    private void RebuildTopics()
    {
        _topics.Controls.Clear();
        foreach (var (label, prompt) in TopicsFor(_settings.Subject))
        {
            var button = MakeButton(label);
            button.Width = Math.Max(220, _topics.Width - 8);
            button.Height = 32;
            button.Margin = new Padding(0, 0, 0, 6);
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Click += async (_, _) =>
            {
                _input.Text = prompt;
                await SendAsync();
            };
            _topics.Controls.Add(button);
        }
    }

    private void BindHistory()
    {
        _historyLoading = true;
        _history.Items.Clear();
        foreach (var item in _historyItems.AsEnumerable().Reverse().Take(40))
        {
            var title = item.Question.Length > 72 ? item.Question[..72] + "…" : item.Question;
            _history.Items.Add(item.At.ToString("dd.MM HH:mm") + "  " + title);
        }
        _historyLoading = false;
    }

    private void OpenSelectedHistory()
    {
        if (_historyLoading || _history.SelectedIndex < 0) return;
        var index = _historyItems.Count - 1 - _history.SelectedIndex;
        if (index < 0 || index >= _historyItems.Count) return;
        var turn = _historyItems[index];
        _session.Clear();
        _session.Add(turn);
        ShowBoard(scrollEnd: true);
    }

    private void ShowBoard(bool scrollEnd)
    {
        _board.Show(_session, includeWelcome: _session.Count == 0);
        if (scrollEnd) _board.ScrollToEnd();
    }

    private void ShowSample()
    {
        _session.Clear();
        _session.Add(new StudyTurn
        {
            Question = "Pokaż, jak wygląda tablica na przykładzie funkcji kwadratowej.",
            Subject = "Matematyka",
            Lesson = SampleLesson.Create(),
            RawLesson = ""
        });
        ShowBoard(scrollEnd: true);
    }

    private void StartFresh()
    {
        _session.Clear();
        _input.Clear();
        ClearImages();
        ShowBoard(scrollEnd: false);
        _status.Text = "Nowa lekcja. Poprzednie zostają na liście z lewej.";
    }

    private void ResetBoard()
    {
        if (_busy)
        {
            _status.Text = "Poczekaj, aż Grok skończy, albo naciśnij Stop.";
            return;
        }

        _history.ClearSelected();
        _session.Clear();
        _input.Clear();
        ClearImages();
        _board.Show([], includeWelcome: false);
        _status.Text = "Tablica wyczyszczona. Poprzednie lekcje zostają na liście z lewej.";
    }

    private async Task SendAsync()
    {
        if (_busy)
        {
            _cts?.Cancel();
            return;
        }

        var key = _key.Text.Trim();
        if (string.IsNullOrEmpty(key))
        {
            _status.Text = "Najpierw wpisz klucz API xAI.";
            _key.Focus();
            return;
        }

        var question = _input.Text.Trim();
        if (question.Length == 0 && _images.Count == 0) return;
        if (question.Length == 0) question = "Odczytaj zadanie z obrazka i wytłumacz je jak na lekcji w liceum.";

        RememberSettings(key);
        var images = _images.Select(item => item.Png).ToList();
        var thumbs = _images.Select(item => (Image)item.Thumb).ToList();
        var history = _session
            .Where(turn => turn.Lesson is not null && !string.IsNullOrWhiteSpace(turn.RawLesson))
            .SelectMany(turn => new[] { ("user", turn.Question), ("assistant", turn.Lesson!.ToContextText()) })
            .ToList();

        _busy = true;
        _send.Text = "Stop";
        _status.Text = "Grok układa lekcję…";
        _cts = new CancellationTokenSource();
        try
        {
            var raw = await _teacher.AskAsync(key, _model.Text, _settings.Subject, _level.Text, history, question, images, _cts.Token);
            var lesson = LessonDocument.Parse(raw);
            var turn = new StudyTurn
            {
                Question = question,
                Subject = _settings.Subject,
                Level = _level.Text,
                At = DateTime.Now,
                RawLesson = raw,
                Lesson = lesson,
                Images = thumbs
            };
            _session.Add(turn);
            _historyItems.Add(turn);
            SettingsStore.SaveHistory(_historyItems);
            BindHistory();
            _input.Clear();
            ClearImages(keepThumbs: true);
            ShowBoard(scrollEnd: true);
            _status.Text = lesson.Blocks.Any(block => block.Type == "graph")
                ? "Lekcja gotowa, z wykresem."
                : "Lekcja gotowa.";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Przerwane.";
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
        finally
        {
            _busy = false;
            _send.Text = "Wyślij";
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void RememberSettings(string key)
    {
        _settings.Model = string.IsNullOrWhiteSpace(_model.Text) ? "grok-4" : _model.Text.Trim();
        _settings.Level = _level.Text;
        _settings.ProtectedKey = SettingsStore.Protect(key);
        SettingsStore.Save(_settings);
    }

    private void ToggleMic()
    {
        try
        {
            _voice.Toggle();
            _mic.BackColor = _voice.IsListening ? Theme.Rose : Theme.InkDeep;
            _mic.Text = _voice.IsListening ? "Stop" : "Mów";
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
    }

    private void InputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            _ = SendAsync();
        }
        else if (e.Control && e.KeyCode == Keys.V && Clipboard.ContainsImage())
        {
            PasteImage();
            if (!Clipboard.ContainsText()) e.SuppressKeyPress = true;
        }
    }

    private void PasteImage()
    {
        if (!Clipboard.ContainsImage())
        {
            _status.Text = "W schowku nie ma obrazu.";
            return;
        }
        using var image = Clipboard.GetImage();
        if (image is not null) AddImage(image);
    }

    private void PickImage()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Obrazy|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|Wszystkie pliki|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        foreach (var path in dialog.FileNames) TryAddFile(path);
    }

    private void TryAddFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not ".png" and not ".jpg" and not ".jpeg" and not ".bmp" and not ".gif") return;
        try
        {
            using var image = Image.FromFile(path);
            AddImage(image);
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
    }

    private void AddImage(Image source)
    {
        if (_images.Count >= 4)
        {
            _status.Text = "Najwyżej 4 obrazy w jednym pytaniu.";
            return;
        }
        var fitted = Fit(source, 1400);
        using var stream = new MemoryStream();
        fitted.Save(stream, ImageFormat.Png);
        var thumb = Fit(fitted, 92);
        _images.Add(new AttachedImage(fitted, thumb, stream.ToArray()));
        var box = new PictureBox
        {
            Image = thumb,
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(72, 48),
            Margin = new Padding(0, 0, 8, 0)
        };
        if (_thumbs.Controls.Count == 0)
        {
            _thumbs.Height = 56;
            var row = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Sidebar, WrapContents = false };
            row.Controls.Add(box);
            _thumbs.Controls.Add(row);
            _thumbs.Tag = row;
        }
        else if (_thumbs.Tag is FlowLayoutPanel row)
        {
            row.Controls.Add(box);
        }
        _status.Text = "Obraz dołączony. Grok odczyta zadanie razem z pytaniem.";
    }

    private void ClearImages(bool keepThumbs = false)
    {
        foreach (var image in _images)
        {
            image.Full.Dispose();
            if (!keepThumbs) image.Thumb.Dispose();
        }
        _images.Clear();
        while (_thumbs.Controls.Count > 0)
        {
            var child = _thumbs.Controls[0];
            _thumbs.Controls.Remove(child);
            if (child is PictureBox box) box.Image = null;
            child.Dispose();
        }
        _thumbs.Tag = null;
        _thumbs.Height = 0;
    }

    private string ExportFileName()
    {
        var subject = string.IsNullOrWhiteSpace(_settings.Subject) ? "Lekcja" : _settings.Subject.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
            subject = subject.Replace(invalid, '_');
        return $"{subject}[{DateTime.Now:yyyy-MM-dd HH-mm-ss}].pdf";
    }

    private void SaveBoard()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "PDF|*.pdf|PNG|*.png",
            FileName = ExportFileName()
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            if (dialog.FileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                _board.ExportPng(dialog.FileName);
                _status.Text = "Tablica zapisana jako PNG.";
            }
            else
            {
                _board.ExportPdf(dialog.FileName);
                _status.Text = "Tablica zapisana jako PDF.";
            }
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
    }

    private static Bitmap Fit(Image source, int maxEdge)
    {
        var scale = Math.Min(1d, maxEdge / (double)Math.Max(source.Width, source.Height));
        var width = Math.Max(1, (int)(source.Width * scale));
        var height = Math.Max(1, (int)(source.Height * scale));
        var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(source, 0, 0, width, height);
        return bitmap;
    }

    private static void StyleField(Control control)
    {
        control.BackColor = Theme.InkDeep;
        control.ForeColor = Theme.Chalk;
        control.Font = Theme.Small;
        if (control is TextBox box) box.BorderStyle = BorderStyle.FixedSingle;
    }

    private static void StyleChoice(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = Theme.InkDeep;
        button.ForeColor = Theme.Chalk;
        button.Font = Theme.SmallBold;
        button.Height = 34;
        button.Cursor = Cursors.Hand;
        button.FlatAppearance.BorderColor = Theme.Line;
    }

    private static Button MakeButton(string text)
    {
        var button = new Button { Text = text, AutoSize = true, Padding = new Padding(8, 0, 8, 0) };
        StyleChoice(button);
        return button;
    }

    private static (string Label, string Prompt)[] TopicsFor(string subject) => subject switch
    {
        "Fizyka" => PhysicsTopics,
        "Chemia" => ChemistryTopics,
        _ => MathTopics
    };

    private static readonly (string Label, string Prompt)[] MathTopics =
    [
        ("Funkcja kwadratowa", "Wyjaśnij funkcję kwadratową na przykładzie f(x)=x^2-4x+3. Pokaż wykres, miejsca zerowe i wierzchołek."),
        ("Pochodna", "Pokaż, czym jest pochodna, i policz f'(x) dla f(x)=x^3-3x. Zaznacz ekstrema na wykresie."),
        ("Trygonometria", "Wytłumacz sinus i cosinus w trójkącie prostokątnym i na okręgu jednostkowym."),
        ("Ciągi", "Porównaj ciąg arytmetyczny i geometryczny. Podaj wzory i kilka pierwszych wyrazów."),
        ("Prawdopodobieństwo", "Jak liczyć prawdopodobieństwo klasyczne? Daj przykład z kostką."),
        ("Geometria analityczna", "Prosta przez A(1,2) i B(4,6). Podaj równanie i narysuj ją."),
        ("Logarytmy", "Czym różnią się log i ln? Podaj wzory oraz wykres y=ln(x)."),
        ("Nierówność", "Rozwiąż x^2-5x+6>0 i pokaż rozwiązanie na osi oraz na wykresie.")
    ];

    private static readonly (string Label, string Prompt)[] PhysicsTopics =
    [
        ("Ruch przyspieszony", "Ruch jednostajnie przyspieszony: s(t)=5t+2t^2. Narysuj zależność od czasu i policz prędkość po 3 s. We wzorze wykresu użyj x zamiast t."),
        ("II zasada dynamiki", "Wytłumacz drugą zasadę dynamiki na klocku o masie 3 kg, na który działa siła 12 N."),
        ("Energia", "Ciało 2 kg spada z 10 m. Pokaż, jak energia potencjalna zamienia się w kinetyczną."),
        ("Obwód", "Szeregowo R1=4 ohm i R2=6 ohm, U=12 V. Policz prąd i spadki napięć."),
        ("Drgania", "x(t)=0.1*sin(2*pi*t). Wykres, amplituda i okres. We wzorze wykresu użyj x jako czasu."),
        ("Soczewka", "Soczewka skupiająca: f=10 cm, przedmiot w odległości 30 cm. Policz obraz i powiększenie.")
    ];

    private static readonly (string Label, string Prompt)[] ChemistryTopics =
    [
        ("Mol", "Wytłumacz mol i masę molową na przykładzie 18 g wody. Policz liczbę moli i cząsteczek."),
        ("Stechiometria", "Ile gramów CO2 powstanie ze spalenia 16 g metanu? Ułóż równanie i policz z moli."),
        ("Stężenie", "W 200 cm3 roztworu jest 0,1 mola NaCl. Policz stężenie molowe i procentowe, jeśli gęstość to 1,02 g/cm3."),
        ("pH", "Oblicz pH roztworu, w którym [H+] = 0,001 mol/dm3. Narysuj zależność pH od x, gdzie x to wykładnik: pH = -log(10^(-x)) dla x od 1 do 6."),
        ("Równanie reakcji", "Zbilansuj spalanie etanolu i wytłumacz, co znaczą współczynniki stechiometryczne."),
        ("Wiązania", "Porównaj wiązanie jonowe, kowalencyjne i metaliczne na przykładach NaCl, H2O i miedzi."),
        ("Szybkość reakcji", "Stężenie substratu maleje jak c(t)=2*exp(-0.3*x). We wzorze wykresu użyj x jako czasu. Odczytaj stężenie po 4 jednostkach czasu."),
        ("Termochemia", "Reakcja ma ΔH = -286 kJ/mol. Wytłumacz, czy jest egzo- czy endotermiczna i co to znaczy dla ciepła.")
    ];

    private sealed record AttachedImage(Bitmap Full, Bitmap Thumb, byte[] Png);
}
