using System;
using System.Runtime.InteropServices;
using System.Windows;
using SnapTranslate.Win32;

namespace SnapTranslate
{
    public partial class App : Application
    {
        private MainWindow? _mainWindow;
        private Hardcodet.Wpf.TaskbarNotification.TaskbarIcon? _notifyIcon;
        private System.Drawing.Icon? _trayIcon;
        private static System.Threading.EventWaitHandle? _eventWaitHandle;

        // ===== Per-Monitor V2 DPI Awareness =====
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiFlag);

        [DllImport("SHCore.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwareness(int awareness);

        private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);
        private const int PROCESS_PER_MONITOR_DPI_AWARE = 2;

        private static void ConfigureDpiAwareness()
        {
            try
            {
                if (SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2))
                    return;
            }
            catch { }
            try
            {
                SetProcessDpiAwareness(PROCESS_PER_MONITOR_DPI_AWARE);
            }
            catch { }
        }

        // ===== Low-Level Keyboard Hook (WH_KEYBOARD_LL) =====
        // This intercepts keystrokes at the OS kernel level BEFORE any window receives them.
        // Returning a non-zero value from the hook completely suppresses the key system-wide.

        private static NativeMethods.LowLevelKeyboardProc? _hookProc; // must be field to prevent GC
        private static IntPtr _hookHandle = IntPtr.Zero;
        private static App? _instance;

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_KEYDOWN    = 0x0100;
        private const int VK_SPACE      = 0x20;
        private const int VK_MENU       = 0x12; // Alt

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        private static void InstallHook()
        {
            _hookProc = LowLevelKeyboardCallback;
            using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
            using var curModule  = curProcess.MainModule!;
            _hookHandle = NativeMethods.SetWindowsHookEx(
                WH_KEYBOARD_LL,
                _hookProc,
                GetModuleHandle(curModule.ModuleName),
                0);
        }

        private static void UninstallHook()
        {
            if (_hookHandle != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }
        }

        private static IntPtr LowLevelKeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && lParam != IntPtr.Zero)
                {
                    int msg = wParam.ToInt32();
                    if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                    {
                        var kbd = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                        bool altDown  = (GetAsyncKeyState(VK_MENU)  & 0x8000) != 0;
                        bool isSpace  = kbd.vkCode == VK_SPACE;

                        if (altDown && isSpace)
                        {
                            // Dispatch to UI thread and suppress the key entirely
                            _instance?.Dispatcher.BeginInvoke(() => _instance.StartSnipping());
                            return (IntPtr)1; // Non-zero = suppress: no other app sees this key
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in LowLevelKeyboardCallback: {ex.Message}");
            }
            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        // ===== Application lifecycle =====
        private static System.Threading.Mutex? _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            const string appName = "SnapTranslate_SingleInstance_Mutex";
            const string eventName = "SnapTranslate_SingleInstance_Event";

            _mutex = new System.Threading.Mutex(true, appName, out bool createdNew);

            if (!createdNew)
            {
                // App is already running, signal the existing instance to show its window and exit
                try
                {
                    using (var eventHandle = System.Threading.EventWaitHandle.OpenExisting(eventName))
                    {
                        eventHandle.Set();
                    }
                }
                catch { }
                Environment.Exit(0);
                return;
            }

            try
            {
                _eventWaitHandle = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, eventName);
                System.Threading.ThreadPool.QueueUserWorkItem(state =>
                {
                    while (true)
                    {
                        try
                        {
                            if (_eventWaitHandle.WaitOne())
                            {
                                _instance?.Dispatcher.BeginInvoke(() =>
                                {
                                    _mainWindow?.ShowWindow();
                                });
                            }
                        }
                        catch (ObjectDisposedException)
                        {
                            break;
                        }
                        catch { }
                    }
                });
            }
            catch { }

            ConfigureDpiAwareness();
            base.OnStartup(e);

            _instance = this;

            _mainWindow = new MainWindow();

            bool startMinimized = false;
            foreach (var arg in e.Args)
            {
                if (arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase) || 
                    arg.Equals("/minimized", StringComparison.OrdinalIgnoreCase))
                {
                    startMinimized = true;
                    break;
                }
            }

            if (!startMinimized)
            {
                _mainWindow.Show();
            }

            SetupTrayIcon();
            InstallHook();

            ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UnhandledException: {ex.Message}");
            }
        }

        private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            MessageBox.Show($"Đã xảy ra lỗi hệ thống: {e.Exception.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void OnUnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
        {
            e.SetObserved();
            System.Diagnostics.Debug.WriteLine($"UnobservedTaskException: {e.Exception.Message}");
        }

        protected override void OnExit(ExitEventArgs e)
        {
            UninstallHook();
            _notifyIcon?.Dispose();
            _trayIcon?.Dispose();
            if (_eventWaitHandle != null)
            {
                try
                {
                    _eventWaitHandle.Close();
                }
                catch { }
                _eventWaitHandle.Dispose();
            }
            if (_mutex != null)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch { }
                _mutex.Dispose();
            }
            base.OnExit(e);
        }

        private void StartSnipping()
        {
            _mainWindow?.StartSnipping();
        }

        // ===== Tray Icon =====

        private void SetupTrayIcon()
        {
            _notifyIcon = new Hardcodet.Wpf.TaskbarNotification.TaskbarIcon();

            try
            {
                var iconUri    = new Uri("pack://application:,,,/icon.ico");
                var resourceInfo = System.Windows.Application.GetResourceStream(iconUri);
                if (resourceInfo != null)
                {
                    using var iconStream = resourceInfo.Stream;
                    _trayIcon = new System.Drawing.Icon(iconStream);
                    _notifyIcon.Icon = _trayIcon;
                }
                else
                {
                    LoadFallbackIcon(_notifyIcon);
                }
            }
            catch
            {
                LoadFallbackIcon(_notifyIcon);
            }

            _notifyIcon.ToolTipText = "SnapTranslate";
            _notifyIcon.Visibility  = Visibility.Visible;

            var contextMenu = new System.Windows.Controls.ContextMenu();

            var showItem = new System.Windows.Controls.MenuItem { Header = "Hiện giao diện" };
            showItem.Click += (s, args) => _mainWindow?.ShowWindow();
            contextMenu.Items.Add(showItem);

            var snipItem = new System.Windows.Controls.MenuItem { Header = "Dịch (Alt+Space)" };
            snipItem.Click += (s, args) => StartSnipping();
            contextMenu.Items.Add(snipItem);

            contextMenu.Items.Add(new System.Windows.Controls.Separator());

            var exitItem = new System.Windows.Controls.MenuItem { Header = "Thoát" };
            exitItem.Click += (s, args) =>
            {
                _mainWindow?.SaveSettings();
                _notifyIcon.Dispose();
                Application.Current.Shutdown();
            };
            contextMenu.Items.Add(exitItem);

            _notifyIcon.ContextMenu         = contextMenu;
            _notifyIcon.TrayMouseDoubleClick += (s, args) => StartSnipping();
        }

        private void LoadFallbackIcon(Hardcodet.Wpf.TaskbarNotification.TaskbarIcon notifyIcon)
        {
            using var bmp        = new System.Drawing.Bitmap(32, 32);
            using var g          = System.Drawing.Graphics.FromImage(bmp);
            g.Clear(System.Drawing.Color.Transparent);
            using var brush      = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(66, 133, 244));
            g.FillEllipse(brush, 2, 2, 28, 28);
            using var whiteBrush = new System.Drawing.SolidBrush(System.Drawing.Color.White);
            g.FillEllipse(whiteBrush, 8, 8, 16, 16);
            var hIcon = bmp.GetHicon();
            try
            {
                using var tempIcon = System.Drawing.Icon.FromHandle(hIcon);
                _trayIcon?.Dispose();
                _trayIcon = (System.Drawing.Icon)tempIcon.Clone();
                notifyIcon.Icon = _trayIcon;
            }
            finally
            {
                NativeMethods.DestroyIcon(hIcon);
            }
        }
    }
}