using System.Windows;

namespace HeartRateMonitor
{
    public partial class MessageDialog : Window
    {
        public MessageDialog(string message)
        {
            InitializeComponent();
            MessageText.Text = message;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}