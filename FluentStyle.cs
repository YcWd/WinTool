using System.Drawing;
using System.Drawing.Drawing2D;

namespace ClipHistory
{
    internal static class FluentStyle
    {
        internal static readonly Color Background = Color.FromArgb(234, 240, 247);
        internal static readonly Color Surface = Color.White;
        internal static readonly Color Text = Color.FromArgb(24, 40, 62);
        internal static readonly Color SecondaryText = Color.FromArgb(82, 100, 125);
        internal static readonly Color Accent = Color.FromArgb(37, 99, 235);
        internal static readonly Color Border = Color.FromArgb(203, 216, 231);
        internal static readonly Color ControlFill = Color.FromArgb(223, 232, 243);
        internal static readonly Color HoverSurface = Color.FromArgb(243, 247, 253);
        internal static readonly Color PressedSurface = Color.FromArgb(220, 232, 252);
        internal static readonly Color SelectedSurface = Color.FromArgb(232, 240, 255);
        internal static readonly Color HoverBorder = Color.FromArgb(143, 169, 204);

        // 绘制控件和记录卡片共用的圆角路径。
        internal static GraphicsPath RoundedRectangle(Rectangle rectangle, int radius)
        {
            var path = new GraphicsPath();
            int diameter = radius * 2;
            path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
