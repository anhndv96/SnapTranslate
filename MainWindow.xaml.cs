using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Reflection;
using System.Windows.Media.Animation;
using SnapTranslate.Models;
using SnapTranslate.Services;
using SnapTranslate.ViewModels;
using SnapTranslate.Win32;
using System.Threading;

namespace SnapTranslate
{ 
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly LensOcrService _ocrService = new();

        private readonly ITranslateService _googleService = new GoogleTranslateService();
        private readonly ITranslateService _llmService = new LlmOpenAiService();
        private readonly ITranslateService _deepSeekService = new DeepSeekTranslateService();
        private ITranslateService _activeTranslateService;
        private readonly List<SnippingWindow> _activeSnippingWindows = new();
        private string _targetLang = "vi";
        private DispatcherTimer? _typeTimer;
        private System.Threading.Timer? _streamTimer;
        private readonly System.Text.StringBuilder _streamBuffer = new();
        private readonly System.Text.StringBuilder _translatedAccumulator = new();
        private readonly object _streamLock = new();
        private bool _isTranslating;
        private bool _isUpdating;
        private bool _isHeightManuallyResized = false;
        private bool _isInitialized = false;
        private string _lastTranslatedText = "";
        private string _lastTranslatedLang = "";

        public static readonly Dictionary<string, string> LangMap = new()
        {
            ["en"] = "Tiếng Anh",
            ["ja"] = "Tiếng Nhật",
            ["zh-CN"] = "Tiếng Trung (Giản Thể)",
            ["zh-TW"] = "Tiếng Trung (Phồn Thể)",
            ["zh"] = "Tiếng Trung",
            ["ko"] = "Tiếng Hàn",
            ["fr"] = "Tiếng Pháp",
            ["vi"] = "Tiếng Việt",
            ["ru"] = "Tiếng Nga",
            ["de"] = "Tiếng Đức",
            ["es"] = "Tiếng Tây Ban Nha",
            ["th"] = "Tiếng Thái",
            ["it"] = "Tiếng Ý",
            ["pt"] = "Tiếng Bồ Đào Nha",
            ["id"] = "Tiếng Indonesia",
            ["ms"] = "Tiếng Malaysia",
            ["tl"] = "Tiếng Tagalog",
            ["ar"] = "Tiếng Ả Rập",
            ["tr"] = "Tiếng Thổ Nhĩ Kỳ",
            ["nl"] = "Tiếng Hà Lan",
            ["pl"] = "Tiếng Ba Lan",
            ["uk"] = "Tiếng Ukraine",
            ["auto"] = "Tự động",
            ["unknown"] = "Không rõ"
        };

        public MainWindow()
        {
            AppSettings.Load();
            AppSettings.ApplyStartupSetting();
            _viewModel = new MainViewModel();
            this.DataContext = _viewModel;
            InitializeComponent();
            _activeTranslateService = _googleService; // Default

            if (AppSettings.Current.WindowWidth > 0)
            {
                this.Width = AppSettings.Current.WindowWidth;
            }

            this.Loaded += OnLoaded;
            this.MouseLeftButtonDown += OnHeaderDrag;
            this.Closing += (s, e) => SaveSettings();

            // Setup debounce timer for auto-translate on text edit
            _typeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(800)
            };
            _typeTimer.Tick += OnTypeTimerTick;

            // _streamTimer is created on-demand in DoTranslateAsync

            UpdateTabStyles();
            
            try
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                if (version != null)
                {
                    AppVersionText.Text = $"v{version.Major}.{version.Minor}.{version.Build}";
                }
            }
            catch { }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var hWnd = new WindowInteropHelper(this).Handle;
            NativeMethods.HideFromTaskbarAndAltTab(this);

            // Bound window height to screen's working area (without Taskbar) minus 40px buffer
            this.MaxHeight = SystemParameters.WorkArea.Height - 40;

            if (AppSettings.Current.WindowWidth > 0)
                this.Width = AppSettings.Current.WindowWidth;

            if (AppSettings.Current.SelectedEngineIndex >= 0 && AppSettings.Current.SelectedEngineIndex < EngineSelector.Items.Count)
                EngineSelector.SelectedIndex = AppSettings.Current.SelectedEngineIndex;

            // Position window at center of screen
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            Left = (screenWidth - (double.IsNaN(this.Width) ? 500 : this.Width)) / 2;
            Top = 80;

            ApplyAutoHeight();



            _isInitialized = true; // Mark initialization complete
        }

        private void ApplyAutoHeight()
        {
            if (!_isHeightManuallyResized)
            {
                this.Height = double.NaN;
                this.SizeToContent = SizeToContent.Height;
            }
        }

        public void SaveSettings()
        {
            if (this.Visibility == Visibility.Visible && this.ActualWidth > 0)
            {
                AppSettings.Current.WindowWidth = this.ActualWidth;
                if (EngineSelector != null && EngineSelector.SelectedIndex >= 0)
                {
                    AppSettings.Current.SelectedEngineIndex = EngineSelector.SelectedIndex;
                }
            }
            AppSettings.Save();
        }

        private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                    this.DragMove();
            }
            catch (InvalidOperationException)
            {
                // Prevent crash if drag state is invalid
            }
        }

        private void OnSnipClick(object sender, RoutedEventArgs e)
        {
            StartSnipping();
        }

        private bool _isSnippingInProgress = false;

        public async void StartSnipping()
        {
            if (_isSnippingInProgress) return;
            _isSnippingInProgress = true;

            // 1. Hide main window first so it is not in screenshots
            this.Hide();
            await Task.Delay(180); // Let hide animation fully settle

            CloseAllSnippingWindows();

            try
            {
                // 2. Query all available monitors
                var screens = System.Windows.Forms.Screen.AllScreens;
                foreach (var screen in screens)
                {
                    // 3. Take native high-fidelity physical screenshot of this monitor
                    int width = screen.Bounds.Width;
                    int height = screen.Bounds.Height;
                    
                    var bmp = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    using (var g = System.Drawing.Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen(screen.Bounds.X, screen.Bounds.Y, 0, 0, screen.Bounds.Size, System.Drawing.CopyPixelOperation.SourceCopy);
                    }

                    // 4. Instantiate DPI-aware SnippingWindow for this screen
                    var win = new SnippingWindow(screen, bmp);
                    win.SnipCompleted += OnSnipCompleted;
                    
                    // Safe cleanup if user cancels (escapes or clicks away)
                    win.Closed += (s, e) => {
                        // Use dispatcher to safely close all windows from UI thread
                        Dispatcher.BeginInvoke(new Action(() => CloseAllSnippingWindows()));
                    };

                    _activeSnippingWindows.Add(win);
                }

                // 5. Open all overlays simultaneously
                foreach (var win in _activeSnippingWindows)
                {
                    win.Show();
                    win.Activate();
                }
            }
            catch (Exception ex)
            {
                CloseAllSnippingWindows();
                MessageBox.Show($"Lỗi chuẩn bị chụp màn hình: {ex.Message}", "Lỗi",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                ShowWindow();
            }
            finally
            {
                _isSnippingInProgress = false;
            }
        }

        private void CloseAllSnippingWindows()
        {
            var windowsToClose = new List<SnippingWindow>(_activeSnippingWindows);
            _activeSnippingWindows.Clear();
            foreach (var win in windowsToClose)
            {
                try
                {
                    win.Close();
                }
                catch { }
            }
        }

        private void OnMinimizeClick(object sender, RoutedEventArgs e)
        {
            _isHeightManuallyResized = false; // Restore auto-scale for next open
            SaveSettings();
            this.Hide();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            _isHeightManuallyResized = false; // Restore auto-scale for next open
            SaveSettings();
            this.Hide();
        }

        private void OnSettingsClick(object sender, RoutedEventArgs e)
        {
            var win = new SettingsWindow();
            win.Owner = this;
            win.ShowDialog();
        }

        public void ShowWindow()
        {
            ApplyAutoHeight();

            this.Show();
            this.Activate();
            this.Topmost = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                this.Topmost = false;
            }));
        }

        private async void OnSnipCompleted(byte[] pngBytes, int width, int height)
        {
            CloseAllSnippingWindows();
            await ProcessImage(pngBytes, width, height);
        }

        private void StartLogoAnimation()
        {
            try
            {
                var sb = (System.Windows.Media.Animation.Storyboard)FindResource("LogoLoadingStoryboard");
                sb?.Begin(this, true);
            }
            catch { }
        }

        private void StopLogoAnimation()
        {
            try
            {
                var sb = (System.Windows.Media.Animation.Storyboard)FindResource("LogoLoadingStoryboard");
                sb?.Stop(this);
            }
            catch { }
        }

        private void StartOcrLoading()
        {
            _viewModel.IsOcrLoading = true;
            StartLogoAnimation();
            try
            {
                var sb = (System.Windows.Media.Animation.Storyboard)FindResource("OcrBouncingDotsStoryboard");
                sb?.Begin(this, true);
            }
            catch { }
        }

        private void StopOcrLoading()
        {
            _viewModel.IsOcrLoading = false;
            StopLogoAnimation();
            try
            {
                var sb = (System.Windows.Media.Animation.Storyboard)FindResource("OcrBouncingDotsStoryboard");
                sb?.Stop(this);
            }
            catch { }
        }

        private void StartTransLoading()
        {
            _viewModel.IsTransLoading = true;
            StartLogoAnimation();
            try
            {
                var sb = (System.Windows.Media.Animation.Storyboard)FindResource("TransBouncingDotsStoryboard");
                sb?.Begin(this, true);
            }
            catch { }
        }

        private void StopTransLoading()
        {
            _viewModel.IsTransLoading = false;
            StopLogoAnimation();
            try
            {
                var sb = (System.Windows.Media.Animation.Storyboard)FindResource("TransBouncingDotsStoryboard");
                sb?.Stop(this);
            }
            catch { }
        }

        private async Task ProcessImage(byte[] pngBytes, int width, int height)
        {
            _isUpdating = true;
            _viewModel.OriginalText = "";
            _viewModel.TranslatedText = "";
            _viewModel.SourceLang = "Phát hiện ngôn ngữ";
            ShowWindow();
            StartOcrLoading();

            try
            {
                var result = await _ocrService.PerformOcrAsync(pngBytes, width, height);
                DisplayOcrResult(result);
            }
            catch (Exception ex)
            {
                _isUpdating = false;
                _viewModel.OriginalText = $"Lỗi: {ex.Message}";
                StopOcrLoading();
            }
        }

        private void UpdateLanguageDetectionImmediate(string text)
        {
            text = text?.Trim() ?? "";
            if (string.IsNullOrEmpty(text) || text == "Đang trích xuất...") return;

            var localDetected = Services.LanguageDetector.Detect(text);
            if (!string.IsNullOrEmpty(localDetected))
            {
                var langName = LangMap.TryGetValue(localDetected, out var name)
                    ? name : localDetected;
                _viewModel.SourceLang = $"Phát hiện: {langName}";
            }
        }

        private async void DisplayOcrResult(OcrResult result)
        {
            _isUpdating = true;
            if (result.HasError)
            {
                _viewModel.OriginalText = result.Error!;
                _isUpdating = false;
                StopOcrLoading();
                return;
            }

            _viewModel.OriginalText = result.Text;
            _isUpdating = false;

            StopOcrLoading();
            UpdateLanguageDetectionImmediate(result.Text);
            await DoTranslateAsync();
        }

        private void OnOriginalTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdating) return;
            UpdateLanguageDetectionImmediate(_viewModel.OriginalText);
            _typeTimer?.Stop();
            _typeTimer?.Start();
        }

        private void OnClearClick(object sender, RoutedEventArgs e)
        {
            _viewModel.OriginalText = "";
            _viewModel.TranslatedText = "";
            _viewModel.SourceLang = "Phát hiện ngôn ngữ";
            _viewModel.TotalTokens = 0;
            _viewModel.CacheHitTokens = 0;
            _viewModel.CacheMissTokens = 0;
        }

        private void OnTypeTimerTick(object? sender, EventArgs e)
        {
            _typeTimer?.Stop();
            _ = DoTranslateAsync();
        }

        private void OnEngineChanged(object sender, SelectionChangedEventArgs e)
        {
            if (EngineSelector == null) return;
            var selected = (EngineSelector.SelectedItem as ComboBoxItem)?.Content?.ToString();
            
            if (selected == "Local LLM")
            {
                _activeTranslateService = _llmService;
                _viewModel.TotalTokens = 0;
                _viewModel.CacheHitTokens = 0;
                _viewModel.CacheMissTokens = 0;
                mainWindowGrid.Margin = new Thickness(10, 10, 10, 10);
            }
            else if (selected == "DeepSeek")
            {
                _activeTranslateService = _deepSeekService;
            }
            else
            {
                _activeTranslateService = _googleService;
                _viewModel.TotalTokens = 0;
                _viewModel.CacheHitTokens = 0;
                _viewModel.CacheMissTokens = 0;
                mainWindowGrid.Margin = new Thickness(10, 10, 10, 10);
            }
            
            if (_isInitialized)
            {
                SaveSettings();
            }

            if (!string.IsNullOrWhiteSpace(_viewModel.OriginalText))
                _ = DoTranslateAsync();
        }

        private async Task DoTranslateAsync()
        {
            var text = _viewModel.OriginalText?.Trim() ?? "";
            if (string.IsNullOrEmpty(text) || _isTranslating) return;

            _isTranslating = true;
            _lastTranslatedText = text;
            _lastTranslatedLang = _targetLang;
            
            _viewModel.TranslatedText = "";
            StartTransLoading();

            // Reset accumulators
            lock (_streamLock)
            {
                _streamBuffer.Clear();
                _translatedAccumulator.Clear();
            }

            // --- Performance: freeze layout during streaming ---
            // 1. Disable SizeToContent so window doesn't relayout on every text change
            this.SizeToContent = SizeToContent.Manual;
            if (double.IsNaN(this.Height) || this.Height < this.ActualHeight)
                this.Height = this.ActualHeight;
            // 2. Disable undo tracking on the TextBox (very expensive during rapid updates)
            TranslatedTextBox.IsUndoEnabled = false;

            // Create a thread-pool timer that flushes buffered chunks to the UI every 100ms
            _streamTimer?.Dispose();
            _streamTimer = new System.Threading.Timer(_ => DrainStreamBuffer(), null, 100, 100);

            try
            {
                var result = await _activeTranslateService.TranslateAsync(text, _targetLang, chunk =>
                {
                    lock (_streamLock)
                    {
                        _streamBuffer.Append(chunk);
                    }
                });

                // Stop timer and flush any remaining buffered chunks
                _streamTimer?.Dispose();
                _streamTimer = null;
                DrainStreamBuffer();
                if (string.IsNullOrWhiteSpace(_viewModel.OriginalText))
                {
                    _viewModel.TranslatedText = "";
                    _viewModel.SourceLang = "Phát hiện ngôn ngữ";
                    _viewModel.TotalTokens = 0;
                    _viewModel.CacheHitTokens = 0;
                    _viewModel.CacheMissTokens = 0;
                    return;
                }

                _viewModel.TranslatedText = result.Translated;

                if (!string.IsNullOrEmpty(result.Detected))
                {
                    var langName = LangMap.TryGetValue(result.Detected, out var name)
                        ? name : result.Detected;
                    _viewModel.SourceLang = $"Phát hiện: {langName}";
                }

                if (_activeTranslateService is DeepSeekTranslateService && result.TotalTokens > 0)
                {
                    _viewModel.TotalTokens = result.TotalTokens;
                    _viewModel.CacheHitTokens = result.CacheHitTokens;
                    _viewModel.CacheMissTokens = result.CacheMissTokens;
                    mainWindowGrid.Margin = new Thickness(10, 10, 10, 5);
                }
                else
                {
                    _viewModel.TotalTokens = 0;
                    _viewModel.CacheHitTokens = 0;
                    _viewModel.CacheMissTokens = 0;
                    mainWindowGrid.Margin = new Thickness(10, 10, 10, 10);
                }
            }
            catch
            {
                _viewModel.TranslatedText = "Lỗi kết nối dịch thuật...";
            }
            finally
            {
                _isTranslating = false;
                StopTransLoading();
                _streamTimer?.Dispose();
                _streamTimer = null;
                DrainStreamBuffer();

                // --- Restore layout after streaming ---
                TranslatedTextBox.IsUndoEnabled = true;
                if (!_isHeightManuallyResized)
                {
                    ApplyAutoHeight();
                }

                if ((_viewModel.OriginalText?.Trim() ?? "") != _lastTranslatedText || _targetLang != _lastTranslatedLang)
                {
                    _ = DoTranslateAsync();
                }
            }
        }

        private void OnTabViClick(object sender, RoutedEventArgs e)
        {
            if (_targetLang != "vi")
            {
                _targetLang = "vi";
                UpdateTabStyles();
                if (!string.IsNullOrWhiteSpace(_viewModel.OriginalText))
                    _ = DoTranslateAsync();
            }
        }

        private void OnTabEnClick(object sender, RoutedEventArgs e)
        {
            if (_targetLang != "en")
            {
                _targetLang = "en";
                UpdateTabStyles();
                if (!string.IsNullOrWhiteSpace(_viewModel.OriginalText))
                    _ = DoTranslateAsync();
            }
        }

        private void OnTranslatedTextChanged(object sender, TextChangedEventArgs e)
        {
            // Handled via two-way data bindings in the ViewModel
        }

        private void OnCopyClick(object sender, RoutedEventArgs e)
        {
            string textToCopy = _viewModel.TranslatedText;
            if (string.IsNullOrEmpty(textToCopy)) return;

            try
            {
                // Attempt to copy using Windows Forms Clipboard with built-in retry logic
                // (10 retries, 100ms delay between attempts)
                System.Windows.Forms.Clipboard.SetDataObject(textToCopy, true, 10, 100);
                ShowToast();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WinForms Clipboard SetDataObject failed: {ex.Message}");
                
                // Fallback to WPF Clipboard SetText (non-persistent, lighter COM operation)
                try
                {
                    System.Windows.Clipboard.SetText(textToCopy);
                    ShowToast();
                }
                catch (Exception ex2)
                {
                    System.Diagnostics.Debug.WriteLine($"WPF Clipboard fallback failed: {ex2.Message}");
                    
                    // Final fallback: attempt WPF SetDataObject without lifetime persistence
                    try
                    {
                        System.Windows.Clipboard.SetDataObject(textToCopy, false);
                        ShowToast();
                    }
                    catch (Exception ex3)
                    {
                        System.Diagnostics.Debug.WriteLine($"All clipboard operations failed: {ex3.Message}");
                        MessageBox.Show("Không thể sao chép văn bản do bộ nhớ tạm đang bị khóa bởi ứng dụng khác.", "Lỗi sao chép", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
        }

        private int _toastId = 0;
        private async void ShowToast()
        {
            int currentId = ++_toastId;
            
            ToastBorder.Visibility = Visibility.Visible;
            ToastBorder.BeginAnimation(UIElement.OpacityProperty, null);
            ToastTransform.BeginAnimation(TranslateTransform.YProperty, null);
            
            var duration = TimeSpan.FromMilliseconds(250);
            var fadeIn = new DoubleAnimation(0, 1, duration) { EasingFunction = new QuadraticEase() };
            var slideDown = new DoubleAnimation(-20, 0, duration) { EasingFunction = new QuadraticEase() };
            
            ToastBorder.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            ToastTransform.BeginAnimation(TranslateTransform.YProperty, slideDown);
            
            await Task.Delay(2000); // Wait 2s
            
            if (currentId != _toastId) return; // A new toast was started
            
            var fadeOut = new DoubleAnimation(1, 0, duration) { EasingFunction = new QuadraticEase() };
            var slideUp = new DoubleAnimation(0, -20, duration) { EasingFunction = new QuadraticEase() };
            
            fadeOut.Completed += (s, e) => {
                if (currentId == _toastId)
                    ToastBorder.Visibility = Visibility.Collapsed;
            };

            ToastBorder.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            ToastTransform.BeginAnimation(TranslateTransform.YProperty, slideUp);
        }

        /// <summary>
        /// Drains the stream buffer and pushes accumulated text to the UI.
        /// Safe to call from any thread.
        /// </summary>
        private void DrainStreamBuffer()
        {
            string fullText;
            lock (_streamLock)
            {
                if (_streamBuffer.Length == 0) return;
                _translatedAccumulator.Append(_streamBuffer);
                _streamBuffer.Clear();
                fullText = _translatedAccumulator.ToString();
            }

            Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                if (_viewModel.OriginalText?.Trim() == _lastTranslatedText)
                {
                    if (_viewModel.IsTransLoading)
                    {
                        StopTransLoading();
                    }
                    // Write directly to the TextBox, bypassing data binding
                    // to avoid PropertyChanged → Binding → Measure/Arrange chain
                    TranslatedTextBox.Text = fullText;
                    // Keep ViewModel in sync (won't trigger re-render since TextBox already has the value)
                    _viewModel.TranslatedText = fullText;
                }
            });
        }

        private void UpdateTabStyles()
        {
            var activeStyle = FindResource("ActiveTabButtonStyle") as Style;
            var inactiveStyle = FindResource("TabButtonStyle") as Style;

            TabVi.Style = _targetLang == "vi" ? activeStyle : inactiveStyle;
            TabEn.Style = _targetLang == "en" ? activeStyle : inactiveStyle;
        }

        // Existing Resize_DragDelta method
        private void Resize_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            if (sender is System.Windows.Controls.Primitives.Thumb thumb && thumb.Tag is string tag)
            {
                double minWidth = this.MinWidth > 0 ? this.MinWidth : 400;
                double minHeight = this.MinHeight > 0 ? this.MinHeight : 300;

                if (tag.Contains("R"))
                {
                    double newWidth = this.ActualWidth + e.HorizontalChange;
                    if (newWidth >= minWidth) this.Width = newWidth;
                }
                if (tag.Contains("B"))
                {
                    this.SizeToContent = SizeToContent.Manual; // Disable auto-scale first so height is editable!
                    _isHeightManuallyResized = true;
                    double newHeight = this.ActualHeight + e.VerticalChange;
                    if (newHeight >= minHeight) this.Height = newHeight;
                }
                if (tag.Contains("L"))
                {
                    double change = Math.Min(e.HorizontalChange, this.ActualWidth - minWidth);
                    this.Width = this.ActualWidth - change;
                    this.Left += change;
                }
                if (tag.Contains("T"))
                {
                    this.SizeToContent = SizeToContent.Manual; // Disable auto-scale first so height is editable!
                    _isHeightManuallyResized = true;
                    double change = Math.Min(e.VerticalChange, this.ActualHeight - minHeight);
                    this.Height = this.ActualHeight - change;
                    this.Top += change;
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_typeTimer != null)
            {
                _typeTimer.Stop();
                _typeTimer.Tick -= OnTypeTimerTick;
                _typeTimer = null;
            }
            this.Loaded -= OnLoaded;
            this.MouseLeftButtonDown -= OnHeaderDrag;
            base.OnClosed(e);
        }
    }
}