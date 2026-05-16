using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using HeartRateMonitor.ViewModels;

namespace HeartRateMonitor
{
    /// <summary>
    /// MainWindow 仅负责 ViewModel 初始化和 View 层事件桥接。
    /// 所有业务逻辑均在 MainViewModel 中。
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();

            _viewModel = new MainViewModel();
            _viewModel.GetOwnerWindow = () => this;
            DataContext = _viewModel;
        }

        /// <summary>
        /// 双击扫描设备列表 → 连接设备
        /// </summary>
        private void DeviceList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel.ConnectCommand.CanExecute(null))
                _viewModel.ConnectCommand.Execute(null);
        }

        /// <summary>
        /// 双击已连接设备名称 → 断开连接
        /// </summary>
        private void DeviceName_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2) return;

            if (sender is FrameworkElement element &&
                element.DataContext is ConnectedDevice connected)
            {
                _viewModel.DisconnectDevice(connected);
            }

            e.Handled = true;
        }

        /// <summary>
        /// 关闭窗口前提示导出数据
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            _viewModel.OnClosing();
            base.OnClosing(e);
        }

        /// <summary>
        /// 窗口关闭时释放资源
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            _viewModel.Dispose();
            base.OnClosed(e);
        }
    }
}
