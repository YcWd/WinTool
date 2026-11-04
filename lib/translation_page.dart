import 'package:flutter/material.dart';
import 'main.dart' show WindowHeader;
import 'native_bridge.dart';

class TranslationPage extends StatefulWidget {
  const TranslationPage({super.key, required this.bridge, required this.initial});
  final NativeBridge bridge;
  final Map<String, dynamic> initial;
  @override
  State<TranslationPage> createState() => _TranslationPageState();
}

class _TranslationPageState extends State<TranslationPage> with WidgetsBindingObserver {
  final input = TextEditingController();
  final output = TextEditingController();
  final inputFocus = FocusNode();
  late Map<String, dynamic> configuration;
  String naming = 'camelCase';
  String? englishResult;
  bool toChinese = false;
  bool busy = false;
  String status = '';
  static const styles = {'camelCase': '小驼峰', 'PascalCase': '大驼峰',
    'snake_case': '下划线', 'UPPER_SNAKE_CASE': '大写下划线', 'kebab-case': '短横线'};

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    configuration = Map.of(widget.initial);
    WidgetsBinding.instance.addPostFrameCallback((_) {
      inputFocus.requestFocus();
      if ((configuration['apiKey'] as String).isEmpty) openConfiguration();
    });
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) inputFocus.requestFocus();
    if (state == AppLifecycleState.hidden && busy) widget.bridge.perform('cancelTranslation');
  }

  // 从原始英文结果重新分词，切换命名格式不会触发 API 请求。
  void applyNaming() {
    if (englishResult == null) return;
    output.text = englishResult!.replaceAllMapped(RegExp(r'[^\r\n]+'), (line) => formatIdentifier(line[0]!));
  }

  // 拆分驼峰、缩写和分隔符，按选中的格式组合字段。
  String formatIdentifier(String value) {
    var separated = value.replaceAllMapped(RegExp(r'([A-Z]+)([A-Z][a-z])'), (match) => '${match[1]} ${match[2]}');
    separated = separated.replaceAllMapped(RegExp(r'([a-z0-9])([A-Z])'), (match) => '${match[1]} ${match[2]}');
    final words = separated.split(RegExp(r'[^\p{L}\p{N}]+', unicode: true))
        .where((word) => word.isNotEmpty).map((word) => word.toLowerCase()).toList();
    if (words.isEmpty) return value;
    if (naming == 'snake_case') return words.join('_');
    if (naming == 'UPPER_SNAKE_CASE') return words.join('_').toUpperCase();
    if (naming == 'kebab-case') return words.join('-');
    final capitalized = words.map((word) => word[0].toUpperCase() + word.substring(1));
    if (naming == 'PascalCase') return capitalized.join();
    return words.first + capitalized.skip(1).join();
  }

  // 请求期间固定翻译方向，返回后应用当前最新命名选择。
  Future<void> translate() async {
    if (input.text.trim().isEmpty) { setState(() => status = '请输入要翻译的字段。'); return; }
    if ((configuration['apiKey'] as String).isEmpty) { await openConfiguration(); return; }
    setState(() { busy = true; status = '正在翻译…'; });
    try {
      final result = await widget.bridge.call('translate', {
        'input': input.text, 'toChinese': toChinese, 'naming': naming,
      }) as String;
      if (!mounted) return;
      if (toChinese) { englishResult = null; output.text = result; }
      else { englishResult = result; applyNaming(); }
      setState(() => status = '翻译完成。');
    } catch (error) {
      if (mounted) setState(() => status = error.toString());
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  // 配置在弹窗内编辑，保存成功后才更新当前配置。
  Future<void> openConfiguration() async {
    final apiKey = TextEditingController(text: configuration['apiKey'] as String);
    final model = TextEditingController(text: configuration['model'] as String);
    String error = '';
    bool saving = false;
    final route = DialogRoute<void>(context: context, barrierDismissible: false, builder: (context) => StatefulBuilder(
      builder: (context, update) => AlertDialog(
        title: const Text('DeepSeek 配置'),
        content: SizedBox(width: 380, child: Column(mainAxisSize: MainAxisSize.min, children: [
          TextField(controller: apiKey, obscureText: true, decoration: const InputDecoration(labelText: 'API Key')),
          const SizedBox(height: 16),
          TextField(controller: model, decoration: const InputDecoration(labelText: '模型名称')),
          if (error.isNotEmpty) Padding(padding: const EdgeInsets.only(top: 12), child: Text(error, style: const TextStyle(color: Colors.red))),
        ])),
        actions: [TextButton(onPressed: saving ? null : () => Navigator.pop(context), child: const Text('取消')),
          FilledButton(onPressed: saving ? null : () async {
            if (apiKey.text.trim().isEmpty || model.text.trim().isEmpty) { update(() => error = '请填写 API Key 和模型名称。'); return; }
            update(() => saving = true);
            final next = {'apiKey': apiKey.text.trim(), 'model': model.text.trim()};
            try {
              await widget.bridge.call('saveTranslationConfig', next);
              configuration = next;
              if (context.mounted) Navigator.pop(context);
            } catch (exception) {
              if (context.mounted) update(() { saving = false; error = exception.toString(); });
            }
          }, child: const Text('保存配置'))],
      ),
    ));
    await Navigator.of(context).push(route);
    // 弹窗实际退场后再释放其文本控制器。
    await route.completed;
    apiKey.dispose(); model.dispose();
    if (mounted) inputFocus.requestFocus();
  }

  @override
  Widget build(BuildContext context) => Scaffold(body: Column(children: [
    WindowHeader(bridge: widget.bridge, title: '开发字段翻译', onSettings: busy ? () {} : openConfiguration),
    Expanded(child: Padding(padding: const EdgeInsets.fromLTRB(12, 4, 12, 12), child: Column(children: [
      Row(children: [Expanded(child: SegmentedButton<bool>(showSelectedIcon: false,
        segments: const [ButtonSegment(value: false, label: Text('中译英')), ButtonSegment(value: true, label: Text('英译中'))],
        selected: {toChinese}, onSelectionChanged: busy ? null : (value) => setState(() => toChinese = value.single))),
        TextButton(onPressed: busy ? null : openConfiguration, child: const Text('配置'))]),
      const SizedBox(height: 12),
      Expanded(flex: 45, child: TextField(controller: input, focusNode: inputFocus, expands: true, maxLines: null,
        textAlignVertical: TextAlignVertical.top,
        decoration: const InputDecoration(hintText: '每行输入一个字段或中文含义'))),
      Padding(padding: const EdgeInsets.symmetric(vertical: 8), child: Row(children: [
        FilledButton(onPressed: busy ? null : translate, child: const Text('翻译')),
        const SizedBox(width: 8),
        TextButton(onPressed: busy ? () => widget.bridge.perform('cancelTranslation') : null, child: const Text('取消请求')),
        const SizedBox(width: 8), Expanded(child: Text(status, maxLines: 2, overflow: TextOverflow.ellipsis,
          style: const TextStyle(fontSize: 12, color: Color(0xFF52647D)))),
      ])),
      Expanded(flex: 55, child: TextField(controller: output, readOnly: true, expands: true, maxLines: null,
        textAlignVertical: TextAlignVertical.top, decoration: const InputDecoration(hintText: '翻译结果'))),
      const SizedBox(height: 10),
      Row(children: [Expanded(child: SingleChildScrollView(scrollDirection: Axis.horizontal, child: SegmentedButton<String>(
        showSelectedIcon: false, style: const ButtonStyle(visualDensity: VisualDensity.compact),
        segments: styles.entries.map((entry) => ButtonSegment(value: entry.key, label: Text(entry.value, style: const TextStyle(fontSize: 11)))).toList(),
        selected: {naming}, onSelectionChanged: toChinese ? null : (value) => setState(() { naming = value.single; applyNaming(); })))),
        const SizedBox(width: 8), TextButton(onPressed: () async {
          if (output.text.isEmpty) return;
          try {
            await widget.bridge.call('copyText', {'text': output.text});
            if (mounted) setState(() => status = '结果已复制。');
          } catch (error) { if (mounted) setState(() => status = error.toString()); }
        }, child: const Text('复制结果'))]),
    ]))),
  ]));

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    input.dispose(); output.dispose(); inputFocus.dispose(); super.dispose();
  }
}
