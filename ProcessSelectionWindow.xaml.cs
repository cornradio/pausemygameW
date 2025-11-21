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
    }

    public class ProcessItem
    {
        public string Name { get; set; }
        public string MemoryUsage { get; set; }
    }
}
