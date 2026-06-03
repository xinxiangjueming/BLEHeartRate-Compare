using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;
using HeartRateMonitor.Models;

namespace HeartRateMonitor.Services
{
    /// <summary>
    /// BLE 蓝牙心率设备服务。
    /// 封装设备扫描、连接、数据解析、自动重连等全部蓝牙交互逻辑。
    ///
    /// 所有回调均通过事件通知调用方，调用方需自行处理线程同步（通常需要 Dispatch 到 UI 线程）。
    /// </summary>
    public sealed class BluetoothService : IDisposable
    {
        // ── BLE UUID 常量 ────────────────────────────────

        private static readonly Guid HeartRateServiceUuid = GattServiceUuids.HeartRate;
        private static readonly Guid HeartRateMeasurementUuid = GattCharacteristicUuids.HeartRateMeasurement;
        private static readonly Guid BatteryServiceUuid = GattServiceUuids.Battery;
        private static readonly Guid BatteryLevelUuid = GattCharacteristicUuids.BatteryLevel;

        // ── 内部状态 ─────────────────────────────────────

        private BluetoothLEAdvertisementWatcher? _watcher;
        private readonly Dictionary<string, BleDeviceHandle> _handles = new();

        // ── 事件 ─────────────────────────────────────────

        /// <summary>扫描到新设备</summary>
        public event Action<BleScanResult>? DeviceDiscovered;

        /// <summary>收到心率数据（原始 BLE 数据点）</summary>
        public event Action<string, HeartRateDataPoint>? HeartRateReceived;

        /// <summary>电池电量更新</summary>
        public event Action<string, int>? BatteryUpdated;

        /// <summary>设备连接成功</summary>
        public event Action<string>? DeviceConnected;

        /// <summary>设备断开连接</summary>
        public event Action<string>? DeviceDisconnected;

        /// <summary>设备重连成功</summary>
        public event Action<string>? DeviceReconnected;

        /// <summary>重连失败</summary>
        public event Action<string>? DeviceReconnectFailed;

        /// <summary>蓝牙操作出错</summary>
        public event Action<string, string>? ErrorOccurred;  // (deviceId, errorMessage)

        // ── 扫描控制 ─────────────────────────────────────

        /// <summary>
        /// 开始扫描附近的 BLE 心率设备。
        /// 每次调用会停止上一次扫描。
        /// </summary>
        public void StartScan()
        {
            StopScan();

            _watcher = new BluetoothLEAdvertisementWatcher();
            _watcher.AdvertisementFilter.Advertisement.ServiceUuids.Add(HeartRateServiceUuid);
            _watcher.ScanningMode = BluetoothLEScanningMode.Active;

            _watcher.Received += OnAdvertisementReceived;
            _watcher.Start();
        }

        /// <summary>停止扫描</summary>
        public void StopScan()
        {
            if (_watcher != null)
            {
                _watcher.Received -= OnAdvertisementReceived;
                _watcher.Stop();
                _watcher = null;
            }
        }

        private void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender,
            BluetoothLEAdvertisementReceivedEventArgs args)
        {
            Debug.WriteLine($"[BLE] 原始广播名: [{args.Advertisement.LocalName}] (长度:{args.Advertisement.LocalName?.Length ?? 0})");
            DeviceDiscovered?.Invoke(new BleScanResult
            {
                BluetoothAddress = args.BluetoothAddress,
                LocalName = args.Advertisement.LocalName,
                RawSignalStrength = args.RawSignalStrengthInDBm,
                Timestamp = args.Timestamp
            });
        }

        // ── 连接管理 ─────────────────────────────────────

        /// <summary>
        /// 连接到指定 BLE 设备。
        /// 连接成功后会自动订阅心率通知和电池通知。
        /// </summary>
        /// <param name="bluetoothAddress">BLE 蓝牙地址</param>
        /// <returns>连接成功返回 DeviceModel，失败返回 null</returns>
        public async Task<DeviceModel?> ConnectAsync(ulong bluetoothAddress, string? displayName = null)
        {
            // 停止扫描以释放蓝牙资源
            StopScan();

            try
            {
                var bleDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress);
                if (bleDevice == null)
                {
                    ErrorOccurred?.Invoke("", "无法获取 BLE 设备");
                    return null;
                }

                string deviceId = bleDevice.DeviceId;
                // 优先使用扫描时获取的广播名称，GATT 名称作为后备
                string rawName = !string.IsNullOrWhiteSpace(displayName) ? displayName : bleDevice.Name;
                string cleanName = CleanDeviceName(rawName);

                // 已连接的设备不做任何操作，直接返回
                if (_handles.TryGetValue(deviceId, out var existHandle) &&
                    existHandle.Model.Status == DeviceStatus.Connected)
                {
                    bleDevice.Dispose();
                    return existHandle.Model;
                }

                // 创建或更新设备模型
                DeviceModel model;
                if (_handles.TryGetValue(deviceId, out var existingHandle))
                {
                    // 重新连接已有设备
                    existingHandle.Dispose();
                    model = existingHandle.Model;
                    model.Status = DeviceStatus.Connecting;
                }
                else
                {
                    model = new DeviceModel
                    {
                        DeviceId = deviceId,
                        CleanName = cleanName,
                        Alias = cleanName,
                        Status = DeviceStatus.Connecting
                    };
                }

                var handle = new BleDeviceHandle { Model = model };

                // 获取 GATT 服务
                var servicesResult = await bleDevice.GetGattServicesAsync();
                if (servicesResult.Status != GattCommunicationStatus.Success)
                {
                    ErrorOccurred?.Invoke(deviceId, "获取 GATT 服务失败");
                    model.Status = DeviceStatus.Failed;
                    return null;
                }

                handle.BleDevice = bleDevice;

                // 订阅心率服务
                if (!await SubscribeHeartRate(handle, servicesResult.Services))
                {
                    ErrorOccurred?.Invoke(deviceId, "心率通知订阅失败");
                }

                // 订阅电池服务
                await SubscribeBattery(handle, servicesResult.Services);

                // 监听连接状态变化
                bleDevice.ConnectionStatusChanged += (s, args) =>
                    OnConnectionStatusChanged(deviceId, s.ConnectionStatus);

                handle.Model.Status = DeviceStatus.Connected;
                _handles[deviceId] = handle;

                DeviceConnected?.Invoke(deviceId);
                return model;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke("", $"连接异常: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 断开指定设备连接。
        /// </summary>
        public async Task DisconnectAsync(string deviceId)
        {
            if (!_handles.TryGetValue(deviceId, out var handle))
                return;

            handle.Model.Status = DeviceStatus.Disconnected;

            await UnsubscribeNotifications(handle);
            handle.BleDevice?.Dispose();
            handle.BleDevice = null;

            DeviceDisconnected?.Invoke(deviceId);
        }

        /// <summary>
        /// 自动重连（最多尝试 maxAttempts 次，每次间隔 delaySeconds 秒）。
        /// </summary>
        public async Task TryReconnectAsync(string deviceId, int maxAttempts = 5, int delaySeconds = 3)
        {
            if (!_handles.TryGetValue(deviceId, out var handle))
                return;

            var model = handle.Model;
            model.Status = DeviceStatus.Reconnecting;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                Debug.WriteLine($"[BLE] 尝试重连 {model.Alias} 第 {attempt}/{maxAttempts} 次...");

                try
                {
                    var newDevice = await BluetoothLEDevice.FromIdAsync(model.DeviceId);
                    if (newDevice == null)
                    {
                        Debug.WriteLine($"[BLE] 重连 {model.Alias}: 无法获取设备");
                        await Task.Delay(delaySeconds * 1000);
                        continue;
                    }

                    var servicesResult = await newDevice.GetGattServicesAsync(BluetoothCacheMode.Uncached);
                    if (servicesResult.Status != GattCommunicationStatus.Success)
                    {
                        Debug.WriteLine($"[BLE] 重连 {model.Alias}: 获取服务失败");
                        newDevice.Dispose();
                        await Task.Delay(delaySeconds * 1000);
                        continue;
                    }

                    // 清理旧句柄
                    await UnsubscribeNotifications(handle);
                    handle.BleDevice?.Dispose();

                    handle.BleDevice = newDevice;

                    // 重新订阅服务
                    await SubscribeHeartRate(handle, servicesResult.Services);
                    await SubscribeBattery(handle, servicesResult.Services);

                    // 重新监听连接状态
                    newDevice.ConnectionStatusChanged += (s, args) =>
                        OnConnectionStatusChanged(deviceId, s.ConnectionStatus);

                    model.Status = DeviceStatus.Connected;
                    model.ConnectionEvents.Add($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} 重连成功");
                    DeviceReconnected?.Invoke(deviceId);
                    return;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[BLE] 重连 {model.Alias} 异常: {ex.Message}");
                }

                await Task.Delay(delaySeconds * 1000);
            }

            // 重连失败
            model.Status = DeviceStatus.Failed;
            model.ConnectionEvents.Add($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} 重连失败，已放弃");
            DeviceReconnectFailed?.Invoke(deviceId);
        }

        // ── 连接状态变更处理 ─────────────────────────────

        private void OnConnectionStatusChanged(string deviceId, BluetoothConnectionStatus status)
        {
            if (!_handles.TryGetValue(deviceId, out var handle))
                return;

            if (status == BluetoothConnectionStatus.Disconnected)
            {
                handle.Model.Status = DeviceStatus.Disconnected;
                handle.Model.ConnectionEvents.Add($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} 断开连接");

                // 清除实时数据
                handle.Model.HeartRate = null;
                handle.Model.LastRR = null;
                handle.Model.Hrv = null;
                handle.Model.BatteryLevel = null;

                DeviceDisconnected?.Invoke(deviceId);
            }
        }

        // ── GATT 订阅 ────────────────────────────────────

        private async Task<bool> SubscribeHeartRate(BleDeviceHandle handle,
            IReadOnlyList<GattDeviceService> services)
        {
            var hrService = services.FirstOrDefault(s => s.Uuid == HeartRateServiceUuid);
            if (hrService == null) return false;

            var charsResult = await hrService.GetCharacteristicsAsync();
            if (charsResult.Status != GattCommunicationStatus.Success) return false;

            var hrChar = charsResult.Characteristics.FirstOrDefault(c => c.Uuid == HeartRateMeasurementUuid);
            if (hrChar == null) return false;

            hrChar.ValueChanged += (sender, args) => OnHeartRateValueChanged(handle.Model.DeviceId, args);

            var status = await hrChar.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify);

            handle.HeartRateCharacteristic = hrChar;
            return status == GattCommunicationStatus.Success;
        }

        private async Task SubscribeBattery(BleDeviceHandle handle,
            IReadOnlyList<GattDeviceService> services)
        {
            var battService = services.FirstOrDefault(s => s.Uuid == BatteryServiceUuid);
            if (battService == null) return;

            var charsResult = await battService.GetCharacteristicsAsync();
            if (charsResult.Status != GattCommunicationStatus.Success) return;

            var battChar = charsResult.Characteristics.FirstOrDefault(c => c.Uuid == BatteryLevelUuid);
            if (battChar == null) return;

            // 读取当前电量
            try
            {
                var val = await battChar.ReadValueAsync();
                if (val.Status == GattCommunicationStatus.Success)
                {
                    var reader = DataReader.FromBuffer(val.Value);
                    handle.Model.BatteryLevel = reader.ReadByte();
                }
            }
            catch { /* 电量读取失败不影响连接 */ }

            battChar.ValueChanged += (sender, args) => OnBatteryValueChanged(handle.Model.DeviceId, args);
            await battChar.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify);

            handle.BatteryCharacteristic = battChar;
        }

        private async Task UnsubscribeNotifications(BleDeviceHandle handle)
        {
            if (handle.HeartRateCharacteristic != null)
            {
                try
                {
                    await handle.HeartRateCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.None);
                }
                catch { }
                handle.HeartRateCharacteristic = null;
            }

            if (handle.BatteryCharacteristic != null)
            {
                try
                {
                    await handle.BatteryCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.None);
                }
                catch { }
                handle.BatteryCharacteristic = null;
            }
        }

        // ── BLE 数据解析 ─────────────────────────────────

        /// <summary>
        /// 解析 BLE Heart Rate Measurement 通知数据。
        /// 遵循 Bluetooth SIG Heart Rate Profile 规范。
        /// </summary>
        private void OnHeartRateValueChanged(string deviceId, GattValueChangedEventArgs args)
        {
            if (!_handles.TryGetValue(deviceId, out var handle))
                return;

            try
            {
                var reader = DataReader.FromBuffer(args.CharacteristicValue);
                byte flags = reader.ReadByte();

                // Flags bit 0: 0 = UINT8, 1 = UINT16 心率值格式
                int hr = (flags & 0x01) == 0 ? reader.ReadByte() : reader.ReadUInt16();

                // Flags bit 3: Sensor Contact 状态（跳过）
                // Flags bit 4: Energy Expended（跳过）
                if ((flags & 0x08) != 0)
                    reader.ReadUInt16(); // Energy Expended

                // Flags bit 5: RR-Interval 数据存在
                double[]? rrIntervals = null;
                if ((flags & 0x10) != 0)
                {
                    var rrList = new List<double>();
                    byte[] remaining = new byte[reader.UnconsumedBufferLength];
                    reader.ReadBytes(remaining);

                    int offset = 0;
                    while (offset + 1 < remaining.Length)
                    {
                        ushort raw = (ushort)(remaining[offset] | (remaining[offset + 1] << 8));
                        // BLE 协议：RR 间期单位为 1/1024 秒
                        // 使用浮点除法保留亚毫秒精度（分辨率 ≈ 0.977ms）
                        double rrMs = raw * 1000.0 / 1024.0;
                        offset += 2;

                        if (rrMs >= 300 && rrMs <= 2000) // 基本范围过滤
                            rrList.Add(rrMs);
                    }

                    if (rrList.Count > 0)
                        rrIntervals = rrList.ToArray();
                }

                var model = handle.Model;
                model.HeartRate = hr;
                model.LastDataTimestamp = DateTime.Now;

                // 更新 RR 缓冲
                if (rrIntervals != null)
                {
                    model.RRBuffer.AddRange(rrIntervals);
                    model.LastRR = rrIntervals[^1]; // 最后一个 RR

                    // 限制缓冲区大小（约2小时 @ 1Hz 采样）
                    while (model.RRBuffer.Count > 7200)
                        model.RRBuffer.RemoveAt(0);
                }

                var dataPoint = new HeartRateDataPoint
                {
                    Timestamp = DateTime.Now,
                    ElapsedSeconds = 0, // 由调用方根据会话起始时间计算
                    HeartRate = hr,
                    RRIntervals = rrIntervals
                };

                HeartRateReceived?.Invoke(deviceId, dataPoint);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BLE] 解析心率数据异常 ({deviceId}): {ex.Message}");
            }
        }

        private void OnBatteryValueChanged(string deviceId, GattValueChangedEventArgs args)
        {
            if (!_handles.TryGetValue(deviceId, out var handle))
                return;

            try
            {
                var reader = DataReader.FromBuffer(args.CharacteristicValue);
                int level = reader.ReadByte();
                handle.Model.BatteryLevel = level;
                BatteryUpdated?.Invoke(deviceId, level);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BLE] 解析电量数据异常 ({deviceId}): {ex.Message}");
            }
        }

        // ── 辅助方法 ─────────────────────────────────────

        /// <summary>
        /// 获取指定设备的模型（供 ViewModel 查询使用）
        /// </summary>
        public DeviceModel? GetDeviceModel(string deviceId)
            => _handles.TryGetValue(deviceId, out var handle) ? handle.Model : null;

        /// <summary>是否配置了自动重连</summary>
        public bool AutoReconnect { get; set; }

        private static string CleanDeviceName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "未知设备";
            return name.Trim();
        }

        // ── IDisposable ──────────────────────────────────

        public void Dispose()
        {
            StopScan();

            foreach (var handle in _handles.Values)
            {
                handle.Model.Dispose();
                handle.BleDevice?.Dispose();
            }
            _handles.Clear();
        }

        // ── 内部句柄 ─────────────────────────────────────

        private sealed class BleDeviceHandle
        {
            public DeviceModel Model { get; init; } = null!;
            public BluetoothLEDevice? BleDevice { get; set; }
            public GattCharacteristic? HeartRateCharacteristic { get; set; }
            public GattCharacteristic? BatteryCharacteristic { get; set; }

            public void Dispose()
            {
                BleDevice?.Dispose();
                BleDevice = null;
                HeartRateCharacteristic = null;
                BatteryCharacteristic = null;
            }
        }
    }

    // ── 扫描结果 DTO ─────────────────────────────────────

    /// <summary>
    /// BLE 设备扫描结果
    /// </summary>
    public sealed class BleScanResult
    {
        public ulong BluetoothAddress { get; init; }
        public string LocalName { get; init; } = string.Empty;
        public short RawSignalStrength { get; init; }
        public DateTimeOffset Timestamp { get; init; }
    }
}
