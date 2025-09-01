using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
// 明确指定使用的类型，避免命名冲突
using WPFKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WPFTextBox = System.Windows.Controls.TextBox;

namespace WpfApp1
{
    public partial class HotkeyConfigWindow : Window
    {
        private Dictionary<string, string> hotkeys;
        private Dictionary<string, string> tempHotkeys = new Dictionary<string, string>();
        
        public HotkeyConfigWindow(Dictionary<string, string> currentHotkeys)
        {
            InitializeComponent();
            
            // 复制当前热键配置
            hotkeys = currentHotkeys;
            foreach (var key in hotkeys.Keys)
            {
                tempHotkeys[key] = hotkeys[key];
            }
            
            // 显示当前热键配置
            PauseHotkeyTextBox.Text = tempHotkeys["pause"];
            ResumeHotkeyTextBox.Text = tempHotkeys["resume"];
        }
        
        private void HotkeyTextBox_KeyDown(object sender, WPFKeyEventArgs e)
        {
            e.Handled = true;
            
            WPFTextBox textBox = sender as WPFTextBox;
            string hotkeyType = textBox.Tag.ToString();
            
            // 获取修饰键
            List<string> modifiers = new List<string>();
            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
                modifiers.Add("Ctrl");
            if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt))
                modifiers.Add("Alt");
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
                modifiers.Add("Shift");
            if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin))
                modifiers.Add("Win");
            
            // 确保至少有一个修饰键
            if (modifiers.Count == 0)
                return;
            
            // 获取主键
            string keyString = e.Key.ToString();
            
            // 忽略修饰键本身
            if (keyString == "LeftCtrl" || keyString == "RightCtrl" ||
                keyString == "LeftAlt" || keyString == "RightAlt" ||
                keyString == "LeftShift" || keyString == "RightShift" ||
                keyString == "LWin" || keyString == "RWin")
                return;
            
            // 组合热键字符串
            string hotkeyString = string.Join("+", modifiers) + "+" + keyString;
            textBox.Text = hotkeyString;
            
            // 更新临时热键配置
            tempHotkeys[hotkeyType] = hotkeyString;
        }
        
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // 将临时热键配置应用到实际配置
            foreach (var key in tempHotkeys.Keys)
            {
                hotkeys[key] = tempHotkeys[key];
            }
            
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