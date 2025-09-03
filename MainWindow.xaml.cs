using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Management;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

// 明确指定使用的类型，避免命名冲突
using WinForms = System.Windows.Forms;
using DrawingColor = System.Drawing.Color;
using MediaColor = System.Windows.Media.Color;
using WPFMessageBox = System.Windows.MessageBox;
using WPFImage = System.Windows.Controls.Image;
using IOPath = System.IO.Path;

namespace WpfApp1
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Dictionary<string, GameInfo> gameDatabase = new Dictionary<string, GameInfo>();
        private string dbFile = "game_database.json";
        private string configFile = "game_name.txt";
        private string hotkeyFile = "hotkeys.json";
        private Dictionary<string, string> hotkeys = new Dictionary<string, string>() { { "pause", "Ctrl+Alt+P" }, { "resume", "Ctrl+Alt+R" } };
        
        // 用于全局热键的API
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        
        // 热键ID
        private const int HOTKEY_ID_PAUSE = 1;
        private const int HOTKEY_ID_RESUME = 2;
        
        // 修饰键
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;
        private const uint MOD_NOREPEAT = 0x4000;

        public MainWindow()
        {
            InitializeComponent();
            LoadDatabase();
            LoadConfig();
            LoadHotkeys();
            RegisterHotKeys();
            
            // 添加消息钩子用于处理热键
            ComponentDispatcher.ThreadFilterMessage += ComponentDispatcherThreadFilterMessage;
            
            // 窗口关闭时注销热键
            Closed += (s, e) => {
                UnregisterHotKey(new System.Windows.Interop.WindowInteropHelper(this).Handle, HOTKEY_ID_PAUSE);
                UnregisterHotKey(new System.Windows.Interop.WindowInteropHelper(this).Handle, HOTKEY_ID_RESUME);
            };
        }

        #region 数据库操作

        private void LoadDatabase()
        {
            try
            {
                if (File.Exists(dbFile))
                {
                    string json = File.ReadAllText(dbFile);
                    gameDatabase = JsonSerializer.Deserialize<Dictionary<string, GameInfo>>(json) ?? new Dictionary<string, GameInfo>();
                }
            }
            catch (Exception ex)
            {
                WPFMessageBox.Show($"加载数据库失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                gameDatabase = new Dictionary<string, GameInfo>();
            }
        }

        private void SaveDatabase()
        {
            try
            {
                string json = JsonSerializer.Serialize(gameDatabase, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(dbFile, json);
            }
            catch (Exception ex)
            {
                WPFMessageBox.Show($"保存数据库失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateDatabase(string gameName, string? exePath = null, BitmapSource? iconImage = null)
        {
            if (!gameDatabase.ContainsKey(gameName))
            {
                gameDatabase[gameName] = new GameInfo();
            }

            if (exePath != null)
            {
                gameDatabase[gameName].ExePath = exePath;
            }

            if (iconImage != null)
            {
                gameDatabase[gameName].IconBase64 = ConvertBitmapSourceToBase64(iconImage);
            }

            SaveDatabase();
        }

        private string ConvertBitmapSourceToBase64(BitmapSource bitmapSource)
        {
            try
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmapSource));

                using (var stream = new MemoryStream())
                {
                    encoder.Save(stream);
                    return Convert.ToBase64String(stream.ToArray());
                }
            }
            catch (Exception ex)
            {
                WPFMessageBox.Show($"图像转base64失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return string.Empty;
            }
        }

        private BitmapSource? ConvertBase64ToBitmapSource(string base64String)
        {
            try
            {
                byte[] bytes = Convert.FromBase64String(base64String);
                using (var stream = new MemoryStream(bytes))
                {
                    var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.Default);
                    return decoder.Frames[0];
                }
            }
            catch (Exception ex)
            {
                WPFMessageBox.Show($"base64转图像失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        #endregion

        #region 配置文件操作

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(configFile))
                {
                    string[] gameNames = File.ReadAllLines(configFile);
                    
                    // 清空列表
                    ProgramListBox.Items.Clear();
                    
                    // 添加程序到列表
                    foreach (string gameName in gameNames)
                    {
                        if (!string.IsNullOrWhiteSpace(gameName))
                        {
                            ProgramListBox.Items.Add(gameName);
                        }
                    }
                    
                    // 选择第一个程序
                    if (ProgramListBox.Items.Count > 0)
                    {
                        ProgramListBox.SelectedIndex = 0;
                    }
                    
                    InfoLabel.Text = $"Game loaded: {ProgramListBox.Items.Count}";
                }
                else
                {
                    InfoLabel.Text = "No game_name.txt found";
                }
            }
            catch (Exception ex)
            {
                WPFMessageBox.Show($"加载配置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region 进程操作

        private string FindExePath(string exeName)
        {
            try
            {
                // 首先尝试从运行中的进程获取路径
                Process[] processes = Process.GetProcessesByName(IOPath.GetFileNameWithoutExtension(exeName));
                if (processes.Length > 0)
                {
                    return processes[0].MainModule.FileName;
                }
                
                // 如果进程未运行，尝试从数据库获取路径
                if (gameDatabase.ContainsKey(exeName) && !string.IsNullOrEmpty(gameDatabase[exeName].ExePath))
                {
                    return gameDatabase[exeName].ExePath;
                }
                
                return null;
            }
            catch (Exception ex)
            {
                WPFMessageBox.Show($"查找进程时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        private BitmapSource ExtractIconFromExe(string exePath)
        {
            try
            {
                using (System.Drawing.Icon icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath))
                {
                    if (icon != null)
                    {
                        return System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                            icon.Handle,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                WPFMessageBox.Show($"提取图标时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        private BitmapSource CreatePlaceholderIcon(string exeName)
        {
            try
            {
                // 创建一个32x32的位图
                var drawingBitmap = new System.Drawing.Bitmap(32, 32);
                using (var g = System.Drawing.Graphics.FromImage(drawingBitmap))
                {
                    g.Clear(System.Drawing.Color.Transparent);
                    
                    // 根据程序名称选择不同的图标样式
                    string exeNameLower = exeName.ToLower();
                    System.Drawing.Color fillColor;
                    
                    if (exeNameLower.Contains("chrome"))
                    {
                        fillColor = System.Drawing.Color.FromArgb(220, 20, 60); // 红色
                    }
                    else if (exeNameLower.Contains("game") || exeNameLower.Contains("devil"))
                    {
                        fillColor = System.Drawing.Color.FromArgb(34, 139, 34); // 绿色
                    }
                    else if (exeNameLower.Contains("notepad"))
                    {
                        fillColor = System.Drawing.Color.White;
                    }
                    else if (exeNameLower.Contains("calc"))
                    {
                        fillColor = System.Drawing.Color.FromArgb(255, 165, 0); // 橙色
                    }
                    else
                    {
                        fillColor = System.Drawing.Color.FromArgb(70, 130, 180); // 蓝色
                    }
                    
                    // 绘制圆形图标
                    using (var brush = new System.Drawing.SolidBrush(fillColor))
                    using (var pen = new System.Drawing.Pen(System.Drawing.Color.White, 2))
                    {
                        g.FillEllipse(brush, 2, 2, 28, 28);
                        g.DrawEllipse(pen, 2, 2, 28, 28);
                    }
                }
                
                // 转换为BitmapSource
                var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    drawingBitmap.GetHbitmap(),
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                
                drawingBitmap.Dispose();
                return bitmapSource;
            }
            catch (Exception ex)
            {
                WPFMessageBox.Show($"创建占位符图标失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        private void UpdateIcon(string exeName)
        {
            try
            {
                // 清除之前的图标
                IconPanel.Children.Clear();
                
                // 1. 查找exe路径
                string exePath = FindExePath(exeName);
                
                if (exePath != null)
                {
                    // 2. 提取真实图标
                    BitmapSource iconImage = ExtractIconFromExe(exePath);
                    if (iconImage != null)
                    {
                        // 显示真实图标
                        WPFImage iconControl = new WPFImage
                        {
                            Source = iconImage,
                            Width = 32,
                            Height = 32,
                            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                        };
                        IconPanel.Children.Add(iconControl);
                        
                        // 添加程序名称标签
                        TextBlock nameLabel = new TextBlock
                        {
                            Text = exeName,
                            Foreground = new SolidColorBrush(MediaColor.FromRgb(255, 255, 255)),
                            FontSize = 10,
                            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                            Margin = new Thickness(0, 2, 0, 0)
                        };
                        IconPanel.Children.Add(nameLabel);
                        
                        // 更新状态显示
                        StatusLabel.Text = $"✓ {exeName} (运行中)";
                        StatusLabel.Foreground = new SolidColorBrush(MediaColor.FromRgb(0, 255, 0));
                        
                        // 更新数据库
                        UpdateDatabase(exeName, exePath, iconImage);
                        return;
                    }
                }
                
                // 3. 尝试从数据库加载图标
                if (gameDatabase.ContainsKey(exeName) && !string.IsNullOrEmpty(gameDatabase[exeName].IconBase64))
                {
                    BitmapSource dbIcon = ConvertBase64ToBitmapSource(gameDatabase[exeName].IconBase64);
                    if (dbIcon != null)
                    {
                        // 显示数据库中的图标
                        WPFImage iconControl = new WPFImage
                        {
                            Source = dbIcon,
                            Width = 32,
                            Height = 32,
                            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                        };
                        IconPanel.Children.Add(iconControl);
                        
                        // 添加程序名称标签
                        TextBlock nameLabel = new TextBlock
                        {
                            Text = $"{exeName} (数据库)",
                            Foreground = new SolidColorBrush(MediaColor.FromRgb(120, 120, 120)),
                            FontSize = 10,
                            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                            Margin = new Thickness(0, 2, 0, 0)
                        };
                        IconPanel.Children.Add(nameLabel);
                        
                        // 更新状态显示
                        StatusLabel.Text = $"○ {exeName} (未运行)";
                        StatusLabel.Foreground = new SolidColorBrush(MediaColor.FromRgb(120, 120, 120));
                        return;
                    }
                }
                
                // 4. 如果没有找到真实图标，使用占位符
                BitmapSource placeholderImg = CreatePlaceholderIcon(exeName);
                if (placeholderImg != null)
                {
                    // 显示占位符图标
                    WPFImage iconControl = new WPFImage
                    {
                        Source = placeholderImg,
                        Width = 32,
                        Height = 32,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                    };
                    IconPanel.Children.Add(iconControl);
                    
                    // 添加程序名称标签
                    TextBlock nameLabel = new TextBlock
                    {
                        Text = $"{exeName} (占位符)",
                        Foreground = new SolidColorBrush(MediaColor.FromRgb(120, 120, 120)),
                        FontSize = 10,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        Margin = new Thickness(0, 2, 0, 0)
                    };
                    IconPanel.Children.Add(nameLabel);
                    
                    // 更新状态显示
                    StatusLabel.Text = $"○ {exeName} (未运行)";
                    StatusLabel.Foreground = new SolidColorBrush(MediaColor.FromRgb(120, 120, 120));
                }
                else
                {
                    // 5. 如果连占位符都创建失败，显示文本
                    TextBlock defaultLabel = new TextBlock
                    {
                        Text = $"未找到 {exeName} 的图标",
                        Foreground = new SolidColorBrush(MediaColor.FromRgb(120, 120, 120)),
                        FontSize = 10,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        VerticalAlignment = System.Windows.VerticalAlignment.Center
                    };
                    IconPanel.Children.Add(defaultLabel);
                    
                    // 更新状态显示
                    StatusLabel.Text = $"✗ {exeName} (图标加载失败)";
                    StatusLabel.Foreground = new SolidColorBrush(MediaColor.FromRgb(255, 0, 0));
                }
            }
            catch (Exception ex)
            {
                // 显示错误信息
                IconPanel.Children.Clear();
                TextBlock errorLabel = new TextBlock
                {
                    Text = "图标加载失败",
                    Foreground = new SolidColorBrush(MediaColor.FromRgb(255, 0, 0)),
                    FontSize = 10,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };
                IconPanel.Children.Add(errorLabel);
                
                // 更新状态显示
                StatusLabel.Text = $"✗ {exeName} (错误)";
                StatusLabel.Foreground = new SolidColorBrush(MediaColor.FromRgb(255, 0, 0));
            }
        }

        private string GetSelectedGame()
        {
            return ProgramListBox.SelectedItem?.ToString();
        }

        private void RunProcess(string command)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", $"/c {command}")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                WPFMessageBox.Show($"执行命令失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region 事件处理

        private void ProgramListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string selectedGame = GetSelectedGame();
            if (selectedGame != null)
            {
                UpdateIcon(selectedGame);
            }
        }

        private void PauseGame_Click(object sender, RoutedEventArgs e)
        {
            string gameName = GetSelectedGame();
            if (gameName != null)
            {
                StatusLabel.Text = $"⏸ {gameName} Paused";
                StatusLabel.Foreground = new SolidColorBrush(MediaColor.FromRgb(255, 0, 255));
                
                // 检查pssuspend.exe是否存在
                string pssuspendPath = "PsSuspend.exe";
                if (!File.Exists(pssuspendPath) && !File.Exists(IOPath.Combine(Environment.CurrentDirectory, pssuspendPath)))
                {
                    WPFMessageBox.Show("找不到PsSuspend.exe工具，请确保它在程序目录或系统路径中。\n\n您可以从Sysinternals Suite下载此工具。", 
                        "缺少必要工具", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                // 尝试最小化目标窗口
                try
                {
                    Process[] processes = Process.GetProcessesByName(IOPath.GetFileNameWithoutExtension(gameName));
                    if (processes.Length > 0)
                    {
                        MinimizeProcessWindow(processes[0]);
                    }
                }
                catch (Exception ex)
                {
                    // 最小化失败不影响暂停功能
                    Console.WriteLine($"最小化窗口失败: {ex.Message}");
                }
                
                // 暂停游戏进程
                RunProcess($"PsSuspend \"{gameName}\"");
            }
            else
            {
                StatusLabel.Text = "请先选择一个程序";
                StatusLabel.Foreground = new SolidColorBrush(MediaColor.FromRgb(255, 0, 0));
            }
        }
        
        private void MinimizeProcessWindow(Process process)
        {
            if (process != null && !process.HasExited && process.MainWindowHandle != IntPtr.Zero)
            {
                ShowWindow(process.MainWindowHandle, SW_MINIMIZE);
            }
        }
        
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        
        private const int SW_MINIMIZE = 6;

        private void ResumeGame_Click(object sender, RoutedEventArgs e)
        {
            string gameName = GetSelectedGame();
            if (gameName != null)
            {
                StatusLabel.Text = $"▶ {gameName} Resumed";
                StatusLabel.Foreground = new SolidColorBrush(MediaColor.FromRgb(0, 255, 0));
                
                // 恢复游戏进程
                RunProcess($"PsSuspend -r \"{gameName}\"");
            }
            else
            {
                StatusLabel.Text = "请先选择一个程序";
                StatusLabel.Foreground = new SolidColorBrush(MediaColor.FromRgb(255, 0, 0));
            }
        }
        
        #region 热键处理
        
        private void LoadHotkeys()
        {
            try
            {
                if (File.Exists(hotkeyFile))
                {
                    string json = File.ReadAllText(hotkeyFile);
                    hotkeys = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? hotkeys;
                }
                else
                {
                    // 保存默认热键配置
                    SaveHotkeys();
                }
                
                // 更新菜单项显示当前热键
                UpdateHotkeyMenuItems();
            }
            catch (Exception ex)
            {
                WPFMessageBox.Show($"加载热键配置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void UpdateHotkeyMenuItems()
        {
            // 更新菜单项显示当前热键
            if (PauseHotkeyMenuItem != null)
            {
                PauseHotkeyMenuItem.Header = $"Pause ({hotkeys["pause"]})"; 
            }
            
            if (ResumeHotkeyMenuItem != null)
            {
                ResumeHotkeyMenuItem.Header = $"Resume ({hotkeys["resume"]})"; 
            }
        }
        
        private void ConfigHotkeys_Click(object sender, RoutedEventArgs e)
        {
            // 注销当前热键
            IntPtr handle = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(handle, HOTKEY_ID_PAUSE);
            UnregisterHotKey(handle, HOTKEY_ID_RESUME);
            
            // 打开热键配置窗口
            HotkeyConfigWindow configWindow = new HotkeyConfigWindow(hotkeys);
            configWindow.Owner = this;
            bool? result = configWindow.ShowDialog();
            
            if (result == true)
            {
                // 保存新的热键配置
                SaveHotkeys();
                
                // 更新菜单项显示当前热键
                UpdateHotkeyMenuItems();
                
                // 重新注册热键
                RegisterHotKeys();
            }
            else
            {
                // 重新注册原来的热键
                RegisterHotKeys();
            }
        }
        
        private void SaveHotkeys()
        {
            try
            {
                string json = JsonSerializer.Serialize(hotkeys, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(hotkeyFile, json);
            }
            catch (Exception ex)
            {
                WPFMessageBox.Show($"保存热键配置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void RegisterHotKeys()
        {
            try
            {
                IntPtr handle = new WindowInteropHelper(this).Handle;
                
                // 解析暂停热键
                if (ParseHotkey(hotkeys["pause"], out uint pauseModifiers, out uint pauseKey))
                {
                    RegisterHotKey(handle, HOTKEY_ID_PAUSE, pauseModifiers, pauseKey);
                }
                
                // 解析恢复热键
                if (ParseHotkey(hotkeys["resume"], out uint resumeModifiers, out uint resumeKey))
                {
                    RegisterHotKey(handle, HOTKEY_ID_RESUME, resumeModifiers, resumeKey);
                }
            }
            catch (Exception ex)
            {
                WPFMessageBox.Show($"注册热键失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private bool ParseHotkey(string hotkeyString, out uint modifiers, out uint key)
        {
            modifiers = 0;
            key = 0;
            
            if (string.IsNullOrEmpty(hotkeyString))
                return false;
                
            string[] parts = hotkeyString.Split('+');
            if (parts.Length < 2)
                return false;
                
            // 解析修饰键
            for (int i = 0; i < parts.Length - 1; i++)
            {
                string mod = parts[i].Trim().ToLower();
                switch (mod)
                {
                    case "ctrl":
                    case "control":
                        modifiers |= MOD_CONTROL;
                        break;
                    case "alt":
                        modifiers |= MOD_ALT;
                        break;
                    case "shift":
                        modifiers |= MOD_SHIFT;
                        break;
                    case "win":
                    case "windows":
                        modifiers |= MOD_WIN;
                        break;
                }
            }
            
            // 解析键值
            string keyStr = parts[parts.Length - 1].Trim().ToUpper();
            if (keyStr.Length == 1 && keyStr[0] >= 'A' && keyStr[0] <= 'Z')
            {
                // 字母键
                key = (uint)keyStr[0];
                return true;
            }
            else if (keyStr.Length == 1 && keyStr[0] >= '0' && keyStr[0] <= '9')
            {
                // 数字键
                key = (uint)keyStr[0];
                return true;
            }
            else
            {
                // 功能键等
                switch (keyStr)
                {
                    case "F1": key = 0x70; return true;
                    case "F2": key = 0x71; return true;
                    case "F3": key = 0x72; return true;
                    case "F4": key = 0x73; return true;
                    case "F5": key = 0x74; return true;
                    case "F6": key = 0x75; return true;
                    case "F7": key = 0x76; return true;
                    case "F8": key = 0x77; return true;
                    case "F9": key = 0x78; return true;
                    case "F10": key = 0x79; return true;
                    case "F11": key = 0x7A; return true;
                    case "F12": key = 0x7B; return true;
                    case "P": key = 0x50; return true;
                    case "R": key = 0x52; return true;
                }
            }
            
            return false;
        }
        
        private void ComponentDispatcherThreadFilterMessage(ref MSG msg, ref bool handled)
        {
            if (msg.message == 0x0312) // WM_HOTKEY
            {
                int id = msg.wParam.ToInt32();
                
                if (id == HOTKEY_ID_PAUSE)
                {
                    // 处理暂停热键
                    Dispatcher.Invoke(() => {
                        if (ProgramListBox.SelectedItem != null)
                        {
                            PauseGame_Click(null, null);
                        }
                    });
                    handled = true;
                }
                else if (id == HOTKEY_ID_RESUME)
                {
                    // 处理恢复热键
                    Dispatcher.Invoke(() => {
                        if (ProgramListBox.SelectedItem != null)
                        {
                            ResumeGame_Click(null, null);
                        }
                    });
                    handled = true;
                }
            }
        }
        
        #endregion

        private void KillGame_Click(object sender, RoutedEventArgs e)
        {
            string gameName = GetSelectedGame();
            if (gameName != null)
            {
                StatusLabel.Text = $"⏹ {gameName} Killed";
                StatusLabel.Foreground = new SolidColorBrush(MediaColor.FromRgb(255, 0, 0));
                
                // 结束游戏进程
                RunProcess($"taskkill /IM \"{gameName}\" /F");
            }
            else
            {
                StatusLabel.Text = "请先选择一个程序";
                StatusLabel.Foreground = new SolidColorBrush(MediaColor.FromRgb(255, 0, 0));
            }
        }

        private void LaunchGame_Click(object sender, RoutedEventArgs e)
        {
            string gameName = GetSelectedGame();
            if (gameName != null)
            {
                string exePath = null;
                if (gameDatabase.ContainsKey(gameName))
                {
                    exePath = gameDatabase[gameName].ExePath;
                }
                
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    StatusLabel.Text = $"🚀 Launching {gameName}...";
                    StatusLabel.Foreground = new SolidColorBrush(MediaColor.FromRgb(0, 123, 255));
                    
                    // 启动游戏
                    Process.Start(exePath);
                }
                else
                {
                    // 尝试直接运行程序名
                    StatusLabel.Text = $"🚀 Launching {gameName}...";
                    StatusLabel.Foreground = new SolidColorBrush(MediaColor.FromRgb(0, 123, 255));
                    
                    try
                    {
                        Process.Start(gameName);
                    }
                    catch
                    {
                        WPFMessageBox.Show($"无法启动 {gameName}，请确保程序名称正确或在数据库中设置正确的路径。", "启动失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                
                // 更新图标以反映启动状态
                Task.Delay(1000).ContinueWith(_ => Dispatcher.Invoke(() => UpdateIcon(gameName)));
            }
            else
            {
                StatusLabel.Text = "请先选择一个程序";
                StatusLabel.Foreground = new SolidColorBrush(MediaColor.FromRgb(255, 0, 0));
            }
        }

        private void ReloadConfig_Click(object sender, RoutedEventArgs e)
        {
            LoadConfig();
        }

        private void AddProgram_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 使用深色对话框获取程序名
                var dialog = new AddProgramWindow { Owner = this };
                bool? result = dialog.ShowDialog();
                if (result != true)
                {
                    return;
                }
                string exeName = dialog.EnteredExeName;

                // 确保配置文件存在
                if (!File.Exists(configFile))
                {
                    File.WriteAllText(configFile, "");
                }

                // 读取现有列表，避免重复
                var lines = new List<string>(File.ReadAllLines(configFile));
                bool exists = false;
                foreach (var line in lines)
                {
                    if (string.Equals(line.Trim(), exeName, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    lines.Add(exeName);
                    File.WriteAllLines(configFile, lines);
                }

                // 重新加载配置并选中新项
                LoadConfig();
                for (int i = 0; i < ProgramListBox.Items.Count; i++)
                {
                    if (string.Equals(ProgramListBox.Items[i]?.ToString(), exeName, StringComparison.OrdinalIgnoreCase))
                    {
                        ProgramListBox.SelectedIndex = i;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                WPFMessageBox.Show($"添加程序失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EditConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 如果文件不存在，创建一个空文件
                if (!File.Exists(configFile))
                {
                    File.WriteAllText(configFile, "");
                }
                
                // 使用记事本打开配置文件
                Process.Start("notepad.exe", configFile);
            }
            catch (Exception ex)
            {
                WPFMessageBox.Show($"打开配置文件失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowDatabase_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 如果文件不存在，创建一个空的数据库文件
                if (!File.Exists(dbFile))
                {
                    SaveDatabase();
                }
                
                // 使用记事本打开数据库文件
                Process.Start("notepad.exe", dbFile);
            }
            catch (Exception ex)
            {
                WPFMessageBox.Show($"打开数据库文件失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenTaskManager_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("taskmgr.exe");
        }

        private void OpenGitHub_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/cornradio/pausemygame",
                UseShellExecute = true
            });
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        #endregion
    }

    public class GameInfo
    {
        public string ExePath { get; set; }
        public string IconBase64 { get; set; }
    }
}