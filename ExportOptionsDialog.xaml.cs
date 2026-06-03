using System.Collections.Generic;
using System.Windows;
using HeartRateMonitor.Models;

namespace HeartRateMonitor
{
    public partial class ExportOptionsDialog : Window
    {
        public string ExportType { get; private set; } = "历史数据";
        public List<string> SelectedFields { get; private set; } = new();

        public ExportOptionsDialog(IReadOnlyList<DeviceModel> connectedDevices)
        {
            InitializeComponent();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedFields.Clear();
            if (DeviceCheckBox.IsChecked == true) SelectedFields.Add("设备");
            if (HRCheckBox.IsChecked == true) SelectedFields.Add("心率");
            if (RRCheckBox.IsChecked == true) SelectedFields.Add("RR");
            if (SDNNCheckBox.IsChecked == true) SelectedFields.Add("SDNN");
            if (RMSSDCheckBox.IsChecked == true) SelectedFields.Add("RMSSD");
            if (DFAAlpha1CheckBox.IsChecked == true) SelectedFields.Add("DFA α1");
            if (DFAAlpha2CheckBox.IsChecked == true) SelectedFields.Add("DFA α2");
            if (BatteryCheckBox.IsChecked == true) SelectedFields.Add("电量");

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
