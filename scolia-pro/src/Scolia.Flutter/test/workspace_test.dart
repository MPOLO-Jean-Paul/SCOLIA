import 'dart:async';
import 'package:flutter/services.dart';
import 'package:flutter/material.dart' show TextField, OutlinedButton;
import 'package:flutter_test/flutter_test.dart';
import 'package:scolia_flutter/core/host.dart';
import 'package:scolia_flutter/main.dart';

class TestHost implements ScoliaHost {
  TestHost(this.state);
  SchoolSession state;
  Operation? last;
  Map<String, String> lastArgs = {};
  @override
  Future<SchoolSession> execute(Operation op, [Map<String, String> args = const {}]) async {
    last = op;
    lastArgs = args;
    return state;
  }
}

class MissingHost implements ScoliaHost {
  @override
  Future<SchoolSession> execute(Operation op, [Map<String, String> args = const {}]) async {
    throw MissingPluginException();
  }
}

void main() {
  test('reject incompatible protocol', () {
    expect(() => SchoolSession.fromMap({'protocol': 2, 'stage': 'home'}), throwsFormatException);
  });
  testWidgets('missing bridge fails closed', (tester) async {
    await tester.pumpWidget(ScoliaApp(host: MissingHost()));
    await tester.pumpAndSettle();
    expect(find.text('Le moteur SCOLIA natif n’est pas raccordé à ce module.'), findsOneWidget);
    expect(find.text('Se connecter'), findsNothing);
  });
  testWidgets('unresponsive channel times out without exposing workspace', (tester) async {
    final messenger = TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger;
    final pending = Completer<Object?>();
    messenger.setMockMethodCallHandler(NativeHost.channel, (_) => pending.future);
    addTearDown(() {
      if (!pending.isCompleted) pending.complete(null);
      messenger.setMockMethodCallHandler(NativeHost.channel, null);
    });
    await tester.pumpWidget(const ScoliaApp(host: NativeHost()));
    await tester.pump(const Duration(seconds: 61));
    await tester.pumpAndSettle();
    expect(find.text('Le moteur SCOLIA ne répond pas. Réessayez.'), findsOneWidget);
    expect(find.text('Encaisser'), findsNothing);
  });
  testWidgets('permissions and narrow layout', (tester) async {
    tester.view.physicalSize = const Size(320, 720);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final host = TestHost(const SchoolSession(stage: Stage.home, user: 'Test',
      modules: ['students']));
    await tester.pumpWidget(ScoliaApp(host: host));
    await tester.pumpAndSettle();
    expect(find.text('Dossiers scolaires'), findsOneWidget);
    expect(find.text('Encaisser'), findsNothing);
    await tester.tap(find.text('Dossiers scolaires'));
    await tester.pumpAndSettle();
    expect(host.last, Operation.listStudents);
    expect(tester.takeException(), isNull);
  });
  testWidgets('student list displays supplied records on narrow screen', (tester) async {
    tester.view.physicalSize = const Size(320, 720);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final host = TestHost(const SchoolSession(stage: Stage.students,
      students: StudentListing(title: 'Apprenants', subtitle: '1 dossier', page: 1,
        search: '', hasPrevious: false, hasNext: true,
        rows: [StudentEntry(name: 'Dossier de test', code: 'T001', promotion: 'INF1', status: 'Actif')])));
    await tester.pumpWidget(ScoliaApp(host: host));
    await tester.pumpAndSettle();
    expect(find.text('Dossier de test'), findsOneWidget);
    await tester.ensureVisible(find.byTooltip('Page suivante'));
    await tester.tap(find.byTooltip('Page suivante'));
    await tester.pumpAndSettle();
    expect(host.last, Operation.listStudents);
    expect(tester.takeException(), isNull);
  });
  testWidgets('monthly export uses confirmed selection and keeps pagination', (tester) async {
    final host = TestHost(const SchoolSession(stage: Stage.monthly,
      monthly: MonthlyListing(title: 'Suivi mensuel', subtitle: 'Octobre · INF1',
        period: 'oct', promotion: 'inf1', status: 'paye', search: '', page: 1, count: 32,
        hasPrevious: false, hasNext: true, periods: [FilterChoice('oct', 'Octobre')],
        promotions: [FilterChoice('inf1', 'INF1')], states: [FilterChoice('paye', 'Payés')],
        rows: [PaymentEntry(name: 'Dossier test', promotion: 'INF1', expected: '100',
          paid: '100', remaining: '0', status: 'Payé')], exportScope: 'Export : Octobre · INF1')));
    await tester.pumpWidget(ScoliaApp(host: host));
    await tester.pumpAndSettle();
    await tester.ensureVisible(find.text('Excel'));
    await tester.tap(find.text('Excel'));
    await tester.pumpAndSettle();
    expect(host.last, Operation.exportMonthly);
    expect(host.lastArgs, {'format': 'xlsx', 'page': '1'});
    await tester.ensureVisible(find.byType(TextField));
    await tester.enterText(find.byType(TextField), 'nouveau filtre');
    await tester.pumpAndSettle();
    final excel = tester.widget<OutlinedButton>(find.widgetWithText(OutlinedButton, 'Excel'));
    expect(excel.onPressed, isNull);
    expect(find.text('Appliquez la recherche avant d’exporter.'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
  testWidgets('catalog exposes authorised native modules only', (tester) async {
    final host = TestHost(const SchoolSession(stage: Stage.home,
      modules: ['page:CardsPage'], catalog: [
        ModuleEntry('page:CardsPage', 'Cartes d’accès', 'Scolarité'),
        ModuleEntry('page:UsersPage', 'Utilisateurs', 'Administration'),
      ]));
    await tester.pumpWidget(ScoliaApp(host: host));
    await tester.pumpAndSettle();
    expect(find.text('Cartes d’accès'), findsOneWidget);
    expect(find.text('Utilisateurs'), findsNothing);
    await tester.tap(find.text('Cartes d’accès'));
    await tester.pumpAndSettle();
    expect(host.last, Operation.openModule);
    expect(host.lastArgs['module'], 'page:CardsPage');
  });
  testWidgets('native screen never exposes workspace actions', (tester) async {
    final host = TestHost(const SchoolSession(stage: Stage.nativeScreen,
      modules: ['cashier', 'students']));
    await tester.pumpWidget(ScoliaApp(host: host));
    await tester.pumpAndSettle();
    expect(find.text('Encaisser'), findsNothing);
    expect(find.text('Dossiers scolaires'), findsNothing);
    expect(find.text('Interface SCOLIA'), findsOneWidget);
  });
  testWidgets('pairing and login validation', (tester) async {
    final host = TestHost(const SchoolSession(stage: Stage.pairing));
    await tester.pumpWidget(ScoliaApp(host: host));
    await tester.pumpAndSettle();
    expect(find.text('Scanner le code QR'), findsOneWidget);
    host.state = const SchoolSession(stage: Stage.login, school: 'Test');
    await tester.tap(find.text('Scanner le code QR'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Se connecter'));
    await tester.pumpAndSettle();
    expect(find.text('Identifiant requis'), findsOneWidget);
    expect(host.last, Operation.scanQr);
  });
}
