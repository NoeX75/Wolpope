using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Windows.Storage;
using Windows.System.UserProfile;

namespace Wolpope.Services
{
    /// <summary>
    /// Сервис для поиска обоев на Wallhaven, скачивания и установки
    /// на рабочий стол (в т.ч. per-monitor) и экран блокировки.
    /// </summary>
    public static class WallpaperService
    {
        // ── WinAPI (fallback) ─────────────────────────────────────────
        private const int SPI_SETDESKWALLPAPER = 0x0014;
        private const int SPIF_UPDATEINIFILE   = 0x01;
        private const int SPIF_SENDCHANGE      = 0x02;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SystemParametersInfo(
            int uAction, int uParam, string lpvParam, int fuWinIni);

        private enum QUERY_USER_NOTIFICATION_STATE
        {
            QUNS_NOT_PRESENT = 1, QUNS_BUSY = 2, QUNS_RUNNING_D3D_FULL_SCREEN = 3,
            QUNS_PRESENTATION_MODE = 4, QUNS_ACCEPTS_NOTIFICATIONS = 5,
            QUNS_QUIET_TIME = 6, QUNS_APP = 7
        }

        [DllImport("shell32.dll")]
        private static extern int SHQueryUserNotificationState(out QUERY_USER_NOTIFICATION_STATE pquns);

        public static bool IsFullscreenAppRunning()
        {
            try 
            {
                if (SHQueryUserNotificationState(out var state) == 0)
                {
                    return state == QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN ||
                           state == QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE;
                }
            } 
            catch { }
            return false;
        }

        // ── HTTP-клиент (переиспользуется) ────────────────────────────
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

        // ── Папки ────────────────────────────────────────────────────
        private static readonly string _appDataDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "Wolpope");

        private static readonly string _cacheDir    = Path.Combine(_appDataDir, "Cache");
        private static readonly string _favoritesDir = Path.Combine(_appDataDir, "Favorites");

        /// <summary>Папка приложения в %LocalAppData%</summary>
        public static string AppDataDir => _appDataDir;

        /// <summary>Папка кеша обоев</summary>
        public static string CacheDir => _cacheDir;

        /// <summary>Папка избранного</summary>
        public static string FavoritesDir => _favoritesDir;

        static WallpaperService()
        {
            Directory.CreateDirectory(_cacheDir);
            Directory.CreateDirectory(_favoritesDir);
        }

        // ═══════════════════════════════════════════════════════════════
        //  Wallhaven API
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Ищет случайное изображение на Wallhaven по тегам.
        /// </summary>
        private static readonly Random _pageRng = new();

        public static async Task<string?> SearchRandomImageUrlAsync(string tags)
        {
            if (string.IsNullOrWhiteSpace(tags)) return null;

            var query = tags.Trim();
            // Выбираем случайную страницу от 1 до 5 чтобы разнообразить результаты
            var page = _pageRng.Next(1, 6);
            var url   = $"https://wallhaven.cc/api/v1/search?q={query}&sorting=random&purity=100&page={page}";

            var response = await _http.GetStringAsync(url);
            var json     = JObject.Parse(response);
            var data     = json["data"] as JArray;

            if (data == null || data.Count == 0)
            {
                // Попробуем первую страницу, если случайная пустая
                url = $"https://wallhaven.cc/api/v1/search?q={query}&sorting=random&purity=100";
                response = await _http.GetStringAsync(url);
                json = JObject.Parse(response);
                data = json["data"] as JArray;

                if (data == null || data.Count == 0) return null;
            }

            // Выбираем случайную картинку со страницы загруженной по 'sorting=random'
            var index = _pageRng.Next(data.Count);
            return data[index]?["path"]?.ToString();
        }

        // ═══════════════════════════════════════════════════════════════
        //  Скачивание с префиксом (для истории)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Скачивает изображение потоком. prefix используется для группировки истории:
        /// "desktop", "monitor0", "monitor1", "lockscreen"
        /// </summary>
        public static async Task<string> DownloadImageAsync(string imageUrl, string prefix = "desktop")
        {
            var ext = Path.GetExtension(new Uri(imageUrl).AbsolutePath);
            if (string.IsNullOrEmpty(ext)) ext = ".jpg";

            var fileName = $"{prefix}_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{ext}";
            var filePath = Path.Combine(_cacheDir, fileName);

            using var response = await _http.GetAsync(imageUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            await response.Content.CopyToAsync(fs);
            
            return filePath;
        }

        // ═══════════════════════════════════════════════════════════════
        //  Рабочий стол — все мониторы сразу (WinAPI)
        // ═══════════════════════════════════════════════════════════════

        public static void SetDesktopWallpaper(string imagePath)
        {
            var fullPath = Path.GetFullPath(imagePath);
            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, fullPath,
                                 SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        }

        // ═══════════════════════════════════════════════════════════════
        //  Рабочий стол — конкретный монитор (IDesktopWallpaper COM)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Получает список подключённых мониторов (DevicePath).
        /// </summary>
        public static List<string> GetMonitorDevicePaths()
        {
            var result = new List<string>();
            try
            {
                var wallpaper = (IDesktopWallpaper)new DesktopWallpaperClass();
                uint count = wallpaper.GetMonitorDevicePathCount();
                for (uint i = 0; i < count; i++)
                {
                    var path = wallpaper.GetMonitorDevicePathAt(i);
                    if (!string.IsNullOrEmpty(path))
                        result.Add(path);
                }
            }
            catch { }
            return result;
        }

        /// <summary>
        /// Устанавливает обои для конкретного монитора через COM.
        /// </summary>
        public static void SetWallpaperForMonitor(string monitorDevicePath, string imagePath)
        {
            try
            {
                var wallpaper = (IDesktopWallpaper)new DesktopWallpaperClass();
                wallpaper.SetPosition(DesktopWallpaperPosition.Fill);
                wallpaper.SetWallpaper(monitorDevicePath, Path.GetFullPath(imagePath));
            }
            catch
            {
                SetDesktopWallpaper(imagePath);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  Экран блокировки (WinRT)
        // ═══════════════════════════════════════════════════════════════

        public static async Task SetLockScreenWallpaperAsync(string imagePath)
        {
            var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(imagePath));
            await LockScreen.SetImageFileAsync(file);
        }

        // ═══════════════════════════════════════════════════════════════
        //  Избранное
        // ═══════════════════════════════════════════════════════════════

        public static string ExtractBaseName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            var name = Path.GetFileName(path);
            var parts = name.Split('_', 4);
            // Matches cache or favorite format: prefix_YYYYMMDD_HHMMSS_basename
            if (parts.Length == 4 && parts[1].Length == 8 && parts[2].Length >= 6 && parts[1].All(char.IsDigit) && parts[2].All(char.IsDigit))
            {
                return parts[3];
            }
            return name;
        }

        public static bool IsFavorite(string imagePath)
        {
            var baseName = ExtractBaseName(imagePath);
            if (string.IsNullOrEmpty(baseName)) return false;
            
            var favs = GetFavorites();
            return favs.Any(f => f.EndsWith("_" + baseName, StringComparison.OrdinalIgnoreCase));
        }

        public static string? GetFavoritePath(string imagePath)
        {
            var baseName = ExtractBaseName(imagePath);
            if (string.IsNullOrEmpty(baseName)) return null;
            
            var favs = GetFavorites();
            return favs.FirstOrDefault(f => f.EndsWith("_" + baseName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Копирует файл в избранное и возвращает путь копии.</summary>
        public static string AddToFavorites(string imagePath)
        {
            var originalName = ExtractBaseName(imagePath);
            var dest = Path.Combine(_favoritesDir, $"fav_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{originalName}");
            File.Copy(imagePath, dest, true);
            return dest;
        }

        /// <summary>Получает список файлов в избранном.</summary>
        public static List<string> GetFavorites()
        {
            return Directory.GetFiles(_favoritesDir)
                .Where(f => IsImageFile(f))
                .OrderByDescending(f => File.GetCreationTimeUtc(f))
                .ToList();
        }

        /// <summary>Удаляет файл из избранного.</summary>
        public static void RemoveFromFavorites(string filePath)
        {
            try { if (File.Exists(filePath)) File.Delete(filePath); } catch { }
        }

        public static void RemoveFromFavoritesBySourcePath(string imagePath)
        {
             var favFile = GetFavoritePath(imagePath);
             if (favFile != null && File.Exists(favFile))
             {
                 try { File.Delete(favFile); } catch { }
             }
        }

        // ═══════════════════════════════════════════════════════════════
        //  Открыть папки в проводнике
        // ═══════════════════════════════════════════════════════════════

        public static void OpenCacheFolder()
        {
            try { Process.Start("explorer.exe", _cacheDir); } catch { }
        }

        public static void OpenFavoritesFolder()
        {
            try { Process.Start("explorer.exe", _favoritesDir); } catch { }
        }

        // ═══════════════════════════════════════════════════════════════
        //  Очистка кеша — 3 файла на каждый префикс
        // ═══════════════════════════════════════════════════════════════

        public static void CleanupCache()
        {
            try
            {
                var files = new DirectoryInfo(_cacheDir).GetFiles()
                    .Where(f => IsImageFile(f.Name))
                    .ToList();

                // Группируем по префиксу (desktop_, monitor0_, lockscreen_ и т.д.)
                var groups = files.GroupBy(f =>
                {
                    var name = f.Name;
                    var idx = name.IndexOf('_');
                    if (idx > 0)
                    {
                        var prefix = name[..idx];
                        // monitor0, monitor1 etc — включаем цифру
                        if (prefix == "monitor" && name.Length > idx + 1 && char.IsDigit(name[idx + 1]))
                            return name[..(idx + 2)]; // "monitor0", "monitor1"
                        return prefix; // "desktop", "lockscreen"
                    }
                    return "other";
                });

                foreach (var group in groups)
                {
                    var old = group.OrderByDescending(f => f.CreationTimeUtc).Skip(3);
                    foreach (var f in old)
                    {
                        try { f.Delete(); } catch { }
                    }
                }
            }
            catch { }
        }

        private static bool IsImageFile(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".webp";
        }
    }
}
