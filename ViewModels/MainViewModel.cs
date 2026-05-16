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
        private readonly AutoSaveService _autoSaveService = new();
        private readonly DispatcherTimer _timer;
        private DateTime? _sessionStartTime;
        private bool _autoSavePrompted;
        private readonly Queue<int> _timeLabels = new();

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

        private string _yMaxText = "180";
        public string YMaxText
        {
            get => _yMaxText;
            set => SetProperty(ref _yMaxText, value);
        }

        // ── 开关状态 ─────────────────────────────────────

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

        private string _statusMessage = "就绪";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private string? _autoSavePath;
        /// <summary>自动保存的文件路径（显示在状态栏）</summary>
        public string? AutoSavePath
        {
            get => _autoSavePath;
            set => SetProperty(ref _autoSavePath, value);
        }

        // ── 命令 ─────────────────────────────────────────

        public ICommand ScanCommand { get; }
        public ICommand ConnectCommand { get; }
        public ICommand ApplyYRangeCommand { get; }
        public ICommand ResetYRangeCommand { get; }
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
            ResetYRangeCommand = new RelayCommand(ResetYRange);
            ClearChartCommand = new RelayCommand(ClearChart);
            ExportCommand = new RelayCommand(ExportData, () => _connectedDevices.Count > 0);
            ToggleDeviceVisibilityCommand = new RelayCommand<ConnectedDevice>(ToggleDeviceVisibility);
        }

        // ══════════════════════════════════════════════════
        // 图表初始化
        // ══════════════════════════════════════════════════

        private void InitializePlotModel()
        {
            var model = new PlotModel { Title = "实时心率曲线" };
            model.Background = OxyColors.Transparent;

            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Title = "时间 (s)",
                Minimum = 0,
                Maximum = 60,
                AbsoluteMinimum = 0,
                IsZoomEnabled = false,
                IsPanEnabled = false,
                MajorGridlineStyle = LineStyle.None,
                MinorGridlineStyle = LineStyle.None,
                TextColor = OxyColor.FromRgb(51, 51, 51),
                TitleColor = OxyColor.FromRgb(34, 34, 34)
            });

            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "心率 (bpm)",
                Minimum = 40,
                Maximum = 180,
                AbsoluteMinimum = 0,
                AbsoluteMaximum = 250,
                IsZoomEnabled = false,
                IsPanEnabled = false,
                MajorGridlineStyle = LineStyle.None,
                MinorGridlineStyle = LineStyle.None,
                TextColor = OxyColor.FromRgb(51, 51, 51),
                TitleColor = OxyColor.FromRgb(34, 34, 34)
            });

            PlotModel = model;
        }

        // ══════════════════════════════════════════════════
        // 扫描
        // ══════════════════════════════════════════════════

        private void StartScan()
        {
            _discoveredDevices.Clear();
            _discoveredAddresses.Clear();
            StatusMessage = "正在扫描心率设备...";

            _bluetoothService.StartScan();
        }

        private void OnDeviceDiscovered(BleScanResult result)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                string name = string.IsNullOrEmpty(result.LocalName) ? "未知设备" : result.LocalName;
                string display = $"{name} ({result.RawSignalStrength} dBm)";

                if (!_discoveredAddresses.ContainsKey(display))
                {
                    _discoveredAddresses[display] = result.BluetoothAddress;
                    _discoveredDevices.Add(display);
                    StatusMessage = $"发现心率设备: {name}";
                }
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

            StatusMessage = "正在连接...";

            // 启动定时器（首次连接时）
            if (!_timer.IsEnabled)
                _timer.Start();

            if (_sessionStartTime == null)
            {
                _sessionStartTime = DateTime.Now;
                UpdateXAxisRange();
            }

            var model = await _bluetoothService.ConnectAsync(address);

            if (model == null)
            {
                ShowError("连接失败：无法获取设备。");
                return;
            }

            // 检查是否已存在于 UI 列表中（重连场景）
            if (_connectedDevices.Any(d => d.Model.DeviceId == model.DeviceId))
            {
                StatusMessage = $"设备 {model.Alias} 已重新连接。";
                return;
            }

            int colorIndex = _connectedDevices.Count % PredefinedColors.Length;
            model.ColorIndex = colorIndex;

            // 创建图表曲线
            var series = new LineSeries
            {
                Title = model.Alias,
                StrokeThickness = 2,
                Color = PredefinedColors[colorIndex]
            };
            PlotModel.Series.Add(series);

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
                    series.Color = PredefinedColors[model.ColorIndex];
                    PlotModel.InvalidatePlot(true);
                }
            };
            model.VisibilityChanged += (_, _) =>
            {
                series.IsVisible = model.IsVisible;
                PlotModel.InvalidatePlot(true);
            };

            _connectedDevices.Add(connected);

            // 首次连接时提示自动保存 CSV
            if (!_autoSavePrompted)
            {
                _autoSavePrompted = true;
                PromptAutoSave();
            }

            StatusMessage = $"设备 {model.Alias} 已连接。";
        }

        private void OnDeviceConnected(string deviceId)
        {
            var model = _bluetoothService.GetDeviceModel(deviceId);
            if (model != null)
                model.ConnectionEvents.Add($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} 连接成功");
        }

        // ── 自动保存 ─────────────────────────────────────

        /// <summary>
        /// 首次连接设备时提示用户选择自动保存路径。
        /// 用户可取消跳过。
        /// </summary>
        private void PromptAutoSave()
        {
            var dialog = new SaveFileDialog
            {
                FileName = $"心率实时记录_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                DefaultExt = ".csv",
                Filter = "CSV 文件 (*.csv)|*.csv",
                Title = "选择自动保存位置（取消则不自动保存）"
            };

            if (dialog.ShowDialog() == true)
            {
                _autoSaveService.Start(dialog.FileName);
                AutoSavePath = dialog.FileName;
                StatusMessage = $"已启用自动保存：{System.IO.Path.GetFileName(dialog.FileName)}";
            }
        }

        private void OnDeviceDisconnected(string deviceId)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var connected = _connectedDevices.FirstOrDefault(d => d.Model.DeviceId == deviceId);
                if (connected == null) return;

                StatusMessage = $"设备 {connected.Model.Alias} 断开连接。";

                if (AutoReconnect)
                {
                    _bluetoothService.TryReconnectAsync(deviceId);
                }
            });
        }

        private void OnDeviceReconnected(string deviceId)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var model = _bluetoothService.GetDeviceModel(deviceId);
                if (model != null)
                    StatusMessage = $"设备 {model.Alias} 已重新连接。";
            });
        }

        private void OnDeviceReconnectFailed(string deviceId)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var model = _bluetoothService.GetDeviceModel(deviceId);
                if (model != null)
                    StatusMessage = $"设备 {model.Alias} 重连失败。";
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
            if (_sessionStartTime == null) return;

            DateTime now = DateTime.Now;
            double currentSeconds = (now - _sessionStartTime.Value).TotalSeconds;
            int currentSecond = (int)currentSeconds;

            _timeLabels.Enqueue(currentSecond);
            while (_timeLabels.Count > MaxDataPoints)
                _timeLabels.Dequeue();

            foreach (var connected in _connectedDevices)
            {
                SampleDeviceData(connected, now, currentSeconds, currentSecond);
            }

            // 自动保存：记录所有已连接设备的当前数据
            if (_autoSaveService.IsActive)
            {
                foreach (var connected in _connectedDevices)
                {
                    if (connected.Model.IsConnected)
                        _autoSaveService.RecordSnapshot(currentSecond, connected.Model);
                }
                _autoSaveService.Flush(); // 每秒刷盘，确保数据不丢失
            }

            UpdateXAxisRange();
            PlotModel.InvalidatePlot(true);
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

            var xAxis = PlotModel.Axes.FirstOrDefault(a => a.Position == AxisPosition.Bottom) as LinearAxis;
            if (xAxis == null) return;

            double currentSeconds = (DateTime.Now - _sessionStartTime.Value).TotalSeconds;

            if (ShowAllData)
            {
                xAxis.Minimum = 0;
                xAxis.Maximum = currentSeconds;
            }
            else
            {
                xAxis.Minimum = Math.Max(0, currentSeconds - 60);
                xAxis.Maximum = currentSeconds;
            }

            PlotModel.InvalidatePlot(false);
        }

        // ══════════════════════════════════════════════════
        // Y 轴控制
        // ══════════════════════════════════════════════════

        private void ApplyYRange()
        {
            if (!double.TryParse(YMinText, out double min) || !double.TryParse(YMaxText, out double max))
            {
                ShowError("请输入有效的数字。");
                return;
            }

            min = Math.Clamp(min, 0, 250);
            max = Math.Clamp(max, 0, 250);

            if (min >= max)
            {
                ShowError("最小值必须小于最大值。");
                return;
            }

            var yAxis = PlotModel.Axes.FirstOrDefault(a => a.Position == AxisPosition.Left);
            if (yAxis != null)
            {
                yAxis.Minimum = min;
                yAxis.Maximum = max;
                PlotModel.InvalidatePlot(true);
            }
        }

        private void ResetYRange()
        {
            YMinText = "40";
            YMaxText = "180";

            var yAxis = PlotModel.Axes.FirstOrDefault(a => a.Position == AxisPosition.Left);
            if (yAxis != null)
            {
                yAxis.Minimum = 40;
                yAxis.Maximum = 180;
                PlotModel.InvalidatePlot(true);
            }
        }

        // ══════════════════════════════════════════════════
        // 清空图表
        // ══════════════════════════════════════════════════

        private void ClearChart()
        {
            if (!ShowConfirmDialog("确定要清空所有心率曲线吗？此操作将删除所有历史数据并重新计时。"))
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

            var xAxis = PlotModel.Axes.FirstOrDefault(a => a.Position == AxisPosition.Bottom) as LinearAxis;
            if (xAxis != null)
            {
                xAxis.Minimum = 0;
                xAxis.Maximum = 60;
            }

            PlotModel.InvalidatePlot(true);
            StatusMessage = "图表已清空，计时重新开始。";
        }

        // ══════════════════════════════════════════════════
        // 设备断开（双击设备名称）
        // ══════════════════════════════════════════════════

        public async void DisconnectDevice(ConnectedDevice connected)
        {
            if (connected == null || !connected.Model.IsConnected) return;

            if (!ShowConfirmDialog($"确定要断开设备 \"{connected.Model.Alias}\" 的连接吗？"))
                return;

            await _bluetoothService.DisconnectAsync(connected.Model.DeviceId);

            // 移除图表曲线
            if (PlotModel.Series.Contains(connected.HrSeries))
            {
                PlotModel.Series.Remove(connected.HrSeries);
                PlotModel.InvalidatePlot(true);
            }

            _connectedDevices.Remove(connected);
            connected.Model.Dispose();
            StatusMessage = $"设备 {connected.Model.Alias} 已断开。";
        }

        // ══════════════════════════════════════════════════
        // 数据导出
        // ══════════════════════════════════════════════════

        private void ExportData()
        {
            if (_connectedDevices.Count == 0)
            {
                ShowError("没有已连接的设备，无法导出数据。");
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
                    Filter = "CSV 文件 (*.csv)|*.csv"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    ExportService.SaveToFile(saveDialog.FileName, csvContent);
                    ShowMessage($"数据已导出到：{saveDialog.FileName}");
                }
            }
            catch (Exception ex)
            {
                ShowError($"导出数据时出错：{ex.Message}");
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
                BatteryData = c.BatteryHistory.ToArray(),
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
                Hrv = c.Model.Hrv,
                BatteryLevel = c.Model.BatteryLevel
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
            // 停止自动保存并告知用户文件位置
            if (_autoSaveService.IsActive)
            {
                string savedPath = _autoSaveService.FilePath ?? "";
                _autoSaveService.Stop();
                AutoSavePath = null;

                if (!string.IsNullOrEmpty(savedPath))
                    ShowMessage($"自动保存数据已写入：\n{savedPath}");
            }

            if (_connectedDevices.Count == 0)
                return true;

            if (!ShowConfirmDialog("是否在关闭前导出数据？"))
                return true;

            ExportData();
            return true;
        }

        // ══════════════════════════════════════════════════
        // 清理
        // ══════════════════════════════════════════════════

        public void Dispose()
        {
            _timer.Stop();
            _autoSaveService.Dispose();
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
        public LineSeries HrSeries { get; init; } = null!;

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
