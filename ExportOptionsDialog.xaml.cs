using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;

namespace HeartRateMonitor
{
    public partial class ExportOptionsDialog : Window
    {
        private ObservableCollection<DeviceData> _connectedDevices;

        public string ExportType { get; private set; }
        public List<string> SelectedFields { get; private set; }

        public ExportOptionsDialog(ObservableCollection<DeviceData> connectedDevices)
        {
            InitializeComponent();
            _connectedDevices = connectedDevices;
            ExportType = "历史数据"; // 默认
            SelectedFields = new List<string>();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // 收集选择的字段
            SelectedFields.Clear();
            if (DeviceCheckBox.IsChecked == true) SelectedFields.Add("设备");
            if (HRCheckBox.IsChecked == true) SelectedFields.Add("心率");
            if (RRCheckBox.IsChecked == true) SelectedFields.Add("RR");
            if (SDNNCheckBox.IsChecked == true) SelectedFields.Add("SDNN");
            if (HRVCheckBox.IsChecked == true) SelectedFields.Add("HRV");
            if (BatteryCheckBox.IsChecked == true) SelectedFields.Add("电量");

            // 确定导出类型
            ExportType = HistoryRadio.IsChecked == true ? "历史数据" : "当前快照";

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}