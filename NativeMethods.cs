using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace ClipHistory
{
    internal static class NativeMethods
    {
        internal const int WM_CLIPBOARDUPDATE = 0x031D;
        internal const int WM_HOTKEY = 0x0312;
        internal const int WM_NCCALCSIZE = 0x0083;
        internal const int WM_DWMCOMPOSITIONCHANGED = 0x031E;
        internal const int WS_CAPTION = 0x00C00000;
        internal const int WS_THICKFRAME = 0x00040000;
        internal static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);
        internal static readonly int ShowHistoryMessage = RegisterWindowMessage("WinTool.ClipHistory.Show");

        [DllImport("user32.dll")] internal static extern bool AddClipboardFormatListener(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool RemoveClipboardFormatListener(IntPtr window);
        [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr window, StringBuilder text, int maximum);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string className, string windowName);
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref uint size);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint ExtractIconEx(string path, int index, [Out] IntPtr[] largeIcons, IntPtr[] smallIcons, uint count);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
        [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int RegisterWindowMessage(string name);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] internal static extern bool ReleaseCapture();
        [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, string text);
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] private static extern bool InvalidateRect(IntPtr window, ref RECT rectangle, bool erase);
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref MARGINS margins);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        // 读取来源窗口的程序路径，进程退出或无访问权限时只保留窗口标题。
        internal static string GetWindowExecutable(IntPtr window)
        {
            uint processId;
            GetWindowThreadProcessId(window, out processId);
            if (processId == 0) return null;
            IntPtr process = OpenProcess(0x1000, false, processId);
            if (process == IntPtr.Zero) return null;
            try
            {
                var path = new StringBuilder(32768);
                uint size = (uint)path.Capacity;
                return QueryFullProcessImageName(process, 0, path, ref size) ? path.ToString() : null;
            }
            finally { CloseHandle(process); }
        }

        // 提取程序主图标并复制为托管对象，及时释放系统图标句柄。
        internal static Icon LoadApplicationIcon(string path)
        {
            var icons = new IntPtr[1];
            ExtractIconEx(path, 0, icons, null, 1);
            if (icons[0] == IntPtr.Zero) return null;
            try
            {
                using (Icon icon = Icon.FromHandle(icons[0])) return (Icon)icon.Clone();
            }
            finally { DestroyIcon(icons[0]); }
        }

        // 将已完整自绘的区域标记为待刷新，避免系统先清空背景。
        internal static void InvalidateWithoutErase(IntPtr window, Rectangle bounds)
        {
            var rectangle = new RECT { Left = bounds.Left, Top = bounds.Top, Right = bounds.Right, Bottom = bounds.Bottom };
            InvalidateRect(window, ref rectangle, false);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS
        {
            public int Left;
            public int Right;
            public int Top;
            public int Bottom;
        }

        // 使用 DWM 原生非客户区渲染，提供系统阴影和 Windows 11 圆角。
        internal static void ApplyWindowChrome(IntPtr window)
        {
            int policy = 2; // DWMNCRP_ENABLED
            DwmSetWindowAttribute(window, 2, ref policy, sizeof(int));
            int corners = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(window, 33, ref corners, sizeof(int));
            var margins = new MARGINS { Left = 1, Right = 1, Top = 1, Bottom = 1 };
            DwmExtendFrameIntoClientArea(window, ref margins);
        }

        // 启用系统 Acrylic 并扩展客户区；系统不支持时使用普通窗口边框。
        internal static bool ApplyAcrylicBackground(IntPtr window, bool enabled)
        {
            int backdrop = enabled ? 3 : 1; // DWMSBT_TRANSIENTWINDOW / DWMSBT_NONE
            if (DwmSetWindowAttribute(window, 38, ref backdrop, sizeof(int)) == 0 && enabled)
            {
                int darkMode = 0;
                DwmSetWindowAttribute(window, 20, ref darkMode, sizeof(int));
                var glass = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                if (DwmExtendFrameIntoClientArea(window, ref glass) == 0) return true;
            }
            backdrop = 1;
            DwmSetWindowAttribute(window, 38, ref backdrop, sizeof(int));
            var margins = new MARGINS { Left = 1, Right = 1, Top = 1, Bottom = 1 };
            DwmExtendFrameIntoClientArea(window, ref margins);
            return false;
        }

        // 弹出菜单只请求圆角，不扩展客户区以免覆盖菜单内容。
        internal static void ApplyMenuCorners(IntPtr window)
        {
            int corners = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(window, 33, ref corners, sizeof(int));
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint Type;
            public INPUTUNION Data;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUTUNION
        {
            [FieldOffset(0)] public KEYBDINPUT Keyboard;
            [FieldOffset(0)] public MOUSEINPUT Mouse;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort Key;
            public ushort Scan;
            public uint Flags;
            public uint Time;
            public IntPtr Extra;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int X;
            public int Y;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public IntPtr Extra;
        }

        // 发送一次标准 Ctrl+V，返回系统是否接受全部按键事件。
        internal static bool SendPaste()
        {
            INPUT[] inputs = {
                Key(0x11, false), Key(0x56, false), Key(0x56, true), Key(0x11, true)
            };
            return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT))) == inputs.Length;
        }

        // 构造一个键盘按下或松开事件。
        private static INPUT Key(ushort key, bool up)
        {
            return new INPUT { Type = 1, Data = new INPUTUNION {
                Keyboard = new KEYBDINPUT { Key = key, Flags = up ? 2u : 0u }
            }};
        }

        // 等待用户松开快捷键，避免组合键影响目标程序的粘贴。
        internal static bool ModifiersReleased()
        {
            return (GetAsyncKeyState(0x11) & 0x8000) == 0 &&
                (GetAsyncKeyState(0x12) & 0x8000) == 0 &&
                (GetAsyncKeyState(0x10) & 0x8000) == 0 &&
                (GetAsyncKeyState(0x5b) & 0x8000) == 0 &&
                (GetAsyncKeyState(0x5c) & 0x8000) == 0;
        }
    }
}
