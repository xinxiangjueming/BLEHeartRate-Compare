using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HeartRateMonitor.Models
{
    /// <summary>
    /// 单个 BLE 心率设备的完整数据模型。
    /// 实现 INotifyPropertyChanged 以支持 WPF 数据绑定。
    /// </summary>
    public sealed class DeviceModel : INotifyPropertyChanged, IDisposable
    {
        // ── 设备标识 ─────────────────────────────────────

        /// <summary>BLE 设备 ID（系统级唯一标识）</summary>
        public string DeviceId { get; init; } = string.Empty;

        /// <summary>清洗后的设备名称（仅保留字母数字和空格）</summary>
        public string CleanName { get; init; } = string.Empty;

        private string _alias = string.Empty;
        /// <summary>用户可见的设备别名（默认等于 CleanName）</summary>
        public string Alias
        {
            get => _alias;
            set { if (_alias != value) { _alias = value; OnPropertyChanged(); } }
        }

        // ── 连接状态 ─────────────────────────────────────

        private DeviceStatus _status = DeviceStatus.Discovered;
        public DeviceStatus Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    var old = _status;
                    _status = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsConnected));
                    StatusChanged?.Invoke(this, old, value);
                }
            }
        }

        /// <summary>是否处于已连接状态</summary>
        public bool IsConnected => _status == DeviceStatus.Connected;

        // ── 实时生理指标 ─────────────────────────────────

        private int? _heartRate;
        /// <summary>最新心率值 (bpm)</summary>
        public int? HeartRate
        {
            get => _heartRate;
            set { if (_heartRate != value) { _heartRate = value; OnPropertyChanged(); } }
        }

        private int? _batteryLevel;
        /// <summary>电池电量 (%)</summary>
        public int? BatteryLevel
        {
            get => _batteryLevel;
            set { if (_batteryLevel != value) { _batteryLevel = value; OnPropertyChanged(); } }
        }

        private double? _lastRR;
        /// <summary>最新 RR 间期 (ms，保留亚毫秒精度)</summary>
        public double? LastRR
        {
            get => _lastRR;
            set { if (_lastRR != value) { _lastRR = value; OnPropertyChanged(); } }
        }

        private HrvMetrics? _hrv;
        /// <summary>最新 HRV 分析结果</summary>
        public HrvMetrics? Hrv
        {
            get => _hrv;
            set { if (_hrv != value) { _hrv = value; OnPropertyChanged(); } }
        }

        // ── 图表外观 ─────────────────────────────────────

        private int _colorIndex;
        /// <summary>预定义颜色索引</summary>
        public int ColorIndex
        {
            get => _colorIndex;
            set
            {
                if (_colorIndex != value)
                {
                    _colorIndex = value;
                    OnPropertyChanged();
                    ColorChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private bool _isVisible = true;
        /// <summary>图表中该设备曲线是否可见</summary>
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

        // ── 内部数据缓冲 ─────────────────────────────────

        /// <summary>最后一次收到心率数据的时间</summary>
        public DateTime LastDataTimestamp { get; set; }

        /// <summary>原始 RR 间期缓冲区（毫秒，保留亚毫秒精度，用于 HRV 计算）</summary>
        public List<double> RRBuffer { get; } = new List<double>();

        /// <summary>连接/断开事件日志</summary>
        public List<string> ConnectionEvents { get; } = new List<string>();

        // ── 事件 ─────────────────────────────────────────

        /// <summary>颜色变更事件</summary>
        public event EventHandler? ColorChanged;

        /// <summary>可见性变更事件</summary>
        public event EventHandler? VisibilityChanged;

        /// <summary>连接状态变更事件 (sender, oldStatus, newStatus)</summary>
        public event Action<DeviceModel, DeviceStatus, DeviceStatus>? StatusChanged;

        // ── INotifyPropertyChanged ──────────────────────

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? string.Empty));

        // ── IDisposable ──────────────────────────────────

        public void Dispose()
        {
            RRBuffer.Clear();
            ConnectionEvents.Clear();
        }
    }
}
