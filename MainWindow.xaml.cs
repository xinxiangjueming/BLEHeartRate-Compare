using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        /// 点击扫描设备列表中的"连接"文字 → 选中设备并连接
        /// </summary>
        private void ConnectDevice_Click(object sender, RoutedEventArgs e)
        {
            // 选中当前 ListBoxItem
            if (sender is FrameworkElement element)
            {
                var listBoxItem = FindParent<ListBoxItem>(element);
                if (listBoxItem != null)
                    listBoxItem.IsSelected = true;
            }

            if (_viewModel.ConnectCommand.CanExecute(null))
                _viewModel.ConnectCommand.Execute(null);
        }

        private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            if (parent == null) return null;
            if (parent is T typed) return typed;
            return FindParent<T>(parent);
        }

        /// <summary>
        /// 点击"断开"按钮 → 断开设备
        /// </summary>
        private void DisconnectDevice_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element &&
                element.DataContext is ConnectedDevice connected)
            {
                _viewModel.DisconnectDevice(connected);
            }
        }

        /// <summary>
        /// 点击"开始记录/停止记录"切换按钮
        /// </summary>
        private void ToggleRecording_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.ToggleRecordingFromView();
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
