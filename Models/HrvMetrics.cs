namespace HeartRateMonitor.Models
{
    /// <summary>
    /// 心率变异性 (HRV) 分析结果
    /// </summary>
    public sealed class HrvMetrics
    {
        /// <summary>用于计算的 RR 间期数量</summary>
        public int SampleCount { get; init; }

        // ── 时域指标 ──────────────────────────────────────

        /// <summary>RR 间期标准差 (ms)，最基本的 HRV 指标</summary>
        public double SDNN { get; init; }

        /// <summary>
        /// 相邻 RR 间期差值的均方根 (ms)，即 RMSSD。
        /// 反映副交感神经活性，对短时变化更敏感。
        /// </summary>
        public double RMSSD { get; init; }

        /// <summary>
        /// 相邻 RR 间期差值的标准差 (ms)。
        /// 与 RMSSD 高度相关，但对异常值稍敏感。
        /// </summary>
        public double SDSD { get; init; }

        /// <summary>
        /// 相邻 RR 间期差值超过 50ms 的百分比 (%)。
        /// 简单直观的副交感神经指标，与 RMSSD 高度相关。
        /// </summary>
        public double PNN50 { get; init; }

        /// <summary>
        /// 相邻 RR 间期差值超过 20ms 的百分比 (%)。
        /// </summary>
        public double PNN20 { get; init; }

        /// <summary>RR 间期均值 (ms)</summary>
        public double MeanRR { get; init; }

        /// <summary>RR 间期最小值 (ms)</summary>
        public double MinRR { get; init; }

        /// <summary>RR 间期最大值 (ms)</summary>
        public double MaxRR { get; init; }

        // ── 非线性指标 (DFA) ───────────────────────────────

        /// <summary>
        /// DFA α1 — 短期标度指数 (4 ≤ n ≤ 16 拍)。
        /// 反映短期分形相关性。健康静息心率典型值 ≈ 1.0（1/f 噪声）。
        /// - α1 ≈ 0.5：白噪声（无相关性）
        /// - 0.5 &lt; α1 &lt; 1.0：长程正相关（粉红噪声）
        /// - α1 ≈ 1.0：1/f 噪声（健康典型值）
        /// - α1 &gt; 1.0：持续性更强的长程相关
        /// - α1 → 0：反相关
        /// </summary>
        public double? DFA_alpha1 { get; init; }

        /// <summary>
        /// DFA α2 — 长期标度指数 (16 ≤ n ≤ 64 拍)。
        /// 反映长期分形相关性。正常值通常高于 α1。
        /// 需要较长的数据序列（&gt;500 个 RR 间期）才可靠。
        /// </summary>
        public double? DFA_alpha2 { get; init; }
    }
}
