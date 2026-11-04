# 剪贴记录

基于 Flutter 的 Windows 桌面剪贴板历史工具。历史列表、设置、预览和开发字段翻译窗口使用 Dart / Flutter，Windows 系统能力由独立 C# 服务提供。

## 开发环境

- Flutter stable（Dart 3.4 或更新版本），将 `flutter/bin` 加入 PATH。
- Visual Studio 2022 的“使用 C++ 的桌面开发”工作负载和 Windows SDK。
- Windows 系统自带的 .NET Framework 4.x。

安装说明见 [Flutter Windows 开发环境文档](https://docs.flutter.dev/platform-integration/windows/setup)。

```powershell
flutter pub get
flutter run -d windows
```

Windows runner 的 CMake 会同时生成 `WinTool.Native.exe`，开发运行与正式打包使用相同的桥接服务。

## 打包

双击 `打包.bat`，或显式执行以下命令。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

输出目录为 `dist\flutter`，主程序为 `winTool.exe`。发布时复制整个目录，其中包含 Flutter DLL、资源和 `WinTool.Native.exe`。

## 功能

- 记录文本、HTML、RTF、图片和文件列表，点击记录恢复原始格式并粘贴到之前的目标窗口。
- 默认 Ctrl+Alt+V 打开历史，可修改快捷键，冲突时保留原快捷键。
- 文本、图片、文件筛选，支持搜索内容、来源窗口和文件路径。
- 置顶、删除、完整内容预览、仅复制和粘贴。方向键选择，Enter 粘贴，Ctrl+P 切换置顶，Delete 删除，Shift+F10 或菜单键打开记录菜单。
- 0–60 秒重复合并，默认 3 秒。记录上限可选 20、50、100 条，默认 100 条，按复制时间淘汰，置顶记录也计入上限。
- 清空缓存保留置顶记录。删除和淘汰同步清理图片副本，文件记录对应的原始文件保持独立。
- 窗口置顶、失焦收起、底部横向卡片模式、托盘驻留、单实例、开机在托盘启动。
- Alt+2 或托盘打开独立开发字段翻译窗口，收起后保留输入和结果。
- DeepSeek 中英字段翻译，支持取消请求，提供小驼峰、大驼峰、下划线、大写下划线和短横线格式。命名切换在本地处理，基于原始英文结果重新分词。

在目标应用点击输入框，再使用快捷键选择记录。目标应用以管理员权限运行时，可手动 Ctrl+V，或以相同权限运行工具。

## 数据与配置

继续使用 `%LOCALAPPDATA%\WinTool\ClipboardHistory`，沿用已有文件格式。

- `records\yyyy-MM-dd.jsonl` 保存完整记录及修订版本。
- `images\*.png` 保存独立图片副本。
- `settings.json` 保存快捷键、重复合并、底部布局和记录总数。
- `deepseek-translation.json` 保存模型与当前 Windows 用户 DPAPI 加密的 API Key。
- `logs\yyyy-MM-dd.log` 保存桥接服务生命周期及请求错误。

旧版配置缺少记录上限字段时继续按 100 条处理。启动项启用时会更新为当前 Flutter 主程序路径。迁移前先从旧版托盘退出，避免两个版本争用单实例与全局快捷键。

## 项目结构

- `lib/` 为 Flutter 界面与进程通信客户端。
- `windows/runner/` 为 Flutter 官方 Windows runner 模板及窗口行为定制。
- `windows/native/` 为隐藏消息窗口、剪贴板桥接、托盘和 JSON 行协议。
- 根目录的 `HistoryStore.cs`、`AppSettings.cs`、`TranslationSettings.cs`、`DeepSeekTranslator.cs` 和 `NativeMethods.cs` 由桥接服务复用，原生托盘继续复用 `TrayMenu.cs` 与 `FluentStyle.cs`。
- 原有 WinForms 界面源码暂留作迁移参考，当前打包入口仅编译原生服务和 Flutter 界面。

当前开发环境未安装 Flutter SDK，本次迁移未执行构建和桌面运行验收。仍需在具备开发环境的 Windows 上验证快捷键、托盘、跨窗口粘贴、图片、底部布局和翻译请求。
