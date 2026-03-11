using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace DevotionDesk
{
    public class PdfItem : INotifyPropertyChanged
    {
        private string _filePath = "";
        private string _displayName = "";
        private DateTime _lastModified;

        public string FilePath
        {
            get => _filePath;
            set
            {
                if (_filePath == value) return;
                _filePath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FileName));
            }
        }

        public string FileName => Path.GetFileName(FilePath);

        public string DisplayName
        {
            get => _displayName;
            set
            {
                if (_displayName == value) return;
                _displayName = value;
                OnPropertyChanged();
            }
        }

        public DateTime LastModified
        {
            get => _lastModified;
            set
            {
                if (_lastModified == value) return;
                _lastModified = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
