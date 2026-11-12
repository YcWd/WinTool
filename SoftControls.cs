using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClipHistory
{
    internal sealed class FluentLabel : Label
    {
        // 使用带 Alpha 的 GDI+ 文字，适配毛玻璃背景。
        internal FluentLabel()
        {
            UseCompatibleTextRendering = true;
            DoubleBuffered = true;
        }

        // 清理透明背景，避免标签四周出现不透明底色。
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            FluentStyle.ClearBackground(e.Graphics, BackColor);
        }

        // 灰度抗锯齿保持小字号文字在毛玻璃上的清晰度。
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            base.OnPaint(e);
        }
    }

    internal sealed class ToggleSwitch : CheckBox
    {
        // 保留复选框的空格键和无障碍状态，绘制右侧开关。
        internal ToggleSwitch()
        {
            AutoSize = false;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            CheckedChanged += delegate { Invalidate(); };
        }

        // 标签与开关居中排列，键盘焦点使用浅色圆角边线。
        protected override void OnPaint(PaintEventArgs e)
        {
            FluentStyle.ClearBackground(e.Graphics, Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var track = new Rectangle(Width - 42, (Height - 22) / 2, 40, 22);
            using (var path = FluentStyle.RoundedRectangle(track, 11))
            using (var brush = new SolidBrush(Checked ? FluentStyle.Accent : FluentStyle.ControlFill))
                e.Graphics.FillPath(brush, path);
            e.Graphics.FillEllipse(Brushes.White, Checked ? track.Right - 20 : track.Left + 2, track.Top + 2, 18, 18);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(0, 0, Width - 54, Height), FluentStyle.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues)
                using (var path = FluentStyle.RoundedRectangle(new Rectangle(1, 1, Width - 3, Height - 3), 8))
                using (var pen = new Pen(FluentStyle.Accent)) e.Graphics.DrawPath(pen, path);
        }
    }

    internal sealed class RoundedPanel : Panel
    {
        // 圆角容器用于分段选择器的连续浅灰底色。
        internal RoundedPanel() { DoubleBuffered = true; }

        // 用父容器底色清理四角，再绘制分段控件的圆角底板。
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            FluentStyle.ClearBackground(e.Graphics, Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = FluentStyle.RoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), 12))
            using (var brush = new SolidBrush(BackColor)) e.Graphics.FillPath(brush, path);
        }
    }

    internal sealed class SegmentTab : RadioButton
    {
        private bool hovered;

        // 保留单选组的方向键交互，统一绘制圆角选中态。
        internal SegmentTab()
        {
            Appearance = Appearance.Button;
            FlatStyle = FlatStyle.Flat;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            MouseEnter += delegate { hovered = true; Invalidate(); };
            MouseLeave += delegate { hovered = false; Invalidate(); };
        }

        // 浅色底板配深色文字，蓝底白字选中态使用 Alpha 抗锯齿绘制。
        protected override void OnPaint(PaintEventArgs e)
        {
            FluentStyle.ClearBackground(e.Graphics, Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using (var path = FluentStyle.RoundedRectangle(new Rectangle(1, 1, Width - 3, Height - 3), 8))
            {
                if (Enabled && (Checked || hovered))
                    using (var brush = new SolidBrush(Checked ? FluentStyle.Accent : FluentStyle.Surface))
                        e.Graphics.FillPath(brush, path);
                if (Enabled && Focused && ShowFocusCues)
                    using (var pen = new Pen(Checked ? Color.White : FluentStyle.Accent)) e.Graphics.DrawPath(pen, path);
            }
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter })
            using (var brush = new SolidBrush(!Enabled ? FluentStyle.SecondaryText : Checked ? Color.White : FluentStyle.Text))
                e.Graphics.DrawString(Text, Font, brush, ClientRectangle, format);
        }
    }

    internal sealed class RoundedTextField : UserControl
    {
        private readonly TextBox editor;

        // 复用圆角输入外观，原生文本框保留输入法、选择与复制操作。
        internal RoundedTextField(TextBox textBox)
        {
            editor = textBox;
            DoubleBuffered = true;
            TabStop = false;
            BackColor = FluentStyle.Surface;
            editor.BorderStyle = BorderStyle.None;
            Controls.Add(editor);
            editor.GotFocus += delegate { Invalidate(); };
            editor.LostFocus += delegate { Invalidate(); };
            editor.FontChanged += delegate { LayoutEditor(); };
            Resize += delegate { LayoutEditor(); };
            BackColorChanged += delegate { editor.BackColor = BackColor; };
            MouseDown += delegate { editor.Focus(); };
            editor.Dock = DockStyle.None;
            editor.BackColor = BackColor;
            LayoutEditor();
        }

        // 多行编辑区保留内边距，单行输入在圆角表面内垂直居中。
        private void LayoutEditor()
        {
            int inset = 12;
            editor.SetBounds(inset, editor.Multiline ? inset : (Height - editor.PreferredHeight) / 2,
                Math.Max(0, Width - inset * 2), editor.Multiline ? Math.Max(0, Height - inset * 2) : editor.PreferredHeight);
        }

        // 输入表面以细边线分层，聚焦时使用蓝色强调。
        protected override void OnPaint(PaintEventArgs e)
        {
            FluentStyle.ClearBackground(e.Graphics, Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = FluentStyle.RoundedRectangle(new Rectangle(1, 1, Width - 3, Height - 3), 10))
            {
                using (var brush = new SolidBrush(BackColor)) e.Graphics.FillPath(brush, path);
                using (var pen = new Pen(editor.Focused ? FluentStyle.Accent : FluentStyle.Border)) e.Graphics.DrawPath(pen, path);
            }
        }
    }

    internal sealed class SearchField : UserControl
    {
        private readonly TextBox editor;

        // 无边框原生输入框保留输入法和快捷键，外层绘制搜索图标与圆角。
        internal SearchField(TextBox textBox)
        {
            editor = textBox;
            DoubleBuffered = true;
            TabStop = false;
            AutoSize = false;
            Size = new Size(200, 36);
            editor.BorderStyle = BorderStyle.None;
            editor.BackColor = FluentStyle.Surface;
            editor.GotFocus += delegate { Invalidate(); };
            editor.LostFocus += delegate { Invalidate(); };
            editor.HandleCreated += delegate { NativeMethods.SendMessage(editor.Handle, 0x1501, new IntPtr(1), "搜索记录"); };
            Controls.Add(editor);
            Resize += delegate { LayoutEditor(); };
            editor.FontChanged += delegate { LayoutEditor(); };
            MouseDown += delegate { editor.Focus(); };
            LayoutEditor();
        }

        // 输入框与图标在固定高度的工具栏中垂直居中。
        private void LayoutEditor()
        {
            editor.SetBounds(34, (Height - editor.PreferredHeight) / 2, Math.Max(0, Width - 46), editor.PreferredHeight);
        }

        // 搜索框保留清晰的边界，聚焦时强调蓝色，图标保持垂直居中。
        protected override void OnPaint(PaintEventArgs e)
        {
            FluentStyle.ClearBackground(e.Graphics, Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = FluentStyle.RoundedRectangle(new Rectangle(1, 1, Width - 3, Height - 3), 10))
            {
                using (var brush = new SolidBrush(FluentStyle.Surface)) e.Graphics.FillPath(brush, path);
                using (var pen = new Pen(editor.Focused ? FluentStyle.Accent : FluentStyle.Border)) e.Graphics.DrawPath(pen, path);
            }
            using (var pen = new Pen(FluentStyle.SecondaryText, 1.5f))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                float y = Height / 2f;
                e.Graphics.DrawEllipse(pen, 13, y - 6, 9, 9);
                e.Graphics.DrawLine(pen, 21, y + 2, 25, y + 6);
            }
        }
    }
}
