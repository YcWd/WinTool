using System;
using System.Collections.Specialized;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace ClipHistory
{
    internal sealed partial class BridgeHost
    {
        // 捕获常用剪贴板格式；剪贴板被其他进程占用时短暂重试。
        private void CaptureClipboard()
        {
            clipboardRetry.Stop();
            if (writingClipboard) return;
            uint sequence = NativeMethods.GetClipboardSequenceNumber();
            if (sequence == 0 || sequence == capturedSequence || sequence == ownSequence) return;
            try
            {
                IDataObject data = Clipboard.GetDataObject();
                if (data == null) return;
                var source = new StringBuilder(256);
                NativeMethods.GetWindowText(NativeMethods.GetForegroundWindow(), source, source.Capacity);
                var record = new ClipRecord { Id = Guid.NewGuid().ToString("N"), CapturedAt = DateTime.Now.ToString("o"), Source = source.ToString() };
                Bitmap image = null;
                try
                {
                    if (data.GetDataPresent(DataFormats.FileDrop))
                    {
                        record.Kind = "文件";
                        record.Files = (string[])data.GetData(DataFormats.FileDrop);
                        record.Text = string.Join(Environment.NewLine, record.Files);
                    }
                    else if (data.GetDataPresent(DataFormats.UnicodeText))
                    {
                        record.Kind = "文本";
                        record.Text = (string)data.GetData(DataFormats.UnicodeText);
                        if (data.GetDataPresent(DataFormats.Html)) record.Html = (string)data.GetData(DataFormats.Html);
                        if (data.GetDataPresent(DataFormats.Rtf)) record.Rtf = (string)data.GetData(DataFormats.Rtf);
                    }
                    else if (data.GetDataPresent(DataFormats.Bitmap))
                    {
                        record.Kind = "图片";
                        image = new Bitmap((Image)data.GetData(DataFormats.Bitmap));
                        record.Text = "图片  " + image.Width + " × " + image.Height;
                    }
                    else
                    {
                        capturedSequence = sequence;
                        SetStatus("本次复制的专有格式暂不支持；可记录文本、图片和文件列表。");
                        return;
                    }
                    if (NativeMethods.GetClipboardSequenceNumber() != sequence)
                    {
                        clipboardRetry.Start();
                        return;
                    }
                    store.Append(record, image, settings.DuplicateSeconds);
                    capturedSequence = sequence;
                    PublishRecords();
                }
                finally { if (image != null) image.Dispose(); }
            }
            catch (ExternalException)
            {
                if (++retries <= 8) clipboardRetry.Start();
                else SetStatus("剪贴板持续被占用，本次记录未能保存。");
            }
            catch (IOException exception) { SetStatus("记录保存失败：" + exception.Message); }
            catch (UnauthorizedAccessException exception) { SetStatus("记录保存失败：" + exception.Message); }
        }

        // 恢复记录格式，并忽略本程序引发的剪贴板通知。
        private bool WriteClipboard(ClipRecord record)
        {
            writingClipboard = true;
            try
            {
                var data = new DataObject();
                if (record.Kind == "文件")
                {
                    foreach (string path in record.Files)
                    {
                        if (!File.Exists(path) && !Directory.Exists(path))
                        {
                            SetStatus("文件已不存在：" + path);
                            return false;
                        }
                    }
                    var files = new StringCollection();
                    files.AddRange(record.Files);
                    data.SetFileDropList(files);
                    Clipboard.SetDataObject(data, true, 8, 60);
                }
                else if (record.Kind == "图片")
                {
                    using (var image = store.LoadImage(record))
                    {
                        data.SetImage(image);
                        Clipboard.SetDataObject(data, true, 8, 60);
                    }
                }
                else
                {
                    data.SetData(DataFormats.UnicodeText, record.Text);
                    if (record.Html != null) data.SetData(DataFormats.Html, record.Html);
                    if (record.Rtf != null) data.SetData(DataFormats.Rtf, record.Rtf);
                    Clipboard.SetDataObject(data, true, 8, 60);
                }
                ownSequence = NativeMethods.GetClipboardSequenceNumber();
                return true;
            }
            catch (ExternalException) { SetStatus("剪贴板正被占用，请再次点击记录。"); return false; }
            catch (IOException exception) { SetStatus("记录读取失败：" + exception.Message); return false; }
            finally { writingClipboard = false; }
        }

    }
}
