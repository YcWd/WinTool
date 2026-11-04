import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'package:flutter/foundation.dart';

class NativeBridge extends ChangeNotifier {
  Process? _process;
  final _pending = <int, Completer<dynamic>>{};
  final events = StreamController<Map<String, dynamic>>.broadcast();
  int _sequence = 0;
  List<Map<String, dynamic>> records = [];
  Map<String, dynamic> settings = {};
  String root = '';
  String status = '';

  // 启动 Windows 服务并建立逐行 JSON 通信。
  Future<Map<String, dynamic>> start(bool translation) async {
    final executable = File(Platform.resolvedExecutable).parent.path;
    _process = await Process.start('$executable${Platform.pathSeparator}WinTool.Native.exe',
        [if (translation) '--translation']);
    _process!.stdout.transform(utf8.decoder).transform(const LineSplitter()).listen(_receive,
        onError: (Object error) => _disconnect(error.toString()));
    _process!.stderr.transform(utf8.decoder).listen((message) {
      status = message;
      notifyListeners();
    });
    unawaited(_process!.exitCode.then((code) => _disconnect('Windows 服务已退出（$code）。')));
    final result = Map<String, dynamic>.from(await call('initialize', {'pid': pid}));
    if (!translation && result['duplicate'] != true) {
      root = result['root'] as String;
      settings = Map<String, dynamic>.from(result['settings']);
      records = _recordList(result['records']);
      status = result['warning'] as String? ?? '';
    }
    return result;
  }

  // 分发请求响应和系统推送，保持界面与存储同步。
  void _receive(String line) {
    final message = jsonDecode(line) as Map<String, dynamic>;
    if (message.containsKey('id')) {
      final completer = _pending.remove(message['id']);
      if (message.containsKey('error')) {
        completer?.completeError(StateError(message['error'] as String));
      } else {
        completer?.complete(message['result']);
      }
      return;
    }
    if (message['eventName'] == 'records') records = _recordList(message['records']);
    if (message['eventName'] == 'configuration') settings = Map<String, dynamic>.from(message['settings']);
    if (message['eventName'] == 'status') status = message['message'] as String;
    events.add(message);
    notifyListeners();
  }

  // 将协议数组转换为具名字段映射。
  List<Map<String, dynamic>> _recordList(dynamic value) =>
      (value as List).map((row) => Map<String, dynamic>.from(row as Map)).toList();

  // 为每个请求分配独立 ID，允许翻译期间发送取消请求。
  Future<dynamic> call(String method, [Map<String, dynamic> args = const {}]) {
    if (_process == null) return Future.error(StateError('Windows 服务未连接。'));
    final id = ++_sequence;
    final completer = Completer<dynamic>();
    _pending[id] = completer;
    _process!.stdin.writeln(jsonEncode({'id': id, 'method': method, 'args': args}));
    return completer.future;
  }

  // 将普通界面操作失败显示为状态反馈。
  Future<dynamic> perform(String method, [Map<String, dynamic> args = const {}]) async {
    dynamic result;
    try {
      result = await call(method, args);
      status = '';
    } catch (error) {
      status = error.toString();
    }
    notifyListeners();
    return result;
  }

  // 服务断开时结束等待中的请求，避免界面持续加载。
  void _disconnect(String message) {
    _process = null;
    status = message;
    for (final completer in _pending.values) {
      completer.completeError(StateError(message));
    }
    _pending.clear();
    notifyListeners();
  }
}
