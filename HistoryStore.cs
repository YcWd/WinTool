using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace ClipHistory
{
    [DataContract]
    internal sealed class ClipRecord
    {
        [DataMember] public string Id { get; set; }
        [DataMember] public string CapturedAt { get; set; }
        [DataMember] public string Kind { get; set; }
        [DataMember] public string Text { get; set; }
        [DataMember] public string Html { get; set; }
        [DataMember] public string Rtf { get; set; }
        [DataMember] public string[] Files { get; set; }
        [DataMember] public string ImagePath { get; set; }
        [DataMember] public string Source { get; set; }
        [DataMember] public string Fingerprint { get; set; }
        [DataMember] public string Revision { get; set; }
        [DataMember] public bool IsPinned { get; set; }
        public DateTime LocalTime { get { return DateTime.Parse(CapturedAt, null, System.Globalization.DateTimeStyles.RoundtripKind).ToLocalTime(); } }
        public string Preview
        {
            get
            {
                if (Kind == "图片") return Text;
                if (Kind == "文件") return string.Join(" · ", Files.Select(Path.GetFileName));
                return Text;
            }
        }
    }

    internal sealed class HistoryStore
    {
        private const int RecordLimit = 100;
        internal readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinTool", "ClipboardHistory");
        private readonly DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(ClipRecord));
        private readonly Dictionary<string, string> latestVersions = new Dictionary<string, string>();
        private readonly List<ClipRecord> recentRecords = new List<ClipRecord>();
        internal long Count { get { return latestVersions.Count; } }

        // 准备数据目录，启动时将已有历史清理至最近 100 条。
        internal HistoryStore()
        {
            Directory.CreateDirectory(Path.Combine(Root, "records"));
            Directory.CreateDirectory(Path.Combine(Root, "images"));
            LoadIndex();
            TrimHistory();
            DateTime cutoff = DateTime.Now.AddSeconds(-60);
            recentRecords.Clear();
            recentRecords.AddRange(Read("", "全部类型")
                .Where(r => r.LocalTime >= cutoff).OrderByDescending(r => r.LocalTime));
        }

        // 根据现存日志重建索引，清理中途失败时也能反映剩余记录。
        private void LoadIndex()
        {
            latestVersions.Clear();
            foreach (string path in Directory.EnumerateFiles(Path.Combine(Root, "records"), "*.jsonl").OrderBy(p => p))
            {
                foreach (string line in File.ReadLines(path, Encoding.UTF8).Where(line => line.Length > 0))
                {
                    ClipRecord record = Decode(line);
                    latestVersions[record.Id] = record.Revision ?? record.CapturedAt;
                }
            }
        }

        // 清除未置顶记录，保留置顶记录的最新版本与图片副本。
        internal void Clear()
        {
            RewriteLogs(r => r.IsPinned && (r.Revision ?? r.CapturedAt) == latestVersions[r.Id]);
            DeleteUnusedImages();
        }

        // 按复制时间保留最近 100 条，置顶仅影响展示顺序，不豁免数量限制。
        private void TrimHistory()
        {
            if (Count <= RecordLimit) return;
            var retainedIds = new HashSet<string>(Read("", "全部类型")
                .OrderByDescending(r => r.LocalTime).Take(RecordLimit).Select(r => r.Id));
            RewriteLogs(r => retainedIds.Contains(r.Id) && (r.Revision ?? r.CapturedAt) == latestVersions[r.Id]);
            DeleteUnusedImages();
        }

        // 删除已不再被任何记录引用的图片副本，不触碰文件记录指向的原文件。
        private void DeleteUnusedImages()
        {
            var retainedImages = new HashSet<string>(Read("", "全部类型")
                .Where(r => r.ImagePath != null).Select(r => Path.GetFullPath(Path.Combine(Root, r.ImagePath))),
                StringComparer.OrdinalIgnoreCase);
            foreach (string path in Directory.EnumerateFiles(Path.Combine(Root, "images"), "*.png"))
                if (!retainedImages.Contains(Path.GetFullPath(path))) File.Delete(path);
        }

        // 追加置顶状态的新版本，保留原时间、内容与重复合并状态。
        internal void SetPinned(ClipRecord record, bool pinned)
        {
            ClipRecord updated = Read("", "全部类型").First(r => r.Id == record.Id);
            updated.IsPinned = pinned;
            updated.Revision = Guid.NewGuid().ToString("N");
            WriteRecord(updated);
            latestVersions[updated.Id] = updated.Revision;
            int index = recentRecords.FindIndex(r => r.Id == updated.Id);
            if (index >= 0) recentRecords[index] = updated;
        }

        // 删除单条记录的所有日志版本及其图片副本。
        internal void Delete(ClipRecord record)
        {
            RewriteLogs(r => r.Id != record.Id);
            if (record.ImagePath != null) File.Delete(Path.Combine(Root, record.ImagePath));
        }

        // 每个日志先写临时文件再替换，完成后同步索引与近期合并记录。
        private void RewriteLogs(Func<ClipRecord, bool> retain)
        {
            try
            {
                foreach (string path in Directory.EnumerateFiles(Path.Combine(Root, "records"), "*.jsonl").OrderBy(p => p))
                {
                    string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                    string[] kept = lines.Where(line => line.Length > 0 && retain(Decode(line))).ToArray();
                    if (kept.Length == lines.Length) continue;
                    if (kept.Length == 0) File.Delete(path);
                    else
                    {
                        string temporary = path + ".tmp";
                        try
                        {
                            File.WriteAllLines(temporary, kept, new UTF8Encoding(false));
                            File.Replace(temporary, path, null);
                        }
                        finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    }
                }
            }
            finally
            {
                LoadIndex();
                recentRecords.Clear();
                DateTime cutoff = DateTime.Now.AddSeconds(-60);
                recentRecords.AddRange(Read("", "全部类型")
                    .Where(r => r.LocalTime >= cutoff).OrderByDescending(r => r.LocalTime));
            }
        }

        // 读取现存历史的最新版本，搜索和类型筛选不会扩大保留范围。
        internal List<ClipRecord> Read(string search, string kind)
        {
            var result = new List<ClipRecord>();
            foreach (string path in Directory.EnumerateFiles(Path.Combine(Root, "records"), "*.jsonl").OrderByDescending(p => p))
            {
                foreach (string line in File.ReadLines(path, Encoding.UTF8))
                {
                    if (line.Length == 0) continue;
                    ClipRecord record = Decode(line);
                    if ((record.Revision ?? record.CapturedAt) != latestVersions[record.Id]) continue;
                    if (kind != "全部类型" && record.Kind != kind) continue;
                    if (search.Length > 0 && record.Preview.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 &&
                        record.Source.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 &&
                        (record.Files == null || !record.Files.Any(p => p.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0))) continue;
                    result.Add(record);
                }
            }
            return result.OrderByDescending(r => r.IsPinned).ThenByDescending(r => r.LocalTime).ToList();
        }

        // 间隔内相同内容沿用原 ID，写入后淘汰超出最近 100 条的记录。
        internal bool Append(ClipRecord record, Image image, int duplicateSeconds)
        {
            record.Revision = record.Id;
            record.Fingerprint = Fingerprint(record, image);
            recentRecords.RemoveAll(r => (record.LocalTime - r.LocalTime).TotalSeconds > 60);
            ClipRecord previous = duplicateSeconds == 0 ? null : recentRecords.FirstOrDefault(r =>
                r.Kind == record.Kind &&
                (record.LocalTime - r.LocalTime).TotalSeconds >= 0 &&
                (record.LocalTime - r.LocalTime).TotalSeconds <= duplicateSeconds &&
                RecordFingerprint(r) == record.Fingerprint);
            if (previous != null)
            {
                record.Id = previous.Id;
                record.IsPinned = previous.IsPinned;
            }
            if (image != null)
            {
                record.ImagePath = previous != null ? previous.ImagePath : Path.Combine("images", record.Id + ".png");
                if (previous == null) image.Save(Path.Combine(Root, record.ImagePath), ImageFormat.Png);
            }
            WriteRecord(record);
            latestVersions[record.Id] = record.Revision;
            if (previous != null) recentRecords.Remove(previous);
            recentRecords.Insert(0, record);
            TrimHistory();
            return previous != null;
        }

        // 按记录原始日期追加版本，写入完成后由调用方更新索引。
        private void WriteRecord(ClipRecord record)
        {
            byte[] data;
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, record);
                stream.WriteByte(10);
                data = stream.ToArray();
            }
            string path = Path.Combine(Root, "records", record.LocalTime.ToString("yyyy-MM-dd") + ".jsonl");
            using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough))
                stream.Write(data, 0, data.Length);
        }

        // 反序列化一行历史日志。
        private ClipRecord Decode(string line)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(line)))
                return (ClipRecord)serializer.ReadObject(stream);
        }

        // 旧版记录没有指纹时，仅对近期候选记录计算一次。
        private string RecordFingerprint(ClipRecord record)
        {
            if (record.Fingerprint == null)
            {
                if (record.Kind == "图片")
                    using (var image = LoadImage(record)) record.Fingerprint = Fingerprint(record, image);
                else record.Fingerprint = Fingerprint(record, null);
            }
            return record.Fingerprint;
        }

        // 按文本内容、图片像素编码或文件路径计算稳定指纹。
        private static string Fingerprint(ClipRecord record, Image image)
        {
            using (var hash = SHA256.Create())
            {
                byte[] data;
                if (image != null)
                {
                    using (var stream = new MemoryStream())
                    {
                        image.Save(stream, ImageFormat.Png);
                        data = stream.ToArray();
                    }
                }
                else
                {
                    string content = record.Kind == "文件" ?
                        string.Join("\n", record.Files.OrderBy(p => p, StringComparer.OrdinalIgnoreCase)).ToUpperInvariant() : record.Text;
                    data = Encoding.UTF8.GetBytes(content);
                }
                return record.Kind + ":" + Convert.ToBase64String(hash.ComputeHash(data));
            }
        }

        // 加载独立图片副本，避免预览或粘贴时占用历史文件。
        internal Bitmap LoadImage(ClipRecord record)
        {
            using (Image image = Image.FromFile(Path.Combine(Root, record.ImagePath)))
                return new Bitmap(image);
        }
    }
}
