using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Wolpope.Models
{
    // ═══════════════════════════════════════════════════════════════
    //  Модель тега
    // ═══════════════════════════════════════════════════════════════

    public class TagItem : INotifyPropertyChanged
    {
        public string Name     { get; }
        public string Category { get; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public TagItem(string name, string category)
        {
            Name     = name;
            Category = category;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public class TagCategoryItem : INotifyPropertyChanged
    {
        private bool? _isAllSelected = false;
        private bool _isUpdating = false;

        public string CategoryName { get; }
        public ObservableCollection<TagItem> Tags { get; }

        public TagCategoryItem(string categoryName, IEnumerable<TagItem> tags)
        {
            CategoryName = categoryName;
            Tags = new ObservableCollection<TagItem>(tags);

            foreach (var tag in Tags)
            {
                tag.PropertyChanged += Tag_PropertyChanged;
            }
            UpdateIsAllSelected();
        }

        public bool? IsAllSelected
        {
            get => _isAllSelected;
            set
            {
                if (_isAllSelected != value)
                {
                    _isAllSelected = value;
                    OnPropertyChanged();

                    if (_isAllSelected.HasValue)
                    {
                        _isUpdating = true;
                        foreach (var tag in Tags)
                        {
                            if (tag.IsSelected != _isAllSelected.Value)
                                tag.IsSelected = _isAllSelected.Value;
                        }
                        _isUpdating = false;
                    }
                }
            }
        }

        private void Tag_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!_isUpdating && e.PropertyName == nameof(TagItem.IsSelected))
            {
                UpdateIsAllSelected();
            }
        }

        private void UpdateIsAllSelected()
        {
            if (_isUpdating) return;
            
            int selectedCount = Tags.Count(t => t.IsSelected);
            bool? newValue = selectedCount == Tags.Count ? true :
                             selectedCount == 0 ? false :
                             (bool?)null;

            if (_isAllSelected != newValue)
            {
                _isAllSelected = newValue;
                OnPropertyChanged(nameof(IsAllSelected));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    // ═══════════════════════════════════════════════════════════════
    //  Модель монитора
    // ═══════════════════════════════════════════════════════════════

    public class MonitorInfo : INotifyPropertyChanged
    {
        public string DevicePath  { get; }
        public string DisplayName { get; }
        public ObservableCollection<TagItem> Tags { get; }

        /// <summary>Пользовательские теги, введённые вручную (через запятую)</summary>
        private string _customTags = "";
        public string CustomTags
        {
            get => _customTags;
            set { _customTags = value; OnPropertyChanged(); OnPropertyChanged(nameof(SelectedTagsQuery)); }
        }

        /// <summary>Путь к последнему скачанному файлу обоев (для lock screen reuse)</summary>
        public string? LastWallpaperPath { get; set; }

        private static readonly Random _queryRng = new();

        /// <summary>Собираем выбранные чипы + кастомные теги и выбираем один случайный</summary>
        public string SelectedTagsQuery
        {
            get
            {
                var chips = Tags.Where(t => t.IsSelected).Select(t => t.Name);
                var custom = (CustomTags ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(s => s.Length > 0);
                var all = chips.Concat(custom).ToList();
                if (all.Count == 0) return "";
                var chosen = all[_queryRng.Next(all.Count)];
                return Uri.EscapeDataString(chosen);
            }
        }

        public MonitorInfo(string devicePath, string displayName)
        {
            DevicePath  = devicePath;
            DisplayName = displayName;
            Tags        = new ObservableCollection<TagItem>(WallhavenTags.CreateDefaultTags());

            foreach (var tag in Tags)
                tag.PropertyChanged += (_, _) => OnPropertyChanged(nameof(SelectedTagsQuery));
        }

        public override string ToString() => DisplayName;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    // ═══════════════════════════════════════════════════════════════
    //  Полный каталог популярных тегов Wallhaven
    //  Источник: wallhaven.cc/tags/popular + /tags/tagged + /tags/viewed
    // ═══════════════════════════════════════════════════════════════

    public static class WallhavenTags
    {
        public static List<TagItem> CreateDefaultTags() => new()
        {
            // 1. Anime & Manga
            new("Characters", "Anime & Manga"),
            new("Other", "Anime & Manga"),
            new("Series", "Anime & Manga"),
            new("Visual Novels", "Anime & Manga"),

            // 2. Art & Design
            new("Architecture", "Art & Design"),
            new("Digital", "Art & Design"),
            new("Photography", "Art & Design"),
            new("Traditional", "Art & Design"),

            // 3. Entertainment
            new("Comic Books & Graphic Novels", "Entertainment"),
            new("Events", "Entertainment"),
            new("Games", "Entertainment"),
            new("Literature", "Entertainment"),
            new("Movies", "Entertainment"),
            new("Music", "Entertainment"),
            new("Sports", "Entertainment"),
            new("Television", "Entertainment"),

            // 4. Knowledge
            new("History", "Knowledge"),
            new("Holidays", "Knowledge"),
            new("Military & Weapons", "Knowledge"),
            new("Quotes", "Knowledge"),
            new("Religion", "Knowledge"),
            new("Science", "Knowledge"),

            // 5. Location
            new("Cities", "Location"),
            new("Countries", "Location"),
            new("Other", "Location"),
            new("Space", "Location"),

            // 6. Miscellaneous
            new("Clothing", "Miscellaneous"),
            new("Colors", "Miscellaneous"),
            new("Companies & Logos", "Miscellaneous"),
            new("Food", "Miscellaneous"),
            new("Technology", "Miscellaneous"),

            // 7. Nature
            new("Animals", "Nature"),
            new("Landscapes", "Nature"),
            new("Plants", "Nature"),

            // 8. People
            new("Artists", "People"),
            new("Celebrities", "People"),
            new("Fictional Characters", "People"),
            new("Models", "People"),
            new("Musicians", "People"),
            new("Other Figures", "People"),
            new("Photographers", "People"),
            new("Pornstars", "People"),

            // 9. Vehicles
            new("Aircraft", "Vehicles"),
            new("Cars & Motorcycles", "Vehicles"),
            new("Ships", "Vehicles"),
            new("Spacecrafts", "Vehicles"),
            new("Trains", "Vehicles"),
        };

        public static List<string> GetCategories() =>
            CreateDefaultTags().Select(t => t.Category).Distinct().ToList();
    }
}
