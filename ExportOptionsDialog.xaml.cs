using System.Collections.Generic;
using System.Windows;
using HeartRateMonitor.Models;
using HeartRateMonitor.Resources;

namespace HeartRateMonitor
{
    public partial class ExportOptionsDialog : Window
    {
        public string ExportType { get; private set; } = Strings.ExportTypeHistory;
        public List<string> SelectedFields { get; private set; } = new();

        public ExportOptionsDialog(IReadOnlyList<DeviceModel> connectedDevices)
        {
            InitializeComponent();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedFields.Clear();
            if (DeviceCheckBox.IsChecked == true) SelectedFields.Add(Strings.FieldDevice);
            if (HRCheckBox.IsChecked == true) SelectedFields.Add(Strings.FieldHeartRate);
            if (RRCheckBox.IsChecked == true) SelectedFields.Add(Strings.FieldRR);
            if (DFAAlpha1CheckBox.IsChecked == true) SelectedFields.Add(Strings.FieldDfaAlpha1);

            ExportType = HistoryRadio.IsChecked == true ? Strings.ExportTypeHistory : Strings.ExportTypeSnapshot;

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
