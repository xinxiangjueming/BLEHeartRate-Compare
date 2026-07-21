using System;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace HeartRateMonitor.Services
{
    /// <summary>
    /// 管理应用主题（深色/浅色），自动跟随系统设置切换。
    /// 同时使用事件和轮询双保险。
    /// </summary>
    public sealed class ThemeService : IDisposable
    {
        private readonly ResourceDictionary _lightTheme;
        private readonly ResourceDictionary _darkTheme;
        private ResourceDictionary _currentTheme;
        private readonly DispatcherTimer _pollTimer;

        /// <summary>
        /// 主题变化时触发。参数为 true 表示深色模式。
        /// </summary>
        public static event Action<bool>? ThemeChanged;

        /// <summary>
        /// 当前是否为深色模式。
        /// </summary>
        public bool IsDarkMode { get; private set; }

        public ThemeService()
        {
            _lightTheme = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Themes/LightTheme.xaml", UriKind.Absolute)
            };
            _darkTheme = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Themes/DarkTheme.xaml", UriKind.Absolute)
            };

            IsDarkMode = GetSystemDarkMode();
            _currentTheme = IsDarkMode ? _darkTheme : _lightTheme;
            Application.Current.Resources.MergedDictionaries.Add(_currentTheme);

            Logger.Info($"ThemeService 初始化: IsDarkMode={IsDarkMode}");

            // 事件监听（快速响应，但可能不稳定）
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            // 轮询检测（每 2 秒检查注册表，可靠兜底）
            _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _pollTimer.Tick += OnPollTick;
            _pollTimer.Start();
            Logger.Info("ThemeService 轮询 Timer 已启动 (2秒间隔)");
        }

        /// <summary>
        /// 读取注册表获取系统深色模式设置。
        /// </summary>
        private static bool GetSystemDarkMode()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var value = key?.GetValue("AppsUseLightTheme");
                return value is int v && v == 0;
            }
            catch
            {
                return false;
            }
        }

        private void OnPollTick(object? sender, EventArgs e)
        {
            bool dark = GetSystemDarkMode();
            if (dark == IsDarkMode) return;

            Logger.Info($"[轮询] 主题变化: {IsDarkMode} → {dark}");
            IsDarkMode = dark;
            ApplyTheme(dark);
            ThemeChanged?.Invoke(dark);
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General) return;

            bool dark = GetSystemDarkMode();
            if (dark == IsDarkMode) return;

            Logger.Info($"[事件] 检测到主题变化: {(dark ? "深色" : "浅色")}");
            IsDarkMode = dark;
            ApplyTheme(dark);
            ThemeChanged?.Invoke(dark);
        }

        private void ApplyTheme(bool dark)
        {
            var app = Application.Current;
            var dicts = app.Resources.MergedDictionaries;
            var newTheme = dark ? _darkTheme : _lightTheme;

            // 先移除旧字典，再添加新字典，强制 WPF 刷新 DynamicResource
            if (_currentTheme != null)
                dicts.Remove(_currentTheme);

            dicts.Add(newTheme);
            _currentTheme = newTheme;
        }

        public void Dispose()
        {
            _pollTimer.Stop();
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }
    }
}
