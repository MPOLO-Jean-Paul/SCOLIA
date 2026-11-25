// Captures des widgets de production, avec données fictives réservées aux tests.
import 'dart:io';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:scolia_flutter/core/host.dart';
import 'package:scolia_flutter/main.dart';

class PreviewHost implements ScoliaHost {
  const PreviewHost(this.state);
  final SchoolSession state;
  @override
  Future<SchoolSession> execute(Operation operation,
    [Map<String, String> args = const {}]) async => state;
}

const modules = ['enroll', 'cashier', 'students', 'monthly', 'dashboard', 'receipts',
  'attendance', 'grades', 'lmd', 'settings', 'page:UsersPage', 'network'];
const header = WorkspaceHeader(greeting: 'Bonjour, Secrétariat', date: '25 novembre 2026',
  initials: 'S', syncStatus: 'Synchronisé', syncHint: 'Données locales disponibles',
  offline: false, pending: false);
const school = 'Établissement de démonstration';
const previews = <String, SchoolSession>{
  '01-connexion': SchoolSession(stage: Stage.login, school: school),
  '02-accueil': SchoolSession(stage: Stage.home, school: school, user: 'Secrétariat',
    year: 'Année 2026-2027', modules: modules, header: header),
  '03-apprenants': SchoolSession(stage: Stage.students, modules: modules,
    students: StudentListing(title: 'Apprenants', subtitle: '3 dossiers de démonstration',
      page: 1, search: '', hasPrevious: false, hasNext: false, rows: [
        StudentEntry(name: 'Apprenant exemple A', code: 'EX001', promotion: 'INF1', status: 'Actif'),
        StudentEntry(name: 'Apprenant exemple B', code: 'EX002', promotion: 'INF2', status: 'Actif'),
        StudentEntry(name: 'Apprenant exemple C', code: 'EX003', promotion: 'ACC1', status: 'Actif'),
      ])),
  '04-inscription': SchoolSession(stage: Stage.enrollment,
    enrollment: EnrollmentForm(token: 'preview', title: 'Nouvelle inscription', subtitle: 'Année scolaire 2026-2027',
      fields: [
        EnrollmentField(key: 'nom', label: 'Nom', kind: 'Text', required: true),
        EnrollmentField(key: 'postnom', label: 'Post-nom', kind: 'Text', required: true),
        EnrollmentField(key: 'prenom', label: 'Prénom', kind: 'Text', required: true),
        EnrollmentField(key: 'sexe', label: 'Sexe', kind: 'Choice', value: 'F',
          options: [FilterChoice('F', 'Féminin'), FilterChoice('M', 'Masculin')]),
        EnrollmentField(key: 'promotion', label: 'Promotion', kind: 'Choice', value: 'INF1',
          options: [FilterChoice('INF1', 'Première infirmière')]),
        EnrollmentField(key: 'telephone', label: 'Téléphone du parent', kind: 'Phone'),
      ])),
  '05-encaissement': SchoolSession(stage: Stage.cashier,
    cashier: CashierScreen(student: 'Apprenant exemple A', info: 'INF1 · EX001',
      balance: '105 000 FC', overdue: '35 000 FC', hasStudent: true, canPay: true,
      amount: '35000', method: 'Especes', total: '35 000 FC',
      methods: [FilterChoice('Especes', 'Espèces')],
      fees: [CashierFee('f1', 'Octobre', '35 000 FC', '05/10/2026', true, true),
        CashierFee('f2', 'Novembre', '35 000 FC', '05/11/2026', false, false)])),
  '06-paiements': SchoolSession(stage: Stage.monthly,
    monthly: MonthlyListing(title: 'Suivi mensuel', subtitle: 'Octobre · INF1',
      period: 'oct', promotion: 'inf1', status: 'all', search: '', page: 1,
      count: 2, hasPrevious: false, hasNext: false, periods: [FilterChoice('oct', 'Octobre')],
      promotions: [FilterChoice('inf1', 'INF1')], states: [FilterChoice('all', 'Tous les états')],
      rows: [PaymentEntry(name: 'Apprenant exemple A', promotion: 'INF1', expected: '35 000 FC',
        paid: '35 000 FC', remaining: '0 FC', status: 'En ordre'),
        PaymentEntry(name: 'Apprenant exemple B', promotion: 'INF1', expected: '35 000 FC',
        paid: '20 000 FC', remaining: '15 000 FC', status: 'Litige')],
      exportScope: 'Export : Octobre · INF1')),
  '07-tableau-de-bord': SchoolSession(stage: Stage.schoolRegister, modules: modules,
    register: SchoolRegister(kind: 'dashboard', title: 'Tableau de bord', subtitle: 'Données de démonstration',
      overview: 'Garçons 48 · Filles 72', rows: [
        RegisterRow('', 'Apprenants actifs', '120', 'Indicateurs', '110 payants', []),
        RegisterRow('', 'Pris en charge', '10', 'Indicateurs', 'Prise en charge complète', []),
        RegisterRow('', 'Recettes de l’année', '8 450 000 FC', 'Indicateurs', 'Situation annuelle', []),
        RegisterRow('', 'Taux de recouvrement', '78 %', 'Indicateurs', 'Depuis la rentrée', []),
        RegisterRow('', 'Encaissé', '350 000 FC', 'Aujourd’hui', '10 reçus', []),
        RegisterRow('', 'Octobre', '3,5 M', 'Encaissements', '', [], share: .7, track: 1),
      ])),
  '08-frais-lmd': SchoolSession(stage: Stage.schoolRegister,
    register: SchoolRegister(kind: 'lmd', title: 'Organisation LMD', subtitle: 'Année académique 2026-2027',
      promotion: 'l1', canWrite: true, token: 'preview', overview: '450 USD · 3 tranche(s)',
      promotions: [FilterChoice('l1', 'Licence 1 – Informatique')],
      rows: [
        RegisterRow('', 'Tranche 1', '', 'Tranche', '', [ProfileValue('Montant', '150 USD'), ProfileValue('Échéance', '05/12/2026')]),
        RegisterRow('', 'Tranche 2', '', 'Tranche', '', [ProfileValue('Montant', '150 USD'), ProfileValue('Échéance', '05/03/2027')]),
        RegisterRow('', 'Tranche 3', '', 'Tranche', '', [ProfileValue('Montant', '150 USD'), ProfileValue('Échéance', '05/06/2027')]),
      ])),
  '09-utilisateurs': SchoolSession(stage: Stage.schoolRegister,
    register: SchoolRegister(kind: 'users', title: 'Utilisateurs', subtitle: '3 comptes actifs',
      canWrite: true, token: 'preview', rows: [
        RegisterRow('u1', 'Secrétariat exemple', 'secretariat', 'Actif', '', [ProfileValue('Rôle', 'Secrétariat')]),
        RegisterRow('u2', 'Comptabilité exemple', 'comptabilite', 'Actif', '', [ProfileValue('Rôle', 'Comptabilité')]),
        RegisterRow('u3', 'Enseignant exemple', 'enseignant', 'Actif', '', [ProfileValue('Rôle', 'Enseignant')]),
      ])),
};

void main() {
  final enabled = Platform.environment['SCOLIA_CAPTURE_PREVIEWS'] == '1';
  setUpAll(() async {
    if (!enabled) return;
    final root = Platform.environment['FLUTTER_ROOT'];
    if (root == null) throw StateError('FLUTTER_ROOT is required for real preview fonts');
    final fonts = Directory('$root/bin/cache/artifacts/material_fonts');
    final roboto = FontLoader('Roboto');
    final files = fonts.listSync().whereType<File>().where((f) =>
      RegExp(r'Roboto-(Regular|Medium|Bold|Light)\.ttf$').hasMatch(f.path)).toList();
    if (files.isEmpty) throw StateError('Roboto font unavailable');
    for (final f in files) {
      roboto.addFont(Future.value(ByteData.sublistView(f.readAsBytesSync())));
    }
    await roboto.load();
    final icon = File(fonts.path + '/MaterialIcons-Regular.otf');
    if (!icon.existsSync()) throw StateError('Material Icons unavailable');
    final icons = FontLoader('MaterialIcons')
      ..addFont(Future.value(ByteData.sublistView(icon.readAsBytesSync())));
    await icons.load();
  });
  for (final entry in previews.entries) {
    for (final dark in [false, true]) {
      final name = entry.key + (dark ? '-sombre' : '-clair');
      testWidgets('capture $name', (tester) async {
        tester.view.physicalSize = const Size(824, 1800);
        tester.view.devicePixelRatio = 2;
        tester.platformDispatcher.platformBrightnessTestValue = dark ? Brightness.dark : Brightness.light;
        addTearDown(tester.view.resetPhysicalSize);
        addTearDown(tester.view.resetDevicePixelRatio);
        addTearDown(tester.platformDispatcher.clearPlatformBrightnessTestValue);
        const boundary = ValueKey('preview-boundary');
        await tester.pumpWidget(RepaintBoundary(key: boundary,
          child: ScoliaApp(host: PreviewHost(entry.value))));
        await tester.pumpAndSettle();
        expect(tester.takeException(), isNull);
        await expectLater(find.byKey(boundary), matchesGoldenFile('captures/$name.png'));
      }, skip: !enabled);
    }
  }
}
