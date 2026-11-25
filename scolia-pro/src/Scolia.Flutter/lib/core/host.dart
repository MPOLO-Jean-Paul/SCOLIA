import 'package:flutter/services.dart';

enum Stage { pairing, login, home, nativeScreen, loading, students, monthly }

enum Operation {
  snapshot, scanQr, chooseServer, createSchool, signIn, recover,
  biometric, openModule, signOut, listStudents, listMonthly, exportMonthly
}

class SchoolSession {
  const SchoolSession({required this.stage, this.school = '', this.user = '',
    this.year = '', this.modules = const [], this.notice = '', this.students, this.monthly});
  final Stage stage;
  final String school, user, year, notice;
  final List<String> modules;
  final StudentListing? students;
  final MonthlyListing? monthly;

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
      students: stage == Stage.students ? StudentListing.fromMap(value['students'] as Map<Object?, Object?>) : null,
      monthly: stage == Stage.monthly ? MonthlyListing.fromMap(value['monthly'] as Map<Object?, Object?>) : null);
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

class FilterChoice {
  const FilterChoice(this.id, this.label);
  final String id, label;
  factory FilterChoice.fromMap(Map<Object?, Object?> m) =>
    FilterChoice(m['id'] as String, m['label'] as String);
}

class PaymentEntry {
  const PaymentEntry({required this.name, required this.promotion, required this.expected,
    required this.paid, required this.remaining, required this.status});
  final String name, promotion, expected, paid, remaining, status;
  factory PaymentEntry.fromMap(Map<Object?, Object?> m) => PaymentEntry(
    name: m['name'] as String, promotion: m['promotion'] as String,
    expected: m['expected'] as String, paid: m['paid'] as String,
    remaining: m['remaining'] as String, status: m['status'] as String);
}

class MonthlyListing {
  const MonthlyListing({required this.title, required this.subtitle, required this.period,
    required this.promotion, required this.status, required this.search, required this.page,
    required this.count, required this.hasPrevious, required this.hasNext, required this.periods,
    required this.promotions, required this.states, required this.rows, required this.exportScope});
  final String title, subtitle, period, promotion, status, search, exportScope;
  final int page, count;
  final bool hasPrevious, hasNext;
  final List<FilterChoice> periods, promotions, states;
  final List<PaymentEntry> rows;
  factory MonthlyListing.fromMap(Map<Object?, Object?> m) {
    List<FilterChoice> choices(String key) => List<FilterChoice>.unmodifiable(
      (m[key] as List<Object?>).map((x) => FilterChoice.fromMap(x as Map<Object?, Object?>)));
    return MonthlyListing(title: m['title'] as String, subtitle: m['subtitle'] as String,
      period: m['period'] as String, promotion: m['promotion'] as String,
      status: m['status'] as String, search: m['search'] as String, page: m['page'] as int,
      count: m['count'] as int, hasPrevious: m['hasPrevious'] as bool, hasNext: m['hasNext'] as bool,
      periods: choices('periods'), promotions: choices('promotions'), states: choices('states'),
      rows: List<PaymentEntry>.unmodifiable((m['rows'] as List<Object?>).map(
        (x) => PaymentEntry.fromMap(x as Map<Object?, Object?>))), exportScope: m['exportScope'] as String);
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
  static const channel = MethodChannel('com.jpservices.scolia/mobile/v1', JSONMethodCodec());

  @override
  Future<SchoolSession> execute(Operation operation,
      [Map<String, String> arguments = const {}]) async {
    final response = await channel.invokeMapMethod<Object?, Object?>(
      operation.name, arguments).timeout(const Duration(seconds: 60));
    if (response == null) throw const FormatException('Réponse SCOLIA absente.');
    return SchoolSession.fromMap(response);
  }
}
