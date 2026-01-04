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
using System.Media;

// 明确指定使用的类型，避免命名冲突
using WinForms = System.Windows.Forms;
using DrawingColor = System.Drawing.Color;
using MediaColor = System.Windows.Media.Color;
using WPFMessageBox = System.Windows.MessageBox;
using WPFImage = System.Windows.Controls.Image;
using IOPath = System.IO.Path;
using Application = System.Windows.Application;

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

        // 托盘图标相关
        private WinForms.NotifyIcon notifyIcon;
        private bool isExiting = false;
        
        // 拖放相关
        private object draggedItem = null;
        private System.Windows.Point dragStartPoint;

        private DispatcherTimer statusTimer;

        public MainWindow()
        {
            InitializeComponent();
            LoadDatabase();
            LoadConfig();
            LoadHotkeys();
            RegisterHotKeys();
            
            // 添加消息钩子用于处理热键
            ComponentDispatcher.ThreadFilterMessage += ComponentDispatcherThreadFilterMessage;
            
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;

            InitializeTrayIcon();
            InitializeStatusTimer();
        }

        private void InitializeStatusTimer()
        {
            statusTimer = new DispatcherTimer();
            statusTimer.Interval = TimeSpan.FromSeconds(1);
            statusTimer.Tick += (s, e) => UpdateButtonStates();
            statusTimer.Start();
        }

        private void InitializeTrayIcon()
        {
            notifyIcon = new WinForms.NotifyIcon();
            notifyIcon.Text = "Pause My Game";
            
            string iconPath = IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "Babasse-Old-School-Time-Machine.ico");
            if (File.Exists(iconPath))
            {
                notifyIcon.Icon = new System.Drawing.Icon(iconPath);
            }
            else
            {
                notifyIcon.Icon = System.Drawing.SystemIcons.Application;
            }

            notifyIcon.Visible = true;

            notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == WinForms.MouseButtons.Left)
                {
                    RestoreWindow();
                }
            };

            var contextMenu = new WinForms.ContextMenuStrip();
            
            var restoreItem = new WinForms.ToolStripMenuItem("恢复 (Restore)");
            restoreItem.Click += (s, e) => RestoreWindow();
            contextMenu.Items.Add(restoreItem);

            var exitItem = new WinForms.ToolStripMenuItem("退出 (Exit)");
            exitItem.Click += (s, e) => RealExit();
            contextMenu.Items.Add(exitItem);

            notifyIcon.ContextMenuStrip = contextMenu;
        }

        private void RestoreWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void RealExit()
        {
            isExiting = true;
            if (notifyIcon != null)
            {
                notifyIcon.Visible = false;
                notifyIcon.Dispose();
            }
            Application.Current.Shutdown();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!isExiting)
            {
                e.Cancel = true;
                Hide();
                notifyIcon.ShowBalloonTip(1000, "Pause My Game", "程序已最小化到托盘，点击图标恢复。", WinForms.ToolTipIcon.Info);
            }
            else
            {
                base.OnClosing(e);
            }
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

        private void SaveConfig()
        {
            try
            {
                var gameNames = new List<string>();
                foreach (var item in ProgramListBox.Items)
                {
                    if (item != null)
                    {
                        string gameName = item.ToString();
                        if (!string.IsNullOrWhiteSpace(gameName))
                        {
                            gameNames.Add(gameName);
                        }
                    }
                }
                
                File.WriteAllLines(configFile, gameNames);
                InfoLabel.Text = $"Game loaded: {ProgramListBox.Items.Count}";
            }
            catch (Exception ex)
            {
                WPFMessageBox.Show($"保存配置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region 进程操作

        private string FindExePath(string exeName)
        {
            try
            {
                // 更新状态显示
                StatusLabel.Text = $"程序未运行";
                StatusLabel.Foreground = new SolidColorBrush(MediaColor.FromRgb(120, 120, 120));
                // 首先尝试从运行中的进程获取路径
                Process[] processes = Process.GetProcessesByName(IOPath.GetFileNameWithoutExtension(exeName));
                if (processes.Length > 0)
                {
                                            
                    // 更新状态显示
                    StatusLabel.Text = $"✓ 程序运行中";
                    StatusLabel.Foreground = new SolidColorBrush(MediaColor.FromRgb(0, 255, 0));
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
                // 跳过报错，有的游戏是有加密措施的禁止读取图标icon
                // 在运行状态中提示-无法获取游戏允许状态，禁止读取
                StatusLabel.Text = $"无法获取游戏允许状态，禁止读取";
                StatusLabel.Foreground = new SolidColorBrush(MediaColor.FromRgb(255, 0, 0));

                // WPFMessageBox.Show($"查找进程时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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
                //不要烦人的报错。
                //WPFMessageBox.Show($"提取图标时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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



        #endregion

        #region 事件处理

        private void ProgramListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string selectedGame = GetSelectedGame();
            if (selectedGame != null)
            {
                UpdateIcon(selectedGame);
                UpdateButtonStates();
            }
        }

        private void ProgramListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            dragStartPoint = e.GetPosition(null);
            draggedItem = null;
            
            var item = GetItemFromPoint(ProgramListBox, e.GetPosition(ProgramListBox));
            if (item != null)
            {
                draggedItem = item;
            }
        }

        private void ProgramListBox_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && draggedItem != null)
            {
                System.Windows.Point currentPoint = e.GetPosition(null);
                Vector diff = dragStartPoint - currentPoint;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    // 传递字符串数据以便在Drop中获取
                    string itemData = draggedItem.ToString();
                    DragDrop.DoDragDrop(ProgramListBox, itemData, System.Windows.DragDropEffects.Move);
                }
            }
        }

        private void ProgramListBox_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            e.Effects = System.Windows.DragDropEffects.Move;
            e.Handled = true;
        }

        private void ProgramListBox_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(string)))
            {
                draggedItem = null;
                return;
            }

            var draggedItemData = e.Data.GetData(typeof(string)) as string;
            if (string.IsNullOrEmpty(draggedItemData))
            {
                draggedItem = null;
                return;
            }

            var targetItem = GetItemFromPoint(ProgramListBox, e.GetPosition(ProgramListBox));
            if (targetItem == null)
            {
                draggedItem = null;
                return;
            }

            string targetItemData = targetItem.ToString();
            if (targetItemData == draggedItemData)
            {
                draggedItem = null;
                return;
            }

            int draggedIndex = -1;
            int targetIndex = -1;

            // 查找索引
            for (int i = 0; i < ProgramListBox.Items.Count; i++)
            {
                if (ProgramListBox.Items[i]?.ToString() == draggedItemData)
                    draggedIndex = i;
                if (ProgramListBox.Items[i]?.ToString() == targetItemData)
                    targetIndex = i;
            }

            if (draggedIndex == -1 || targetIndex == -1)
            {
                draggedItem = null;
                return;
            }

            // 保存当前选中的项
            var selectedItem = ProgramListBox.SelectedItem;

            // 移除拖拽的项
            ProgramListBox.Items.RemoveAt(draggedIndex);

            // 计算新的插入位置（如果向下拖，插入到目标位置；如果向上拖，插入到目标位置之前）
            int newIndex = draggedIndex < targetIndex ? targetIndex : targetIndex;
            ProgramListBox.Items.Insert(newIndex, draggedItemData);

            // 恢复选中状态
            ProgramListBox.SelectedItem = selectedItem;

            // 保存配置
            SaveConfig();

            draggedItem = null;
        }

        private object GetItemFromPoint(System.Windows.Controls.ListBox listBox, System.Windows.Point point)
        {
            var item = listBox.InputHitTest(point) as DependencyObject;
            while (item != null && item != listBox)
            {
                if (item is System.Windows.Controls.ListBoxItem)
                {
                    return (item as System.Windows.Controls.ListBoxItem).Content;
                }
                item = VisualTreeHelper.GetParent(item);
            }
            return null;
        }

        private void TogglePause_Click(object sender, RoutedEventArgs e)
        {
            string gameName = GetSelectedGame();
            if (string.IsNullOrEmpty(gameName))
            {
                StatusLabel.Text = "请先选择一个程序";
                StatusLabel.Foreground = new SolidColorBrush(MediaColor.FromRgb(255, 0, 0));
                return;
            }

            if (ProcessLogic.IsProcessSuspended(gameName))
            {
                // 恢复
                PlayButtonSound("resume");
                ProcessLogic.ResumeProcess(gameName);
                StatusLabel.Text = $"▶ {gameName} Resumed";
                StatusLabel.Foreground = new SolidColorBrush(MediaColor.FromRgb(0, 255, 0));
            }
            else
            {
                // 暂停
                PlayButtonSound("pause");
                ProcessLogic.PauseProcess(gameName);
                StatusLabel.Text = $"⏸ {gameName} Paused";
                StatusLabel.Foreground = new SolidColorBrush(MediaColor.FromRgb(255, 0, 255));
            }
            UpdateButtonStates();
        }

        private void ToggleLaunch_Click(object sender, RoutedEventArgs e)
        {
            string gameName = GetSelectedGame();
            if (string.IsNullOrEmpty(gameName))
            {
                StatusLabel.Text = "请先选择一个程序";
                StatusLabel.Foreground = new SolidColorBrush(MediaColor.FromRgb(255, 0, 0));
                return;
            }

            if (ProcessLogic.IsProcessRunning(gameName))
            {
                // 杀死
                PlayButtonSound("kill");
                ProcessLogic.KillProcess(gameName);
                StatusLabel.Text = $"⏹ {gameName} Killed";
                StatusLabel.Foreground = new SolidColorBrush(MediaColor.FromRgb(255, 0, 0));
            }
            else
            {
                // 启动
                PlayButtonSound("launch");
                string exePath = gameDatabase.ContainsKey(gameName) ? gameDatabase[gameName].ExePath : null;
                try
                {
                    ProcessLogic.LaunchProcess(exePath, gameName);
                    StatusLabel.Text = $"🚀 Launching {gameName}...";
                    StatusLabel.Foreground = new SolidColorBrush(MediaColor.FromRgb(0, 123, 255));
                }
                catch (Exception ex)
                {
                    WPFMessageBox.Show(ex.Message, "启动失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            UpdateButtonStates();
        }

        private void UpdateButtonStates()
        {
            string gameName = GetSelectedGame();
            if (string.IsNullOrEmpty(gameName)) return;

            bool isRunning = ProcessLogic.IsProcessRunning(gameName);
            bool isSuspended = ProcessLogic.IsProcessSuspended(gameName);

            // 更新两按钮模式
            if (isSuspended)
            {
                PauseResumeIcon.Text = "▶";
                PauseResumeText.Text = "恢复运行";
                PauseResumeBtn.Foreground = new SolidColorBrush(MediaColor.FromRgb(78, 205, 196)); 
            }
            else
            {
                PauseResumeIcon.Text = "⏸";
                PauseResumeText.Text = "暂停游戏";
                PauseResumeBtn.Foreground = new SolidColorBrush(MediaColor.FromRgb(255, 142, 83)); 
            }
            
            // 核心逻辑：如果程序没启动，暂停按钮应当禁用
            PauseResumeBtn.IsEnabled = isRunning;
            PauseResumeBtn.Opacity = isRunning ? 1.0 : 0.5;

            if (isRunning)
            {
                LaunchKillIcon.Text = "💀";
                LaunchKillText.Text = "杀死进程";
                LaunchKillBtn.Foreground = new SolidColorBrush(MediaColor.FromRgb(255, 107, 107));
            }
            else
            {
                LaunchKillIcon.Text = "🚀";
                LaunchKillText.Text = "启动游戏";
                LaunchKillBtn.Foreground = new SolidColorBrush(MediaColor.FromRgb(112, 173, 7));
            }
            
            // 四按钮模式的禁用逻辑 (在容器内查找按钮并设置)
            if (FourButtonPanel.Visibility == Visibility.Visible)
            {
                foreach (var child in FourButtonPanel.Children)
                {
                    if (child is System.Windows.Controls.Button btn)
                    {
                        if (btn.Content is StackPanel sp)
                        {
                            foreach (var spChild in sp.Children)
                            {
                                if (spChild is TextBlock tb && (tb.Text.Contains("暂停") || tb.Text.Contains("恢复")))
                                {
                                    btn.IsEnabled = isRunning;
                                    btn.Opacity = isRunning ? 1.0 : 0.5;
                                }
                            }
                        }
                    }
                }
            }
        }

        private void SwitchToTwoButton_Click(object sender, RoutedEventArgs e)
        {
            TwoButtonPanel.Visibility = Visibility.Visible;
            FourButtonPanel.Visibility = Visibility.Collapsed;
            TwoButtonModeMenuItem.Header = "✓ Two Buttons (Default)";
            FourButtonModeMenuItem.Header = "Four Buttons";
            UpdateButtonStates();
        }

        private void SwitchToFourButton_Click(object sender, RoutedEventArgs e)
        {
            TwoButtonPanel.Visibility = Visibility.Collapsed;
            FourButtonPanel.Visibility = Visibility.Visible;
            TwoButtonModeMenuItem.Header = "Two Buttons (Default)";
            FourButtonModeMenuItem.Header = "✓ Four Buttons";
            UpdateButtonStates();
        }

        private void PauseGame_Click(object sender, RoutedEventArgs e) 
        {
            string gameName = GetSelectedGame();
            if (!string.IsNullOrEmpty(gameName))
            {
                PlayButtonSound("pause");
                ProcessLogic.PauseProcess(gameName);
                StatusLabel.Text = $"⏸ {gameName} Paused";
                StatusLabel.Foreground = new SolidColorBrush(MediaColor.FromRgb(255, 0, 255));
                UpdateButtonStates();
            }
        }

        private void ResumeGame_Click(object sender, RoutedEventArgs e)
        {
            string gameName = GetSelectedGame();
            if (!string.IsNullOrEmpty(gameName))
            {
                PlayButtonSound("resume");
                ProcessLogic.ResumeProcess(gameName);
                StatusLabel.Text = $"▶ {gameName} Resumed";
                StatusLabel.Foreground = new SolidColorBrush(MediaColor.FromRgb(0, 255, 0));
                UpdateButtonStates();
            }
        }

        private void KillGame_Click(object sender, RoutedEventArgs e)
        {
            string gameName = GetSelectedGame();
            if (!string.IsNullOrEmpty(gameName))
            {
                PlayButtonSound("kill");
                ProcessLogic.KillProcess(gameName);
                StatusLabel.Text = $"⏹ {gameName} Killed";
                StatusLabel.Foreground = new SolidColorBrush(MediaColor.FromRgb(255, 0, 0));
                UpdateButtonStates();
            }
        }

        private void LaunchGame_Click(object sender, RoutedEventArgs e)
        {
            string gameName = GetSelectedGame();
            if (!string.IsNullOrEmpty(gameName))
            {
                PlayButtonSound("launch");
                string exePath = gameDatabase.ContainsKey(gameName) ? gameDatabase[gameName].ExePath : null;
                try
                {
                    ProcessLogic.LaunchProcess(exePath, gameName);
                    StatusLabel.Text = $"🚀 Launching {gameName}...";
                    StatusLabel.Foreground = new SolidColorBrush(MediaColor.FromRgb(0, 123, 255));
                }
                catch (Exception ex)
                {
                    WPFMessageBox.Show(ex.Message, "启动失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                UpdateButtonStates();
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

        // 播放按钮音效的方法
        private void PlayButtonSound(string action)
        {
            try
            {
                // 使用更有感觉的系统音效
                switch (action.ToLower())
                {
                    case "pause":
                        // 暂停 - 使用Windows关机音效的变体
                        System.Media.SoundPlayer player1 = new System.Media.SoundPlayer(@"C:\Windows\Media\Windows Shutdown.wav");
                        if (File.Exists(@"C:\Windows\Media\Windows Shutdown.wav"))
                        {
                            player1.Play();
                        }
                        else
                        {
                            SystemSounds.Hand.Play();
                        }
                        break;
                    case "resume":
                        // 恢复 - 使用Windows启动音效
                        System.Media.SoundPlayer player2 = new System.Media.SoundPlayer(@"C:\Windows\Media\Windows Logon.wav");
                        if (File.Exists(@"C:\Windows\Media\Windows Logon.wav"))
                        {
                            player2.Play();
                        }
                        else
                        {
                            SystemSounds.Asterisk.Play();
                        }
                        break;
                    case "kill":
                        // 终止 - 使用Windows错误音效
                        System.Media.SoundPlayer player3 = new System.Media.SoundPlayer(@"C:\Windows\Media\Windows Critical Stop.wav");
                        if (File.Exists(@"C:\Windows\Media\Windows Critical Stop.wav"))
                        {
                            player3.Play();
                        }
                        else
                        {
                            SystemSounds.Exclamation.Play();
                        }
                        break;
                    case "launch":
                        // 启动 - 使用Windows通知音效
                        System.Media.SoundPlayer player4 = new System.Media.SoundPlayer(@"C:\Windows\Media\Windows Notify.wav");
                        if (File.Exists(@"C:\Windows\Media\Windows Notify.wav"))
                        {
                            player4.Play();
                        }
                        else
                        {
                            SystemSounds.Question.Play();
                        }
                        break;
                    default:
                        SystemSounds.Beep.Play();
                        break;
                }
            }
            catch (Exception)
            {
                // 如果播放失败，使用默认系统音效
                SystemSounds.Beep.Play();
            }
        }

        private void DeleteGame_Click(object sender, RoutedEventArgs e)
        {
            string gameName = GetSelectedGame();
            if (string.IsNullOrEmpty(gameName))
            {
                return;
            }

            var result = WPFMessageBox.Show($"从列表中删除 '{gameName}' ?", "删除exe", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                if (File.Exists(configFile))
                {
                    var lines = File.ReadAllLines(configFile).Where(l => !string.Equals(l.Trim(), gameName, StringComparison.OrdinalIgnoreCase)).ToList();
                    File.WriteAllLines(configFile, lines);
                    LoadConfig();
                    StatusLabel.Text = $"Deleted {gameName}";
                }
            }

        }

        private void ReloadConfig_Click(object sender, RoutedEventArgs e)
        {
            LoadConfig();
        }

        private void SearchProcess_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 获取所有进程并按内存使用量排序
                var processes = Process.GetProcesses()
                    .OrderByDescending(p => p.WorkingSet64)
                    .Take(10) // 取前10个
                    .Select(p => new ProcessItem
                    {
                        Name = p.ProcessName,
                        MemoryUsage = $"{(p.WorkingSet64 / 1024 / 1024)} MB"
                    })
                    .ToList();

                var dialog = new ProcessSelectionWindow(processes) { Owner = this };
                if (dialog.ShowDialog() == true)
                {
                    string exeName = dialog.SelectedProcessName;
                    if (!string.IsNullOrEmpty(exeName))
                    {
                        // 添加 .exe 后缀如果需要
                        AddGameToConfig(exeName + ".exe");
                    }
                }
            }
            catch (Exception ex)
            {
                WPFMessageBox.Show($"搜索进程失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddGameToConfig(string exeName)
        {
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
                AddGameToConfig(exeName);
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
        private void Opendevmgmt_Click(object sender, RoutedEventArgs e)
        {
           Process.Start(new ProcessStartInfo
            {
                FileName = "devmgmt.msc",
                UseShellExecute = true
            });
        }
        private void Opendiskmgmt_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "diskmgmt.msc",
                UseShellExecute = true
            });
        }
        private void Openncpa_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ncpa.cpl",
                UseShellExecute = true
            });
        }
        private void Openmsinfo32_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "msinfo32.exe",
                UseShellExecute = true
            });
        }
        //wf.msc
        private void Openwf_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "wf.msc",
                UseShellExecute = true
            });
        }
        //resmon.exe
        private void Openresmon_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "resmon.exe",
                UseShellExecute = true
            });
        }
        //powershell
        private void Openpowershell_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = true
            });
        }
        //cmd
        private void Opencmd_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                UseShellExecute = true
            });
        }


        private void OpenPssuspend_Click(object sender, RoutedEventArgs e)
        {
           //https://learn.microsoft.com/zh-tw/sysinternals/downloads/pssuspend
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://learn.microsoft.com/zh-tw/sysinternals/downloads/pssuspend",
                UseShellExecute = true
            });
        }

        private void OpenBilibili_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://www.bilibili.com/video/BV1hyULBEEzy",
                UseShellExecute = true
            });
        }
        private void OpenGitHub_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/cornradio/pausemygameW",
                UseShellExecute = true
            });
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void Tray_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
            if (notifyIcon != null)
            {
                notifyIcon.ShowBalloonTip(1000, "Pause My Game", "程序已最小化到托盘，点击图标恢复。", WinForms.ToolTipIcon.Info);
            }
        }

        private void Quit_Click(object sender, RoutedEventArgs e)
        {
            RealExit();
        }

        private void ToggleFullScreen_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Normal)
            {
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Maximized;
            }
            else
            {
                WindowState = WindowState.Normal;
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        #endregion
    }

    public class GameInfo
    {
        public string ExePath { get; set; }
        public string IconBase64 { get; set; }
    }

}