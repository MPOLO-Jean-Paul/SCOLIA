import 'dart:async';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:scolia_flutter/core/host.dart';
import 'package:scolia_flutter/main.dart';

class TestHost implements ScoliaHost {
  TestHost(this.state);
  SchoolSession state;
  Operation? last;
  @override
  Future<SchoolSession> execute(Operation op, [Map<String, String> args = const {}]) async {
    last = op;
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
