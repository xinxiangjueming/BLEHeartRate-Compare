using System.Windows;

namespace HeartRateMonitor
{
    public partial class ConfirmDialog : Window
    {
        public ConfirmDialog(string message)
        {
            InitializeComponent();
            MessageText.Text = message;
        }

        private void YesButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void NoButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}