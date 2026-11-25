import 'package:flutter/services.dart';

enum Stage { pairing, login, home, nativeScreen, loading }

enum Operation {
  snapshot, scanQr, chooseServer, createSchool, signIn, recover,
  biometric, openModule, signOut
}

class SchoolSession {
  const SchoolSession({required this.stage, this.school = '', this.user = '',
    this.year = '', this.modules = const [], this.notice = ''});
  final Stage stage;
  final String school, user, year, notice;
  final List<String> modules;

  factory SchoolSession.fromMap(Map<Object?, Object?> value) {
    if (value['protocol'] != 1) {
      throw const FormatException('Protocole SCOLIA incompatible.');
    }
    final stage = Stage.values.byName(value['stage'] as String);
    final modules = (value['modules'] as List<Object?>? ?? []).whereType<String>();
    return SchoolSession(stage: stage, school: value['school'] as String? ?? '',
      user: value['user'] as String? ?? '', year: value['year'] as String? ?? '',
      notice: value['notice'] as String? ?? '',
      modules: List<String>.unmodifiable(modules));
  }
}

abstract interface class ScoliaHost {
  Future<SchoolSession> execute(Operation operation,
      [Map<String, String> arguments = const {}]);
}

/// No HTTP bypass, no local credential storage, no simulated successful login.
/// Host authorization, device pairing, offline queue and database remain authoritative.
class NativeHost implements ScoliaHost {
  const NativeHost();
  static const channel = MethodChannel('com.jpservices.scolia/mobile/v1');

  @override
  Future<SchoolSession> execute(Operation operation,
      [Map<String, String> arguments = const {}]) async {
    final response = await channel.invokeMapMethod<Object?, Object?>(
      operation.name, arguments);
    if (response == null) throw const FormatException('Réponse SCOLIA absente.');
    return SchoolSession.fromMap(response);
  }
}
