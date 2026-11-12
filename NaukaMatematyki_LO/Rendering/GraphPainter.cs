using System.Globalization;

namespace NaukaMatematyki_LO;

internal static class GraphPainter
{
    public static void Draw(Graphics graphics, Rectangle bounds, GraphSpec spec)
    {
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        using var back = new SolidBrush(Theme.Formula);
        graphics.FillRectangle(back, bounds);

        var plot = Rectangle.Inflate(bounds, -8, -8);
        plot = new Rectangle(plot.X + 46, plot.Y + 28, Math.Max(20, plot.Width - 58), Math.Max(20, plot.Height - 62));
        if (!string.IsNullOrWhiteSpace(spec.Title))
        {
            TextRenderer.DrawText(graphics, spec.Title, Theme.SmallBold,
                new Rectangle(bounds.X + 14, bounds.Y + 8, bounds.Width - 28, 20),
                Theme.Chalk, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }

        var xMin = spec.XMin ?? -10;
        var xMax = spec.XMax ?? 10;
        if (xMax - xMin < 1e-6) { xMin -= 1; xMax += 1; }

        var compiled = new List<(GraphSeries Series, Func<double, double> F, Color Color)>();
        var notes = new List<string>();
        for (var i = 0; i < spec.Series.Count; i++)
        {
            var series = spec.Series[i];
            if (string.IsNullOrWhiteSpace(series.Formula)) continue;
            try
            {
                compiled.Add((series, SafeExpression.Compile(series.Formula), ColorOf(series.ColorHex, i)));
            }
            catch (FormatException ex)
            {
                notes.Add(ex.Message);
            }
        }

        const int samples = 480;
        var ys = new List<double>();
        var cache = new List<PointF?>[compiled.Count];
        for (var s = 0; s < compiled.Count; s++)
        {
            cache[s] = new List<PointF?>(samples);
            double? previous = null;
            for (var i = 0; i < samples; i++)
            {
                var x = xMin + (xMax - xMin) * i / (samples - 1);
                double y;
                try { y = compiled[s].F(x); }
                catch { y = double.NaN; }
                if (double.IsFinite(y) && previous is not null && Math.Abs(y - previous.Value) > 1e6)
                    y = double.NaN;
                if (double.IsFinite(y))
                {
                    ys.Add(y);
                    previous = y;
                }
                else previous = null;
                cache[s].Add(null);
            }
        }

        foreach (var point in spec.Points)
            if (double.IsFinite(point.Y)) ys.Add(point.Y);

        if (ys.Count == 0)
        {
            TextRenderer.DrawText(graphics, notes.FirstOrDefault() ?? "Brak danych do wykresu.", Theme.Small,
                plot, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        ys.Sort();
        var low = ys[Math.Max(0, (int)(ys.Count * 0.02))];
        var high = ys[Math.Min(ys.Count - 1, (int)(ys.Count * 0.98))];
        if (high - low < 1e-6) { low -= 1; high += 1; }
        var pad = (high - low) * 0.12;
        var yMin = spec.YMin ?? low - pad;
        var yMax = spec.YMax ?? high + pad;
        if (yMax - yMin < 1e-6) { yMin -= 1; yMax += 1; }

        DrawGrid(graphics, plot, xMin, xMax, yMin, yMax, spec.XLabel, spec.YLabel);

        for (var s = 0; s < compiled.Count; s++)
        {
            var series = compiled[s];
            using var pen = new Pen(series.Color, 2.2f)
            {
                LineJoin = System.Drawing.Drawing2D.LineJoin.Round,
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round
            };
            var segment = new List<PointF>();
            double? previous = null;
            for (var i = 0; i < samples; i++)
            {
                var x = xMin + (xMax - xMin) * i / (samples - 1);
                double y;
                try { y = series.F(x); }
                catch { y = double.NaN; }
                var jump = previous is not null && double.IsFinite(y) && Math.Abs(y - previous.Value) > (yMax - yMin) * 8;
                if (!double.IsFinite(y) || y < yMin - (yMax - yMin) || y > yMax + (yMax - yMin) || jump)
                {
                    Stroke(graphics, pen, segment);
                    segment.Clear();
                    previous = double.IsFinite(y) ? y : null;
                    continue;
                }
                segment.Add(ToPoint(plot, x, y, xMin, xMax, yMin, yMax));
                previous = y;
            }
            Stroke(graphics, pen, segment);
        }

        using var dash = new Pen(Color.FromArgb(150, Theme.Rose), 1.2f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        foreach (var x in spec.VerticalLines)
        {
            if (x < xMin || x > xMax) continue;
            var px = ToPoint(plot, x, yMin, xMin, xMax, yMin, yMax).X;
            graphics.DrawLine(dash, px, plot.Top, px, plot.Bottom);
        }

        for (var i = 0; i < spec.Points.Count; i++)
        {
            var point = spec.Points[i];
            if (point.X < xMin || point.X > xMax || point.Y < yMin || point.Y > yMax) continue;
            var at = ToPoint(plot, point.X, point.Y, xMin, xMax, yMin, yMax);
            var color = Theme.Series[i % Theme.Series.Length];
            using var brush = new SolidBrush(color);
            graphics.FillEllipse(brush, at.X - 4.5f, at.Y - 4.5f, 9, 9);
            graphics.DrawEllipse(Pens.White, at.X - 4.5f, at.Y - 4.5f, 9, 9);
            if (point.Label.Length > 0)
            {
                TextRenderer.DrawText(graphics, point.Label, Theme.TinyBold,
                    new Rectangle((int)at.X + 6, (int)at.Y - 16, 140, 16), color,
                    TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            }
        }

        var legend = compiled.Where(item => item.Series.Label.Length > 0).ToList();
        var legendX = plot.Right - 8;
        var legendY = plot.Top + 6;
        for (var i = legend.Count - 1; i >= 0; i--)
        {
            var text = legend[i].Series.Label;
            var size = TextRenderer.MeasureText(text, Theme.Tiny);
            legendX -= size.Width + 18;
            using var brush = new SolidBrush(legend[i].Color);
            graphics.FillRectangle(brush, legendX, legendY + 3, 12, 3);
            TextRenderer.DrawText(graphics, text, Theme.Tiny, new Point(legendX + 16, legendY), Theme.Chalk);
        }

        if (notes.Count > 0)
        {
            TextRenderer.DrawText(graphics, notes[0], Theme.Tiny,
                new Rectangle(bounds.X + 12, bounds.Bottom - 22, bounds.Width - 24, 18),
                Theme.Rose, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }
    }

    private static void DrawGrid(Graphics graphics, Rectangle plot, double xMin, double xMax, double yMin, double yMax, string xLabel, string yLabel)
    {
        using var grid = new Pen(Color.FromArgb(38, 52, 70));
        using var axis = new Pen(Color.FromArgb(214, 222, 232), 1.3f);
        var xStep = NiceStep(xMax - xMin);
        var yStep = NiceStep(yMax - yMin);
        var labelFont = Theme.Tiny;

        for (var x = Math.Ceiling(xMin / xStep) * xStep; x <= xMax + xStep * 0.01; x += xStep)
        {
            var px = ToPoint(plot, x, yMin, xMin, xMax, yMin, yMax).X;
            graphics.DrawLine(grid, px, plot.Top, px, plot.Bottom);
            var text = FormatTick(x);
            TextRenderer.DrawText(graphics, text, labelFont, new Rectangle((int)px - 28, plot.Bottom + 4, 56, 16),
                Theme.Muted, TextFormatFlags.HorizontalCenter);
        }
        for (var y = Math.Ceiling(yMin / yStep) * yStep; y <= yMax + yStep * 0.01; y += yStep)
        {
            var py = ToPoint(plot, xMin, y, xMin, xMax, yMin, yMax).Y;
            graphics.DrawLine(grid, plot.Left, py, plot.Right, py);
            TextRenderer.DrawText(graphics, FormatTick(y), labelFont, new Rectangle(plot.Left - 50, (int)py - 8, 46, 16),
                Theme.Muted, TextFormatFlags.Right);
        }

        if (xMin <= 0 && xMax >= 0)
        {
            var px = ToPoint(plot, 0, yMin, xMin, xMax, yMin, yMax).X;
            graphics.DrawLine(axis, px, plot.Top, px, plot.Bottom);
        }
        if (yMin <= 0 && yMax >= 0)
        {
            var py = ToPoint(plot, xMin, 0, xMin, xMax, yMin, yMax).Y;
            graphics.DrawLine(axis, plot.Left, py, plot.Right, py);
        }
        graphics.DrawRectangle(grid, plot);
        TextRenderer.DrawText(graphics, xLabel, labelFont, new Rectangle(plot.Right - 40, plot.Bottom + 16, 48, 16), Theme.Chalk, TextFormatFlags.Right);
        TextRenderer.DrawText(graphics, yLabel, labelFont, new Rectangle(plot.Left - 4, plot.Top - 18, 40, 16), Theme.Chalk, TextFormatFlags.Left);
    }

    private static void Stroke(Graphics graphics, Pen pen, List<PointF> segment)
    {
        if (segment.Count > 1) graphics.DrawLines(pen, segment.ToArray());
    }

    private static PointF ToPoint(Rectangle plot, double x, double y, double xMin, double xMax, double yMin, double yMax)
    {
        var px = plot.Left + (float)((x - xMin) / (xMax - xMin) * plot.Width);
        var py = plot.Bottom - (float)((y - yMin) / (yMax - yMin) * plot.Height);
        return new PointF(px, py);
    }

    private static double NiceStep(double range)
    {
        var raw = Math.Max(range, 1e-6) / 6;
        var mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var norm = raw / mag;
        var nice = norm < 1.5 ? 1 : norm < 3.5 ? 2 : norm < 7.5 ? 5 : 10;
        return nice * mag;
    }

    private static string FormatTick(double value)
    {
        if (Math.Abs(value) < 1e-9) return "0";
        return Math.Abs(value) >= 1000 || Math.Abs(value) < 0.01
            ? value.ToString("0.##e0", CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static Color ColorOf(string? hex, int index)
    {
        if (!string.IsNullOrWhiteSpace(hex))
        {
            try { return ColorTranslator.FromHtml(hex); }
            catch (Exception) { }
        }
        return Theme.Series[index % Theme.Series.Length];
    }
}
