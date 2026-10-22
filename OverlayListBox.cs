using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClipHistory
{
    internal sealed class OverlayListBox : ListBox
    {
        private readonly Control thumb = new Control();
        internal readonly Panel ScrollBar = new Panel { Dock = DockStyle.Right, Width = 12,
            BackColor = FluentStyle.Background, TabStop = false };
        private int dragOffset;
        private int wheelDelta;
        private bool updating;
        internal bool HorizontalCards { get; private set; }
        internal event EventHandler ViewScrolled;

        // 滑块放在列表外的独立侧栏，保留原有滚动和拖动交互。
        internal OverlayListBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            thumb.BackColor = Color.FromArgb(153, 153, 153);
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
                dragOffset = HorizontalCards ? e.X : e.Y;
                thumb.Capture = true;
            };
            thumb.MouseMove += delegate
            {
                if (!thumb.Capture) return;
                int travel = HorizontalCards ? ScrollBar.ClientSize.Width - 8 - thumb.Width :
                    ScrollBar.ClientSize.Height - 8 - thumb.Height;
                if (travel <= 0) return;
                Point mouse = ScrollBar.PointToClient(Control.MousePosition);
                int position = (HorizontalCards ? mouse.X : mouse.Y) - dragOffset - 4;
                TopIndex = (int)Math.Round(Math.Max(0, Math.Min(travel, position)) * (double)MaximumTopIndex / travel);
            };
            thumb.MouseUp += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) thumb.Capture = false;
            };
            thumb.MouseCaptureChanged += delegate { thumb.BackColor = Color.FromArgb(153, 153, 153); };
        }

        // 底部模式采用单行等宽列，继续使用原生选中、命中与键盘导航。
        internal void SetHorizontalCards(bool enabled)
        {
            HorizontalCards = enabled;
            wheelDelta = 0;
            ScrollBar.Dock = enabled ? DockStyle.Bottom : DockStyle.Right;
            if (enabled) ScrollBar.Height = 12; else ScrollBar.Width = 12;
            MultiColumn = enabled;
            ScrollBar.Visible = !enabled;
            UpdateCardSize();
            if (Items.Count > 0) TopIndex = 0;
            UpdateScrollThumb();
            Invalidate();
        }

        // 扣除四边各 4px 间距后，横排卡片保持 4:3，普通列表维持原行高。
        private void UpdateCardSize()
        {
            if (HorizontalCards)
            {
                int cardHeight = (Math.Max(11, Math.Min(255, ClientSize.Height)) - 8) / 3 * 3;
                ItemHeight = cardHeight + 8;
                ColumnWidth = cardHeight / 3 * 4 + 8;
            }
            else ItemHeight = 104;
        }

        // 侧栏或窗口尺寸改变时同步 4:3 卡片和滑块尺寸。
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (HorizontalCards) UpdateCardSize();
            UpdateScrollThumb();
        }

        // 创建时移除系统滚动条样式，使卡片使用完整客户区宽度。
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.Style &= ~(0x00200000 | 0x00100000 | 0x00800000); // 滚动条及 WS_BORDER
                parameters.ExStyle &= ~(0x00000200 | 0x00020000); // WS_EX_CLIENTEDGE | WS_EX_STATICEDGE
                return parameters;
            }
        }

        private int MaximumTopIndex
        {
            get { return Math.Max(0, Items.Count - Math.Max(1, HorizontalCards ? ClientSize.Width / ColumnWidth : ClientSize.Height / ItemHeight)); }
        }

        // 每个滚轮刻度移动一张卡片，累积触控板输入，避免一次跳过多张大卡片。
        private void ScrollWheel(int delta)
        {
            wheelDelta += delta;
            int steps = wheelDelta / 120;
            wheelDelta %= 120;
            int lines = SystemInformation.MouseWheelScrollLines < 0 ? Math.Max(1,
                HorizontalCards ? ClientSize.Width / ColumnWidth : ClientSize.Height / ItemHeight) : 1;
            if (SystemInformation.MouseWheelScrollLines == 0) return;
            if (steps == 0 || Items.Count == 0) return;
            int top = Math.Max(0, Math.Min(MaximumTopIndex, TopIndex - steps * lines));
            if (top != TopIndex) TopIndex = top;
        }

        // 底部模式不显示滑块，普通模式按可见比例更新侧栏滑块。
        internal void UpdateScrollThumb()
        {
            if (thumb == null || !IsHandleCreated || updating) return;
            if (HorizontalCards)
            {
                thumb.Visible = false;
                return;
            }
            int viewport = HorizontalCards ? ClientSize.Width : ClientSize.Height;
            int extent = HorizontalCards ? ColumnWidth : ItemHeight;
            int trackHeight = (HorizontalCards ? ScrollBar.ClientSize.Width : ScrollBar.ClientSize.Height) - 8;
            bool overflow = Items.Count * (long)extent > viewport && trackHeight > 0;
            thumb.Visible = overflow;
            if (!overflow) return;
            int height = Math.Min(trackHeight, Math.Max(28,
                (int)(trackHeight * (double)viewport / (Items.Count * (long)extent))));
            int top = 4 + (int)Math.Round((trackHeight - height) * (double)TopIndex / Math.Max(1, MaximumTopIndex));
            if (HorizontalCards) thumb.SetBounds(top, (ScrollBar.ClientSize.Height - 6) / 2, height, 6);
            else thumb.SetBounds((ScrollBar.ClientSize.Width - 6) / 2, top, 6, height);
            thumb.BringToFront();
        }

        // 整个可见列表在同一缓冲区内绘制，背景和卡片一次提交到屏幕。
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            for (int index = TopIndex; index < Items.Count; index++)
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

        // 原生列表处理选中和内容更新后同步滑块，滚轮由自定义逻辑接管。
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x020A) // WM_MOUSEWHEEL
            {
                ScrollWheel((short)(message.WParam.ToInt64() >> 16));
                message.Result = IntPtr.Zero;
                return;
            }
            if (HorizontalCards && message.Msg == 0x020E) // WM_MOUSEHWHEEL
            {
                ScrollWheel(-(short)(message.WParam.ToInt64() >> 16));
                message.Result = IntPtr.Zero;
                return;
            }
            if (message.Msg == 0x000B) updating = message.WParam == IntPtr.Zero;
            bool scrolling = message.Msg == 0x0197 && !updating;
            if (scrolling) NativeMethods.SendMessage(Handle, 0x000B, IntPtr.Zero, IntPtr.Zero);
            base.WndProc(ref message);
            if (scrolling)
            {
                NativeMethods.SendMessage(Handle, 0x000B, new IntPtr(1), IntPtr.Zero);
                NativeMethods.InvalidateWithoutErase(Handle, ClientRectangle);
            }
            if ((message.Msg == 0x0100 || message.Msg == 0x0115 || message.Msg == 0x0114) && !updating)
                NativeMethods.InvalidateWithoutErase(Handle, ClientRectangle);
            // 可见记录变化后通知卡片清除旧按钮的悬停状态。
            if ((message.Msg == 0x0115 || message.Msg == 0x0114 || message.Msg == 0x0197 || message.Msg == 0x0100) && ViewScrolled != null)
                ViewScrolled(this, EventArgs.Empty);
            switch (message.Msg)
            {
                case 0x0005: // WM_SIZE
                case 0x000B: // WM_SETREDRAW
                case 0x0100: // WM_KEYDOWN
                case 0x0115: // WM_VSCROLL
                case 0x0114: // WM_HSCROLL
                case 0x0180: // LB_ADDSTRING
                case 0x0181: // LB_INSERTSTRING
                case 0x0182: // LB_DELETESTRING
                case 0x0184: // LB_RESETCONTENT
                case 0x0186: // LB_SETCURSEL
                case 0x0197: // LB_SETTOPINDEX
                case 0x01A7: // LB_SETCOUNT
                    UpdateScrollThumb();
                    break;
            }
        }
    }
}
