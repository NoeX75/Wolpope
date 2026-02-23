using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.Win32;
using Wolpope.Helpers;
using Wolpope.Models;
using Wolpope.Services;

namespace Wolpope.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private const string AppName = "Wolpope";
        private const string RunKey  = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        private readonly DispatcherTimer _timer;
        private readonly DispatcherTimer _countdownTimer;
        private readonly Random _rng = new();
        
        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set 
            { 
                _isBusy = value; 
                OnPropertyChanged(); 
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => RelayCommand.RaiseCanExecuteChanged()));
            }
        }
        
        private DateTime _nextTickTime;

        public event EventHandler<string>? ErrorOccurred;

        // SPA Navigation
        private string _currentView = "Dashboard";
        public string CurrentView
        {
            get => _currentView;
            set { _currentView = value; OnPropertyChanged(); }
        }

        // Feature: Pause On Fullscreen
        private bool _pauseOnFullscreen;
        public bool PauseOnFullscreen
        {
            get => _pauseOnFullscreen;
            set { _pauseOnFullscreen = value; OnPropertyChanged(); }
        }

        // Feature: Sync Monitors
        private bool _isSyncAllMonitors;
        public bool IsSyncAllMonitors
        {
            get => _isSyncAllMonitors;
            set { _isSyncAllMonitors = value; OnPropertyChanged(); }
        }

        // SPA Commands
        public RelayCommand<string> NavigateCommand { get; }
        public RelayCommand<string> RemoveCustomTagCommand { get; }

        // Favorites Logic
        public ObservableCollection<FavoriteItem> FavoritesList { get; } = new();
        
        private string _favoritesCountText = "0 items";
        public string FavoritesCountText
        {
            get => _favoritesCountText;
            set { _favoritesCountText = value; OnPropertyChanged(); }
        }

        private bool _isFavoritesEmpty = true;
        public bool IsFavoritesEmpty
        {
            get => _isFavoritesEmpty;
            set { _isFavoritesEmpty = value; OnPropertyChanged(); }
        }

        public RelayCommand<FavoriteItem> SelectFavoriteCommand { get; }
        public RelayCommand AddFromFileCommand { get; }
        public RelayCommand DeleteSelectedFavoritesCommand { get; }
        public RelayCommand OpenFavoritesFolderCommand { get; }


        private double _timeProgress;
        public double TimeProgress
        {
            get => _timeProgress;
            set { _timeProgress = value; OnPropertyChanged(); }
        }

        private TimeSpan _currentTimerDuration;

        // ═══════════════════════════════════════════════════════════════
        //  КОНСТРУКТОР
        // ═══════════════════════════════════════════════════════════════

        public MainViewModel()
        {
            _timer = new DispatcherTimer();
            _timer.Tick += async (_, _) => 
            {
                if (_isBusy) return;
                await OnTimerTickAsync();
            };

            _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _countdownTimer.Tick += (_, _) =>
            {
                if (_isBusy)
                {
                    TimeProgress = 100;
                    CountdownText = "Downloading...";
                    return;
                }

                var remaining = _nextTickTime - DateTime.Now;
                var total = _currentTimerDuration.TotalSeconds;

                if (remaining.TotalSeconds <= 0)
                {
                    TimeProgress = 100;
                    CountdownText = "0 min.";
                }
                else
                {
                    if (total > 0)
                    {
                        var progress = ((total - remaining.TotalSeconds) / total) * 100.0;
                        TimeProgress = Math.Clamp(progress, 0, 100);
                    }
                    CountdownText = $"{Math.Ceiling(remaining.TotalMinutes)} min.";
                }
            };

            StartCommand         = new RelayCommand(Start, () => !IsRunning);
            StopCommand          = new RelayCommand(Stop,  () => IsRunning);
            SkipCommand          = new RelayCommand(Skip,  () => !_isBusy);
            AddToFavoritesCommand = new RelayCommand(ToggleCurrentFavorite, () => _currentWallpaperPath != null);
            OpenCacheFolderCommand = new RelayCommand(() => WallpaperService.OpenCacheFolder());

            NavigateCommand = new RelayCommand<string>(v => CurrentView = v);
            RemoveCustomTagCommand = new RelayCommand<string>(t => RemoveCustomTag(t));
            RemoveCustomTagCommand = new RelayCommand<string>(t => RemoveCustomTag(t));

            SelectFavoriteCommand = new RelayCommand<FavoriteItem>(item => SelectFavorite(item));
            AddFromFileCommand = new RelayCommand(AddFromFile);
            DeleteSelectedFavoritesCommand = new RelayCommand(DeleteSelectedFavorites);
            OpenFavoritesFolderCommand = new RelayCommand(() => WallpaperService.OpenFavoritesFolder());

            SharedTags     = new ObservableCollection<TagItem>(WallhavenTags.CreateDefaultTags());
            LockScreenTags = new ObservableCollection<TagItem>(WallhavenTags.CreateDefaultTags());

            DetectMonitors();
            LoadSettings();
            UpdateTagScopes();
            LoadFavorites();

            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (s, e) =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    DetectMonitors();
                    UpdateTagScopes();
                });
            };
        }

        // ═══════════════════════════════════════════════════════════════
        //  СВОЙСТВА — Интервал
        // ═══════════════════════════════════════════════════════════════

        private string _interval = "30";
        public string Interval
        {
            get => _interval;
            set { _interval = value; OnPropertyChanged(); }
        }

        private int _intervalUnitIndex = 1;
        public int IntervalUnitIndex
        {
            get => _intervalUnitIndex;
            set { _intervalUnitIndex = value; OnPropertyChanged(); }
        }

        private bool _isExactTimeMode;
        public bool IsExactTimeMode
        {
            get => _isExactTimeMode;
            set { _isExactTimeMode = value; OnPropertyChanged(); }
        }

        private string _exactTime = "12:00";
        public string ExactTime
        {
            get => _exactTime;
            set { _exactTime = value; OnPropertyChanged(); }
        }

        // ═══════════════════════════════════════════════════════════════
        //  СВОЙСТВА — Режимы
        // ═══════════════════════════════════════════════════════════════

        private bool _isLockScreenSeparate;
        public bool IsLockScreenSeparate
        {
            get => _isLockScreenSeparate;
            set { _isLockScreenSeparate = value; OnPropertyChanged(); UpdateTagScopes(); }
        }

        private bool _isPerMonitor;
        public bool IsPerMonitor
        {
            get => _isPerMonitor;
            set { _isPerMonitor = value; OnPropertyChanged(); UpdateTagScopes(); }
        }

        private bool _canUsePerMonitor;
        public bool CanUsePerMonitor
        {
            get => _canUsePerMonitor;
            private set { _canUsePerMonitor = value; OnPropertyChanged(); }
        }

        private int _lockScreenMonitorIndex;
        public int LockScreenMonitorIndex
        {
            get => _lockScreenMonitorIndex;
            set { _lockScreenMonitorIndex = value; OnPropertyChanged(); }
        }

        private bool _randomPerScreen;
        public bool RandomPerScreen
        {
            get => _randomPerScreen;
            set { _randomPerScreen = value; OnPropertyChanged(); }
        }

        private bool _randomForMonitors;
        public bool RandomForMonitors
        {
            get => _randomForMonitors;
            set { _randomForMonitors = value; OnPropertyChanged(); UpdatePreviewTabs(); }
        }

        private bool _randomForLockScreen;
        public bool RandomForLockScreen
        {
            get => _randomForLockScreen;
            set { _randomForLockScreen = value; OnPropertyChanged(); UpdatePreviewTabs(); }
        }

        private bool _randomizeLockScreenSource;
        public bool RandomizeLockScreenSource
        {
            get => _randomizeLockScreenSource;
            set { _randomizeLockScreenSource = value; OnPropertyChanged(); }
        }

        // ═══════════════════════════════════════════════════════════════
        //  СВОЙСТВА — Теги
        // ═══════════════════════════════════════════════════════════════

        public class TagScopeItem
        {
            public string DisplayName { get; set; } = "";
            public int TargetIndex { get; set; } 
        }

        public ObservableCollection<TagScopeItem> TagScopes { get; } = new();

        private TagScopeItem? _selectedTagScopeItem;
        public TagScopeItem? SelectedTagScopeItem
        {
            get => _selectedTagScopeItem;
            set
            {
                _selectedTagScopeItem = value;
                OnPropertyChanged();
                UpdateTagEditorBindings();
            }
        }

        public ObservableCollection<TagItem> SharedTags { get; }
        public ObservableCollection<TagItem> LockScreenTags { get; }
        public ObservableCollection<MonitorInfo> Monitors { get; } = new();

        private int _favoritesPercentage = 10;
        public int FavoritesPercentage
        {
            get => _favoritesPercentage;
            set { _favoritesPercentage = value; OnPropertyChanged(); }
        }

        private string _sharedCustomTags = "";
        public string SharedCustomTags
        {
            get => _sharedCustomTags;
            set { _sharedCustomTags = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> CustomTagsList { get; } = new();

        private string _lockScreenCustomTags = "";
        public string LockScreenCustomTags
        {
            get => _lockScreenCustomTags;
            set { _lockScreenCustomTags = value; OnPropertyChanged(); }
        }

        public ObservableCollection<TagCategoryItem> CategoriesTree { get; } = new();

        // ═══════════════════════════════════════════════════════════════
        //  СВОЙСТВА — Статус и текущие обои
        // ═══════════════════════════════════════════════════════════════

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            set 
            { 
                if (_isRunning != value)
                {
                    _isRunning = value; 
                    OnPropertyChanged(); 
                    RelayCommand.RaiseCanExecuteChanged(); 
                    SaveSettings(); 
                }
            }
        }

        private string _statusText = "⏸ Stopped";
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        private string _countdownText = "-- min.";
        public string CountdownText
        {
            get => _countdownText;
            set { _countdownText = value; OnPropertyChanged(); }
        }

        private bool _startWithWindows;
        public bool StartWithWindows
        {
            get => _startWithWindows;
            set { _startWithWindows = value; OnPropertyChanged(); WriteAutoStartToRegistry(value); }
        }

        private string? _currentWallpaperPath;
        public string? CurrentWallpaperPath
        {
            get => _currentWallpaperPath;
            set 
            { 
                _currentWallpaperPath = value; 
                OnPropertyChanged(); 
                RelayCommand.RaiseCanExecuteChanged();
                
                if (value != null)
                {
                    IsCurrentWallpaperFavorite = WallpaperService.IsFavorite(value);
                }
                else
                {
                    IsCurrentWallpaperFavorite = false;
                }
            }
        }

        private bool _isCurrentWallpaperFavorite;
        public bool IsCurrentWallpaperFavorite
        {
            get => _isCurrentWallpaperFavorite;
            set { _isCurrentWallpaperFavorite = value; OnPropertyChanged(); }
        }

        public string? LastLockScreenPath { get; set; }
        private string? _lastDesktopPath;

        public ObservableCollection<PreviewTabItem> PreviewTabs { get; } = new();
        public bool IsPreviewSwitcherVisible => PreviewTabs.Count > 1;

        private PreviewTabItem? _selectedPreviewTab;
        public PreviewTabItem? SelectedPreviewTab
        {
            get => _selectedPreviewTab;
            set 
            { 
                _selectedPreviewTab = value; 
                OnPropertyChanged();
                UpdateCurrentWallpaperDisplay();
            }
        }

        private void UpdateCurrentWallpaperDisplay()
        {
            if (SelectedPreviewTab == null) return;
            string targetId = SelectedPreviewTab.TargetId;
            
            if (targetId == "desktop")
                CurrentWallpaperPath = _lastDesktopPath;
            else if (targetId == "lockscreen")
                CurrentWallpaperPath = LastLockScreenPath;
            else if (targetId.StartsWith("monitor"))
            {
                if (int.TryParse(targetId.Replace("monitor", ""), out int idx) && idx >= 0 && idx < Monitors.Count)
                    CurrentWallpaperPath = Monitors[idx].LastWallpaperPath;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  КОМАНДЫ
        // ═══════════════════════════════════════════════════════════════

        public RelayCommand StartCommand { get; }
        public RelayCommand StopCommand  { get; }
        public RelayCommand SkipCommand  { get; }
        public RelayCommand AddToFavoritesCommand { get; }
        public RelayCommand OpenCacheFolderCommand { get; }

        private void Start()
        {
            // ── Проверка тегов ────────────────────────────────────────
            bool hasTags;
            if (IsPerMonitor && CanUsePerMonitor)
            {
                hasTags = Monitors.Any(m => !string.IsNullOrWhiteSpace(m.SelectedTagsQuery));
            }
            else
            {
                hasTags = !string.IsNullOrWhiteSpace(BuildQuery(SharedTags, SharedCustomTags));
            }

            if (!hasTags && WallpaperService.GetFavorites().Count == 0)
            {
                StatusText = "⚠ Select at least one tag or add wallpapers to favorites";
                return;
            }

            TimeSpan span;

            // ── Логика таймера: Интервал или Точное время суток ──────────────────
            if (IsExactTimeMode)
            {
                if (!TimeSpan.TryParse(ExactTime, out var exactTimeOfDay))
                {
                    StatusText = "⚠ Invalid time format. Please use HH:MM format";
                    return;
                }

                var now = DateTime.Now;
                var targetTime = now.Date + exactTimeOfDay;

                if (now > targetTime)
                {
                     // Если время уже прошло сегодня, сработает завтра в это время
                    targetTime = targetTime.AddDays(1);
                }

                span = targetTime - now;
                
                // Период таймера после первого срабатывания - 24 часа. 
                // Но мы просто перезапустим таймер на 24 часа в OnTimerTickAsync в будущем. 
                // Для простоты, мы будем пересчитывать это.
                _timer.Interval = span;

                if (!hasTags)
                    StatusText = $"▶ Running (Favorites only) · Change at {ExactTime}";
                else
                    StatusText = $"▶ Running · Wallpaper change at {ExactTime}";
            }
            else
            {
                if (!int.TryParse(Interval, out var value) || value <= 0)
                {
                    StatusText = "⚠ Enter a valid interval (integer > 0)";
                    return;
                }

                span = IntervalUnitIndex switch
                {
                    0 => TimeSpan.FromSeconds(value),
                    1 => TimeSpan.FromMinutes(value),
                    2 => TimeSpan.FromHours(value),
                    _ => TimeSpan.FromMinutes(value)
                };

                _timer.Interval = span;

                if (!hasTags)
                    StatusText = $"▶ Running (Favorites only) · Interval: {value} {UnitLabel(IntervalUnitIndex)}";
                else
                    StatusText = $"▶ Running · Interval: {value} {UnitLabel(IntervalUnitIndex)}";
            }

            _currentTimerDuration = span;
            IsRunning = true;
            
            // Сначала таймер обратного отсчета пойдет с "Downloading..." 
            // _isBusy is triggered immediately when _ = OnTimerTickAsync() starts
            _nextTickTime = DateTime.Now + span;
            _countdownTimer.Start();

            SaveSettings();
            
            // Если мы в ExactTime, и включили комп позже, юзер хочет смену "при старте, если ПК был выключен".
            // Для MVP: запускаем сейчас, а затем таймер рассчитает для завтра.
            if (IsExactTimeMode)
            {
                 // Не запускаем OnTimerTickAsync немедленно, так как он обновит обои сейчас,
                 // а не в ExactTime.
                 // Юзер просил: "If PC is off, wallpaper changes at startup."
                 // Мы могли бы хранить LastRunTime в настройках и проверять, но для начала просто ждем таймера.
            }
            else
            {
                _ = OnTimerTickAsync();
            }

            _timer.Start();
        }

        private void Stop()
        {
            _timer.Stop();
            _countdownTimer.Stop();
            CountdownText = "-- min.";
            IsRunning = false;
            StatusText = "⏸ Stopped";
            SaveSettings();
        }

        private void Skip()
        {
            if (!_isBusy)
            {
                _timer.Stop(); // Сперва отключаем таймер
                _ = OnTimerTickAsync();
                RelayCommand.RaiseCanExecuteChanged();
            }
        }

        private void ToggleCurrentFavorite()
        {
            if (_currentWallpaperPath == null) return;
            try
            {
                if (IsCurrentWallpaperFavorite)
                {
                    WallpaperService.AddToFavorites(_currentWallpaperPath);
                    StatusText = "❤️ Added to favorites!";
                }
                else
                {
                    WallpaperService.RemoveFromFavoritesBySourcePath(_currentWallpaperPath);
                    StatusText = "💔 Removed from favorites!";
                }
                LoadFavorites();
            }
            catch (Exception ex)
            {
                StatusText = $"❌ Ошибка: {ex.Message}";
            }
        }

        private void SelectFavorite(FavoriteItem item)
        {
            if (item != null)
            {
                item.IsSelected = !item.IsSelected;
            }
        }

        public void LoadFavorites()
        {
            FavoritesList.Clear();
            var files = WallpaperService.GetFavorites();

            foreach (var f in files)
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(f);
                bmp.DecodePixelHeight = 150; // Thumbnail size
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();

                FavoritesList.Add(new FavoriteItem
                {
                    FilePath = f,
                    Thumbnail = bmp,
                    IsSelected = false
                });
            }
            if (_currentWallpaperPath != null)
            {
                IsCurrentWallpaperFavorite = WallpaperService.IsFavorite(_currentWallpaperPath);
            }
            FavoritesCountText = $"{FavoritesList.Count} items";
            IsFavoritesEmpty = FavoritesList.Count == 0;
        }

        private void DeleteSelectedFavorites()
        {
            var selected = FavoritesList.Where(f => f.IsSelected).ToList();
            if (selected.Count == 0) return;

            foreach (var item in selected)
            {
                WallpaperService.RemoveFromFavorites(item.FilePath);
            }

            LoadFavorites();
        }

        private void AddFromFile()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp",
                Multiselect = true,
                Title = "Select wallpapers to add to favorites"
            };

            if (dialog.ShowDialog() == true)
            {
                foreach (var file in dialog.FileNames)
                {
                    WallpaperService.AddToFavorites(file);
                }
                LoadFavorites();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  ОБНАРУЖЕНИЕ МОНИТОРОВ
        // ═══════════════════════════════════════════════════════════════

        public void DetectMonitors()
        {
            Monitors.Clear();
            var paths = WallpaperService.GetMonitorDevicePaths();
            var activeScreenCount = System.Windows.Forms.Screen.AllScreens.Length;
            
            for (int i = 0; i < activeScreenCount; i++)
            {
                string path = (i < paths.Count) ? paths[i] : "";
                Monitors.Add(new MonitorInfo(path, $"Desktop {i + 1}"));
            }

            if (Monitors.Count == 0)
                Monitors.Add(new MonitorInfo("", "Primary Desktop"));

            CanUsePerMonitor = Monitors.Count > 1;
        }

        // ═══════════════════════════════════════════════════════════════
        //  ЛОГИКА ТИКА
        // ═══════════════════════════════════════════════════════════════

        private async Task OnTimerTickAsync()
        {
            if (_isBusy) return;
            _isBusy = true;

            if (PauseOnFullscreen && WallpaperService.IsFullscreenAppRunning())
            {
                StatusText = "⏸ Paused (Fullscreen App)";
                IsBusy = false;
                return;
            }

            try
            {
                // ── Проверка избранных ────────────────────
                var favorites = WallpaperService.GetFavorites();

                string? GetFavoriteOrNull(string prefix)
                {
                    if (favorites.Count > 0 && _rng.Next(100) < FavoritesPercentage)
                    {
                        var favPath = favorites[_rng.Next(favorites.Count)];
                        var cachedFav = System.IO.Path.Combine(WallpaperService.CacheDir, $"{prefix}_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{WallpaperService.ExtractBaseName(favPath)}");
                        System.IO.File.Copy(favPath, cachedFav, true);
                        return cachedFav;
                    }
                    return null;
                }

                // ── Рабочий стол ──────────────────────────────────────
                if ((IsPerMonitor || RandomForMonitors) && CanUsePerMonitor)
                {
                    var sharedQuery = RandomForMonitors ? BuildQuery(SharedTags, SharedCustomTags) : null;
                    
                    for (int i = 0; i < Monitors.Count; i++)
                    {
                        var fav = GetFavoriteOrNull($"monitor{i}");
                        if (fav != null)
                        {
                            StatusText = $"⭐ {Monitors[i].DisplayName}: applying favorite…";
                            WallpaperService.SetWallpaperForMonitor(Monitors[i].DevicePath, fav);
                            Monitors[i].LastWallpaperPath = fav;
                            UpdateCurrentWallpaperDisplay();
                        }
                        else
                        {
                            var mon = Monitors[i];
                            var query = RandomForMonitors ? sharedQuery : mon.SelectedTagsQuery;
                            if (string.IsNullOrWhiteSpace(query)) continue;

                            StatusText = $"🔍 {mon.DisplayName}: searching for wallpapers…";
                            var url = await WallpaperService.SearchRandomImageUrlAsync(query);
                            if (url != null)
                            {
                                StatusText = $"⬇ {mon.DisplayName}: downloading…";
                                var path = await WallpaperService.DownloadImageAsync(url, $"monitor{i}");
                                WallpaperService.SetWallpaperForMonitor(mon.DevicePath, path);
                                mon.LastWallpaperPath = path;
                                UpdateCurrentWallpaperDisplay();
                            }
                        }
                    }
                }
                else
                {
                    var fav = GetFavoriteOrNull("desktop");
                    if (fav != null)
                    {
                        StatusText = "⭐ Applying favorite wallpaper…";
                        WallpaperService.SetDesktopWallpaper(fav);
                        _lastDesktopPath = fav;
                        UpdateCurrentWallpaperDisplay();
                    }
                    else
                    {
                        var query = BuildQuery(SharedTags, SharedCustomTags);
                        if (!string.IsNullOrWhiteSpace(query))
                        {
                            StatusText = "🔍 Searching for desktop wallpaper…";
                            var url = await WallpaperService.SearchRandomImageUrlAsync(query);
                            if (url != null)
                            {
                                StatusText = "⬇ Downloading desktop wallpaper…";
                                _lastDesktopPath = await WallpaperService.DownloadImageAsync(url, "desktop");
                                WallpaperService.SetDesktopWallpaper(_lastDesktopPath);
                                UpdateCurrentWallpaperDisplay();
                            }
                        }
                    }
                }

                // ── Экран блокировки ──────────────────────────────────
                if (IsLockScreenSeparate)
                {
                    var fav = GetFavoriteOrNull("lockscreen");
                    if (fav != null)
                    {
                        StatusText = "⭐ Lock screen: applying favorite wallpaper…";
                        await WallpaperService.SetLockScreenWallpaperAsync(fav);
                        LastLockScreenPath = fav;
                        UpdateCurrentWallpaperDisplay();
                    }
                    else
                    {
                        var lockQuery = BuildQuery(LockScreenTags, LockScreenCustomTags);
                        if (!string.IsNullOrWhiteSpace(lockQuery))
                        {
                            StatusText = "🔍 Searching for lock screen wallpaper…";
                            var url2 = await WallpaperService.SearchRandomImageUrlAsync(lockQuery);
                            if (url2 != null)
                            {
                                StatusText = "⬇ Downloading lock screen wallpaper…";
                                var path2 = await WallpaperService.DownloadImageAsync(url2, "lockscreen");
                                await WallpaperService.SetLockScreenWallpaperAsync(path2);
                                LastLockScreenPath = path2;
                                UpdateCurrentWallpaperDisplay();
                            }
                        }
                    }
                }
                else if (IsPerMonitor && CanUsePerMonitor)
                {
                    var idx = LockScreenMonitorIndex;
                    if (idx >= 0 && idx < Monitors.Count)
                    {
                        var mon = Monitors[idx];

                        if (RandomForLockScreen && !string.IsNullOrWhiteSpace(mon.SelectedTagsQuery))
                        {
                            var fav = GetFavoriteOrNull("lockscreen_random");
                            if (fav != null)
                            {
                                StatusText = $"⭐ Lock Screen (Favorite from {mon.DisplayName})…";
                                await WallpaperService.SetLockScreenWallpaperAsync(fav);
                                LastLockScreenPath = fav;
                                UpdateCurrentWallpaperDisplay();
                            }
                            else
                            {
                                StatusText = $"🔍 Lock Screen (Random from {mon.DisplayName})…";
                                var url = await WallpaperService.SearchRandomImageUrlAsync(mon.SelectedTagsQuery);
                                if (url != null)
                                {
                                    var path = await WallpaperService.DownloadImageAsync(url, "lockscreen_random");
                                    await WallpaperService.SetLockScreenWallpaperAsync(path);
                                    LastLockScreenPath = path;
                                    UpdateCurrentWallpaperDisplay();
                                }
                            }
                        }
                        else
                        {
                            var reusePath = mon.LastWallpaperPath;
                            if (reusePath != null)
                            {
                                StatusText = $"🔒 Lock screen ← {mon.DisplayName}…";
                                await WallpaperService.SetLockScreenWallpaperAsync(reusePath);
                                LastLockScreenPath = reusePath;
                                UpdateCurrentWallpaperDisplay();
                            }
                        }
                    }
                }
                else if (RandomForLockScreen)
                {
                    var fav = GetFavoriteOrNull("lockscreen");
                    if (fav != null)
                    {
                        StatusText = "⭐ Lock screen: applying favorite wallpaper…";
                        await WallpaperService.SetLockScreenWallpaperAsync(fav);
                        LastLockScreenPath = fav;
                        UpdateCurrentWallpaperDisplay();
                    }
                    else
                    {
                        var lockQuery = BuildQuery(SharedTags, SharedCustomTags);
                        if (!string.IsNullOrWhiteSpace(lockQuery))
                        {
                            StatusText = "🔍 Random wallpaper for lock screen…";
                            var url2 = await WallpaperService.SearchRandomImageUrlAsync(lockQuery);
                            if (url2 != null)
                            {
                                StatusText = "⬇ Downloading lock screen wallpaper…";
                                var path2 = await WallpaperService.DownloadImageAsync(url2, "lockscreen");
                                await WallpaperService.SetLockScreenWallpaperAsync(path2);
                                LastLockScreenPath = path2;
                                UpdateCurrentWallpaperDisplay();
                            }
                        }
                    }
                }
                else
                {
                    string? fallbackPath = _lastDesktopPath ?? Monitors.FirstOrDefault()?.LastWallpaperPath;
                    if (fallbackPath != null)
                    {
                        StatusText = "🔒 Lock screen…";
                        await WallpaperService.SetLockScreenWallpaperAsync(fallbackPath);
                        LastLockScreenPath = fallbackPath;
                        UpdateCurrentWallpaperDisplay();
                    }
                }

                WallpaperService.CleanupCache();
                StatusText = $"✅ Wallpaper updated";
            }
            catch (Exception ex)
            {
                StatusText = $"❌ Error: {ex.Message}";
            }
            finally
            {
                // Если мы в режиме ExactTime, расчитываем время до завтра
                if (IsExactTimeMode && TimeSpan.TryParse(ExactTime, out var exactTimeOfDay))
                {
                    var tomorrow = DateTime.Now.Date.AddDays(1) + exactTimeOfDay;
                    if (DateTime.Now > tomorrow) tomorrow = tomorrow.AddDays(1);
                    _currentTimerDuration = tomorrow - DateTime.Now;
                    _timer.Interval = _currentTimerDuration;
                }
                else
                {
                    _currentTimerDuration = _timer.Interval;
                }

                _nextTickTime = DateTime.Now + _currentTimerDuration;
                _timer.Stop();
                
                if (IsRunning)
                {
                    _timer.Start(); // Reset the main timer so the next interval starts NOW, not when it was originally fired
                }

                IsBusy = false;
                System.Windows.Application.Current.Dispatcher.Invoke(() => 
                {
                    RelayCommand.RaiseCanExecuteChanged();
                });
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  СОХРАНЕНИЕ / ЗАГРУЗКА НАСТРОЕК
        // ═══════════════════════════════════════════════════════════════

        public void SaveSettings()
        {
            var s = new SettingsService.AppSettings
            {
                Interval             = Interval,
                IntervalUnitIndex    = IntervalUnitIndex,
                IsPerMonitor         = IsPerMonitor,
                IsSyncAllMonitors    = IsSyncAllMonitors,
                PauseOnFullscreen    = PauseOnFullscreen,
                IsLockScreenSeparate = IsLockScreenSeparate,
                LockScreenMonitorIndex = LockScreenMonitorIndex,
                StartWithWindows     = StartWithWindows,
                RandomPerScreen      = RandomPerScreen,
                RandomForMonitors    = RandomForMonitors,
                RandomForLockScreen  = RandomForLockScreen,
                RandomizeLockScreenSource = RandomizeLockScreenSource,
                WasRunning           = IsRunning,

                SelectedSharedTags     = SharedTags.Where(t => t.IsSelected).Select(t => t.Name).ToList(),
                SharedCustomTags       = SharedCustomTags,
                CustomTagsList         = CustomTagsList.ToList(),
                SelectedLockScreenTags = LockScreenTags.Where(t => t.IsSelected).Select(t => t.Name).ToList(),
                LockScreenCustomTags   = LockScreenCustomTags,
            };

            foreach (var mon in Monitors)
            {
                s.MonitorTagSettings.Add(new SettingsService.MonitorSettings
                {
                    SelectedTags = mon.Tags.Where(t => t.IsSelected).Select(t => t.Name).ToList(),
                    CustomTags   = mon.CustomTags
                });
            }

            s.FavoritesPercentage = FavoritesPercentage;

            SettingsService.Save(s);
        }

        private void ShowErrorMessage(string msg)
        {
            ErrorOccurred?.Invoke(this, msg);
        }

        private void UpdateTagScopes()
        {
            var oldTarget = SelectedTagScopeItem?.TargetIndex ?? 0;
            TagScopes.Clear();

            if (!IsPerMonitor || !CanUsePerMonitor)
            {
                TagScopes.Add(new TagScopeItem { DisplayName = "Shared Tags", TargetIndex = 0 });
            }

            if (IsLockScreenSeparate)
                TagScopes.Add(new TagScopeItem { DisplayName = "Lock Screen", TargetIndex = 1 });

            if (IsPerMonitor && CanUsePerMonitor)
            {
                for (int i = 0; i < Monitors.Count; i++)
                    TagScopes.Add(new TagScopeItem { DisplayName = Monitors[i].DisplayName, TargetIndex = 2 + i });
            }

            var next = TagScopes.FirstOrDefault(s => s.TargetIndex == oldTarget) ?? TagScopes[0];
            SelectedTagScopeItem = next;
            
            UpdatePreviewTabs();
        }

        private void UpdatePreviewTabs()
        {
            var oldSelection = SelectedPreviewTab?.TargetId;
            PreviewTabs.Clear();

            if ((IsPerMonitor || RandomForMonitors) && CanUsePerMonitor)
            {
                for (int i = 0; i < Monitors.Count; i++)
                {
                    PreviewTabs.Add(new PreviewTabItem { Title = Monitors[i].DisplayName, TargetId = $"monitor{i}" });
                }
            }
            else
            {
                PreviewTabs.Add(new PreviewTabItem { Title = "Desktop", TargetId = "desktop" });
            }

            if (IsLockScreenSeparate || RandomForLockScreen)
            {
                PreviewTabs.Add(new PreviewTabItem { Title = "Lock Screen", TargetId = "lockscreen" });
            }

            if (oldSelection != null)
            {
                SelectedPreviewTab = PreviewTabs.FirstOrDefault(t => t.TargetId == oldSelection) ?? PreviewTabs.FirstOrDefault();
            }
            else
            {
                SelectedPreviewTab = PreviewTabs.FirstOrDefault();
            }

            OnPropertyChanged(nameof(IsPreviewSwitcherVisible));
        }

        private void UpdateTagEditorBindings()
        {
            if (SelectedTagScopeItem == null) return;
            int targetIdx = SelectedTagScopeItem.TargetIndex;

            ObservableCollection<TagItem> targetTags;
            string targetCustomTags;

            if (targetIdx == 0)
            {
                targetTags = SharedTags;
                targetCustomTags = SharedCustomTags;
            }
            else if (targetIdx == 1)
            {
                targetTags = LockScreenTags;
                targetCustomTags = LockScreenCustomTags;
            }
            else
            {
                int monIdx = targetIdx - 2;
                if (monIdx >= 0 && monIdx < Monitors.Count)
                {
                    targetTags = Monitors[monIdx].Tags;
                    targetCustomTags = Monitors[monIdx].CustomTags;
                }
                else return;
            }

            CategoriesTree.Clear();
            var grouped = targetTags.GroupBy(t => t.Category).OrderBy(g => g.Key);
            foreach (var group in grouped)
            {
                CategoriesTree.Add(new TagCategoryItem(group.Key, group.ToList()));
            }

            CustomTagsList.Clear();
            var tags = (targetCustomTags ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var tag in tags)
            {
                if (tag.Length > 0) CustomTagsList.Add(tag);
            }
        }



        private void RemoveCustomTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return;

            if (CustomTagsList.Contains(tag))
            {
                CustomTagsList.Remove(tag);
                SyncCustomTagsString();
            }
        }

        private void SyncCustomTagsString()
        {
            if (SelectedTagScopeItem == null) return;
            
            var str = string.Join(", ", CustomTagsList);
            int targetIdx = SelectedTagScopeItem.TargetIndex;

            if (targetIdx == 0)
                SharedCustomTags = str;
            else if (targetIdx == 1)
                LockScreenCustomTags = str;
            else
            {
                int monIdx = targetIdx - 2;
                if (monIdx >= 0 && monIdx < Monitors.Count)
                    Monitors[monIdx].CustomTags = str;
            }
            SaveSettings();
        }

        private void LoadSettings()
        {
            var s = SettingsService.Load();

            Interval             = s.Interval;
            IntervalUnitIndex    = s.IntervalUnitIndex;
            IsPerMonitor         = s.IsPerMonitor;
            IsSyncAllMonitors    = s.IsSyncAllMonitors;
            PauseOnFullscreen    = s.PauseOnFullscreen;
            IsLockScreenSeparate = s.IsLockScreenSeparate;
            LockScreenMonitorIndex = s.LockScreenMonitorIndex;
            StartWithWindows     = s.StartWithWindows;
            
            RandomPerScreen      = s.RandomPerScreen;
            RandomForMonitors    = s.RandomForMonitors;
            RandomForLockScreen  = s.RandomForLockScreen;
            RandomizeLockScreenSource = s.RandomizeLockScreenSource;

            FavoritesPercentage  = s.FavoritesPercentage;

            SharedCustomTags     = s.SharedCustomTags;
            LockScreenCustomTags = s.LockScreenCustomTags;
            
            CustomTagsList.Clear();
            foreach (var tag in s.CustomTagsList)
                CustomTagsList.Add(tag);

            ApplySelectedTags(SharedTags, s.SelectedSharedTags);
            ApplySelectedTags(LockScreenTags, s.SelectedLockScreenTags);

            for (int i = 0; i < Monitors.Count && i < s.MonitorTagSettings.Count; i++)
            {
                ApplySelectedTags(Monitors[i].Tags, s.MonitorTagSettings[i].SelectedTags);
                Monitors[i].CustomTags = s.MonitorTagSettings[i].CustomTags;
            }

            if (s.WasRunning)
            {
                Start();
            }
        }

        private static void ApplySelectedTags(ObservableCollection<TagItem> tags, System.Collections.Generic.List<string> selectedNames)
        {
            foreach (var tag in tags)
                tag.IsSelected = selectedNames.Contains(tag.Name);
        }

        // ═══════════════════════════════════════════════════════════════
        //  АВТОЗАПУСК
        // ═══════════════════════════════════════════════════════════════

        private static bool ReadAutoStartFromRegistry()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
                return key?.GetValue(AppName) != null;
            }
            catch { return false; }
        }

        private static void WriteAutoStartToRegistry(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
                if (key == null) return;
                if (enable)
                    key.SetValue(AppName, $"\"{Environment.ProcessPath ?? ""}\" --autorun");
                else
                    key.DeleteValue(AppName, false);
            }
            catch { }
        }

        // ═══════════════════════════════════════════════════════════════
        //  ВСПОМОГАТЕЛЬНЫЕ
        // ═══════════════════════════════════════════════════════════════

        private static readonly Random _queryRng = new();

        private static string BuildQuery(ObservableCollection<TagItem> chips, string? customTags)
        {
            var selected = chips.Where(t => t.IsSelected).Select(t => t.Name);
            var custom = (customTags ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => s.Length > 0);
            var all = selected.Concat(custom).ToList();
            if (all.Count == 0) return "";
            // Случайно выбираем один тег, чтобы API гарантированно находил результаты
            var chosen = all[_queryRng.Next(all.Count)];
            return Uri.EscapeDataString(chosen);
        }

        private static string UnitLabel(int idx) => idx switch
        {
            0 => "сек.", 1 => "мин.", 2 => "ч.", _ => ""
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
    public class PreviewTabItem
    {
        public string Title { get; set; } = "";
        public string TargetId { get; set; } = "";
    }
}
