using System.Runtime.InteropServices;
using System.Text.Json;

namespace NaukaMatematyki_LO;

internal sealed class UserSettings
{
    public string Model { get; set; } = "grok-4";
    public string Subject { get; set; } = "Matematyka";
    public string Level { get; set; } = "Rozszerzony";
    public string ProtectedKey { get; set; } = "";
}

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NaukaMatematyki_LO");
    private static string SettingsPath => Path.Combine(Dir, "settings.json");
    private static string HistoryPath => Path.Combine(Dir, "history.json");

    public static UserSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new UserSettings();
            return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(SettingsPath), Json) ?? new UserSettings();
        }
        catch (Exception)
        {
            return new UserSettings();
        }
    }

    public static void Save(UserSettings settings)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, Json));
    }

    public static string Unprotect(string protectedKey)
    {
        if (string.IsNullOrWhiteSpace(protectedKey)) return "";
        var bytes = Convert.FromBase64String(protectedKey);
        return System.Text.Encoding.UTF8.GetString(UnprotectBytes(bytes));
    }

    public static string Protect(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        return Convert.ToBase64String(ProtectBytes(System.Text.Encoding.UTF8.GetBytes(key)));
    }

    public static List<StudyTurn> LoadHistory()
    {
        try
        {
            if (!File.Exists(HistoryPath)) return [];
            var stored = JsonSerializer.Deserialize<List<StoredTurn>>(File.ReadAllText(HistoryPath), Json) ?? [];
            return stored.Select(item => new StudyTurn
            {
                Question = item.Question,
                Subject = item.Subject,
                Level = item.Level,
                At = item.At,
                RawLesson = item.RawLesson,
                Prepared = item.Prepared,
                Lesson = LessonDocument.Parse(item.RawLesson)
            }).ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    public static void SaveHistory(IEnumerable<StudyTurn> turns)
    {
        var stored = turns.TakeLast(40).Select(turn => new StoredTurn
        {
            Question = turn.Question,
            Subject = turn.Subject,
            Level = turn.Level,
            At = turn.At,
            RawLesson = turn.RawLesson,
            Prepared = turn.Prepared
        }).ToList();
        Directory.CreateDirectory(Dir);
        File.WriteAllText(HistoryPath, JsonSerializer.Serialize(stored, Json));
    }

    public static StudyPlan? LoadPlan()
    {
        try
        {
            var path = Path.Combine(Dir, "plan.json");
            if (!File.Exists(path)) return null;
            return StudyPlan.Parse(File.ReadAllText(path));
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void SavePlan(StudyPlan? plan)
    {
        var path = Path.Combine(Dir, "plan.json");
        if (plan is null)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }
        Directory.CreateDirectory(Dir);
        File.WriteAllText(path, plan.ToJson());
    }

    private static byte[] ProtectBytes(byte[] data)
    {
        var input = Blob(data);
        var output = new DataBlob();
        try
        {
            if (!CryptProtectData(ref input, "NaukaMatematyki_LO", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, ref output))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[output.cbData];
            Marshal.Copy(output.pbData, result, 0, output.cbData);
            return result;
        }
        finally
        {
            if (input.pbData != IntPtr.Zero) Marshal.FreeHGlobal(input.pbData);
            if (output.pbData != IntPtr.Zero) LocalFree(output.pbData);
        }
    }

    private static byte[] UnprotectBytes(byte[] data)
    {
        var input = Blob(data);
        var output = new DataBlob();
        try
        {
            if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, ref output))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[output.cbData];
            Marshal.Copy(output.pbData, result, 0, output.cbData);
            return result;
        }
        finally
        {
            if (input.pbData != IntPtr.Zero) Marshal.FreeHGlobal(input.pbData);
            if (output.pbData != IntPtr.Zero) LocalFree(output.pbData);
        }
    }

    private static DataBlob Blob(byte[] data)
    {
        var pointer = Marshal.AllocHGlobal(data.Length);
        Marshal.Copy(data, 0, pointer, data.Length);
        return new DataBlob { cbData = data.Length, pbData = pointer };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CryptProtectData(ref DataBlob dataIn, string description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob dataOut);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);

    private sealed class StoredTurn
    {
        public string Question { get; set; } = "";
        public string Subject { get; set; } = "";
        public string Level { get; set; } = "";
        public DateTime At { get; set; }
        public string RawLesson { get; set; } = "";
        public bool Prepared { get; set; }
    }
}
