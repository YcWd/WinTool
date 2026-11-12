using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClipHistory
{
    internal enum RecordMenuIcon { Preview, Copy, Paste, Pin, Unpin, Delete }

    internal sealed class RecordMenu : ContextMenuStrip
    {
        private readonly float scale;

        // 使用紧凑的记录操作布局，保留原生菜单的键盘和自动关闭行为。
        internal RecordMenu(Font font)
        {
            Font = font;
            scale = font.Height / 19f;
            BackColor = FluentStyle.Surface;
            ForeColor = FluentStyle.Text;
            ShowImageMargin = ShowCheckMargin = false;
            DropShadowEnabled = true;
            Padding = new Padding((int)Math.Ceiling(6 * scale));
            Renderer = new RecordMenuRenderer();
        }

        // 所有操作使用相同尺寸，图标类型交给渲染器绘制。
        internal void AddAction(string text, RecordMenuIcon icon, EventHandler click)
        {
            var item = new RecordMenuItem(text, icon) { AutoSize = false,
                Size = new Size((int)Math.Ceiling(212 * scale), (int)Math.Ceiling(36 * scale)),
                Margin = new Padding(0, (int)Math.Ceiling(scale), 0, (int)Math.Ceiling(scale)),
                ForeColor = icon == RecordMenuIcon.Delete ? Color.FromArgb(190, 45, 45) : FluentStyle.Text,
                Padding = Padding.Empty };
            item.Click += click;
            Items.Add(item);
        }

        // 菜单圆角交给 Windows 绘制，沿用系统弹出阴影。
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeMethods.ApplyMenuCorners(Handle);
        }

        private sealed class RecordMenuItem : ToolStripMenuItem
        {
            internal readonly RecordMenuIcon Icon;

            // 保留菜单项语义，将图标与文本作为同一操作绘制。
            internal RecordMenuItem(string text, RecordMenuIcon icon) : base(text)
            {
                Icon = icon;
                AccessibleName = text;
            }
        }

        // 使用统一细线绘制查看、剪贴板、置顶和删除操作图标。
        internal static void DrawIcon(Graphics graphics, Rectangle bounds, RecordMenuIcon icon, Color color)
        {
            GraphicsState state = graphics.Save();
            graphics.TranslateTransform(bounds.X, bounds.Y);
            graphics.ScaleTransform(bounds.Width / 20f, bounds.Height / 20f);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(color, 1.1f))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                if (icon == RecordMenuIcon.Preview)
                {
                    using (var eye = new GraphicsPath())
                    {
                        eye.AddBezier(1, 10, 5, 2, 15, 2, 19, 10);
                        eye.AddBezier(19, 10, 15, 18, 5, 18, 1, 10);
                        graphics.DrawPath(pen, eye);
                    }
                    graphics.DrawEllipse(pen, 7, 7, 6, 6);
                }
                else if (icon == RecordMenuIcon.Copy)
                {
                    graphics.DrawLines(pen, new[] { new Point(5, 14), new Point(2, 14),
                        new Point(2, 2), new Point(14, 2), new Point(14, 5) });
                    graphics.DrawRectangle(pen, 6, 6, 12, 12);
                }
                else if (icon == RecordMenuIcon.Paste)
                {
                    graphics.DrawLines(pen, new[] { new Point(6, 4), new Point(3, 4), new Point(3, 18),
                        new Point(17, 18), new Point(17, 4), new Point(14, 4) });
                    graphics.DrawRectangle(pen, 6, 2, 8, 4);
                    graphics.DrawLine(pen, 7, 10, 13, 10);
                    graphics.DrawLine(pen, 7, 14, 13, 14);
                }
                else if (icon == RecordMenuIcon.Pin || icon == RecordMenuIcon.Unpin)
                {
                    graphics.DrawLine(pen, 6, 3, 14, 3);
                    graphics.DrawLines(pen, new[] { new Point(7, 3), new Point(7, 8), new Point(4, 12),
                        new Point(16, 12), new Point(13, 8), new Point(13, 3) });
                    graphics.DrawLine(pen, 10, 12, 10, 18);
                    if (icon == RecordMenuIcon.Unpin) graphics.DrawLine(pen, 3, 3, 17, 17);
                }
                else if (icon == RecordMenuIcon.Delete)
                {
                    graphics.DrawLine(pen, 4, 5, 16, 5);
                    graphics.DrawLines(pen, new[] { new Point(7, 5), new Point(7, 2), new Point(13, 2), new Point(13, 5) });
                    graphics.DrawLines(pen, new[] { new Point(5, 7), new Point(6, 18), new Point(14, 18), new Point(15, 7) });
                    graphics.DrawLine(pen, 8, 8, 8, 15);
                    graphics.DrawLine(pen, 12, 8, 12, 15);
                }
            }
            graphics.Restore(state);
        }

        private sealed class RecordMenuRenderer : TrayMenuRenderer
        {
            // 菜单整体圆角与记录卡片保持一致。
            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                int radius = (int)Math.Round(12 * e.Graphics.DpiX / 96f);
                using (var path = FluentStyle.RoundedRectangle(new Rectangle(0, 0,
                    e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), radius))
                using (var pen = new Pen(FluentStyle.Border)) e.Graphics.DrawPath(pen, path);
            }

            // 悬停与键盘选中使用按钮同款浅灰底色和圆角。
            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                if (!e.Item.Enabled || !e.Item.Selected) return;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                int radius = (int)Math.Round(8 * e.Graphics.DpiX / 96f);
                using (var path = FluentStyle.RoundedRectangle(new Rectangle(0, 0,
                    e.Item.Width - 1, e.Item.Height - 1), radius))
                using (var brush = new SolidBrush(FluentStyle.Background)) e.Graphics.FillPath(brush, path);
            }

            // 自行对齐图标和文字，去掉系统菜单预留的勾选及图片栏。
            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                float scale = e.Graphics.DpiX / 96f;
                int inset = (int)Math.Round(12 * scale);
                int iconSize = (int)Math.Round(18 * scale);
                int textLeft = inset + iconSize + (int)Math.Round(10 * scale);
                var icon = new Rectangle(inset, (e.Item.Height - iconSize) / 2, iconSize, iconSize);
                DrawIcon(e.Graphics, icon, ((RecordMenuItem)e.Item).Icon, e.Item.ForeColor);
                TextRenderer.DrawText(e.Graphics, e.Item.Text, e.TextFont,
                    new Rectangle(textLeft, 0, e.Item.Width - textLeft - inset, e.Item.Height),
                    e.Item.Enabled ? e.Item.ForeColor : FluentStyle.SecondaryText,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                    TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }

        }
    }
}
