using System;
using System.IO;
using System.Text;
using HeartRateMonitor.Models;

namespace HeartRateMonitor.Services
{
    /// <summary>
    /// 自动保存服务。连接设备后自动将实时数据追加写入 CSV 文件。
    /// 每秒调用一次 RecordSnapshot 记录所有已连接设备的当前数据。
    ///
    /// CSV 格式：
    /// 时间,时间戳,设备,心率,RR,SDNN,RMSSD,DFA_α1,DFA_α2,电量
    /// 1s,2026-05-16 10:00:01,Polar H10,72,833,45.2,32.1,0.95,1.02,85
    /// </summary>
    public sealed class AutoSaveService : IDisposable
    {
        private StreamWriter? _writer;
        private bool _headerWritten;

        /// <summary>是否正在自动保存</summary>
        public bool IsActive => _writer != null;

        /// <summary>当前保存的文件路径</summary>
        public string? FilePath { get; private set; }

        // ── 启动/停止 ────────────────────────────────────

        /// <summary>
        /// 启动自动保存到指定文件。如果文件已存在则覆盖。
        /// </summary>
        /// <param name="filePath">CSV 文件路径</param>
        public void Start(string filePath)
        {
            Stop(); // 关闭之前的文件

            FilePath = filePath;
            var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            _headerWritten = false;
        }

        /// <summary>
        /// 停止自动保存并关闭文件。
        /// </summary>
        public void Stop()
        {
            if (_writer != null)
            {
                _writer.Flush();
                _writer.Dispose();
                _writer = null;
            }
            _headerWritten = false;
        }

        // ── 数据记录 ─────────────────────────────────────

        /// <summary>
        /// 记录一次数据快照。每秒调用一次。
        /// </summary>
        /// <param name="elapsedSeconds">从会话开始的秒数</param>
        /// <param name="device">设备数据模型</param>
        public void RecordSnapshot(int elapsedSeconds, DeviceModel device)
        {
            if (_writer == null) return;

            // 首次写入时输出表头
            if (!_headerWritten)
            {
                _writer.WriteLine("时间,时间戳,设备,心率,RR,SDNN,RMSSD,DFA_α1,DFA_α2,电量");
                _headerWritten = true;
            }

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string hr = device.HeartRate?.ToString() ?? "";
            string rr = device.LastRR?.ToString("F1") ?? "";
            string sdnn = device.Hrv?.SDNN.ToString("F1") ?? "";
            string rmssd = device.Hrv?.RMSSD.ToString("F1") ?? "";
            string dfa1 = device.Hrv?.DFA_alpha1?.ToString("F2") ?? "";
            string dfa2 = device.Hrv?.DFA_alpha2?.ToString("F2") ?? "";
            string battery = device.BatteryLevel?.ToString() ?? "";

            // CSV 字段中如包含逗号则引号包裹
            string safeName = EscapeCsvField(device.Alias);

            _writer.WriteLine($"{elapsedSeconds}s,{timestamp},{safeName},{hr},{rr},{sdnn},{rmssd},{dfa1},{dfa2},{battery}");
        }

        /// <summary>
        /// 立即将缓冲区数据写入磁盘。
        /// </summary>
        public void Flush()
        {
            _writer?.Flush();
        }

        // ── 辅助 ─────────────────────────────────────────

        private static string EscapeCsvField(string field)
        {
            if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
                return $"\"{field.Replace("\"", "\"\"")}\"";
            return field;
        }

        // ── IDisposable ──────────────────────────────────

        public void Dispose()
        {
            Stop();
        }
    }
}
