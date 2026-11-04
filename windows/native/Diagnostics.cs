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
