using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace NaukaMatematyki_LO;

internal static class DrivePlan
{
    private const int MaxBytes = 3 * 1024 * 1024;
    private static readonly Regex UrlPattern = new(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex IdPattern = new(@"(?:/file/d/|/document/d/|/spreadsheets/d/|/presentation/d/|[?&]id=)([a-zA-Z0-9_-]{10,})", RegexOptions.Compiled);

    public static string? FileId(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var url = UrlPattern.Match(text);
        if (!url.Success) return null;
        var raw = url.Value.TrimEnd('.', ',', ';', ')', ']', '>');
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return null;
        if (uri.Host is not ("drive.google.com" or "docs.google.com" or "drive.usercontent.google.com"))
            return null;
        var id = IdPattern.Match(uri.PathAndQuery);
        return id.Success ? id.Groups[1].Value : null;
    }

    public static async Task<StudyPlan> DownloadAsync(string text, CancellationToken cancellationToken)
    {
        var id = FileId(text) ?? throw new InvalidOperationException("Wklej link z Dysku Google, ten z uprawnieniem „Każdy, kto ma link”.");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PracowniaLO");
        var url = "https://drive.usercontent.google.com/download?id=" + WebUtility.UrlEncode(id) + "&export=download&confirm=t";
        using var response = await http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Dysk nie oddał pliku. Udostępnij go jako „Każdy, kto ma link”.");
        if (response.Content.Headers.ContentLength > MaxBytes)
            throw new InvalidOperationException("Plik z Dysku jest za duży na plan zagadnień.");

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length > MaxBytes)
            throw new InvalidOperationException("Plik z Dysku jest za duży na plan zagadnień.");
        var body = Encoding.UTF8.GetString(bytes).Trim().TrimStart('\uFEFF');
        if (body.StartsWith('<'))
            throw new InvalidOperationException("Dysk nie oddał pliku planu. Udostępnij go jako „Każdy, kto ma link”.");
        try
        {
            return StudyPlan.Parse(body);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException(ex.Message + " Na Dysku ma leżeć plik .pracownia.json.");
        }
    }
}
