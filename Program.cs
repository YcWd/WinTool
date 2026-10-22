using System;
using System.Threading;
using System.Windows.Forms;

namespace ClipHistory
{
    internal static class Program
    {
        // 启动单实例桌面应用，第二次启动时唤起已有窗口。
        [STAThread]
        private static void Main(string[] args)
        {
            bool created;
            using (var mutex = new Mutex(true, "Local\\WinTool.ClipHistory", out created))
            {
                if (!created)
                {
                    NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST,
                        NativeMethods.ShowHistoryMessage, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                NativeMethods.SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(Array.IndexOf(args, "--tray") >= 0));
            }
        }
    }
}
