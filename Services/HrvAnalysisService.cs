using System;
using System.Collections.Generic;
using System.Linq;
using HeartRateMonitor.Models;

namespace HeartRateMonitor.Services
{
    /// <summary>
    /// HRV（心率变异性）分析服务。
    /// 从原始 RR 间期序列计算时域 HRV 指标。
    ///
    /// 参考标准：
    /// - Task Force of ESC/NASPE (1996): Heart Rate Variability - Standards of Measurement
    /// - Shaffer &amp; Ginsberg (2017): An Overview of Heart Rate Variability Metrics and Norms
    /// </summary>
    public static class HrvAnalysisService
    {
        // ── 配置常量 ─────────────────────────────────────

        /// <summary>RR 间期合理范围下限 (ms)，低于此值视为噪声</summary>
        private const int RR_MIN_MS = 300;

        /// <summary>RR 间期合理范围上限 (ms)，高于此值视为噪声</summary>
        private const int RR_MAX_MS = 2000;

        /// <summary>异常值检测阈值：与局部中值的偏差百分比</summary>
        private const double OUTLIER_DEVIATION_RATIO = 0.25;

        /// <summary>异常值检测的滑动窗口半径</summary>
        private const int WINDOW_RADIUS = 5;

        /// <summary>最少需要的 RR 间期数量才能计算 HRV</summary>
        private const int MIN_RR_COUNT = 5;

        /// <summary>DFA α1 短期窗口下界（拍数）</summary>
        private const int DFA_N_MIN_SHORT = 4;

        /// <summary>DFA α1 短期窗口上界（拍数）</summary>
        private const int DFA_N_MAX_SHORT = 16;

        /// <summary>DFA α2 长期窗口下界（拍数）</summary>
        private const int DFA_N_MIN_LONG = 16;

            /// <summary>
        /// DFA α2 长期窗口上界（拍数）。
        /// 经典文献 (Peng et al. 1995) 使用 16-64。
        /// </summary>
        private const int DFA_N_MAX_LONG = 64;

        /// <summary>
        /// 计算 DFA α1 所需的最少 RR 间期数量。
        /// 对数间距窗口 4-16，至少需要 ~64 个 RR 间期（约 1 分钟）。
        /// </summary>
        private const int MIN_RR_FOR_DFA_ALPHA1 = DFA_N_MAX_SHORT * 4;

        /// <summary>
        /// 计算 DFA α2 所需的最少 RR 间期数量。
        /// 对数间距窗口 16-64，至少需要 ~256 个 RR 间期（约 4 分钟）。
        /// </summary>
        private const int MIN_RR_FOR_DFA_ALPHA2 = DFA_N_MAX_LONG * 4;

        // ── 公开接口 ─────────────────────────────────────

        /// <summary>
        /// 从原始 RR 缓冲区计算 HRV 指标。
        /// </summary>
        /// <param name="rawRRBuffer">原始 RR 间期序列 (ms，保留亚毫秒精度)，按时间顺序</param>
        /// <returns>HRV 分析结果；如果数据不足则返回 null</returns>
        public static HrvMetrics? Calculate(IList<double> rawRRBuffer)
        {
            if (rawRRBuffer.Count < MIN_RR_COUNT)
                return null;

            // 第一步：范围过滤
            var rangeFiltered = RangeFilter(rawRRBuffer);

            if (rangeFiltered.Count < MIN_RR_COUNT)
                return null;

            // 第二步：基于局部中值的异常值剔除
            var cleaned = OutlierFilter(rangeFiltered);

            if (cleaned.Count < MIN_RR_COUNT)
                return null;

            // 第三步：计算各项指标
            return ComputeMetrics(cleaned);
        }

        // ── 过滤算法 ─────────────────────────────────────

        /// <summary>
        /// 第一步：过滤超出生理合理范围的 RR 间期。
        /// </summary>
        private static List<double> RangeFilter(IList<double> rrList)
        {
            return rrList.Where(rr => rr >= RR_MIN_MS && rr <= RR_MAX_MS).ToList();
        }

        /// <summary>
        /// 第二步：基于局部滑动中值的自适应异常值剔除。
        ///
        /// 相比原代码的简单"前后差值 > 300ms"检测：
        /// - 使用局部中值作为参考基准（而非相邻值），对渐变漂移更稳健
        /// - 使用相对偏差而非绝对阈值，自适应不同心率水平
        /// - 在高 HRV（如运动员、深呼吸）场景下误删率更低
        /// </summary>
        private static List<double> OutlierFilter(List<double> rrList)
        {
            if (rrList.Count < WINDOW_RADIUS * 2 + 1)
                return new List<double>(rrList);

            var result = new List<double>(rrList.Count);

            for (int i = 0; i < rrList.Count; i++)
            {
                // 计算局部窗口内的中值
                int winStart = Math.Max(0, i - WINDOW_RADIUS);
                int winEnd = Math.Min(rrList.Count - 1, i + WINDOW_RADIUS);

                var window = new List<double>(winEnd - winStart + 1);
                for (int j = winStart; j <= winEnd; j++)
                    window.Add(rrList[j]);

                window.Sort();
                double localMedian = window.Count % 2 == 1
                    ? window[window.Count / 2]
                    : (window[window.Count / 2 - 1] + window[window.Count / 2]) / 2.0;

                // 判断当前值与局部中值的偏差
                double deviation = Math.Abs(rrList[i] - localMedian) / localMedian;

                if (deviation <= OUTLIER_DEVIATION_RATIO)
                    result.Add(rrList[i]);
            }

            return result;
        }

        // ── 指标计算 ─────────────────────────────────────

        /// <summary>
        /// 从已清洗的 RR 间期序列计算全部时域指标。
        /// </summary>
        private static HrvMetrics ComputeMetrics(List<double> rr)
        {
            int n = rr.Count;

            // 基本统计
            double sum = 0, sumSq = 0;
            for (int i = 0; i < n; i++)
            {
                sum += rr[i];
                sumSq += (double)rr[i] * rr[i];
            }
            double meanRR = sum / n;

            // SDNN = sqrt(1/N * Σ(RRi - mean)²)
            double sdnn = Math.Sqrt(sumSq / n - meanRR * meanRR);

            // 相邻差值统计
            double sumSqDiff = 0;
            int nn50 = 0, nn20 = 0;
            double sumDiff = 0;

            for (int i = 1; i < n; i++)
            {
                double diff = rr[i] - rr[i - 1];
                sumDiff += diff;
                sumSqDiff += diff * diff;

                double absDiff = Math.Abs(diff);
                if (absDiff > 50) nn50++;
                if (absDiff > 20) nn20++;
            }

            int diffCount = n - 1;

            // RMSSD = sqrt(1/(N-1) * Σ(RRi+1 - RRi)²)
            double rmssd = Math.Sqrt(sumSqDiff / diffCount);

            // SDSD = std(RRi+1 - RRi)
            double meanDiff = sumDiff / diffCount;
            double sdsd = Math.Sqrt(sumSqDiff / diffCount - meanDiff * meanDiff);

            // pNN50, pNN20 (百分比)
            double pnn50 = (double)nn50 / diffCount * 100.0;
            double pnn20 = (double)nn20 / diffCount * 100.0;

            // DFA 分析（需要足够长的数据）
            double? alpha1 = null, alpha2 = null;
            if (n >= MIN_RR_FOR_DFA_ALPHA1)
            {
                double a1 = ComputeDfaAlpha(rr, DFA_N_MIN_SHORT, DFA_N_MAX_SHORT);
                alpha1 = double.IsNaN(a1) ? null : a1;
            }
            if (n >= MIN_RR_FOR_DFA_ALPHA2)
            {
                double a2 = ComputeDfaAlpha(rr, DFA_N_MIN_LONG, DFA_N_MAX_LONG);
                alpha2 = double.IsNaN(a2) ? null : a2;
            }

            return new HrvMetrics
            {
                SampleCount = n,
                MeanRR = meanRR,
                MinRR = rr.Min(),
                MaxRR = rr.Max(),
                SDNN = sdnn,
                RMSSD = rmssd,
                SDSD = sdsd,
                PNN50 = pnn50,
                PNN20 = pnn20,
                DFA_alpha1 = alpha1,
                DFA_alpha2 = alpha2
            };
        }

        // ── DFA 算法 ─────────────────────────────────────

        /// <summary>
        /// Detrended Fluctuation Analysis (DFA)。
        ///
        /// 算法步骤（Peng et al. 1995 标准实现）：
        /// 1. 对 RR 间期序列去均值后求累积偏差（积分），得到轮廓 y(k)
        /// 2. 生成对数间距的窗口大小序列
        /// 3. 对每个窗口大小 n，正向+反向划分窗口，每个窗口做线性去趋势
        /// 4. 计算去趋势后的均方根波动 F(n)
        /// 5. 在双对数坐标 log(n) vs log(F(n)) 上做线性回归
        /// 6. 回归斜率即为标度指数 α
        ///
        /// 参考：
        /// - Peng et al. (1994) "Mosaic organization of DNA nucleotides"
        /// - Peng et al. (1995) "Quantification of scaling exponents and crossover
        ///   phenomena in nonstationary heartbeat time series"
        /// - Hardstone et al. (2012) "Detrended fluctuation analysis: A scale-free
        ///   view on neuronal oscillations"
        /// </summary>
        /// <param name="rr">已清洗的 RR 间期序列</param>
        /// <param name="nMin">窗口大小下界（含）</param>
        /// <param name="nMax">窗口大小上界（含）</param>
        /// <returns>标度指数 α（log-log 回归斜率）</returns>
        private static double ComputeDfaAlpha(List<double> rr, int nMin, int nMax)
        {
            int N = rr.Count;

            // 步骤 1：计算累积偏差序列 y(k) = Σ_{i=1}^{k} (RR_i - RR̄)
            double meanRR = 0;
            for (int i = 0; i < N; i++) meanRR += rr[i];
            meanRR /= N;

            double[] y = new double[N];
            double cumSum = 0;
            for (int i = 0; i < N; i++)
            {
                cumSum += rr[i] - meanRR;
                y[i] = cumSum;
            }

            // 步骤 2：生成对数间距的窗口大小序列
            // 避免小窗口过度采样导致 log-log 回归偏差
            var windowSizes = GenerateLogSpacedWindows(nMin, nMax);

            // 步骤 3 & 4：对每个窗口大小 n，计算波动函数 F(n)
            var logN = new List<double>();
            var logF = new List<double>();

            foreach (int n in windowSizes)
            {
                if (n > N / 2) break; // 至少需要 2 个完整窗口

                int numWindows = N / n;
                if (numWindows < 4) continue;

                double sumSqResidual = 0;
                int totalPoints = 0;

                // 正向窗口
                for (int win = 0; win < numWindows; win++)
                {
                    int start = win * n;
                    sumSqResidual += ComputeWindowResidualSumSq(y, start, n);
                    totalPoints += n;
                }

                // 反向窗口（从末尾开始，与正向窗口等权）
                for (int win = 0; win < numWindows; win++)
                {
                    int start = N - (win + 1) * n;
                    if (start < 0) break;
                    sumSqResidual += ComputeWindowResidualSumSq(y, start, n);
                    totalPoints += n;
                }

                if (totalPoints > 0)
                {
                    double fn = Math.Sqrt(sumSqResidual / totalPoints);
                    if (fn > 0)
                    {
                        logN.Add(Math.Log(n));
                        logF.Add(Math.Log(fn));
                    }
                }
            }

            // 步骤 5：log-log 线性回归
            if (logN.Count < 2)
                return double.NaN;

            return LinearRegressionSlope(logN, logF);
        }

        /// <summary>
        /// 生成对数间距的窗口大小序列。
        /// 在 log(nMin) 到 log(nMax) 之间均匀取点，
        /// 保证小窗口细密、大窗口稀疏，避免回归偏差。
        /// </summary>
        private static List<int> GenerateLogSpacedWindows(int nMin, int nMax)
        {
            var sizes = new List<int>();
            double logMin = Math.Log(nMin);
            double logMax = Math.Log(nMax);

            // 至少 8 个数据点用于回归
            int numPoints = Math.Max(8, (int)((logMax - logMin) / Math.Log(1.3)) + 1);
            double step = (logMax - logMin) / (numPoints - 1);

            int prev = -1;
            for (int i = 0; i < numPoints; i++)
            {
                int n = (int)Math.Round(Math.Exp(logMin + i * step));
                n = Math.Max(nMin, Math.Min(nMax, n));
                if (n != prev)
                {
                    sizes.Add(n);
                    prev = n;
                }
            }

            return sizes;
        }

        /// <summary>
        /// 对轮廓序列 y 中从 start 开始的 n 个点做线性拟合，
        /// 返回残差平方和 Σ(y_i - ŷ_i)²。
        ///
        /// 使用最小二乘法拟合 y = a + b*x，其中 x = 0, 1, ..., n-1。
        /// 残差 = y_i - (a + b*i)
        /// </summary>
        private static double ComputeWindowResidualSumSq(double[] y, int start, int n)
        {
            // 计算线性回归参数（避免分配）
            // x = 0, 1, ..., n-1
            // Σx = n(n-1)/2, Σx² = n(n-1)(2n-1)/6
            double sumX = (double)n * (n - 1) / 2.0;
            double sumX2 = (double)n * (n - 1) * (2 * n - 1) / 6.0;
            double sumY = 0, sumXY = 0;

            for (int i = 0; i < n; i++)
            {
                double yi = y[start + i];
                sumY += yi;
                sumXY += i * yi;
            }

            double det = n * sumX2 - sumX * sumX;
            if (Math.Abs(det) < 1e-12)
            {
                // 退化情况：所有 x 相同（不应发生）
                double meanY = sumY / n;
                double ss = 0;
                for (int i = 0; i < n; i++)
                {
                    double diff = y[start + i] - meanY;
                    ss += diff * diff;
                }
                return ss;
            }

            double b = (n * sumXY - sumX * sumY) / det;  // 斜率
            double a = (sumY - b * sumX) / n;              // 截距

            // 计算残差平方和
            double ssResidual = 0;
            for (int i = 0; i < n; i++)
            {
                double predicted = a + b * i;
                double residual = y[start + i] - predicted;
                ssResidual += residual * residual;
            }

            return ssResidual;
        }

        /// <summary>
        /// 简单线性回归：y = a + b*x，返回斜率 b。
        /// 使用最小二乘法。
        /// </summary>
        private static double LinearRegressionSlope(List<double> x, List<double> y)
        {
            int n = x.Count;
            double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;

            for (int i = 0; i < n; i++)
            {
                sumX += x[i];
                sumY += y[i];
                sumXY += x[i] * y[i];
                sumX2 += x[i] * x[i];
            }

            double det = n * sumX2 - sumX * sumX;
            if (Math.Abs(det) < 1e-12)
                return double.NaN;

            return (n * sumXY - sumX * sumY) / det;
        }
    }
}
