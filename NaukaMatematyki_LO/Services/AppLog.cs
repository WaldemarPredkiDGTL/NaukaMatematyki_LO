namespace NaukaMatematyki_LO;

internal static class AppLog
{
    private static readonly object Gate = new();

    public static string Path { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NaukaMatematyki_LO",
        "problemy.log");

    public static void Error(string source, Exception exception) =>
        Write("BŁĄD", source, exception.GetType().Name + ": " + exception.Message + Environment.NewLine + exception);

    public static void Problem(string source, string message) => Write("PROBLEM", source, message);

    public static void Write(string level, string source, string message)
    {
        try
        {
            var directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {source}: {message.ReplaceLineEndings(" | ")}{Environment.NewLine}";
            lock (Gate) File.AppendAllText(Path, line);
        }
        catch (Exception)
        {
        }
    }
}
