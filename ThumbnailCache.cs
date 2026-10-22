using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

namespace ClipHistory
{
    internal sealed class ThumbnailCache : IDisposable
    {
        private readonly HistoryStore store;
        private readonly Dictionary<string, Bitmap> images = new Dictionary<string, Bitmap>();
        private readonly Queue<string> order = new Queue<string>();

        internal ThumbnailCache(HistoryStore historyStore) { store = historyStore; }

        // 仅为可见记录生成等比例小图，最多缓存 48 张并释放淘汰项。
        internal Bitmap Get(ClipRecord record)
        {
            Bitmap thumbnail;
            if (images.TryGetValue(record.ImagePath, out thumbnail)) return thumbnail;
            if (images.Count == 48)
            {
                string oldest = order.Dequeue();
                if (images[oldest] != null) images[oldest].Dispose();
                images.Remove(oldest);
            }
            Bitmap source;
            try { source = store.LoadImage(record); }
            catch (IOException) { source = null; }
            catch (UnauthorizedAccessException) { source = null; }
            // GDI+ 也以此异常报告无效图片格式，失败项不反复解码。
            catch (OutOfMemoryException) { source = null; }
            thumbnail = null;
            if (source != null)
            {
                using (source)
                {
                    double scale = Math.Min(1, Math.Min(240.0 / source.Width, 144.0 / source.Height));
                    thumbnail = new Bitmap(Math.Max(1, (int)(source.Width * scale)), Math.Max(1, (int)(source.Height * scale)));
                    using (var graphics = Graphics.FromImage(thumbnail))
                    {
                        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        graphics.DrawImage(source, new Rectangle(0, 0, thumbnail.Width, thumbnail.Height));
                    }
                }
            }
            images.Add(record.ImagePath, thumbnail);
            order.Enqueue(record.ImagePath);
            return thumbnail;
        }

        // 清理本地历史和退出程序时一并释放缩略图内存。
        internal void Clear()
        {
            foreach (Bitmap image in images.Values) if (image != null) image.Dispose();
            images.Clear();
            order.Clear();
        }

        public void Dispose() { Clear(); }
    }
}
