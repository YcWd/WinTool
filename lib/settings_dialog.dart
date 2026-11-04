import 'package:flutter/material.dart';
import 'native_bridge.dart';

class SettingsDialog extends StatefulWidget {
  const SettingsDialog({super.key, required this.bridge});
  final NativeBridge bridge;
  @override
  State<SettingsDialog> createState() => _SettingsDialogState();
}

class _SettingsDialogState extends State<SettingsDialog> {
  late Map<String, dynamic> settings;
  String error = '';
  bool saving = false;
  @override
  void initState() { super.initState(); settings = Map.of(widget.bridge.settings); }

  // 保存成功后采用服务返回的真实状态，错误保留在当前弹窗。
  Future<void> save() async {
    setState(() { saving = true; error = ''; });
    try {
      final result = await widget.bridge.call('saveSettings', settings);
      widget.bridge.settings = Map<String, dynamic>.from(result);
      if (mounted) Navigator.pop(context);
    } catch (exception) {
      if (mounted) setState(() { error = exception.toString(); saving = false; });
    }
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('设置'),
    content: SizedBox(width: 440, child: SingleChildScrollView(child: Column(
      mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
      const Text('打开历史的快捷键'),
      Wrap(spacing: 4, children: {2: 'Ctrl', 1: 'Alt', 4: 'Shift', 8: 'Win'}.entries.map((entry) => FilterChip(
        label: Text(entry.value), selected: ((settings['HotkeyModifiers'] as int) & entry.key) != 0,
        onSelected: saving ? null : (selected) => setState(() {
          final modifiers = settings['HotkeyModifiers'] as int;
          settings['HotkeyModifiers'] = selected ? modifiers | entry.key : modifiers & ~entry.key;
        }),
      )).toList()),
      DropdownButtonFormField<int>(value: settings['HotkeyKey'] as int,
        items: [for (var key = 65; key <= 90; key++) DropdownMenuItem(value: key, child: Text(String.fromCharCode(key))),
          for (var key = 48; key <= 57; key++) DropdownMenuItem(value: key, child: Text(String.fromCharCode(key))),
          for (var key = 112; key <= 123; key++) DropdownMenuItem(value: key, child: Text('F${key - 111}'))],
        onChanged: saving ? null : (value) => setState(() => settings['HotkeyKey'] = value)),
      const SizedBox(height: 16),
      Text('重复合并间隔 ${settings['DuplicateSeconds']} 秒（0 表示关闭）'),
      Slider(min: 0, max: 60, divisions: 60, value: (settings['DuplicateSeconds'] as int).toDouble(),
        onChanged: saving ? null : (value) => setState(() => settings['DuplicateSeconds'] = value.round())),
      const Text('记录总数'), const SizedBox(height: 8),
      SegmentedButton<int>(segments: [20, 50, 100].map((value) => ButtonSegment(value: value, label: Text('$value 条'))).toList(),
        selected: {settings['RecordLimit'] as int}, onSelectionChanged: saving ? null : (value) => setState(() => settings['RecordLimit'] = value.single)),
      const Padding(padding: EdgeInsets.symmetric(vertical: 8), child: Text('超限会删除较早记录及其图片副本，置顶记录也计入总数。', style: TextStyle(fontSize: 12))),
      SwitchListTile(contentPadding: EdgeInsets.zero, title: const Text('固定到底部'), value: settings['DockBottom'] as bool,
        onChanged: saving ? null : (value) => setState(() => settings['DockBottom'] = value)),
      SwitchListTile(contentPadding: EdgeInsets.zero, title: const Text('登录 Windows 后在托盘启动'), value: settings['Startup'] as bool,
        onChanged: saving ? null : (value) => setState(() => settings['Startup'] = value)),
      TextButton(onPressed: saving ? null : () async {
        final confirmed = await showDialog<bool>(context: context, builder: (context) => AlertDialog(
          title: const Text('清空未置顶记录？'), content: const Text('将删除记录与图片副本，保留置顶记录。'),
          actions: [TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('取消')),
            TextButton(onPressed: () => Navigator.pop(context, true), child: const Text('清空'))]));
        if (confirmed == true) {
          try { await widget.bridge.call('clear'); }
          catch (exception) { if (mounted) setState(() => error = exception.toString()); }
        }
      }, child: const Text('清空未置顶记录')),
      if (error.isNotEmpty) Text(error, style: const TextStyle(color: Colors.red)),
    ]))),
    actions: [TextButton(onPressed: saving ? null : () => Navigator.pop(context), child: const Text('取消')),
      FilledButton(onPressed: saving ? null : save, child: const Text('保存'))],
  );
}
