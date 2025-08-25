using System.Text.Json;
using CmdKit.Theme;
using System.Collections.Generic;

namespace CmdKit.Settings;

public class AppSettings
{
    public string DataPath { get; set; } = string.Empty; // custom storage directory (if empty use default)
    public AppTheme Theme { get; set; } = AppTheme.Dark; // supports Dark, Light, Blossom
    public bool AutoCloseAfterCopy { get; set; } = true; // new setting
    public List<string> SensitivePatterns { get; set; } = new() { "password", "token", "secret", "pwd", "api[-_]?key" }; // regex patterns (case-insensitive)
    public float UiFontSize { get; set; } = 10f; // user configurable base UI font size

    public static string GetSettingsFile()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CmdKit");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        return Path.Combine(dir, "settings.json");
    }

    public static AppSettings Load()
    {
        try
        {
            var file = GetSettingsFile();
            if (File.Exists(file))
            {
                var json = File.ReadAllText(file);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var file = GetSettingsFile();
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(file, json);
        }
        catch { }
    }
}
