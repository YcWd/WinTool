import 'package:flutter/material.dart';
import 'native_bridge.dart';

class SettingsDialog extends StatefulWidget {
  const SettingsDialog({super.key, required this.bridge});
  final NativeBridge bridge;

  @override
  State<SettingsDialog> createState() => _SettingsDialogState();
}

class _SettingsDialogState extends State<SettingsDialog> {
  static const textColor = Color(0xFF242424);
  static const secondaryColor = Color(0xFF787878);
  static const borderColor = Color(0xFFE8E8E8);
  static const accentColor = Color(0xFF3385FF);

  late Map<String, dynamic> settings;
  String error = '';
  bool saving = false;

  @override
  void initState() {
    super.initState();
    settings = Map.of(widget.bridge.settings);
  }

  // 保存成功后采用服务返回的真实状态，错误保留在当前弹窗。
  Future<void> save() async {
    setState(() {
      saving = true;
      error = '';
    });
    try {
      final result = await widget.bridge.call('saveSettings', settings);
      widget.bridge.settings = Map<String, dynamic>.from(result);
      if (mounted) Navigator.pop(context);
    } catch (exception) {
      if (mounted) {
        setState(() {
          error = exception.toString();
          saving = false;
        });
      }
    }
  }

  // 清理前保留确认步骤，清理失败时在设置页内显示错误。
  Future<void> clearHistory() async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('清空未置顶记录？'),
        content: const Text('将删除记录与图片副本，保留置顶记录。'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('取消')),
          TextButton(onPressed: () => Navigator.pop(context, true), child: const Text('清空')),
        ],
      ),
    );
    if (confirmed != true) return;
    try {
      await widget.bridge.call('clear');
    } catch (exception) {
      if (mounted) setState(() => error = exception.toString());
    }
  }

  // 分组共用圆角细边框，仅在相邻设置项之间绘制分隔线。
  Widget buildGroup(String title, List<Widget> rows) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text(title, style: const TextStyle(fontSize: 14, fontWeight: FontWeight.w600, color: textColor)),
        const SizedBox(height: 12),
        DecoratedBox(
          decoration: BoxDecoration(
            color: Colors.white,
            border: Border.all(color: borderColor),
            borderRadius: BorderRadius.circular(16),
          ),
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 16),
            child: Column(children: [
              for (var index = 0; index < rows.length; index++) ...[
                if (index > 0) const Divider(height: 1, color: borderColor),
                rows[index],
              ],
            ]),
          ),
        ),
      ],
    );
  }

  // 普通设置项左右对齐，快捷键在窄窗口内移至说明下方。
  Widget buildRow({
    required String title,
    required String description,
    required Widget control,
    bool wideControl = false,
  }) {
    final label = Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(title, style: const TextStyle(fontSize: 13, fontWeight: FontWeight.w500, color: textColor)),
        const SizedBox(height: 4),
        Text(description, style: const TextStyle(fontSize: 12, height: 1.4, color: secondaryColor)),
      ],
    );
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 14),
      child: LayoutBuilder(builder: (context, constraints) {
        if (wideControl && constraints.maxWidth < 580) {
          return Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [label, const SizedBox(height: 12), control],
          );
        }
        return Row(children: [Expanded(child: label), const SizedBox(width: 20), control]);
      }),
    );
  }

  // 主键和合并间隔共用紧凑的白底下拉控件。
  Widget buildSelector({
    required String label,
    required int value,
    required List<DropdownMenuItem<int>> items,
    required ValueChanged<int?> onChanged,
    double width = 96,
  }) {
    return Semantics(
      label: label,
      child: Container(
        width: width,
        height: 34,
        padding: const EdgeInsets.symmetric(horizontal: 10),
        decoration: BoxDecoration(
          color: Colors.white,
          border: Border.all(color: borderColor),
          borderRadius: BorderRadius.circular(8),
        ),
        child: DropdownButtonHideUnderline(
          child: DropdownButton<int>(
            value: value,
            isExpanded: true,
            isDense: true,
            menuMaxHeight: 280,
            borderRadius: BorderRadius.circular(10),
            dropdownColor: Colors.white,
            icon: const Icon(Icons.keyboard_arrow_down, size: 16, color: secondaryColor),
            style: const TextStyle(fontSize: 13, color: textColor),
            items: items,
            onChanged: saving ? null : onChanged,
          ),
        ),
      ),
    );
  }

  // 使用同一尺寸的小开关，使每行右侧控制区对齐。
  Widget buildSwitch(String key, String label) {
    return Semantics(
      label: label,
      child: SizedBox(
        width: 44,
        height: 32,
        child: FittedBox(
          child: Switch(
            value: settings[key] as bool,
            onChanged: saving ? null : (value) => setState(() => settings[key] = value),
            activeColor: Colors.white,
            activeTrackColor: accentColor,
            inactiveThumbColor: Colors.white,
            inactiveTrackColor: const Color(0xFFD6D6D6),
            trackOutlineColor: MaterialStateProperty.all(Colors.transparent),
          ),
        ),
      ),
    );
  }

  // 组合修饰键与主键，沿用现有修饰键位掩码。
  Widget buildHotkey() {
    return Wrap(
      spacing: 6,
      runSpacing: 6,
      crossAxisAlignment: WrapCrossAlignment.center,
      children: [
        for (final entry in {2: 'Ctrl', 1: 'Alt', 4: 'Shift', 8: 'Win'}.entries)
          FilterChip(
            label: Text(entry.value),
            labelStyle: TextStyle(
              fontSize: 12,
              color: ((settings['HotkeyModifiers'] as int) & entry.key) != 0 ? accentColor : textColor,
            ),
            selected: ((settings['HotkeyModifiers'] as int) & entry.key) != 0,
            showCheckmark: false,
            selectedColor: const Color(0xFFEDF4FF),
            backgroundColor: Colors.white,
            side: const BorderSide(color: borderColor),
            shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
            padding: const EdgeInsets.symmetric(horizontal: 2),
            visualDensity: VisualDensity.compact,
            onSelected: saving ? null : (selected) {
              setState(() {
                final modifiers = settings['HotkeyModifiers'] as int;
                settings['HotkeyModifiers'] = selected ? modifiers | entry.key : modifiers & ~entry.key;
              });
            },
          ),
        buildSelector(
          label: '快捷键主键',
          width: 64,
          value: settings['HotkeyKey'] as int,
          items: [
            for (var key = 65; key <= 90; key++)
              DropdownMenuItem(value: key, child: Text(String.fromCharCode(key))),
            for (var key = 48; key <= 57; key++)
              DropdownMenuItem(value: key, child: Text(String.fromCharCode(key))),
            for (var key = 112; key <= 123; key++)
              DropdownMenuItem(value: key, child: Text('F${key - 111}')),
          ],
          onChanged: (value) => setState(() => settings['HotkeyKey'] = value),
        ),
      ],
    );
  }

  @override
  Widget build(BuildContext context) {
    final buttonShape = RoundedRectangleBorder(borderRadius: BorderRadius.circular(8));
    return Dialog(
      insetPadding: const EdgeInsets.all(16),
      backgroundColor: Colors.white,
      surfaceTintColor: Colors.white,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(20),
        side: const BorderSide(color: borderColor),
      ),
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 720, maxHeight: 760),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(24, 16, 12, 8),
              child: Row(children: [
                const Expanded(child: Text('设置', style: TextStyle(
                  fontSize: 18, fontWeight: FontWeight.w600, color: textColor,
                ))),
                IconButton(
                  tooltip: '关闭设置',
                  onPressed: saving ? null : () => Navigator.pop(context),
                  icon: const Icon(Icons.close, size: 18, color: secondaryColor),
                ),
              ]),
            ),
            Flexible(
              child: SingleChildScrollView(
                padding: const EdgeInsets.fromLTRB(24, 8, 24, 24),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    buildGroup('常规', [
                      buildRow(
                        title: '打开历史的快捷键',
                        description: '选择修饰键和主键组合，快速唤起剪贴板历史。',
                        control: buildHotkey(),
                        wideControl: true,
                      ),
                      buildRow(
                        title: '登录时启动',
                        description: '登录 Windows 后自动启动，并在托盘运行。',
                        control: buildSwitch('Startup', '登录时启动'),
                      ),
                      buildRow(
                        title: '固定到底部',
                        description: '在屏幕底部显示 4:3 卡片，支持横向滚动。',
                        control: buildSwitch('DockBottom', '固定到底部'),
                      ),
                    ]),
                    const SizedBox(height: 28),
                    buildGroup('剪贴板记录', [
                      buildRow(
                        title: '重复内容合并间隔',
                        description: '间隔内相同内容保留最新时间和来源。设为 0 秒关闭合并。',
                        control: buildSelector(
                          label: '重复内容合并间隔',
                          value: settings['DuplicateSeconds'] as int,
                          items: [
                            for (var seconds = 0; seconds <= 60; seconds++)
                              DropdownMenuItem(value: seconds, child: Text('$seconds 秒')),
                          ],
                          onChanged: (value) => setState(() => settings['DuplicateSeconds'] = value),
                        ),
                      ),
                      buildRow(
                        title: '记录总数',
                        description: '超出上限删除较早记录，置顶记录也计入总数。',
                        control: SizedBox(
                          width: 156,
                          child: SegmentedButton<int>(
                            showSelectedIcon: false,
                            style: ButtonStyle(
                              minimumSize: MaterialStateProperty.all(const Size(0, 32)),
                              tapTargetSize: MaterialTapTargetSize.shrinkWrap,
                              visualDensity: VisualDensity.compact,
                              padding: MaterialStateProperty.all(const EdgeInsets.symmetric(horizontal: 6)),
                              textStyle: MaterialStateProperty.all(const TextStyle(fontSize: 12)),
                              side: MaterialStateProperty.all(const BorderSide(color: borderColor)),
                              backgroundColor: MaterialStateProperty.resolveWith((states) {
                                return states.contains(MaterialState.selected)
                                    ? const Color(0xFFEDF4FF) : Colors.white;
                              }),
                              foregroundColor: MaterialStateProperty.resolveWith((states) {
                                return states.contains(MaterialState.selected) ? accentColor : textColor;
                              }),
                            ),
                            segments: [
                              for (final limit in [20, 50, 100])
                                ButtonSegment(value: limit, label: Text('$limit 条')),
                            ],
                            selected: {settings['RecordLimit'] as int},
                            onSelectionChanged: saving ? null : (value) {
                              setState(() => settings['RecordLimit'] = value.single);
                            },
                          ),
                        ),
                      ),
                      buildRow(
                        title: '清除缓存',
                        description: '清除未置顶记录和图片缓存，保留置顶记录与设置。',
                        control: OutlinedButton(
                          onPressed: saving ? null : clearHistory,
                          style: OutlinedButton.styleFrom(
                            foregroundColor: textColor,
                            side: const BorderSide(color: borderColor),
                            shape: buttonShape,
                            textStyle: const TextStyle(fontSize: 13),
                          ),
                          child: const Text('清除缓存'),
                        ),
                      ),
                    ]),
                    if (error.isNotEmpty)
                      Padding(
                        padding: const EdgeInsets.only(top: 16),
                        child: Semantics(
                          liveRegion: true,
                          child: Text(error, style: const TextStyle(color: Colors.red, fontSize: 12)),
                        ),
                      ),
                  ],
                ),
              ),
            ),
            const Divider(height: 1, color: borderColor),
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 16),
              child: Row(
                mainAxisAlignment: MainAxisAlignment.end,
                children: [
                  OutlinedButton(
                    onPressed: saving ? null : () => Navigator.pop(context),
                    style: OutlinedButton.styleFrom(
                      foregroundColor: textColor,
                      side: const BorderSide(color: borderColor),
                      shape: buttonShape,
                    ),
                    child: const Text('取消'),
                  ),
                  const SizedBox(width: 10),
                  FilledButton(
                    onPressed: saving ? null : save,
                    style: FilledButton.styleFrom(
                      backgroundColor: accentColor,
                      foregroundColor: Colors.white,
                      shape: buttonShape,
                    ),
                    child: Text(saving ? '正在保存…' : '保存设置'),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}
