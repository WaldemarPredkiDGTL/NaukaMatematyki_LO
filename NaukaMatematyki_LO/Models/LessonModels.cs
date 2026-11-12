using System.Globalization;
using System.Text.Json;

namespace NaukaMatematyki_LO;

internal sealed class LessonDocument
{
    public string Title { get; set; } = "";
    public string Headline { get; set; } = "";
    public string CheckQuestion { get; set; } = "";
    public string CheckAnswer { get; set; } = "";
    public string RawText { get; set; } = "";
    public List<LessonBlock> Blocks { get; set; } = [];

    public static LessonDocument Parse(string raw)
    {
        var lesson = new LessonDocument { RawText = raw ?? "" };
        var json = ExtractJson(raw);
        if (json is null)
        {
            lesson.Title = "Odpowiedź";
            lesson.Blocks.Add(new LessonBlock { Type = "text", Content = (raw ?? "").Trim() });
            return lesson;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                lesson.Title = "Lekcja";
                foreach (var item in root.EnumerateArray())
                    lesson.Blocks.Add(LessonBlock.From(item));
                return lesson;
            }

            lesson.Title = ReadString(root, "title") ?? "Lekcja";
            lesson.Headline = ReadString(root, "headline") ?? "";
            lesson.CheckQuestion = ReadString(root, "checkQuestion", "check_question") ?? "";
            lesson.CheckAnswer = ReadString(root, "checkAnswer", "check_answer") ?? "";

            if (root.TryGetProperty("blocks", out var blocks) && blocks.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in blocks.EnumerateArray())
                    lesson.Blocks.Add(LessonBlock.From(item));
            }

            if (lesson.Blocks.Count == 0)
                lesson.Blocks.Add(new LessonBlock { Type = "text", Content = json });
        }
        catch (JsonException)
        {
            lesson.Title = "Odpowiedź";
            lesson.Blocks.Clear();
            lesson.Blocks.Add(new LessonBlock { Type = "text", Content = raw.Trim() });
        }

        return lesson;
    }

    public string ToContextText()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(Title)) parts.Add(Title);
        if (!string.IsNullOrWhiteSpace(Headline)) parts.Add(Headline);
        foreach (var block in Blocks)
        {
            var bit = block.Type switch
            {
                "formula" => block.Display ?? block.Latex,
                "steps" => block.Items is null ? null : string.Join(" ", block.Items),
                "graph" => block.Graph?.Title ?? block.Caption,
                _ => block.Content
            };
            if (!string.IsNullOrWhiteSpace(bit)) parts.Add(bit.Trim());
        }
        if (!string.IsNullOrWhiteSpace(CheckQuestion)) parts.Add("Pytanie: " + CheckQuestion);
        var text = string.Join("\n", parts);
        return text.Length <= 3500 ? text : text[..3500];
    }

    private static string? ExtractJson(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        return raw[start..(end + 1)];
    }

    internal static string? ReadString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value)) continue;
            if (value.ValueKind == JsonValueKind.String) return value.GetString();
            if (value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                return value.ToString();
        }
        return null;
    }

    internal static double? ReadDouble(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value)) continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return number;
            if (value.ValueKind == JsonValueKind.String &&
                double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
        }
        return null;
    }
}

internal sealed class LessonBlock
{
    public string Type { get; set; } = "text";
    public string? Content { get; set; }
    public string? Latex { get; set; }
    public string? Display { get; set; }
    public string? Caption { get; set; }
    public List<string>? Items { get; set; }
    public GraphSpec? Graph { get; set; }

    public static LessonBlock From(JsonElement element)
    {
        var type = (LessonDocument.ReadString(element, "type") ?? "text").Trim().ToLowerInvariant();
        var block = new LessonBlock
        {
            Type = type,
            Content = LessonDocument.ReadString(element, "content", "text"),
            Latex = LessonDocument.ReadString(element, "latex"),
            Display = LessonDocument.ReadString(element, "display", "plain"),
            Caption = LessonDocument.ReadString(element, "caption")
        };

        if (element.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            block.Items = items.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : item.ToString())
                .Where(item => item.Length > 0)
                .ToList();
        }

        if (type is "graph" or "wykres")
        {
            block.Type = "graph";
            block.Graph = element.TryGetProperty("graph", out var nested) && nested.ValueKind == JsonValueKind.Object
                ? GraphSpec.From(nested)
                : GraphSpec.From(element);
        }

        return block;
    }
}

internal sealed class GraphSpec
{
    public string Title { get; set; } = "";
    public string XLabel { get; set; } = "x";
    public string YLabel { get; set; } = "y";
    public double? XMin { get; set; }
    public double? XMax { get; set; }
    public double? YMin { get; set; }
    public double? YMax { get; set; }
    public List<GraphSeries> Series { get; set; } = [];
    public List<GraphPoint> Points { get; set; } = [];
    public List<double> VerticalLines { get; set; } = [];

    public static GraphSpec From(JsonElement element)
    {
        var spec = new GraphSpec
        {
            Title = LessonDocument.ReadString(element, "title", "caption") ?? "",
            XLabel = LessonDocument.ReadString(element, "xLabel", "xlabel") ?? "x",
            YLabel = LessonDocument.ReadString(element, "yLabel", "ylabel") ?? "y",
            XMin = LessonDocument.ReadDouble(element, "xMin", "xmin"),
            XMax = LessonDocument.ReadDouble(element, "xMax", "xmax"),
            YMin = LessonDocument.ReadDouble(element, "yMin", "ymin"),
            YMax = LessonDocument.ReadDouble(element, "yMax", "ymax")
        };

        if (element.TryGetProperty("series", out var series) && series.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in series.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    spec.Series.Add(new GraphSeries { Formula = item.GetString() ?? "", Label = "f(x)" });
                }
                else if (item.ValueKind == JsonValueKind.Object)
                {
                    spec.Series.Add(new GraphSeries
                    {
                        Formula = LessonDocument.ReadString(item, "formula", "expression", "f") ?? "",
                        Label = LessonDocument.ReadString(item, "label", "name") ?? $"f{index + 1}",
                        ColorHex = LessonDocument.ReadString(item, "color")
                    });
                }
                index++;
            }
        }

        var single = LessonDocument.ReadString(element, "formula", "expression");
        if (spec.Series.Count == 0 && !string.IsNullOrWhiteSpace(single))
            spec.Series.Add(new GraphSeries { Formula = single, Label = spec.YLabel });

        if (element.TryGetProperty("points", out var points) && points.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in points.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var x = LessonDocument.ReadDouble(item, "x");
                var y = LessonDocument.ReadDouble(item, "y");
                if (x is null || y is null) continue;
                spec.Points.Add(new GraphPoint
                {
                    X = x.Value,
                    Y = y.Value,
                    Label = LessonDocument.ReadString(item, "label") ?? ""
                });
            }
        }

        if (element.TryGetProperty("vlines", out var lines) && lines.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in lines.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out var value))
                    spec.VerticalLines.Add(value);
            }
        }

        return spec;
    }
}

internal sealed class GraphSeries
{
    public string Formula { get; set; } = "";
    public string Label { get; set; } = "";
    public string? ColorHex { get; set; }
}

internal sealed class GraphPoint
{
    public double X { get; set; }
    public double Y { get; set; }
    public string Label { get; set; } = "";
}

internal sealed class StudyTurn
{
    public string Question { get; set; } = "";
    public string Subject { get; set; } = "Matematyka";
    public string Level { get; set; } = "";
    public DateTime At { get; set; } = DateTime.Now;
    public string RawLesson { get; set; } = "";
    public LessonDocument? Lesson { get; set; }
    public List<Image>? Images { get; set; }
}
