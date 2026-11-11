using System.IO;
using System.Text.Json;

namespace NexaTextGrab;

public sealed class UserSettings
{
    public string Hotkey { get; set; } = "Space";
    public bool CopyAsBlock { get; set; }
    public string OcrLanguage { get; set; } = "English";

    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexaTextGrab", "settings.json");

    public static UserSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(FilePath)) ?? new UserSettings();
        }
        catch { }
        return new UserSettings();
    }

    public void Save()
    {
        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
