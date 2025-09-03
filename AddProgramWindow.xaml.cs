using System;
using System.Windows;

namespace WpfApp1
{
    public partial class AddProgramWindow : Window
    {
        public string EnteredExeName { get; private set; }

        public AddProgramWindow()
        {
            InitializeComponent();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            var text = ExeNameTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                DialogResult = false;
                return;
            }

            if (!text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                text += ".exe";
            }

            EnteredExeName = text;
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}


