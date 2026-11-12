using System;
using System.Drawing;
using System.Drawing.Drawing2D;
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
        private static readonly Color textColor = Color.FromArgb(36, 36, 36);
        private static readonly Color secondaryColor = Color.FromArgb(120, 120, 120);
        private static readonly Color borderColor = Color.FromArgb(232, 232, 232);
        private readonly Font descriptionFont = new Font("Microsoft YaHei UI", 9);
        private readonly Font sectionFont = new Font("Microsoft YaHei UI", 10, FontStyle.Bold);
        private readonly CheckBox startup = new ToggleSwitch { Size = new Size(60, 32), AccessibleName = "登录 Windows 后在托盘启动" };
        private readonly CheckBox dockBottom = new ToggleSwitch { Size = new Size(60, 32), AccessibleName = "固定到底部，正方形卡片并横向滚动" };
        private readonly Label error = new Label { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(180, 40, 40), AutoEllipsis = true };
        private readonly Label cacheStatus = new Label { Dock = DockStyle.Fill, AutoEllipsis = true };
        internal Func<AppSettings, bool, string> ApplySettings { get; set; }
        internal Func<string> ClearCache { get; set; }

        // 用分组卡片展示设置，说明在左侧、原生操作控件在右侧。
        internal SettingsForm(AppSettings settings)
        {
            Text = "设置 · v" + Application.ProductVersion;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(680, 690);
            BackColor = Color.White;
            ForeColor = textColor;
            var font = new Font("Microsoft YaHei UI", 10);
            Font = font;
            Disposed += delegate { font.Dispose(); descriptionFont.Dispose(); sectionFont.Dispose(); };

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
                BackColor = Color.White, Margin = Padding.Empty };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            Controls.Add(layout);

            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
            var content = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 4,
                Padding = new Padding(24, 18, 24, 10), BackColor = Color.White, Margin = Padding.Empty };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int index = 0; index < 4; index++) content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            scroll.Controls.Add(content);
            layout.Controls.Add(scroll, 0, 0);

            var hotkey = new FlowLayoutPanel { Size = new Size(330, 32), WrapContents = false,
                BackColor = Color.White, Margin = Padding.Empty };
            foreach (CheckBox modifier in new[] { control, alt, shift, win })
            {
                modifier.Margin = new Padding(0, 5, 10, 0);
                modifier.ForeColor = textColor;
            }
            key.Width = 76;
            key.FlatStyle = FlatStyle.Flat;
            key.Margin = new Padding(0, 2, 0, 0);
            key.AccessibleName = "快捷键主键";
            hotkey.Controls.AddRange(new Control[] { control, alt, shift, win, key });
            control.Checked = (settings.HotkeyModifiers & 2) != 0;
            alt.Checked = (settings.HotkeyModifiers & 1) != 0;
            shift.Checked = (settings.HotkeyModifiers & 4) != 0;
            win.Checked = (settings.HotkeyModifiers & 8) != 0;
            for (int value = (int)Keys.A; value <= (int)Keys.Z; value++) key.Items.Add((Keys)value);
            for (int value = (int)Keys.D0; value <= (int)Keys.D9; value++) key.Items.Add((Keys)value);
            for (int value = (int)Keys.F1; value <= (int)Keys.F12; value++) key.Items.Add((Keys)value);
            key.SelectedItem = (Keys)settings.HotkeyKey;
            startup.Checked = AppSettings.StartupEnabled;
            dockBottom.Checked = settings.DockBottom;

            var general = CreateGroup(
                CreateSettingRow("打开历史的快捷键", "选择修饰键和主键组合，快速打开剪贴板历史。", hotkey, 88),
                CreateSettingRow("登录时启动", "登录 Windows 后自动启动，并在托盘运行。", startup, 66),
                CreateSettingRow("固定到底部", "在屏幕底部显示大尺寸正方形卡片，支持横向滚动。", dockBottom, 66));
            general.Margin = new Padding(0, 0, 0, 24);
            content.Controls.Add(new Label { Text = "常规", AutoSize = true, Font = sectionFont,
                Margin = new Padding(0, 0, 0, 10) }, 0, 0);
            content.Controls.Add(general, 0, 1);

            var duplicates = new FlowLayoutPanel { Size = new Size(116, 30), WrapContents = false,
                BackColor = Color.White, Margin = Padding.Empty };
            duplicateSeconds.Width = 78;
            duplicateSeconds.BorderStyle = BorderStyle.FixedSingle;
            duplicateSeconds.Margin = new Padding(0, 2, 0, 0);
            duplicateSeconds.Value = settings.DuplicateSeconds;
            duplicateSeconds.AccessibleName = "重复内容合并间隔，秒";
            duplicates.Controls.Add(duplicateSeconds);
            duplicates.Controls.Add(new Label { Text = "秒", AutoSize = true, ForeColor = secondaryColor,
                Margin = new Padding(8, 5, 0, 0) });

            var limitSurface = new RoundedPanel { Size = new Size(210, 32), BackColor = Color.FromArgb(245, 245, 245),
                Padding = new Padding(3), Margin = Padding.Empty };
            var limitTabs = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
                BackColor = limitSurface.BackColor, Margin = Padding.Empty, AccessibleName = "历史记录总数" };
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

            var clear = new FluentButton { Text = "清除缓存", Width = 100, Height = 32, ForeColor = textColor,
                AccessibleName = "清除未置顶的本地剪贴板记录和图片缓存" };
            clear.FlatAppearance.BorderColor = borderColor;
            clear.FlatAppearance.MouseOverBackColor = Color.FromArgb(245, 245, 245);
            clear.FlatAppearance.MouseDownBackColor = Color.FromArgb(235, 235, 235);
            clear.Click += delegate { ClearLocalCache(); };
            var history = CreateGroup(
                CreateSettingRow("重复内容合并间隔", "间隔内相同内容仅保留最新时间和来源。设为 0 秒关闭合并，超过间隔会新增记录。", duplicates, 84),
                CreateSettingRow("记录总数", "超出上限删除较早记录，置顶记录也计入总数。", limitSurface, 78),
                CreateSettingRow("清除缓存", "清除未置顶记录和图片缓存，保留置顶记录与设置。", clear, 78));
            content.Controls.Add(new Label { Text = "剪贴板记录", AutoSize = true, Font = sectionFont,
                Margin = new Padding(0, 0, 0, 10) }, 0, 2);
            content.Controls.Add(history, 0, 3);

            var feedback = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
                Padding = new Padding(24, 0, 24, 0), Margin = Padding.Empty };
            feedback.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            feedback.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            cacheStatus.Font = error.Font = descriptionFont;
            cacheStatus.ForeColor = secondaryColor;
            cacheStatus.Margin = error.Margin = Padding.Empty;
            feedback.Controls.Add(cacheStatus, 0, 0);
            feedback.Controls.Add(error, 0, 1);
            layout.Controls.Add(feedback, 0, 1);

            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false, Padding = new Padding(24, 12, 24, 12), Margin = Padding.Empty };
            actions.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (var pen = new Pen(borderColor)) e.Graphics.DrawLine(pen, 0, 0, actions.ClientSize.Width, 0);
            };
            var save = new FluentButton { Text = "保存设置", Width = 112, Height = 34, BackColor = FluentStyle.Accent,
                ForeColor = Color.White, Margin = Padding.Empty };
            save.FlatAppearance.BorderSize = 0;
            save.FlatAppearance.MouseOverBackColor = Color.FromArgb(0, 94, 174);
            save.FlatAppearance.MouseDownBackColor = Color.FromArgb(0, 83, 154);
            save.Click += delegate { SaveSettings(); };
            var cancel = new FluentButton { Text = "取消", Width = 80, Height = 34, DialogResult = DialogResult.Cancel,
                ForeColor = textColor, Margin = new Padding(0, 0, 10, 0) };
            cancel.FlatAppearance.BorderColor = borderColor;
            actions.Controls.AddRange(new Control[] { save, cancel });
            layout.Controls.Add(actions, 0, 2);
            AcceptButton = save;
            CancelButton = cancel;
        }

        // 设置组复用圆角容器，边框与行分隔线仅在本窗口绘制。
        private RoundedPanel CreateGroup(params Control[] rows)
        {
            var group = new RoundedPanel { Dock = DockStyle.Top, BackColor = Color.White,
                Padding = new Padding(16, 1, 16, 1), Margin = Padding.Empty };
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = rows.Length,
                BackColor = Color.White, Margin = Padding.Empty };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            int height = 2;
            for (int index = 0; index < rows.Length; index++)
            {
                Control row = rows[index];
                table.RowStyles.Add(new RowStyle(SizeType.Absolute, row.Height));
                height += row.Height;
                row.Dock = DockStyle.Fill;
                row.Margin = Padding.Empty;
                if (index < rows.Length - 1)
                    row.Paint += delegate(object sender, PaintEventArgs e)
                    {
                        using (var pen = new Pen(borderColor))
                            e.Graphics.DrawLine(pen, 0, row.ClientSize.Height - 1, row.ClientSize.Width, row.ClientSize.Height - 1);
                    };
                table.Controls.Add(row, 0, index);
            }
            group.Height = height;
            group.Controls.Add(table);
            group.Paint += delegate(object sender, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = FluentStyle.RoundedRectangle(new Rectangle(0, 0, group.Width - 1, group.Height - 1), 14))
                using (var pen = new Pen(borderColor)) e.Graphics.DrawPath(pen, path);
            };
            return group;
        }

        // 所有设置项统一标题、说明和右侧控件的间距与垂直对齐。
        private TableLayoutPanel CreateSettingRow(string title, string description, Control editor, int height)
        {
            var row = new TableLayoutPanel { Height = height, ColumnCount = 2, RowCount = 1,
                BackColor = Color.White, Margin = Padding.Empty };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, editor.Width + 20));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var labels = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
                Padding = new Padding(0, 12, 0, 10), Margin = Padding.Empty, BackColor = Color.White };
            labels.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            labels.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            labels.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            labels.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, ForeColor = textColor,
                Margin = Padding.Empty, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            labels.Controls.Add(new Label { Text = description, Dock = DockStyle.Fill, ForeColor = secondaryColor,
                Font = descriptionFont, Margin = Padding.Empty }, 0, 1);
            editor.Anchor = AnchorStyles.Right;
            editor.Margin = Padding.Empty;
            row.Controls.Add(labels, 0, 0);
            row.Controls.Add(editor, 1, 0);
            return row;
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
