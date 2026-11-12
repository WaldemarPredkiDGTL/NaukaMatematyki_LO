namespace NaukaMatematyki_LO;

internal static class Theme
{
    public static readonly Color Ink = Color.FromArgb(18, 24, 34);
    public static readonly Color InkDeep = Color.FromArgb(12, 16, 24);
    public static readonly Color Sidebar = Color.FromArgb(16, 21, 31);
    public static readonly Color Line = Color.FromArgb(42, 53, 70);
    public static readonly Color Paper = Color.FromArgb(251, 247, 240);
    public static readonly Color PaperAlt = Color.FromArgb(244, 236, 223);
    public static readonly Color InkText = Color.FromArgb(28, 36, 48);
    public static readonly Color Muted = Color.FromArgb(96, 108, 124);
    public static readonly Color Copper = Color.FromArgb(212, 146, 58);
    public static readonly Color CopperDeep = Color.FromArgb(166, 104, 32);
    public static readonly Color Sage = Color.FromArgb(61, 158, 122);
    public static readonly Color Sky = Color.FromArgb(90, 156, 214);
    public static readonly Color Rose = Color.FromArgb(214, 112, 122);
    public static readonly Color Chalk = Color.FromArgb(244, 231, 200);
    public static readonly Color Formula = Color.FromArgb(22, 28, 40);

    public static readonly Color[] Series =
    [
        Color.FromArgb(232, 176, 74),
        Color.FromArgb(72, 184, 146),
        Color.FromArgb(126, 182, 255),
        Color.FromArgb(232, 122, 138),
        Color.FromArgb(186, 148, 232)
    ];

    public static readonly Font Title = new("Segoe UI", 18f, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font Body = new("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font BodyBold = new("Segoe UI", 11f, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font Small = new("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font SmallBold = new("Segoe UI", 9f, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font Tiny = new("Segoe UI", 8f, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font TinyBold = new("Segoe UI", 8f, FontStyle.Bold, GraphicsUnit.Point);

    private static readonly Dictionary<(int Size, FontStyle Style), Font> MathFonts = [];

    public static Font Math(float size, FontStyle style = FontStyle.Regular)
    {
        var key = ((int)System.Math.Round(size * 2), style);
        if (MathFonts.TryGetValue(key, out var cached)) return cached;
        Font font;
        try
        {
            font = new Font("Cambria Math", key.Item1 / 2f, style, GraphicsUnit.Point);
        }
        catch (ArgumentException)
        {
            font = new Font("Cambria", key.Item1 / 2f, style, GraphicsUnit.Point);
        }
        MathFonts[key] = font;
        return font;
    }
}
