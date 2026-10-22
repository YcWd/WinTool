using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace ClipHistory
{
    internal sealed class TranslationForm : Form
    {
        private readonly string root;
        private string apiKey = "";
        private string model = "";
        private readonly TextBox input = new TextBox { Multiline = true, AcceptsReturn = true,
            ScrollBars = ScrollBars.None, Dock = DockStyle.Fill };
        private readonly TextBox output = new TextBox { Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.None, WordWrap = false, Dock = DockStyle.Fill, BackColor = FluentStyle.Surface };
        private bool toChinese;
        private string englishResult;
        private string naming = "camelCase";
        private readonly TableLayoutPanel namingOptions = new TableLayoutPanel();
        private readonly Button pinButton = new FluentButton();
        private readonly ToolTip tips = new ToolTip();
        private readonly Font symbolFont = new Font("Segoe MDL2 Assets", 10);
        private readonly Font smallFont = new Font("Microsoft YaHei UI", 9);
        private readonly Button translate = new FluentButton { Text = "翻译", Width = 96, Height = 32 };
        private readonly Button cancel = new FluentButton { Text = "取消请求", Width = 96, Height = 32, Enabled = false };
        private readonly Button copy = new FluentButton { Text = "复制结果", Width = 100, Height = 32, Enabled = false };
        private readonly Label status = new Label { Dock = DockStyle.Fill, ForeColor = FluentStyle.SecondaryText };
        private CancellationTokenSource requestCancellation;

        // 翻译窗口独立于剪贴板窗口，保留本次输入及结果直到程序退出。
        internal TranslationForm(string dataRoot)
        {
            root = dataRoot;
            Text = "开发字段翻译";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.None;
            Padding = new Padding(8);
            DoubleBuffered = true;
            ClientSize = new Size(600, 533);
            MinimumSize = new Size(520, 500);
            BackColor = FluentStyle.Background;
            ForeColor = FluentStyle.Text;
            var font = new Font("Microsoft YaHei UI", 10);
            Font = font;
            Disposed += delegate { font.Dispose(); };
            BuildLayout();
            LoadSettings();
            Shown += delegate { if (String.IsNullOrWhiteSpace(apiKey)) OpenConfiguration(); };
            KeyPreview = true;
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Control && e.KeyCode == Keys.Enter)
                {
                    e.Handled = e.SuppressKeyPress = true;
                    translate.PerformClick();
                }
                else if (e.KeyCode == Keys.Escape) { e.Handled = e.SuppressKeyPress = true; Close(); }
            };
        }

        // 翻译方向与命名格式均可直接点选，配置通过独立入口打开。
        private void BuildLayout()
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 12, 0),
                ColumnCount = 1, RowCount = 9 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            Controls.Add(layout);

            var heading = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            var title = new Label { Text = "开发字段翻译", AutoSize = true, Location = new Point(4, 6) };
            heading.Controls.Add(title);
            heading.MouseDown += DragWindow;
            title.MouseDown += DragWindow;
            var windowActions = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 108, WrapContents = false };
            pinButton.Text = "\uE718";
            pinButton.Click += delegate { TogglePin(); };
            var minimize = new FluentButton { Text = "\uE921" };
            minimize.Click += delegate { WindowState = FormWindowState.Minimized; };
            var close = new FluentButton { Text = "\uE8BB" };
            close.Click += delegate { Close(); };
            foreach (Button button in new Button[] { pinButton, minimize, close })
            {
                button.Size = new Size(32, 28);
                button.Font = symbolFont;
                button.Margin = new Padding(2, 0, 2, 0);
                button.FlatAppearance.BorderSize = 0;
                button.BackColor = BackColor;
                button.ForeColor = FluentStyle.SecondaryText;
                windowActions.Controls.Add(button);
            }
            pinButton.AccessibleName = "切换窗口置顶";
            minimize.AccessibleName = "最小化";
            close.AccessibleName = "收起到托盘";
            tips.SetToolTip(pinButton, "置顶窗口");
            tips.SetToolTip(minimize, "最小化");
            tips.SetToolTip(close, "收起到托盘");
            heading.Controls.Add(windowActions);
            layout.Controls.Add(heading, 0, 0);

            var directionBar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            var tabSurface = new RoundedPanel { Size = new Size(340, 34), BackColor = FluentStyle.ControlFill,
                Padding = new Padding(3), Margin = new Padding(0, 3, 8, 7) };
            var tabs = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                Margin = Padding.Empty, BackColor = FluentStyle.ControlFill, AccessibleName = "翻译方向" };
            tabs.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            string[] directions = { "中文 → 英文字段", "英文字段 → 中文" };
            for (int index = 0; index < directions.Length; index++)
            {
                tabs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                var tab = new SegmentTab { Text = directions[index], Dock = DockStyle.Fill,
                    Font = smallFont, Margin = new Padding(1, 0, 1, 0), Tag = index,
                    Cursor = Cursors.Hand, AccessibleName = directions[index] };
                tab.CheckedChanged += delegate
                {
                    if (!tab.Checked) return;
                    toChinese = (int)tab.Tag == 1;
                    namingOptions.Enabled = !toChinese;
                };
                tabs.Controls.Add(tab, index, 0);
                tab.Checked = index == 0;
            }
            tabSurface.Controls.Add(tabs);
            var preferences = new FluentButton { Text = "配置", Size = new Size(80, 32),
                Margin = new Padding(0, 4, 0, 8), AccessibleName = "DeepSeek API 设置" };
            preferences.Click += delegate { OpenConfiguration(); };
            tips.SetToolTip(preferences, "配置 DeepSeek API Key 和模型");
            directionBar.Controls.AddRange(new Control[] { tabSurface, preferences });
            layout.Controls.Add(directionBar, 0, 1);
            layout.Controls.Add(new Label { Text = "待翻译字段（每行一个）", AutoSize = true }, 0, 2);
            input.AccessibleName = "待翻译字段，每行一个";
            layout.Controls.Add(new RoundedTextField(input) { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 3);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 0) };
            translate.BackColor = FluentStyle.Accent;
            translate.ForeColor = Color.White;
            translate.FlatAppearance.BorderSize = 0;
            translate.FlatAppearance.MouseOverBackColor = Color.FromArgb(0, 97, 205);
            translate.FlatAppearance.MouseDownBackColor = Color.FromArgb(0, 82, 180);
            translate.Click += async delegate { await TranslateAsync(); };
            cancel.Click += delegate { requestCancellation.Cancel(); };
            actions.Controls.AddRange(new Control[] { translate, cancel });
            tips.SetToolTip(translate, "翻译（Ctrl+Enter）");
            layout.Controls.Add(actions, 0, 4);
            layout.Controls.Add(new Label { Text = "翻译结果", AutoSize = true }, 0, 5);
            output.AccessibleName = "翻译结果";
            layout.Controls.Add(new RoundedTextField(output) { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 6);
            copy.Click += delegate { CopyResult(); };
            var namingSurface = new RoundedPanel { Dock = DockStyle.Fill, BackColor = FluentStyle.ControlFill,
                Padding = new Padding(3), Margin = new Padding(0, 0, 8, 0) };
            namingOptions.Dock = DockStyle.Fill;
            namingOptions.ColumnCount = 5;
            namingOptions.RowCount = 1;
            namingOptions.Margin = Padding.Empty;
            namingOptions.BackColor = FluentStyle.ControlFill;
            namingOptions.AccessibleName = "英文字段命名格式";
            namingOptions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            string[] styles = { "camelCase", "PascalCase", "snake_case", "UPPER_SNAKE_CASE", "kebab-case" };
            string[] labels = { "小驼峰", "大驼峰", "下划线", "大写下划线", "短横线" };
            string[] examples = { "userName", "UserName", "user_name", "USER_NAME", "user-name" };
            for (int index = 0; index < styles.Length; index++)
            {
                namingOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
                var option = new SegmentTab { Text = labels[index], Tag = styles[index],
                    Dock = DockStyle.Fill, Font = smallFont, Margin = new Padding(1, 0, 1, 0),
                    Cursor = Cursors.Hand, AccessibleName = "命名格式：" + labels[index] };
                option.CheckedChanged += delegate
                {
                    if (!option.Checked) return;
                    naming = (string)option.Tag;
                    ApplyNamingFormat();
                };
                tips.SetToolTip(option, labels[index] + "，示例：" + examples[index]);
                namingOptions.Controls.Add(option, index, 0);
                option.Checked = styles[index] == naming;
            }
            namingSurface.Controls.Add(namingOptions);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                Padding = new Padding(0, 6, 0, 6), Margin = Padding.Empty };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            copy.Dock = DockStyle.Fill;
            copy.Margin = Padding.Empty;
            footer.Controls.Add(namingSurface, 0, 0);
            footer.Controls.Add(copy, 1, 0);
            layout.Controls.Add(footer, 0, 7);
            status.Font = smallFont;
            layout.Controls.Add(status, 0, 8);
            status.Text = "DeepSeek · Ctrl+Enter 翻译；仅提交当前输入的字段。";
        }

        // 每次从快捷键或托盘打开后，将输入焦点放回第一个文本框。
        internal void FocusInput()
        {
            input.Focus();
        }

        // 基于原始英文结果逐行切换格式，避免来回切换丢失分词边界。
        private void ApplyNamingFormat()
        {
            if (englishResult == null) return;
            output.Text = Regex.Replace(englishResult, @"[^\r\n]+", match => FormatIdentifier(match.Value, naming));
        }

        // 拆分驼峰、缩写和分隔符，再按目标规则组合单个字段。
        private static string FormatIdentifier(string value, string style)
        {
            string separated = Regex.Replace(value, @"([A-Z]+)([A-Z][a-z])", "$1 $2");
            separated = Regex.Replace(separated, @"([a-z0-9])([A-Z])", "$1 $2");
            string[] words = Regex.Split(separated, @"[^\p{L}\p{Nd}]+")
                .Where(word => word.Length > 0).Select(word => word.ToLowerInvariant()).ToArray();
            if (words.Length == 0) return value;
            if (style == "snake_case") return string.Join("_", words);
            if (style == "UPPER_SNAKE_CASE") return string.Join("_", words).ToUpperInvariant();
            if (style == "kebab-case") return string.Join("-", words);
            string[] capitalized = words.Select(word => char.ToUpperInvariant(word[0]) + word.Substring(1)).ToArray();
            if (style == "PascalCase") return string.Concat(capitalized);
            return words[0] + string.Concat(capitalized.Skip(1));
        }

        // 与剪贴板标题栏使用相同的图标和置顶状态。
        private void TogglePin()
        {
            TopMost = !TopMost;
            pinButton.Text = TopMost ? "\uE77A" : "\uE718";
            pinButton.BackColor = TopMost ? Color.FromArgb(222, 234, 255) : BackColor;
            pinButton.ForeColor = TopMost ? FluentStyle.Accent : FluentStyle.SecondaryText;
            pinButton.AccessibleName = TopMost ? "取消窗口置顶" : "置顶窗口";
            tips.SetToolTip(pinButton, pinButton.AccessibleName);
        }

        // 无系统标题栏时，通过顶部空白或应用名称拖动窗口。
        private void DragWindow(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero);
        }

        // 保留系统窗口框架，由 DWM 提供与剪贴板相同的圆角及阴影。
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.Style |= NativeMethods.WS_CAPTION | NativeMethods.WS_THICKFRAME;
                return parameters;
            }
        }

        // 初始化系统窗口效果并刷新非客户区。
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeMethods.ApplyWindowChrome(Handle);
            NativeMethods.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0037);
        }

        // 隐藏原生标题栏，保留窗口边缘的拖动缩放命中区域。
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == NativeMethods.WM_NCCALCSIZE && message.WParam != IntPtr.Zero)
            {
                message.Result = IntPtr.Zero;
                return;
            }
            if (message.Msg == NativeMethods.WM_DWMCOMPOSITIONCHANGED) NativeMethods.ApplyWindowChrome(Handle);
            base.WndProc(ref message);
            if (message.Msg == 0x84 && message.Result == new IntPtr(1))
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
        }

        // 读取独立配置，读取失败时在窗口中明确提示。
        private void LoadSettings()
        {
            try
            {
                var settings = TranslationSettings.Load(root);
                apiKey = settings.ReadApiKey();
                model = settings.Model;
            }
            catch (IOException exception) { status.Text = "配置读取失败：" + exception.Message; }
            catch (UnauthorizedAccessException exception) { status.Text = "配置读取失败：" + exception.Message; }
            catch (SerializationException exception) { status.Text = "配置格式错误：" + exception.Message; }
            catch (CryptographicException) { status.Text = "无法解密 API Key，请重新填写并保存配置。"; }
            catch (FormatException) { status.Text = "API Key 配置格式错误，请重新填写并保存配置。"; }
        }

        // 在独立弹窗中编辑配置，仅保存成功后更新当前翻译使用的密钥和模型。
        private void OpenConfiguration()
        {
            using (var dialog = new Form { Text = "DeepSeek 配置", ClientSize = new Size(540, 252),
                FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
                ShowInTaskbar = false, StartPosition = FormStartPosition.CenterParent,
                BackColor = FluentStyle.Background, ForeColor = FluentStyle.Text, Font = Font })
            {
                var config = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20),
                    ColumnCount = 2, RowCount = 5 };
                config.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
                config.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                foreach (int height in new[] { 44, 44, 28, 54, 42 })
                    config.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
                dialog.Controls.Add(config);
                var keyEditor = new TextBox { Text = apiKey, UseSystemPasswordChar = true,
                    AccessibleName = "DeepSeek API Key" };
                var modelEditor = new TextBox { Text = model, AccessibleName = "DeepSeek 模型名称" };
                config.Controls.Add(new Label { Text = "API Key", Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
                config.Controls.Add(new RoundedTextField(keyEditor) { Dock = DockStyle.Fill }, 1, 0);
                config.Controls.Add(new Label { Text = "模型名称", Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
                config.Controls.Add(new RoundedTextField(modelEditor) { Dock = DockStyle.Fill }, 1, 1);
                config.Controls.Add(new Label { Text = "DeepSeek · deepseek-flash / deepseek-v4-pro",
                    Dock = DockStyle.Fill, Font = smallFont, ForeColor = FluentStyle.SecondaryText }, 1, 2);
                var error = new Label { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(180, 40, 40) };
                config.Controls.Add(error, 0, 3);
                config.SetColumnSpan(error, 2);
                var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
                    WrapContents = false, Margin = Padding.Empty };
                var save = new FluentButton { Text = "保存配置", Size = new Size(100, 32) };
                var dismiss = new FluentButton { Text = "取消", Size = new Size(80, 32), DialogResult = DialogResult.Cancel };
                save.Click += delegate
                {
                    string updatedKey = keyEditor.Text.Trim();
                    string updatedModel = modelEditor.Text.Trim();
                    if (updatedKey.Length == 0 || updatedModel.Length == 0)
                    {
                        error.Text = "请填写 DeepSeek API Key 和模型名称。";
                        if (updatedKey.Length == 0) keyEditor.Focus();
                        else modelEditor.Focus();
                        return;
                    }
                    try
                    {
                        TranslationSettings.Save(root, updatedKey, updatedModel);
                        apiKey = updatedKey;
                        model = updatedModel;
                        status.Text = "配置已保存，API Key 已加密。";
                        dialog.DialogResult = DialogResult.OK;
                    }
                    catch (IOException exception) { error.Text = "配置保存失败：" + exception.Message; }
                    catch (UnauthorizedAccessException exception) { error.Text = "配置保存失败：" + exception.Message; }
                    catch (CryptographicException exception) { error.Text = "密钥加密失败：" + exception.Message; }
                };
                actions.Controls.AddRange(new Control[] { save, dismiss });
                config.Controls.Add(actions, 0, 4);
                config.SetColumnSpan(actions, 2);
                dialog.AcceptButton = save;
                dialog.CancelButton = dismiss;
                dialog.Shown += delegate { keyEditor.Focus(); };
                dialog.ShowDialog(this);
            }
        }

        // 调用前检查必填配置，缺失时打开配置弹窗。
        private bool ValidateConfiguration()
        {
            if (!String.IsNullOrWhiteSpace(apiKey) && !String.IsNullOrWhiteSpace(model)) return true;
            status.Text = "请填写 DeepSeek API Key 和模型名称。";
            OpenConfiguration();
            return false;
        }

        // 异步翻译并支持取消，网络失败时允许用户直接重试。
        private async System.Threading.Tasks.Task TranslateAsync()
        {
            if (requestCancellation != null || !ValidateConfiguration()) return;
            if (String.IsNullOrWhiteSpace(input.Text)) { status.Text = "请输入待翻译字段，每行一个。"; input.Focus(); return; }
            requestCancellation = new CancellationTokenSource();
            translate.Enabled = false;
            cancel.Enabled = true;
            copy.Enabled = false;
            englishResult = null;
            output.Clear();
            bool requestToChinese = toChinese;
            status.Text = "正在请求 DeepSeek…";
            try
            {
                string result = await DeepSeekTranslator.TranslateAsync(apiKey.Trim(), model.Trim(),
                    input.Text, requestToChinese, naming, requestCancellation.Token);
                requestCancellation.Token.ThrowIfCancellationRequested();
                if (IsDisposed) return;
                output.Text = result.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
                if (!requestToChinese) englishResult = output.Text;
                ApplyNamingFormat();
                copy.Enabled = true;
                status.Text = "翻译完成。";
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed) status.Text = requestCancellation.IsCancellationRequested ? "已取消翻译。" : "请求超时，请重试。";
            }
            catch (HttpRequestException exception) { if (!IsDisposed) status.Text = "翻译失败：" + exception.Message; }
            catch (SerializationException) { if (!IsDisposed) status.Text = "DeepSeek 响应格式无法读取，请检查模型配置。"; }
            catch (InvalidOperationException exception) { if (!IsDisposed) status.Text = exception.Message; }
            finally
            {
                requestCancellation.Dispose();
                requestCancellation = null;
                if (!IsDisposed)
                {
                    translate.Enabled = true;
                    cancel.Enabled = false;
                }
            }
        }

        // 只在点击复制时写入剪贴板，不自动粘贴到其他程序。
        private void CopyResult()
        {
            try { Clipboard.SetText(output.Text); status.Text = "结果已复制。"; }
            catch (ExternalException) { status.Text = "剪贴板正被占用，请重试复制。"; }
        }

        // 收起窗口时取消未完成的请求，重新打开仍保留输入和结果。
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                if (requestCancellation != null) requestCancellation.Cancel();
                Hide();
            }
            base.OnFormClosing(e);
        }

        // 主程序退出时取消网络请求，异步回调不会再操作已释放的窗口。
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (requestCancellation != null) requestCancellation.Cancel();
                tips.Dispose();
            }
            base.Dispose(disposing);
            if (disposing)
            {
                symbolFont.Dispose();
                smallFont.Dispose();
            }
        }
    }
}
