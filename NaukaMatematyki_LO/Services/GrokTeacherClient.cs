using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace NaukaMatematyki_LO;

internal sealed class GrokTeacherClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(3) };

    public async Task<string> AskAsync(
        string apiKey,
        string model,
        string subject,
        string level,
        IReadOnlyList<(string Role, string Text)> history,
        string question,
        IReadOnlyList<byte[]> images,
        CancellationToken cancellationToken,
        bool guided = false)
    {
        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = SystemPrompt(subject, level, history.Count > 0, guided) }
        };
        foreach (var turn in history.TakeLast(8))
        {
            messages.Add(new JsonObject { ["role"] = turn.Role, ["content"] = turn.Text });
        }

        if (images.Count == 0)
        {
            messages.Add(new JsonObject { ["role"] = "user", ["content"] = question });
        }
        else
        {
            var content = new JsonArray
            {
                new JsonObject { ["type"] = "text", ["text"] = question }
            };
            foreach (var image in images.Take(4))
            {
                content.Add(new JsonObject
                {
                    ["type"] = "image_url",
                    ["image_url"] = new JsonObject { ["url"] = "data:image/png;base64," + Convert.ToBase64String(image) }
                });
            }
            messages.Add(new JsonObject { ["role"] = "user", ["content"] = content });
        }

        var payload = new JsonObject
        {
            ["model"] = string.IsNullOrWhiteSpace(model) ? "grok-4" : model.Trim(),
            ["temperature"] = 0.3,
            ["max_tokens"] = 8000,
            ["messages"] = messages
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.x.ai/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(ReadError(body) ?? $"xAI zwróciło {(int)response.StatusCode}.");

        var root = JsonNode.Parse(body) as JsonObject
            ?? throw new InvalidOperationException("Pusta odpowiedź xAI.");
        var contentNode = root["choices"]?[0]?["message"]?["content"];
        var text = ReadContent(contentNode);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Model nie zwrócił treści. Spróbuj modelu grok-4 albo zwiększ limit odpowiedzi.");
        return text;
    }

    public async Task<string> PolishSpeechAsync(
        string apiKey,
        string model,
        string subject,
        string transcript,
        CancellationToken cancellationToken)
    {
        var messages = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "system",
                ["content"] = "Poprawiasz polską transkrypcję mowy ucznia liceum. Przedmiot: " + subject + ". " +
                    "Zwróć wyłącznie poprawione pytanie, bez cudzysłowu, komentarza i wyjaśnienia. " +
                    "Zachowaj sens, liczby, symbole i nazwy. Popraw powtórzenia, urwane słowa i błędy rozpoznawania. " +
                    "Nie dodawaj odpowiedzi ani treści, której uczeń nie powiedział."
            },
            new JsonObject { ["role"] = "user", ["content"] = transcript }
        };
        var payload = new JsonObject
        {
            ["model"] = string.IsNullOrWhiteSpace(model) ? "grok-4" : model.Trim(),
            ["temperature"] = 0,
            ["max_tokens"] = 120,
            ["messages"] = messages
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.x.ai/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(ReadError(body) ?? $"xAI zwróciło {(int)response.StatusCode}.");

        var root = JsonNode.Parse(body) as JsonObject
            ?? throw new InvalidOperationException("Pusta odpowiedź xAI.");
        return ReadContent(root["choices"]?[0]?["message"]?["content"]).Trim().Trim('"', '„', '”');
    }

    public async Task<byte[]> SpeakAsync(string apiKey, string text, CancellationToken cancellationToken)
    {
        var spoken = text.Trim();
        if (spoken.Length > 400) spoken = spoken[..400];
        var payload = new JsonObject
        {
            ["text"] = spoken,
            ["voice_id"] = "eve",
            ["language"] = "en",
            ["speed"] = 0.85
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.x.ai/v1/tts");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, cancellationToken);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = Encoding.UTF8.GetString(bytes);
            throw new InvalidOperationException(ReadError(body) ?? $"Wymowa nie weszła ({(int)response.StatusCode}).");
        }
        if (bytes.Length < 80)
            throw new InvalidOperationException("Wymowa wróciła pusta.");
        return bytes;
    }

    private static string? ReadError(string body)
    {
        try
        {
            var root = JsonNode.Parse(body) as JsonObject;
            return root?["error"]?["message"]?.ToString();
        }
        catch (Exception)
        {
            return body.Length > 280 ? body[..280] : body;
        }
    }

    private static string ReadContent(JsonNode? node)
    {
        if (node is null) return "";
        if (node is JsonValue value && value.TryGetValue<string>(out var text)) return text;
        if (node is JsonArray array)
        {
            var builder = new StringBuilder();
            foreach (var part in array)
            {
                var piece = part?["text"]?.ToString() ?? part?["content"]?.ToString();
                if (!string.IsNullOrWhiteSpace(piece)) builder.AppendLine(piece);
            }
            return builder.ToString();
        }
        return node.ToString();
    }

    private static string SystemPrompt(string subject, string level, bool inLesson, bool guided)
    {
        var pace = inLesson
            ? "Na tablicy jest już lekcja. Dopytanie o pokazany fragment zrób krótko. Prośba o kontynuację, dalszy krok albo trudniejsze zadanie jest pełną lekcją i podnosi poprzeczkę."
            : "To początek. Ułóż pełną lekcję i skończ zadaniem, które zaprasza do trudniejszego kroku.";
        var guide = guided
            ? "Lekcja na tablicy została przejrzana przy przygotowaniu zagadnienia. Trzymaj się jej pojęć, przykładu i kolejności, dopóki uczeń idzie tym tropem. Gdy pyta o coś obok, utknie inaczej albo chce iść dalej, idź za uczniem."
            : "";
        return $$$"""
            Jesteś cierpliwym nauczycielem {{{subject}}} w liceum ogólnokształcącym. {{{LevelLine(level)}}}
            Uczeń pisze, mówi albo wkleja zdjęcie. Najpierw odczytaj treść, potem ucz. Po polsku, krótko, konkretnie, bez protekcjonalności.
            Odpowiadaj WYŁĄCZNIE jednym obiektem JSON, bez markdown i bez tekstu przed ani po nim.
            {
              "title": "krótki tytuł",
              "headline": "jedno zdanie, o co chodzi",
              "blocks": [
                {"type":"text","content":"wyjaśnienie, 2-5 zdań"},
                {"type":"formula","latex":"\\frac{-b \\pm \\sqrt{b^{2}-4ac}}{2a}","display":"(-b ± sqrt(b^2-4ac)) / 2a","units":"","caption":"po co ten wzór"},
                {"type":"steps","items":["Krok 1","Krok 2"]},
                {"type":"callout","content":"na co uważać"}
              ],
              "checkQuestion": "opcjonalne",
              "checkAnswer": "opcjonalne, z uzasadnieniem, nie sam wynik",
              "solution": "opcjonalne: dane, rozumowanie, wynik i kontrola",
              "nextTask": "opcjonalne: jedno zadanie, trochę trudniejsze, bez rozwiązania"
            }
            Pełna lekcja: blocks, solution, checkQuestion, checkAnswer i jedno nextTask.
            W nextTask daj jedno zadanie o krok trudniejsze, bez rozwiązania, i nazwij w nim nowe pojęcie albo nowy przypadek.
            Krótka odpowiedź dotyczy tylko dopytania o fragment, który już jest na tablicy: title, headline i blocks. Pomiń solution, checkQuestion, checkAnswer i nextTask.
            Kontynuacja: nie powtarzaj poprzedniej lekcji. Dodaj jedną nową definicję, jedno wyzwanie trudniejsze niż poprzedni krok i w nextTask jedno zadanie jeszcze bardziej złożone. Trudność rośnie przez nowy warunek, nowy symbol albo połączenie dwóch pojęć.
            {{{pace}}}
            {{{guide}}}
            {{{SubjectGuide(subject)}}}
            Pytanie spoza tego przedmiotu zamknij w jednym zdaniu i zaproponuj powrót do lekcji.
            """;
    }

    private static string LevelLine(string level) => level switch
    {
        "Podstawowy" => "Poziom podstawowy: typowe przykłady i wymagania z podstawy.",
        "Rozszerzony" => "Poziom rozszerzony: więcej samodzielności, analiza i trudniejsze zadanie.",
        _ => "Dopasuj trudność do pytania. Nie pytaj, jaki wybrać poziom."
    };

    private static string SubjectGuide(string subject) => subject switch
    {
        "Fizyka" => "Podawaj jednostki i rozróżniaj wielkość od wartości.\n" + UnitsGuide + "\n" + GraphGuide,
        "Chemia" => "Chemia szkolna: bilans równań, mole, stężenia i pH. We wzorach chemicznych używaj indeksów dolnych.\n" + UnitsGuide + "\nWykres dodawaj tylko przy zależności liczbowej.\n" + GraphGuide,
        "Język polski" => "Opieraj się na podstawie programowej LO: odbiór i tworzenie wypowiedzi, retoryka, kultura języka, literatura. Rozróżniaj tezę, argument, przykład z tekstu, wniosek i środki stylistyczne. Nie zmyślaj cytatów. Nie dodawaj wykresu.",
        "Język angielski" => "Opieraj się na podstawie programowej LO: rozumienie tekstu, mediacja, gramatyka, słownictwo, wymowa i wypowiedź pisemna. Przykład podawaj po angielsku, wyjaśnienie krótko po polsku. Zaznacz rejestr: neutralny, formalny albo nieformalny. Nie dodawaj wykresu.\nGdy uczeń ma usłyszeć słowo, parę albo krótkie zdanie, dodaj blok {\"type\":\"pronounce\",\"say\":\"ship\",\"content\":\"ship — szyp, krótkie i.\"}. say jest tylko po angielsku, bez opisu. content mówi po polsku, co słychać. Najwyżej dwa takie bloki, a przy pytaniu o wymowę do czterech.",
        _ => "Ucz szkolnej matematyki: po co to jest, przykład liczbowy, potem ćwiczenie. Wzór podawaj w latex i w display, ułamkami, pierwiastkami i potęgami. Przy czystej matematyce units zostaw puste.\n" + GraphGuide
    };

    private const string UnitsGuide =
        "Wzór podawaj w latex i w display. Jednostkę wpisz w units, osobno od wzoru: m/s, km/h, N, kg·m/s², mol/dm³, J, °C. Gdy wzór nie ma jednostki, units zostaw puste.";

    private const string GraphGuide =
        """Wykres dodaj tylko wtedy, gdy zależność da się narysować. Blok: {"type":"graph","title":"nazwa","xLabel":"x","yLabel":"y","series":[{"formula":"2*x+1","label":"f(x)"}],"points":[{"x":0,"y":1,"label":"punkt"}]}. W formula używaj wyłącznie zmiennej x, liczb z kropką, + - * / ^, nawiasów, pi, e oraz sin cos tan cot sqrt abs ln log exp. Mnożenie zapisuj gwiazdką, bez LaTeX-a. Czas i inne wielkości też licz jako x, a nazwę osi wpisz w xLabel i yLabel, na przykład xLabel „t (s)”.""";

    public void Dispose() => _http.Dispose();
}
