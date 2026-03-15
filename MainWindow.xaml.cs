using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace HeartRateMonitor
{
    public class ColorOption
    {
        public string Name { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
    }

    public class DeviceData : INotifyPropertyChanged
    {
        private int? _hr;
        private int? _batteryLevel;
        private double? _lastSDNN;
        private double? _lastHRV;
        private int? _lastRR;
        private int _selectedColorIndex;
        private bool _isVisible = true;  // 新增：曲线可见性

        public string Id { get; set; } = string.Empty;
        public string CleanName { get; set; } = string.Empty;
        public string Alias { get; set; } = string.Empty;
        public bool Connected { get; set; }
        public BluetoothLEDevice? Device { get; set; }
        public GattCharacteristic? HeartRateChar { get; set; }
        public GattCharacteristic? BatteryChar { get; set; }

        public int? HR
        {
            get => _hr;
            set { _hr = value; OnPropertyChanged(); }
        }

        public int? BatteryLevel
        {
            get => _batteryLevel;
            set { _batteryLevel = value; OnPropertyChanged(); }
        }

        public int? LastRR
        {
            get => _lastRR;
            set { _lastRR = value; OnPropertyChanged(); }
        }

        public double? LastSDNN
        {
            get => _lastSDNN;
            set { _lastSDNN = value; OnPropertyChanged(); }
        }

        public double? LastHRV
        {
            get => _lastHRV;
            set { _lastHRV = value; OnPropertyChanged(); }
        }

        public int SelectedColorIndex
        {
            get => _selectedColorIndex;
            set
            {
                if (_selectedColorIndex != value)
                {
                    _selectedColorIndex = value;
                    OnPropertyChanged();
                    ColorChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        // 新增：曲线可见性属性
        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                if (_isVisible != value)
                {
                    _isVisible = value;
                    OnPropertyChanged();
                    VisibilityChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public Queue<int> RRBuffer { get; set; } = new Queue<int>();
        public DateTime LastHRTimestamp { get; set; }
        public LineSeries? HrSeries { get; set; }

        // 历史数据队列（每秒记录）
        public Queue<int?> HRData { get; set; } = new Queue<int?>();
        public Queue<int?> RRData { get; set; } = new Queue<int?>();
        public Queue<double?> SDNNData { get; set; } = new Queue<double?>();
        public Queue<double?> HRVData { get; set; } = new Queue<double?>();
        public Queue<int?> BatteryData { get; set; } = new Queue<int?>();

        public bool IsImported { get; set; } = false;

        public event EventHandler? ColorChanged;
        public event EventHandler? VisibilityChanged;  // 新增：可见性变化事件
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? string.Empty));
    }

    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private static readonly Guid HeartRateServiceUuid = GattServiceUuids.HeartRate;
        private static readonly Guid HeartRateMeasurementCharacteristicUuid = GattCharacteristicUuids.HeartRateMeasurement;
        private static readonly Guid BatteryServiceUuid = GattServiceUuids.Battery;
        private static readonly Guid BatteryLevelCharacteristicUuid = GattCharacteristicUuids.BatteryLevel;

        private BluetoothLEAdvertisementWatcher? _watcher;
        private readonly Dictionary<string, DeviceData> _devices = new Dictionary<string, DeviceData>();
        private readonly ObservableCollection<DeviceData> _connectedDevices = new ObservableCollection<DeviceData>();
        private readonly DispatcherTimer _timer;

        private PlotModel _plotModel = null!;
        private DateTime? _sessionStartTime = null;
        private readonly Queue<int> _timeLabels = new Queue<int>();

        private static readonly OxyColor[] PredefinedColors = new OxyColor[]
        {
            OxyColors.Red,
            OxyColors.DarkGreen,
            OxyColors.Purple,
            OxyColors.Black,
            OxyColors.DarkBlue
        };

        // 数据点上限（2小时 = 7200秒；可改为3小时 = 10800）
        private const int MaxDataPoints = 7200;
        private const int MaxRRPoints = 7200;

        public PlotModel PlotModel
        {
            get => _plotModel;
            set { _plotModel = value; OnPropertyChanged(); }
        }

        // 公开已连接设备集合，供图例绑定
        public ObservableCollection<DeviceData> ConnectedDevices => _connectedDevices;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? string.Empty));

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            InitializePlotModel();
            DevicesDataView.ItemsSource = _connectedDevices;
            // 图例绑定已在 XAML 中设置 ItemsSource="{Binding ConnectedDevices, RelativeSource={RelativeSource AncestorType=Window}}"

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;
            _timer.Start();

            BtnShowAllData.Background = new SolidColorBrush(Color.FromArgb(255, 240, 240, 240));
        }

        private void InitializePlotModel()
        {
            var model = new PlotModel { Title = "实时心率曲线" };
            model.Background = OxyColors.Transparent;

            var xAxis = new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Title = "时间 (s)",
                MajorGridlineStyle = LineStyle.None,
                MinorGridlineStyle = LineStyle.None,
                Minimum = 0,
                Maximum = 60,
                AbsoluteMinimum = 0,
                IsZoomEnabled = false,
                IsPanEnabled = false,
                TextColor = OxyColor.FromRgb((byte)51, (byte)51, (byte)51),
                TitleColor = OxyColor.FromRgb((byte)34, (byte)34, (byte)34)
            };
            model.Axes.Add(xAxis);

            var yAxis = new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "心率 (bpm)",
                Minimum = 40,
                Maximum = 180,
                AbsoluteMinimum = 0,
                AbsoluteMaximum = 250,
                MajorGridlineStyle = LineStyle.None,
                MinorGridlineStyle = LineStyle.None,
                IsZoomEnabled = false,
                IsPanEnabled = false,
                TextColor = OxyColor.FromRgb((byte)51, (byte)51, (byte)51),
                TitleColor = OxyColor.FromRgb((byte)34, (byte)34, (byte)34)
            };
            model.Axes.Add(yAxis);

            PlotModel = model;
        }

        private void UpdateXAxisRange()
        {
            if (_sessionStartTime == null) return;

            var xAxis = PlotModel.Axes.FirstOrDefault(a => a.Position == AxisPosition.Bottom) as LinearAxis;
            if (xAxis == null) return;

            double currentSeconds = (DateTime.Now - _sessionStartTime!.Value).TotalSeconds;

            if (BtnShowAllData.IsChecked == true)
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

        private void BtnShowAllData_Checked(object sender, RoutedEventArgs e)
        {
            BtnShowAllData.Background = new SolidColorBrush(Color.FromArgb(255, 200, 230, 201));
            UpdateXAxisRange();
        }

        private void BtnShowAllData_Unchecked(object sender, RoutedEventArgs e)
        {
            BtnShowAllData.Background = new SolidColorBrush(Color.FromArgb(255, 240, 240, 240));
            UpdateXAxisRange();
        }

        private void BtnScan_Click(object sender, RoutedEventArgs e)
        {
            DeviceList.Items.Clear();
            DebugMessage("开始扫描心率设备...");

            _watcher?.Stop();

            _watcher = new BluetoothLEAdvertisementWatcher();
            // 设置过滤器：只扫描包含心率服务UUID的设备
            _watcher.AdvertisementFilter.Advertisement.ServiceUuids.Add(HeartRateServiceUuid);
            _watcher.ScanningMode = BluetoothLEScanningMode.Active;

            _watcher.Received += (w, btAdv) =>
            {
                Dispatcher.Invoke(() =>
                {
                    string name = btAdv.Advertisement.LocalName;
                    ulong address = btAdv.BluetoothAddress;

                    bool exists = DeviceList.Items.Cast<BluetoothLEAdvertisementReceivedEventArgs>()
                        .Any(x => x.BluetoothAddress == address);

                    if (!exists)
                    {
                        DeviceList.Items.Add(btAdv);
                        string displayName = string.IsNullOrEmpty(name) ? "未知设备" : name;
                        DebugMessage($"发现心率设备: {displayName}");
                    }
                });
            };

            _watcher.Start();
            DebugMessage("扫描心率设备中...");
        }

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (DeviceList.SelectedItem == null)
            {
                var dialog = new MessageDialog("请先在列表中选择一个设备。")
                {
                    Owner = this
                };
                dialog.ShowDialog();
                return;
            }

            var selected = DeviceList.SelectedItem as BluetoothLEAdvertisementReceivedEventArgs;
            if (selected == null) return;

            _watcher?.Stop();
            DebugMessage($"正在连接 {selected.Advertisement.LocalName}...");

            try
            {
                var device = await BluetoothLEDevice.FromBluetoothAddressAsync(selected.BluetoothAddress);
                if (device == null)
                {
                    var dialog = new MessageDialog("连接失败：无法获取设备。")
                    {
                        Owner = this
                    };
                    dialog.ShowDialog();
                    return;
                }

                string id = device.DeviceId;
                string cleanName = CleanDeviceName(device.Name);

                DeviceData devData;
                if (_devices.ContainsKey(id))
                {
                    devData = _devices[id];
                    devData.Connected = true;
                    devData.Device = device;
                }
                else
                {
                    int colorIndex = _connectedDevices.Count % PredefinedColors.Length;
                    devData = new DeviceData
                    {
                        Id = id,
                        CleanName = cleanName,
                        Alias = cleanName,
                        Connected = true,
                        Device = device,
                        SelectedColorIndex = colorIndex,
                        HrSeries = new LineSeries
                        {
                            Title = cleanName,
                            StrokeThickness = 2,
                            Color = PredefinedColors[colorIndex]
                        }
                    };

                    // 订阅颜色变化事件
                    devData.ColorChanged += (s, args) =>
                    {
                        if (s is DeviceData d && d.HrSeries != null)
                        {
                            d.HrSeries.Color = PredefinedColors[d.SelectedColorIndex];
                            PlotModel.InvalidatePlot(true);
                        }
                    };

                    // 新增：订阅可见性变化事件
                    devData.VisibilityChanged += (s, args) =>
                    {
                        if (s is DeviceData d && d.HrSeries != null)
                        {
                            d.HrSeries.IsVisible = d.IsVisible;
                            PlotModel.InvalidatePlot(true);
                        }
                    };

                    PlotModel.Series.Add(devData.HrSeries);
                    _devices[id] = devData;
                    _connectedDevices.Add(devData);
                }

                if (_sessionStartTime == null)
                {
                    _sessionStartTime = DateTime.Now;
                    UpdateXAxisRange();
                }

                var servicesResult = await device.GetGattServicesAsync();
                if (servicesResult.Status != GattCommunicationStatus.Success)
                {
                    var dialog = new MessageDialog("获取设备服务失败。")
                    {
                        Owner = this
                    };
                    dialog.ShowDialog();
                    return;
                }

                var hrService = servicesResult.Services.FirstOrDefault(s => s.Uuid == HeartRateServiceUuid);
                if (hrService != null)
                {
                    var charsResult = await hrService.GetCharacteristicsAsync();
                    if (charsResult.Status == GattCommunicationStatus.Success)
                    {
                        devData.HeartRateChar = charsResult.Characteristics.FirstOrDefault(c => c.Uuid == HeartRateMeasurementCharacteristicUuid);
                        if (devData.HeartRateChar != null)
                        {
                            devData.HeartRateChar.ValueChanged += OnHeartRateValueChanged;
                            var status = await devData.HeartRateChar.WriteClientCharacteristicConfigurationDescriptorAsync(
                                GattClientCharacteristicConfigurationDescriptorValue.Notify);
                            if (status == GattCommunicationStatus.Success)
                                DebugMessage("心率通知订阅成功。");
                            else
                            {
                                var dialog = new MessageDialog($"心率通知订阅失败: {status}")
                                {
                                    Owner = this
                                };
                                dialog.ShowDialog();
                            }
                        }
                    }
                }

                var batteryService = servicesResult.Services.FirstOrDefault(s => s.Uuid == BatteryServiceUuid);
                if (batteryService != null)
                {
                    var charsResult = await batteryService.GetCharacteristicsAsync();
                    if (charsResult.Status == GattCommunicationStatus.Success)
                    {
                        devData.BatteryChar = charsResult.Characteristics.FirstOrDefault(c => c.Uuid == BatteryLevelCharacteristicUuid);
                        if (devData.BatteryChar != null)
                        {
                            var val = await devData.BatteryChar.ReadValueAsync();
                            if (val.Status == GattCommunicationStatus.Success)
                            {
                                var reader = DataReader.FromBuffer(val.Value);
                                devData.BatteryLevel = reader.ReadByte();
                            }

                            devData.BatteryChar.ValueChanged += OnBatteryValueChanged;
                            await devData.BatteryChar.WriteClientCharacteristicConfigurationDescriptorAsync(
                                GattClientCharacteristicConfigurationDescriptorValue.Notify);
                        }
                    }
                }

                DebugMessage($"设备 {devData.Alias} 连接成功。");
            }
            catch (Exception ex)
            {
                var dialog = new MessageDialog($"连接出错: {ex.Message}")
                {
                    Owner = this
                };
                dialog.ShowDialog();
            }
        }

        private void OnHeartRateValueChanged(object sender, GattValueChangedEventArgs args)
        {
            var characteristic = sender as GattCharacteristic;
            if (characteristic?.Service?.Device?.DeviceId is string deviceId && _devices.TryGetValue(deviceId, out var dev) && dev.Connected)
            {
                ProcessHeartRateNotification(dev, args);
            }
        }

        private void OnBatteryValueChanged(object sender, GattValueChangedEventArgs args)
        {
            var characteristic = sender as GattCharacteristic;
            if (characteristic?.Service?.Device?.DeviceId is string deviceId && _devices.TryGetValue(deviceId, out var dev) && dev.Connected)
            {
                var reader = DataReader.FromBuffer(args.CharacteristicValue);
                dev.BatteryLevel = reader.ReadByte();
            }
        }

        private void ProcessHeartRateNotification(DeviceData dev, GattValueChangedEventArgs args)
        {
            var reader = DataReader.FromBuffer(args.CharacteristicValue);
            byte flags = reader.ReadByte();

            ushort hr;
            if ((flags & 0x01) == 0)
                hr = reader.ReadByte();
            else
                hr = reader.ReadUInt16();

            dev.HR = (int)hr;
            dev.LastHRTimestamp = DateTime.Now;

            if ((flags & 0x08) != 0)
                reader.ReadUInt16();

            if ((flags & 0x10) != 0)
            {
                byte[] remaining = new byte[reader.UnconsumedBufferLength];
                reader.ReadBytes(remaining);

                int offset = 0;
                while (offset + 1 < remaining.Length)
                {
                    ushort raw = (ushort)(remaining[offset] | (remaining[offset + 1] << 8));
                    int rrMs = raw * 1000 / 1024;
                    offset += 2;

                    if (rrMs >= 200 && rrMs <= 2000)
                    {
                        dev.RRBuffer.Enqueue(rrMs);
                        while (dev.RRBuffer.Count > MaxRRPoints)
                            dev.RRBuffer.Dequeue();
                    }
                }
            }

            var lastRR = dev.RRBuffer.LastOrDefault();
            if (lastRR != 0)
                dev.LastRR = lastRR;
        }

        // RR间期异常检测
        private List<int> FilterRRIntervals(List<int> rrList)
        {
            if (rrList.Count < 3) return rrList;

            var filtered = new List<int>();
            int n = rrList.Count;
            for (int i = 0; i < n; i++)
            {
                bool keep = true;
                if (i > 0 && i < n - 1)
                {
                    int prevDiff = Math.Abs(rrList[i] - rrList[i - 1]);
                    int nextDiff = Math.Abs(rrList[i] - rrList[i + 1]);
                    if (prevDiff > 300 && nextDiff > 300)
                    {
                        keep = false;
                    }
                }
                if (keep)
                {
                    filtered.Add(rrList[i]);
                }
            }
            return filtered;
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (_sessionStartTime == null) return;

            DateTime now = DateTime.Now;
            double currentSeconds = (now - _sessionStartTime!.Value).TotalSeconds;

            int currentSecond = (int)currentSeconds;
            _timeLabels.Enqueue(currentSecond);
            if (_timeLabels.Count > MaxDataPoints)
                _timeLabels.Dequeue();

            foreach (var dev in _connectedDevices)
            {
                var rrList = dev.RRBuffer.ToList();
                var filteredRR = FilterRRIntervals(rrList);

                if (filteredRR.Count >= 2)
                {
                    int n = filteredRR.Count;
                    double sum = 0;
                    double sumSq = 0;
                    double sumSqDiff = 0;
                    for (int i = 0; i < n; i++)
                    {
                        int val = filteredRR[i];
                        sum += val;
                        sumSq += (double)val * val;
                        if (i > 0)
                        {
                            double diff = val - filteredRR[i - 1];
                            sumSqDiff += diff * diff;
                        }
                    }
                    double sdnn = Math.Sqrt((sumSq - sum * sum / n) / n);
                    double hrv = Math.Sqrt(sumSqDiff / (n - 1));
                    dev.LastSDNN = sdnn;
                    dev.LastHRV = hrv;
                }
                else
                {
                    dev.LastSDNN = null;
                    dev.LastHRV = null;
                }

                if (dev.HR.HasValue && dev.HrSeries != null)
                {
                    dev.HrSeries.Points.Add(new DataPoint(currentSeconds, dev.HR.Value));
                    if (dev.HrSeries.Points.Count > MaxDataPoints)
                        dev.HrSeries.Points.RemoveAt(0);
                }

                dev.HRData.Enqueue(dev.HR);
                if (dev.HRData.Count > MaxDataPoints)
                    dev.HRData.Dequeue();

                dev.RRData.Enqueue(dev.LastRR);
                if (dev.RRData.Count > MaxDataPoints)
                    dev.RRData.Dequeue();

                dev.SDNNData.Enqueue(dev.LastSDNN);
                if (dev.SDNNData.Count > MaxDataPoints)
                    dev.SDNNData.Dequeue();

                dev.HRVData.Enqueue(dev.LastHRV);
                if (dev.HRVData.Count > MaxDataPoints)
                    dev.HRVData.Dequeue();

                dev.BatteryData.Enqueue(dev.BatteryLevel);
                if (dev.BatteryData.Count > MaxDataPoints)
                    dev.BatteryData.Dequeue();
            }

            UpdateXAxisRange();
            PlotModel.InvalidatePlot(true);
        }

        private async void DeviceName_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                if (sender is TextBlock textBlock && textBlock.DataContext is DeviceData device)
                {
                    var dialog = new ConfirmDialog($"确定要断开设备 \"{device.Alias}\" 的连接吗？")
                    {
                        Owner = this
                    };

                    if (dialog.ShowDialog() == true)
                    {
                        await DisconnectDeviceAsync(device);
                    }
                }
                e.Handled = true;
            }
        }

        private async Task DisconnectDeviceAsync(DeviceData device)
        {
            if (device == null || !device.Connected) return;

            if (device.HeartRateChar != null)
            {
                try
                {
                    device.HeartRateChar.ValueChanged -= OnHeartRateValueChanged;
                    await device.HeartRateChar.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.None);
                }
                catch { }
            }

            if (device.BatteryChar != null)
            {
                try
                {
                    device.BatteryChar.ValueChanged -= OnBatteryValueChanged;
                    await device.BatteryChar.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.None);
                }
                catch { }
            }

            if (device.Device != null)
            {
                try
                {
                    device.Device.Dispose();
                }
                catch { }
            }

            if (device.HrSeries != null && PlotModel.Series.Contains(device.HrSeries))
            {
                PlotModel.Series.Remove(device.HrSeries);
                PlotModel.InvalidatePlot(true);
            }

            await Application.Current.Dispatcher.InvokeAsync(() => _connectedDevices.Remove(device));

            device.Connected = false;
            device.Device = null;
            device.HeartRateChar = null;
            device.BatteryChar = null;

            DebugMessage($"设备 {device.Alias} 已断开。");
        }

        private void ApplyYRange_Click(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(YMinBox.Text, out double min) && double.TryParse(YMaxBox.Text, out double max))
            {
                min = Math.Max(0, Math.Min(250, min));
                max = Math.Max(0, Math.Min(250, max));

                if (min >= max)
                {
                    var dialog = new MessageDialog("最小值必须小于最大值。")
                    {
                        Owner = this
                    };
                    dialog.ShowDialog();
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
        }

        private void ResetYRange_Click(object sender, RoutedEventArgs e)
        {
            YMinBox.Text = "40";
            YMaxBox.Text = "180";
            var yAxis = PlotModel.Axes.FirstOrDefault(a => a.Position == AxisPosition.Left);
            if (yAxis != null)
            {
                yAxis.Minimum = 40;
                yAxis.Maximum = 180;
                PlotModel.InvalidatePlot(true);
            }
        }

        private void DeviceList_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

        private string CleanDeviceName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "未知设备";
            return new string(name.Where(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-').ToArray()).Trim();
        }

        private void DebugMessage(string msg)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] {msg}");
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_connectedDevices.Count == 0)
            {
                var dialog = new MessageDialog("没有已连接的设备，无法导出数据。")
                {
                    Owner = this
                };
                dialog.ShowDialog();
                return;
            }

            var exportDialog = new ExportOptionsDialog(_connectedDevices)
            {
                Owner = this
            };
            if (exportDialog.ShowDialog() == true)
            {
                ExportData(exportDialog.ExportType, exportDialog.SelectedFields);
            }
        }

        private void ExportData(string exportType, List<string> fields)
        {
            var csv = new System.Text.StringBuilder();

            if (exportType == "历史数据")
            {
                var timeSeriesFields = fields.Where(f => f != "设备" && f != "电量").ToList();

                var headers = new List<string> { "时间" };
                foreach (var dev in _connectedDevices)
                {
                    foreach (var field in timeSeriesFields)
                    {
                        headers.Add($"{dev.Alias}_{field}");
                    }
                }
                csv.AppendLine(string.Join(",", headers));

                var timeArray = _timeLabels.ToArray();
                int dataCount = timeArray.Length;

                var devDataArrays = _connectedDevices.Select(dev => new
                {
                    HR = dev.HRData.ToArray(),
                    RR = dev.RRData.ToArray(),
                    SDNN = dev.SDNNData.ToArray(),
                    HRV = dev.HRVData.ToArray(),
                }).ToList();

                for (int i = 0; i < dataCount; i++)
                {
                    var row = new List<string> { timeArray[i] + "s" };
                    int devIndex = 0;
                    foreach (var dev in _connectedDevices)
                    {
                        var arrays = devDataArrays[devIndex];
                        foreach (var field in timeSeriesFields)
                        {
                            string value = "";
                            switch (field)
                            {
                                case "心率":
                                    value = (i < arrays.HR.Length && arrays.HR[i].HasValue) ? arrays.HR[i].Value.ToString() : "";
                                    break;
                                case "RR":
                                    value = (i < arrays.RR.Length && arrays.RR[i].HasValue) ? arrays.RR[i].Value.ToString() : "";
                                    break;
                                case "SDNN":
                                    value = (i < arrays.SDNN.Length && arrays.SDNN[i].HasValue) ? arrays.SDNN[i].Value.ToString("F1") : "";
                                    break;
                                case "HRV":
                                    value = (i < arrays.HRV.Length && arrays.HRV[i].HasValue) ? arrays.HRV[i].Value.ToString("F1") : "";
                                    break;
                            }
                            row.Add(value);
                        }
                        devIndex++;
                    }
                    csv.AppendLine(string.Join(",", row));
                }

                csv.AppendLine();
                csv.AppendLine("# Summary,,");
                csv.AppendLine($"# Total Duration: {dataCount}s,,");

                foreach (var dev in _connectedDevices)
                {
                    var hrArray = dev.HRData.Where(v => v.HasValue).Select(v => v.Value).ToArray();
                    if (hrArray.Length > 0)
                    {
                        double avg = hrArray.Average();
                        int max = hrArray.Max();
                        int min = hrArray.Min();
                        csv.AppendLine($"# Device: {dev.Alias}, Avg HR: {avg:F0} bpm, Max: {max} bpm, Min: {min} bpm");
                    }
                    else
                    {
                        csv.AppendLine($"# Device: {dev.Alias}, Avg HR: --, Max: --, Min: --");
                    }

                    var lastBattery = dev.BatteryData.LastOrDefault();
                    string batteryStr = lastBattery.HasValue ? lastBattery.Value.ToString() + "%" : "--";
                    csv.AppendLine($"# Device: {dev.Alias}, Battery: {batteryStr},");
                }
            }
            else // 当前快照
            {
                var headers = new List<string> { "设备" };
                headers.AddRange(fields.Where(f => f != "设备"));
                csv.AppendLine(string.Join(",", headers));

                foreach (var dev in _connectedDevices)
                {
                    var row = new List<string> { dev.Alias };
                    foreach (var field in fields)
                    {
                        if (field == "设备") continue;
                        string value = GetCurrentFieldValue(dev, field);
                        row.Add(value);
                    }
                    csv.AppendLine(string.Join(",", row));
                }
            }

            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = $"心率数据_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                DefaultExt = ".csv",
                Filter = "CSV 文件 (*.csv)|*.csv"
            };

            if (saveDialog.ShowDialog() == true)
            {
                System.IO.File.WriteAllText(saveDialog.FileName, csv.ToString(), System.Text.Encoding.UTF8);
                var successDialog = new MessageDialog($"数据已导出到：{saveDialog.FileName}")
                {
                    Owner = this
                };
                successDialog.ShowDialog();
            }
        }

        private string GetCurrentFieldValue(DeviceData dev, string field)
        {
            switch (field)
            {
                case "心率": return dev.HR?.ToString() ?? "";
                case "RR": return dev.LastRR?.ToString() ?? "";
                case "SDNN": return dev.LastSDNN?.ToString("F1") ?? "";
                case "HRV": return dev.LastHRV?.ToString("F1") ?? "";
                case "电量": return dev.BatteryLevel?.ToString() ?? "";
                default: return "";
            }
        }

        private void ClearChart_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ConfirmDialog("确定要清空所有心率曲线吗？此操作将删除所有历史数据并重新计时。")
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
            {
                _sessionStartTime = DateTime.Now;
                _timeLabels.Clear();

                foreach (var dev in _connectedDevices)
                {
                    dev.HrSeries?.Points.Clear();
                    dev.HRData.Clear();
                    dev.RRData.Clear();
                    dev.SDNNData.Clear();
                    dev.HRVData.Clear();
                    dev.BatteryData.Clear();
                    dev.RRBuffer.Clear();
                    dev.LastRR = null;
                    dev.LastSDNN = null;
                    dev.LastHRV = null;
                }

                var xAxis = PlotModel.Axes.FirstOrDefault(a => a.Position == AxisPosition.Bottom) as LinearAxis;
                if (xAxis != null)
                {
                    xAxis.Minimum = 0;
                    xAxis.Maximum = 60;
                }

                PlotModel.InvalidatePlot(true);
                DebugMessage("图表已清空，计时重新开始。");
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (_connectedDevices.Count > 0)
            {
                var dialog = new ConfirmDialog("是否在关闭前导出数据？")
                {
                    Owner = this,
                    Title = "关闭确认"
                };

                if (dialog.ShowDialog() == true)
                {
                    var exportDialog = new ExportOptionsDialog(_connectedDevices)
                    {
                        Owner = this
                    };
                    if (exportDialog.ShowDialog() == true)
                    {
                        try
                        {
                            ExportData(exportDialog.ExportType, exportDialog.SelectedFields);
                        }
                        catch (Exception ex)
                        {
                            var errorDialog = new MessageDialog($"导出数据时出错：{ex.Message}")
                            {
                                Owner = this
                            };
                            errorDialog.ShowDialog();
                        }
                    }
                }
            }

            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            _timer?.Stop();
            _watcher?.Stop();
            foreach (var dev in _devices.Values.ToList())
            {
                if (dev.Connected)
                {
                    try
                    {
                        if (dev.HeartRateChar != null)
                        {
                            dev.HeartRateChar.ValueChanged -= OnHeartRateValueChanged;
                        }
                        if (dev.BatteryChar != null)
                        {
                            dev.BatteryChar.ValueChanged -= OnBatteryValueChanged;
                        }
                        dev.Device?.Dispose();
                    }
                    catch { }
                }
            }
            base.OnClosed(e);
        }
    }
}