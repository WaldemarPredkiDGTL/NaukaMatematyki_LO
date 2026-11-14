using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace NaukaMatematyki_LO;

internal sealed class LessonBoard : Panel
{
    private readonly List<LessonCard> _cards = [];
    private int _contentHeight;
    private bool _layingOut;

    public LessonBoard()
    {
        AutoScroll = true;
        BackColor = Theme.Ink;
        DoubleBuffered = true;
        Resize += (_, _) => LayoutCards();
    }

    public void Show(IReadOnlyList<StudyTurn> turns, bool includeWelcome, StudyPlan? plan = null, string? working = null)
    {
        SuspendLayout();
        foreach (var card in _cards) card.Dispose();
        _cards.Clear();
        Controls.Clear();
        if (plan is { Items.Count: > 0 } && turns.Count == 0)
            Add(new PlanCard(plan));
        else if (includeWelcome && turns.Count == 0)
            AddDocument(null, SampleLesson.Welcome());
        foreach (var turn in turns)
        {
            if (turn.Lesson is null) continue;
            AddDocument(turn, turn.Lesson);
        }
        if (!string.IsNullOrWhiteSpace(working))
            Add(new WorkingCard(working));
        ResumeLayout();
        LayoutCards();
        ScrollToEnd();
    }

    public void ExportPng(string path)
    {
        var width = Math.Max(860, ClientSize.Width);
        var height = Math.Max(240, _contentHeight + 24);
        using var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Theme.Ink);
        foreach (var card in _cards)
        {
            using var piece = new Bitmap(Math.Max(1, card.Width), Math.Max(1, card.Height));
            card.DrawToBitmap(piece, new Rectangle(0, 0, piece.Width, piece.Height));
            graphics.DrawImage(piece, card.Left, card.Top - AutoScrollPosition.Y);
        }
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    public void ExportPdf(string path)
    {
        var width = Math.Max(860, ClientSize.Width);
        var height = Math.Max(240, _contentHeight + 24);
        BoardPdf.Write(path, width, height, (graphics, top, sliceHeight) => PaintExport(graphics, top, sliceHeight));
    }

    private void PaintExport(Graphics graphics, int sliceTop, int sliceHeight)
    {
        graphics.Clear(Theme.Paper);
        var scroll = AutoScrollPosition.Y;
        foreach (var card in _cards)
        {
            var logicalTop = card.Top - scroll;
            if (logicalTop + card.Height < sliceTop || logicalTop > sliceTop + sliceHeight) continue;
            using var piece = new Bitmap(Math.Max(1, card.Width), Math.Max(1, card.Height), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var cardGraphics = Graphics.FromImage(piece))
            {
                cardGraphics.Clear(Color.Transparent);
                card.Render(cardGraphics);
            }
            graphics.DrawImage(piece, card.Left, logicalTop - sliceTop);
        }
    }

    private void AddDocument(StudyTurn? turn, LessonDocument lesson)
    {
        if (turn is not null) Add(new QuestionCard(turn));
        if (!string.IsNullOrWhiteSpace(lesson.Title) || !string.IsNullOrWhiteSpace(lesson.Headline))
            Add(new TitleCard(lesson.Title, lesson.Headline));
        foreach (var block in lesson.Blocks)
        {
            LessonCard? card = block.Type switch
            {
                "formula" => new FormulaCard(block),
                "steps" => new StepsCard(block),
                "callout" => new CalloutCard(block.Content ?? ""),
                "graph" => new GraphCard(block),
                _ => string.IsNullOrWhiteSpace(block.Content) ? null : new TextCard(block.Content)
            };
            if (card is not null) Add(card);
        }
        if (!string.IsNullOrWhiteSpace(lesson.Solution))
            Add(new SolutionCard(lesson.Solution));
        if (!string.IsNullOrWhiteSpace(lesson.CheckQuestion))
            Add(new CheckCard(lesson.CheckQuestion, lesson.CheckAnswer));
        if (!string.IsNullOrWhiteSpace(lesson.NextTask))
            Add(new NextTaskCard(lesson.NextTask));
    }

    private void Add(LessonCard card)
    {
        _cards.Add(card);
        Controls.Add(card);
    }

    private void LayoutCards()
    {
        if (_layingOut || _cards.Count == 0) return;
        _layingOut = true;
        var scrollY = ScrollOffset();
        ScrollTo(0);
        var width = Math.Max(320, ClientSize.Width - 28);
        var y = 16;
        foreach (var card in _cards)
        {
            card.Reflow(width);
            card.SetBounds(14, y, width, card.Height);
            y += card.Height + 12;
        }
        _contentHeight = y;
        AutoScrollMinSize = new Size(0, y + 8);
        ScrollTo(scrollY);
        _layingOut = false;
    }

    public void ScrollToEnd() => ScrollTo(int.MaxValue);

    public void Relayout() => LayoutCards();

    public void Reveal(Control card, Control anchor)
    {
        var button = RectangleToClient(anchor.RectangleToScreen(anchor.ClientRectangle));
        var bounds = RectangleToClient(card.RectangleToScreen(card.ClientRectangle));
        var overflow = bounds.Bottom - ClientSize.Height + 12;
        if (overflow <= 0) return;
        var room = Math.Max(0, button.Y - 16);
        ScrollTo(ScrollOffset() + Math.Min(overflow, room));
    }

    private int MaxScroll() => Math.Max(0, Math.Max(_contentHeight, AutoScrollMinSize.Height) - ClientSize.Height);

    private int ScrollOffset() => -AutoScrollPosition.Y;

    private void ScrollTo(int y)
    {
        if (!IsHandleCreated) return;
        var target = Math.Clamp(y, 0, MaxScroll());
        if (ScrollOffset() == target) return;
        SetDisplayRectLocation(0, -target);
        if (ScrollOffset() != target)
            AutoScrollPosition = new Point(0, target);
    }

    protected override Point ScrollToControl(Control activeControl) => DisplayRectangle.Location;
}

internal sealed class QuietButton : Button
{
    public QuietButton()
    {
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
    }
}

internal abstract class LessonCard : Panel
{
    protected LessonCard()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        TabStop = false;
        SetStyle(ControlStyles.Selectable, false);
        Margin = Padding.Empty;
    }

    public abstract void Reflow(int width);

    internal void Render(Graphics graphics)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        PaintCard(graphics);
        foreach (Control child in Controls)
        {
            if (!child.Visible || child.Width < 1 || child.Height < 1) continue;
            using var piece = new Bitmap(child.Width, child.Height);
            child.DrawToBitmap(piece, new Rectangle(0, 0, child.Width, child.Height));
            graphics.DrawImage(piece, child.Left, child.Top);
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x020A && Parent is not null)
        {
            SendMessage(Parent.Handle, m.Msg, m.WParam, m.LParam);
            return;
        }
        base.WndProc(ref m);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

    protected static int TextHeight(string text, Font font, int width)
    {
        if (string.IsNullOrWhiteSpace(text) || width < 20) return 0;
        return TextRenderer.MeasureText(text, font, new Size(width, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        PaintCard(e.Graphics);
    }

    protected abstract void PaintCard(Graphics graphics);

    protected static void FillRound(Graphics graphics, Rectangle bounds, int radius, Color color)
    {
        using var path = Round(bounds, radius);
        using var brush = new SolidBrush(color);
        graphics.FillPath(brush, path);
    }

    protected static GraphicsPath Round(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var d = Math.Max(2, radius * 2);
        var r = new Rectangle(bounds.X, bounds.Y, Math.Max(d, bounds.Width - 1), Math.Max(d, bounds.Height - 1));
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class PlanCard(StudyPlan plan) : LessonCard
{
    public override void Reflow(int width)
    {
        var inner = Math.Max(80, width - 40);
        var height = 18 + 20 + 40;
        if (!string.IsNullOrWhiteSpace(plan.Author)) height += 24;
        if (!string.IsNullOrWhiteSpace(plan.Note)) height += 10 + TextHeight(plan.Note, Theme.Body, inner);
        foreach (var item in plan.Items)
        {
            height += 14 + 22 + TextHeight(item.Prompt, Theme.Body, inner);
            if (item.HasLesson) height += 18;
        }
        Height = height + 18;
    }

    protected override void PaintCard(Graphics graphics)
    {
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        FillRound(graphics, bounds, 14, Theme.Paper);
        var inner = Math.Max(80, Width - 40);
        var y = 16;
        var kicker = string.Join("  ·  ", new[] { plan.Subject, plan.Level }.Where(part => !string.IsNullOrWhiteSpace(part)));
        TextRenderer.DrawText(graphics, "PRZESŁANY PLAN", Theme.TinyBold, new Rectangle(20, y, inner, 16), Theme.Copper, TextFormatFlags.Left);
        y += 18;
        if (kicker.Length > 0)
        {
            TextRenderer.DrawText(graphics, kicker.ToUpperInvariant(), Theme.TinyBold, new Rectangle(20, y, inner, 16), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            y += 20;
        }
        TextRenderer.DrawText(graphics, plan.Title, Theme.Title, new Rectangle(20, y, inner, 36), Theme.InkText, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
        y += 40;
        if (!string.IsNullOrWhiteSpace(plan.Author))
        {
            TextRenderer.DrawText(graphics, "Ułożył " + plan.Author, Theme.Small, new Rectangle(20, y, inner, 20), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            y += 24;
        }
        if (!string.IsNullOrWhiteSpace(plan.Note))
        {
            var noteHeight = TextHeight(plan.Note, Theme.Body, inner);
            TextRenderer.DrawText(graphics, plan.Note, Theme.Body, new Rectangle(20, y, inner, noteHeight), Theme.InkText, TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            y += noteHeight + 10;
        }
        var number = 1;
        foreach (var item in plan.Items)
        {
            TextRenderer.DrawText(graphics, number + ".  " + item.Title, Theme.BodyBold, new Rectangle(20, y, inner, 22), Theme.InkText, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            y += 24;
            var promptHeight = TextHeight(item.Prompt, Theme.Body, inner);
            TextRenderer.DrawText(graphics, item.Prompt, Theme.Body, new Rectangle(20, y, inner, promptHeight), Theme.InkText, TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            y += promptHeight + 4;
            if (item.HasLesson)
            {
                TextRenderer.DrawText(graphics, "Lekcja przygotowana", Theme.TinyBold, new Rectangle(20, y, inner, 16), Theme.Copper, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                y += 18;
            }
            y += 8;
            number++;
        }
    }
}

internal sealed class WorkingCard(string detail) : LessonCard
{
    public override void Reflow(int width)
    {
        var inner = Math.Max(80, width - 40);
        Height = 78 + TextHeight(detail, Theme.Body, inner);
    }

    protected override void PaintCard(Graphics graphics)
    {
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        FillRound(graphics, bounds, 14, Theme.Formula);
        using var pen = new Pen(Theme.Copper, 2f);
        using var path = Round(new Rectangle(1, 1, Width - 3, Height - 3), 14);
        graphics.DrawPath(pen, path);
        var inner = Math.Max(80, Width - 40);
        TextRenderer.DrawText(graphics, "GROK OPRACOWUJE LEKCJĘ", Theme.TinyBold, new Rectangle(20, 14, inner, 16), Theme.Copper, TextFormatFlags.Left);
        TextRenderer.DrawText(graphics, "To zwykle trwa minutę albo dwie. Nie zamykaj programu. Stop przerywa.", Theme.Small, new Rectangle(20, 34, inner, 36), Theme.Chalk, TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        var bodyHeight = TextHeight(detail, Theme.Body, inner);
        TextRenderer.DrawText(graphics, detail, Theme.Body, new Rectangle(20, 74, inner, bodyHeight), Theme.Chalk, TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
    }
}

internal sealed class QuestionCard(StudyTurn turn) : LessonCard
{
    public override void Reflow(int width)
    {
        var text = TextHeight(turn.Question, Theme.Body, width - 36);
        var images = turn.Images is { Count: > 0 } ? 62 : 0;
        Height = Math.Max(54, 28 + text + images);
    }

    protected override void PaintCard(Graphics graphics)
    {
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        FillRound(graphics, bounds, 12, Color.FromArgb(28, 36, 50));
        using var bar = new SolidBrush(Theme.Copper);
        graphics.FillRectangle(bar, 0, 10, 4, Height - 20);
        TextRenderer.DrawText(graphics, turn.Subject.ToUpperInvariant(), Theme.TinyBold,
            new Rectangle(18, 8, Width - 36, 16), Theme.Copper, TextFormatFlags.Left);
        var imageBand = turn.Images is { Count: > 0 } ? 62 : 8;
        TextRenderer.DrawText(graphics, turn.Question, Theme.Body,
            new Rectangle(18, 26, Width - 36, Math.Max(18, Height - 26 - imageBand)), Theme.Chalk,
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        if (turn.Images is not { Count: > 0 }) return;
        var x = 18;
        var y = Height - 58;
        foreach (var image in turn.Images.Take(6))
        {
            graphics.DrawImage(image, new Rectangle(x, y, 72, 46));
            using var pen = new Pen(Theme.Line);
            graphics.DrawRectangle(pen, x, y, 72, 46);
            x += 80;
        }
    }
}

internal sealed class TitleCard(string title, string headline) : LessonCard
{
    public override void Reflow(int width)
    {
        var head = TextHeight(headline, Theme.Body, width - 36);
        Height = 22 + (string.IsNullOrWhiteSpace(title) ? 0 : 32) + head;
    }

    protected override void PaintCard(Graphics graphics)
    {
        FillRound(graphics, new Rectangle(0, 0, Width - 1, Height - 1), 14, Theme.Paper);
        var y = 12;
        if (!string.IsNullOrWhiteSpace(title))
        {
            TextRenderer.DrawText(graphics, title, Theme.Title, new Rectangle(18, y, Width - 36, 32),
                Theme.InkText, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            y += 34;
        }
        if (!string.IsNullOrWhiteSpace(headline))
        {
            TextRenderer.DrawText(graphics, headline, Theme.Body, new Rectangle(18, y, Width - 36, Height - y - 8),
                Theme.Muted, TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        }
    }
}

internal sealed class TextCard(string content) : LessonCard
{
    public override void Reflow(int width) => Height = 28 + TextHeight(Clean(content), Theme.Body, width - 40);

    protected override void PaintCard(Graphics graphics)
    {
        FillRound(graphics, new Rectangle(0, 0, Width - 1, Height - 1), 14, Theme.Paper);
        TextRenderer.DrawText(graphics, Clean(content), Theme.Body, new Rectangle(20, 14, Width - 40, Height - 22),
            Theme.InkText, TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
    }

    private static string Clean(string text) => text.Replace("**", "", StringComparison.Ordinal).Replace("`", "", StringComparison.Ordinal).Trim();
}

internal sealed class FormulaCard(LessonBlock block) : LessonCard
{
    public override void Reflow(int width)
    {
        var units = string.IsNullOrWhiteSpace(block.Units) ? 0 : 34;
        var caption = string.IsNullOrWhiteSpace(block.Caption) ? 0 : 22;
        Height = 108 + units + caption;
    }

    protected override void PaintCard(Graphics graphics)
    {
        var hasUnits = !string.IsNullOrWhiteSpace(block.Units);
        var hasCaption = !string.IsNullOrWhiteSpace(block.Caption);
        FillRound(graphics, new Rectangle(0, 0, Width - 1, Height - 1), 14, Theme.Formula);
        using var pen = new Pen(Color.FromArgb(90, Theme.Copper), 1.2f);
        using var path = Round(new Rectangle(1, 1, Width - 3, Height - 3), 14);
        graphics.DrawPath(pen, path);
        var footer = (hasUnits ? 34 : 0) + (hasCaption ? 22 : 0);
        var area = new RectangleF(16, 8, Width - 32, Height - 16 - footer);
        if (!FormulaLayout.TryRender(graphics, block.Latex, area, 26, Theme.Chalk, out _))
        {
            var fallback = string.IsNullOrWhiteSpace(block.Display) ? block.Latex ?? "" : block.Display;
            TextRenderer.DrawText(graphics, fallback, Theme.Math(20), Rectangle.Round(area), Theme.Chalk,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }
        var y = Height - footer - 4;
        if (hasUnits)
        {
            TextRenderer.DrawText(graphics, block.Units, Theme.Math(18, FontStyle.Bold), new Rectangle(16, y, Width - 32, 28),
                Theme.Copper, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            y += 34;
        }
        if (hasCaption)
        {
            TextRenderer.DrawText(graphics, block.Caption, Theme.Tiny, new Rectangle(16, Height - 24, Width - 32, 18),
                Theme.Copper, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}

internal sealed class StepsCard(LessonBlock block) : LessonCard
{
    public override void Reflow(int width)
    {
        var y = 42;
        foreach (var item in block.Items ?? [])
            y += TextHeight(item, Theme.Body, width - 68) + 12;
        Height = Math.Max(64, y);
    }

    protected override void PaintCard(Graphics graphics)
    {
        FillRound(graphics, new Rectangle(0, 0, Width - 1, Height - 1), 14, Theme.Paper);
        TextRenderer.DrawText(graphics, "KROK PO KROKU", Theme.TinyBold, new Rectangle(18, 12, Width - 36, 16), Theme.CopperDeep);
        var y = 36;
        var index = 1;
        foreach (var item in block.Items ?? [])
        {
            var h = TextHeight(item, Theme.Body, Width - 68);
            using var brush = new SolidBrush(Theme.Copper);
            graphics.FillEllipse(brush, 16, y, 22, 22);
            TextRenderer.DrawText(graphics, index.ToString(), Theme.TinyBold, new Rectangle(16, y + 3, 22, 16),
                Color.White, TextFormatFlags.HorizontalCenter);
            TextRenderer.DrawText(graphics, item, Theme.Body, new Rectangle(48, y, Width - 68, h),
                Theme.InkText, TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            y += h + 12;
            index++;
        }
    }
}

internal sealed class CalloutCard(string content) : LessonCard
{
    public override void Reflow(int width) => Height = 28 + TextHeight(content, Theme.Body, width - 48);

    protected override void PaintCard(Graphics graphics)
    {
        FillRound(graphics, new Rectangle(0, 0, Width - 1, Height - 1), 12, Color.FromArgb(255, 246, 230));
        using var bar = new SolidBrush(Theme.Copper);
        graphics.FillRectangle(bar, 0, 8, 5, Height - 16);
        TextRenderer.DrawText(graphics, content, Theme.Body, new Rectangle(20, 12, Width - 36, Height - 20),
            Theme.InkText, TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
    }
}

internal sealed class GraphCard(LessonBlock block) : LessonCard
{
    public override void Reflow(int width) => Height = string.IsNullOrWhiteSpace(block.Caption) ? 292 : 318;

    protected override void PaintCard(Graphics graphics)
    {
        var plotHeight = string.IsNullOrWhiteSpace(block.Caption) ? Height - 1 : Height - 26;
        if (block.Graph is not null)
            GraphPainter.Draw(graphics, new Rectangle(0, 0, Width - 1, plotHeight), block.Graph);
        else
            FillRound(graphics, new Rectangle(0, 0, Width - 1, plotHeight), 12, Theme.Formula);
        if (!string.IsNullOrWhiteSpace(block.Caption))
        {
            TextRenderer.DrawText(graphics, block.Caption, Theme.Small, new Rectangle(8, Height - 22, Width - 16, 18),
                Theme.Muted, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }
    }
}

internal sealed class SolutionCard(string solution) : LessonCard
{
    public override void Reflow(int width)
    {
        Height = 36 + TextHeight(solution, Theme.Body, width - 40) + 16;
    }

    protected override void PaintCard(Graphics graphics)
    {
        FillRound(graphics, new Rectangle(0, 0, Width - 1, Height - 1), 14, Theme.PaperAlt);
        TextRenderer.DrawText(graphics, "JAK TO ROZWIĄZAĆ", Theme.TinyBold, new Rectangle(18, 10, Width - 36, 16), Theme.Copper);
        TextRenderer.DrawText(graphics, solution, Theme.Body, new Rectangle(18, 30, Width - 40, Height - 42),
            Theme.InkText, TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
    }
}

internal sealed class NextTaskCard(string task) : LessonCard
{
    public override void Reflow(int width)
    {
        Height = 36 + TextHeight(task, Theme.BodyBold, width - 40) + 16;
    }

    protected override void PaintCard(Graphics graphics)
    {
        FillRound(graphics, new Rectangle(0, 0, Width - 1, Height - 1), 14, Theme.Paper);
        TextRenderer.DrawText(graphics, "NASTĘPNE ZADANIE", Theme.TinyBold, new Rectangle(18, 10, Width - 36, 16), Theme.Sky);
        TextRenderer.DrawText(graphics, task, Theme.BodyBold, new Rectangle(18, 30, Width - 40, Height - 42),
            Theme.InkText, TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
    }
}

internal sealed class CheckCard : LessonCard
{
    private readonly string _question;
    private readonly string _answer;
    private readonly Button _reveal;
    private bool _open;

    public CheckCard(string question, string answer)
    {
        _question = question;
        _answer = answer;
        _reveal = new QuietButton
        {
            Text = "Pokaż odpowiedź",
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Copper,
            ForeColor = Color.White,
            Font = Theme.SmallBold,
            Cursor = Cursors.Hand,
            Size = new Size(170, 30)
        };
        _reveal.FlatAppearance.BorderSize = 0;
        _reveal.Click += (_, _) =>
        {
            _open = !_open;
            _reveal.Text = _open ? "Ukryj" : "Pokaż odpowiedź";
            if (Parent is LessonBoard board)
            {
                board.Relayout();
                if (_open) board.Reveal(this, _reveal);
            }
            Invalidate();
        };
        Controls.Add(_reveal);
    }

    public override void Reflow(int width)
    {
        var q = TextHeight(_question, Theme.BodyBold, width - 40);
        var buttonTop = 36 + q;
        var answerHeight = _open ? TextHeight(_answer, Theme.Body, width - 40) : 0;
        var answerTop = buttonTop + _reveal.Height + 8;
        Height = answerTop + answerHeight + 12;
        _reveal.Location = new Point(18, buttonTop);
    }

    protected override void PaintCard(Graphics graphics)
    {
        FillRound(graphics, new Rectangle(0, 0, Width - 1, Height - 1), 14, Theme.PaperAlt);
        TextRenderer.DrawText(graphics, "SPRAWDŹ SIĘ", Theme.TinyBold, new Rectangle(18, 10, Width - 36, 16), Theme.Sage);
        var q = TextHeight(_question, Theme.BodyBold, Width - 40);
        TextRenderer.DrawText(graphics, _question, Theme.BodyBold, new Rectangle(18, 28, Width - 40, q),
            Theme.InkText, TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        if (_open && !string.IsNullOrWhiteSpace(_answer))
        {
            var answerTop = 36 + q + _reveal.Height + 8;
            TextRenderer.DrawText(graphics, _answer, Theme.Body, new Rectangle(18, answerTop, Width - 40, Height - answerTop - 12),
                Theme.InkText, TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        }
    }
}
