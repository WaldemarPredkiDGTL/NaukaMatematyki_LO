using System.Globalization;

namespace NaukaMatematyki_LO;

internal static class SafeExpression
{
    public static Func<double, double> Compile(string expression)
    {
        var tokens = Tokenize(Normalize(expression));
        var rpn = ToRpn(tokens);
        return x => Evaluate(rpn, x);
    }

    private static string Normalize(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new FormatException("Pusty wzór.");

        var text = expression.Trim()
            .Replace("π", "pi", StringComparison.Ordinal)
            .Replace("−", "-", StringComparison.Ordinal)
            .Replace("–", "-", StringComparison.Ordinal)
            .Replace("·", "*", StringComparison.Ordinal)
            .Replace("×", "*", StringComparison.Ordinal)
            .Replace("²", "^2", StringComparison.Ordinal)
            .Replace("³", "^3", StringComparison.Ordinal)
            .Replace("**", "^", StringComparison.Ordinal);

        var chars = new List<char>(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == ',' && i > 0 && i < text.Length - 1 && char.IsDigit(text[i - 1]) && char.IsDigit(text[i + 1]))
                chars.Add('.');
            else
                chars.Add(text[i]);
        }
        return new string(chars.ToArray());
    }

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        for (var i = 0; i < text.Length;)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (char.IsDigit(c) || (c == '.' && i + 1 < text.Length && char.IsDigit(text[i + 1])))
            {
                var start = i++;
                while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.')) i++;
                if (!double.TryParse(text[start..i], NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    throw new FormatException("Nieczytelna liczba w wykresie.");
                tokens.Add(Token.Number(number));
                continue;
            }

            if (char.IsLetter(c))
            {
                var start = i++;
                while (i < text.Length && char.IsLetter(text[i])) i++;
                tokens.Add(Token.Name(text[start..i].ToLowerInvariant()));
                continue;
            }

            if ("+-*/^(),".Contains(c))
            {
                tokens.Add(Token.Symbol(c.ToString()));
                i++;
                continue;
            }

            throw new FormatException($"Niedozwolony znak we wzorze wykresu: {c}");
        }

        InsertImplicitMultiply(tokens);
        return tokens;
    }

    private static void InsertImplicitMultiply(List<Token> tokens)
    {
        for (var i = 0; i < tokens.Count - 1; i++)
        {
            var left = tokens[i];
            var right = tokens[i + 1];
            var leftValue = left.Kind is TokenKind.Number or TokenKind.Variable || left.Text == ")";
            var rightValue = right.Kind is TokenKind.Number or TokenKind.Variable or TokenKind.Function || right.Text == "(";
            if (leftValue && rightValue)
            {
                tokens.Insert(i + 1, Token.Symbol("*"));
                i++;
            }
        }
    }

    private static List<Token> ToRpn(List<Token> tokens)
    {
        var output = new List<Token>();
        var ops = new Stack<Token>();
        var previous = "";

        foreach (var token in tokens)
        {
            switch (token.Kind)
            {
                case TokenKind.Number:
                case TokenKind.Variable:
                    output.Add(token);
                    previous = "value";
                    break;
                case TokenKind.Function:
                    ops.Push(token);
                    previous = "func";
                    break;
                case TokenKind.Operator when token.Text == "(":
                    ops.Push(token);
                    previous = "(";
                    break;
                case TokenKind.Operator when token.Text == ",":
                    while (ops.Count > 0 && ops.Peek().Text != "(") output.Add(ops.Pop());
                    previous = ",";
                    break;
                case TokenKind.Operator when token.Text == ")":
                    while (ops.Count > 0 && ops.Peek().Text != "(") output.Add(ops.Pop());
                    if (ops.Count == 0) throw new FormatException("Brak nawiasu w wykresie.");
                    ops.Pop();
                    if (ops.Count > 0 && ops.Peek().Kind == TokenKind.Function) output.Add(ops.Pop());
                    previous = ")";
                    break;
                case TokenKind.Operator:
                    var op = token.Text;
                    var unary = op is "+" or "-" && previous is "" or "(" or "," or "+" or "-" or "*" or "/" or "^" or "u-";
                    if (unary && op == "+")
                    {
                        previous = "+";
                        break;
                    }
                    if (unary) op = "u-";
                    while (ops.Count > 0 && ops.Peek().Kind == TokenKind.Operator && ops.Peek().Text != "(" &&
                           (Precedence(ops.Peek().Text) > Precedence(op) ||
                            (Precedence(ops.Peek().Text) == Precedence(op) && op is not "^" and not "u-")))
                        output.Add(ops.Pop());
                    ops.Push(Token.Symbol(op));
                    previous = op;
                    break;
            }
        }

        while (ops.Count > 0)
        {
            var op = ops.Pop();
            if (op.Text is "(" or ")") throw new FormatException("Brak nawiasu w wykresie.");
            output.Add(op);
        }
        return output;
    }

    private static int Precedence(string op) => op switch
    {
        "u-" => 4,
        "^" => 5,
        "*" or "/" => 3,
        "+" or "-" => 2,
        _ => 0
    };

    private static double Evaluate(List<Token> rpn, double x)
    {
        var stack = new Stack<double>();
        foreach (var token in rpn)
        {
            switch (token.Kind)
            {
                case TokenKind.Number:
                    stack.Push(token.Value);
                    break;
                case TokenKind.Variable:
                    stack.Push(x);
                    break;
                case TokenKind.Function:
                    if (stack.Count < 1) throw new FormatException("Zła funkcja w wykresie.");
                    stack.Push(Apply(token.Text, stack.Pop()));
                    break;
                default:
                    if (token.Text == "u-")
                    {
                        if (stack.Count < 1) throw new FormatException("Zły znak w wykresie.");
                        stack.Push(-stack.Pop());
                        break;
                    }
                    if (stack.Count < 2) throw new FormatException("Zły wzór wykresu.");
                    var b = stack.Pop();
                    var a = stack.Pop();
                    stack.Push(token.Text switch
                    {
                        "+" => a + b,
                        "-" => a - b,
                        "*" => a * b,
                        "/" => b == 0 ? double.NaN : a / b,
                        "^" => Math.Pow(a, b),
                        _ => throw new FormatException("Nieznany operator wykresu.")
                    });
                    break;
            }
        }
        if (stack.Count != 1) throw new FormatException("Zły wzór wykresu.");
        return stack.Pop();
    }

    private static double Apply(string name, double value) => name switch
    {
        "sin" => Math.Sin(value),
        "cos" => Math.Cos(value),
        "tan" => Math.Tan(value),
        "cot" => 1.0 / Math.Tan(value),
        "asin" => Math.Asin(value),
        "acos" => Math.Acos(value),
        "atan" => Math.Atan(value),
        "sqrt" => value < 0 ? double.NaN : Math.Sqrt(value),
        "abs" => Math.Abs(value),
        "ln" => value <= 0 ? double.NaN : Math.Log(value),
        "log" => value <= 0 ? double.NaN : Math.Log10(value),
        "exp" => Math.Exp(value),
        "floor" => Math.Floor(value),
        "ceil" => Math.Ceiling(value),
        _ => throw new FormatException($"Nieznana funkcja: {name}")
    };

    private enum TokenKind { Number, Name, Variable, Function, Operator }

    private readonly record struct Token(TokenKind Kind, string Text, double Value)
    {
        public static Token Number(double value) => new(TokenKind.Number, "", value);
        public static Token Symbol(string text) => new(TokenKind.Operator, text, 0);
        public static Token Name(string text)
        {
            if (text is "x" or "t") return new Token(TokenKind.Variable, text, 0);
            if (text is "pi") return Number(Math.PI);
            if (text is "e") return Number(Math.E);
            if (text is "sin" or "cos" or "tan" or "cot" or "asin" or "acos" or "atan" or "sqrt" or "abs" or "ln" or "log" or "exp" or "floor" or "ceil")
                return new Token(TokenKind.Function, text, 0);
            throw new FormatException($"Nieznany symbol we wzorze: {text}");
        }
    }
}
