using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NaukaMatematyki_LO;

internal sealed class StudyPlan
{
    public string Kind { get; set; } = "pracownia-plan";
    public int Version { get; set; } = 1;
    public string Title { get; set; } = "";
    public string Subject { get; set; } = "Matematyka";
    public string Level { get; set; } = "Rozszerzony";
    public string Author { get; set; } = "";
    public string Note { get; set; } = "";
    public List<StudyPlanItem> Items { get; set; } = [];

    public static StudyPlan Parse(string json)
    {
        StudyPlan? plan;
        try
        {
            plan = JsonSerializer.Deserialize<StudyPlan>(json, Json);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("To nie jest plik planu Pracowni. Zapisz go w programie przyciskiem „Zapisz plik”.");
        }

        if (plan is null || plan.Kind != "pracownia-plan")
            throw new InvalidOperationException("To nie jest plik planu Pracowni. Zapisz go w programie przyciskiem „Zapisz plik”.");

        plan.Title = Clean(plan.Title, 120);
        plan.Subject = Clean(plan.Subject, 40);
        plan.Level = Clean(plan.Level, 40);
        plan.Author = Clean(plan.Author, 80);
        plan.Note = Clean(plan.Note, 500);
        if (plan.Title.Length == 0) plan.Title = "Plan";
        plan.Items = (plan.Items ?? [])
            .Select(item => new StudyPlanItem
            {
                Title = Clean(item.Title, 120),
                Prompt = Clean(item.Prompt, 2000),
                Lesson = CleanLesson(item.Lesson)
            })
            .Where(item => item.Prompt.Length > 0)
            .Take(40)
            .ToList();
        foreach (var item in plan.Items)
        {
            if (item.Title.Length == 0) item.Title = "Zagadnienie";
        }
        if (plan.Items.Count == 0)
            throw new InvalidOperationException("W planie nie ma żadnego zagadnienia z poleceniem.");
        return plan;
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public StudyPlan Copy()
    {
        var copy = Parse(ToJson());
        return copy;
    }

    private static string Clean(string? value, int max)
    {
        var text = (value ?? "").Trim();
        return text.Length <= max ? text : text[..max];
    }

    private static string? CleanLesson(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0 || text.Length > 32000 || !text.Contains('{')) return null;
        return text;
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}

internal sealed class StudyPlanItem
{
    public string Title { get; set; } = "";
    public string Prompt { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Lesson { get; set; }

    [JsonIgnore]
    public bool HasLesson => !string.IsNullOrWhiteSpace(Lesson);
}

internal static class PlanLessonText
{
    public static string Question(string planTitle, string author, string note, int index, int count, string prompt)
    {
        var who = string.IsNullOrWhiteSpace(author) ? "" : $" Plan ułożył {author}.";
        var extra = string.IsNullOrWhiteSpace(note) ? "" : $" Notatka do całego planu: {note}.";
        return $"To jest zagadnienie {index} z {count} z planu „{planTitle}”.{who}{extra} " +
            $"Polecenie: {prompt} " +
            "Zrób z tego pełną lekcję. Zostań przy tym zagadnieniu i nie przechodź do następnego.";
    }

    public static string Shown(string planTitle, string subject, int index, int count, string title, string prompt)
        => $"Plan „{planTitle}” · {subject}\nZagadnienie {index} z {count}: {title}\n{prompt}";
}
