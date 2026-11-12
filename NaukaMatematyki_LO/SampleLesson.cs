namespace NaukaMatematyki_LO;

internal static class SampleLesson
{
    public static LessonDocument Create() => new()
    {
        Title = "Tablica jest gotowa",
        Headline = "Wpisz klucz xAI, a Grok poprowadzi lekcję. Na start — funkcja kwadratowa, tak jak na sprawdzianie.",
        CheckQuestion = "Ile miejsc zerowych ma f(x) = x² − 4x + 3?",
        CheckAnswer = "Dwa: x = 1 i x = 3. Delta wynosi 4, więc parabola przecina oś OX w dwóch punktach.",
        Blocks =
        [
            new LessonBlock
            {
                Type = "text",
                Content = "Możesz zapytać tekstem, przytrzymać mikrofon albo wkleić zdjęcie zadania. Odpowiedź ułożę jako lekcję: najpierw sens, potem wzór, kroki i wykres. Fizyka korzysta z tej samej tablicy — zmieniasz przedmiot z lewej."
            },
            new LessonBlock
            {
                Type = "formula",
                Latex = @"\frac{-b \pm \sqrt{b^{2}-4ac}}{2a}",
                Display = "(−b ± √(b² − 4ac)) / 2a",
                Caption = "miejsca zerowe, gdy a ≠ 0"
            },
            new LessonBlock
            {
                Type = "steps",
                Items =
                [
                    "Dla f(x) = x² − 4x + 3 masz a = 1, b = −4, c = 3.",
                    "Delta = (−4)² − 4·1·3 = 4.",
                    "x₁ = (4 − 2) / 2 = 1, x₂ = (4 + 2) / 2 = 3.",
                    "Wierzchołek leży w połowie drogi, przy x = 2, y = −1."
                ]
            },
            new LessonBlock
            {
                Type = "graph",
                Caption = "Parabola otwiera się do góry i przecina oś OX w 1 oraz 3.",
                Graph = new GraphSpec
                {
                    Title = "f(x) = x² − 4x + 3",
                    XLabel = "x",
                    YLabel = "y",
                    XMin = -1,
                    XMax = 5,
                    Series = [new GraphSeries { Formula = "x^2-4*x+3", Label = "f(x)", ColorHex = "#E8B04A" }],
                    Points =
                    [
                        new GraphPoint { X = 1, Y = 0, Label = "x=1" },
                        new GraphPoint { X = 3, Y = 0, Label = "x=3" },
                        new GraphPoint { X = 2, Y = -1, Label = "wierzchołek" }
                    ]
                }
            },
            new LessonBlock
            {
                Type = "callout",
                Content = "Klucz API zostaje tylko na tym koncie Windows, zaszyfrowany. Do zdjęć zadań wybierz model Groka, który widzi obrazy."
            }
        ]
    };
}
