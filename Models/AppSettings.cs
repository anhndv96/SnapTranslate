using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using System.Diagnostics;

namespace SnapTranslate.Models
{
    public class AppSettingsData
    {
        public string DeepSeekApiKey { get; set; } = "";
        public double WindowWidth { get; set; } = 500;
        public double WindowHeight { get; set; } = 0;
        public int SelectedEngineIndex { get; set; } = 0;
        public string LocalLlmUrl { get; set; } = "http://localhost:1234/v1/chat/completions";
        public bool StartWithWindows { get; set; } = true;
    }

    public static class AppSettings
    {
        public static AppSettingsData Current { get; private set; } = new AppSettingsData();

        private static string ConfigPath
        {
            get
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var folder = Path.Combine(appData, "SnapTranslate");
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);
                return Path.Combine(folder, "config.json");
            }
        }

        public static void Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    var data = JsonSerializer.Deserialize<AppSettingsData>(json);
                    if (data != null)
                        Current = data;
                }
            }
            catch
            {
                // Ignored
            }
        }

        public static void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch
            {
                // Ignored
            }
        }

        public static void ApplyStartupSetting()
        {
            try
            {
                const string StartupKey = "SnapTranslate";
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key == null) return;
                    if (Current.StartWithWindows)
                    {
                        var path = Environment.ProcessPath;
                        if (!string.IsNullOrEmpty(path))
                        {
                            key.SetValue(StartupKey, $"\"{path}\" --minimized");
                        }
                    }
                    else
                    {
                        key.DeleteValue(StartupKey, false);
                    }
                }
            }
            catch
            {
                // Ignored
            }
        }
    }
}
