using System;

namespace HeartRateMonitor.Services
{
    /// <summary>
    /// HSL 颜色工具：Hex ↔ HSL 转换，深色模式自动变体生成。
    /// </summary>
    public static class HslColorHelper
    {
        /// <summary>
        /// 根据浅色模式的 Hex 颜色，自动计算深色模式对应颜色。
        /// 规则：色相不变，饱和度 ×1.15，亮度压到 28%~38%。
        /// </summary>
        public static string DarkVariant(string hex)
        {
            HexToHsl(hex, out double h, out double s, out double l);

            s = Math.Clamp(s * 1.15, 0.0, 1.0);
            l = Math.Clamp(l, 0.28, 0.38);

            HslToHex(h, s, l, out byte r, out byte g, out byte b);
            return $"#{r:X2}{g:X2}{b:X2}";
        }

        /// <summary>
        /// 将 #RRGGBB 转换为 HSL（H 0~360, S/L 0~1）。
        /// </summary>
        public static void HexToHsl(string hex, out double h, out double s, out double l)
        {
            hex = hex.TrimStart('#');
            int offset = hex.Length == 8 ? 2 : 0; // 跳过 #AARRGGBB 的 AA
            byte r = Convert.ToByte(hex.Substring(offset, 2), 16);
            byte g = Convert.ToByte(hex.Substring(offset + 2, 2), 16);
            byte b = Convert.ToByte(hex.Substring(offset + 4, 2), 16);

            double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
            double max = Math.Max(rd, Math.Max(gd, bd));
            double min = Math.Min(rd, Math.Min(gd, bd));
            double d = max - min;

            l = (max + min) / 2.0;

            if (d < 1e-6)
            {
                h = 0; s = 0;
                return;
            }

            s = l < 0.5 ? d / (max + min) : d / (2.0 - max - min);

            if (max == rd)
                h = ((gd - bd) / d + (gd < bd ? 6 : 0)) * 60;
            else if (max == gd)
                h = ((bd - rd) / d + 2) * 60;
            else
                h = ((rd - gd) / d + 4) * 60;
        }

        /// <summary>
        /// 将 HSL（H 0~360, S/L 0~1）转换为 RGB。
        /// </summary>
        public static void HslToHex(double h, double s, double l, out byte r, out byte g, out byte b)
        {
            double c = (1.0 - Math.Abs(2.0 * l - 1.0)) * s;
            double x = c * (1.0 - Math.Abs(h / 60.0 % 2.0 - 1.0));
            double m = l - c / 2.0;

            double rd, gd, bd;
            if (h < 60)       { rd = c; gd = x; bd = 0; }
            else if (h < 120) { rd = x; gd = c; bd = 0; }
            else if (h < 180) { rd = 0; gd = c; bd = x; }
            else if (h < 240) { rd = 0; gd = x; bd = c; }
            else if (h < 300) { rd = x; gd = 0; bd = c; }
            else              { rd = c; gd = 0; bd = x; }

            r = (byte)Math.Round((rd + m) * 255);
            g = (byte)Math.Round((gd + m) * 255);
            b = (byte)Math.Round((bd + m) * 255);
        }
    }
}
