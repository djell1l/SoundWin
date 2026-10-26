using System;
using System.IO;
using System.Text.Json;
using SoundWin.Core.Models;

namespace SoundWin.Core.Services;

public sealed class SettingsService
{
    private const string FileName = "settings.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public SoundWinSettings Current { get; private set; }

    public event Action? SettingsChanged;

    public SettingsService()
    {
        Current = Load();
    }

    public static string GetSettingsPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(localAppData, "SoundWin");
        Directory.CreateDirectory(appFolder);
        return Path.Combine(appFolder, FileName);
    }

    private static SoundWinSettings Load()
    {
        try
        {
            var path = GetSettingsPath();
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<SoundWinSettings>(json, JsonOptions) ?? new SoundWinSettings();
                
                // Migrate obsolete / rejected client IDs to default verified ID
                if (string.IsNullOrWhiteSpace(settings.DiscordRpcClientId) || settings.DiscordRpcClientId == "1296534887309971556")
                {
                    settings.DiscordRpcClientId = SoundWinSettings.DefaultDiscordClientId;
                }
                return settings;
            }
        }
        catch
        {
            // Fallback to defaults on error
        }

        return new SoundWinSettings();
    }

    public void Save()
    {
        try
        {
            var path = GetSettingsPath();
            var json = JsonSerializer.Serialize(Current, JsonOptions);
            File.WriteAllText(path, json);
            SettingsChanged?.Invoke();
        }
        catch
        {
            // Ignored on write error
        }
    }
}
