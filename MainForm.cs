using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
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
        private Bitmap rowBuffer;
        private readonly Dictionary<string, string[]> previewLines = new Dictionary<string, string[]>();
        private Size previewSize;
        private int previewLineHeight;
        private readonly OverlayListBox records = new OverlayListBox();
        private readonly TextBox search = new TextBox();
        private List<ClipRecord> filteredRecords = new List<ClipRecord>();
        private readonly RadioButton[] kindTabs = new RadioButton[4];
        private string selectedKind = "全部类型";
        private readonly Label status = new Label();
        private readonly RowStyle feedbackRow = new RowStyle(SizeType.Absolute, 0);
        private readonly Label emptyState = new Label();
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
        private RoundedPanel tabSurface;
        private TableLayoutPanel toolbar;
        private SearchField searchField;
        private Button reset;
        private readonly TableLayoutPanel compactHeader = new TableLayoutPanel { Dock = DockStyle.Fill,
            ColumnCount = 6, RowCount = 1, Margin = Padding.Empty, Padding = new Padding(0, 6, 0, 6), BackColor = FluentStyle.Background };
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly Timer foregroundTimer = new Timer { Interval = 150 };
        private readonly Timer clipboardRetry = new Timer { Interval = 60 };
        private readonly Timer filterTimer = new Timer { Interval = 250 };
        private readonly Timer pasteTimer = new Timer { Interval = 50 };
        private readonly Font titleFont = new Font("Microsoft YaHei UI", 10);
        private readonly Font symbolFont = new Font("Segoe MDL2 Assets", 10);
        private readonly Font rowFont = new Font("Microsoft YaHei UI", 10, FontStyle.Regular);
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
        private enum RecordAction { None = -1, More, Pin, Delete }
        private readonly uint currentProcessId = (uint)Process.GetCurrentProcess().Id;
        private readonly Color ink = FluentStyle.Text;
        private readonly Color muted = FluentStyle.SecondaryText;
        private readonly Color accent = FluentStyle.Accent;

        // 初始化历史窗口，并在启动时记住当前粘贴目标。
        internal MainForm(bool trayOnly)
        {
            thumbnails = new ThumbnailCache(store);
            startInTray = trayOnly;
            settings = AppSettings.Load(store.Root);
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
            var title = new Label { Text = "剪贴记录", Font = titleFont, AutoSize = true, Location = new Point(4, 6) };
            heading.Controls.Add(title);
            heading.MouseDown += DragWindow;
            title.MouseDown += DragWindow;
            windowActions = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 144, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty };
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

            tabSurface = new RoundedPanel { Dock = DockStyle.Fill, BackColor = FluentStyle.ControlFill,
                Padding = new Padding(3), Margin = new Padding(0, 3, 0, 7) };
            var tabs = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1,
                Margin = Padding.Empty, BackColor = FluentStyle.ControlFill, AccessibleName = "记录类型筛选" };
            tabs.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            tabSurface.Controls.Add(tabs);
            string[] typeNames = { "全部类型", "文本", "图片", "文件" };
            for (int index = 0; index < typeNames.Length; index++)
            {
                tabs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
                string kind = typeNames[index];
                var tab = new SegmentTab { Text = index == 0 ? "全部" : kind, Tag = kind,
                    Dock = DockStyle.Fill, Appearance = Appearance.Button, FlatStyle = FlatStyle.Flat,
                    TextAlign = ContentAlignment.MiddleCenter, Font = smallFont, ForeColor = muted,
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
            records.ItemHeight = 104;
            records.IntegralHeight = false;
            records.AccessibleName = "剪贴板历史记录，Enter 粘贴，Ctrl+P 切换记录置顶，Delete 删除，Shift+F10 打开更多菜单";
            records.DrawItem += DrawRecord;
            records.MouseUp += RecordMouseUp;
            records.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                int index = records.IndexFromPoint(e.Location);
                UpdateHoveredRecord(index, index == ListBox.NoMatches ? RecordAction.None :
                    HitRecordAction(records.GetItemRectangle(index), e.Location));
            };
            records.MouseLeave += delegate { UpdateHoveredRecord(ListBox.NoMatches, RecordAction.None); };
            records.ViewScrolled += delegate { UpdateHoveredRecord(ListBox.NoMatches, RecordAction.None); };
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
                    Rectangle more = RecordActionBounds(records.GetItemRectangle(records.SelectedIndex), RecordAction.More);
                    ShowRecordMenu((ClipRecord)records.SelectedItem, new Point(more.Right, more.Bottom + 2));
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
            emptyState.ForeColor = muted;
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
            search.AccessibleName = "搜索最近 100 条记录的内容、文件名或来源窗口";
            search.TextChanged += delegate { ScheduleFilter(); };
            searchField = new SearchField(search) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0) };
            toolbar.Controls.Add(searchField, 0, 0);
            reset = CreateButton("重置", 50);
            reset.Dock = DockStyle.Fill;
            reset.Font = smallFont;
            reset.BackColor = FluentStyle.Surface;
            reset.Margin = Padding.Empty;
            reset.Click += delegate { search.Clear(); kindTabs[0].Checked = true; ScheduleFilter(); };
            toolbar.Controls.Add(reset, 1, 0);
            layout.Controls.Add(toolbar, 0, 3);

            status.Dock = DockStyle.Fill;
            status.Margin = Padding.Empty;
            status.Font = smallFont;
            status.ForeColor = muted;
            status.AutoEllipsis = true;
            status.Visible = false;
            layout.Controls.Add(status, 0, 4);
            compactHeader.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            foreach (int width in new[] { 96, 248, 256, 54 })
                compactHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width));
            compactHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            compactHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 144));
            compactHeader.MouseDown += DragWindow;
        }

        // 底部模式合并为单行工具栏，重置按钮随顶栏使用窗口底色。
        private void ArrangeHeader()
        {
            layout.SuspendLayout();
            compactHeader.SuspendLayout();
            reset.BackColor = bottomLayout ? BackColor : FluentStyle.Surface;
            if (bottomLayout)
            {
                compactHeader.Controls.Add(heading, 0, 0);
                compactHeader.Controls.Add(searchField, 1, 0);
                compactHeader.Controls.Add(tabSurface, 2, 0);
                compactHeader.Controls.Add(reset, 3, 0);
                compactHeader.Controls.Add(windowActions, 5, 0);
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
            menu.Items.Add("退出", null, delegate { exiting = true; Close(); });
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

        // 先验证新快捷键可注册，保存成功后释放旧快捷键。
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
            settings = candidate;
            if (changeLayout) ApplyWindowLayout();
            UpdateSettingsLabels();
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

        // 固定到当前屏幕工作区底部，避开任务栏并适应屏幕尺寸变化。
        private void PositionBottomWindow()
        {
            Rectangle area = Screen.FromControl(this).WorkingArea;
            int height = Math.Min(260, area.Height);
            var bounds = new Rectangle(area.Left, area.Bottom - height, area.Width, height);
            if (Bounds != bounds) Bounds = bounds;
        }

        // 在托盘展示用户配置的快捷键，主窗口不显示常驻提示。
        private void UpdateSettingsLabels()
        {
            trayOpen.Text = "打开剪贴板";
            trayOpen.ShortcutKeyDisplayString = settings.HotkeyText;
            tray.Text = "剪贴记录 · " + settings.HotkeyText;
        }

        // 初始化系统窗口效果、剪贴板通知和全局快捷键。
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeMethods.ApplyWindowChrome(Handle);
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
            if (message.Msg == NativeMethods.WM_DWMCOMPOSITIONCHANGED)
                NativeMethods.ApplyWindowChrome(Handle);
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
                var source = new StringBuilder(256);
                NativeMethods.GetWindowText(NativeMethods.GetForegroundWindow(), source, source.Capacity);
                var record = new ClipRecord { Id = Guid.NewGuid().ToString("N"), CapturedAt = DateTime.Now.ToString("o"), Source = source.ToString() };
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

        // 在最近 100 条历史中筛选，实时复制时保留当前滚动位置。
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

        // 一次展示全部筛选结果，继续复用列表的滚动和选中状态。
        private void ShowRecords(bool preservePosition)
        {
            int topIndex = preservePosition ? records.TopIndex : 0;
            var selected = preservePosition ? records.SelectedItem as ClipRecord : null;
            records.BeginUpdate();
            records.Items.Clear();
            previewLines.Clear();
            hoveredRecord = ListBox.NoMatches;
            hoveredAction = RecordAction.None;
            tips.SetToolTip(records, "");
            records.Items.AddRange(filteredRecords.ToArray());
            if (selected != null)
                records.SelectedIndex = filteredRecords.FindIndex(item => item.Id == selected.Id);
            if (filteredRecords.Count > 0) records.TopIndex = Math.Min(topIndex, filteredRecords.Count - 1);
            records.EndUpdate();
            UpdateEmptyState();
        }

        // 更新卡片与操作按钮的悬停提示，只重绘受影响的行。
        private void UpdateHoveredRecord(int index, RecordAction action)
        {
            if (index == hoveredRecord && action == hoveredAction) return;
            int previous = hoveredRecord;
            hoveredRecord = index;
            hoveredAction = action;
            string hint = "";
            if (action == RecordAction.More) hint = "更多操作";
            else if (action == RecordAction.Pin)
                hint = ((ClipRecord)records.Items[index]).IsPinned ? "取消置顶（Ctrl+P）" : "置顶，清除缓存时保留（Ctrl+P）";
            else if (action == RecordAction.Delete) hint = "删除这条记录（Delete）";
            tips.SetToolTip(records, hint);
            foreach (int row in new[] { previous, index })
            {
                if (row < 0 || row >= records.Items.Count) continue;
                Rectangle bounds = Rectangle.Intersect(records.GetItemRectangle(row), records.ClientRectangle);
                if (bounds.IsEmpty) continue;
                NativeMethods.InvalidateWithoutErase(records.Handle, bounds);
            }
        }

        // 用独立位图绘制整行，避免原生列表的设备上下文残留边线。
        private void DrawRecord(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            if (rowBuffer == null || rowBuffer.Size != e.Bounds.Size)
            {
                if (rowBuffer != null) rowBuffer.Dispose();
                // 卡片背景始终不透明，行缓冲使用 RGB 位图。
                rowBuffer = new Bitmap(e.Bounds.Width, e.Bounds.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                rowBuffer.SetResolution(e.Graphics.DpiX, e.Graphics.DpiY);
            }
            using (var graphics = Graphics.FromImage(rowBuffer))
            {
                graphics.Clear(records.BackColor);
                var bufferedArgs = new DrawItemEventArgs(graphics, e.Font,
                    new Rectangle(Point.Empty, e.Bounds.Size), e.Index, e.State, e.ForeColor, e.BackColor);
                PaintRecord(bufferedArgs);
            }
            e.Graphics.DrawImageUnscaled(rowBuffer, e.Bounds.Location);
        }

        // 白色卡片始终保留细边线，选中和悬停使用蓝色层级区分。
        private void PaintRecord(DrawItemEventArgs e)
        {
            var record = (ClipRecord)records.Items[e.Index];
            bool selected = (e.State & DrawItemState.Selected) != 0;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using (var brush = new SolidBrush(records.BackColor)) e.Graphics.FillRectangle(brush, e.Bounds);
            var card = new Rectangle(e.Bounds.X + 4, e.Bounds.Y + 4, e.Bounds.Width - 8, e.Bounds.Height - 8);
            using (var path = FluentStyle.RoundedRectangle(card, 12))
            using (var brush = new SolidBrush(selected ? FluentStyle.SelectedSurface :
                hoveredRecord == e.Index ? FluentStyle.HoverSurface : FluentStyle.Surface))
            {
                e.Graphics.FillPath(brush, path);
                using (var pen = new Pen(selected ? accent : hoveredRecord == e.Index ? FluentStyle.HoverBorder : FluentStyle.Border,
                    selected && records.Focused ? 1.5f : 1f)) e.Graphics.DrawPath(pen, path);
            }
            if (record.Kind == "图片")
                DrawImageRecord(e.Graphics, card, record);
            else
                DrawPreviewText(e.Graphics, record.Preview, bottomLayout ?
                    new Rectangle(card.X + 14, card.Y + 44, card.Width - 28, card.Height - 58) :
                    new Rectangle(card.X + 14, card.Y + 13, card.Width - 58, card.Height - 26));
            DrawRecordAction(e, RecordAction.More, false);
            DrawRecordAction(e, RecordAction.Pin, record.IsPinned);
            DrawRecordAction(e, RecordAction.Delete, false);
        }

        // 保留圆角按钮状态，图标统一使用细线绘制。
        private void DrawRecordAction(DrawItemEventArgs e, RecordAction action, bool active)
        {
            Rectangle bounds = RecordActionBounds(e.Bounds, action);
            bool hovered = hoveredRecord == e.Index && hoveredAction == action;
            bool deleting = hovered && action == RecordAction.Delete;
            Color color = deleting ? Color.FromArgb(190, 45, 45) : active ? accent : muted;
            if (hovered || active)
            {
                Color fill = deleting ? Color.FromArgb(255, 235, 235) : active ?
                    Color.FromArgb(222, 234, 255) : FluentStyle.ControlFill;
                using (var path = FluentStyle.RoundedRectangle(bounds, 8))
                using (var brush = new SolidBrush(fill)) e.Graphics.FillPath(brush, path);
            }
            DrawRecordIcon(e.Graphics, bounds, action, active, color);
        }

        // 绘制细圆点、轮廓图钉和垃圾桶，避免字体图标的小尺寸粗重边缘。
        private static void DrawRecordIcon(Graphics graphics, Rectangle bounds, RecordAction action, bool active, Color color)
        {
            float size = Math.Min(16f * graphics.DpiX / 96f, bounds.Height - 8f);
            GraphicsState state = graphics.Save();
            graphics.TranslateTransform(bounds.X + (bounds.Width - size) / 2f, bounds.Y + (bounds.Height - size) / 2f);
            graphics.ScaleTransform(size / 16f, size / 16f);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (action == RecordAction.More)
            {
                using (var brush = new SolidBrush(color))
                    for (int dot = 0; dot < 3; dot++)
                        graphics.FillEllipse(brush, 3.3f + dot * 4f, 7.3f, 1.4f, 1.4f);
            }
            else
            {
                using (var pen = new Pen(color, 1f))
                {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    if (action == RecordAction.Pin)
                    {
                        if (!active)
                        {
                            graphics.TranslateTransform(8, 8);
                            graphics.RotateTransform(30);
                            graphics.TranslateTransform(-8, -8);
                        }
                        graphics.DrawLine(pen, 4, 2, 12, 2);
                        graphics.DrawLines(pen, new[] { new Point(5, 2), new Point(5, 6), new Point(3, 10),
                            new Point(13, 10), new Point(11, 6), new Point(11, 2) });
                        graphics.DrawLine(pen, 8, 10, 8, 14);
                    }
                    else
                    {
                        graphics.DrawLine(pen, 3, 4, 13, 4);
                        graphics.DrawLines(pen, new[] { new Point(6, 4), new Point(6, 2),
                            new Point(10, 2), new Point(10, 4) });
                        graphics.DrawLines(pen, new[] { new PointF(4, 6), new PointF(4.8f, 14),
                            new PointF(11.2f, 14), new PointF(12, 6) });
                        graphics.DrawLine(pen, 6.5f, 6.5f, 7, 11.5f);
                        graphics.DrawLine(pen, 9.5f, 6.5f, 9, 11.5f);
                    }
                }
            }
            graphics.Restore(state);
        }

        // 横排 4:3 卡片的操作放在顶端，普通卡片保留右侧纵排操作。
        private Rectangle RecordActionBounds(Rectangle row, RecordAction action)
        {
            if (bottomLayout) return new Rectangle(row.Right - 108 + (int)action * 32, row.Top + 8, 32, 28);
            return new Rectangle(row.Right - 40, row.Top + 8 + (int)action * 30, 32, 28);
        }

        // 按固定按钮区域判断操作，内容区域继续用于直接粘贴。
        private RecordAction HitRecordAction(Rectangle row, Point location)
        {
            for (RecordAction action = RecordAction.More; action <= RecordAction.Delete; action++)
                if (RecordActionBounds(row, action).Contains(location)) return action;
            return RecordAction.None;
        }

        // 缓存当前页的文字排版，滚动和悬停时直接绘制，末行保留省略号。
        private void DrawPreviewText(Graphics graphics, string text, Rectangle bounds)
        {
            int textHeight = (int)Math.Ceiling(rowFont.GetHeight(graphics));
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
                int offset = 0;
                using (var measureFormat = new StringFormat { FormatFlags = StringFormatFlags.LineLimit })
                {
                    for (int line = 0; line < lineCount && offset < text.Length; line++)
                    {
                        int fitted, lines;
                        graphics.MeasureString(text.Substring(offset), rowFont,
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
                    graphics.DrawString(visibleLines[line], rowFont, brush,
                        new RectangleF(bounds.X, bounds.Y + line * lineHeight, bounds.Width, textHeight), drawFormat);
                }
            }
        }

        // 图片等比例展示，横排卡片将尺寸说明放在图片下方。
        private void DrawImageRecord(Graphics graphics, Rectangle card, ClipRecord record)
        {
            var preview = bottomLayout ? new Rectangle(card.X + 12, card.Y + 44, card.Width - 24, card.Height - 82) :
                new Rectangle(card.X + 12, card.Y + 12, 116, card.Height - 24);
            Bitmap thumbnail = thumbnails.Get(record);
            using (var path = FluentStyle.RoundedRectangle(preview, 8))
            {
                using (var brush = new SolidBrush(FluentStyle.Background)) graphics.FillPath(brush, path);
                if (thumbnail != null)
                {
                    double scale = Math.Min(preview.Width / (double)thumbnail.Width, preview.Height / (double)thumbnail.Height);
                    int width = Math.Max(1, (int)(thumbnail.Width * scale));
                    int height = Math.Max(1, (int)(thumbnail.Height * scale));
                    GraphicsState state = graphics.Save();
                    graphics.SetClip(path);
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(thumbnail, new Rectangle(preview.X + (preview.Width - width) / 2,
                        preview.Y + (preview.Height - height) / 2, width, height));
                    graphics.Restore(state);
                }
            }
            var caption = bottomLayout ? new Rectangle(card.Left + 12, preview.Bottom + 4, card.Width - 24, 24) :
                new Rectangle(preview.Right + 16, card.Top + 24, card.Right - preview.Right - 58, 24);
            using (var format = new StringFormat { FormatFlags = StringFormatFlags.NoWrap,
                LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter })
            using (var titleBrush = new SolidBrush(ink))
            using (var detailBrush = new SolidBrush(muted))
            {
                if (!bottomLayout)
                {
                    graphics.DrawString("图片", rowFont, titleBrush, caption, format);
                    caption.Y += 26;
                }
                graphics.DrawString(thumbnail == null ? "缩略图不可用" : record.Preview.Replace("图片", "").Trim(),
                    smallFont, detailBrush, caption, format);
            }
        }

        // 卡片按钮分别打开更多、切换置顶或删除，点击内容仍直接粘贴。
        private void RecordMouseUp(object sender, MouseEventArgs e)
        {
            int index = records.IndexFromPoint(e.Location);
            if (index == ListBox.NoMatches) return;
            records.SelectedIndex = index;
            var record = (ClipRecord)records.Items[index];
            Rectangle bounds = records.GetItemRectangle(index);
            RecordAction action = HitRecordAction(bounds, e.Location);
            if (e.Button == MouseButtons.Right)
                ShowRecordMenu(record, e.Location);
            else if (e.Button == MouseButtons.Left)
            {
                if (action == RecordAction.More)
                {
                    Rectangle more = RecordActionBounds(bounds, action);
                    ShowRecordMenu(record, new Point(more.Right, more.Bottom + 2));
                }
                else if (action == RecordAction.Pin) ToggleRecordPin(record);
                else if (action == RecordAction.Delete) DeleteRecord(record);
                else PasteRecord(record);
            }
        }

        // 使用紧凑的图标菜单展示当前记录的查看、复制和粘贴操作。
        private void ShowRecordMenu(ClipRecord record, Point location)
        {
            var menu = new RecordMenu(rowFont);
            menu.AddAction("查看完整内容", RecordMenuIcon.Preview, delegate { PreviewRecord(record); });
            menu.AddAction("复制到剪贴板", RecordMenuIcon.Copy, delegate { if (WriteClipboard(record)) ClearStatus(); });
            menu.AddAction("粘贴到目标窗口", RecordMenuIcon.Paste, delegate { PasteRecord(record); });
            menu.Closed += delegate { BeginInvoke(new Action(menu.Dispose)); };
            menu.Show(records, location, ToolStripDropDownDirection.BelowLeft);
        }

        // 保存记录置顶状态并刷新当前筛选结果的顺序。
        private void ToggleRecordPin(ClipRecord record)
        {
            try
            {
                store.SetPinned(record, !record.IsPinned);
                ReloadRecords();
            }
            catch (IOException exception) { SetStatus("置顶状态保存失败：" + exception.Message); }
            catch (UnauthorizedAccessException exception) { SetStatus("置顶状态保存失败：" + exception.Message); }
            catch (SerializationException exception) { SetStatus("历史日志格式损坏：" + exception.Message); }
        }

        // 删除当前单条记录，释放缩略图并刷新列表。
        private void DeleteRecord(ClipRecord record)
        {
            string error = null;
            try { store.Delete(record); }
            catch (IOException exception) { error = "删除未完成：" + exception.Message; }
            catch (UnauthorizedAccessException exception) { error = "删除未完成：" + exception.Message; }
            catch (SerializationException exception) { error = "历史日志格式损坏：" + exception.Message; }
            finally
            {
                thumbnails.Clear();
                ReloadRecords();
            }
            if (error != null) SetStatus(error);
        }

        // 展示完整文本、文件路径或图片。
        private void PreviewRecord(ClipRecord record)
        {
            using (var preview = new Form { Text = record.Kind + " · " + record.LocalTime.ToString("yyyy-MM-dd HH:mm:ss"),
                Size = new Size(600, 440), StartPosition = FormStartPosition.CenterParent, Font = rowFont, Icon = applicationIcon })
            {
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
                thumbnails.Dispose();
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
                smallFont.Dispose();
            }
        }
    }
}
