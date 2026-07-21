using System.ComponentModel;
using HeartRateMonitor.Resources;

namespace HeartRateMonitor.Resources
{
    /// <summary>
    /// XAML 资源代理类。提供 INotifyPropertyChanged 支持，使 XAML 绑定能响应语言切换。
    /// </summary>
    public sealed class StringResources : INotifyPropertyChanged
    {
        public static StringResources Instance { get; } = new();

        // ── Main Window ──
        public string AppTitle => Strings.AppTitle;
        public string ChartTitle => Strings.ChartTitle;
        public string TimeAxis => Strings.TimeAxis;

        // ── Buttons ──
        public string BtnScan => Strings.BtnScan;
        public string BtnScanning => Strings.BtnScanning;
        public string BtnExport => Strings.BtnExport;
        public string BtnAutoReconnect => Strings.BtnAutoReconnect;
        public string BtnShowAll => Strings.BtnShowAll;
        public string BtnRecord => Strings.BtnRecord;
        public string BtnRecording => Strings.BtnRecording;
        public string BtnClearChart => Strings.BtnClearChart;
        public string BtnApply => Strings.BtnApply;
        public string BtnConnect => Strings.BtnConnect;
        public string BtnDisconnect => Strings.BtnDisconnect;
        public string BtnOk => Strings.BtnOk;
        public string BtnCancel => Strings.BtnCancel;
        public string BtnYes => Strings.BtnYes;
        public string BtnNo => Strings.BtnNo;

        // ── Tray ──
        public string TrayShow => Strings.TrayShow;
        public string TrayExit => Strings.TrayExit;
        public string TrayMinimized => Strings.TrayMinimized;

        // ── Headers ──
        public string HeaderScanControl => Strings.HeaderScanControl;
        public string HeaderDiscoveredDevices => Strings.HeaderDiscoveredDevices;
        public string HeaderRealtimeData => Strings.HeaderRealtimeData;

        // ── Table Columns ──
        public string ColDevice => Strings.ColDevice;
        public string ColHeartRate => Strings.ColHeartRate;
        public string ColBattery => Strings.ColBattery;
        public string ColColor => Strings.ColColor;
        public string ColAction => Strings.ColAction;

        // ── Labels ──
        public string LabelYAxisRange => Strings.LabelYAxisRange;
        public string LabelMinValue => Strings.LabelMinValue;
        public string LabelMaxValue => Strings.LabelMaxValue;

        // ── Disconnected ──
        public string Disconnected => Strings.Disconnected;

        // ── Export Dialog ──
        public string ExportDialogTitle => Strings.ExportDialogTitle;
        public string ExportTypeHistory => Strings.ExportTypeHistory;
        public string ExportTypeSnapshot => Strings.ExportTypeSnapshot;
        public string ExportSelectFields => Strings.ExportSelectFields;
        public string ExportBtnExport => Strings.ExportBtnExport;
        public string ExportBtnCancel => Strings.ExportBtnCancel;

        // ── Confirm Dialog ──
        public string ConfirmTitle => Strings.ConfirmTitle;

        // ── Message Dialog ──
        public string MessageTitle => Strings.MessageTitle;

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
