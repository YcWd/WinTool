using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClipHistory
{
    internal sealed class OverlayListBox : ListBox
    {
        internal const int CardMargin = 8;
        internal int NormalItemHeight = 160 + CardMargin * 2;
        internal Func<int, float> RemovalProgress;
        private readonly Control thumb = new Control();
        internal readonly Panel ScrollBar = new Panel { Dock = DockStyle.Right, Width = 12,
            BackColor = FluentStyle.Background, TabStop = false };
        private int dragOffset;
        private double wheelRemainder;
        private int scrollPosition;
        private bool updating;
        internal bool HorizontalCards { get; private set; }
        internal event EventHandler ViewScrolled;

        private int ItemExtent { get { return Math.Max(1, HorizontalCards ? ColumnWidth : NormalItemHeight); } }
        private int Viewport { get { return HorizontalCards ? ClientSize.Width : ClientSize.Height; } }
        private int MaximumScroll { get { return Math.Max(0, ItemStart(Items.Count) - Viewport); } }

        // 删除中的记录逐步收起占位，后续卡片同步滑动补齐。
        private int ItemLength(int index)
        {
            float progress = RemovalProgress == null ? 0 : RemovalProgress(index);
            return (int)Math.Round(ItemExtent * (1 - progress));
        }

        // 绘制、滚动和点击统一累计动画中的实际占位长度。
        private int ItemStart(int index)
        {
            int position = 0;
            for (int item = 0; item < index; item++) position += ItemLength(item);
            return position;
        }

        // 动画更新后同步滚动边界和滑块，再重绘可见记录。
        internal void RefreshAnimationLayout()
        {
            ScrollPosition = scrollPosition;
            UpdateScrollThumb();
            Invalidate();
        }

        // 使用像素偏移绘制和命中卡片，滚动可以停在卡片中间。
        internal int ScrollPosition
        {
            get { return scrollPosition; }
            set
            {
                int position = Math.Max(0, Math.Min(MaximumScroll, value));
                if (scrollPosition == position) return;
                scrollPosition = position;
                if (updating) return;
                Invalidate();
                UpdateScrollThumb();
                if (ViewScrolled != null) ViewScrolled(this, EventArgs.Empty);
            }
        }

        // 自行处理鼠标命中，滑块拖动与绘制共享同一像素偏移。
        internal OverlayListBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.UserMouse | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            thumb.BackColor = Color.FromArgb(153, 153, 153);
            thumb.Paint += delegate(object sender, PaintEventArgs e) { e.Graphics.Clear(thumb.BackColor); };
            thumb.Cursor = Cursors.Hand;
            thumb.TabStop = false;
            thumb.AccessibleName = "滚动历史记录";
            thumb.AccessibleRole = AccessibleRole.ScrollBar;
            thumb.Visible = false;
            ScrollBar.Controls.Add(thumb);
            ScrollBar.SizeChanged += delegate { UpdateScrollThumb(); };
            ScrollBar.MouseWheel += delegate(object sender, MouseEventArgs e) { ScrollWheel(e.Delta); };
            thumb.MouseWheel += delegate(object sender, MouseEventArgs e) { ScrollWheel(e.Delta); };
            thumb.SizeChanged += delegate
            {
                using (var path = FluentStyle.RoundedRectangle(new Rectangle(Point.Empty, thumb.Size), Math.Min(thumb.Width, thumb.Height) / 2))
                {
                    Region previous = thumb.Region;
                    thumb.Region = new Region(path);
                    if (previous != null) previous.Dispose();
                }
            };
            thumb.MouseEnter += delegate { thumb.BackColor = Color.FromArgb(112, 112, 112); };
            thumb.MouseLeave += delegate { if (!thumb.Capture) thumb.BackColor = Color.FromArgb(153, 153, 153); };
            thumb.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                Focus();
                dragOffset = e.Y;
                thumb.Capture = true;
            };
            thumb.MouseMove += delegate
            {
                if (!thumb.Capture) return;
                int travel = ScrollBar.ClientSize.Height - 8 - thumb.Height;
                if (travel <= 0) return;
                int position = ScrollBar.PointToClient(Control.MousePosition).Y - dragOffset - 4;
                ScrollPosition = (int)Math.Round(Math.Max(0, Math.Min(travel, position)) * (double)MaximumScroll / travel);
            };
            thumb.MouseUp += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) thumb.Capture = false;
            };
            thumb.MouseCaptureChanged += delegate { thumb.BackColor = Color.FromArgb(153, 153, 153); };
        }

        // 切换列表方向并重新计算卡片尺寸及像素范围。
        internal void SetHorizontalCards(bool enabled)
        {
            HorizontalCards = enabled;
            wheelRemainder = 0;
            ScrollBar.Dock = enabled ? DockStyle.Bottom : DockStyle.Right;
            if (enabled) ScrollBar.Height = 12; else ScrollBar.Width = 12;
            MultiColumn = enabled;
            ScrollBar.Visible = !enabled;
            UpdateCardSize();
            ScrollPosition = 0;
            UpdateScrollThumb();
            Invalidate();
        }

        // 横排按可用高度生成正方形，普通模式使用固定行高。
        private void UpdateCardSize()
        {
            if (HorizontalCards)
            {
                ColumnWidth = Math.Max(1, ClientSize.Height);
                // 原生行高上限为 255px，实际卡片高度由 ColumnWidth 绘制。
                ItemHeight = Math.Min(255, ColumnWidth);
            }
            else ItemHeight = Math.Min(255, NormalItemHeight);
        }

        // 窗口尺寸变化后收紧滚动边界，并同步卡片和滑块。
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (HorizontalCards) UpdateCardSize();
            if (!IsHandleCreated) return;
            ScrollPosition = scrollPosition;
            UpdateScrollThumb();
        }

        // 创建时移除系统滚动条样式，使卡片使用完整客户区宽度。
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.Style &= ~(0x00200000 | 0x00100000 | 0x00800000);
                parameters.ExStyle &= ~(0x00000200 | 0x00020000);
                return parameters;
            }
        }

        // 绘制、滚动与鼠标命中共用卡片尺寸，首尾记录允许部分可见。
        public new Rectangle GetItemRectangle(int index)
        {
            if (index < 0 || index >= Items.Count) throw new ArgumentOutOfRangeException("index");
            int position = ItemStart(index) - scrollPosition - (ItemExtent - ItemLength(index)) / 2;
            return HorizontalCards ? new Rectangle(position, 0, ColumnWidth, ColumnWidth) :
                new Rectangle(0, position, ClientSize.Width, ItemExtent);
        }

        // 反算像素位置对应的记录，避免部分滚动后点击相邻卡片。
        public new int IndexFromPoint(Point point)
        {
            if (!ClientRectangle.Contains(point)) return NoMatches;
            int position = (HorizontalCards ? point.X : point.Y) + scrollPosition;
            for (int index = 0; index < Items.Count; index++)
            {
                int length = ItemLength(index);
                if (position < length) return index;
                position -= length;
            }
            return NoMatches;
        }

        // 坐标重载与 Point 重载共用命中逻辑。
        public new int IndexFromPoint(int x, int y)
        {
            return IndexFromPoint(new Point(x, y));
        }

        // 鼠标选中按绘制位置命中，保持当前像素偏移。
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right)
            {
                Focus();
                int index = IndexFromPoint(e.Location);
                if (index != NoMatches) SelectedIndex = index;
            }
            base.OnMouseDown(e);
        }

        // 键盘导航显示目标记录，超出视口的大卡片按起始边缘对齐。
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Handled || Items.Count == 0) return;
            int index = SelectedIndex;
            int page = Math.Max(1, Viewport / ItemExtent);
            switch (e.KeyCode)
            {
                case Keys.Left:
                case Keys.Up: index--; break;
                case Keys.Right:
                case Keys.Down: index++; break;
                case Keys.PageUp: index -= page; break;
                case Keys.PageDown: index += page; break;
                case Keys.Home: index = 0; break;
                case Keys.End: index = Items.Count - 1; break;
                default: return;
            }
            SelectedIndex = Math.Max(0, Math.Min(Items.Count - 1, index));
            int start = ItemStart(SelectedIndex);
            if (start < scrollPosition || ItemExtent > Viewport) ScrollPosition = start;
            else if (start + ItemExtent > scrollPosition + Viewport) ScrollPosition = start + ItemExtent - Viewport;
            e.Handled = e.SuppressKeyPress = true;
        }

        // 原生选中状态改变后刷新自绘卡片。
        protected override void OnSelectedIndexChanged(EventArgs e)
        {
            base.OnSelectedIndexChanged(e);
            if (!updating) Invalidate();
        }

        // 按系统滚轮行数换算像素，保留触控板的小幅度输入。
        private void ScrollWheel(int delta)
        {
            int lines = SystemInformation.MouseWheelScrollLines;
            if (lines == 0 || Items.Count == 0) return;
            double distance = lines < 0 ? Viewport : Font.Height * lines;
            wheelRemainder -= delta * distance / 120.0;
            int pixels = (int)wheelRemainder;
            wheelRemainder -= pixels;
            ScrollPosition += pixels;
        }

        // 纵向滑块按实际内容长度和像素偏移计算，底部模式沿用横向滚动。
        internal void UpdateScrollThumb()
        {
            if (thumb == null || !IsHandleCreated || updating) return;
            if (HorizontalCards)
            {
                thumb.Visible = false;
                return;
            }
            int trackHeight = ScrollBar.ClientSize.Height - 8;
            bool overflow = MaximumScroll > 0 && Viewport > 0 && trackHeight > 0;
            thumb.Visible = overflow;
            if (!overflow) return;
            int height = Math.Min(trackHeight, Math.Max(28,
                (int)(trackHeight * (double)Viewport / ItemStart(Items.Count))));
            int top = 4 + (int)Math.Round((trackHeight - height) * (double)scrollPosition / MaximumScroll);
            thumb.SetBounds((ScrollBar.ClientSize.Width - 6) / 2, top, 6, height);
            thumb.BringToFront();
        }

        // 按动画后的坐标绘制可见记录，删除占位收起时也保持连续布局。
        protected override void OnPaint(PaintEventArgs e)
        {
            FluentStyle.ClearBackground(e.Graphics, BackColor);
            for (int index = 0; index < Items.Count; index++)
            {
                Rectangle bounds = GetItemRectangle(index);
                if (HorizontalCards ? bounds.Left >= ClientSize.Width : bounds.Top >= ClientSize.Height) break;
                if (!bounds.IntersectsWith(e.ClipRectangle)) continue;
                DrawItemState state = index == SelectedIndex ? DrawItemState.Selected : DrawItemState.None;
                if (Focused && index == SelectedIndex) state |= DrawItemState.Focus;
                OnDrawItem(new DrawItemEventArgs(e.Graphics, Font, bounds, index, state, ForeColor, BackColor));
            }
        }

        // 背景随卡片一起绘制，避免独立清底造成闪烁。
        protected override void OnPaintBackground(PaintEventArgs e) { }

        // 滚轮由像素逻辑接管，数据批量更新后统一同步滚动范围。
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x020A)
            {
                ScrollWheel((short)(message.WParam.ToInt64() >> 16));
                message.Result = IntPtr.Zero;
                return;
            }
            if (HorizontalCards && message.Msg == 0x020E)
            {
                ScrollWheel(-(short)(message.WParam.ToInt64() >> 16));
                message.Result = IntPtr.Zero;
                return;
            }
            // 自绘只由 OnPaint 提交，忽略原生列表按整行坐标发出的绘制请求。
            if (message.Msg == 0x202B)
            {
                message.Result = new IntPtr(1);
                return;
            }
            if (message.Msg == 0x000B) updating = message.WParam == IntPtr.Zero;
            base.WndProc(ref message);
            switch (message.Msg)
            {
                case 0x0007: // WM_SETFOCUS
                case 0x0008: // WM_KILLFOCUS
                case 0x000B: // WM_SETREDRAW
                case 0x0180: // LB_ADDSTRING
                case 0x0181: // LB_INSERTSTRING
                case 0x0182: // LB_DELETESTRING
                case 0x0184: // LB_RESETCONTENT
                case 0x01A7: // LB_SETCOUNT
                    ScrollPosition = scrollPosition;
                    UpdateScrollThumb();
                    if (!updating) Invalidate();
                    break;
            }
        }
    }
}
