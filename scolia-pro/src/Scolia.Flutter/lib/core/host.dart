import 'package:flutter/services.dart';

enum Stage { pairing, login, home, nativeScreen, loading, students }

enum Operation {
  snapshot, scanQr, chooseServer, createSchool, signIn, recover,
  biometric, openModule, signOut, listStudents
}

class SchoolSession {
  const SchoolSession({required this.stage, this.school = '', this.user = '',
    this.year = '', this.modules = const [], this.notice = '', this.students});
  final Stage stage;
  final String school, user, year, notice;
  final List<String> modules;
  final StudentListing? students;

  factory SchoolSession.fromMap(Map<Object?, Object?> value) {
    if (value['protocol'] != 1) {
      throw const FormatException('Protocole SCOLIA incompatible.');
    }
    final stage = Stage.values.byName(value['stage'] as String);
    final modules = (value['modules'] as List<Object?>? ?? []).whereType<String>();
    return SchoolSession(stage: stage, school: value['school'] as String? ?? '',
      user: value['user'] as String? ?? '', year: value['year'] as String? ?? '',
      notice: value['notice'] as String? ?? '',
      modules: List<String>.unmodifiable(modules),
      students: stage == Stage.students ? StudentListing.fromMap(value['students'] as Map<Object?, Object?>) : null);
  }
}

class StudentEntry {
  const StudentEntry({required this.name, required this.code, required this.promotion, required this.status});
  final String name, code, promotion, status;
  factory StudentEntry.fromMap(Map<Object?, Object?> data) => StudentEntry(
    name: data['name'] as String, code: data['code'] as String,
    promotion: data['promotion'] as String, status: data['status'] as String);
}

class StudentListing {
  const StudentListing({required this.title, required this.subtitle, required this.page,
    required this.search, required this.hasPrevious, required this.hasNext, required this.rows});
  final String title, subtitle, search;
  final int page;
  final bool hasPrevious, hasNext;
  final List<StudentEntry> rows;
  factory StudentListing.fromMap(Map<Object?, Object?> data) => StudentListing(
    title: data['title'] as String, subtitle: data['subtitle'] as String,
    page: data['page'] as int, search: data['search'] as String,
    hasPrevious: data['hasPrevious'] as bool, hasNext: data['hasNext'] as bool,
    rows: List<StudentEntry>.unmodifiable((data['rows'] as List<Object?>).map(
      (r) => StudentEntry.fromMap(r as Map<Object?, Object?>))));
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
      operation.name, arguments).timeout(const Duration(seconds: 60));
    if (response == null) throw const FormatException('Réponse SCOLIA absente.');
    return SchoolSession.fromMap(response);
  }
}
