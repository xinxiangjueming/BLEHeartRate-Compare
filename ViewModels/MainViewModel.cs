using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using HeartRateMonitor.Models;
using HeartRateMonitor.Services;
using HeartRateMonitor.Resources;

namespace HeartRateMonitor.ViewModels
{
    /// <summary>
    /// 主窗口 ViewModel。协调蓝牙服务、HRV 分析、图表和数据导出。
    /// </summary>
    public sealed class MainViewModel : ObservableObject, IDisposable
    {
        // ── 常量 ─────────────────────────────────────────

        private static readonly OxyColor[] PredefinedColors =
        {
            OxyColors.Red, OxyColors.Orange, OxyColors.Green,
            OxyColors.DodgerBlue, OxyColors.Purple, OxyColors.Black
        };

        private const int MaxDataPoints = 7200;    // 最大数据点（约2小时）
        private const int TimerIntervalSeconds = 1; // 数据采样间隔

        // ── 服务 ─────────────────────────────────────────

        private readonly BluetoothService _bluetoothService = new();
        private readonly DispatcherTimer _timer;
        private DateTime? _sessionStartTime;
        private readonly Queue<DateTime> _timeLabels = new();

        // ── 扫描结果 ─────────────────────────────────────

        private readonly ObservableCollection<string> _discoveredDevices = new();
        /// <summary>扫描到的设备列表（显示格式: "设备名 (信号 dBm)"）</summary>
        public ObservableCollection<string> DiscoveredDevices => _discoveredDevices;

        /// <summary>扫描结果的地址映射（内部使用）</summary>
        private readonly Dictionary<string, ulong> _discoveredAddresses = new();

        private int _selectedDeviceIndex = -1;
        /// <summary>用户选中的扫描设备索引</summary>
        public int SelectedDeviceIndex
        {
            get => _selectedDeviceIndex;
            set => SetProperty(ref _selectedDeviceIndex, value);
        }

        // ── 已连接设备 ───────────────────────────────────

        private readonly ObservableCollection<ConnectedDevice> _connectedDevices = new();
        /// <summary>已连接设备列表，绑定到 UI 表格和图例</summary>
        public ObservableCollection<ConnectedDevice> ConnectedDevices => _connectedDevices;

        /// <summary>是否有已连接的设备</summary>
        public bool HasConnectedDevices => _connectedDevices.Count > 0;

        // ── 图表 ─────────────────────────────────────────

        private PlotModel _plotModel = null!;
        public PlotModel PlotModel
        {
            get => _plotModel;
            set => SetProperty(ref _plotModel, value);
        }

        // ── Y 轴控制 ─────────────────────────────────────

        private string _yMinText = "40";
        public string YMinText
        {
            get => _yMinText;
            set => SetProperty(ref _yMinText, value);
        }

        private string _yMaxText = "100";
        public string YMaxText
        {
            get => _yMaxText;
            set => SetProperty(ref _yMaxText, value);
        }

        // ── 开关状态 ─────────────────────────────────────

        private bool _isScanning;
        public bool IsScanning
        {
            get => _isScanning;
            set => SetProperty(ref _isScanning, value);
        }

        private DispatcherTimer? _scanTimer;
        private DispatcherTimer? _cleanupTimer;
        private readonly Dictionary<string, DateTime> _deviceDiscoveryTimes = new();

        private bool _isRecording;
        public bool IsRecording
        {
            get => _isRecording;
            set => SetProperty(ref _isRecording, value);
        }

        private bool _autoReconnect;
        public bool AutoReconnect
        {
            get => _autoReconnect;
            set
            {
                if (SetProperty(ref _autoReconnect, value))
                    _bluetoothService.AutoReconnect = value;
            }
        }

        private bool _showHrvColumns;
        /// <summary>是否有设备能提供 RR/HRV 数据，控制 SDNN/RMSSD 列的显示</summary>
        public bool ShowHrvColumns
        {
            get => _showHrvColumns;
            set => SetProperty(ref _showHrvColumns, value);
        }

        private bool _showAllData;
        public bool ShowAllData
        {
            get => _showAllData;
            set
            {
                if (SetProperty(ref _showAllData, value))
                    UpdateXAxisRange();
            }
        }

        // ── 状态信息 ─────────────────────────────────────

        private string _statusMessage = Strings.StatusReady;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        // ── 命令 ─────────────────────────────────────────

        public ICommand ScanCommand { get; }
        public ICommand ConnectCommand { get; }
        public ICommand ApplyYRangeCommand { get; }
        public ICommand ClearChartCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand ToggleDeviceVisibilityCommand { get; }

        // ── 获取 ViewModel 所在的 Window（用于设置 Dialog Owner）──
        /// <summary>由 View 设置，用于对话框归属</summary>
        public Func<Window>? GetOwnerWindow { get; set; }

        // ══════════════════════════════════════════════════
        // 构造函数
        // ══════════════════════════════════════════════════

        public MainViewModel()
        {
            InitializePlotModel();

            // 监听系统主题变化，更新图表轴颜色
            Services.ThemeService.ThemeChanged += OnThemeChanged;

            // 注册蓝牙服务事件
            _bluetoothService.DeviceDiscovered += OnDeviceDiscovered;
            _bluetoothService.DeviceConnected += OnDeviceConnected;
            _bluetoothService.DeviceDisconnected += OnDeviceDisconnected;
            _bluetoothService.DeviceReconnected += OnDeviceReconnected;
            _bluetoothService.DeviceReconnectFailed += OnDeviceReconnectFailed;
            _bluetoothService.HeartRateReceived += OnHeartRateReceived;
            _bluetoothService.BatteryUpdated += OnBatteryUpdated;
            _bluetoothService.ErrorOccurred += OnErrorOccurred;

            // 初始化定时器
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(TimerIntervalSeconds) };
            _timer.Tick += OnTimerTick;

            // 初始化命令
            ScanCommand = new RelayCommand(StartScan);
            ConnectCommand = new RelayCommand(ConnectSelectedDevice, () => SelectedDeviceIndex >= 0);
            ApplyYRangeCommand = new RelayCommand(ApplyYRange);
            ClearChartCommand = new RelayCommand(ClearChart);
            ExportCommand = new RelayCommand(ExportData, () => _connectedDevices.Count > 0);
            ToggleDeviceVisibilityCommand = new RelayCommand<ConnectedDevice>(ToggleDeviceVisibility);
        }

        // ══════════════════════════════════════════════════
        // 图表初始化
        // ══════════════════════════════════════════════════

        private void InitializePlotModel()
        {
            var isDark = App.ThemeService.IsDarkMode;
            var textColor = isDark ? OxyColor.FromRgb(224, 224, 224) : OxyColor.FromRgb(51, 51, 51);
            var titleColor = isDark ? OxyColor.FromRgb(255, 255, 255) : OxyColor.FromRgb(34, 34, 34);

            var model = new PlotModel { Title = Strings.ChartTitle };
            model.Background = OxyColors.Transparent;
            model.TitleColor = titleColor;
            model.PlotAreaBorderColor = isDark ? OxyColor.FromRgb(85, 85, 85) : OxyColor.FromRgb(221, 221, 221);

            model.Axes.Add(new LinearAxis
            {
                Key = "XAxis",
                Position = AxisPosition.Bottom,
                Title = Strings.TimeAxis,
                Minimum = 0,
                Maximum = 10,
                AbsoluteMinimum = 0,
                IsZoomEnabled = false,
                IsPanEnabled = false,
                MajorGridlineStyle = LineStyle.None,
                MinorGridlineStyle = LineStyle.None,
                TextColor = textColor,
                TitleColor = titleColor,
                AxislineColor = textColor
            });

            model.Axes.Add(new LinearAxis
            {
                Key = "YAxis",
                Position = AxisPosition.Left,
                Title = "",
                Minimum = 40,
                Maximum = 100,
                AbsoluteMinimum = 0,
                AbsoluteMaximum = 250,
                IsZoomEnabled = false,
                IsPanEnabled = false,
                MajorGridlineStyle = LineStyle.None,
                MinorGridlineStyle = LineStyle.None,
                TextColor = textColor,
                TitleColor = titleColor,
                AxislineColor = textColor
            });

            PlotModel = model;
        }

        private void OnThemeChanged(bool isDark)
        {
            var textColor = isDark ? OxyColor.FromRgb(224, 224, 224) : OxyColor.FromRgb(51, 51, 51);
            var titleColor = isDark ? OxyColor.FromRgb(255, 255, 255) : OxyColor.FromRgb(34, 34, 34);

            PlotModel.TitleColor = titleColor;
            PlotModel.PlotAreaBorderColor = isDark ? OxyColor.FromRgb(85, 85, 85) : OxyColor.FromRgb(221, 221, 221);

            foreach (var axis in PlotModel.Axes)
            {
                axis.TextColor = textColor;
                axis.TitleColor = titleColor;
                axis.AxislineColor = textColor;
            }

            Application.Current.Dispatcher.BeginInvoke(new Action(() => SafeInvalidatePlot(false)),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>清洗设备名称</summary>
        private static string CleanDeviceName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return Strings.UnknownDevice;
            return name.Trim();
        }

        /// <summary>安全刷新图表，防止 OxyPlot 内部空引用崩溃</summary>
        private void SafeInvalidatePlot(bool updateData)
        {
            try { PlotModel.InvalidatePlot(updateData); }
            catch (Exception ex) { Logger.Error("InvalidatePlot 异常", ex); System.Diagnostics.Debug.WriteLine($"[Plot] InvalidatePlot 异常: {ex.Message}"); }
        }

        // ══════════════════════════════════════════════════
        // 扫描
        // ══════════════════════════════════════════════════

        private async void StartScan()
        {
            // 正在扫描时点击则停止
            if (IsScanning)
            {
                _scanTimer?.Stop();
                _bluetoothService.StopScan();
                IsScanning = false;
                StatusMessage = Strings.StatusScanningStopped;
                return;
            }

            // 检测蓝牙状态，未开启则引导用户
            StatusMessage = Strings.StatusDetectingBluetooth;
            bool bluetoothReady = await BluetoothService.EnsureBluetoothEnabledAsync();
            if (!bluetoothReady)
            {
                ShowError(Strings.ErrorBluetoothNotReady);
                StatusMessage = Strings.StatusReady;
                return;
            }

            _discoveredDevices.Clear();
            _discoveredAddresses.Clear();
            _deviceDiscoveryTimes.Clear();

            StatusMessage = Strings.StatusScanningDevices;
            IsScanning = true;

            _bluetoothService.StartScan();

            // 15秒后自动停止扫描
            _scanTimer?.Stop();
            _scanTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _scanTimer.Tick += (_, _) =>
            {
                _scanTimer.Stop();
                _bluetoothService.StopScan();
                IsScanning = false;
                StatusMessage = Strings.StatusScanningComplete;
            };
            _scanTimer.Start();

            // 30秒未连接设备清理定时器（每5秒检查一次）
            _cleanupTimer?.Stop();
            _cleanupTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _cleanupTimer.Tick += (_, _) => CleanupStaleDevices();
            _cleanupTimer.Start();
        }

        /// <summary>
        /// 清理超过30秒未连接的扫描设备
        /// </summary>
        private void CleanupStaleDevices()
        {
            var now = DateTime.Now;
            var stale = _deviceDiscoveryTimes
                .Where(kv => (now - kv.Value).TotalSeconds > 30 &&
                             !_connectedDevices.Any(c => c.Model.CleanName == kv.Key))
                .Select(kv => kv.Key)
                .ToList();

            foreach (var name in stale)
            {
                _discoveredDevices.Remove(name);
                _discoveredAddresses.Remove(name);
                _deviceDiscoveryTimes.Remove(name);
            }

            // 没有设备了就停止清理定时器
            if (_deviceDiscoveryTimes.Count == 0)
                _cleanupTimer?.Stop();
        }

        private void OnDeviceDiscovered(BleScanResult result)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                string display = CleanDeviceName(result.LocalName);

                // 跳过已连接设备
                if (_connectedDevices.Any(c => c.Model.CleanName == display))
                    return;

                if (_discoveredAddresses.ContainsKey(display))
                    return;

                // 按蓝牙地址去重，信号强度变化时更新显示
                var existing = _discoveredAddresses.FirstOrDefault(kv => kv.Value == result.BluetoothAddress);
                if (existing.Key != null)
                {
                    int idx = _discoveredDevices.IndexOf(existing.Key);
                    if (idx >= 0)
                    {
                        _discoveredDevices[idx] = display;
                        _discoveredAddresses.Remove(existing.Key);
                        _discoveredAddresses[display] = result.BluetoothAddress;
                    }
                    return;
                }

                _discoveredAddresses[display] = result.BluetoothAddress;
                _discoveredDevices.Add(display);
                _deviceDiscoveryTimes[display] = DateTime.Now;
                StatusMessage = string.Format(Strings.StatusDeviceDiscovered, display);
            });
        }

        // ══════════════════════════════════════════════════
        // 连接
        // ══════════════════════════════════════════════════

        private async void ConnectSelectedDevice()
        {
            if (SelectedDeviceIndex < 0 || SelectedDeviceIndex >= _discoveredDevices.Count)
                return;

            string display = _discoveredDevices[SelectedDeviceIndex];
            if (!_discoveredAddresses.TryGetValue(display, out ulong address))
                return;

            StatusMessage = Strings.StatusConnecting;

            var model = await _bluetoothService.ConnectAsync(address, display);

            if (model == null)
            {
                ShowError(Strings.ErrorConnectFailed);
                return;
            }

            // 检查是否已存在于 UI 列表中（已连接场景）
            if (_connectedDevices.Any(d => d.Model.DeviceId == model.DeviceId))
            {
                StatusMessage = string.Format(Strings.StatusDeviceAlreadyConnected, model.Alias);
                return;
            }

            int colorIndex = _connectedDevices.Count % PredefinedColors.Length;
            model.ColorIndex = colorIndex;

            // 创建图表曲线，延迟添加确保 PlotView 轴已就绪
            var lineColor = PredefinedColors[colorIndex];
            var series = new AreaSeries
            {
                Title = model.Alias,
                StrokeThickness = 2,
                Color = lineColor,
                Fill = OxyColor.FromAColor(40, lineColor),
                XAxisKey = "XAxis",
                YAxisKey = "YAxis"
            };
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                PlotModel.Series.Add(series);
                SafeInvalidatePlot(true);
            }), System.Windows.Threading.DispatcherPriority.Loaded);

            // 创建连接设备包装
            var connected = new ConnectedDevice
            {
                Model = model,
                HrSeries = series
            };

            // 注册设备事件
            model.ColorChanged += (_, _) =>
            {
                if (model.ColorIndex >= 0 && model.ColorIndex < PredefinedColors.Length)
                {
                    var c = PredefinedColors[model.ColorIndex];
                    series.Color = c;
                    series.Fill = OxyColor.FromAColor(40, c);
                    SafeInvalidatePlot(true);
                }
            };
            model.VisibilityChanged += (_, _) =>
            {
                series.IsVisible = model.IsVisible;
                SafeInvalidatePlot(true);
            };

            _connectedDevices.Add(connected);
            _deviceDiscoveryTimes.Remove(model.CleanName);

            // 从扫描列表中移除已连接设备
            _discoveredDevices.Remove(model.CleanName);
            _discoveredAddresses.Remove(model.CleanName);

            StatusMessage = string.Format(Strings.StatusDeviceConnected, model.Alias);
        }

        private void OnDeviceConnected(string deviceId)
        {
            var model = _bluetoothService.GetDeviceModel(deviceId);
            if (model != null)
                model.ConnectionEvents.Add($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {Strings.BleConnected}");
        }

        // ── 自动保存 ─────────────────────────────────────

        /// <summary>
        /// 首次连接设备时提示用户选择自动保存路径。
        /// 用户可取消跳过。
        /// </summary>

        private void OnDeviceDisconnected(string deviceId)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var connected = _connectedDevices.FirstOrDefault(d => d.Model.DeviceId == deviceId);
                if (connected == null) return;

                StatusMessage = string.Format(Strings.StatusDeviceDisconnected, connected.Model.Alias);

                ShowHrvColumns = _connectedDevices.Any(c => c.Model.IsConnected && c.Model.HeartRate.HasValue && c.Model.LastRR.HasValue);

                if (AutoReconnect)
                {
                    _ = _bluetoothService.TryReconnectAsync(deviceId);
                }
            });
        }

        private void OnDeviceReconnected(string deviceId)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var model = _bluetoothService.GetDeviceModel(deviceId);
                if (model != null)
                    StatusMessage = string.Format(Strings.StatusDeviceReconnected, model.Alias);
            });
        }

        private void OnDeviceReconnectFailed(string deviceId)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var model = _bluetoothService.GetDeviceModel(deviceId);
                if (model != null)
                    StatusMessage = string.Format(Strings.StatusDeviceReconnectFailed, model.Alias);
            });
        }

        private void OnErrorOccurred(string deviceId, string errorMessage)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                StatusMessage = errorMessage;
            });
        }

        private void OnHeartRateReceived(string deviceId, HeartRateDataPoint dataPoint)
        {
            // 数据主要通过 DeviceModel 的 PropertyChanged 通知 UI
            // 此事件可用于日志或调试
        }

        private void OnBatteryUpdated(string deviceId, int level)
        {
            // 电池电量已更新到 DeviceModel，UI 通过绑定自动刷新
        }

        // ══════════════════════════════════════════════════
        // 定时器：每秒采样 + 图表更新 + HRV 计算
        // ══════════════════════════════════════════════════

        private void OnTimerTick(object? sender, EventArgs e)
        {
            if (_sessionStartTime == null || _connectedDevices.Count == 0) return;

            try
            {
                DateTime now = DateTime.Now;
                double currentSeconds = (now - _sessionStartTime.Value).TotalSeconds;
                int currentSecond = (int)currentSeconds;

                _timeLabels.Enqueue(DateTime.Now);
                while (_timeLabels.Count > MaxDataPoints)
                    _timeLabels.Dequeue();

                foreach (var connected in _connectedDevices)
                {
                    SampleDeviceData(connected, now, currentSeconds, currentSecond);
                }

                UpdateXAxisRange();
                SafeInvalidatePlot(true);

                // 更新 HRV 列可见性
                ShowHrvColumns = _connectedDevices.Any(c => c.Model.HeartRate.HasValue && c.Model.LastRR.HasValue);
            }
            catch (Exception ex)
            {
                Logger.Error("Timer 异常", ex);
                System.Diagnostics.Debug.WriteLine($"[Timer] 异常: {ex.Message}");
            }
        }

        private void SampleDeviceData(ConnectedDevice connected, DateTime now,
            double currentSeconds, int currentSecond)
        {
            var model = connected.Model;
            bool hasNewData = model.LastDataTimestamp >= now.AddSeconds(-1);

            // ── HRV 计算 ──
            if (model.IsConnected && model.RRBuffer.Count >= 5)
            {
                var metrics = HrvAnalysisService.Calculate(model.RRBuffer);
                model.Hrv = metrics;
            }
            else if (!model.IsConnected)
            {
                model.Hrv = null;
            }

            // ── 图表数据点 ──
            if (model.IsConnected && model.HeartRate.HasValue && hasNewData)
            {
                connected.HrSeries.Points.Add(new DataPoint(currentSeconds, model.HeartRate.Value));
                if (connected.HrSeries.Points.Count > MaxDataPoints)
                    connected.HrSeries.Points.RemoveAt(0);
            }

            // ── 历史队列记录 ──
            int? hr = hasNewData ? model.HeartRate : null;
            double? rr = hasNewData ? model.LastRR : null;
            double? sdnn = hasNewData ? model.Hrv?.SDNN : null;
            double? rmssd = hasNewData ? model.Hrv?.RMSSD : null;
            double? dfa1 = hasNewData ? model.Hrv?.DFA_alpha1 : null;
            double? dfa2 = hasNewData ? model.Hrv?.DFA_alpha2 : null;

            connected.HRHistory.Enqueue(hr);
            connected.RRHistory.Enqueue(rr);
            connected.SDNNHistory.Enqueue(sdnn);
            connected.RMSSDHistory.Enqueue(rmssd);
            connected.DFA_alpha1History.Enqueue(dfa1);
            connected.DFA_alpha2History.Enqueue(dfa2);
            connected.BatteryHistory.Enqueue(model.BatteryLevel);

            // 限制队列长度
            while (connected.HRHistory.Count > MaxDataPoints) connected.HRHistory.Dequeue();
            while (connected.RRHistory.Count > MaxDataPoints) connected.RRHistory.Dequeue();
            while (connected.SDNNHistory.Count > MaxDataPoints) connected.SDNNHistory.Dequeue();
            while (connected.RMSSDHistory.Count > MaxDataPoints) connected.RMSSDHistory.Dequeue();
            while (connected.DFA_alpha1History.Count > MaxDataPoints) connected.DFA_alpha1History.Dequeue();
            while (connected.DFA_alpha2History.Count > MaxDataPoints) connected.DFA_alpha2History.Dequeue();
            while (connected.BatteryHistory.Count > MaxDataPoints) connected.BatteryHistory.Dequeue();
        }

        // ══════════════════════════════════════════════════
        // X 轴范围更新
        // ══════════════════════════════════════════════════

        private void UpdateXAxisRange()
        {
            if (_sessionStartTime == null) return;

            var xAxis = PlotModel.Axes.FirstOrDefault(a => a.Key == "XAxis") as LinearAxis;
            if (xAxis == null) return;

            double currentSeconds = (DateTime.Now - _sessionStartTime.Value).TotalSeconds;

            if (ShowAllData)
            {
                xAxis.Minimum = 0;
                xAxis.Maximum = currentSeconds;
            }
            else
            {
                xAxis.Minimum = Math.Max(0, currentSeconds - 10);
                xAxis.Maximum = currentSeconds;
            }

            SafeInvalidatePlot(false);
        }

        // ══════════════════════════════════════════════════
        // Y 轴控制
        // ══════════════════════════════════════════════════

        private void ApplyYRange()
        {
            if (!double.TryParse(YMinText, out double min) || !double.TryParse(YMaxText, out double max))
            {
                ShowError(Strings.ErrorInvalidNumber);
                return;
            }

            min = Math.Clamp(min, 0, 250);
            max = Math.Clamp(max, 0, 250);

            if (min >= max)
            {
                ShowError(Strings.ErrorMinMustBeLessThanMax);
                return;
            }

            var yAxis = PlotModel.Axes.FirstOrDefault(a => a.Key == "YAxis");
            if (yAxis != null)
            {
                yAxis.Minimum = min;
                yAxis.Maximum = max;
                SafeInvalidatePlot(true);
            }
        }

        /// <summary>
        /// 供 View 层 Click 事件调用。
        /// IsRecording 已由 ToggleButton 的 IsChecked 绑定自动更新，
        /// 此方法只需根据当前状态执行实际的启动/停止逻辑。
        /// </summary>
        public void ToggleRecordingFromView()
        {
            if (IsRecording)
            {
                // 用户点击后 IsRecording 刚变为 true → 开始记录
                if (_connectedDevices.Count == 0)
                {
                    IsRecording = false;
                    ShowError(Strings.ErrorConnectFirst);
                    return;
                }

                _sessionStartTime = DateTime.Now;
                _timer.Start();
                UpdateXAxisRange();
                StatusMessage = Strings.StatusRecordingStarted;
            }
            else
            {
                // 用户点击后 IsRecording 刚变为 false → 停止记录
                _timer.Stop();
                StatusMessage = Strings.StatusRecordingStopped;
            }
        }

        // ══════════════════════════════════════════════════
        // 清空图表
        // ══════════════════════════════════════════════════

        private void ClearChart()
        {
            if (!ShowConfirmDialog(Strings.ConfirmClearChart))
                return;

            _sessionStartTime = DateTime.Now;
            _timeLabels.Clear();

            foreach (var connected in _connectedDevices)
            {
                connected.HrSeries.Points.Clear();
                connected.HRHistory.Clear();
                connected.RRHistory.Clear();
                connected.SDNNHistory.Clear();
                connected.RMSSDHistory.Clear();
                connected.DFA_alpha1History.Clear();
                connected.DFA_alpha2History.Clear();
                connected.BatteryHistory.Clear();
                connected.Model.RRBuffer.Clear();
                connected.Model.LastRR = null;
                connected.Model.Hrv = null;
            }

            var xAxis = PlotModel.Axes.FirstOrDefault(a => a.Key == "XAxis") as LinearAxis;
            if (xAxis != null)
            {
                xAxis.Minimum = 0;
                xAxis.Maximum = 60;
            }

            SafeInvalidatePlot(true);
            StatusMessage = Strings.StatusChartCleared;
        }

        // ══════════════════════════════════════════════════
        // 设备断开（双击设备名称）
        // ══════════════════════════════════════════════════

        public async void DisconnectDevice(ConnectedDevice connected)
        {
            if (connected == null || !connected.Model.IsConnected) return;

            if (!ShowConfirmDialog(string.Format(Strings.ConfirmDisconnect, connected.Model.Alias)))
                return;

            await _bluetoothService.DisconnectAsync(connected.Model.DeviceId);

            // 移除图表曲线
            if (PlotModel.Series.Contains(connected.HrSeries))
            {
                PlotModel.Series.Remove(connected.HrSeries);
                SafeInvalidatePlot(true);
            }

            _connectedDevices.Remove(connected);
            connected.Model.Dispose();
            StatusMessage = string.Format(Strings.StatusDeviceDisconnectedAction, connected.Model.Alias);

            // 所有设备都已手动断开，停止记录
            if (_connectedDevices.Count == 0)
            {
                _timer.Stop();
                IsRecording = false;
                _sessionStartTime = null;
                StatusMessage = Strings.StatusDeviceDisconnectedAll;
            }
        }

        // ══════════════════════════════════════════════════
        // 数据导出
        // ══════════════════════════════════════════════════

        private void ExportData()
        {
            if (_connectedDevices.Count == 0)
            {
                ShowError(Strings.ErrorNoDeviceForExport);
                return;
            }

            var owner = GetOwnerWindow?.Invoke();
            var dialog = new ExportOptionsDialog(_connectedDevices.Select(c => c.Model).ToList())
            {
                Owner = owner
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                string csvContent;
                if (dialog.ExportType == "历史数据")
                    csvContent = GenerateHistoryCsv(dialog.SelectedFields);
                else
                    csvContent = GenerateSnapshotCsv(dialog.SelectedFields);

                var saveDialog = new SaveFileDialog
                {
                    FileName = ExportService.GenerateFileName(),
                    DefaultExt = ".csv",
                    Filter = Strings.ExportFilter
                };

                if (saveDialog.ShowDialog() == true)
                {
                    ExportService.SaveToFile(saveDialog.FileName, csvContent);
                    ShowMessage(string.Format(Strings.ExportSuccess, saveDialog.FileName));
                }
            }
            catch (Exception ex)
            {
                ShowError(string.Format(Strings.ErrorExportFailed, ex.Message));
            }
        }

        private string GenerateHistoryCsv(IReadOnlyList<string> selectedFields)
        {
            var timeArray = _timeLabels.ToArray();
            var devices = _connectedDevices.Select(c => new ExportService.DeviceTimeSeries
            {
                Alias = c.Model.Alias,
                HeartRateData = c.HRHistory.ToArray(),
                RRData = c.RRHistory.ToArray(),
                SDNNData = c.SDNNHistory.ToArray(),
                RMSSDData = c.RMSSDHistory.ToArray(),
                DFA_alpha1Data = c.DFA_alpha1History.ToArray(),
                DFA_alpha2Data = c.DFA_alpha2History.ToArray(),
                FirstBattery = c.BatteryHistory.FirstOrDefault(),
                LastBattery = c.BatteryHistory.LastOrDefault(),
                ConnectionEvents = new List<string>(c.Model.ConnectionEvents)
            }).ToList();

            return ExportService.GenerateHistoryCsv(timeArray, devices, selectedFields);
        }

        private string GenerateSnapshotCsv(IReadOnlyList<string> selectedFields)
        {
            var devices = _connectedDevices.Select(c => new ExportService.DeviceSnapshot
            {
                Alias = c.Model.Alias,
                HeartRate = c.Model.HeartRate,
                LastRR = c.Model.LastRR,
                Hrv = c.Model.Hrv
            }).ToList();

            return ExportService.GenerateSnapshotCsv(devices, selectedFields);
        }

        // ══════════════════════════════════════════════════
        // 设备可见性切换（图例行复选框）
        // ══════════════════════════════════════════════════

        private void ToggleDeviceVisibility(ConnectedDevice? connected)
        {
            if (connected == null) return;
            connected.Model.IsVisible = !connected.Model.IsVisible;
        }

        // ══════════════════════════════════════════════════
        // 对话框辅助
        // ══════════════════════════════════════════════════

        private void ShowMessage(string message)
        {
            var owner = GetOwnerWindow?.Invoke();
            var dialog = new MessageDialog(message) { Owner = owner };
            dialog.ShowDialog();
        }

        private void ShowError(string message)
        {
            var owner = GetOwnerWindow?.Invoke();
            var dialog = new MessageDialog(message) { Owner = owner };
            dialog.ShowDialog();
        }

        private bool ShowConfirmDialog(string message)
        {
            var owner = GetOwnerWindow?.Invoke();
            var dialog = new ConfirmDialog(message) { Owner = owner };
            return dialog.ShowDialog() == true;
        }

        // ══════════════════════════════════════════════════
        // 关闭窗口前的导出提示
        // ══════════════════════════════════════════════════

        /// <summary>
        /// 在窗口关闭前调用，提示用户是否导出数据。
        /// </summary>
        public bool OnClosing()
        {
            if (_connectedDevices.Count == 0)
                return true;

            if (!ShowConfirmDialog(Strings.ConfirmCloseWithExport))
                return true;

            ExportData();
            return true;
        }

        // ══════════════════════════════════════════════════
        // 清理
        // ══════════════════════════════════════════════════

        public void Dispose()
        {
            Services.ThemeService.ThemeChanged -= OnThemeChanged;
            _timer.Stop();
            _scanTimer?.Stop();
            _cleanupTimer?.Stop();
            _bluetoothService.Dispose();

            foreach (var connected in _connectedDevices)
                connected.Model.Dispose();

            _connectedDevices.Clear();
        }
    }

    // ══════════════════════════════════════════════════════
    // 包装类：已连接设备 + 图表曲线 + 历史队列
    // ══════════════════════════════════════════════════════

    /// <summary>
    /// 包装单个已连接设备的全部状态：数据模型、图表曲线、历史队列。
    /// </summary>
    public sealed class ConnectedDevice
    {
        /// <summary>设备数据模型</summary>
        public DeviceModel Model { get; init; } = null!;

        /// <summary>OxyPlot 心率曲线</summary>
        public AreaSeries HrSeries { get; init; } = null!;

        // ── 历史数据队列（每秒记录，用于导出）──

        public Queue<int?> HRHistory { get; } = new();
        public Queue<double?> RRHistory { get; } = new();
        public Queue<double?> SDNNHistory { get; } = new();
        public Queue<double?> RMSSDHistory { get; } = new();
        public Queue<double?> DFA_alpha1History { get; } = new();
        public Queue<double?> DFA_alpha2History { get; } = new();
        public Queue<int?> BatteryHistory { get; } = new();
    }
}
