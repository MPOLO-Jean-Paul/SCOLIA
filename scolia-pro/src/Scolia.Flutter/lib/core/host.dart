import 'package:flutter/services.dart';

enum Stage { pairing, login, home, nativeScreen, loading, students, monthly, studentProfile, enrollment, cashier, receipts, schoolRegister }

enum Operation {
  snapshot, scanQr, chooseServer, createSchool, signIn, recover,
  biometric, openModule, signOut, listStudents, listMonthly, exportMonthly, openStudent, cashierStudent, studentActions, submitEnrollment, cashierSearch, cashierChoose, cashierPreview, cashierConfirm, cashierLeave, listReceipts, exportReceipts, openReceipt, listAttendance, listDiscipline, editAttendance, saveAttendance, attendanceAll, attendanceDay, disciplineExport, registerNative, listPersonnel, marksCheck, marksSave, academicExport, lmdFeeForm, lmdFeePreview, settingsForm, settingsSubmit, settingsPreview, userForm
}

class SchoolSession {
  const SchoolSession({required this.stage, this.school = '', this.user = '',
    this.year = '', this.modules = const [], this.notice = '', this.students, this.monthly, this.catalog = const [], this.header, this.profile, this.enrollment, this.cashier, this.journal, this.register});
  final Stage stage;
  final String school, user, year, notice;
  final List<String> modules;
  final StudentListing? students;
  final MonthlyListing? monthly;
  final List<ModuleEntry> catalog;
  final WorkspaceHeader? header;
  final StudentProfile? profile;
  final EnrollmentForm? enrollment;
  final CashierScreen? cashier;
  final ReceiptJournal? journal;
  final SchoolRegister? register;

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
      header: value['header'] == null ? null : WorkspaceHeader.fromMap(value['header'] as Map<Object?, Object?>),
      catalog: List<ModuleEntry>.unmodifiable((value['catalog'] as List<Object?>? ?? []).map(
        (x) => ModuleEntry.fromMap(x as Map<Object?, Object?>))),
      students: stage == Stage.students ? StudentListing.fromMap(value['students'] as Map<Object?, Object?>) : null,
      monthly: stage == Stage.monthly ? MonthlyListing.fromMap(value['monthly'] as Map<Object?, Object?>) : null,
      profile: stage == Stage.studentProfile ? StudentProfile.fromMap(value['profile'] as Map<Object?, Object?>) : null,
      enrollment: stage == Stage.enrollment ? EnrollmentForm.fromMap(value['enrollment'] as Map<Object?, Object?>) : null,
      cashier: stage == Stage.cashier ? CashierScreen.fromMap(value['cashier'] as Map<Object?, Object?>) : null,
      journal: stage == Stage.receipts ? ReceiptJournal.fromMap(value['journal'] as Map<Object?, Object?>) : null,
      register: stage == Stage.schoolRegister ? SchoolRegister.fromMap(value['register'] as Map<Object?, Object?>) : null);
  }
}

class RegisterRow {
  const RegisterRow(this.id, this.name, this.code, this.status, this.note, this.details);
  final String id, name, code, status, note;
  final List<ProfileValue> details;
  factory RegisterRow.fromMap(Map<Object?, Object?> m) => RegisterRow(m['id'] as String, m['name'] as String,
    m['code'] as String, m['status'] as String, m['note'] as String,
    (m['details'] as List<Object?>).map((v) => ProfileValue.fromMap(v as Map<Object?, Object?>)).toList());
}
class MarkInput {
  const MarkInput({this.rules = '', this.announcement = '', this.sheet = '', this.hint = '', this.maximum = '',
    this.code = '', this.validatedCode = '', this.score = '', this.feedback = '', this.kinds = const []});
  final String rules, announcement, sheet, hint, maximum, code, validatedCode, score, feedback;
  final List<FilterChoice> kinds;
  factory MarkInput.fromMap(Map<Object?, Object?> m) => MarkInput(
    rules: m['rules'] as String, announcement: m['announcement'] as String,
    sheet: m['sheet'] as String, hint: m['hint'] as String, maximum: m['maximum'] as String,
    code: m['code'] as String, validatedCode: m['validatedCode'] as String, score: m['score'] as String,
    feedback: m['feedback'] as String, kinds: (m['kinds'] as List<Object?>)
      .map((v) => FilterChoice.fromMap(v as Map<Object?, Object?>)).toList());
}
class SchoolRegister {
  const SchoolRegister({required this.kind, required this.title, this.subtitle = '', this.promotion = '',
    this.date = '', this.measure = '', this.summary = false, this.canWrite = false, this.pending = false,
    this.dirty = false, this.token = '', this.promotions = const [], this.options = const [], this.rows = const [], this.marks, this.overview = '', this.context = ''});
  final String kind, title, subtitle, promotion, date, measure, token;
  final bool summary, canWrite, pending, dirty;
  final List<FilterChoice> promotions, options;
  final List<RegisterRow> rows;
  final MarkInput? marks;
  final String overview, context;
  factory SchoolRegister.fromMap(Map<Object?, Object?> m) {
    List<FilterChoice> choices(String key) => (m[key] as List<Object?>).map((v) => FilterChoice.fromMap(v as Map<Object?, Object?>)).toList();
    return SchoolRegister(kind: m['kind'] as String, title: m['title'] as String, subtitle: m['subtitle'] as String,
      promotion: m['promotion'] as String, date: m['date'] as String, measure: m['measure'] as String,
      summary: m['summary'] as bool, canWrite: m['canWrite'] as bool, pending: m['pending'] as bool,
      dirty: m['dirty'] as bool, token: m['token'] as String, promotions: choices('promotions'), options: choices('options'),
      rows: (m['rows'] as List<Object?>).map((v) => RegisterRow.fromMap(v as Map<Object?, Object?>)).toList(),
      marks: m['marks'] == null ? null : MarkInput.fromMap(m['marks'] as Map<Object?, Object?>),
      overview: m['overview'] as String? ?? '', context: m['context'] as String? ?? '');
  }
}

class JournalRow {
  const JournalRow({required this.id, required this.number, required this.date, required this.code,
    required this.student, required this.promotion, required this.amount, required this.method,
    required this.status, required this.cashier});
  final String id, number, date, code, student, promotion, amount, method, status, cashier;
  factory JournalRow.fromMap(Map<Object?, Object?> m) => JournalRow(id: m['id'] as String,
    number: m['number'] as String, date: m['date'] as String, code: m['code'] as String,
    student: m['student'] as String, promotion: m['promotion'] as String, amount: m['amount'] as String,
    method: m['method'] as String, status: m['status'] as String, cashier: m['cashier'] as String);
}
class ReceiptJournal {
  const ReceiptJournal({this.title = 'Journal des reçus', this.subtitle = '', this.search = '', this.from = '',
    this.to = '', this.method = '', this.status = '', this.promotion = '', this.page = 1, this.count = 0,
    this.hasPrevious = false, this.hasNext = false, this.methods = const [], this.statuses = const [],
    this.promotions = const [], this.rows = const []});
  final String title, subtitle, search, from, to, method, status, promotion;
  final int page, count;
  final bool hasPrevious, hasNext;
  final List<FilterChoice> methods, statuses, promotions;
  final List<JournalRow> rows;
  factory ReceiptJournal.fromMap(Map<Object?, Object?> m) {
    List<FilterChoice> choices(String key) => List<FilterChoice>.unmodifiable(
      (m[key] as List<Object?>).map((v) => FilterChoice.fromMap(v as Map<Object?, Object?>)));
    return ReceiptJournal(title: m['title'] as String, subtitle: m['subtitle'] as String, search: m['search'] as String,
      from: m['from'] as String, to: m['to'] as String, method: m['method'] as String, status: m['status'] as String,
      promotion: m['promotion'] as String, page: m['page'] as int, count: m['count'] as int,
      hasPrevious: m['hasPrevious'] as bool, hasNext: m['hasNext'] as bool,
      methods: choices('methods'), statuses: choices('statuses'), promotions: choices('promotions'),
      rows: List<JournalRow>.unmodifiable((m['rows'] as List<Object?>).map((v) => JournalRow.fromMap(v as Map<Object?, Object?>))));
  }
}

class CashierPayer {
  const CashierPayer(this.id, this.label, this.detail);
  final String id, label, detail;
  factory CashierPayer.fromMap(Map<Object?, Object?> m) => CashierPayer(m['id'] as String, m['label'] as String, m['detail'] as String);
}
class CashierFee {
  const CashierFee(this.id, this.label, this.amount, this.deadline, this.selected, this.overdue);
  final String id, label, amount, deadline;
  final bool selected, overdue;
  factory CashierFee.fromMap(Map<Object?, Object?> m) => CashierFee(m['id'] as String, m['label'] as String,
    m['amount'] as String, m['deadline'] as String, m['selected'] as bool, m['overdue'] as bool);
}
class CashierAllocation {
  const CashierAllocation(this.label, this.amount, this.remaining);
  final String label, amount, remaining;
  factory CashierAllocation.fromMap(Map<Object?, Object?> m) => CashierAllocation(m['label'] as String, m['amount'] as String, m['remaining'] as String);
}
class CashierScreen {
  const CashierScreen({this.student = '', this.info = '', this.balance = '', this.overdue = '', this.hint = '',
    this.searchStatus = '', this.amount = '', this.method = '', this.reference = '', this.note = '', this.total = '',
    this.balanceAfter = '', this.receipt = '', this.hasStudent = false, this.canPay = false,
    this.needsReference = false, this.hasReceipt = false, this.payers = const [], this.fees = const [],
    this.methods = const [], this.allocations = const []});
  final String student, info, balance, overdue, hint, searchStatus, amount, method, reference, note, total, balanceAfter, receipt;
  final bool hasStudent, canPay, needsReference, hasReceipt;
  final List<CashierPayer> payers;
  final List<CashierFee> fees;
  final List<FilterChoice> methods;
  final List<CashierAllocation> allocations;
  factory CashierScreen.fromMap(Map<Object?, Object?> m) {
    List<T> read<T>(String k, T Function(Map<Object?, Object?>) parse) => List<T>.unmodifiable(
      (m[k] as List<Object?>).map((v) => parse(v as Map<Object?, Object?>)));
    return CashierScreen(student: m['student'] as String, info: m['info'] as String, balance: m['balance'] as String,
      overdue: m['overdue'] as String, hint: m['hint'] as String, searchStatus: m['searchStatus'] as String,
      amount: m['amount'] as String, method: m['method'] as String, reference: m['reference'] as String,
      note: m['note'] as String, total: m['total'] as String, balanceAfter: m['balanceAfter'] as String,
      receipt: m['receipt'] as String, hasStudent: m['hasStudent'] as bool, canPay: m['canPay'] as bool,
      needsReference: m['needsReference'] as bool, hasReceipt: m['hasReceipt'] as bool,
      payers: read('payers', CashierPayer.fromMap), fees: read('fees', CashierFee.fromMap),
      methods: read('methods', FilterChoice.fromMap), allocations: read('allocations', CashierAllocation.fromMap));
  }
}

class EnrollmentField {
  const EnrollmentField({required this.key, required this.label, required this.kind,
    this.required = false, this.value = '', this.hint = '', this.error = '', this.options = const []});
  final String key, label, kind, value, hint, error;
  final bool required;
  final List<FilterChoice> options;
  factory EnrollmentField.fromMap(Map<Object?, Object?> m) => EnrollmentField(
    key: m['key'] as String, label: m['label'] as String, kind: m['kind'] as String,
    required: m['required'] as bool, value: m['value'] as String, hint: m['hint'] as String,
    error: m['error'] as String, options: List<FilterChoice>.unmodifiable(
      (m['options'] as List<Object?>).map((x) => FilterChoice.fromMap(x as Map<Object?, Object?>))));
}

class EnrollmentForm {
  const EnrollmentForm({required this.token, required this.title, required this.subtitle,
    this.error = '', required this.fields, this.purpose = 'enrollment'});
  final String token, title, subtitle, error, purpose;
  final List<EnrollmentField> fields;
  factory EnrollmentForm.fromMap(Map<Object?, Object?> m) => EnrollmentForm(
    token: m['token'] as String, title: m['title'] as String, subtitle: m['subtitle'] as String,
    error: m['error'] as String, purpose: m['purpose'] as String? ?? 'enrollment', fields: List<EnrollmentField>.unmodifiable(
      (m['fields'] as List<Object?>).map((x) => EnrollmentField.fromMap(x as Map<Object?, Object?>))));
}

class ProfileValue {
  const ProfileValue(this.label, this.value);
  final String label, value;
  factory ProfileValue.fromMap(Map<Object?, Object?> m) =>
    ProfileValue(m['label'] as String, m['value'] as String);
}

class ProfileSection {
  const ProfileSection(this.title, this.rows);
  final String title;
  final List<List<ProfileValue>> rows;
  factory ProfileSection.fromMap(Map<Object?, Object?> m) => ProfileSection(m['title'] as String,
    List<List<ProfileValue>>.unmodifiable((m['rows'] as List<Object?>).map((r) =>
      List<ProfileValue>.unmodifiable((r as List<Object?>).map((v) => ProfileValue.fromMap(v as Map<Object?, Object?>))))));
}

class StudentProfile {
  const StudentProfile({required this.id, required this.title, required this.subtitle, required this.sections});
  final String id, title, subtitle;
  final List<ProfileSection> sections;
  factory StudentProfile.fromMap(Map<Object?, Object?> m) => StudentProfile(
    id: m['id'] as String, title: m['title'] as String, subtitle: m['subtitle'] as String,
    sections: List<ProfileSection>.unmodifiable((m['sections'] as List<Object?>).map(
      (s) => ProfileSection.fromMap(s as Map<Object?, Object?>))));
}

class WorkspaceHeader {
  const WorkspaceHeader({required this.greeting, required this.date, required this.initials,
    required this.syncStatus, required this.syncHint, required this.offline, required this.pending});
  final String greeting, date, initials, syncStatus, syncHint;
  final bool offline, pending;
  factory WorkspaceHeader.fromMap(Map<Object?, Object?> m) => WorkspaceHeader(
    greeting: m['greeting'] as String, date: m['date'] as String,
    initials: m['initials'] as String, syncStatus: m['syncStatus'] as String,
    syncHint: m['syncHint'] as String, offline: m['offline'] as bool, pending: m['pending'] as bool);
}

class ModuleEntry {
  const ModuleEntry(this.id, this.label, this.group);
  final String id, label, group;
  factory ModuleEntry.fromMap(Map<Object?, Object?> m) => ModuleEntry(
    m['id'] as String, m['label'] as String, m['group'] as String);
}

class StudentEntry {
  const StudentEntry({required this.name, required this.code, required this.promotion, required this.status, this.id = ''});
  final String name, code, promotion, status, id;
  factory StudentEntry.fromMap(Map<Object?, Object?> data) => StudentEntry(
    name: data['name'] as String, code: data['code'] as String,
    promotion: data['promotion'] as String, status: data['status'] as String, id: data['id'] as String);
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
