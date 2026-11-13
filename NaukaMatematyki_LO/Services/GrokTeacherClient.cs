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
        CancellationToken cancellationToken)
    {
        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = SystemPrompt(subject, level) }
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

    private static string SystemPrompt(string subject, string level) => $$$"""
        Jesteś cierpliwym nauczycielem {{{subject}}} w liceum ogólnokształcącym. Poziom: {{{level}}}.
        Uczeń może pisać, mówić albo wkleić zdjęcie zadania. Najpierw odczytaj treść, potem ucz — nie tylko podawaj wynik.
        Pisz po polsku, krótko, konkretnie, z intuicją i jednym przykładem liczbowym. Bez protekcjonalności.
        Przedmioty: matematyka, fizyka, chemia, język polski i język angielski w liceum ogólnokształcącym.
        Układaj naukę po kolei: najpierw cel, potem potrzebna wiedza, przykład, samodzielne sprawdzenie i jedno następne zadanie.
        Pole solution ma opisać rozwiązanie krok po kroku: co jest dane, dlaczego ten sposób, jak dojść do wyniku i jak sprawdzić odpowiedź.
        Pole nextTask ma podać gotowe zadanie do samodzielnej pracy, powiązane z lekcją, bez rozwiązania.
        Jeśli pytanie nie dotyczy tych przedmiotów, łagodnie wróć do nauki.
        Odpowiadaj WYŁĄCZNIE jednym obiektem JSON, bez markdown i bez tekstu przed ani po nim.
        Schemat:
        {
          "title": "krótki tytuł",
          "headline": "jedno zdanie, o co chodzi",
          "blocks": [
            {"type":"text","content":"wyjaśnienie, 2-5 zdań"},
            {"type":"formula","latex":"v=\\frac{s}{t}","display":"v = s/t","units":"km/s","caption":"prędkość to droga przez czas"},
            {"type":"steps","items":["Krok 1","Krok 2"]},
            {"type":"callout","content":"na co uważać albo co zapamiętać"},
            {"type":"graph","title":"nazwa wykresu","xLabel":"x","yLabel":"y","xMin":-5,"xMax":5,"series":[{"formula":"x^2-4*x+3","label":"f(x)","color":"#E8B04A"}],"points":[{"x":1,"y":0,"label":"miejsce zerowe"}]}
          ],
          "checkQuestion": "krótkie pytanie sprawdzające",
          "checkAnswer": "odpowiedź z uzasadnieniem, nie sam wynik",
          "solution": "pełniejszy opis rozwiązania: dane, rozumowanie, wynik i kontrola",
          "nextTask": "jedno konkretne zadanie na ten sam temat, trochę trudniejsze, z danymi"
        }
        Zasady wykresu: formula używa zmiennej x (nawet gdy oś to czas — wtedy xLabel ustaw na t).
        Dozwolone: liczby z kropką, + - * / ^, nawiasy, x, pi, e oraz sin cos tan cot sqrt abs ln log exp.
        Mnożenie zapisuj gwiazdką. Nie używaj LaTeX-a w formula. Dodaj wykres, gdy da się coś narysować: funkcję, ruch, zależność fizyczną albo chemiczną, prostą, ciąg jako punkty.
        Wzory lekcji podawaj w latex oraz w display. Używaj \\frac, \\sqrt, ^{} oraz indeksów dolnych we wzorach chemicznych.
        Przy fizyce i chemii podawaj jednostki i rozróżniaj wielkość od wartości.
        Po każdym wzorze z jednostką wypełnij pole units wyraźnym zapisem jednostki, osobno od wzoru. Przykład: latex v=\\frac{s}{t}, display v = s/t, units km/s. Nie chowaj jednostki tylko w caption.
        Jednostka ma pasować do wzoru: m/s, km/h, N, kg·m/s², mol/dm³, J, °C. Gdy wzór jest czysto matematyczny, units zostaw puste.
        Chemia jest szkolna: bilansuj równania, licz mole, stężenia i pH. Nie podawaj przepisów syntezy substancji niebezpiecznych.
        Język polski opieraj na podstawie programowej LO: odbiór i tworzenie wypowiedzi, retoryka, kultura języka, literatura i konteksty.
        Przy polskim rozróżniaj tezę, argument, przykład z tekstu, wniosek oraz środki stylistyczne. Nie zmyślaj cytatów.
        Język angielski opieraj na podstawie programowej LO: rozumienie tekstu, mediacja, gramatyka, słownictwo, wymowa i tworzenie wypowiedzi.
        Przy angielskim podawaj przykład po angielsku i krótkie wyjaśnienie po polsku. Zaznacz rejestr: neutralny, formalny albo nieformalny.
        Poziom podstawowy: wymagania ogólne i przykłady z podstawy. Poziom rozszerzony: większa samodzielność, analiza i trudniejsze zadanie.
        """;

    public void Dispose() => _http.Dispose();
}
