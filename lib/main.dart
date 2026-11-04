import 'dart:io';
import 'package:flutter/material.dart';
import 'history_page.dart';
import 'native_bridge.dart';
import 'translation_page.dart';

// 启动 Flutter 界面，后台服务继续使用原有 Windows 存储与系统能力。
Future<void> main(List<String> arguments) async {
  WidgetsFlutterBinding.ensureInitialized();
  final bridge = NativeBridge();
  final translation = arguments.contains('--translation');
  try {
    final initial = await bridge.start(translation);
    if (initial['duplicate'] == true) exit(0);
    runApp(WinToolApp(bridge: bridge, translation: translation, initial: initial));
  } catch (error) {
    runApp(MaterialApp(home: Scaffold(body: Center(child: Padding(
      padding: const EdgeInsets.all(24), child: SelectableText('启动失败\n$error'),
    )))));
  }
}

class WinToolApp extends StatelessWidget {
  const WinToolApp({super.key, required this.bridge, required this.translation, required this.initial});
  final NativeBridge bridge;
  final bool translation;
  final Map<String, dynamic> initial;

  @override
  Widget build(BuildContext context) => MaterialApp(
    title: translation ? '开发字段翻译' : '剪贴记录',
    debugShowCheckedModeBanner: false,
    theme: ThemeData(
      useMaterial3: true,
      fontFamily: 'Microsoft YaHei UI',
      colorScheme: ColorScheme.fromSeed(seedColor: const Color(0xFF2563EB), surface: Colors.white),
      scaffoldBackgroundColor: const Color(0xFFEAF0F7),
      visualDensity: VisualDensity.compact,
      inputDecorationTheme: InputDecorationTheme(
        filled: true, fillColor: Colors.white,
        border: OutlineInputBorder(borderRadius: BorderRadius.circular(10)),
        contentPadding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
      ),
    ),
    home: translation ? TranslationPage(bridge: bridge, initial: initial) : HistoryPage(bridge: bridge),
  );
}

class WindowHeader extends StatefulWidget {
  const WindowHeader({super.key, required this.bridge, required this.title, required this.onSettings, this.toolbar});
  final NativeBridge bridge;
  final String title;
  final VoidCallback onSettings;
  final Widget? toolbar;
  @override
  State<WindowHeader> createState() => _WindowHeaderState();
}

class _WindowHeaderState extends State<WindowHeader> {
  bool pinned = false;
  @override
  Widget build(BuildContext context) {
    final title = GestureDetector(
      behavior: HitTestBehavior.opaque,
      onPanStart: (_) => widget.bridge.perform('drag'),
      child: Padding(
        padding: const EdgeInsets.only(left: 14),
        child: Text(widget.title),
      ),
    );
    return SizedBox(
      height: 40,
      child: Row(children: [
        if (widget.toolbar == null) Expanded(child: title)
        else SizedBox(width: 90, child: title),
        if (widget.toolbar != null) Expanded(child: widget.toolbar!),
        IconButton(
          tooltip: pinned ? '取消窗口置顶' : '窗口置顶',
          color: pinned ? Theme.of(context).colorScheme.primary : null,
          icon: const Icon(Icons.push_pin_outlined, size: 18),
          onPressed: () async {
            final result = await widget.bridge.perform('topmost');
            if (mounted && result is bool) setState(() => pinned = result);
          },
        ),
        IconButton(
          tooltip: '设置',
          icon: const Icon(Icons.settings_outlined, size: 18),
          onPressed: widget.onSettings,
        ),
        IconButton(
          tooltip: '最小化',
          icon: const Icon(Icons.remove, size: 18),
          onPressed: () => widget.bridge.perform('minimize'),
        ),
        IconButton(
          tooltip: '收起',
          icon: const Icon(Icons.close, size: 18),
          onPressed: () => widget.bridge.perform('hide'),
        ),
      ]),
    );
  }
}
