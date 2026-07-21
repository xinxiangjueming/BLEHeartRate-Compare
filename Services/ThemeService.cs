using System;
using System.Windows;
using Microsoft.Win32;

namespace HeartRateMonitor.Services
{
    /// <summary>
    /// 管理应用主题（深色/浅色），自动跟随系统设置切换。
    /// </summary>
    public sealed class ThemeService : IDisposable
    {
        private readonly ResourceDictionary _lightTheme;
        private readonly ResourceDictionary _darkTheme;
        private ResourceDictionary _currentTheme;

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

            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
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

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General) return;

            bool dark = GetSystemDarkMode();
            if (dark == IsDarkMode) return;

            IsDarkMode = dark;
            ApplyTheme(dark);
            ThemeChanged?.Invoke(dark);
        }

        private void ApplyTheme(bool dark)
        {
            var app = Application.Current;
            var dicts = app.Resources.MergedDictionaries;

            int index = dicts.IndexOf(_currentTheme);
            var newTheme = dark ? _darkTheme : _lightTheme;

            if (index >= 0)
                dicts[index] = newTheme;
            else
                dicts.Add(newTheme);

            _currentTheme = newTheme;
        }

        public void Dispose()
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }
    }
}
