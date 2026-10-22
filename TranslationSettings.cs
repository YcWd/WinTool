using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace ClipHistory
{
    [DataContract]
    internal sealed class TranslationSettings
    {
        [DataMember] public string ProtectedApiKey { get; set; }
        [DataMember] public string Model { get; set; }

        // 使用当前 Windows 用户的凭据解密 API Key。
        internal string ReadApiKey()
        {
            if (String.IsNullOrEmpty(ProtectedApiKey)) return "";
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(ProtectedApiKey),
                null, DataProtectionScope.CurrentUser));
        }

        // 独立保存翻译配置，不修改剪贴板设置。
        internal static void Save(string root, string apiKey, string model)
        {
            var settings = new TranslationSettings { Model = model,
                ProtectedApiKey = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(apiKey),
                    null, DataProtectionScope.CurrentUser)) };
            string path = Path.Combine(root, "deepseek-translation.json");
            string temporary = path + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                new DataContractJsonSerializer(typeof(TranslationSettings)).WriteObject(stream, settings);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        // DeepSeek 使用独立配置，避免沿用其他服务的密钥。
        internal static TranslationSettings Load(string root)
        {
            string path = Path.Combine(root, "deepseek-translation.json");
            if (!File.Exists(path)) return new TranslationSettings { Model = "deepseek-flash" };
            using (var stream = File.OpenRead(path))
                return (TranslationSettings)new DataContractJsonSerializer(typeof(TranslationSettings)).ReadObject(stream);
        }
    }
}
