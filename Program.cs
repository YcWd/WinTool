using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ClipHistory
{
    internal static class Program
    {
        private static readonly int processId = Process.GetCurrentProcess().Id;
        private static readonly string logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinTool", "ClipboardHistory", "logs");

        // 启动单实例桌面应用，第二次启动时唤起已有窗口。
        [STAThread]
        private static void Main(string[] args)
        {
            bool created;
            using (var mutex = new Mutex(true, "Local\\WinTool.ClipHistory", out created))
            {
                if (!created)
                {
                    WriteDiagnostic("已有实例运行，发送唤起消息后结束本次启动。");
                    NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST,
                        NativeMethods.ShowHistoryMessage, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
                {
                    WriteDiagnostic("未处理异常；运行时将终止=" + e.IsTerminating + Environment.NewLine + e.ExceptionObject);
                };
                AppDomain.CurrentDomain.ProcessExit += delegate
                {
                    WriteDiagnostic("进程退出；退出码=" + Environment.ExitCode);
                };
                WriteDiagnostic("启动；版本=" + Application.ProductVersion + "；路径=" + Application.ExecutablePath +
                    "；托盘启动=" + (Array.IndexOf(args, "--tray") >= 0));
                NativeMethods.SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(Array.IndexOf(args, "--tray") >= 0));
                WriteDiagnostic("主消息循环正常结束。");
            }
        }

        // 按日期记录生命周期和运行时异常；日志写入失败不影响剪贴板业务。
        internal static void WriteDiagnostic(string message)
        {
            try
            {
                DateTime now = DateTime.Now;
                Directory.CreateDirectory(logDirectory);
                File.AppendAllText(Path.Combine(logDirectory, now.ToString("yyyy-MM-dd") + ".log"),
                    now.ToString("o") + " [PID " + processId + "] " + message + Environment.NewLine, Encoding.UTF8);
            }
            catch (IOException exception) { Trace.WriteLine("诊断日志写入失败：" + exception.Message); }
            catch (UnauthorizedAccessException exception) { Trace.WriteLine("诊断日志写入失败：" + exception.Message); }
        }
    }
}
