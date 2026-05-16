namespace HeartRateMonitor.Models
{
    /// <summary>
    /// BLE 设备连接状态
    /// </summary>
    public enum DeviceStatus
    {
        /// <summary>已发现但未连接</summary>
        Discovered,

        /// <summary>正在连接中</summary>
        Connecting,

        /// <summary>已连接并正常工作</summary>
        Connected,

        /// <summary>连接断开</summary>
        Disconnected,

        /// <summary>正在尝试重连</summary>
        Reconnecting,

        /// <summary>重连失败，已放弃</summary>
        Failed
    }
}
