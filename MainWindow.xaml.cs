using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HeartRateMonitor.ViewModels;
using HeartRateMonitor.Resources;
using Microsoft.Win32;

namespace HeartRateMonitor
{
    /// <summary>
    /// MainWindow 仅负责 ViewModel 初始化和 View 层事件桥接。
    /// 所有业务逻辑均在 MainViewModel 中。
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly System.Windows.Forms.NotifyIcon _trayIcon;
        private Popup? _trayMenu;

        public MainWindow()
        {
            InitializeComponent();

            _viewModel = new MainViewModel();
            _viewModel.GetOwnerWindow = () => this;
            DataContext = _viewModel;

            // 初始化系统托盘图标
            _trayIcon = new System.Windows.Forms.NotifyIcon();
            _trayIcon.Icon = LoadAppIcon();
            _trayIcon.Text = "HeartRateMonitor";
            _trayIcon.Visible = false;

            // 双击托盘图标 → 显示窗口
            _trayIcon.DoubleClick += (_, _) => ShowFromTray();

            // 右键菜单
            _trayIcon.MouseClick += TrayIcon_MouseClick;
        }

        // ── 系统托盘 ─────────────────────────────────────

        /// <summary>
        /// 检测系统是否为深色模式。
        /// </summary>
        private static bool IsDarkMode()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var value = key?.GetValue("AppsUseLightTheme");
                return value is int v && v == 0;
            }
            catch
            {
                return false;
            }
        }

        private void TrayIcon_MouseClick(object? sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button != System.Windows.Forms.MouseButtons.Right) return;

            CloseTrayMenu();

            bool dark = IsDarkMode();

            // 根据系统主题设置颜色
            var bgColor = dark
                ? System.Windows.Media.Color.FromRgb(43, 43, 43)
                : System.Windows.Media.Color.FromRgb(245, 245, 245);
            var textColor = dark
                ? System.Windows.Media.Color.FromRgb(230, 230, 230)
                : System.Windows.Media.Color.FromRgb(51, 51, 51);
            var hoverColor = dark
                ? System.Windows.Media.Color.FromRgb(62, 62, 62)
                : System.Windows.Media.Color.FromRgb(230, 230, 230);
            var pressedColor = dark
                ? System.Windows.Media.Color.FromRgb(80, 80, 80)
                : System.Windows.Media.Color.FromRgb(210, 210, 210);
            var borderColor = dark
                ? System.Windows.Media.Color.FromRgb(60, 60, 60)
                : System.Windows.Media.Color.FromRgb(200, 200, 200);

            // 创建自定义圆角 Popup 菜单
            _trayMenu = new Popup
            {
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade,
                Placement = PlacementMode.Mouse,
                StaysOpen = false,
                IsOpen = true
            };

            var border = new Border
            {
                Background = new SolidColorBrush(bgColor),
                BorderBrush = new SolidColorBrush(borderColor),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(4),
                MinWidth = 120,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 15,
                    Opacity = 0.3,
                    ShadowDepth = 2
                }
            };

            var stack = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };

            var showBtn = CreateMenuItem(Strings.TrayShow, textColor, hoverColor, pressedColor);
            showBtn.Click += (_, _) => { CloseTrayMenu(); ShowFromTray(); };

            var exitBtn = CreateMenuItem(Strings.TrayExit, textColor, hoverColor, pressedColor);
            exitBtn.Click += (_, _) => { CloseTrayMenu(); ForceClose(); };

            stack.Children.Add(showBtn);
            stack.Children.Add(exitBtn);
            border.Child = stack;
            _trayMenu.Child = border;

            _trayMenu.LostFocus += (_, _) => CloseTrayMenu();
        }

        private static Button CreateMenuItem(string text, System.Windows.Media.Color textColor,
            System.Windows.Media.Color hoverColor, System.Windows.Media.Color pressedColor)
        {
            return new Button
            {
                Content = text,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(textColor),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(16, 8, 16, 8),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Cursor = Cursors.Hand,
                FontSize = 13,
                Template = CreateMenuItemTemplate(hoverColor, pressedColor)
            };
        }

        private static ControlTemplate CreateMenuItemTemplate(System.Windows.Media.Color hoverColor,
            System.Windows.Media.Color pressedColor)
        {
            var template = new ControlTemplate(typeof(Button));

            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "border";
            borderFactory.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });

            var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentFactory.SetBinding(ContentPresenter.HorizontalAlignmentProperty,
                new Binding("HorizontalContentAlignment") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            contentFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            contentFactory.SetValue(ContentPresenter.MarginProperty, new Thickness(0));
            borderFactory.AppendChild(contentFactory);

            template.VisualTree = borderFactory;

            // Hover 效果
            var trigger = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            trigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(hoverColor), "border"));
            template.Triggers.Add(trigger);

            // Pressed 效果
            var pressTrigger = new Trigger { Property = Button.IsPressedProperty, Value = true };
            pressTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(pressedColor), "border"));
            template.Triggers.Add(pressTrigger);

            return template;
        }

        private void CloseTrayMenu()
        {
            if (_trayMenu != null)
            {
                _trayMenu.IsOpen = false;
                _trayMenu = null;
            }
        }

        private void ShowFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            _trayIcon.Visible = false;
        }

        private void ForceClose()
        {
            _trayIcon.Visible = false;
            _viewModel.Dispose();
            _trayIcon.Dispose();
            Application.Current.Shutdown();
        }

        /// <summary>
        /// 加载应用图标用于托盘（从嵌入资源）
        /// </summary>
        private static System.Drawing.Icon LoadAppIcon()
        {
            try
            {
                var uri = new Uri("pack://application:,,,/icon.ico", UriKind.Absolute);
                var stream = Application.GetResourceStream(uri)?.Stream;
                if (stream != null)
                    return new System.Drawing.Icon(stream);
            }
            catch { }

            return System.Drawing.SystemIcons.Application;
        }

        // ── 事件处理 ─────────────────────────────────────

        private void ConnectDevice_Click(object sender, RoutedEventArgs e)
        {
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

        private void DisconnectDevice_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element &&
                element.DataContext is ConnectedDevice connected)
            {
                _viewModel.DisconnectDevice(connected);
            }
        }

        private void ToggleRecording_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.ToggleRecordingFromView();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (_viewModel.HasConnectedDevices)
            {
                e.Cancel = true;
                Hide();
                _trayIcon.Visible = true;
                _trayIcon.ShowBalloonTip(2000, "HeartRateMonitor", Strings.TrayMinimized, System.Windows.Forms.ToolTipIcon.Info);
                return;
            }

            _viewModel.OnClosing();
            _trayIcon.Visible = false;
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            _trayIcon.Dispose();
            _viewModel.Dispose();
            base.OnClosed(e);
        }

        // ── 设备名称滚动动画 ──────────────────────────────

        private const double DeviceNameMaxWidth = 100;

        private void DeviceName_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is not Grid grid) return;

            var textBlock = FindChild<TextBlock>(grid);
            if (textBlock == null) return;

            textBlock.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (textBlock.ActualWidth <= DeviceNameMaxWidth) return;

                double scrollDistance = -(textBlock.ActualWidth - DeviceNameMaxWidth + 10);
                var transform = textBlock.RenderTransform as TranslateTransform;
                if (transform == null) return;

                var animation = new DoubleAnimationUsingKeyFrames();
                animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
                animation.KeyFrames.Add(new SplineDoubleKeyFrame(scrollDistance, KeyTime.FromPercent(0.4),
                    new KeySpline(0.42, 0, 0.58, 1)));
                animation.KeyFrames.Add(new LinearDoubleKeyFrame(scrollDistance, KeyTime.FromPercent(0.54)));
                animation.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromPercent(0.61),
                    new KeySpline(0.42, 0, 0.58, 1)));
                animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
                animation.Duration = TimeSpan.FromSeconds(7.5);
                animation.RepeatBehavior = RepeatBehavior.Forever;

                transform.BeginAnimation(TranslateTransform.XProperty, animation);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void DeviceName_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is not Grid grid) return;

            var textBlock = FindChild<TextBlock>(grid);
            if (textBlock == null) return;

            var transform = textBlock.RenderTransform as TranslateTransform;
            if (transform != null)
            {
                transform.BeginAnimation(TranslateTransform.XProperty, null);
                transform.X = 0;
            }
        }

        private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed) return typed;
                var result = FindChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }
    }
}
