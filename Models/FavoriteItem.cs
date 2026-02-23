using System.Windows.Media.Imaging;

namespace Wolpope.Models
{
    public class FavoriteItem : System.ComponentModel.INotifyPropertyChanged
    {
        public string FilePath { get; set; } = "";
        
        public BitmapImage? Thumbnail { get; set; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }
}
