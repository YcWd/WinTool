using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace ClipHistory
{
    internal static class BridgeProgram
    {
        // 在 STA 消息循环中执行剪贴板操作，通过 JSON 行与 Flutter 通信。
        [STAThread]
        private static void Main(string[] args)
        {
            Console.InputEncoding = new System.Text.UTF8Encoding(false);
            Console.OutputEncoding = new System.Text.UTF8Encoding(false);
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e) {
                Program.WriteDiagnostic("未处理异常；运行时将终止=" + e.IsTerminating + Environment.NewLine + e.ExceptionObject);
            };
            AppDomain.CurrentDomain.ProcessExit += delegate { Program.WriteDiagnostic("进程退出；退出码=" + Environment.ExitCode); };
            Application.EnableVisualStyles();
            NativeMethods.SetProcessDPIAware();
            using (var host = new BridgeHost(args.Contains("--translation")))
            {
                Task.Run(() => {
                    string line;
                    while ((line = Console.ReadLine()) != null)
                    {
                        string request = line;
                        host.BeginInvoke(new Action(() => host.Dispatch(request)));
                    }
                    host.BeginInvoke(new Action(Application.ExitThread));
                });
                Application.Run();
            }
        }
    }

    internal sealed partial class BridgeHost : Form
    {
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        private readonly bool translationOnly;
        private HistoryStore store;
        private AppSettings settings;
        private readonly string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinTool", "ClipboardHistory");
        private readonly Timer clipboardRetry = new Timer { Interval = 60 };
        private readonly Timer foregroundTimer = new Timer { Interval = 150 };
        private readonly Timer pasteTimer = new Timer { Interval = 50 };
        private readonly NotifyIcon tray = new NotifyIcon();
        private Mutex instance;
        private Process frontend;
        private Process translation;
        private IntPtr targetWindow;
        private IntPtr pendingPasteWindow;
        private uint capturedSequence, ownSequence;
        private int retries, pasteTicks, hotkeyId = 1;
        private bool writingClipboard, hotkeyRegistered, pinned, docked;
        private Rectangle normalBounds;
        private CancellationTokenSource requestCancellation;
        private string startupWarning;
        private IntPtr FrontendWindow { get { return FindFrontendWindow(frontend.Id); } }

        private delegate bool EnumWindowCallback(IntPtr window, IntPtr parameter);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowCallback callback, IntPtr parameter);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int maximum);

        // 按进程与窗口类定位 Flutter 窗口，收起到托盘时同样可用。
        private static IntPtr FindFrontendWindow(int processId)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows(delegate(IntPtr window, IntPtr parameter) {
                uint owner; NativeMethods.GetWindowThreadProcessId(window, out owner);
                if (owner != processId) return true;
                var name = new System.Text.StringBuilder(128);
                GetClassName(window, name, name.Capacity);
                if (name.ToString() != "FLUTTER_RUNNER_WIN32_WINDOW") return true;
                found = window; return false;
            }, IntPtr.Zero);
            return found;
        }

        // 创建不可见消息窗口，翻译进程仅提供配置和 API 服务。
        internal BridgeHost(bool translationMode)
        {
            translationOnly = translationMode;
            Directory.CreateDirectory(root);
            var handle = Handle;
            clipboardRetry.Tick += delegate { CaptureClipboard(); };
            foregroundTimer.Tick += delegate { TrackTarget(); };
            pasteTimer.Tick += delegate { CompletePaste(); };
        }

        // 协议边界统一报告错误，使失败操作能在 Flutter 界面中显示。
        internal async void Dispatch(string line)
        {
            int id = 0;
            try
            {
                var request = json.Deserialize<System.Collections.Generic.Dictionary<string, object>>(line);
                id = Convert.ToInt32(request["id"]);
                var args = (System.Collections.Generic.Dictionary<string, object>)request["args"];
                object result = await Execute((string)request["method"], args);
                Send(new { id = id, result = result });
            }
            catch (Exception exception)
            {
                Send(new { id = id, error = exception.Message });
                Program.WriteDiagnostic("桥接请求失败：" + exception);
            }
        }

        // 将界面请求映射到现有存储和 Windows 能力，所有状态修改均在 STA 线程执行。
        private async Task<object> Execute(string method, System.Collections.Generic.Dictionary<string, object> args)
        {
            switch (method)
            {
                case "initialize":
                    frontend = Process.GetProcessById(Convert.ToInt32(args["pid"]));
                    if (!translationOnly)
                    {
                        bool created;
                        instance = new Mutex(true, "Local\\WinTool.ClipHistory", out created);
                        if (!created)
                        {
                            NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST, NativeMethods.ShowHistoryMessage, IntPtr.Zero, IntPtr.Zero);
                            return new { duplicate = true };
                        }
                        store = new HistoryStore();
                        settings = AppSettings.Load(root);
                        if (AppSettings.StartupEnabled) AppSettings.SetStartup(true, frontend.MainModule.FileName);
                        store.SetRecordLimit(settings.RecordLimit);
                        BuildTray();
                        if (!NativeMethods.AddClipboardFormatListener(Handle)) throw new InvalidOperationException("无法注册剪贴板监听。");
                        hotkeyRegistered = NativeMethods.RegisterHotKey(Handle, hotkeyId, settings.HotkeyModifiers | 0x4000, settings.HotkeyKey);
                        if (!hotkeyRegistered) startupWarning = settings.HotkeyText + " 已被占用，请在设置中更换。";
                        if (!NativeMethods.RegisterHotKey(Handle, 3, 0x4001, 0x32))
                            tray.ShowBalloonTip(5000, "开发字段翻译", "Alt+2 已被占用，请从托盘打开。", ToolTipIcon.Warning);
                        ApplyLayout();
                        foregroundTimer.Start();
                        CaptureClipboard();
                    }
                    Program.WriteDiagnostic("Flutter 桥接启动；前端 PID=" + frontend.Id);
                    if (translationOnly) return TranslationConfiguration();
                    return new { duplicate = false, root = root, settings = SettingsState(), records = store.Read("", "全部类型"), warning = startupWarning };
                case "records": return store.Read("", "全部类型");
                case "pin":
                    var pinRecord = FindRecord(args);
                    store.SetPinned(pinRecord, !pinRecord.IsPinned); PublishRecords(); return null;
                case "delete": store.Delete(FindRecord(args)); PublishRecords(); return null;
                case "clear": store.Clear(); PublishRecords(); return null;
                case "copy":
                    if (!WriteClipboard(FindRecord(args))) throw new InvalidOperationException("恢复剪贴板失败，请重试。");
                    return null;
                case "paste": PasteRecord(FindRecord(args)); return null;
                case "settings": return SettingsState();
                case "saveSettings":
                    try { SaveSettings(args); return SettingsState(); }
                    finally
                    {
                        Send(new { eventName = "configuration", settings = SettingsState() });
                        PublishRecords();
                    }
                case "translationConfig": return TranslationConfiguration();
                case "saveTranslationConfig":
                    TranslationSettings.Save(root, (string)args["apiKey"], ((string)args["model"]).Trim()); return null;
                case "translate":
                    if (requestCancellation != null) throw new InvalidOperationException("翻译请求仍在进行中。");
                    var configuration = TranslationSettings.Load(root);
                    using (requestCancellation = new CancellationTokenSource())
                    {
                        try { return await DeepSeekTranslator.TranslateAsync(configuration.ReadApiKey(), configuration.Model,
                            (string)args["input"], (bool)args["toChinese"], (string)args["naming"], requestCancellation.Token); }
                        finally { requestCancellation = null; }
                    }
                case "cancelTranslation": if (requestCancellation != null) requestCancellation.Cancel(); return null;
                case "copyText": Clipboard.SetText((string)args["text"]); return null;
                case "openTranslation": OpenTranslation(); return null;
                case "openData": Process.Start(root); return null;
                case "hide": HideFrontend(); return null;
                case "minimize": NativeMethods.ShowWindow(FrontendWindow, 6); return null;
                case "drag":
                    if (!docked)
                    {
                        NativeMethods.SendMessage(FrontendWindow, 0x8016, IntPtr.Zero, IntPtr.Zero);
                    }
                    return null;
                case "topmost":
                    pinned = !pinned;
                    NativeMethods.SetWindowPos(FrontendWindow, new IntPtr(pinned ? -1 : -2), 0, 0, 0, 0, 0x13); return pinned;
                default: throw new InvalidOperationException("未知桥接方法：" + method);
            }
        }

        // 使用持久 ID 定位记录，避免界面筛选或排序影响操作对象。
        private ClipRecord FindRecord(System.Collections.Generic.Dictionary<string, object> args)
        {
            return store.Read("", "全部类型").First(r => r.Id == (string)args["id"]);
        }

        // 向前端提供配置状态，沿用原有 JSON 字段名。
        private object SettingsState()
        {
            return new { HotkeyModifiers = settings.HotkeyModifiers, HotkeyKey = settings.HotkeyKey,
                DuplicateSeconds = settings.DuplicateSeconds, DockBottom = settings.DockBottom,
                RecordLimit = settings.RecordLimit, Startup = AppSettings.StartupEnabled };
        }

        // 读取当前用户 DPAPI 配置，仅在本机进程间传递。
        private object TranslationConfiguration()
        {
            var configuration = TranslationSettings.Load(root);
            return new { apiKey = configuration.ReadApiKey(), model = configuration.Model };
        }

        // 先注册候选快捷键，保存失败时撤销注册，保留原快捷键。
        private void SaveSettings(System.Collections.Generic.Dictionary<string, object> args)
        {
            var candidate = json.ConvertToType<AppSettings>(args);
            if (candidate.HotkeyModifiers == 0 || candidate.DuplicateSeconds < 0 || candidate.DuplicateSeconds > 60 ||
                !new[] { 20, 50, 100 }.Contains(candidate.RecordLimit)) throw new InvalidOperationException("配置值无效。");
            if (candidate.HotkeyModifiers == 1 && candidate.HotkeyKey == 0x32) throw new InvalidOperationException("Alt+2 已保留给开发字段翻译。");
            bool replace = !hotkeyRegistered || candidate.HotkeyModifiers != settings.HotkeyModifiers || candidate.HotkeyKey != settings.HotkeyKey;
            int nextId = hotkeyId == 1 ? 2 : 1;
            if (replace && !NativeMethods.RegisterHotKey(Handle, nextId, candidate.HotkeyModifiers | 0x4000, candidate.HotkeyKey))
                throw new InvalidOperationException("快捷键已被占用，请选择其他组合。");
            try { candidate.Save(root); }
            catch { if (replace) NativeMethods.UnregisterHotKey(Handle, nextId); throw; }
            if (replace)
            {
                if (hotkeyRegistered) NativeMethods.UnregisterHotKey(Handle, hotkeyId);
                hotkeyId = nextId; hotkeyRegistered = true;
            }
            settings = candidate;
            ApplyLayout();
            store.SetRecordLimit(settings.RecordLimit);
            AppSettings.SetStartup((bool)args["Startup"], frontend.MainModule.FileName);
        }

        // 使用原生托盘保留双击打开、启动项、数据目录及退出入口。
        private void BuildTray()
        {
            var menu = new TrayMenu(SystemFonts.MenuFont);
            var open = menu.Items.Add("打开剪贴板", null, delegate { ShowFrontend(); });
            var openItem = (ToolStripMenuItem)open;
            menu.Opening += delegate { openItem.ShortcutKeyDisplayString = settings.HotkeyText; };
            var translationItem = (ToolStripMenuItem)menu.Items.Add("开发字段翻译", null, delegate { OpenTranslation(); });
            translationItem.ShortcutKeyDisplayString = "Alt+2";
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("设置", null, delegate { ShowFrontend(); Send(new { eventName = "settings" }); });
            var startup = new ToolStripMenuItem("开机启动");
            menu.Opening += delegate { startup.Checked = AppSettings.StartupEnabled; };
            startup.Click += delegate {
                try { AppSettings.SetStartup(!AppSettings.StartupEnabled, frontend.MainModule.FileName); }
                catch (Exception exception) { SetStatus(exception.Message); }
            };
            menu.Items.Add(startup);
            menu.Items.Add("打开数据目录", null, delegate { Process.Start(root); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate {
                Program.WriteDiagnostic("用户从 Flutter 托盘退出。");
                if (translation != null && !translation.HasExited) translation.Kill();
                frontend.Kill(); Application.ExitThread();
            });
            tray.Icon = Icon.ExtractAssociatedIcon(frontend.MainModule.FileName);
            tray.Text = "剪贴记录"; tray.ContextMenuStrip = menu; tray.Visible = true;
            tray.DoubleClick += delegate { ShowFrontend(); };
        }

        // 独立 Flutter 进程保留翻译窗口输入，重复打开只恢复窗口。
        private void OpenTranslation()
        {
            if (translation == null || translation.HasExited)
                translation = Process.Start(new ProcessStartInfo(frontend.MainModule.FileName, "--translation") { UseShellExecute = false });
            else
            {
                NativeMethods.ShowWindow(FindFrontendWindow(translation.Id), 9);
                NativeMethods.SetForegroundWindow(FindFrontendWindow(translation.Id));
            }
        }

        // 跟踪外部粘贴目标，普通未置顶窗口在失焦时收起。
        private void TrackTarget()
        {
            if (docked && !NativeMethods.IsIconic(FrontendWindow)) PositionBottomWindow();
            IntPtr window = NativeMethods.GetForegroundWindow();
            uint process;
            NativeMethods.GetWindowThreadProcessId(window, out process);
            bool internalWindow = process == frontend.Id || process == Process.GetCurrentProcess().Id ||
                (translation != null && !translation.HasExited && process == translation.Id);
            if (window != IntPtr.Zero && !internalWindow && NativeMethods.IsWindowVisible(window))
            {
                targetWindow = window;
                if (!pinned && NativeMethods.IsWindowVisible(FrontendWindow) && !NativeMethods.IsIconic(FrontendWindow)) HideFrontend();
            }
        }

        // 唤起时通知 Flutter 聚焦搜索框。
        private void ShowFrontend()
        {
            if (pasteTimer.Enabled) return;
            TrackTarget(); ApplyLayout();
            NativeMethods.ShowWindow(FrontendWindow, 9);
            NativeMethods.SetForegroundWindow(FrontendWindow);
            Send(new { eventName = "show" });
        }

        // 隐藏界面时保留后台监听与窗口状态。
        private void HideFrontend()
        {
            if (requestCancellation != null) requestCancellation.Cancel();
            NativeMethods.ShowWindow(FrontendWindow, 0);
        }

        // 底部模式使用当前屏幕工作区，关闭后恢复普通窗口尺寸位置。
        private void ApplyLayout()
        {
            NativeMethods.SendMessage(FrontendWindow, 0x8014, new IntPtr(settings.DockBottom ? 1 : 0), IntPtr.Zero);
            if (settings.DockBottom)
            {
                if (!docked) normalBounds = ScreenBounds();
                docked = true;
                PositionBottomWindow();
            }
            else if (docked)
            {
                docked = false;
                NativeMethods.SetWindowPos(FrontendWindow, IntPtr.Zero, normalBounds.X, normalBounds.Y, normalBounds.Width, normalBounds.Height, 0x14);
            }
        }

        // 工作区变化后重新贴齐底部，尺寸未变时避免重复调整窗口。
        private void PositionBottomWindow()
        {
            Rectangle area = Screen.FromHandle(FrontendWindow).WorkingArea;
            int height = Math.Min(260, area.Height);
            var bounds = new Rectangle(area.Left, area.Bottom - height, area.Width, height);
            if (ScreenBounds() != bounds)
                NativeMethods.SetWindowPos(FrontendWindow, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x14);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr window, out WindowRect rectangle);
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct WindowRect { public int Left, Top, Right, Bottom; }

        // 读取前端窗口边界供底部模式切换恢复。
        private Rectangle ScreenBounds()
        {
            WindowRect bounds; GetWindowRect(FrontendWindow, out bounds);
            return Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        }

        // 确认外部目标后恢复剪贴板，并延迟发送粘贴按键。
        private void PasteRecord(ClipRecord record)
        {
            if (pasteTimer.Enabled) return;
            if (!NativeMethods.IsWindow(targetWindow)) throw new InvalidOperationException("目标窗口已关闭，请先点击目标输入框。");
            if (!WriteClipboard(record)) throw new InvalidOperationException("恢复剪贴板失败，请重试。");
            pendingPasteWindow = targetWindow;
            NativeMethods.SendMessage(FrontendWindow, 0x8015, new IntPtr(Process.GetCurrentProcess().Id), IntPtr.Zero);
            if (!pinned) HideFrontend();
            if (NativeMethods.IsIconic(pendingPasteWindow)) NativeMethods.ShowWindow(pendingPasteWindow, 9);
            NativeMethods.SetForegroundWindow(pendingPasteWindow);
            pasteTicks = 0; pasteTimer.Start();
        }

        // 只在焦点正确且修饰键松开后粘贴，超时提示手动粘贴。
        private void CompletePaste()
        {
            pasteTicks++;
            if (NativeMethods.GetForegroundWindow() == pendingPasteWindow && NativeMethods.ModifiersReleased())
            {
                pasteTimer.Stop();
                if (!NativeMethods.SendPaste()) NotifyPasteFailure();
            }
            else if (pasteTicks >= 30) { pasteTimer.Stop(); NotifyPasteFailure(); }
        }

        // 窗口已收起时仍通过托盘提示自动粘贴失败。
        private void NotifyPasteFailure()
        {
            const string message = "内容已复制，请在目标输入框手动 Ctrl+V；管理员窗口可能需要相同权限。";
            SetStatus(message);
            tray.ShowBalloonTip(4000, "自动粘贴未完成", message, ToolTipIcon.Info);
        }

        // 剪贴板和全局快捷键通知由隐藏消息窗口处理。
        protected override void WndProc(ref Message message)
        {
            if (store != null)
            {
                if (message.Msg == NativeMethods.WM_CLIPBOARDUPDATE) { retries = 0; CaptureClipboard(); }
                if (message.Msg == NativeMethods.ShowHistoryMessage || (message.Msg == NativeMethods.WM_HOTKEY && message.WParam.ToInt32() == hotkeyId)) ShowFrontend();
                if (message.Msg == NativeMethods.WM_HOTKEY && message.WParam.ToInt32() == 3) OpenTranslation();
            }
            base.WndProc(ref message);
        }

        // 存储变更后推送完整的小规模记录集，筛选在 Flutter 内完成。
        private void PublishRecords() { Send(new { eventName = "records", records = store.Read("", "全部类型") }); }
        // 向界面显示可恢复的业务错误。
        private void SetStatus(string message) { Send(new { eventName = "status", message = message }); }
        // 清除上一次剪贴板操作反馈。
        private void ClearStatus() { SetStatus(""); }
        // 每条协议消息独占一行，标准输出仅用于协议。
        private void Send(object message) { Console.WriteLine(json.Serialize(message)); }

        // 前端退出或关闭管道时释放监听、快捷键及托盘资源。
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (requestCancellation != null) requestCancellation.Cancel();
                NativeMethods.RemoveClipboardFormatListener(Handle);
                NativeMethods.UnregisterHotKey(Handle, hotkeyId); NativeMethods.UnregisterHotKey(Handle, 3);
                clipboardRetry.Dispose(); foregroundTimer.Dispose(); pasteTimer.Dispose();
                if (tray.ContextMenuStrip != null) tray.ContextMenuStrip.Dispose();
                tray.Dispose();
                if (translation != null) { if (!translation.HasExited) translation.Kill(); translation.Dispose(); }
                if (instance != null) instance.Dispose();
                Program.WriteDiagnostic("Flutter 桥接消息循环结束。");
            }
            base.Dispose(disposing);
        }
    }
}
