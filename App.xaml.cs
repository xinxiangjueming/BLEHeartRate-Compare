using System;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Threading.Tasks;
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
            // 全局异常日志
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            ThemeService = new ThemeService();
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            Logger.Info("应用启动");
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                Logger.Error("未处理的 CLR 异常", ex);
            }
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            Logger.Error("未观察的 Task 异常", e.Exception);
            e.SetObserved();
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Logger.Error("UI 线程异常", e.Exception);

            // OxyPlot 渲染管线内部空引用，忽略即可
            if (e.Exception is System.NullReferenceException &&
                e.Exception.StackTrace?.Contains("OxyPlot") == true)
            {
                e.Handled = true;
            }
        }
    }

}
