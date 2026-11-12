using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Windows.Forms;

namespace ClipHistory
{
    internal sealed class MainForm : Form
    {
        private readonly HistoryStore store = new HistoryStore();
        private readonly ThumbnailCache thumbnails;
        private readonly Dictionary<string, Icon> sourceIcons = new Dictionary<string, Icon>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<string> sourceIconOrder = new Queue<string>();
        private readonly int recordHeaderHeight;
        private const int RecordCardRadius = 20;
        private const int HoverTransitionDuration = 160;
        private const int DeleteTransitionDuration = 200;
        private readonly Dictionary<string, RecordAnimation> recordAnimations = new Dictionary<string, RecordAnimation>();
        private readonly Stopwatch animationClock = Stopwatch.StartNew();
        private long lastAnimationFrame;
        private Bitmap rowBuffer;
        private readonly Dictionary<string, string[]> previewLines = new Dictionary<string, string[]>();
        private Size previewSize;
        private int previewLineHeight;
        private readonly OverlayListBox records = new OverlayListBox();
        private readonly TextBox search = new TextBox();
        private List<ClipRecord> filteredRecords = new List<ClipRecord>();
        private readonly RadioButton[] kindTabs = new RadioButton[4];
        private string selectedKind = "全部类型";
        private readonly Label status = new FluentLabel();
        private readonly RowStyle feedbackRow = new RowStyle(SizeType.Absolute, 0);
        private readonly Label emptyState = new FluentLabel();
        private readonly ToolTip tips = new ToolTip();
        private readonly Button pinButton = new FluentButton();
        private readonly Icon applicationIcon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        private AppSettings settings;
        private ToolStripMenuItem trayOpen;
        private ToolStripMenuItem trayStartup;
        private int hotkeyId = 1;
        private const int TranslationHotkeyId = 3;
        private bool translationHotkeyRegistered;
        private TranslationForm translationWindow;
        private bool bottomLayout;
        private Rectangle normalBounds;
        private TableLayoutPanel layout;
        private Panel heading;
        private FlowLayoutPanel windowActions;
        private Panel tabSurface;
        private TableLayoutPanel compactActions;
        private TableLayoutPanel toolbar;
        private SearchField searchField;
        private Button reset;
        private readonly TableLayoutPanel compactHeader = new TableLayoutPanel { Dock = DockStyle.Fill,
            ColumnCount = 3, RowCount = 1, Margin = Padding.Empty, Padding = new Padding(0, 6, 0, 6), BackColor = FluentStyle.Background };
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly Timer foregroundTimer = new Timer { Interval = 150 };
        private readonly Timer clipboardRetry = new Timer { Interval = 60 };
        private readonly Timer filterTimer = new Timer { Interval = 250 };
        private readonly Timer pasteTimer = new Timer { Interval = 50 };
        private readonly Timer animationTimer = new Timer { Interval = 16 };
        private readonly Font titleFont = new Font("Microsoft YaHei UI", 10, FontStyle.Bold);
        private readonly Font symbolFont = new Font("Segoe MDL2 Assets", 10);
        private readonly Font rowFont = new Font("Microsoft YaHei UI", 10, FontStyle.Regular);
        private readonly Font contentFont = new Font("Microsoft YaHei UI", 12, FontStyle.Regular);
        private readonly Font smallFont = new Font("Microsoft YaHei UI", 9, FontStyle.Regular);
        private readonly bool startInTray;
        private IntPtr targetWindow;
        private IntPtr pendingPasteWindow;
        private uint capturedSequence;
        private uint ownSequence;
        private int retries;
        private int pasteTicks;
        private bool exiting;
        private bool hotkeyRegistered;
        private bool writingClipboard;
        private int hoveredRecord = ListBox.NoMatches;
        private RecordAction hoveredAction = RecordAction.None;
        private enum RecordAction { None = -1, Preview, Delete, Pin }

        private sealed class RecordAnimation
        {
            internal float Hover;
            internal float TargetHover;
            internal float DeleteProgress;
            internal long DeleteStarted;
            internal ClipRecord DeletingRecord;
        }
        private readonly uint currentProcessId = (uint)Process.GetCurrentProcess().Id;
        private readonly Color ink = FluentStyle.Text;
        private readonly Color muted = FluentStyle.SecondaryText;
        private readonly Color accent = FluentStyle.Accent;

        // 初始化历史窗口，并在启动时记住当前粘贴目标。
        internal MainForm(bool trayOnly)
        {
            thumbnails = new ThumbnailCache(store);
            using (var graphics = Graphics.FromHwnd(IntPtr.Zero))
            {
                recordHeaderHeight = Math.Max(52, (int)Math.Ceiling(rowFont.GetHeight(graphics)) +
                    (int)Math.Ceiling(smallFont.GetHeight(graphics)) + 18);
                records.NormalItemHeight = Math.Max(160, recordHeaderHeight +
                    (int)Math.Ceiling(contentFont.GetHeight(graphics)) * 2 +
                    (int)Math.Ceiling(4 * graphics.DpiY / 96f) + 26) + OverlayListBox.CardMargin * 2;
            }
            records.RemovalProgress = GetRecordRemovalProgress;
            startInTray = trayOnly;
            settings = AppSettings.Load(store.Root);
            store.SetRecordLimit(settings.RecordLimit);
            targetWindow = NativeMethods.GetForegroundWindow();
            Text = "剪贴记录";
            Icon = applicationIcon;
            Font = rowFont;
            BackColor = FluentStyle.Background;
            ForeColor = ink;
            FormBorderStyle = FormBorderStyle.None;
            Padding = new Padding(8);
            DoubleBuffered = true;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(520, 560);
            MinimumSize = new Size(520, 420);
            BuildLayout();
            BuildTray();
            foregroundTimer.Tick += delegate { TrackTarget(); };
            clipboardRetry.Tick += delegate { CaptureClipboard(); };
            filterTimer.Tick += delegate { filterTimer.Stop(); ReloadRecords(); };
            pasteTimer.Tick += delegate { CompletePaste(); };
            animationTimer.Tick += delegate { AnimateRecords(); };
            foregroundTimer.Start();
            Shown += delegate
            {
                Size = new Size(520, 560);
                ApplyWindowLayout();
                if (startInTray) Hide();
                ReloadRecords();
                CaptureClipboard();
                RefreshStartup();
            };
        }

        // 已启用开机启动时同步当前 EXE 路径，手动关闭后保持关闭。
        private void RefreshStartup()
        {
            try
            {
                if (AppSettings.StartupEnabled) AppSettings.SetStartup(true);
                trayStartup.Checked = AppSettings.StartupEnabled;
            }
            catch (UnauthorizedAccessException exception) { SetStatus("开机启动更新失败：" + exception.Message); }
            catch (IOException exception) { SetStatus("开机启动更新失败：" + exception.Message); }
        }

        // 顶栏沿用窗口底色，创建普通窗口和底部通栏复用的标题与筛选控件。
        private void BuildLayout()
        {
            layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 12, 0), ColumnCount = 1, RowCount = 5 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            layout.RowStyles.Add(feedbackRow);
            Controls.Add(layout);

            heading = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = BackColor };
            var title = new FluentLabel { Text = "剪贴记录", Font = titleFont, AutoSize = true,
                UseCompatibleTextRendering = true, Location = new Point(4, 6) };
            heading.Controls.Add(title);
            heading.SizeChanged += delegate { title.Top = (heading.ClientSize.Height - title.Height) / 2; };
            heading.MouseDown += DragWindow;
            title.MouseDown += DragWindow;
            windowActions = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 144, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty };
            windowActions.SizeChanged += delegate
            {
                windowActions.Padding = new Padding(0, Math.Max(0, (windowActions.ClientSize.Height - 28) / 2), 0, 0);
            };
            pinButton.Text = "\uE718";
            pinButton.Size = new Size(32, 28);
            pinButton.Font = symbolFont;
            pinButton.Margin = new Padding(2, 0, 2, 0);
            pinButton.FlatStyle = FlatStyle.Flat;
            pinButton.FlatAppearance.BorderSize = 0;
            pinButton.BackColor = BackColor;
            pinButton.ForeColor = muted;
            pinButton.Cursor = Cursors.Hand;
            pinButton.AccessibleName = "切换窗口置顶";
            pinButton.Click += delegate { TogglePin(); };
            tips.SetToolTip(pinButton, "置顶后，失去焦点仍保持显示");
            var preferences = CreateButton("\uE713", 32);
            preferences.Font = symbolFont;
            preferences.Margin = new Padding(2, 0, 2, 0);
            preferences.AccessibleName = "设置";
            preferences.Click += delegate { OpenSettings(); };
            var minimize = CreateButton("\uE921", 32);
            minimize.Font = symbolFont;
            minimize.Margin = new Padding(2, 0, 2, 0);
            minimize.AccessibleName = "最小化";
            minimize.Click += delegate { WindowState = FormWindowState.Minimized; };
            var close = CreateButton("\uE8BB", 32);
            close.Font = symbolFont;
            close.Margin = new Padding(2, 0, 2, 0);
            close.AccessibleName = "收起到托盘";
            close.Click += delegate { Close(); };
            tips.SetToolTip(close, "收起到托盘，继续记录");
            tips.SetToolTip(minimize, "最小化");
            tips.SetToolTip(preferences, "快捷键、开机启动与重复复制设置");
            windowActions.Controls.AddRange(new Control[] { pinButton, preferences, minimize, close });
            windowActions.BackColor = BackColor;
            foreach (Button button in windowActions.Controls) button.BackColor = BackColor;
            heading.Controls.Add(windowActions);
            layout.Controls.Add(heading, 0, 0);

            tabSurface = new Panel { Dock = DockStyle.Fill, BackColor = BackColor,
                Margin = new Padding(0, 3, 0, 7) };
            var tabs = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1,
                Margin = Padding.Empty, BackColor = BackColor, AccessibleName = "记录类型筛选" };
            tabs.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            tabSurface.Controls.Add(tabs);
            string[] typeNames = { "全部类型", "文本", "图片", "文件" };
            for (int index = 0; index < typeNames.Length; index++)
            {
                tabs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
                string kind = typeNames[index];
                var tab = new SegmentTab { Text = index == 0 ? "全部" : kind, Tag = kind,
                    Dock = DockStyle.Fill, Appearance = Appearance.Button, FlatStyle = FlatStyle.Flat,
                    TextAlign = ContentAlignment.MiddleCenter, Font = rowFont, ForeColor = ink,
                    Margin = new Padding(1, 0, 1, 0), Cursor = Cursors.Hand, AccessibleName = "筛选：" + kind };
                tab.FlatAppearance.BorderSize = 0;
                tab.FlatAppearance.CheckedBackColor = FluentStyle.Surface;
                tab.FlatAppearance.MouseOverBackColor = Color.FromArgb(232, 232, 232);
                tab.FlatAppearance.MouseDownBackColor = Color.FromArgb(222, 234, 245);
                tab.CheckedChanged += delegate { SelectKind(tab); };
                kindTabs[index] = tab;
                tabs.Controls.Add(tab, index, 0);
            }
            kindTabs[0].Checked = true;
            layout.Controls.Add(tabSurface, 0, 1);

            var listHost = new Panel { Dock = DockStyle.Fill, BackColor = BackColor, Padding = new Padding(0, 2, 0, 2), Margin = new Padding(0) };
            var listContent = new Panel { Dock = DockStyle.Fill, BackColor = BackColor };
            listHost.Controls.Add(listContent);
            listHost.Controls.Add(records.ScrollBar);
            records.Dock = DockStyle.Fill;
            records.BorderStyle = BorderStyle.None;
            records.BackColor = BackColor;
            records.Cursor = Cursors.Hand;
            records.DrawMode = DrawMode.OwnerDrawFixed;
            records.IntegralHeight = false;
            records.AccessibleName = "剪贴板历史记录，Enter 粘贴，Ctrl+P 切换记录置顶，Delete 删除，右键或 Shift+F10 打开记录菜单";
            records.DrawItem += DrawRecord;
            records.MouseUp += RecordMouseUp;
            records.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                TrackHoveredRecord(e.Location);
            };
            records.MouseLeave += delegate { UpdateHoveredRecord(ListBox.NoMatches, RecordAction.None); };
            records.ViewScrolled += delegate { TrackHoveredRecord(records.PointToClient(Control.MousePosition)); };
            records.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter && records.SelectedItem != null)
                {
                    e.Handled = e.SuppressKeyPress = true;
                    PasteRecord((ClipRecord)records.SelectedItem);
                }
                else if ((e.KeyCode == Keys.Apps || (e.Shift && e.KeyCode == Keys.F10)) && records.SelectedItem != null)
                {
                    e.Handled = e.SuppressKeyPress = true;
                    Rectangle visible = Rectangle.Intersect(records.GetItemRectangle(records.SelectedIndex), records.ClientRectangle);
                    ShowRecordMenu((ClipRecord)records.SelectedItem, new Point(visible.Right, visible.Top));
                }
                else if (e.Control && e.KeyCode == Keys.P && records.SelectedItem != null)
                {
                    e.Handled = e.SuppressKeyPress = true;
                    ToggleRecordPin((ClipRecord)records.SelectedItem);
                }
                else if (e.KeyCode == Keys.Delete && records.SelectedItem != null)
                {
                    e.Handled = e.SuppressKeyPress = true;
                    DeleteRecord((ClipRecord)records.SelectedItem);
                }
            };
            listContent.Controls.Add(records);
            emptyState.Dock = DockStyle.Fill;
            emptyState.TextAlign = ContentAlignment.MiddleCenter;
            emptyState.ForeColor = ink;
            emptyState.UseCompatibleTextRendering = true;
            emptyState.Text = "暂无记录";
            emptyState.Visible = false;
            listContent.Controls.Add(emptyState);
            layout.Controls.Add(listHost, 0, 2);

            toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                Padding = new Padding(0, 10, 0, 8), Margin = Padding.Empty, BackColor = BackColor };
            toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54));
            search.Font = rowFont;
            search.ForeColor = ink;
            search.AccessibleName = "搜索历史记录的内容、文件名或来源窗口";
            search.TextChanged += delegate { ScheduleFilter(); };
            searchField = new SearchField(search) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0) };
            toolbar.Controls.Add(searchField, 0, 0);
            reset = CreateButton("重置", 50);
            reset.Dock = DockStyle.Fill;
            reset.Font = rowFont;
            reset.BackColor = FluentStyle.Surface;
            reset.Margin = Padding.Empty;
            reset.Click += delegate { search.Clear(); kindTabs[0].Checked = true; ScheduleFilter(); };
            toolbar.Controls.Add(reset, 1, 0);
            layout.Controls.Add(toolbar, 0, 3);

            status.Dock = DockStyle.Fill;
            status.Margin = Padding.Empty;
            status.Font = smallFont;
            status.ForeColor = ink;
            status.UseCompatibleTextRendering = true;
            status.AutoEllipsis = true;
            status.Visible = false;
            layout.Controls.Add(status, 0, 4);
            compactHeader.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            compactHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            compactHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 558));
            compactHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            compactActions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
                Margin = Padding.Empty, BackColor = BackColor };
            compactActions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            foreach (int width in new[] { 248, 256, 54 })
                compactActions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width));
            compactHeader.Controls.Add(compactActions, 1, 0);
            compactHeader.MouseDown += DragWindow;
        }

        // 底部模式将搜索、标签和重置作为一组水平居中，窗口操作在右侧垂直居中。
        private void ArrangeHeader()
        {
            layout.SuspendLayout();
            compactHeader.SuspendLayout();
            reset.BackColor = bottomLayout ? BackColor : FluentStyle.Surface;
            if (bottomLayout)
            {
                compactHeader.Controls.Add(heading, 0, 0);
                compactActions.Controls.Add(searchField, 0, 0);
                compactActions.Controls.Add(tabSurface, 1, 0);
                compactActions.Controls.Add(reset, 2, 0);
                compactHeader.Controls.Add(windowActions, 2, 0);
                layout.Controls.Add(compactHeader, 0, 0);
                toolbar.Visible = false;
                tabSurface.Margin = new Padding(0, 0, 8, 0);
                layout.RowStyles[0].Height = 44;
                layout.RowStyles[1].Height = 0;
                layout.RowStyles[3].Height = 0;
            }
            else
            {
                layout.Controls.Remove(compactHeader);
                layout.Controls.Add(heading, 0, 0);
                heading.Controls.Add(windowActions);
                layout.Controls.Add(tabSurface, 0, 1);
                toolbar.Controls.Add(searchField, 0, 0);
                toolbar.Controls.Add(reset, 1, 0);
                toolbar.Visible = true;
                tabSurface.Margin = new Padding(0, 3, 0, 7);
                layout.RowStyles[0].Height = 32;
                layout.RowStyles[1].Height = 44;
                layout.RowStyles[3].Height = 54;
            }
            compactHeader.ResumeLayout(true);
            layout.ResumeLayout(true);
        }

        // 同步标签选中样式，并通过原有筛选流程更新记录。
        private void SelectKind(RadioButton tab)
        {
            tab.ForeColor = tab.Checked ? accent : muted;
            tab.Invalidate();
            if (!tab.Checked) return;
            selectedKind = (string)tab.Tag;
            ScheduleFilter();
        }

        // 统一轻量按钮的尺寸、边框和悬停样式。
        private Button CreateButton(string text, int width)
        {
            var button = new FluentButton { Text = text, Width = width, Height = 28, Font = smallFont, FlatStyle = FlatStyle.Flat,
                BackColor = BackColor, ForeColor = ink, Cursor = Cursors.Hand, Margin = new Padding(3, 0, 3, 0) };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = FluentStyle.HoverSurface;
            button.FlatAppearance.MouseDownBackColor = FluentStyle.PressedSurface;
            return button;
        }

        // 切换置顶状态，置顶时不因失去焦点而收起。
        private void TogglePin()
        {
            TopMost = !TopMost;
            pinButton.Text = TopMost ? "\uE77A" : "\uE718";
            pinButton.AccessibleName = TopMost ? "取消窗口置顶" : "置顶窗口";
            pinButton.BackColor = TopMost ? FluentStyle.SelectedSurface : BackColor;
            pinButton.ForeColor = TopMost ? accent : muted;
            tips.SetToolTip(pinButton, TopMost ? "取消置顶，失去焦点时自动收起" : "置顶后，失去焦点仍保持显示");
        }

        // 焦点切换完成后判断，避免打开本程序弹出面板时误收起。
        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (IsHandleCreated && !exiting) BeginInvoke(new Action(HideWhenInactive));
        }

        // 外部窗口获得焦点时收起主窗口，后台继续记录。
        private void HideWhenInactive()
        {
            if (!Visible || TopMost || exiting) return;
            foreach (Form owned in OwnedForms) if (owned.Visible) return;
            uint process;
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            NativeMethods.GetWindowThreadProcessId(foreground, out process);
            if (foreground != IntPtr.Zero && process != currentProcessId)
            {
                Hide();
            }
        }

        // 搜索框或记录列表获得焦点时，Esc 均沿用窗口关闭流程收起到托盘。
        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref message, keyData);
        }

        // 无系统标题栏时，通过顶部空白或应用名称拖动窗口。
        private void DragWindow(object sender, MouseEventArgs e)
        {
            if (bottomLayout || e.Button != MouseButtons.Left) return;
            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero);
        }

        // 保留系统窗口框架样式，让 DWM 提供阴影和圆角。
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.Style |= NativeMethods.WS_CAPTION | NativeMethods.WS_THICKFRAME;
                return parameters;
            }
        }

        // 托盘菜单按打开、工具和退出分组，保留原有操作。
        private void BuildTray()
        {
            var menu = new TrayMenu(smallFont);
            menu.Items.Add(new ToolStripLabel("剪贴记录") { ForeColor = muted, TextAlign = ContentAlignment.MiddleLeft });
            trayOpen = new ToolStripMenuItem();
            trayOpen.Click += delegate { OpenHistory(); };
            menu.Items.Add(trayOpen);
            var translationOpen = new ToolStripMenuItem("开发字段翻译") { ShortcutKeyDisplayString = "Alt+2" };
            translationOpen.Click += delegate { OpenTranslation(); };
            menu.Items.Add(translationOpen);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("设置…", null, delegate { OpenSettings(); });
            menu.Items.Add("打开记录文件夹", null, delegate { Process.Start("explorer.exe", store.Root); });
            trayStartup = new ToolStripMenuItem("开机启动") { Checked = AppSettings.StartupEnabled };
            trayStartup.Click += delegate
            {
                try
                {
                    AppSettings.SetStartup(!trayStartup.Checked);
                    trayStartup.Checked = AppSettings.StartupEnabled;
                }
                catch (UnauthorizedAccessException exception) { OpenHistory(); SetStatus("开机启动设置失败：" + exception.Message); }
                catch (IOException exception) { OpenHistory(); SetStatus("开机启动设置失败：" + exception.Message); }
            };
            menu.Items.Add(trayStartup);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate
            {
                Program.WriteDiagnostic("用户点击托盘退出。");
                exiting = true;
                Close();
            });
            menu.Opening += delegate { trayStartup.Checked = AppSettings.StartupEnabled; };
            tray.Icon = Icon;
            tray.ContextMenuStrip = menu;
            tray.Visible = true;
            tray.DoubleClick += delegate { OpenHistory(); };
            UpdateSettingsLabels();
        }

        // 打开独立翻译窗口并聚焦输入框，不读取剪贴板内容。
        private void OpenTranslation()
        {
            if (translationWindow == null || translationWindow.IsDisposed)
                translationWindow = new TranslationForm(store.Root) { Icon = applicationIcon };
            translationWindow.Show();
            if (translationWindow.WindowState == FormWindowState.Minimized)
                translationWindow.WindowState = FormWindowState.Normal;
            translationWindow.Activate();
            NativeMethods.SetForegroundWindow(translationWindow.Handle);
            translationWindow.FocusInput();
        }

        // 从主窗口或托盘打开同一个设置窗口。
        private void OpenSettings()
        {
            if (pasteTimer.Enabled) return;
            OpenHistory();
            using (var dialog = new SettingsForm(settings))
            {
                dialog.ApplySettings = ApplySettings;
                dialog.ClearCache = ClearHistoryCache;
                dialog.Icon = applicationIcon;
                if (bottomLayout) dialog.StartPosition = FormStartPosition.CenterScreen;
                if (dialog.ShowDialog(this) == DialogResult.OK) ClearStatus();
            }
        }

        // 清理本地历史并刷新列表，忽略清理前尚未处理的剪贴板通知。
        private string ClearHistoryCache()
        {
            clipboardRetry.Stop();
            filterTimer.Stop();
            capturedSequence = NativeMethods.GetClipboardSequenceNumber();
            try
            {
                store.Clear();
                return null;
            }
            catch (IOException exception) { return "清理未完成：" + exception.Message; }
            catch (UnauthorizedAccessException exception) { return "清理未完成：" + exception.Message; }
            catch (SerializationException exception) { return "剩余记录读取失败：" + exception.Message; }
            finally
            {
                thumbnails.Clear();
                records.Items.Clear();
                hoveredRecord = ListBox.NoMatches;
                UpdateEmptyState();
                ReloadRecords();
            }
        }

        // 保存配置后应用快捷键、布局及记录上限，刷新清理后的列表。
        private string ApplySettings(AppSettings candidate, bool startup)
        {
            if (candidate.HotkeyModifiers == 1 && candidate.HotkeyKey == (uint)Keys.D2)
                return "Alt+2 已保留给开发字段翻译，请选择其他历史快捷键。";
            bool replaceHotkey = !hotkeyRegistered || candidate.HotkeyKey != settings.HotkeyKey ||
                candidate.HotkeyModifiers != settings.HotkeyModifiers;
            int candidateId = hotkeyId == 1 ? 2 : 1;
            if (replaceHotkey && !NativeMethods.RegisterHotKey(Handle, candidateId, candidate.HotkeyModifiers | 0x4000, candidate.HotkeyKey))
                return "该快捷键已被占用，请选择其他组合。";
            try
            {
                candidate.Save(store.Root);
            }
            catch (IOException exception)
            {
                if (replaceHotkey) NativeMethods.UnregisterHotKey(Handle, candidateId);
                return "配置保存失败：" + exception.Message;
            }
            catch (UnauthorizedAccessException exception)
            {
                if (replaceHotkey) NativeMethods.UnregisterHotKey(Handle, candidateId);
                return "配置保存失败：" + exception.Message;
            }
            if (replaceHotkey)
            {
                if (hotkeyRegistered) NativeMethods.UnregisterHotKey(Handle, hotkeyId);
                hotkeyId = candidateId;
                hotkeyRegistered = true;
            }
            bool changeLayout = settings.DockBottom != candidate.DockBottom;
            bool changeRecordLimit = settings.RecordLimit != candidate.RecordLimit;
            settings = candidate;
            if (changeLayout) ApplyWindowLayout();
            UpdateSettingsLabels();
            if (changeRecordLimit || store.Count > settings.RecordLimit)
            {
                try { store.SetRecordLimit(settings.RecordLimit); }
                catch (IOException exception) { return "设置已保存，记录数量清理未完成：" + exception.Message; }
                catch (UnauthorizedAccessException exception) { return "设置已保存，记录数量清理未完成：" + exception.Message; }
                catch (SerializationException exception) { return "设置已保存，历史日志格式错误：" + exception.Message; }
                finally
                {
                    thumbnails.Clear();
                    ReloadRecords();
                }
            }
            try { AppSettings.SetStartup(startup); }
            catch (UnauthorizedAccessException)
            {
                return "快捷键和合并配置已保存；没有权限修改开机启动。";
            }
            catch (IOException exception) { return "快捷键和合并配置已保存；开机启动设置失败：" + exception.Message; }
            trayStartup.Checked = AppSettings.StartupEnabled;
            return null;
        }

        // 切换底部通栏与普通窗口，关闭开关时恢复原来的位置和大小。
        private void ApplyWindowLayout()
        {
            if (settings.DockBottom)
            {
                if (!bottomLayout) normalBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                bottomLayout = true;
                WindowState = FormWindowState.Normal;
                MinimumSize = Size.Empty;
                PositionBottomWindow();
            }
            else
            {
                bool restore = bottomLayout;
                bottomLayout = false;
                MinimumSize = new Size(520, 420);
                if (restore) Bounds = normalBounds;
            }
            ArrangeHeader();
            records.SetHorizontalCards(bottomLayout);
            previewLines.Clear();
            UpdateEmptyState();
        }

        // 固定到当前屏幕工作区底部，左、右、下各保留 20px 间隙。
        private void PositionBottomWindow()
        {
            Rectangle area = Screen.FromControl(this).WorkingArea;
            int height = Math.Min(320, area.Height - 20);
            var bounds = new Rectangle(area.Left + 20, area.Bottom - height - 20, area.Width - 40, height);
            if (Bounds != bounds) Bounds = bounds;
        }

        // 在托盘展示用户配置的快捷键，主窗口不显示常驻提示。
        private void UpdateSettingsLabels()
        {
            trayOpen.Text = "打开剪贴板";
            trayOpen.ShortcutKeyDisplayString = settings.HotkeyText;
            tray.Text = "剪贴记录 · " + settings.HotkeyText;
        }

        // 为主窗口和已创建的背景容器同步系统毛玻璃底色。
        private void ApplyWindowBackground()
        {
            Color background = NativeMethods.ApplyAcrylicBackground(Handle, !SystemInformation.HighContrast) ?
                FluentStyle.GlassBackground : FluentStyle.Background;
            Color previous = BackColor;
            if (previous == background) return;
            UpdateBackgroundColors(this, previous, background);
            UpdateBackgroundColors(compactHeader, previous, background);
            Invalidate(true);
        }

        // 只更新使用窗口底色的控件，统一普通布局和底部布局的合成背景。
        private void UpdateBackgroundColors(Control control, Color previous, Color background)
        {
            if (control.BackColor == previous) control.BackColor = background;
            foreach (Control child in control.Controls) UpdateBackgroundColors(child, previous, background);
        }

        // 初始化系统窗口效果、剪贴板通知和全局快捷键。
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeMethods.ApplyWindowChrome(Handle);
            ApplyWindowBackground();
            NativeMethods.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0037);
            if (!NativeMethods.AddClipboardFormatListener(Handle))
                throw new InvalidOperationException("无法注册剪贴板监听。");
            translationHotkeyRegistered = NativeMethods.RegisterHotKey(Handle, TranslationHotkeyId, 0x4001, (uint)Keys.D2);
            if (!translationHotkeyRegistered)
                tray.ShowBalloonTip(5000, "开发字段翻译", "Alt+2 已被占用，可从托盘菜单打开开发字段翻译。", ToolTipIcon.Warning);
            hotkeyRegistered = NativeMethods.RegisterHotKey(Handle, hotkeyId, settings.HotkeyModifiers | 0x4000, settings.HotkeyKey);
            if (!hotkeyRegistered) SetStatus(settings.HotkeyText + " 已被占用，请从设置中更换快捷键。");
        }

        // 隐藏系统标题栏，保留 DWM 框架并处理剪贴板和快捷键通知。
        protected override void WndProc(ref Message message)
        {
            if (bottomLayout && message.Msg == 0x0112 && (message.WParam.ToInt64() & 0xFFF0) == 0xF030)
            {
                message.Result = IntPtr.Zero;
                return;
            }
            if (message.Msg == NativeMethods.WM_NCCALCSIZE && message.WParam != IntPtr.Zero)
            {
                message.Result = IntPtr.Zero;
                return;
            }
            if (message.Msg == NativeMethods.WM_DWMCOMPOSITIONCHANGED || message.Msg == 0x031A || message.Msg == 0x001A)
            {
                NativeMethods.ApplyWindowChrome(Handle);
                ApplyWindowBackground();
            }
            if (message.Msg == 0x84)
            {
                base.WndProc(ref message);
                if (bottomLayout)
                {
                    message.Result = new IntPtr(1);
                    return;
                }
                if (message.Result == new IntPtr(1))
                {
                    long coordinates = message.LParam.ToInt64();
                    Point point = PointToClient(new Point((short)(coordinates & 0xffff), (short)((coordinates >> 16) & 0xffff)));
                    const int grip = 8;
                    bool left = point.X < grip, right = point.X >= ClientSize.Width - grip;
                    bool top = point.Y < grip, bottom = point.Y >= ClientSize.Height - grip;
                    int hit = top && left ? 13 : top && right ? 14 : bottom && left ? 16 : bottom && right ? 17 :
                        left ? 10 : right ? 11 : top ? 12 : bottom ? 15 : 1;
                    message.Result = new IntPtr(hit);
                }
                return;
            }
            if (message.Msg == NativeMethods.WM_CLIPBOARDUPDATE)
            {
                retries = 0;
                CaptureClipboard();
            }
            else if (message.Msg == NativeMethods.WM_HOTKEY && message.WParam.ToInt32() == TranslationHotkeyId)
                OpenTranslation();
            else if ((message.Msg == NativeMethods.WM_HOTKEY && message.WParam.ToInt32() == hotkeyId) ||
                message.Msg == NativeMethods.ShowHistoryMessage)
                OpenHistory();
            base.WndProc(ref message);
        }

        // 记住最近的外部窗口，打开历史后仍保留粘贴目标。
        private void TrackTarget()
        {
            if (bottomLayout && WindowState != FormWindowState.Minimized)
            {
                if (WindowState == FormWindowState.Maximized) WindowState = FormWindowState.Normal;
                PositionBottomWindow();
            }
            IntPtr window = NativeMethods.GetForegroundWindow();
            uint process;
            NativeMethods.GetWindowThreadProcessId(window, out process);
            if (window != IntPtr.Zero && process != currentProcessId && NativeMethods.IsWindowVisible(window))
                targetWindow = window;
            HideWhenInactive();
        }

        // 显示历史并让搜索框获得焦点。
        private void OpenHistory()
        {
            if (pasteTimer.Enabled) return;
            TrackTarget();
            Show();
            WindowState = FormWindowState.Normal;
            if (bottomLayout) PositionBottomWindow();
            Activate();
            search.Focus();
        }

        // 捕获常用剪贴板格式；剪贴板被其他进程占用时短暂重试。
        private void CaptureClipboard()
        {
            clipboardRetry.Stop();
            if (writingClipboard) return;
            uint sequence = NativeMethods.GetClipboardSequenceNumber();
            if (sequence == 0 || sequence == capturedSequence || sequence == ownSequence) return;
            try
            {
                IDataObject data = Clipboard.GetDataObject();
                if (data == null) return;
                IntPtr sourceWindow = NativeMethods.GetForegroundWindow();
                var source = new StringBuilder(256);
                NativeMethods.GetWindowText(sourceWindow, source, source.Capacity);
                var record = new ClipRecord { Id = Guid.NewGuid().ToString("N"), CapturedAt = DateTime.Now.ToString("o"),
                    Source = source.ToString(), SourceExecutable = NativeMethods.GetWindowExecutable(sourceWindow) };
                Bitmap image = null;
                try
                {
                    if (data.GetDataPresent(DataFormats.FileDrop))
                    {
                        record.Kind = "文件";
                        record.Files = (string[])data.GetData(DataFormats.FileDrop);
                        record.Text = string.Join(Environment.NewLine, record.Files);
                    }
                    else if (data.GetDataPresent(DataFormats.UnicodeText))
                    {
                        record.Kind = "文本";
                        record.Text = (string)data.GetData(DataFormats.UnicodeText);
                        if (data.GetDataPresent(DataFormats.Html)) record.Html = (string)data.GetData(DataFormats.Html);
                        if (data.GetDataPresent(DataFormats.Rtf)) record.Rtf = (string)data.GetData(DataFormats.Rtf);
                    }
                    else if (data.GetDataPresent(DataFormats.Bitmap))
                    {
                        record.Kind = "图片";
                        image = new Bitmap((Image)data.GetData(DataFormats.Bitmap));
                        record.Text = "图片  " + image.Width + " × " + image.Height;
                    }
                    else
                    {
                        capturedSequence = sequence;
                        SetStatus("本次复制的专有格式暂不支持；可记录文本、图片和文件列表。");
                        return;
                    }
                    if (NativeMethods.GetClipboardSequenceNumber() != sequence)
                    {
                        clipboardRetry.Start();
                        return;
                    }
                    store.Append(record, image, settings.DuplicateSeconds);
                    capturedSequence = sequence;
                    if (!filterTimer.Enabled) ReloadRecords(true);
                    else ClearStatus();
                }
                finally { if (image != null) image.Dispose(); }
            }
            catch (ExternalException)
            {
                if (++retries <= 8) clipboardRetry.Start();
                else SetStatus("剪贴板持续被占用，本次记录未能保存。");
            }
            catch (IOException exception) { SetStatus("记录保存失败：" + exception.Message); }
            catch (UnauthorizedAccessException exception) { SetStatus("记录保存失败：" + exception.Message); }
        }

        // 延迟刷新筛选，避免输入每个字符都读取日志。
        private void ScheduleFilter()
        {
            filterTimer.Stop();
            filterTimer.Start();
        }

        // 在当前保留的历史中筛选，实时复制时保留当前滚动位置。
        private void ReloadRecords(bool preservePosition = false)
        {
            try
            {
                filteredRecords = store.Read(search.Text.Trim(), selectedKind);
                ShowRecords(preservePosition);
                ClearStatus();
            }
            catch (IOException exception) { SetStatus("读取记录失败：" + exception.Message); }
            catch (SerializationException exception) { SetStatus("历史日志格式损坏：" + exception.Message); }
        }

        // 刷新筛选结果时按像素恢复滚动位置，保留部分可见的卡片。
        private void ShowRecords(bool preservePosition)
        {
            int scrollPosition = preservePosition ? records.ScrollPosition : 0;
            var selected = preservePosition ? records.SelectedItem as ClipRecord : null;
            records.BeginUpdate();
            records.Items.Clear();
            previewLines.Clear();
            hoveredRecord = ListBox.NoMatches;
            hoveredAction = RecordAction.None;
            var idleAnimations = new List<string>();
            foreach (var entry in recordAnimations)
                if (entry.Value.DeletingRecord == null) idleAnimations.Add(entry.Key);
            foreach (string id in idleAnimations) recordAnimations.Remove(id);
            tips.SetToolTip(records, "");
            records.Items.AddRange(filteredRecords.ToArray());
            if (selected != null)
                records.SelectedIndex = filteredRecords.FindIndex(item => item.Id == selected.Id);
            records.ScrollPosition = scrollPosition;
            records.EndUpdate();
            UpdateEmptyState();
            if (Visible) TrackHoveredRecord(records.PointToClient(Control.MousePosition));
        }

        // 鼠标移动或列表滚动后，按实际卡片区域更新悬停状态。
        private void TrackHoveredRecord(Point location)
        {
            int index = records.IndexFromPoint(location);
            if (index != ListBox.NoMatches &&
                (IsDeletingRecord((ClipRecord)records.Items[index]) ||
                !Rectangle.Inflate(records.GetItemRectangle(index), -OverlayListBox.CardMargin, -OverlayListBox.CardMargin).Contains(location)))
                index = ListBox.NoMatches;
            RecordAction action = index == ListBox.NoMatches ? RecordAction.None :
                HitRecordAction(records.GetItemRectangle(index), location);
            UpdateHoveredRecord(index, action);
        }

        // 更新悬停目标和按钮提示，通过动画平滑显示或隐藏卡片操作。
        private void UpdateHoveredRecord(int index, RecordAction action)
        {
            if (index == hoveredRecord && action == hoveredAction) return;
            int previous = hoveredRecord;
            if (previous != index)
            {
                if (previous >= 0 && previous < records.Items.Count)
                    GetRecordAnimation((ClipRecord)records.Items[previous]).TargetHover = 0;
                if (index != ListBox.NoMatches)
                    GetRecordAnimation((ClipRecord)records.Items[index]).TargetHover = 1;
                StartRecordAnimation();
            }
            hoveredRecord = index;
            hoveredAction = action;
            string hint = index == ListBox.NoMatches ? "" : "右键打开记录菜单";
            if (action == RecordAction.Preview) hint = "预览完整内容";
            else if (action == RecordAction.Delete) hint = "删除记录（Delete）";
            else if (action == RecordAction.Pin)
                hint = ((ClipRecord)records.Items[index]).IsPinned ? "取消置顶（Ctrl+P）" : "置顶记录（Ctrl+P）";
            tips.SetToolTip(records, hint);
            foreach (int row in new[] { previous, index })
            {
                if (row < 0 || row >= records.Items.Count) continue;
                Rectangle bounds = Rectangle.Intersect(records.GetItemRectangle(row), records.ClientRectangle);
                if (bounds.IsEmpty) continue;
                NativeMethods.InvalidateWithoutErase(records.Handle, bounds);
            }
        }

        // 按记录 ID 保存过渡状态，刷新和排序后仍能继续完成删除。
        private RecordAnimation GetRecordAnimation(ClipRecord record)
        {
            RecordAnimation animation;
            if (!recordAnimations.TryGetValue(record.Id, out animation))
            {
                animation = new RecordAnimation();
                recordAnimations.Add(record.Id, animation);
            }
            return animation;
        }

        // 删除动画期间暂停该记录的重复操作。
        private bool IsDeletingRecord(ClipRecord record)
        {
            RecordAnimation animation;
            return recordAnimations.TryGetValue(record.Id, out animation) && animation.DeletingRecord != null;
        }

        // 列表按同一删除进度收起占位，绘制和点击位置保持一致。
        private float GetRecordRemovalProgress(int index)
        {
            RecordAnimation animation;
            return recordAnimations.TryGetValue(((ClipRecord)records.Items[index]).Id, out animation) ? animation.DeleteProgress : 0;
        }

        // 仅在存在过渡时启动计时，静止卡片不持续重绘。
        private void StartRecordAnimation()
        {
            if (animationTimer.Enabled) return;
            lastAnimationFrame = animationClock.ElapsedMilliseconds;
            animationTimer.Start();
        }

        // 推进悬停、删除和相邻卡片位移，消失后执行实际删除。
        private void AnimateRecords()
        {
            long now = animationClock.ElapsedMilliseconds;
            float step = (now - lastAnimationFrame) / (float)HoverTransitionDuration;
            lastAnimationFrame = now;
            var idle = new List<string>();
            var completed = new List<string>();
            foreach (var entry in recordAnimations)
            {
                RecordAnimation animation = entry.Value;
                animation.Hover = animation.Hover < animation.TargetHover ?
                    Math.Min(animation.TargetHover, animation.Hover + step) : Math.Max(animation.TargetHover, animation.Hover - step);
                if (animation.DeletingRecord != null)
                {
                    float progress = Math.Min(1, (now - animation.DeleteStarted) / (float)DeleteTransitionDuration);
                    animation.DeleteProgress = EaseTransition(progress);
                    if (progress == 1) completed.Add(entry.Key);
                }
                else if (animation.Hover == 0 && animation.TargetHover == 0) idle.Add(entry.Key);
            }
            foreach (string id in idle) recordAnimations.Remove(id);
            string deletionError = null;
            foreach (string id in completed)
            {
                ClipRecord record = recordAnimations[id].DeletingRecord;
                recordAnimations.Remove(id);
                string error = CompleteRecordDeletion(record);
                if (error != null) deletionError = error;
            }
            records.RefreshAnimationLayout();
            if (deletionError != null) SetStatus(deletionError);
            if (Visible) TrackHoveredRecord(records.PointToClient(Control.MousePosition));
            bool running = false;
            foreach (RecordAnimation animation in recordAnimations.Values)
            {
                if (animation.DeletingRecord != null || animation.Hover != animation.TargetHover)
                {
                    running = true;
                    break;
                }
            }
            if (!running) animationTimer.Stop();
        }

        // 首尾速度平缓的缓动曲线，供颜色、按钮和删除动画共用。
        private static float EaseTransition(float progress)
        {
            return progress * progress * (3 - 2 * progress);
        }

        // 在两种颜色之间连续插值，避免悬停时直接跳变。
        private static Color BlendColor(Color from, Color to, float progress)
        {
            return Color.FromArgb((int)Math.Round(from.R + (to.R - from.R) * progress),
                (int)Math.Round(from.G + (to.G - from.G) * progress),
                (int)Math.Round(from.B + (to.B - from.B) * progress));
        }

        // 缓冲整张卡片，删除时围绕中心缩放并淡出。
        private void DrawRecord(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            if (rowBuffer == null || rowBuffer.Size != e.Bounds.Size)
            {
                if (rowBuffer != null) rowBuffer.Dispose();
                // 行缓冲保留 Alpha，使卡片间隙和阴影参与系统背景合成。
                rowBuffer = new Bitmap(e.Bounds.Width, e.Bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                rowBuffer.SetResolution(e.Graphics.DpiX, e.Graphics.DpiY);
            }
            using (var graphics = Graphics.FromImage(rowBuffer))
            {
                FluentStyle.ClearBackground(graphics, records.BackColor);
                var bufferedArgs = new DrawItemEventArgs(graphics, e.Font,
                    new Rectangle(Point.Empty, e.Bounds.Size), e.Index, e.State, e.ForeColor, e.BackColor);
                PaintRecord(bufferedArgs);
            }
            RecordAnimation animation;
            float removal = recordAnimations.TryGetValue(((ClipRecord)records.Items[e.Index]).Id, out animation) ? animation.DeleteProgress : 0;
            if (removal == 0)
                e.Graphics.DrawImageUnscaled(rowBuffer, e.Bounds.Location);
            else if (removal < 1)
            {
                float scale = 1 - removal;
                var destination = new RectangleF(e.Bounds.X + e.Bounds.Width * removal / 2,
                    e.Bounds.Y + e.Bounds.Height * removal / 2, e.Bounds.Width * scale, e.Bounds.Height * scale);
                using (var attributes = new ImageAttributes())
                {
                    GraphicsState state = e.Graphics.Save();
                    e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    attributes.SetColorMatrix(new ColorMatrix { Matrix33 = scale });
                    PointF[] corners = { new PointF(destination.Left, destination.Top),
                        new PointF(destination.Right, destination.Top), new PointF(destination.Left, destination.Bottom) };
                    e.Graphics.DrawImage(rowBuffer, corners, new RectangleF(0, 0, rowBuffer.Width, rowBuffer.Height), GraphicsUnit.Pixel, attributes);
                    e.Graphics.Restore(state);
                }
            }
        }

        // 悬停以蓝色阴影、来源栏加深和内容高亮突出卡片。
        private void PaintRecord(DrawItemEventArgs e)
        {
            var record = (ClipRecord)records.Items[e.Index];
            bool selected = (e.State & DrawItemState.Selected) != 0;
            RecordAnimation animation;
            recordAnimations.TryGetValue(record.Id, out animation);
            float hover = animation == null ? 0 : EaseTransition(animation.Hover);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            Rectangle card = Rectangle.Inflate(e.Bounds, -OverlayListBox.CardMargin, -OverlayListBox.CardMargin);
            for (int spread = 5; spread >= 0; spread--)
            {
                Rectangle shadow = Rectangle.Inflate(card, spread, spread);
                shadow.Offset(0, 2);
                using (var path = FluentStyle.RoundedRectangle(shadow, RecordCardRadius + spread))
                using (var brush = new SolidBrush(Color.FromArgb((int)Math.Round(7 - spread + (8 - spread) * hover), BlendColor(ink, accent, hover))))
                    e.Graphics.FillPath(brush, path);
            }
            using (var path = FluentStyle.RoundedRectangle(card, RecordCardRadius))
            using (var brush = new SolidBrush(selected ? FluentStyle.PressedSurface :
                BlendColor(FluentStyle.Surface, FluentStyle.SelectedSurface, hover)))
            {
                e.Graphics.FillPath(brush, path);
                GraphicsState state = e.Graphics.Save();
                e.Graphics.SetClip(path, CombineMode.Intersect);
                DrawRecordHeader(e.Graphics, card, record, hover);
                if (record.Kind == "图片")
                {
                    DrawImageRecord(e.Graphics, card, record);
                    if (selected || hover > 0)
                        using (var selection = new SolidBrush(Color.FromArgb((int)Math.Round(selected ? 48 + 12 * hover : 36 * hover), accent)))
                            e.Graphics.FillRectangle(selection, ImageBounds(card));
                }
                else
                    DrawPreviewText(e.Graphics, record.Preview, bottomLayout ?
                        new Rectangle(card.X + 14, card.Y + recordHeaderHeight + 12, card.Width - 28, card.Height - recordHeaderHeight - 58) :
                        new Rectangle(card.X + 14, card.Y + recordHeaderHeight + 12, card.Width - 148, card.Height - recordHeaderHeight - 26));
                e.Graphics.Restore(state);
            }
            if (hover > 0 && (animation == null || animation.DeletingRecord == null))
                for (RecordAction action = RecordAction.Preview; action <= RecordAction.Pin; action++)
                    DrawRecordAction(e, action, record, hover);
        }

        // 来源栏展示程序图标、类型、时间和置顶状态，悬停时同步加深底色。
        private void DrawRecordHeader(Graphics graphics, Rectangle card, ClipRecord record, float hover)
        {
            Color color = record.Kind == "图片" ? Color.FromArgb(208, 51, 69) :
                record.Kind == "文件" ? Color.FromArgb(0, 118, 178) : accent;
            color = BlendColor(color, ControlPaint.Dark(color, 0.08f), hover);
            using (var brush = new SolidBrush(color))
                graphics.FillRectangle(brush, card.X, card.Y, card.Width, recordHeaderHeight);
            Icon icon = GetSourceIcon(record);
            var iconArea = new Rectangle(card.Right - recordHeaderHeight, card.Top, recordHeaderHeight, recordHeaderHeight);
            using (var brush = new SolidBrush(Color.FromArgb(36, Color.White))) graphics.FillRectangle(brush, iconArea);
            Rectangle iconPlate = Rectangle.Inflate(iconArea, -4, -4);
            using (var path = FluentStyle.RoundedRectangle(iconPlate, 12))
            using (var brush = new SolidBrush(Color.FromArgb(245, Color.White))) graphics.FillPath(brush, path);
            int size = Math.Min(iconPlate.Width - 8, (int)Math.Round(40 * graphics.DpiX / 96f));
            graphics.DrawIcon(icon, new Rectangle(iconArea.X + (iconArea.Width - size) / 2,
                iconArea.Y + (iconArea.Height - size) / 2, size, size));
            int textWidth = card.Width - 28 - recordHeaderHeight;
            using (var format = new StringFormat { FormatFlags = StringFormatFlags.NoWrap,
                LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter })
            using (var titleBrush = new SolidBrush(Color.White))
            using (var detailBrush = new SolidBrush(Color.FromArgb(230, Color.White)))
            {
                int titleHeight = (int)Math.Ceiling(rowFont.GetHeight(graphics)) + 2;
                int detailHeight = (int)Math.Ceiling(smallFont.GetHeight(graphics)) + 2;
                graphics.DrawString(string.IsNullOrEmpty(record.Source) ? record.Kind : record.Source,
                    rowFont, titleBrush, new Rectangle(card.X + 14, card.Y + 6, textWidth, titleHeight), format);
                graphics.DrawString((record.IsPinned ? "已置顶 · " : "") + record.Kind + " · " + record.LocalTime.ToString("HH:mm"),
                    smallFont, detailBrush, new Rectangle(card.X + 14, card.Y + titleHeight + 8, textWidth, detailHeight), format);
            }
        }

        // 旧记录通过同名窗口补取程序图标，无法识别时显示通用应用图标。
        private Icon GetSourceIcon(ClipRecord record)
        {
            string executable = record.SourceExecutable;
            string key = string.IsNullOrEmpty(executable) ? "window:" + record.Source : executable;
            Icon icon;
            if (sourceIcons.TryGetValue(key, out icon)) return icon;
            if (sourceIcons.Count == 48)
            {
                string oldest = sourceIconOrder.Dequeue();
                sourceIcons[oldest].Dispose();
                sourceIcons.Remove(oldest);
            }
            if (string.IsNullOrEmpty(executable) && !string.IsNullOrEmpty(record.Source))
                executable = NativeMethods.GetWindowExecutable(NativeMethods.FindWindow(null, record.Source));
            icon = string.IsNullOrEmpty(executable) ? null : NativeMethods.LoadApplicationIcon(executable);
            if (icon == null) icon = (Icon)SystemIcons.Application.Clone();
            sourceIcons.Add(key, icon);
            sourceIconOrder.Enqueue(key);
            return icon;
        }

        // 悬停按钮统一位于右下角，普通列表的文本为按钮预留右侧空间。
        private static Rectangle RecordActionBounds(Rectangle row, RecordAction action)
        {
            Rectangle card = Rectangle.Inflate(row, -OverlayListBox.CardMargin, -OverlayListBox.CardMargin);
            return new Rectangle(card.Right - 116 + (int)action * 36, card.Bottom - 44, 32, 32);
        }

        // 点击与绘制使用同一组预览、删除和置顶按钮区域。
        private static RecordAction HitRecordAction(Rectangle row, Point location)
        {
            for (RecordAction action = RecordAction.Preview; action <= RecordAction.Pin; action++)
                if (RecordActionBounds(row, action).Contains(location)) return action;
            return RecordAction.None;
        }

        // 图标按钮随悬停淡入淡出，置顶和删除使用各自的状态颜色。
        private void DrawRecordAction(DrawItemEventArgs e, RecordAction action, ClipRecord record, float opacity)
        {
            Rectangle bounds = RecordActionBounds(e.Bounds, action);
            bool hovered = hoveredRecord == e.Index && hoveredAction == action;
            bool pinned = action == RecordAction.Pin && record.IsPinned;
            Color fill = pinned ? FluentStyle.SelectedSurface : hovered ? FluentStyle.ControlFill : FluentStyle.Surface;
            Color color = pinned ? accent : ink;
            if (action == RecordAction.Delete)
            {
                color = Color.FromArgb(190, 45, 45);
                if (hovered) fill = Color.FromArgb(255, 235, 235);
            }
            using (var path = FluentStyle.RoundedRectangle(bounds, 9))
            using (var brush = new SolidBrush(Color.FromArgb((int)Math.Round(245 * opacity), fill)))
                e.Graphics.FillPath(brush, path);
            RecordMenuIcon icon = action == RecordAction.Preview ? RecordMenuIcon.Preview :
                action == RecordAction.Delete ? RecordMenuIcon.Delete : pinned ? RecordMenuIcon.Unpin : RecordMenuIcon.Pin;
            int size = Math.Min(24, (int)Math.Round(20 * e.Graphics.DpiX / 96f));
            var iconBounds = new Rectangle(bounds.X + (bounds.Width - size) / 2, bounds.Y + (bounds.Height - size) / 2, size, size);
            RecordMenu.DrawIcon(e.Graphics, iconBounds, icon, Color.FromArgb((int)Math.Round(255 * opacity), color));
        }

        // 普通模式最多展示两行正文，缓存排版并在末行超出时显示省略号。
        private void DrawPreviewText(Graphics graphics, string text, Rectangle bounds)
        {
            int textHeight = (int)Math.Ceiling(contentFont.GetHeight(graphics));
            int gap = (int)Math.Ceiling(4 * graphics.DpiY / 96f);
            int lineHeight = textHeight + gap;
            if (previewSize != bounds.Size || previewLineHeight != lineHeight)
            {
                previewLines.Clear();
                previewSize = bounds.Size;
                previewLineHeight = lineHeight;
            }
            string[] visibleLines;
            if (!previewLines.TryGetValue(text, out visibleLines))
            {
                var linesToDraw = new List<string>();
                int lineCount = (bounds.Height + gap) / lineHeight;
                if (!bottomLayout) lineCount = Math.Min(2, lineCount);
                int offset = 0;
                using (var measureFormat = new StringFormat { FormatFlags = StringFormatFlags.LineLimit })
                {
                    for (int line = 0; line < lineCount && offset < text.Length; line++)
                    {
                        int fitted, lines;
                        graphics.MeasureString(text.Substring(offset), contentFont,
                            new SizeF(bounds.Width, textHeight), measureFormat, out fitted, out lines);
                        if (fitted == 0) break;
                        string visibleText = text.Substring(offset, fitted).TrimEnd('\r', '\n');
                        offset += fitted;
                        if (line == lineCount - 1 && offset < text.Length) visibleText = visibleText.TrimEnd() + "…";
                        linesToDraw.Add(visibleText);
                    }
                }
                visibleLines = linesToDraw.ToArray();
                previewLines.Add(text, visibleLines);
            }
            using (var drawFormat = new StringFormat { FormatFlags = StringFormatFlags.NoWrap,
                Trimming = StringTrimming.EllipsisCharacter })
            using (var brush = new SolidBrush(ink))
            {
                for (int line = 0; line < visibleLines.Length; line++)
                {
                    graphics.DrawString(visibleLines[line], contentFont, brush,
                        new RectangleF(bounds.X, bounds.Y + line * lineHeight, bounds.Width, textHeight), drawFormat);
                }
            }
        }

        // 来源栏下方的整块区域用于图片内容。
        private Rectangle ImageBounds(Rectangle card)
        {
            return new Rectangle(card.X, card.Y + recordHeaderHeight, card.Width, card.Height - recordHeaderHeight);
        }

        // 图片居中裁切铺满内容区，尺寸说明以半透明浮层显示。
        private void DrawImageRecord(Graphics graphics, Rectangle card, ClipRecord record)
        {
            Rectangle preview = ImageBounds(card);
            Bitmap thumbnail = thumbnails.Get(record);
            using (var brush = new SolidBrush(FluentStyle.Background)) graphics.FillRectangle(brush, preview);
            if (thumbnail != null)
            {
                float scale = Math.Max(preview.Width / (float)thumbnail.Width, preview.Height / (float)thumbnail.Height);
                float sourceWidth = preview.Width / scale, sourceHeight = preview.Height / scale;
                var source = new RectangleF((thumbnail.Width - sourceWidth) / 2,
                    (thumbnail.Height - sourceHeight) / 2, sourceWidth, sourceHeight);
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(thumbnail, (RectangleF)preview, source, GraphicsUnit.Pixel);
            }
            string detail = thumbnail == null ? "缩略图不可用" : record.Preview.Replace("图片", "").Trim();
            var caption = new Rectangle(preview.Left + 8, preview.Top + 8,
                Math.Min(preview.Width - 16, (int)Math.Ceiling(graphics.MeasureString(detail, smallFont).Width) + 12),
                (int)Math.Ceiling(smallFont.GetHeight(graphics)) + 8);
            using (var path = FluentStyle.RoundedRectangle(caption, 8))
            using (var brush = new SolidBrush(Color.FromArgb(170, 24, 32, 44))) graphics.FillPath(brush, path);
            caption.Inflate(-6, 0);
            using (var format = new StringFormat { FormatFlags = StringFormatFlags.NoWrap,
                LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter })
            using (var detailBrush = new SolidBrush(Color.White))
            {
                graphics.DrawString(detail, smallFont, detailBrush, caption, format);
            }
        }

        // 左键按按钮区域执行操作或粘贴内容，右键打开记录菜单。
        private void RecordMouseUp(object sender, MouseEventArgs e)
        {
            int index = records.IndexFromPoint(e.Location);
            if (index == ListBox.NoMatches) return;
            records.SelectedIndex = index;
            var record = (ClipRecord)records.Items[index];
            if (IsDeletingRecord(record)) return;
            if (e.Button == MouseButtons.Right)
                ShowRecordMenu(record, e.Location);
            else if (e.Button == MouseButtons.Left)
            {
                RecordAction action = hoveredRecord == index ?
                    HitRecordAction(records.GetItemRectangle(index), e.Location) : RecordAction.None;
                if (action == RecordAction.Preview) PreviewRecord(record);
                else if (action == RecordAction.Delete) DeleteRecord(record);
                else if (action == RecordAction.Pin) ToggleRecordPin(record);
                else PasteRecord(record);
            }
        }

        // 右键菜单集中提供查看、复制、粘贴、置顶和删除操作。
        private void ShowRecordMenu(ClipRecord record, Point location)
        {
            if (IsDeletingRecord(record)) return;
            var menu = new RecordMenu(rowFont);
            menu.AddAction("查看完整内容", RecordMenuIcon.Preview, delegate { PreviewRecord(record); });
            menu.AddAction("复制到剪贴板", RecordMenuIcon.Copy, delegate { if (WriteClipboard(record)) ClearStatus(); });
            menu.AddAction("粘贴到目标窗口", RecordMenuIcon.Paste, delegate { PasteRecord(record); });
            menu.Items.Add(new ToolStripSeparator());
            menu.AddAction(record.IsPinned ? "取消置顶" : "置顶记录", record.IsPinned ? RecordMenuIcon.Unpin : RecordMenuIcon.Pin,
                delegate { ToggleRecordPin(record); });
            menu.AddAction("删除记录", RecordMenuIcon.Delete, delegate { DeleteRecord(record); });
            menu.Closed += delegate { BeginInvoke(new Action(menu.Dispose)); };
            menu.Show(records, location, ToolStripDropDownDirection.BelowLeft);
        }

        // 保存记录置顶状态并刷新当前筛选结果的顺序。
        private void ToggleRecordPin(ClipRecord record)
        {
            if (IsDeletingRecord(record)) return;
            try
            {
                store.SetPinned(record, !record.IsPinned);
                ReloadRecords();
            }
            catch (IOException exception) { SetStatus("置顶状态保存失败：" + exception.Message); }
            catch (UnauthorizedAccessException exception) { SetStatus("置顶状态保存失败：" + exception.Message); }
            catch (SerializationException exception) { SetStatus("历史日志格式损坏：" + exception.Message); }
        }

        // 先缩放淡出卡片，动画结束后再删除该记录。
        private void DeleteRecord(ClipRecord record)
        {
            if (IsDeletingRecord(record)) return;
            RecordAnimation animation = GetRecordAnimation(record);
            animation.DeletingRecord = record;
            animation.DeleteStarted = animationClock.ElapsedMilliseconds;
            animation.TargetHover = 0;
            StartRecordAnimation();
            records.Invalidate();
        }

        // 实际删除后刷新当前位置，保存失败时由现有日志恢复可见记录。
        private string CompleteRecordDeletion(ClipRecord record)
        {
            string error = null;
            try { store.Delete(record); }
            catch (IOException exception) { error = "删除未完成：" + exception.Message; }
            catch (UnauthorizedAccessException exception) { error = "删除未完成：" + exception.Message; }
            catch (SerializationException exception) { error = "历史日志格式损坏：" + exception.Message; }
            finally
            {
                thumbnails.Clear();
                ReloadRecords(true);
            }
            return error;
        }

        // 展示完整文本、文件路径或图片，Esc 关闭预览窗口。
        private void PreviewRecord(ClipRecord record)
        {
            if (IsDeletingRecord(record)) return;
            using (var preview = new Form { Text = record.Kind + " · " + record.LocalTime.ToString("yyyy-MM-dd HH:mm:ss"),
                Size = new Size(600, 440), StartPosition = FormStartPosition.CenterParent, Font = contentFont,
                Icon = applicationIcon, KeyPreview = true })
            {
                preview.KeyDown += delegate(object sender, KeyEventArgs e)
                {
                    if (e.KeyCode != Keys.Escape) return;
                    e.Handled = e.SuppressKeyPress = true;
                    preview.Close();
                };
                var details = new Label { Text = record.Source, Dock = DockStyle.Top, Height = 32,
                    Padding = new Padding(10, 6, 10, 0), Font = smallFont, ForeColor = muted, AutoEllipsis = true };
                if (record.Kind == "图片")
                {
                    using (var image = store.LoadImage(record))
                    {
                        preview.Controls.Add(new PictureBox { Dock = DockStyle.Fill, Image = image, SizeMode = PictureBoxSizeMode.Zoom });
                        preview.Controls.Add(details);
                        preview.ShowDialog(this);
                    }
                }
                else
                {
                    preview.Controls.Add(new TextBox { Text = record.Text, Dock = DockStyle.Fill, Multiline = true,
                        ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, BorderStyle = BorderStyle.None });
                    preview.Controls.Add(details);
                    preview.ShowDialog(this);
                }
            }
        }

        // 恢复记录格式，并忽略本程序引发的剪贴板通知。
        private bool WriteClipboard(ClipRecord record)
        {
            writingClipboard = true;
            try
            {
                var data = new DataObject();
                if (record.Kind == "文件")
                {
                    foreach (string path in record.Files)
                    {
                        if (!File.Exists(path) && !Directory.Exists(path))
                        {
                            SetStatus("文件已不存在：" + path);
                            return false;
                        }
                    }
                    var files = new StringCollection();
                    files.AddRange(record.Files);
                    data.SetFileDropList(files);
                    Clipboard.SetDataObject(data, true, 8, 60);
                }
                else if (record.Kind == "图片")
                {
                    using (var image = store.LoadImage(record))
                    {
                        data.SetImage(image);
                        Clipboard.SetDataObject(data, true, 8, 60);
                    }
                }
                else
                {
                    data.SetData(DataFormats.UnicodeText, record.Text);
                    if (record.Html != null) data.SetData(DataFormats.Html, record.Html);
                    if (record.Rtf != null) data.SetData(DataFormats.Rtf, record.Rtf);
                    Clipboard.SetDataObject(data, true, 8, 60);
                }
                ownSequence = NativeMethods.GetClipboardSequenceNumber();
                return true;
            }
            catch (ExternalException) { SetStatus("剪贴板正被占用，请再次点击记录。"); return false; }
            catch (IOException exception) { SetStatus("记录读取失败：" + exception.Message); return false; }
            finally { writingClipboard = false; }
        }

        // 恢复原窗口的焦点后再发送粘贴，避免误贴到历史界面。
        private void PasteRecord(ClipRecord record)
        {
            if (IsDeletingRecord(record)) return;
            if (pasteTimer.Enabled) return;
            if (!NativeMethods.IsWindow(targetWindow))
            {
                SetStatus("目标窗口已关闭。请先点击输入框，再按 " + settings.HotkeyText + " 打开历史。");
                return;
            }
            if (!WriteClipboard(record)) return;
            pendingPasteWindow = targetWindow;
            if (!TopMost) Hide();
            if (NativeMethods.IsIconic(pendingPasteWindow)) NativeMethods.ShowWindow(pendingPasteWindow, 9);
            NativeMethods.SetForegroundWindow(pendingPasteWindow);
            pasteTicks = 0;
            pasteTimer.Start();
        }

        // 确认焦点和按键状态后发送一次粘贴，失败时通知用户手动粘贴。
        private void CompletePaste()
        {
            pasteTicks++;
            if (NativeMethods.GetForegroundWindow() == pendingPasteWindow && NativeMethods.ModifiersReleased())
            {
                pasteTimer.Stop();
                if (!NativeMethods.SendPaste()) NotifyPasteFailure();
            }
            else if (pasteTicks >= 30)
            {
                pasteTimer.Stop();
                NotifyPasteFailure();
            }
        }

        // 焦点切换或系统权限阻止自动粘贴时保留已恢复的剪贴板。
        private void NotifyPasteFailure()
        {
            tray.ShowBalloonTip(4000, "自动粘贴未完成", "内容已复制。请在目标输入框按 Ctrl+V；管理员窗口可能需要以管理员身份运行本工具。", ToolTipIcon.Info);
        }

        // 无记录时显示简短空状态，不展示常驻统计信息。
        private void UpdateEmptyState()
        {
            UpdateHoveredRecord(ListBox.NoMatches, RecordAction.None);
            records.UpdateScrollThumb();
            emptyState.Visible = records.Items.Count == 0;
            if (emptyState.Visible) emptyState.BringToFront();
        }

        // 仅在需要提示问题时展开底部反馈，不长期占用列表空间。
        private void SetStatus(string message)
        {
            status.Text = hotkeyRegistered || !IsHandleCreated ? message : "快捷键被占用；请从托盘打开。 " + message;
            feedbackRow.Height = 24;
            status.Visible = true;
            tips.SetToolTip(status, status.Text);
        }

        // 操作成功后收起反馈，快捷键冲突仍保留提示。
        private void ClearStatus()
        {
            if (IsHandleCreated && !hotkeyRegistered)
            {
                SetStatus("请在设置中更换快捷键。");
                return;
            }
            status.Text = string.Empty;
            status.Visible = false;
            feedbackRow.Height = 0;
        }

        // 关闭窗口时留在托盘，只有显式退出才结束进程。
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
            base.OnFormClosing(e);
            Program.WriteDiagnostic("主窗口关闭请求；原因=" + e.CloseReason + "；取消关闭=" + e.Cancel +
                "；托盘退出=" + exiting);
        }

        // 释放系统监听、快捷键、计时器与托盘资源。
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (IsHandleCreated)
                {
                    NativeMethods.RemoveClipboardFormatListener(Handle);
                    if (hotkeyRegistered) NativeMethods.UnregisterHotKey(Handle, hotkeyId);
                    if (translationHotkeyRegistered) NativeMethods.UnregisterHotKey(Handle, TranslationHotkeyId);
                }
                if (translationWindow != null) translationWindow.Dispose();
                compactHeader.Dispose();
                foregroundTimer.Dispose();
                clipboardRetry.Dispose();
                filterTimer.Dispose();
                pasteTimer.Dispose();
                animationTimer.Dispose();
                thumbnails.Dispose();
                foreach (Icon icon in sourceIcons.Values) icon.Dispose();
                if (rowBuffer != null) rowBuffer.Dispose();
                tips.Dispose();
                tray.Visible = false;
                var menu = tray.ContextMenuStrip;
                tray.Dispose();
                if (menu != null) menu.Dispose();
            }
            base.Dispose(disposing);
            if (disposing)
            {
                applicationIcon.Dispose();
                titleFont.Dispose();
                symbolFont.Dispose();
                rowFont.Dispose();
                contentFont.Dispose();
                smallFont.Dispose();
            }
        }
    }
}
