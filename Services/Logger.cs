using System;
using System.IO;
using System.Threading;

namespace HeartRateMonitor.Services
{
    /// <summary>
    /// 简单的文件日志服务，线程安全，按日期滚动。
    /// </summary>
    public static class Logger
    {
        private static readonly object _lock = new();
        private static string? _logDir;
        private static string? _currentLogFile;

        /// <summary>
        /// 日志保存目录。
        /// </summary>
        public static string LogDirectory
        {
            get
            {
                if (_logDir != null) return _logDir;

                // 优先：exe 所在目录下的 logs/
                var exeDir = AppDomain.CurrentDomain.BaseDirectory;
                var logsDir = Path.Combine(exeDir, "logs");

                try
                {
                    Directory.CreateDirectory(logsDir);
                    _logDir = logsDir;
                }
                catch
                {
                    // 回退：桌面
                    var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    logsDir = Path.Combine(desktop, "HeartRateMonitor_logs");
                    try
                    {
                        Directory.CreateDirectory(logsDir);
                        _logDir = logsDir;
                    }
                    catch
                    {
                        // 最终回退：临时目录
                        _logDir = Path.Combine(Path.GetTempPath(), "HeartRateMonitor_logs");
                        Directory.CreateDirectory(_logDir);
                    }
                }

                return _logDir;
            }
        }

        private static string GetLogFile()
        {
            var today = DateTime.Now.ToString("yyyyMMdd");
            var path = Path.Combine(LogDirectory, $"HeartRateMonitor_{today}.log");
            if (_currentLogFile != path)
                _currentLogFile = path;
            return _currentLogFile;
        }

        public static void Info(string message) => Write("INFO", message);
        public static void Warn(string message) => Write("WARN", message);
        public static void Error(string message) => Write("ERROR", message);
        public static void Error(string message, Exception ex) => Write("ERROR", $"{message}: {ex}");

        private static void Write(string level, string message)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var line = $"[{timestamp}] [{level}] {message}{Environment.NewLine}";

                lock (_lock)
                {
                    File.AppendAllText(GetLogFile(), line);
                }
            }
            catch
            {
                // 日志写入失败不应影响应用运行
            }
        }
    }
}
