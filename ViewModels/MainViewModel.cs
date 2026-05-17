using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SnapTranslate.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private string _originalText = "";
        private string _translatedText = "";
        private string _sourceLang = "Phát hiện ngôn ngữ";
        private bool _isOcrLoading;
        private bool _isTransLoading;
        private bool _isClearButtonVisible;
        private bool _isCopyButtonVisible;
        private bool _isDebugPanelVisible;
        private int _totalTokens;
        private int _cacheHitTokens;
        private int _cacheMissTokens;
        private string _targetLang = "vi";

        public string OriginalText
        {
            get => _originalText;
            set
            {
                if (SetProperty(ref _originalText, value))
                {
                    IsClearButtonVisible = !string.IsNullOrEmpty(value);
                }
            }
        }

        public string TranslatedText
        {
            get => _translatedText;
            set
            {
                if (SetProperty(ref _translatedText, value))
                {
                    IsCopyButtonVisible = !string.IsNullOrEmpty(value);
                }
            }
        }

        public string SourceLang
        {
            get => _sourceLang;
            set => SetProperty(ref _sourceLang, value);
        }

        public bool IsOcrLoading
        {
            get => _isOcrLoading;
            set => SetProperty(ref _isOcrLoading, value);
        }

        public bool IsTransLoading
        {
            get => _isTransLoading;
            set => SetProperty(ref _isTransLoading, value);
        }

        public bool IsClearButtonVisible
        {
            get => _isClearButtonVisible;
            set => SetProperty(ref _isClearButtonVisible, value);
        }

        public bool IsCopyButtonVisible
        {
            get => _isCopyButtonVisible;
            set => SetProperty(ref _isCopyButtonVisible, value);
        }

        public bool IsDebugPanelVisible
        {
            get => _isDebugPanelVisible;
            set => SetProperty(ref _isDebugPanelVisible, value);
        }

        public int TotalTokens
        {
            get => _totalTokens;
            set
            {
                if (SetProperty(ref _totalTokens, value))
                {
                    UpdateDebugPanelVisibility();
                }
            }
        }

        public int CacheHitTokens
        {
            get => _cacheHitTokens;
            set
            {
                if (SetProperty(ref _cacheHitTokens, value))
                {
                    UpdateDebugPanelVisibility();
                }
            }
        }

        public int CacheMissTokens
        {
            get => _cacheMissTokens;
            set
            {
                if (SetProperty(ref _cacheMissTokens, value))
                {
                    UpdateDebugPanelVisibility();
                }
            }
        }

        public string TargetLang
        {
            get => _targetLang;
            set => SetProperty(ref _targetLang, value);
        }

        private void UpdateDebugPanelVisibility()
        {
            IsDebugPanelVisible = TotalTokens > 0 || CacheHitTokens > 0 || CacheMissTokens > 0;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(storage, value)) return false;
            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
