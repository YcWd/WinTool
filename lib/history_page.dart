import 'dart:async';
import 'dart:io';
import 'package:flutter/gestures.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'main.dart' show WindowHeader;
import 'native_bridge.dart';
import 'settings_dialog.dart';

class HistoryPage extends StatefulWidget {
  const HistoryPage({super.key, required this.bridge});
  final NativeBridge bridge;
  @override
  State<HistoryPage> createState() => _HistoryPageState();
}

class _HistoryPageState extends State<HistoryPage> {
  final search = TextEditingController();
  final searchFocus = FocusNode();
  final listFocus = FocusNode();
  final scroll = ScrollController();
  late final StreamSubscription<Map<String, dynamic>> events;
  String kind = '全部类型';
  String? selectedId;
  double cardExtent = 112;
  NativeBridge get bridge => widget.bridge;
  static const kinds = ['全部类型', '文本', '图片', '文件'];

  @override
  void initState() {
    super.initState();
    bridge.addListener(refresh);
    search.addListener(refresh);
    events = bridge.events.stream.listen((event) {
      if (event['eventName'] == 'show') searchFocus.requestFocus();
      if (event['eventName'] == 'settings' && mounted) openSettings();
    });
  }

  // 数据变化只刷新当前筛选视图。
  void refresh() { if (mounted) setState(() {}); }

  // 显示 Flutter 设置弹窗，保存后立即同步布局。
  Future<void> openSettings() async {
    await showDialog<void>(context: context, builder: (_) => SettingsDialog(bridge: bridge));
    refresh();
  }

  // 同时匹配内容、来源和完整文件路径，保持原有搜索语义。
  List<Map<String, dynamic>> get filtered {
    final query = search.text.toLowerCase();
    return bridge.records.where((record) {
      if (kind != '全部类型' && record['Kind'] != kind) return false;
      final text = '${record['Text']} ${record['Source']} ${(record['Files'] as List?)?.join(' ') ?? ''}';
      return text.toLowerCase().contains(query);
    }).toList();
  }

  // 选中记录后执行以持久 ID 为参数的操作。
  void action(String method, Map<String, dynamic> record) {
    setState(() => selectedId = record['Id'] as String);
    bridge.perform(method, {'id': record['Id']});
  }

  // 完整预览支持文本选择与图片缩放，并展示复制时间和来源。
  void preview(Map<String, dynamic> record) {
    showDialog<void>(context: context, builder: (context) => AlertDialog(
      title: Text('${record['Kind']} · ${record['CapturedAt']}'),
      content: SizedBox(width: 640, height: 420, child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Text(record['Source'] as String), const SizedBox(height: 12),
        Expanded(child: record['Kind'] == '图片'
          ? InteractiveViewer(child: Image.file(File('${bridge.root}\\${record['ImagePath']}')))
          : SingleChildScrollView(child: SelectableText(record['Text'] as String))),
      ])),
      actions: [TextButton(onPressed: () => Navigator.pop(context), child: const Text('关闭'))],
    ));
  }

  // 右键与键盘菜单共用查看、复制和粘贴入口。
  Future<void> showRecordMenu(Map<String, dynamic> record, Offset position) async {
    setState(() => selectedId = record['Id'] as String);
    final value = await showMenu<String>(context: context,
      position: RelativeRect.fromLTRB(position.dx, position.dy, 0, 0),
      items: const [PopupMenuItem(value: 'preview', child: Text('查看完整内容')),
        PopupMenuItem(value: 'copy', child: Text('复制到剪贴板')),
        PopupMenuItem(value: 'paste', child: Text('粘贴到目标窗口'))]);
    if (!mounted || value == null) return;
    if (value == 'preview') preview(record);
    else action(value, record);
  }

  // 键盘操作沿用删除、置顶、确认粘贴和方向键切换。
  KeyEventResult handleKey(FocusNode node, KeyEvent event) {
    if (event is! KeyDownEvent) return KeyEventResult.ignored;
    final rows = filtered;
    if (rows.isEmpty) return KeyEventResult.ignored;
    var index = rows.indexWhere((row) => row['Id'] == selectedId);
    if (event.logicalKey == LogicalKeyboardKey.arrowDown || event.logicalKey == LogicalKeyboardKey.arrowRight) {
      index = (index + 1).clamp(0, rows.length - 1).toInt();
    } else if (event.logicalKey == LogicalKeyboardKey.arrowUp || event.logicalKey == LogicalKeyboardKey.arrowLeft) {
      index = (index - 1).clamp(0, rows.length - 1).toInt();
    } else if (index >= 0) {
      if (event.logicalKey == LogicalKeyboardKey.delete) action('delete', rows[index]);
      else if (event.logicalKey == LogicalKeyboardKey.keyP && HardwareKeyboard.instance.isControlPressed) action('pin', rows[index]);
      else if (event.logicalKey == LogicalKeyboardKey.enter) action('paste', rows[index]);
      else if (event.logicalKey == LogicalKeyboardKey.contextMenu ||
          (event.logicalKey == LogicalKeyboardKey.f10 && HardwareKeyboard.instance.isShiftPressed)) showRecordMenu(rows[index], const Offset(100, 100));
      else return KeyEventResult.ignored;
      return KeyEventResult.handled;
    } else { return KeyEventResult.ignored; }
    setState(() => selectedId = rows[index]['Id'] as String);
    scroll.animateTo((index * cardExtent).clamp(0.0, scroll.position.maxScrollExtent).toDouble(),
      duration: const Duration(milliseconds: 120), curve: Curves.easeOut);
    return KeyEventResult.handled;
  }

  @override
  Widget build(BuildContext context) {
    final dock = bridge.settings['DockBottom'] == true;
    final rows = filtered;
    final tabs = SegmentedButton<String>(
      showSelectedIcon: false,
      segments: kinds.map((value) => ButtonSegment(value: value, label: Text(value == '全部类型' ? '全部' : value))).toList(),
      selected: {kind}, onSelectionChanged: (value) => setState(() => kind = value.single),
    );
    final searchBox = TextField(controller: search, focusNode: searchFocus, decoration: const InputDecoration(
      hintText: '搜索记录', prefixIcon: Icon(Icons.search, size: 20), isDense: true));
    return Scaffold(body: Column(children: [
      WindowHeader(bridge: bridge, title: '剪贴记录', onSettings: openSettings,
        toolbar: dock ? Row(children: [const SizedBox(width: 12), SizedBox(width: 248, child: searchBox), const SizedBox(width: 8),
          SizedBox(width: 256, child: tabs), TextButton(onPressed: () { search.clear(); setState(() => kind = '全部类型'); }, child: const Text('重置'))]) : null),
      if (!dock) Padding(padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 4),
        child: SizedBox(width: double.infinity, child: tabs)),
      Expanded(child: Focus(focusNode: listFocus, onKeyEvent: handleKey,
        child: Listener(onPointerSignal: (event) {
          if (dock && event is PointerScrollEvent && scroll.hasClients) {
            final delta = event.scrollDelta.dx == 0 ? event.scrollDelta.dy : event.scrollDelta.dx;
            scroll.jumpTo((scroll.offset + delta).clamp(0.0, scroll.position.maxScrollExtent).toDouble());
          }
        }, child: rows.isEmpty ? const Center(child: Text('暂无记录', style: TextStyle(color: Color(0xFF52647D))))
          : LayoutBuilder(builder: (context, constraints) {
            cardExtent = dock ? (constraints.maxHeight - 24) * 4 / 3 + 8 : 112;
            return ListView.builder(
            controller: scroll, scrollDirection: dock ? Axis.horizontal : Axis.vertical,
            padding: const EdgeInsets.all(8), itemCount: rows.length,
            itemExtent: cardExtent,
            itemBuilder: (context, index) => recordCard(rows[index], dock),
          ); })))),
      if (!dock) Padding(padding: const EdgeInsets.fromLTRB(12, 4, 8, 10), child: Row(children: [
        Expanded(child: searchBox), const SizedBox(width: 8),
        TextButton(onPressed: () { search.clear(); setState(() => kind = '全部类型'); }, child: const Text('重置')),
      ])),
      if (bridge.status.isNotEmpty) Padding(padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 4),
        child: Text(bridge.status, style: const TextStyle(color: Colors.red, fontSize: 12))),
    ]));
  }

  // 记录卡片共用普通和底部布局，内容点击直接粘贴。
  Widget recordCard(Map<String, dynamic> record, bool dock) {
    final selected = selectedId == record['Id'];
    final image = record['Kind'] == '图片';
    final controls = <Widget>[
      PopupMenuButton<String>(tooltip: '更多', icon: const Icon(Icons.more_horiz, size: 18),
        onSelected: (value) => value == 'preview' ? preview(record) : action(value, record),
        itemBuilder: (_) => const [PopupMenuItem(value: 'preview', child: Text('查看完整内容')),
          PopupMenuItem(value: 'copy', child: Text('复制到剪贴板')), PopupMenuItem(value: 'paste', child: Text('粘贴到目标窗口'))]),
      IconButton(tooltip: record['IsPinned'] == true ? '取消置顶' : '置顶',
        color: record['IsPinned'] == true ? const Color(0xFF2563EB) : null,
        onPressed: () => action('pin', record), icon: const Icon(Icons.push_pin_outlined, size: 16)),
      IconButton(tooltip: '删除', onPressed: () => action('delete', record), icon: const Icon(Icons.delete_outline, size: 18)),
    ];
    final content = image ? Image.file(File('${bridge.root}\\${record['ImagePath']}'), fit: BoxFit.contain,
      cacheWidth: 240, errorBuilder: (_, error, stack) => const Center(child: Text('图片读取失败')))
      : Text(record['Text'] as String, maxLines: dock ? 4 : 3, overflow: TextOverflow.ellipsis,
          style: const TextStyle(fontSize: 13, height: 1.5, color: Color(0xFF18283E)));
    return Card(margin: const EdgeInsets.all(4), elevation: 0,
      color: selected ? const Color(0xFFE8F0FF) : Colors.white,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12), side: BorderSide(
        color: selected ? const Color(0xFF2563EB) : const Color(0xFFCBD8E7))),
      child: InkWell(borderRadius: BorderRadius.circular(12),
        onTap: () { listFocus.requestFocus(); action('paste', record); },
        onSecondaryTapDown: (details) => showRecordMenu(record, details.globalPosition),
        child: Padding(padding: const EdgeInsets.all(8), child: dock
          ? Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
              SizedBox(height: 30, child: Row(mainAxisAlignment: MainAxisAlignment.end, children: controls)),
              Expanded(child: content), if (image) Text(record['Text'] as String, style: const TextStyle(fontSize: 11)),
            ])
          : Row(children: [Expanded(child: Align(alignment: Alignment.centerLeft, child: content)),
              SizedBox(width: 36, child: Column(mainAxisAlignment: MainAxisAlignment.center,
                children: controls.map((control) => SizedBox(height: 28, child: control)).toList())),
            ])),
      ));
  }

  @override
  void dispose() {
    events.cancel(); bridge.removeListener(refresh); search.removeListener(refresh);
    search.dispose(); searchFocus.dispose(); listFocus.dispose(); scroll.dispose(); super.dispose();
  }
}
