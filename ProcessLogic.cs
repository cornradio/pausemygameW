using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;

namespace WpfApp1
{
    public static class ProcessLogic
    {
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_MINIMIZE = 6;
        private const int SW_RESTORE = 9;

        public static bool IsProcessRunning(string exeName)
        {
            try
            {
                string nameWithoutExt = Path.GetFileNameWithoutExtension(exeName);
                return Process.GetProcessesByName(nameWithoutExt).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsProcessSuspended(string exeName)
        {
            try
            {
                string nameWithoutExt = Path.GetFileNameWithoutExtension(exeName);
                Process[] processes = Process.GetProcessesByName(nameWithoutExt);
                if (processes.Length == 0) return false;

                // 检查主进程的所有线程是否都处于挂起或等待状态
                // 注意：这并不是 100% 准确，但对于一般游戏暂停检测足够了
                foreach (ProcessThread thread in processes[0].Threads)
                {
                    if (thread.ThreadState != System.Diagnostics.ThreadState.Wait || 
                        thread.WaitReason != ThreadWaitReason.Suspended)
                    {
                        // 如果有任何一个线程不是挂起状态，我们认为进程还在运行
                        // PsSuspend 通常会挂起所有线程
                        return false;
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void PauseProcess(string exeName, bool minimizeOnPause = true)
        {
            if (!CheckPsSuspend()) return;

            string nameWithoutExt = Path.GetFileNameWithoutExtension(exeName);
            Process[] processes = Process.GetProcessesByName(nameWithoutExt);
            if (processes.Length > 0 && minimizeOnPause)
            {
                try
                {
                    if (processes[0].MainWindowHandle != IntPtr.Zero)
                    {
                        ShowWindow(processes[0].MainWindowHandle, SW_MINIMIZE);
                    }
                }
                catch { }
            }
            RunCommand($"PsSuspend \"{exeName}\"");
        }

        public static void ResumeProcess(string exeName, bool minimizeOnPause = true)
        {
            if (!CheckPsSuspend()) return;
            RunCommand($"PsSuspend -r \"{exeName}\"");

            // 恢复后自动弹出窗口
            string nameWithoutExt = Path.GetFileNameWithoutExtension(exeName);
            Process[] processes = Process.GetProcessesByName(nameWithoutExt);
            if (processes.Length > 0 && minimizeOnPause)
            {
                try
                {
                    if (processes[0].MainWindowHandle != IntPtr.Zero)
                    {
                        ShowWindow(processes[0].MainWindowHandle, SW_RESTORE);
                    }
                }
                catch { }
            }
        }

        private static bool CheckPsSuspend()
        {
            string pssuspendPath = "PsSuspend.exe";
            if (!File.Exists(pssuspendPath) && !File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, pssuspendPath)))
            {
                System.Windows.MessageBox.Show("找不到PsSuspend.exe工具，请确保它在程序目录或系统路径中。\n\n您可以从Sysinternals Suite下载此工具。",
                    "缺少必要工具", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }

        public static void KillProcess(string exeName)
        {
            RunCommand($"taskkill /IM \"{exeName}\" /F");
        }

        public static void LaunchProcess(string exePath, string gameName)
        {
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                Process.Start(exePath);
            }
            else
            {
                try
                {
                    Process.Start(gameName);
                }
                catch (Exception ex)
                {
                    throw new Exception($"无法启动 {gameName}: {ex.Message}");
                }
            }
        }

        private static void RunCommand(string command)
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
                System.Windows.MessageBox.Show($"执行命令失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
