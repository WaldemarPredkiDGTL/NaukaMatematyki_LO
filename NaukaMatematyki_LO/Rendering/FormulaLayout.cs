namespace NaukaMatematyki_LO;

internal readonly record struct FormulaBox(float Width, float Height, float Baseline);

internal static class FormulaLayout
{
    public static bool TryRender(Graphics graphics, string? latex, RectangleF area, float fontSize, Color color, out float usedHeight)
    {
        usedHeight = 0;
        if (string.IsNullOrWhiteSpace(latex) || area.Width < 20) return false;
        try
        {
            var node = new Parser(latex).Parse();
            var natural = node.Measure(graphics, fontSize);
            if (natural.Width < 1 || natural.Height < 1) return false;
            var size = natural.Width > area.Width - 12
                ? fontSize * Math.Max(0.45f, (area.Width - 12) / natural.Width)
                : fontSize;
            var box = node.Measure(graphics, size);
            var x = area.X + Math.Max(0, (area.Width - box.Width) / 2f);
            node.Draw(graphics, x, area.Y + 6 + box.Baseline, size, color);
            usedHeight = box.Height + 12;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

internal abstract class FormulaNode
{
    public abstract FormulaBox Measure(Graphics graphics, float fontSize);
    public abstract void Draw(Graphics graphics, float x, float baseline, float fontSize, Color color);

    protected static FormulaBox MeasureText(Graphics graphics, string text, float fontSize)
    {
        var font = Theme.Math(Math.Max(7, fontSize));
        var size = graphics.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);
        var ascent = font.Size * font.FontFamily.GetCellAscent(font.Style) / font.FontFamily.GetEmHeight(font.Style);
        return new FormulaBox(Math.Max(1, size.Width), Math.Max(ascent + 2, size.Height), ascent);
    }

    protected static void DrawText(Graphics graphics, string text, float x, float baseline, float fontSize, Color color)
    {
        var font = Theme.Math(Math.Max(7, fontSize));
        var box = MeasureText(graphics, text, fontSize);
        using var brush = new SolidBrush(color);
        graphics.DrawString(text, font, brush, x, baseline - box.Baseline, StringFormat.GenericTypographic);
    }
}

internal sealed class TextNode(string text) : FormulaNode
{
    public override FormulaBox Measure(Graphics graphics, float fontSize) =>
        text.Length == 0 ? new FormulaBox(0, fontSize, fontSize * 0.78f) : MeasureText(graphics, text, fontSize);

    public override void Draw(Graphics graphics, float x, float baseline, float fontSize, Color color)
    {
        if (text.Length > 0) DrawText(graphics, text, x, baseline, fontSize, color);
    }
}

internal sealed class RowNode(List<FormulaNode> items) : FormulaNode
{
    public override FormulaBox Measure(Graphics graphics, float fontSize)
    {
        float width = 0, above = 0, below = 0;
        foreach (var item in items)
        {
            var box = item.Measure(graphics, fontSize);
            above = Math.Max(above, box.Baseline);
            below = Math.Max(below, box.Height - box.Baseline);
            width += box.Width + 1.2f;
        }
        return new FormulaBox(Math.Max(1, width), Math.Max(fontSize, above + below), above);
    }

    public override void Draw(Graphics graphics, float x, float baseline, float fontSize, Color color)
    {
        foreach (var item in items)
        {
            var box = item.Measure(graphics, fontSize);
            item.Draw(graphics, x, baseline, fontSize, color);
            x += box.Width + 1.2f;
        }
    }
}

internal sealed class FracNode(FormulaNode numerator, FormulaNode denominator) : FormulaNode
{
    public override FormulaBox Measure(Graphics graphics, float fontSize)
    {
        var child = fontSize * 0.92f;
        var num = numerator.Measure(graphics, child);
        var den = denominator.Measure(graphics, child);
        return new FormulaBox(Math.Max(num.Width, den.Width) + 10, num.Height + den.Height + 8, num.Height + 4);
    }

    public override void Draw(Graphics graphics, float x, float baseline, float fontSize, Color color)
    {
        var child = fontSize * 0.92f;
        var num = numerator.Measure(graphics, child);
        var den = denominator.Measure(graphics, child);
        var width = Math.Max(num.Width, den.Width) + 10;
        using var pen = new Pen(color, Math.Max(1.1f, fontSize / 18f));
        graphics.DrawLine(pen, x, baseline, x + width, baseline);
        numerator.Draw(graphics, x + (width - num.Width) / 2f, baseline - 4 - num.Height + num.Baseline, child, color);
        denominator.Draw(graphics, x + (width - den.Width) / 2f, baseline + 5 + den.Baseline, child, color);
    }
}

internal sealed class SqrtNode(FormulaNode inner) : FormulaNode
{
    public override FormulaBox Measure(Graphics graphics, float fontSize)
    {
        var body = inner.Measure(graphics, fontSize);
        return new FormulaBox(body.Width + fontSize * 0.72f + 6, body.Height + fontSize * 0.22f, body.Baseline + fontSize * 0.12f);
    }

    public override void Draw(Graphics graphics, float x, float baseline, float fontSize, Color color)
    {
        var body = inner.Measure(graphics, fontSize);
        var hook = fontSize * 0.46f;
        var top = baseline - body.Baseline - fontSize * 0.08f;
        using var pen = new Pen(color, Math.Max(1.2f, fontSize / 16f))
        {
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round,
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round
        };
        graphics.DrawLines(pen,
        [
            new PointF(x + 1, baseline - fontSize * 0.12f),
            new PointF(x + hook * 0.42f, baseline + fontSize * 0.16f),
            new PointF(x + hook, top),
            new PointF(x + hook + body.Width + 6, top)
        ]);
        inner.Draw(graphics, x + hook + 3, baseline, fontSize, color);
    }
}

internal sealed class ScriptNode(FormulaNode body, FormulaNode? superscript, FormulaNode? subscript) : FormulaNode
{
    public override FormulaBox Measure(Graphics graphics, float fontSize)
    {
        var main = body.Measure(graphics, fontSize);
        var script = fontSize * 0.62f;
        var sup = superscript?.Measure(graphics, script) ?? default;
        var sub = subscript?.Measure(graphics, script) ?? default;
        var above = superscript is null ? 0 : sup.Height * 0.55f;
        var below = subscript is null ? 0 : sub.Height * 0.45f;
        return new FormulaBox(
            main.Width + Math.Max(sup.Width, sub.Width) + 1,
            main.Height + above + below,
            main.Baseline + above);
    }

    public override void Draw(Graphics graphics, float x, float baseline, float fontSize, Color color)
    {
        var main = body.Measure(graphics, fontSize);
        body.Draw(graphics, x, baseline, fontSize, color);
        var script = fontSize * 0.62f;
        var scriptX = x + main.Width + 1;
        superscript?.Draw(graphics, scriptX, baseline - main.Baseline * 0.55f, script, color);
        subscript?.Draw(graphics, scriptX, baseline + fontSize * 0.38f, script, color);
    }
}

internal sealed class Parser
{
    private readonly string _text;
    private int _index;

    public Parser(string text) => _text = text.Replace("$$", "", StringComparison.Ordinal).Trim().Trim('$');

    public FormulaNode Parse() => ParseRow(stopAtBrace: false);

    private FormulaNode ParseRow(bool stopAtBrace)
    {
        var items = new List<FormulaNode>();
        while (!Done && !(stopAtBrace && Peek == '}'))
        {
            if (char.IsWhiteSpace(Peek) || Peek == '&') { _index++; continue; }
            if (!stopAtBrace && Peek == '}') break;
            items.Add(ParseTerm());
        }
        return items.Count == 1 ? items[0] : new RowNode(items);
    }

    private FormulaNode ParseTerm()
    {
        var node = ParseAtom();
        FormulaNode? sup = null;
        FormulaNode? sub = null;
        var primes = 0;
        while (!Done && Peek is '^' or '_' or '\'')
        {
            if (Peek == '\'') { _index++; primes++; continue; }
            var mark = _text[_index++];
            var script = ParseScript();
            if (mark == '^' && sup is null) sup = script;
            else if (mark == '_' && sub is null) sub = script;
        }
        if (primes > 0 && sup is null) sup = new TextNode(new string('′', primes));
        return sup is null && sub is null ? node : new ScriptNode(node, sup, sub);
    }

    private FormulaNode ParseScript()
    {
        SkipSpace();
        return Done ? new TextNode("") : ParseAtom();
    }

    private FormulaNode ParseAtom()
    {
        SkipSpace();
        if (Done) return new TextNode("");
        if (Peek == '{')
        {
            _index++;
            var inner = ParseRow(stopAtBrace: true);
            if (!Done && Peek == '}') _index++;
            return inner;
        }
        if (Peek == '\\') return ParseCommand();
        return new TextNode(_text[_index++].ToString());
    }

    private FormulaNode ParseCommand()
    {
        _index++;
        if (Done) return new TextNode("");
        if (!char.IsLetter(_text[_index]))
        {
            var symbol = _text[_index++];
            return symbol is ',' or ';' or ':' or ' ' ? new TextNode(" ") : new TextNode(symbol.ToString());
        }

        var start = _index;
        while (_index < _text.Length && char.IsLetter(_text[_index])) _index++;
        var name = _text[start.._index];
        return name switch
        {
            "frac" or "dfrac" or "tfrac" => new FracNode(ParseScript(), ParseScript()),
            "sqrt" => ParseSqrt(),
            "left" or "right" => new TextNode(ReadDelimiter()),
            "mathrm" or "text" or "operatorname" or "mathbf" or "textbf" => ParseScript(),
            _ => new TextNode(Map(name))
        };
    }

    private FormulaNode ParseSqrt()
    {
        SkipSpace();
        if (!Done && Peek == '[')
        {
            while (!Done && Peek != ']') _index++;
            if (!Done) _index++;
        }
        return new SqrtNode(ParseScript());
    }

    private string ReadDelimiter()
    {
        SkipSpace();
        if (Done) return "";
        if (Peek != '\\') return _text[_index++].ToString();
        _index++;
        var start = _index;
        while (_index < _text.Length && char.IsLetter(_text[_index])) _index++;
        return Map(_text[start.._index]);
    }

    private static string Map(string name) => name switch
    {
        "cdot" or "times" => "·",
        "pm" => "±",
        "mp" => "∓",
        "leq" or "le" => "≤",
        "geq" or "ge" => "≥",
        "neq" or "ne" => "≠",
        "approx" => "≈",
        "infty" => "∞",
        "pi" => "π",
        "alpha" => "α",
        "beta" => "β",
        "gamma" => "γ",
        "delta" => "δ",
        "theta" => "θ",
        "lambda" => "λ",
        "mu" => "μ",
        "omega" => "ω",
        "Delta" => "Δ",
        "Sigma" => "Σ",
        "to" or "rightarrow" => "→",
        "leftarrow" => "←",
        "degree" => "°",
        "circ" => "∘",
        "sin" or "cos" or "tan" or "cot" or "ln" or "log" or "lim" => name,
        "quad" => " ",
        "qquad" => "  ",
        "langle" => "⟨",
        "rangle" => "⟩",
        "lbrace" => "{",
        "rbrace" => "}",
        _ => name
    };

    private void SkipSpace()
    {
        while (!Done && char.IsWhiteSpace(Peek)) _index++;
    }

    private bool Done => _index >= _text.Length;
    private char Peek => _text[_index];
}
