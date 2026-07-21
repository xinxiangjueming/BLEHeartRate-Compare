using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using HeartRateMonitor.Services;

namespace HeartRateMonitor
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static ThemeService ThemeService { get; private set; } = null!;

        public App()
        {
            ThemeService = new ThemeService();
            DispatcherUnhandledException += OnDispatcherUnhandledException;
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // OxyPlot 渲染管线内部空引用，忽略即可
            if (e.Exception is System.NullReferenceException &&
                e.Exception.StackTrace?.Contains("OxyPlot") == true)
            {
                Debug.WriteLine($"[OxyPlot] 已忽略渲染异常: {e.Exception.Message}");
                e.Handled = true;
            }
        }
    }

}
