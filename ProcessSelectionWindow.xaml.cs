using System;
using System.Collections.Generic;
using System.Windows;

namespace WpfApp1
{
    public partial class ProcessSelectionWindow : Window
    {
        public string SelectedProcessName { get; private set; }

        public ProcessSelectionWindow(List<ProcessItem> processes)
        {
            InitializeComponent();
            ProcessListBox.ItemsSource = processes;
        }

        public void RefreshList()
        {
            ProcessListBox.Items.Refresh();
        }

        private void Select_Click(object sender, RoutedEventArgs e)
        {
            if (ProcessListBox.SelectedItem is ProcessItem selected)
            {
                SelectedProcessName = selected.Name;
                DialogResult = true;
                Close();
            }
            else
            {
                System.Windows.MessageBox.Show("Please select a process.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ProcessListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            Select_Click(sender, e);
        }
    }

    public class ProcessItem
    {
        public string Name { get; set; }
        public string MemoryUsage { get; set; }
        public System.Windows.Media.ImageSource Icon { get; set; }
    }
}
