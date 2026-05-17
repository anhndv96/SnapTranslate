using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using SnapTranslate.Win32;

namespace SnapTranslate
{
    public partial class SnippingWindow : Window
    {
        private Point _startPoint;
        private Point _endPoint;
        private bool _isDrawing;
        private bool _isClosed;

        private readonly System.Windows.Forms.Screen _screen;
        private readonly System.Drawing.Bitmap _screenBmp;
        private double _dpiScale;

        public event Action<byte[], int, int>? SnipCompleted;

        public SnippingWindow(System.Windows.Forms.Screen screen, System.Drawing.Bitmap screenBmp)
        {
            InitializeComponent();

            _screen = screen;
            _screenBmp = screenBmp;
            _dpiScale = GetDpiScale(screen);

            // Recover HDR if needed to prevent text burnout
            OptimizeForOcr(_screenBmp);

            // Configure manual DPI-independent coordinates positioning
            this.WindowStartupLocation = WindowStartupLocation.Manual;
            this.Left = _screen.Bounds.Left / _dpiScale;
            this.Top = _screen.Bounds.Top / _dpiScale;
            this.Width = _screen.Bounds.Width / _dpiScale;
            this.Height = _screen.Bounds.Height / _dpiScale;

            this.Loaded += OnLoaded;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            try
            {
                var hWnd = new WindowInteropHelper(this).Handle;

                // Force borderless tool window style to avoid taskbar or Alt-Tab inclusion
                int exStyle = NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE);
                NativeMethods.SetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE, exStyle | NativeMethods.WS_EX_TOOLWINDOW);

                // Use Win32 SetWindowPos to force physical pixel positioning on the target screen
                NativeMethods.SetWindowPos(hWnd, (IntPtr)(-1) /* HWND_TOPMOST */,
                    _screen.Bounds.Left, _screen.Bounds.Top,
                    _screen.Bounds.Width, _screen.Bounds.Height,
                    (uint)(NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_NOACTIVATE));
            }
            catch { }
        }

        protected override void OnClosed(EventArgs e)
        {
            _isClosed = true;
            try
            {
                _screenBmp?.Dispose();
            }
            catch { }
            base.OnClosed(e);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            this.Topmost = true;
            this.Focus();

            // Bind background image to captured bitmap with exact monitor DPI representation
            BackgroundImage.Source = BitmapToBitmapSource(_screenBmp, _dpiScale);

            // Initialize full screen dark mask to window logical coordinates
            FullMaskGeometry.Rect = new Rect(0, 0, this.ActualWidth, this.ActualHeight);
            SelectionGeometry.Rect = Rect.Empty;
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                this.Close();
        }

        private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _startPoint = e.GetPosition(OverlayCanvas);
                _endPoint = _startPoint;
                _isDrawing = true;
                SelectionRect.Visibility = Visibility.Visible;
                SelectionGeometry.Rect = Rect.Empty;
            }
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            var mousePos = e.GetPosition(OverlayCanvas);

            // Track custom Google Lens cursor
            if (CustomCursor.Visibility != Visibility.Visible)
                CustomCursor.Visibility = Visibility.Visible;

            System.Windows.Controls.Canvas.SetLeft(CustomCursor, mousePos.X + 12);
            System.Windows.Controls.Canvas.SetTop(CustomCursor, mousePos.Y + 12);

            if (!_isDrawing) return;
            _endPoint = mousePos;

            var x = Math.Min(_startPoint.X, _endPoint.X);
            var y = Math.Min(_startPoint.Y, _endPoint.Y);
            var w = Math.Abs(_endPoint.X - _startPoint.X);
            var h = Math.Abs(_endPoint.Y - _startPoint.Y);

            System.Windows.Controls.Canvas.SetLeft(SelectionRect, x);
            System.Windows.Controls.Canvas.SetTop(SelectionRect, y);
            SelectionRect.Width = w;
            SelectionRect.Height = h;

            // Cut transparent hole in mask
            SelectionGeometry.Rect = new Rect(x, y, w, h);
        }

        private void Canvas_MouseLeave(object sender, MouseEventArgs e)
        {
            CustomCursor.Visibility = Visibility.Collapsed;
        }

        private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDrawing) return;
            _isDrawing = false;

            _endPoint = e.GetPosition(OverlayCanvas);
            var logicalRect = new Rect(
                Math.Min(_startPoint.X, _endPoint.X),
                Math.Min(_startPoint.Y, _endPoint.Y),
                Math.Abs(_endPoint.X - _startPoint.X),
                Math.Abs(_endPoint.Y - _startPoint.Y));

            SelectionGeometry.Rect = Rect.Empty;

            if (logicalRect.Width > 10 && logicalRect.Height > 10)
            {
                CropAndNotify(logicalRect);
            }
            else
            {
                this.Close();
            }
        }

        private void CropAndNotify(Rect logicalRect)
        {
            this.Hide();

            try
            {
                // Translate WPF logical DIP bounds back to physical pixels
                int physX = (int)Math.Round(logicalRect.X * _dpiScale);
                int physY = (int)Math.Round(logicalRect.Y * _dpiScale);
                int physW = (int)Math.Round(logicalRect.Width * _dpiScale);
                int physH = (int)Math.Round(logicalRect.Height * _dpiScale);

                // Math bounds clamping to prevent memory clone crashes
                physX = Math.Clamp(physX, 0, _screenBmp.Width - 1);
                physY = Math.Clamp(physY, 0, _screenBmp.Height - 1);
                physW = Math.Clamp(physW, 1, _screenBmp.Width - physX);
                physH = Math.Clamp(physH, 1, _screenBmp.Height - physY);

                using var cropped = _screenBmp.Clone(
                    new System.Drawing.Rectangle(physX, physY, physW, physH),
                    System.Drawing.Imaging.PixelFormat.Format24bppRgb);

                using var ms = new MemoryStream();
                cropped.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                var pngBytes = ms.ToArray();

                SnipCompleted?.Invoke(pngBytes, physW, physH);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi cắt ảnh: {ex.Message}", "Lỗi",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (!_isClosed)
                {
                    _isClosed = true;
                    try { this.Close(); } catch { }
                }
            }
        }

        // --- Fast low-overhead Win32 DPI Query per monitor ---
        private static double GetDpiScale(System.Windows.Forms.Screen screen)
        {
            try
            {
                var pt = new System.Drawing.Point(screen.Bounds.Left + 1, screen.Bounds.Top + 1);
                var hMonitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
                if (hMonitor != IntPtr.Zero)
                {
                    NativeMethods.GetDpiForMonitor(hMonitor, 0 /* MDT_EFFECTIVE_DPI */, out uint dpiX, out uint dpiY);
                    return dpiX / 96.0;
                }
            }
            catch { }
            return 1.0;
        }

        // --- Ultra fast CPU Pointer-based HDR Tone Mapping & Contrast Stretch ---
        public static void OptimizeForOcr(System.Drawing.Bitmap bmp)
        {
            int width = bmp.Width;
            int height = bmp.Height;

            var rect = new System.Drawing.Rectangle(0, 0, width, height);
            var bmpData = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            try
            {
                unsafe
                {
                    byte* ptr = (byte*)bmpData.Scan0;
                    int bytesPerPixel = 4;
                    int totalBytes = width * height * bytesPerPixel;

                    int minL = 255;
                    int maxL = 0;

                    // Sample 1/8th of the pixels for top-tier CPU performance
                    for (int i = 0; i < totalBytes; i += bytesPerPixel * 8)
                    {
                        byte b = ptr[i];
                        byte g = ptr[i + 1];
                        byte r = ptr[i + 2];
                        int l = (r + g + b) / 3;
                        if (l < minL) minL = l;
                        if (l > maxL) maxL = l;
                    }

                    // Detect washed out/overexposed HDR screenshot
                    if (minL > 80 && maxL - minL < 170 && maxL - minL > 15)
                    {
                        double scale = 255.0 / (maxL - minL);
                        for (int i = 0; i < totalBytes; i += bytesPerPixel)
                        {
                            int b = ptr[i];
                            int g = ptr[i + 1];
                            int r = ptr[i + 2];

                            ptr[i] = (byte)Math.Clamp((b - minL) * scale, 0, 255);
                            ptr[i + 1] = (byte)Math.Clamp((g - minL) * scale, 0, 255);
                            ptr[i + 2] = (byte)Math.Clamp((r - minL) * scale, 0, 255);
                        }
                    }
                }
            }
            finally
            {
                bmp.UnlockBits(bmpData);
            }
        }

        // --- Ultra-fast GDI-pointer-based WPF BitmapSource Creator ---
        private static BitmapSource BitmapToBitmapSource(System.Drawing.Bitmap bitmap, double dpiScale)
        {
            var rect = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var bitmapData = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var size = bitmapData.Stride * bitmapData.Height;
                double dpi = 96.0 * dpiScale;
                return BitmapSource.Create(
                    bitmapData.Width, bitmapData.Height,
                    dpi, dpi,
                    System.Windows.Media.PixelFormats.Bgra32,
                    null,
                    bitmapData.Scan0, size, bitmapData.Stride);
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }
        }
    }
}