using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClipHistory
{
    internal sealed class SettingsForm : Form
    {
        private readonly CheckBox control = new CheckBox { Text = "Ctrl", AutoSize = true };
        private readonly CheckBox alt = new CheckBox { Text = "Alt", AutoSize = true };
        private readonly CheckBox shift = new CheckBox { Text = "Shift", AutoSize = true };
        private readonly CheckBox win = new CheckBox { Text = "Win", AutoSize = true };
        private readonly ComboBox key = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 92 };
        private readonly NumericUpDown duplicateSeconds = new NumericUpDown { Minimum = 0, Maximum = 60, Width = 90 };
        private readonly RadioButton[] recordLimitOptions = new RadioButton[3];
        private readonly CheckBox startup = new CheckBox { Text = "登录 Windows 后自动启动（在托盘运行）", AutoSize = true };
        private readonly CheckBox dockBottom = new ToggleSwitch { Text = "固定到底部（4:3 卡片，横向滚动）", Dock = DockStyle.Fill };
        private readonly Label error = new Label { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(180, 40, 40), AutoEllipsis = true };
        private readonly Label cacheStatus = new Label { Dock = DockStyle.Fill, AutoEllipsis = true };
        internal Func<AppSettings, bool, string> ApplySettings { get; set; }
        internal Func<string> ClearCache { get; set; }

        // 编辑快捷键、启动、布局、合并间隔及历史记录总数。
        internal SettingsForm(AppSettings settings)
        {
            Text = "设置 · v" + Application.ProductVersion;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(520, 514);
            BackColor = FluentStyle.Background;
            ForeColor = FluentStyle.Text;
            var font = new Font("Microsoft YaHei UI", 10);
            Font = font;
            Disposed += delegate { font.Dispose(); };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), RowCount = 12, ColumnCount = 1 };
            int[] heights = { 30, 44, 32, 40, 40, 48, 40, 24, 46, 32, 40, 44 };
            foreach (int height in heights) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            Controls.Add(layout);
            layout.Controls.Add(new Label { Text = "打开历史的快捷键", AutoSize = true }, 0, 0);
            var hotkey = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            hotkey.Controls.AddRange(new Control[] { control, alt, shift, win, key });
            layout.Controls.Add(hotkey, 0, 1);
            control.Checked = (settings.HotkeyModifiers & 2) != 0;
            alt.Checked = (settings.HotkeyModifiers & 1) != 0;
            shift.Checked = (settings.HotkeyModifiers & 4) != 0;
            win.Checked = (settings.HotkeyModifiers & 8) != 0;
            for (int value = (int)Keys.A; value <= (int)Keys.Z; value++) key.Items.Add((Keys)value);
            for (int value = (int)Keys.D0; value <= (int)Keys.D9; value++) key.Items.Add((Keys)value);
            for (int value = (int)Keys.F1; value <= (int)Keys.F12; value++) key.Items.Add((Keys)value);
            key.SelectedItem = (Keys)settings.HotkeyKey;
            key.AccessibleName = "快捷键主键";
            layout.Controls.Add(startup, 0, 2);
            startup.Checked = AppSettings.StartupEnabled;
            dockBottom.Checked = settings.DockBottom;
            dockBottom.AccessibleName = "固定到底部，4:3 卡片并横向滚动";
            layout.Controls.Add(dockBottom, 0, 3);
            var duplicates = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            duplicates.Controls.Add(new Label { Text = "重复内容合并间隔", AutoSize = true, Margin = new Padding(0, 5, 12, 0) });
            duplicateSeconds.Value = settings.DuplicateSeconds;
            duplicateSeconds.AccessibleName = "重复内容合并间隔，秒";
            duplicates.Controls.Add(duplicateSeconds);
            duplicates.Controls.Add(new Label { Text = "秒", AutoSize = true, Margin = new Padding(8, 5, 0, 0) });
            layout.Controls.Add(duplicates, 0, 4);
            layout.Controls.Add(new Label { Text = "间隔内相同内容仅保留最新时间和来源。\n设为 0 关闭合并，超过间隔会新增记录。", Dock = DockStyle.Fill, ForeColor = FluentStyle.SecondaryText }, 0, 5);
            var limits = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            limits.Controls.Add(new Label { Text = "记录总数", AutoSize = true, Margin = new Padding(0, 5, 12, 0) });
            var limitSurface = new RoundedPanel { Size = new Size(240, 32), BackColor = FluentStyle.ControlFill,
                Padding = new Padding(3), Margin = Padding.Empty };
            var limitTabs = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
                BackColor = FluentStyle.ControlFill, Margin = Padding.Empty, AccessibleName = "历史记录总数" };
            limitTabs.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            int[] recordLimits = { 20, 50, 100 };
            for (int index = 0; index < recordLimits.Length; index++)
            {
                int limit = recordLimits[index];
                limitTabs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
                var option = new SegmentTab { Text = limit + " 条", Tag = limit, Dock = DockStyle.Fill,
                    Margin = new Padding(1, 0, 1, 0), Cursor = Cursors.Hand, AccessibleName = "保留最近 " + limit + " 条记录" };
                recordLimitOptions[index] = option;
                limitTabs.Controls.Add(option, index, 0);
                option.Checked = settings.RecordLimit == limit;
            }
            limitSurface.Controls.Add(limitTabs);
            limits.Controls.Add(limitSurface);
            layout.Controls.Add(limits, 0, 6);
            layout.Controls.Add(new Label { Text = "超出上限删除较早记录，置顶记录也计入总数。", Dock = DockStyle.Fill,
                ForeColor = FluentStyle.SecondaryText }, 0, 7);
            var cache = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            var clear = new FluentButton { Text = "清除缓存", Width = 104, Height = 32,
                AccessibleName = "清除未置顶的本地剪贴板记录和图片缓存" };
            clear.Click += delegate { ClearLocalCache(); };
            cache.Controls.Add(clear);
            cache.Controls.Add(new Label { Text = "清除未置顶记录，保留置顶和设置", AutoSize = true,
                ForeColor = FluentStyle.SecondaryText, Margin = new Padding(10, 9, 0, 0) });
            layout.Controls.Add(cache, 0, 8);
            layout.Controls.Add(cacheStatus, 0, 9);
            layout.Controls.Add(error, 0, 10);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var save = new FluentButton { Text = "保存设置", Width = 112, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = FluentStyle.Accent, ForeColor = Color.White };
            save.FlatAppearance.BorderSize = 0;
            save.FlatAppearance.MouseOverBackColor = Color.FromArgb(0, 94, 174);
            save.FlatAppearance.MouseDownBackColor = Color.FromArgb(0, 83, 154);
            save.Click += delegate { SaveSettings(); };
            var cancel = new FluentButton { Text = "取消", Width = 80, Height = 34, DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.Flat };
            actions.Controls.AddRange(new Control[] { save, cancel });
            layout.Controls.Add(actions, 0, 11);
            AcceptButton = save;
            CancelButton = cancel;
        }

        // 永久删除前确认，清理立即生效，不依赖“保存设置”。
        private void ClearLocalCache()
        {
            if (MessageBox.Show(this, "将永久删除所有未置顶的本地剪贴板记录及图片缓存，无法恢复。\n\n保留置顶记录及其图片、设置和当前系统剪贴板内容。是否继续？",
                "清除缓存", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            string message = ClearCache();
            cacheStatus.ForeColor = message == null ? FluentStyle.SecondaryText : Color.FromArgb(180, 40, 40);
            cacheStatus.Text = message ?? "已清除未置顶记录和图片缓存，置顶记录已保留。";
            cacheStatus.AccessibleName = cacheStatus.Text;
        }

        // 保存当前配置及选中的记录上限，应用失败时保留设置窗口。
        private void SaveSettings()
        {
            uint modifiers = (control.Checked ? 2u : 0u) | (alt.Checked ? 1u : 0u) |
                (shift.Checked ? 4u : 0u) | (win.Checked ? 8u : 0u);
            if (modifiers == 0 || key.SelectedItem == null)
            {
                error.Text = "请选择至少一个修饰键和一个主键。";
                return;
            }
            var candidate = new AppSettings { HotkeyModifiers = modifiers, HotkeyKey = (uint)(Keys)key.SelectedItem,
                DuplicateSeconds = (int)duplicateSeconds.Value, DockBottom = dockBottom.Checked,
                RecordLimit = (int)Array.Find(recordLimitOptions, option => option.Checked).Tag };
            string message = ApplySettings(candidate, startup.Checked);
            if (message != null) { error.Text = message; return; }
            DialogResult = DialogResult.OK;
        }
    }
}
