using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClipHistory
{
    internal sealed class FluentButton : Button
    {
        private bool hovered;
        private bool pressed;

        // 使用原生按钮交互，绘制 Fluent 圆角和状态反馈。
        internal FluentButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderColor = FluentStyle.Border;
            FlatAppearance.MouseOverBackColor = FluentStyle.HoverSurface;
            FlatAppearance.MouseDownBackColor = FluentStyle.PressedSurface;
            BackColor = FluentStyle.Surface;
            ForeColor = FluentStyle.Text;
            Cursor = Cursors.Hand;
            DoubleBuffered = true;
        }

        // 毛玻璃区域以 Alpha 绘制按钮和文字，保留悬停与键盘焦点反馈。
        protected override void OnPaint(PaintEventArgs e)
        {
            FluentStyle.ClearBackground(e.Graphics, Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            Color fill = !Enabled ? FluentStyle.Background : pressed ? FlatAppearance.MouseDownBackColor :
                hovered ? FlatAppearance.MouseOverBackColor : BackColor;
            using (var path = FluentStyle.RoundedRectangle(new Rectangle(1, 1, Width - 3, Height - 3), 8))
            using (var brush = new SolidBrush(fill))
            {
                if (fill != FluentStyle.GlassBackground) e.Graphics.FillPath(brush, path);
                if (FlatAppearance.BorderSize > 0)
                    using (var pen = new Pen(FlatAppearance.BorderColor)) e.Graphics.DrawPath(pen, path);
            }
            Color textColor = Enabled ? ForeColor : FluentStyle.SecondaryText;
            if (Parent.BackColor == FluentStyle.GlassBackground)
            {
                using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center,
                    FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter })
                using (var brush = new SolidBrush(textColor))
                    e.Graphics.DrawString(Text, Font, brush, ClientRectangle, format);
            }
            else
                TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, textColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues)
            {
                using (var path = FluentStyle.RoundedRectangle(new Rectangle(3, 3, Width - 7, Height - 7), 6))
                using (var pen = new Pen(FluentStyle.Accent, 1.5f)) e.Graphics.DrawPath(pen, path);
            }
        }

        // 更新按钮悬停状态。
        protected override void OnMouseEnter(System.EventArgs e)
        {
            base.OnMouseEnter(e);
            hovered = true;
            Invalidate();
        }

        // 离开按钮时清除悬停及按下反馈。
        protected override void OnMouseLeave(System.EventArgs e)
        {
            base.OnMouseLeave(e);
            hovered = pressed = false;
            Invalidate();
        }

        // 显示鼠标按下反馈。
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            pressed = e.Button == MouseButtons.Left;
            Invalidate();
        }

        // 释放鼠标后恢复按钮状态。
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            pressed = false;
            Invalidate();
        }
    }
}
