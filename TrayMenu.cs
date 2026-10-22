using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClipHistory
{
    internal sealed class TrayMenu : ContextMenuStrip
    {
        // 保留原生菜单的键盘、焦点和自动关闭行为，只调整外观。
        internal TrayMenu(Font font)
        {
            Font = font;
            BackColor = FluentStyle.Surface;
            ForeColor = FluentStyle.Text;
            ShowImageMargin = false;
            ShowCheckMargin = true;
            DropShadowEnabled = true;
            Renderer = new TrayMenuRenderer();
            float scale = font.Height / 17f;
            Padding = new Padding(6, 8, 6, 8);
            MinimumSize = new Size((int)Math.Ceiling(260 * scale), 0);
            ItemAdded += delegate(object sender, ToolStripItemEventArgs e)
            {
                int inset = e.Item is ToolStripLabel ? (int)Math.Ceiling(24 * scale) : 0;
                e.Item.Padding = new Padding(inset, (int)Math.Ceiling(7 * scale), (int)Math.Ceiling(12 * scale), (int)Math.Ceiling(7 * scale));
            };
        }

        // 菜单请求 Windows 11 圆角，阴影由原生弹出菜单提供。
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeMethods.ApplyMenuCorners(Handle);
        }
    }

    internal class TrayMenuRenderer : ToolStripRenderer
    {
        // 按系统缩放计算绘制尺寸，保持高 DPI 下的留白和线宽。
        private static int Scale(Graphics graphics, int value)
        {
            return (int)Math.Round(value * graphics.DpiX / 96f);
        }

        // 白色菜单表面，去掉系统默认的渐变和左侧色带。
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(FluentStyle.Surface);
        }

        // 使用细圆角边框，避免默认菜单的厚重边缘。
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = FluentStyle.RoundedRectangle(new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), Scale(e.Graphics, 8)))
            using (var pen = new Pen(FluentStyle.Border)) e.Graphics.DrawPath(pen, path);
        }

        // 悬停和键盘选中共用浅灰圆角底色。
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Enabled || !e.Item.Selected) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new Rectangle(0, 1, e.Item.Width - 1, e.Item.Height - 2);
            using (var path = FluentStyle.RoundedRectangle(bounds, Scale(e.Graphics, 4)))
            using (var brush = new SolidBrush(Color.FromArgb(242, 244, 247))) e.Graphics.FillPath(brush, path);
        }

        // 文字与快捷键按整行垂直居中，与勾选图标和悬停背景对齐。
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextRectangle = new Rectangle(e.TextRectangle.X, 0, e.TextRectangle.Width, e.Item.Height);
            e.TextFormat = (e.TextFormat & ~(TextFormatFlags.Bottom | TextFormatFlags.WordBreak)) |
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
            e.TextColor = !e.Item.Enabled || e.Item is ToolStripLabel || e.Text != e.Item.Text ? FluentStyle.SecondaryText : FluentStyle.Text;
            base.OnRenderItemText(e);
        }

        // 以留白和细线分组，入口与退出分开。
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int inset = Scale(e.Graphics, 10);
            using (var pen = new Pen(Color.FromArgb(220, 229, 242)))
                e.Graphics.DrawLine(pen, inset, e.Item.Height / 2, e.Item.Width - inset, e.Item.Height / 2);
        }

        // 开机启动以清晰勾选标记表示，避免默认的蓝色方框。
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int x = e.ImageRectangle.Left + e.ImageRectangle.Width / 2;
            int y = e.Item.Height / 2;
            using (var pen = new Pen(FluentStyle.Accent, Scale(e.Graphics, 2)))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                e.Graphics.DrawLines(pen, new[] { new Point(x - Scale(e.Graphics, 5), y),
                    new Point(x - Scale(e.Graphics, 1), y + Scale(e.Graphics, 4)), new Point(x + Scale(e.Graphics, 6), y - Scale(e.Graphics, 4)) });
            }
        }
    }
}
