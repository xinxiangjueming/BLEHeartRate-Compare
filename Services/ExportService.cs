using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HeartRateMonitor.Models;

namespace HeartRateMonitor.Services
{
    /// <summary>
    /// 数据导出服务。支持两种导出模式：
    /// - 历史数据：按时间序列输出，每个设备占多列
    /// - 当前快照：每设备一行，输出最新瞬时值
    /// </summary>
    public static class ExportService
    {
        // ── 导出数据结构 ─────────────────────────────────

        /// <summary>
        /// 设备的时间序列历史数据（用于"历史数据"导出模式）
        /// </summary>
        public sealed class DeviceTimeSeries
        {
            public string Alias { get; init; } = string.Empty;
            public int?[] HeartRateData { get; init; } = Array.Empty<int?>();
            public double?[] RRData { get; init; } = Array.Empty<double?>();
            public double?[] SDNNData { get; init; } = Array.Empty<double?>();
            public double?[] RMSSDData { get; init; } = Array.Empty<double?>();
            public double?[] DFA_alpha1Data { get; init; } = Array.Empty<double?>();
            public double?[] DFA_alpha2Data { get; init; } = Array.Empty<double?>();
            public int?[] BatteryData { get; init; } = Array.Empty<int?>();
            public List<string> ConnectionEvents { get; init; } = new();
        }

        /// <summary>
        /// 单个设备的当前快照数据（用于"当前快照"导出模式）
        /// </summary>
        public sealed class DeviceSnapshot
        {
            public string Alias { get; init; } = string.Empty;
            public int? HeartRate { get; init; }
            public double? LastRR { get; init; }
            public HrvMetrics? Hrv { get; init; }
            public int? BatteryLevel { get; init; }
        }

        // ── 可导出的字段列表 ─────────────────────────────

        public static readonly string[] AvailableFields = { "设备", "心率", "RR", "SDNN", "RMSSD", "DFA α1", "DFA α2", "电量" };

        // ── 历史数据导出 ─────────────────────────────────

        /// <summary>
        /// 生成历史数据 CSV。
        /// </summary>
        /// <param name="timeLabels">时间轴（秒）</param>
        /// <param name="devices">各设备的时间序列数据</param>
        /// <param name="selectedFields">用户选择的导出字段</param>
        public static string GenerateHistoryCsv(
            int[] timeLabels,
            IReadOnlyList<DeviceTimeSeries> devices,
            IReadOnlyList<string> selectedFields)
        {
            var csv = new StringBuilder();
            var timeSeriesFields = selectedFields.Where(f => f != "设备").ToList();

            // ── 表头 ──
            var headers = new List<string> { "时间" };
            foreach (var dev in devices)
            {
                foreach (var field in timeSeriesFields)
                    headers.Add($"{dev.Alias}_{field}");
            }
            csv.AppendLine(CsvRow(headers));

            // ── 数据行 ──
            for (int i = 0; i < timeLabels.Length; i++)
            {
                var row = new List<string> { $"{timeLabels[i]}s" };

                foreach (var dev in devices)
                {
                    foreach (var field in timeSeriesFields)
                    {
                        string value = field switch
                        {
                            "心率" => SafeGet(dev.HeartRateData, i, v => v.ToString()!),
                            "RR" => SafeGet(dev.RRData, i, v => v.ToString("F1")),
                            "SDNN" => SafeGet(dev.SDNNData, i, v => v.ToString("F1")),
                            "RMSSD" => SafeGet(dev.RMSSDData, i, v => v.ToString("F1")),
                            "DFA α1" => SafeGet(dev.DFA_alpha1Data, i, v => v.ToString("F2")),
                            "DFA α2" => SafeGet(dev.DFA_alpha2Data, i, v => v.ToString("F2")),
                            "电量" => SafeGet(dev.BatteryData, i, v => v.ToString()!),
                            _ => ""
                        };
                        row.Add(value);
                    }
                }
                csv.AppendLine(CsvRow(row));
            }

            // ── 汇总区 ──
            csv.AppendLine();
            csv.AppendLine("# Summary");
            csv.AppendLine($"# Total Duration: {timeLabels.Length}s");

            foreach (var dev in devices)
            {
                var hrValues = dev.HeartRateData.Where(v => v.HasValue).Select(v => v.Value).ToArray();
                if (hrValues.Length > 0)
                {
                    double avg = hrValues.Average();
                    csv.AppendLine($"# Device: {dev.Alias}, Avg HR: {avg:F0} bpm, " +
                                  $"Max: {hrValues.Max()} bpm, Min: {hrValues.Min()} bpm");
                }
                else
                {
                    csv.AppendLine($"# Device: {dev.Alias}, Avg HR: --, Max: --, Min: --");
                }

                var lastBattery = dev.BatteryData.LastOrDefault();
                string batteryStr = lastBattery.HasValue ? $"{lastBattery.Value}%" : "--";
                csv.AppendLine($"# Device: {dev.Alias}, Battery: {batteryStr}");

                foreach (var evt in dev.ConnectionEvents)
                    csv.AppendLine($"# Event: {evt}");
            }

            return csv.ToString();
        }

        // ── 快照导出 ─────────────────────────────────────

        /// <summary>
        /// 生成当前快照 CSV。
        /// </summary>
        public static string GenerateSnapshotCsv(
            IReadOnlyList<DeviceSnapshot> devices,
            IReadOnlyList<string> selectedFields)
        {
            var csv = new StringBuilder();
            var fields = selectedFields.Where(f => f != "设备").ToList();

            // 表头
            csv.AppendLine(CsvRow(new[] { "设备" }.Concat(fields).ToList()));

            // 数据行
            foreach (var dev in devices)
            {
                var row = new List<string> { dev.Alias };
                foreach (var field in fields)
                {
                    string value = field switch
                    {
                        "心率" => dev.HeartRate?.ToString() ?? "",
                        "RR" => dev.LastRR?.ToString("F1") ?? "",
                        "SDNN" => dev.Hrv?.SDNN.ToString("F1") ?? "",
                        "RMSSD" => dev.Hrv?.RMSSD.ToString("F1") ?? "",
                        "DFA α1" => dev.Hrv?.DFA_alpha1?.ToString("F2") ?? "",
                        "DFA α2" => dev.Hrv?.DFA_alpha2?.ToString("F2") ?? "",
                        "电量" => dev.BatteryLevel?.ToString() ?? "",
                        _ => ""
                    };
                    row.Add(value);
                }
                csv.AppendLine(CsvRow(row));
            }

            return csv.ToString();
        }

        // ── 文件写入 ─────────────────────────────────────

        /// <summary>
        /// 将 CSV 内容保存到指定路径（UTF-8 with BOM）。
        /// </summary>
        public static void SaveToFile(string filePath, string csvContent)
        {
            File.WriteAllText(filePath, csvContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }

        /// <summary>
        /// 生成默认文件名
        /// </summary>
        public static string GenerateFileName()
            => $"心率数据_{DateTime.Now:yyyyMMdd_HHmmss}.csv";

        // ── 内部辅助 ─────────────────────────────────────

        private static string SafeGet<T>(T?[] array, int index, Func<T, string> formatter) where T : struct
        {
            if (index < array.Length && array[index].HasValue)
                return formatter(array[index].Value);
            return "";
        }

        private static string CsvRow(IReadOnlyList<string> values)
        {
            // 对包含逗号、引号或换行的字段进行引号转义
            var escaped = values.Select(v =>
            {
                if (v.Contains(',') || v.Contains('"') || v.Contains('\n'))
                    return $"\"{v.Replace("\"", "\"\"")}\"";
                return v;
            });
            return string.Join(",", escaped);
        }
    }
}
