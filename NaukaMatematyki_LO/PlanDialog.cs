namespace NaukaMatematyki_LO;

internal sealed class PlanDialog : Form
{
    private readonly List<StudyPlanItem> _items = [];
    private readonly TextBox _link = new();
    private readonly TextBox _title = new();
    private readonly TextBox _author = new();
    private readonly TextBox _note = new();
    private readonly TextBox _itemTitle = new();
    private readonly TextBox _itemPrompt = new();
    private readonly ComboBox _subject = new();
    private readonly ComboBox _level = new();
    private readonly ListBox _list = new();
    private readonly Label _hint = new();
    private readonly Label _lessonState = new();
    private readonly Button _loadLink = new();
    private readonly Button _lessonButton = new();
    private int _index = -1;
    private bool _loading;

    public StudyPlan? Plan { get; private set; }
    public bool Cleared { get; private set; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool CanCompose { get; set; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<string, string, string, CancellationToken, Task<string>>? Compose { get; set; }

    public PlanDialog(StudyPlan? current, string subject, string level)
    {
        Text = "Plan zagadnień";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(820, 640);
        BackColor = Theme.Sidebar;
        Font = Theme.Body;
        ForeColor = Theme.Chalk;

        var linkLabel = LabelAt("LINK Z DYSKU GOOGLE", 20, 16);
        _link.AccessibleName = "Link";
        _link.PlaceholderText = "Wklej link. Działa u ucznia i u kolegi.";
        _link.SetBounds(20, 36, 640, 28);
        StyleField(_link);
        _loadLink.Text = "Wczytaj";
        StyleChoice(_loadLink);
        _loadLink.SetBounds(670, 34, 130, 32);
        _loadLink.Click += async (_, _) => await LoadLinkAsync();

        var titleLabel = LabelAt("TYTUŁ PLANU", 20, 76);
        _title.AccessibleName = "Tytuł planu";
        _title.SetBounds(20, 96, 390, 28);
        StyleField(_title);
        var authorLabel = LabelAt("AUTOR", 424, 76);
        _author.PlaceholderText = "Imię, klasa albo nick";
        _author.SetBounds(424, 96, 376, 28);
        StyleField(_author);

        var subjectLabel = LabelAt("PRZEDMIOT", 20, 136);
        _subject.DropDownStyle = ComboBoxStyle.DropDownList;
        _subject.SetBounds(20, 156, 250, 28);
        _subject.Items.AddRange(["Matematyka", "Fizyka", "Chemia", "Język polski", "Język angielski"]);
        StyleField(_subject);
        var levelLabel = LabelAt("POZIOM", 284, 136);
        _level.DropDownStyle = ComboBoxStyle.DropDownList;
        _level.SetBounds(284, 156, 220, 28);
        _level.Items.AddRange(["Podstawowy", "Rozszerzony", "Dopasuj do pytania"]);
        StyleField(_level);
        var noteLabel = LabelAt("NOTATKA DO CAŁEGO PLANU", 518, 136);
        _note.PlaceholderText = "Na przykład: róbcie po kolei";
        _note.SetBounds(518, 156, 282, 28);
        StyleField(_note);

        var listLabel = LabelAt("ZAGADNIENIA", 20, 198);
        _list.SetBounds(20, 218, 250, 320);
        _list.BackColor = Theme.InkDeep;
        _list.ForeColor = Theme.Chalk;
        _list.BorderStyle = BorderStyle.FixedSingle;
        _list.Font = Theme.Small;
        _list.IntegralHeight = false;
        _list.SelectedIndexChanged += (_, _) => ShowSelected();

        var itemLabel = LabelAt("TYTUŁ ZAGADNIENIA", 286, 198);
        _itemTitle.SetBounds(286, 218, 514, 28);
        StyleField(_itemTitle);
        var promptLabel = LabelAt("POLECENIE DLA UCZNIA", 286, 254);
        _itemPrompt.AccessibleName = "Polecenie";
        _itemPrompt.Multiline = true;
        _itemPrompt.ScrollBars = ScrollBars.Vertical;
        _itemPrompt.PlaceholderText = "Co ma przerobić. Na przykład: wytłumacz miejsca zerowe f(x)=x^2-5x+6 i daj jedno zadanie.";
        _itemPrompt.SetBounds(286, 274, 514, 156);
        StyleField(_itemPrompt);
        _lessonState.Font = Theme.Small;
        _lessonState.ForeColor = Theme.Muted;
        _lessonState.SetBounds(286, 436, 514, 36);
        _lessonButton.Text = "Ułóż lekcję";
        StyleChoice(_lessonButton);
        _lessonButton.SetBounds(654, 476, 146, 34);
        _lessonButton.Click += (_, _) => PrepareLesson();

        var add = SmallButton("Dodaj", 286, 476);
        var remove = SmallButton("Usuń", 378, 476);
        var up = SmallButton("Wyżej", 470, 476);
        var down = SmallButton("Niżej", 562, 476);
        add.Width = 88;
        remove.Width = 88;
        up.Width = 88;
        down.Width = 88;
        add.Click += (_, _) => AddItem();
        remove.Click += (_, _) => RemoveItem();
        up.Click += (_, _) => MoveItem(-1);
        down.Click += (_, _) => MoveItem(1);

        _hint.Text = "Ułóż lekcję, obejrzyj ją i zapisz plik. Uczeń zobaczy ten układ i może iść dalej po swojemu. Link z Dysku wysyłasz Messengerem.";
        _hint.Font = Theme.Small;
        _hint.ForeColor = Theme.Muted;
        _hint.SetBounds(20, 548, 780, 36);

        var open = SmallButton("Otwórz plik", 20, 592);
        var save = SmallButton("Zapisz plik", 140, 592);
        var clear = SmallButton("Wyczyść", 260, 592);
        var use = SmallButton("Użyj planu", 668, 592);
        use.BackColor = Theme.Copper;
        use.ForeColor = Color.White;
        open.Click += (_, _) => OpenFile();
        save.Click += (_, _) => SaveFile();
        clear.Click += (_, _) => ClearPlan();
        use.Click += (_, _) => UsePlan();

        Controls.AddRange([
            linkLabel, _link, _loadLink,
            titleLabel, _title, authorLabel, _author,
            subjectLabel, _subject, levelLabel, _level, noteLabel, _note,
            listLabel, _list, itemLabel, _itemTitle, promptLabel, _itemPrompt, _lessonState,
            add, remove, up, down, _lessonButton, _hint, open, save, clear, use
        ]);

        var source = current ?? new StudyPlan { Subject = subject, Level = level, Title = "" };
        LoadPlan(source.CopySafe(subject, level));
    }

    private async Task LoadLinkAsync()
    {
        _loadLink.Enabled = false;
        _hint.Text = "Pobieram plan z Dysku…";
        try
        {
            var plan = await DrivePlan.DownloadAsync(_link.Text, CancellationToken.None);
            Plan = plan;
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            AppLog.Error("Plan", ex);
            _hint.Text = ex.Message;
            _loadLink.Enabled = true;
        }
    }

    private void OpenFile()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Plan Pracowni (*.pracownia.json;*.json)|*.pracownia.json;*.json",
            Title = "Otwórz plan zagadnień"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            LoadPlan(StudyPlan.Parse(File.ReadAllText(dialog.FileName)));
            _hint.Text = "Plik wczytany. Kliknij „Użyj planu”, albo popraw go i zapisz jeszcze raz.";
        }
        catch (Exception ex)
        {
            AppLog.Error("Plan", ex);
            _hint.Text = ex.Message;
        }
    }

    private void SaveFile()
    {
        var plan = Collect();
        if (plan == null) return;
        var name = string.Join("-", plan.Title.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (name.Length == 0) name = "plan";
        using var dialog = new SaveFileDialog
        {
            Filter = "Plan Pracowni (*.pracownia.json)|*.pracownia.json",
            FileName = name + ".pracownia.json",
            Title = "Zapisz plan na dysk"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        File.WriteAllText(dialog.FileName, plan.ToJson());
        _hint.Text = "Zapisane. Wrzuć ten plik na Dysk Google, ustaw „Każdy, kto ma link” i wyślij link.";
    }

    private void UsePlan()
    {
        var plan = Collect();
        if (plan == null) return;
        Plan = plan;
        DialogResult = DialogResult.OK;
    }

    private void ClearPlan()
    {
        Cleared = true;
        Plan = null;
        DialogResult = DialogResult.OK;
    }

    private void LoadPlan(StudyPlan plan)
    {
        _loading = true;
        _title.Text = plan.Title;
        _author.Text = plan.Author;
        _note.Text = plan.Note;
        SelectCombo(_subject, plan.Subject);
        SelectCombo(_level, plan.Level);
        _items.Clear();
        _items.AddRange(plan.Items.Select(item => new StudyPlanItem { Title = item.Title, Prompt = item.Prompt, Lesson = item.Lesson }));
        if (_items.Count == 0) _items.Add(new StudyPlanItem { Title = "Zagadnienie 1" });
        _list.Items.Clear();
        foreach (var item in _items) _list.Items.Add(ItemLabel(item));
        _index = 0;
        _list.SelectedIndex = 0;
        _itemTitle.Text = _items[0].Title;
        _itemPrompt.Text = _items[0].Prompt;
        _loading = false;
        RefreshLessonChrome();
    }

    private StudyPlan? Collect()
    {
        FlushItem();
        var plan = new StudyPlan
        {
            Title = _title.Text.Trim(),
            Author = _author.Text.Trim(),
            Note = _note.Text.Trim(),
            Subject = _subject.Text,
            Level = _level.Text,
            Items = _items
                .Select(item => new StudyPlanItem { Title = item.Title.Trim(), Prompt = item.Prompt.Trim(), Lesson = item.Lesson })
                .Where(item => item.Prompt.Length > 0)
                .ToList()
        };
        if (plan.Title.Length == 0)
        {
            _hint.Text = "Wpisz tytuł planu.";
            return null;
        }
        if (plan.Items.Count == 0)
        {
            _hint.Text = "Dopisz polecenie przy choć jednym zagadnieniu.";
            return null;
        }
        return StudyPlan.Parse(plan.ToJson());
    }

    private void AddItem()
    {
        FlushItem();
        _items.Add(new StudyPlanItem { Title = "Zagadnienie " + (_items.Count + 1) });
        ReloadList(_items.Count - 1);
    }

    private void RemoveItem()
    {
        if (_index < 0 || _items.Count == 0) return;
        _items.RemoveAt(_index);
        if (_items.Count == 0) _items.Add(new StudyPlanItem { Title = "Zagadnienie 1" });
        ReloadList(Math.Min(_index, _items.Count - 1));
    }

    private void MoveItem(int delta)
    {
        FlushItem();
        var next = _index + delta;
        if (_index < 0 || next < 0 || next >= _items.Count) return;
        (_items[_index], _items[next]) = (_items[next], _items[_index]);
        ReloadList(next);
    }

    private void ShowSelected()
    {
        if (_loading) return;
        var next = _list.SelectedIndex;
        if (next < 0 || next == _index) return;
        FlushItem();
        _index = next;
        _loading = true;
        _itemTitle.Text = _items[next].Title;
        _itemPrompt.Text = _items[next].Prompt;
        _loading = false;
        RefreshLessonChrome();
    }

    private void FlushItem()
    {
        if (_index < 0 || _index >= _items.Count) return;
        var title = _itemTitle.Text.Trim();
        var prompt = _itemPrompt.Text.Trim();
        var item = _items[_index];
        if (!string.Equals(prompt, item.Prompt, StringComparison.Ordinal) && item.HasLesson)
        {
            item.Lesson = null;
            _hint.Text = "Polecenie się zmieniło. Poprzednia lekcja nie jedzie z plikiem — ułóż ją jeszcze raz.";
        }
        item.Title = title.Length == 0 ? "Zagadnienie" : title;
        item.Prompt = prompt;
        if (_index < _list.Items.Count) _list.Items[_index] = ItemLabel(item);
        RefreshLessonChrome();
    }

    private void ReloadList(int select)
    {
        _loading = true;
        _list.Items.Clear();
        foreach (var item in _items) _list.Items.Add(ItemLabel(item));
        _index = select;
        _list.SelectedIndex = select;
        _itemTitle.Text = _items[select].Title;
        _itemPrompt.Text = _items[select].Prompt;
        _loading = false;
        RefreshLessonChrome();
    }

    private void PrepareLesson()
    {
        FlushItem();
        if (_index < 0 || _index >= _items.Count) return;
        var item = _items[_index];
        if (item.Prompt.Length == 0)
        {
            _hint.Text = "Najpierw wpisz polecenie tego zagadnienia.";
            return;
        }
        var compose = Compose;
        if (compose == null || !CanCompose)
        {
            _hint.Text = "Najpierw wklej klucz API w Ustawieniach.";
            return;
        }

        var ready = _items.Where(entry => entry.Prompt.Length > 0).ToList();
        var place = ready.IndexOf(item);
        if (place < 0) place = _index;
        var count = Math.Max(1, ready.Count);
        var question = PlanLessonText.Question(_title.Text.Trim(), _author.Text.Trim(), _note.Text.Trim(), place + 1, count, item.Prompt);
        var shown = PlanLessonText.Shown(_title.Text.Trim(), _subject.Text, place + 1, count, item.Title, item.Prompt);
        using var preview = new LessonPreview(item.Title, shown, _subject.Text, _level.Text, item.Lesson, ct => compose(_subject.Text, _level.Text, question, ct));
        if (preview.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(preview.Lesson)) return;
        item.Lesson = preview.Lesson.Trim();
        if (_index < _list.Items.Count) _list.Items[_index] = ItemLabel(item);
        RefreshLessonChrome();
        _hint.Text = "Lekcja zostaje przy zagadnieniu. Zapisz plik, żeby uczeń dostał ten sam układ i mógł iść dalej po swojemu.";
    }

    private void RefreshLessonChrome()
    {
        var item = _index >= 0 && _index < _items.Count ? _items[_index] : null;
        var ready = item?.HasLesson == true;
        _lessonButton.Text = ready ? "Zobacz lekcję" : "Ułóż lekcję";
        _lessonState.Text = ready
            ? "Lekcja jest w pliku. Uczeń zobaczy ten układ i może iść dalej po swojemu."
            : "Bez przygotowanej lekcji. Ułóż ją i obejrzyj, zanim wyślesz plik.";
    }

    private static string ItemLabel(StudyPlanItem item)
    {
        if (item.HasLesson) return item.Title + "  · lekcja";
        return item.Prompt.Length == 0 ? item.Title + "  ·" : item.Title;
    }

    private static void SelectCombo(ComboBox combo, string value)
    {
        var index = combo.Items.IndexOf(value);
        combo.SelectedIndex = index >= 0 ? index : 0;
    }

    private static Label LabelAt(string text, int x, int y) => new()
    {
        Text = text,
        ForeColor = Theme.Muted,
        Font = Theme.TinyBold,
        AutoSize = true,
        Location = new Point(x, y)
    };

    private static Button SmallButton(string text, int x, int y)
    {
        var button = new Button { Text = text, AutoSize = false };
        StyleChoice(button);
        button.SetBounds(x, y, 112, 34);
        return button;
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
}

internal static class StudyPlanLoading
{
    public static StudyPlan CopySafe(this StudyPlan source, string subject, string level)
    {
        try
        {
            return source.Items.Count == 0 ? Fresh(source, subject, level) : source.Copy();
        }
        catch (InvalidOperationException)
        {
            return Fresh(source, subject, level);
        }
    }

    private static StudyPlan Fresh(StudyPlan source, string subject, string level) => new()
    {
        Title = source.Title,
        Author = source.Author,
        Note = source.Note,
        Subject = string.IsNullOrWhiteSpace(source.Subject) ? subject : source.Subject,
        Level = string.IsNullOrWhiteSpace(source.Level) ? level : source.Level,
        Items = [new StudyPlanItem { Title = "Zagadnienie 1" }]
    };
}
