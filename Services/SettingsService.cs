using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Wolpope.Services
{
    /// <summary>
    /// Сохраняет и загружает настройки в %LocalAppData%\Wolpope\settings.json
    /// </summary>
    public static class SettingsService
    {
        private static readonly string _settingsPath =
            Path.Combine(WallpaperService.AppDataDir, "settings.json");

        public class AppSettings
        {
            public bool WaitNetwork { get; set; } = true;
            public string Interval { get; set; } = "20";
            public int IntervalUnitIndex { get; set; } = 1; // 1 = Minutes
            public bool IsExactTimeMode { get; set; } = false;
            public string ExactTime { get; set; } = "12:00";
            public bool WasRunning { get; set; } = true;

            public bool IsPerMonitor { get; set; }
            public bool IsSyncAllMonitors { get; set; }
            public bool RandomPerScreen { get; set; }
            public bool RandomForMonitors { get; set; }
            public bool IsLockScreenSeparate { get; set; }
            public bool RandomForLockScreen { get; set; }
            public bool RandomizeLockScreenSource { get; set; }
            public int LockScreenMonitorIndex { get; set; }

            public bool PauseOnFullscreen { get; set; }
            public bool StartWithWindows { get; set; }

            public List<string> SelectedSharedTags { get; set; } = new();
            public string SharedCustomTags { get; set; } = "";
            public List<string> CustomTagsList { get; set; } = new();

            public List<string> SelectedLockScreenTags { get; set; } = new();
            public string LockScreenCustomTags { get; set; } = "";

            public List<MonitorSettings> MonitorTagSettings { get; set; } = new();

            public int FavoritesPercentage { get; set; } = 10;
        }

        public class MonitorSettings
        {
            public List<string> SelectedTags { get; set; } = new();
            public string CustomTags { get; set; } = "";
        }

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);
                    return JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch { }
            return new AppSettings();
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
                var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                File.WriteAllText(_settingsPath, json);
            }
            catch { }
        }
    }
}
