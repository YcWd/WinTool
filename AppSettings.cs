using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ClipHistory
{
    [DataContract]
    internal sealed class AppSettings
    {
        [DataMember] public uint HotkeyModifiers { get; set; }
        [DataMember] public uint HotkeyKey { get; set; }
        [DataMember] public int DuplicateSeconds { get; set; }
        [DataMember] public bool DockBottom { get; set; }

        internal string HotkeyText
        {
            get
            {
                return ((HotkeyModifiers & 2) != 0 ? "Ctrl+" : "") +
                    ((HotkeyModifiers & 1) != 0 ? "Alt+" : "") +
                    ((HotkeyModifiers & 4) != 0 ? "Shift+" : "") +
                    ((HotkeyModifiers & 8) != 0 ? "Win+" : "") + (Keys)HotkeyKey;
            }
        }

        // 首次启动使用默认设置，之后读取用户保存的配置。
        internal static AppSettings Load(string root)
        {
            string path = Path.Combine(root, "settings.json");
            if (!File.Exists(path)) return new AppSettings { HotkeyModifiers = 3, HotkeyKey = 0x56, DuplicateSeconds = 3 };
            using (var stream = File.OpenRead(path))
                return (AppSettings)new DataContractJsonSerializer(typeof(AppSettings)).ReadObject(stream);
        }

        // 原子替换设置文件，保存成功后才应用到界面。
        internal void Save(string root)
        {
            string path = Path.Combine(root, "settings.json");
            string temporary = path + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                new DataContractJsonSerializer(typeof(AppSettings)).WriteObject(stream, this);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        // 读取当前启动项，旧名称在新版启动时迁移。
        internal static bool StartupEnabled
        {
            get
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                    return key != null && (key.GetValue("winTool") != null || key.GetValue("WinToolClipboardHistory") != null);
            }
        }

        // 当前用户登录后在托盘启动，注册当前 EXE 路径并移除旧名称。
        internal static void SetStartup(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                if (enabled) key.SetValue("winTool", "\"" + Application.ExecutablePath + "\" --tray", RegistryValueKind.String);
                else key.DeleteValue("winTool", false);
                key.DeleteValue("WinToolClipboardHistory", false);
            }
        }
    }
}
