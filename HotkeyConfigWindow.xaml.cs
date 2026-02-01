using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
// 明确指定使用的类型，避免命名冲突
using WPFKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WPFTextBox = System.Windows.Controls.TextBox;
using WPFButton = System.Windows.Controls.Button;
using System.Windows.Interop;

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
            if (tempHotkeys.ContainsKey("pause")) PauseHotkeyTextBox.Text = tempHotkeys["pause"];
            if (tempHotkeys.ContainsKey("resume")) ResumeHotkeyTextBox.Text = tempHotkeys["resume"];
            if (tempHotkeys.ContainsKey("toggle")) ToggleHotkeyTextBox.Text = tempHotkeys["toggle"];
            
            if (tempHotkeys.ContainsKey("minimizeOnPause"))
            {
                MinimizeCheckBox.IsChecked = tempHotkeys["minimizeOnPause"] == "true";
            }
        }
        
        private void ClearHotkey_Click(object sender, RoutedEventArgs e)
        {
            WPFButton btn = sender as WPFButton;
            string hotkeyType = btn.Tag.ToString();
            
            // 根据 Tag 清除对应的 TextBox 和配置
            if (hotkeyType == "pause") PauseHotkeyTextBox.Text = "None";
            else if (hotkeyType == "resume") ResumeHotkeyTextBox.Text = "None";
            else if (hotkeyType == "toggle") ToggleHotkeyTextBox.Text = "None";
            
            tempHotkeys[hotkeyType] = "";
        }

        private void HotkeyTextBox_KeyDown(object sender, WPFKeyEventArgs e)
        {
            e.Handled = true;

            WPFTextBox textBox = sender as WPFTextBox;
            string hotkeyType = textBox.Tag.ToString();

            // 获取主键
            Key currentKey = e.Key;
            if (currentKey == Key.System)
            {
                currentKey = e.SystemKey;
            }

            // 允许使用 Backspace 或 Delete 清除热键
            if (currentKey == Key.Back || currentKey == Key.Delete)
            {
                textBox.Text = "None";
                tempHotkeys[hotkeyType] = "";
                return;
            }

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

            // 忽略修饰键本身作为主键
            if (currentKey == Key.LeftCtrl || currentKey == Key.RightCtrl ||
                currentKey == Key.LeftAlt || currentKey == Key.RightAlt ||
                currentKey == Key.LeftShift || currentKey == Key.RightShift ||
                currentKey == Key.LWin || currentKey == Key.RWin)
                return;

            string keyString = currentKey.ToString();
            
            // 组合热键字符串
            string hotkeyString = modifiers.Count > 0 
                ? string.Join("+", modifiers) + "+" + keyString 
                : keyString;

            // 检查热键是否已被其他功能使用
            foreach (var entry in tempHotkeys)
            {
                if (entry.Key != hotkeyType && !string.IsNullOrEmpty(entry.Value) && entry.Value.Equals(hotkeyString, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            textBox.Text = hotkeyString;

            // 更新临时热键配置
            tempHotkeys[hotkeyType] = hotkeyString;
        }
        
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // 更新临时状态中的复选框值
            tempHotkeys["minimizeOnPause"] = MinimizeCheckBox.IsChecked == true ? "true" : "false";

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