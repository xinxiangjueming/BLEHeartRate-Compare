using System;

namespace HeartRateMonitor.Models
{
    /// <summary>
    /// 单次心率测量数据点（原始 BLE 数据）
    /// </summary>
    public sealed class HeartRateDataPoint
    {
        /// <summary>采集时间戳</summary>
        public DateTime Timestamp { get; init; }

        /// <summary>相对时间（秒，从会话开始计算）</summary>
        public double ElapsedSeconds { get; init; }

        /// <summary>心率值 (bpm)</summary>
        public int HeartRate { get; init; }

        /// <summary>本次通知中包含的 RR 间期列表（毫秒，保留亚毫秒精度）</summary>
        public double[]? RRIntervals { get; init; }
    }
}
