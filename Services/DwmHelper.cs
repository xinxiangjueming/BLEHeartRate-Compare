using System;
using System.Runtime.InteropServices;

namespace HeartRateMonitor.Services
{
    /// <summary>
    /// DWM API 封装，用于 Mica 材质和标题栏暗色模式。
    /// </summary>
    internal static class DwmHelper
    {
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;

        // DWM_SYSTEMBACKDROP_TYPE 枚举
        private const int DWMSBT_AUTO = 0;
        private const int DWMSBT_NONE = 1;
        private const int DWMSBT_MAINWINDOW = 2;    // Mica
        private const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic
        private const int DWMSBT_TABBEDWINDOW = 4;   // Mica Alt

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        /// <summary>
        /// 启用 Mica 材质背景。返回是否成功。
        /// </summary>
        public static bool EnableMica(IntPtr hwnd, bool darkMode)
        {
            // 设置 Mica 背景类型
            int backdropType = DWMSBT_MAINWINDOW;
            int hr = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE,
                ref backdropType, sizeof(int));

            // Win11 22H2 以下可能失败，尝试旧 API (DWMWA_MICA_EFFECT = 1029)
            if (hr != 0)
            {
                int micaEnabled = 1;
                hr = DwmSetWindowAttribute(hwnd, 1029, ref micaEnabled, sizeof(int));
            }

            if (hr == 0)
            {
                SetDarkModeTitleBar(hwnd, darkMode);
            }

            return hr == 0;
        }

        /// <summary>
        /// 设置标题栏暗色/亮色模式。
        /// </summary>
        public static void SetDarkModeTitleBar(IntPtr hwnd, bool dark)
        {
            int useDark = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE,
                ref useDark, sizeof(int));
        }

        /// <summary>
        /// 设置标题栏背景色（ARGB 格式，如 0xFF1E1E1E）。
        /// 传入 -1 恢复系统默认。
        /// </summary>
        public static void SetCaptionColor(IntPtr hwnd, int argbColor)
        {
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR,
                ref argbColor, sizeof(int));
        }

        /// <summary>
        /// 设置窗口边框颜色（ARGB 格式）。
        /// 传入 -1 恢复系统默认。
        /// </summary>
        public static void SetBorderColor(IntPtr hwnd, int argbColor)
        {
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR,
                ref argbColor, sizeof(int));
        }

        /// <summary>
        /// 强制窗口圆角（Win11+）。
        /// </summary>
        public static void EnableRoundedCorners(IntPtr hwnd)
        {
            int cornerPref = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE,
                ref cornerPref, sizeof(int));
        }
    }
}
