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
  testWidgets('anonymous codes register uses native actions and keeps missing codes explicit', (tester) async {
    final host = TestHost(const SchoolSession(stage: Stage.schoolRegister,
      register: SchoolRegister(kind: 'codes', title: 'Codes anonymes', token: 'test-token',
        rows: [RegisterRow('id1', 'Dossier test', '', 'À attribuer', '', [])])));
    await tester.pumpWidget(ScoliaApp(host: host));
    await tester.pumpAndSettle();
    expect(find.text('Code non attribué'), findsOneWidget);
    await tester.ensureVisible(find.text('Actions'));
    await tester.tap(find.text('Actions'));
    await tester.pumpAndSettle();
    expect(host.last, Operation.registerNative);
    expect(host.lastArgs, {'token': 'test-token', 'row': 'id1'});
    expect(tester.takeException(), isNull);
  });
  testWidgets('teacher mark entry sends only validated anonymous code and score', (tester) async {
    final host = TestHost(const SchoolSession(stage: Stage.schoolRegister,
      register: SchoolRegister(kind: 'marks', title: 'Mes cours', token: 'test-token',
        date: 'course1', canWrite: true,
        marks: MarkInput(code: 'ABC123', validatedCode: 'ABC123', maximum: 'Note sur 20', score: '12'))));
    await tester.pumpWidget(ScoliaApp(host: host));
    await tester.pumpAndSettle();
    await tester.ensureVisible(find.text('Enregistrer la cote'));
    await tester.tap(find.text('Enregistrer la cote'));
    await tester.pumpAndSettle();
    expect(host.last, Operation.marksSave);
    expect(host.lastArgs, {'token': 'test-token', 'code': 'ABC123', 'score': '12'});
    expect(tester.takeException(), isNull);
  });
  testWidgets('deliberation exports only the confirmed student bulletin', (tester) async {
    final host = TestHost(const SchoolSession(stage: Stage.schoolRegister,
      register: SchoolRegister(kind: 'deliberation', title: 'Délibération', token: 'test-token',
        promotion: 'inf1', overview: 'Résultats provisoires',
        rows: [RegisterRow('student1', 'Dossier test', 'ABC123', 'Admis', '', [])])));
    await tester.pumpWidget(ScoliaApp(host: host));
    await tester.pumpAndSettle();
    expect(find.text('Résultats provisoires'), findsOneWidget);
    await tester.ensureVisible(find.text('Bulletin PDF'));
    await tester.tap(find.text('Bulletin PDF'));
    await tester.pumpAndSettle();
    expect(host.last, Operation.academicExport);
    expect(host.lastArgs, {'token': 'test-token', 'document': 'bulletin', 'row': 'student1'});
    expect(tester.takeException(), isNull);
  });
  testWidgets('LMD fee configuration opens only the selected promotion plan', (tester) async {
    final host = TestHost(const SchoolSession(stage: Stage.schoolRegister,
      register: SchoolRegister(kind: 'lmd', title: 'Organisation LMD', token: 'lmd-token',
        promotion: 'l1', canWrite: true, overview: '300 USD · 3 tranche(s)')));
    await tester.pumpWidget(ScoliaApp(host: host));
    await tester.pumpAndSettle();
    expect(find.text('300 USD · 3 tranche(s)'), findsOneWidget);
    await tester.ensureVisible(find.text('Configurer les tranches'));
    await tester.tap(find.text('Configurer les tranches'));
    await tester.pumpAndSettle();
    expect(host.last, Operation.lmdFeeForm);
    expect(host.lastArgs, {'token': 'lmd-token'});
    expect(tester.takeException(), isNull);
  });
  testWidgets('settings opens actions for the confirmed promotion', (tester) async {
    final host = TestHost(const SchoolSession(stage: Stage.schoolRegister,
      register: SchoolRegister(kind: 'settings', title: 'Configuration', token: 'settings-token',
        date: 'promotions', canWrite: true, options: [FilterChoice('promotions', 'Promotions et frais')],
        rows: [RegisterRow('promotion1', 'Promotion test', 'INF1', 'Active', '', [])])));
    await tester.pumpWidget(ScoliaApp(host: host));
    await tester.pumpAndSettle();
    expect(find.text('Promotion test'), findsOneWidget);
    await tester.ensureVisible(find.text('Modifier'));
    await tester.tap(find.text('Modifier'));
    await tester.pumpAndSettle();
    expect(host.last, Operation.settingsForm);
    expect(host.lastArgs, {'token': 'settings-token', 'row': 'promotion1'});
    expect(tester.takeException(), isNull);
  });
  testWidgets('promotion form sends its own command and preserves active flag', (tester) async {
    final host = TestHost(const SchoolSession(stage: Stage.enrollment,
      enrollment: EnrollmentForm(token: 'promotion-token', title: 'Nouvelle promotion', subtitle: '',
        purpose: 'promotion', fields: [
          EnrollmentField(key: 'actif', label: 'Promotion active', kind: 'Flag', value: 'true'),
        ])));
    await tester.pumpWidget(ScoliaApp(host: host));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Promotion active'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Enregistrer la promotion'));
    await tester.pumpAndSettle();
    expect(host.last, Operation.settingsSubmit);
    expect(host.lastArgs, {'token': 'promotion-token', 'actif': 'false'});
  });
  testWidgets('user registry opens only the selected account reset form', (tester) async {
    final host = TestHost(const SchoolSession(stage: Stage.schoolRegister,
      register: SchoolRegister(kind: 'users', title: 'Utilisateurs', token: 'users-token', canWrite: true,
        rows: [RegisterRow('user1', 'Compte test', 'test', 'Actif', '', [])])));
    await tester.pumpWidget(ScoliaApp(host: host));
    await tester.pumpAndSettle();
    await tester.ensureVisible(find.text('Code provisoire'));
    await tester.tap(find.text('Code provisoire'));
    await tester.pumpAndSettle();
    expect(host.last, Operation.userForm);
    expect(host.lastArgs, {'token': 'users-token', 'row': 'user1', 'mode': 'reset'});
    expect(tester.takeException(), isNull);
  });
  testWidgets('promoter dashboard shows aggregate data and no cashier action', (tester) async {
    final host = TestHost(const SchoolSession(stage: Stage.schoolRegister,
      modules: ['cashier'], register: SchoolRegister(kind: 'promoter', title: 'Vue d’ensemble',
        rows: [RegisterRow('', 'Recettes', '300 FC', 'Indicateurs', '', []),
          RegisterRow('', 'Promotion test', 'L1', 'Promotions', '', [], share: 0.5)])));
    await tester.pumpWidget(ScoliaApp(host: host));
    await tester.pumpAndSettle();
    expect(find.text('300 FC'), findsOneWidget);
    expect(find.text('Consultation uniquement · données agrégées'), findsOneWidget);
    expect(find.text('Encaisser'), findsNothing);
    expect(tester.takeException(), isNull);
  });
  testWidgets('home search only shows authorized features', (tester) async {
    final host = TestHost(const SchoolSession(stage: Stage.home,
      modules: ['students', 'cashier'], catalog: [
        ModuleEntry('students', 'Dossiers scolaires', 'École'),
        ModuleEntry('cashier', 'Encaisser', 'Finances'),
        ModuleEntry('settings', 'Configuration', 'Administration'),
      ]));
    await tester.pumpWidget(ScoliaApp(host: host));
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(TextField).first, 'Encaisser');
    await tester.pumpAndSettle();
    expect(find.text('Encaisser'), findsOneWidget);
    expect(find.text('Dossiers scolaires'), findsNothing);
    expect(find.text('Configuration'), findsNothing);
  });
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
  testWidgets('home displays actual native synchronisation status', (tester) async {
    final host = TestHost(const SchoolSession(stage: Stage.home, school: 'Établissement test',
      modules: ['network'], header: WorkspaceHeader(greeting: 'Bonjour, Compte', date: '25 novembre',
        initials: 'C', syncStatus: 'Hors ligne', syncHint: 'Données locales disponibles',
        offline: true, pending: false)));
    await tester.pumpWidget(ScoliaApp(host: host));
    await tester.pumpAndSettle();
    expect(find.text('Bonjour, Compte'), findsOneWidget);
    expect(find.text('Hors ligne'), findsOneWidget);
    expect(find.text('Données locales disponibles'), findsOneWidget);
  });
  testWidgets('student profile renders actual sections without adding unauthorised cashier', (tester) async {
    final host = TestHost(const SchoolSession(stage: Stage.studentProfile, modules: ['students'],
      profile: StudentProfile(id: 'test', title: 'Dossier test', subtitle: 'INF1',
        sections: [ProfileSection('Identité', [[ProfileValue('Matricule', 'INF001')]])])));
    await tester.pumpWidget(ScoliaApp(host: host));
    await tester.pumpAndSettle();
    expect(find.text('Dossier test'), findsOneWidget);
    expect(find.text('INF001'), findsOneWidget);
    expect(find.text('Encaisser'), findsNothing);
    await tester.tap(find.text('Retour au registre'));
    await tester.pumpAndSettle();
    expect(host.last, Operation.listStudents);
  });
  testWidgets('enrollment sends native form token and typed values', (tester) async {
    final host = TestHost(const SchoolSession(stage: Stage.enrollment,
      enrollment: EnrollmentForm(token: 'test-token', title: 'Nouvelle inscription', subtitle: '',
        fields: [EnrollmentField(key: 'nom', label: 'Nom', kind: 'Text', required: true)])));
    await tester.pumpWidget(ScoliaApp(host: host));
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(TextField), 'Nom de test');
    await tester.ensureVisible(find.text('Inscrire'));
    await tester.tap(find.text('Inscrire'));
    await tester.pumpAndSettle();
    expect(host.last, Operation.submitEnrollment);
    expect(host.lastArgs, {'token': 'test-token', 'nom': 'Nom de test'});
  });
  testWidgets('cashier keeps confirmation unavailable until new preview', (tester) async {
    final host = TestHost(const SchoolSession(stage: Stage.cashier,
      cashier: CashierScreen(student: 'Dossier test', hasStudent: true, canPay: true,
        amount: '100', method: 'Especes', total: '100',
        methods: [FilterChoice('Especes', 'Espèces')],
        fees: [CashierFee('fee', 'Octobre', '100', 'Échéance', true, false)])));
    await tester.pumpWidget(ScoliaApp(host: host));
    await tester.pumpAndSettle();
    await tester.ensureVisible(find.widgetWithText(TextField, 'Montant reçu'));
    await tester.enterText(find.widgetWithText(TextField, 'Montant reçu'), '50');
    await tester.pumpAndSettle();
    expect(find.text('Confirmer 100'), findsNothing);
    await tester.ensureVisible(find.text('Vérifier la répartition'));
    await tester.tap(find.text('Vérifier la répartition'));
    await tester.pumpAndSettle();
    expect(host.last, Operation.cashierPreview);
    expect(host.lastArgs['amount'], '50');
    expect(host.lastArgs['fees'], 'fee');
  });
  testWidgets('journal preserves local receipt status and blocks unapplied export', (tester) async {
    final host = TestHost(const SchoolSession(stage: Stage.receipts, journal: ReceiptJournal(
      rows: [JournalRow(id: 'test', number: 'LOCAL-TEST', date: '25/11/2026', code: 'INF1',
        student: 'Dossier test', promotion: 'INF1', amount: '50', method: 'Espèces',
        status: 'À synchroniser', cashier: 'Compte')], count: 1)));
    await tester.pumpWidget(ScoliaApp(host: host));
    await tester.pumpAndSettle();
    expect(find.text('Espèces · À synchroniser'), findsOneWidget);
    await tester.enterText(find.byType(TextField), 'nouveau filtre');
    await tester.pumpAndSettle();
    expect(tester.widget<OutlinedButton>(find.widgetWithText(OutlinedButton, 'Excel')).onPressed, isNull);
    expect(find.text('Appliquez la recherche avant d’exporter.'), findsOneWidget);
  });
  testWidgets('attendance reader cannot submit an attendance sheet', (tester) async {
    final host = TestHost(const SchoolSession(stage: Stage.schoolRegister, register: SchoolRegister(
      kind: 'attendance', title: 'Présences', date: '2026-11-25',
      rows: [RegisterRow('id', 'Dossier test', 'INF1', 'Present', '', [])],
      options: [FilterChoice('Present', 'Présent')])));
    await tester.pumpWidget(ScoliaApp(host: host));
    await tester.pumpAndSettle();
    expect(find.text('Dossier test'), findsOneWidget);
    expect(find.text('Enregistrer l’appel'), findsNothing);
    expect(find.text('Présent'), findsOneWidget);
  });
  testWidgets('personnel shows payroll data and keeps native row actions', (tester) async {
    final host = TestHost(const SchoolSession(stage: Stage.schoolRegister, register: SchoolRegister(
      kind: 'staff', title: 'Personnel et paie', summary: true, date: '2026-11', token: 'session-form',
      options: [FilterChoice('2026-11', 'Novembre 2026')],
      rows: [RegisterRow('row1', 'Membre test', 'P01', 'Payé', '', [ProfileValue('Net', '100')])])));
    await tester.pumpWidget(ScoliaApp(host: host));
    await tester.pumpAndSettle();
    expect(find.text('Net : 100'), findsOneWidget);
    await tester.ensureVisible(find.text('Actions'));
    await tester.tap(find.text('Actions'));
    await tester.pumpAndSettle();
    expect(host.last, Operation.registerNative);
    expect(host.lastArgs, {'token': 'session-form', 'row': 'row1'});
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
