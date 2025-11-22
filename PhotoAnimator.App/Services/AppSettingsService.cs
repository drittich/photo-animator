using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PhotoAnimator.App.Services;

/// <summary>
/// Simple JSON-backed settings store for last/recent folders.
/// </summary>
public sealed class AppSettingsService : IAppSettingsService
{
    private const int MaxRecent = 6;

    private readonly string _settingsPath;
    private SettingsModel _model;

    public AppSettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var root = Path.Combine(appData, "PhotoAnimator");
        _settingsPath = Path.Combine(root, "settings.json");
        _model = Load();
    }

    public string? LastFolder => _model.LastFolder;

    public IReadOnlyList<string> RecentFolders => _model.RecentFolders;

    public void RecordFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return;
        string normalized;
        try
        {
            normalized = Path.GetFullPath(folderPath);
        }
        catch
        {
            return;
        }

        _model.LastFolder = normalized;

        _model.RecentFolders = _model.RecentFolders
            .Where(f => !string.Equals(f, normalized, StringComparison.OrdinalIgnoreCase))
            .Prepend(normalized)
            .Take(MaxRecent)
            .ToList();

        Save();
    }

    private SettingsModel Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                var model = JsonSerializer.Deserialize<SettingsModel>(json);
                if (model != null)
                {
                    model.RecentFolders ??= new List<string>();
                    return model;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AppSettingsService] Failed to load settings: {ex.Message}");
        }

        return new SettingsModel();
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(_model, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(_settingsPath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AppSettingsService] Failed to save settings: {ex.Message}");
        }
    }

    private sealed class SettingsModel
    {
        public string? LastFolder { get; set; }

        public List<string> RecentFolders { get; set; } = new();
    }
}
