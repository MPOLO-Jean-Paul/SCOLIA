import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter/services.dart';
import 'core/host.dart';

void main() => runApp(const ScoliaApp(host: NativeHost()));

class ScoliaApp extends StatelessWidget {
  const ScoliaApp({super.key, required this.host});
  final ScoliaHost host;
  ThemeData theme(Brightness brightness) {
    // Valeurs de Themes/Material3.axaml : continuité visuelle avec SCOLIA Android.
    final dark = brightness == Brightness.dark;
    final scheme = ColorScheme.fromSeed(seedColor: const Color(0xff0061a4),
      brightness: brightness).copyWith(
        primary: dark ? const Color(0xff9ecaff) : const Color(0xff0061a4),
        onPrimary: dark ? const Color(0xff003258) : Colors.white,
        primaryContainer: dark ? const Color(0xff00497d) : const Color(0xffd1e4ff),
        surface: dark ? const Color(0xff1e2023) : Colors.white,
        surfaceContainerLow: dark ? const Color(0xff1e2023) : Colors.white,
        surfaceContainer: dark ? const Color(0xff282a2d) : const Color(0xffe6ebf2),
        onSurface: dark ? const Color(0xffe2e2e6) : const Color(0xff1a1c1e),
        onSurfaceVariant: dark ? const Color(0xffc3c7cf) : const Color(0xff43474e),
        outline: dark ? const Color(0xff8d9199) : const Color(0xff73777f),
        outlineVariant: dark ? const Color(0xff33363b) : const Color(0xffdde3ea));
    final base = ThemeData(useMaterial3: true, colorScheme: scheme);
    final background = dark ? const Color(0xff111316) : const Color(0xfff3f6fb);
    final shape = RoundedRectangleBorder(borderRadius: BorderRadius.circular(8));
    return base.copyWith(
      scaffoldBackgroundColor: background,
      appBarTheme: AppBarTheme(backgroundColor: background, elevation: 0,
        centerTitle: false, scrolledUnderElevation: 0),
      cardTheme: CardThemeData(color: scheme.surface, elevation: 0,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12),
          side: BorderSide(color: scheme.outlineVariant))),
      inputDecorationTheme: InputDecorationTheme(filled: true,
        fillColor: scheme.surface,
        border: OutlineInputBorder(borderRadius: BorderRadius.circular(8)),
        enabledBorder: OutlineInputBorder(borderRadius: BorderRadius.circular(8),
          borderSide: BorderSide(color: scheme.outline)),
        contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 16)),
      filledButtonTheme: FilledButtonThemeData(style: FilledButton.styleFrom(
        minimumSize: const Size(48, 52), shape: shape)),
      outlinedButtonTheme: OutlinedButtonThemeData(style: OutlinedButton.styleFrom(
        minimumSize: const Size(48, 52), shape: shape)),
    );
  }

  @override
  Widget build(BuildContext context) => MaterialApp(
    debugShowCheckedModeBanner: false, title: 'SCOLIA Pro',
    locale: const Locale('fr'), supportedLocales: const [Locale('fr')],
    localizationsDelegates: GlobalMaterialLocalizations.delegates,
    theme: theme(Brightness.light), darkTheme: theme(Brightness.dark),
    home: Workspace(host: host),
  );
}

class Workspace extends StatefulWidget {
  const Workspace({super.key, required this.host});
  final ScoliaHost host;
  @override
  State<Workspace> createState() => _WorkspaceState();
}

class _WorkspaceState extends State<Workspace> {
  SchoolSession? session;
  bool busy = true, hidePassword = true, recovery = false;
  String? error;
  Timer? openingPoll;
  final studentSearch = TextEditingController();
  final paymentSearch = TextEditingController();
  final username = TextEditingController();
  final password = TextEditingController();
  final recoveryCode = TextEditingController();
  final confirm = TextEditingController();
  final form = GlobalKey<FormState>();
  final enrollmentValues = <String, String>{};
  final journalSearch = TextEditingController();
  final marksCode = TextEditingController();
  final marksScore = TextEditingController();
  final cashierQuery = TextEditingController();
  final cashierAmount = TextEditingController();
  final cashierReference = TextEditingController();
  final cashierNote = TextEditingController();
  final cashierFees = <String>{};
  String cashierMethod = '';
  bool cashierDirty = true;

  @override
  void initState() { super.initState(); execute(Operation.snapshot); }

  @override
  void dispose() {
    openingPoll?.cancel();
    marksCode.dispose(); marksScore.dispose();
    journalSearch.dispose(); cashierQuery.dispose(); cashierAmount.dispose(); cashierReference.dispose(); cashierNote.dispose();
    studentSearch.dispose(); paymentSearch.dispose(); username.dispose(); password.dispose(); recoveryCode.dispose(); confirm.dispose();
    super.dispose();
  }

  Future<void> execute(Operation operation,
      [Map<String, String> args = const {}]) async {
    openingPoll?.cancel();
    setState(() { busy = true; error = null; });
    try {
      final result = await widget.host.execute(operation, args);
      if (!mounted) return;
      setState(() {
        session = result;
        marksCode.text = result.register?.marks?.code ?? '';
        marksScore.text = result.register?.marks?.score ?? '';
        journalSearch.text = result.journal?.search ?? '';
        if (result.cashier case final c?) {
          cashierFees..clear()..addAll(c.fees.where((x) => x.selected).map((x) => x.id));
          cashierAmount.text = c.amount;
          cashierReference.text = c.reference;
          cashierNote.text = c.note;
          cashierMethod = c.method;
          cashierDirty = false;
        } else {
          cashierFees.clear(); cashierAmount.clear(); cashierReference.clear(); cashierNote.clear(); cashierQuery.clear();
          cashierDirty = true;
        }
        enrollmentValues.clear();
        if (result.enrollment != null) {
          for (final field in result.enrollment!.fields) {
            if (field.kind != 'Header') enrollmentValues[field.key] = field.value;
          }
        }
        if (result.stage == Stage.students && result.students != null) {
          studentPage = result.students!.page;
          studentQuery = result.students!.search;
          studentSearch.text = studentQuery;
        } else if (result.stage != Stage.studentProfile) studentSearch.clear();
        if (result.stage != Stage.monthly) paymentSearch.clear();
        else if (result.monthly != null) paymentSearch.text = result.monthly!.search;
        if (result.stage != Stage.login || (operation == Operation.recover && result.notice.isEmpty)) {
          recovery = false;
        }
      });
    } on MissingPluginException {
      if (mounted) setState(() { session = null; error =
        'Le moteur SCOLIA natif n’est pas raccordé à ce module.'; });
    } on TimeoutException {
      if (mounted) setState(() { session = null; error = 'Le moteur SCOLIA ne répond pas. Réessayez.'; });
    } on PlatformException {
      if (mounted) setState(() { session = null; error =
        'Opération refusée ou indisponible. Vérifiez votre configuration.'; });
    } on Exception {
      if (mounted) setState(() { session = null; error = 'Impossible de vérifier la réponse SCOLIA.'; });
    } finally {
      if (mounted) {
        password.clear(); recoveryCode.clear(); confirm.clear();
        if (session?.stage != Stage.enrollment) enrollmentValues.clear();
        if (session?.stage != Stage.cashier) {
          cashierFees.clear(); cashierAmount.clear(); cashierReference.clear(); cashierNote.clear(); cashierQuery.clear();
        }
        setState(() => busy = false);
        if (session?.stage == Stage.loading && error == null) {
          openingPoll = Timer(const Duration(milliseconds: 750), () {
            if (mounted && !busy) unawaited(execute(Operation.snapshot));
          });
        }
      }
    }
  }

  void submit() {
    if (!(form.currentState?.validate() ?? false)) return;
    execute(recovery ? Operation.recover : Operation.signIn, {
      'userName': username.text.trim(), 'password': password.text,
      if (recovery) 'recoveryCode': recoveryCode.text,
      if (recovery) 'confirmation': confirm.text,
    });
  }

  Widget action(String label, IconData icon, Operation operation,
      {bool primary = false, Map<String, String> args = const {}}) {
    final VoidCallback? callback = busy ? null : () => execute(operation, args);
    return primary ? FilledButton.icon(onPressed: callback, icon: Icon(icon),
      label: Text(label, textAlign: TextAlign.center))
      : OutlinedButton.icon(onPressed: callback, icon: Icon(icon),
      label: Text(label, textAlign: TextAlign.center));
  }

  Widget heading(String title, String subtitle) => Column(
    crossAxisAlignment: CrossAxisAlignment.start, children: [
      Text(title, style: Theme.of(context).textTheme.headlineSmall?.copyWith(
        fontWeight: FontWeight.w600)),
      const SizedBox(height: 8),
      Text(subtitle, style: TextStyle(color: Theme.of(context).colorScheme.onSurfaceVariant)),
      const SizedBox(height: 24),
    ],
  );

  Widget pairing() => Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
    heading('Bienvenue sur SCOLIA', 'Reliez cet appareil à votre établissement.'),
    action('Scanner le code QR', Icons.qr_code_scanner_rounded, Operation.scanQr, primary: true),
    const SizedBox(height: 12),
    action('Rechercher ou configurer le serveur', Icons.lan_outlined, Operation.chooseServer),
    const SizedBox(height: 12),
    action('Créer un établissement', Icons.school_outlined, Operation.createSchool),
  ]);

  Widget login(SchoolSession s) => Form(key: form, child: Column(
    crossAxisAlignment: CrossAxisAlignment.stretch, children: [
      heading(recovery ? 'Récupérer mon compte' : 'Connexion', s.school),
      TextFormField(controller: username, enabled: !busy,
        autofillHints: const [AutofillHints.username], textInputAction: TextInputAction.next,
        decoration: const InputDecoration(labelText: 'Identifiant', prefixIcon: Icon(Icons.person_outline)),
        validator: (v) => v == null || v.trim().isEmpty ? 'Identifiant requis' : null),
      const SizedBox(height: 14),
      if (recovery) ...[
        TextFormField(controller: recoveryCode, enabled: !busy, obscureText: true,
          decoration: const InputDecoration(labelText: 'Code de récupération', prefixIcon: Icon(Icons.key_outlined)),
          validator: (v) => v == null || v.isEmpty ? 'Code requis' : null),
        const SizedBox(height: 14),
      ],
      TextFormField(controller: password, enabled: !busy, obscureText: hidePassword,
        autofillHints: recovery ? const [AutofillHints.newPassword] : const [AutofillHints.password],
        decoration: InputDecoration(labelText: recovery ? 'Nouveau mot de passe' : 'Mot de passe',
          prefixIcon: const Icon(Icons.lock_outline),
          suffixIcon: IconButton(tooltip: hidePassword ? 'Afficher' : 'Masquer',
            onPressed: () => setState(() => hidePassword = !hidePassword),
            icon: Icon(hidePassword ? Icons.visibility_outlined : Icons.visibility_off_outlined))),
        validator: (v) => v == null || v.isEmpty ? 'Mot de passe requis'
          : recovery && v.length < 8 ? '8 caractères minimum' : null),
      if (recovery) ...[
        const SizedBox(height: 14),
        TextFormField(controller: confirm, enabled: !busy, obscureText: hidePassword,
          decoration: const InputDecoration(labelText: 'Confirmer le mot de passe'),
          validator: (v) => v != password.text ? 'Les mots de passe diffèrent' : null),
      ],
      const SizedBox(height: 20),
      FilledButton(onPressed: busy ? null : submit,
        child: Text(recovery ? 'Réinitialiser le mot de passe' : 'Se connecter')),
      const SizedBox(height: 8),
      TextButton(onPressed: busy ? null : () => setState(() {
        recovery = !recovery; password.clear(); recoveryCode.clear(); confirm.clear();
      }), child: Text(recovery ? 'Retour à la connexion' : 'Mot de passe oublié ?')),
      if (!recovery)
        action('Utiliser mon empreinte', Icons.fingerprint, Operation.biometric),
    ],
  ));

  static const moduleLabels = <String, (String, IconData)>{
    'students': ('Dossiers scolaires', Icons.groups_outlined),
    'enroll': ('Inscrire', Icons.person_add_alt_1_outlined),
    'cashier': ('Encaisser', Icons.payments_outlined),
    'monthly': ('Suivi des paiements', Icons.calendar_month_outlined),
    'reports': ('Rapports et exports', Icons.description_outlined),
    'attendance': ('Présences', Icons.fact_check_outlined),
    'grades': ('Cotes et résultats', Icons.school_outlined),
    'settings': ('Configuration', Icons.settings_outlined),
    'network': ('Poste et réseau', Icons.sync_outlined),
    'receipts': ('Journal des reçus', Icons.receipt_long_outlined),
    'dashboard': ('Tableau de bord', Icons.insights_outlined),
    'results': ('Mes résultats', Icons.school_outlined),
    'lmd': ('Organisation LMD', Icons.account_tree_outlined),
  };

  List<ModuleEntry> catalog(SchoolSession s) => s.catalog.isNotEmpty
    ? s.catalog.where((x) => s.modules.contains(x.id)).toList()
    : moduleLabels.entries.where((x) => s.modules.contains(x.key))
        .map((x) => ModuleEntry(x.key, x.value.$1, 'Espace de travail')).toList();

  void openModule(String id) {
    if (id == 'students') { unawaited(listStudents(1, '')); }
    else if (id == 'monthly') { unawaited(listMonthly()); }
    else if (id == 'receipts') { unawaited(listReceipts()); }
    else if (id == 'attendance') { unawaited(loadRegister('attendance')); }
    else if (id == 'page:DisciplinePage') { unawaited(loadRegister('discipline')); }
    else if (id == 'page:StaffPage') { unawaited(loadRegister('staff')); }
    else if (id == 'page:StaffAttendancePage') { unawaited(loadRegister('pointage')); }
    else if (id == 'page:TeachingPage') { unawaited(loadRegister('teaching')); }
    else if (id == 'page:AnonymousCodesPage') { unawaited(loadRegister('codes')); }
    else if (id == 'grades') { unawaited(loadRegister('marks')); }
    else if (id == 'page:DeliberationPage') { unawaited(loadRegister('deliberation')); }
    else if (id == 'page:EncodingProgressPage') { unawaited(loadRegister('progress')); }
    else if (id == 'lmd') { unawaited(loadRegister('lmd')); }
    else if (id == 'settings') { unawaited(loadRegister('settings')); }
    else if (id == 'page:UsersPage') { unawaited(loadRegister('users')); }
    else if (id == 'dashboard') { unawaited(loadRegister('dashboard')); }
    else if (id == 'page:PromoterDashboardPage') { unawaited(loadRegister('promoter')); }
    else { unawaited(execute(Operation.openModule, {'module': id})); }
  }

  Widget welcome(SchoolSession s) {
    final h = s.header;
    final c = Theme.of(context).colorScheme;
    return Card.outlined(margin: EdgeInsets.zero, child: Padding(
      padding: const EdgeInsets.all(12), child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text('ESPACE DE TRAVAIL', style: TextStyle(fontSize: 11, fontWeight: FontWeight.w600, color: c.primary)),
            const SizedBox(height: 4),
            Text(h?.greeting ?? 'Bienvenue, ${s.user}', style: Theme.of(context).textTheme.headlineSmall),
            Text([s.school, s.year].where((v) => v.isNotEmpty).join(' · ')),
            if (h != null) Text(h.date, style: TextStyle(color: c.onSurfaceVariant)),
          ])),
          if (h != null) CircleAvatar(backgroundColor: c.primaryContainer, child: Text(h.initials)),
        ]),
        if (h != null) ...[
          const Divider(),
          ListTile(contentPadding: EdgeInsets.zero,
            leading: Icon(h.offline ? Icons.cloud_off_outlined : h.pending ? Icons.sync : Icons.cloud_done_outlined,
              color: h.offline || h.pending ? c.onSurfaceVariant : c.primary),
            title: Text(h.syncStatus), subtitle: Text(h.syncHint),
            trailing: s.modules.contains('network') ? const Icon(Icons.chevron_right) : null,
            onTap: busy || !s.modules.contains('network') ? null : () => openModule('network')),
        ],
      ])));
  }

  Widget home(SchoolSession s) => Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
    welcome(s),
    const SizedBox(height: 16),
    Text('Démarrer', style: Theme.of(context).textTheme.titleMedium),
    const SizedBox(height: 12),
    LayoutBuilder(builder: (context, constraints) {
      final width = constraints.maxWidth;
      final columns = width >= 680 ? 3 : width >= 330 ? 2 : 1;
      final all = catalog(s);
      final quick = ['enroll', 'cashier', 'students', 'monthly'];
      final allowed = [
        for (final id in quick)
          for (final item in all.where((x) => x.id == id))
            ModuleEntry(item.id, item.label, 'Actions rapides'),
        ...all.where((x) => !quick.contains(x.id)),
      ];
      if (allowed.isEmpty) return const Text('Aucune fonctionnalité autorisée pour ce compte.');
      return Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        for (final group in allowed.map((x) => x.group).toSet()) ...[
          Padding(padding: const EdgeInsets.symmetric(vertical: 12),
            child: Text(group, style: Theme.of(context).textTheme.titleSmall)),
          Wrap(spacing: 12, runSpacing: 12, children: [
            for (final entry in allowed.where((x) => x.group == group))
              SizedBox(width: (width - 12 * (columns - 1)) / columns,
                child: ModuleTile(label: entry.label,
                  icon: moduleLabels[entry.id]?.$2 ?? Icons.apps_outlined,
                  onTap: busy ? null : () => openModule(entry.id))),
          ]),
        ],
      ]);
    }),
    const SizedBox(height: 24),
    action('Se déconnecter', Icons.logout, Operation.signOut),
  ]);

  int studentPage = 1;
  String studentQuery = '';
  Future<void> listStudents(int page, String query) {
    studentPage = page; studentQuery = query.trim();
    return execute(Operation.listStudents, {'page': page.toString(), 'q': studentQuery});
  }

  Widget students(StudentListing listing) => Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
    Align(alignment: Alignment.centerLeft, child: TextButton.icon(
      onPressed: busy ? null : () => execute(Operation.snapshot),
      icon: const Icon(Icons.arrow_back), label: const Text('Retour à l’accueil'))),
    heading(listing.title, listing.subtitle),
    if (session?.modules.contains('enroll') ?? false) ...[
      action('Nouvelle inscription', Icons.person_add_alt_1, Operation.openModule,
        primary: true, args: {'module': 'enroll'}),
      const SizedBox(height: 12),
    ],
    TextField(controller: studentSearch, enabled: !busy, maxLength: 100,
      textInputAction: TextInputAction.search,
      onSubmitted: (q) => listStudents(1, q),
      decoration: InputDecoration(labelText: 'Rechercher un dossier',
        prefixIcon: const Icon(Icons.search), counterText: '',
        suffixIcon: IconButton(tooltip: 'Rechercher', onPressed: busy ? null :
          () => listStudents(1, studentSearch.text), icon: const Icon(Icons.arrow_forward)))),
    const SizedBox(height: 16),
    if (listing.rows.isEmpty)
      const Padding(padding: EdgeInsets.all(16), child: Text('Aucun dossier pour cette recherche.')),
    for (final row in listing.rows)
      Card.outlined(margin: const EdgeInsets.only(bottom: 8), child: Padding(
        padding: const EdgeInsets.all(12), child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
          const CircleAvatar(child: Icon(Icons.person_outline)),
          const SizedBox(width: 12),
          Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text(row.name, style: Theme.of(context).textTheme.titleSmall),
            const SizedBox(height: 5),
            Text([row.code, row.promotion, row.status].where((v) => v.isNotEmpty).join(' · ')),
            if (row.id.isNotEmpty) Wrap(spacing: 8, children: [
              TextButton(onPressed: busy ? null : () => execute(Operation.openStudent, {'student': row.id}),
                child: const Text('Fiche')),
              if (session?.modules.contains('cashier') ?? false)
                TextButton(onPressed: busy ? null : () => execute(Operation.cashierStudent, {'student': row.id}),
                  child: const Text('Encaisser')),
            ]),
          ])),
        ]))),
    const SizedBox(height: 12),
    Row(children: [
      IconButton(tooltip: 'Page précédente', onPressed: busy || !listing.hasPrevious ? null :
        () => listStudents(listing.page - 1, listing.search), icon: const Icon(Icons.chevron_left)),
      Expanded(child: Text('Page ${listing.page}', textAlign: TextAlign.center)),
      IconButton(tooltip: 'Page suivante', onPressed: busy || !listing.hasNext ? null :
        () => listStudents(listing.page + 1, listing.search), icon: const Icon(Icons.chevron_right)),
    ]),
  ]);

  Future<void> loadRegister(String kind, {SchoolRegister? current, String? promotion, String? date,
    String? measure, bool? summary}) => execute(kind == 'attendance' ? Operation.listAttendance : kind == 'discipline' ? Operation.listDiscipline : Operation.listPersonnel,
      {'kind': kind, 'promotion': promotion ?? current?.promotion ?? '', 'date': date ?? current?.date ?? '',
        'measure': measure ?? current?.measure ?? '', 'summary': (summary ?? current?.summary ?? false).toString()});

  Future<void> leaveRegister(SchoolRegister r) async {
    if (r.dirty) {
      final discard = await showDialog<bool>(context: context, builder: (context) => AlertDialog(
        title: const Text('Abandonner les modifications ?'),
        content: const Text('Les changements non enregistrés seront perdus.'),
        actions: [TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Rester')),
          TextButton(onPressed: () => Navigator.pop(context, true), child: const Text('Abandonner'))]));
      if (discard != true || !mounted) return;
    }
    await execute(Operation.snapshot);
  }

  Future<void> editAttendanceNote(SchoolRegister r, RegisterRow row) async {
    String note = row.note;
    final result = await showDialog<String>(context: context, builder: (context) => AlertDialog(
      title: Text(row.name), content: TextFormField(initialValue: row.note, maxLength: 500,
        maxLines: 3, decoration: const InputDecoration(labelText: 'Remarque'), onChanged: (v) => note = v),
      actions: [TextButton(onPressed: () => Navigator.pop(context), child: const Text('Annuler')),
        TextButton(onPressed: () => Navigator.pop(context, note), child: const Text('Appliquer'))]));
    if (!mounted || result == null || session?.register?.token != r.token) return;
    await execute(Operation.editAttendance, {'token': r.token, 'row': row.id, 'status': row.status, 'note': result});
  }

  Widget personnelRegister(SchoolRegister r) {
    final staff = r.kind == 'staff';
    final pointage = r.kind == 'pointage';
    Widget rows(Iterable<RegisterRow> values) => Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
      for (final row in values) Card.outlined(margin: const EdgeInsets.only(bottom: 8),
        child: Padding(padding: const EdgeInsets.all(12), child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          Text(row.name, style: Theme.of(context).textTheme.titleSmall),
          if (row.code.isNotEmpty || row.status.isNotEmpty) Text([row.code, row.status].where((x) => x.isNotEmpty).join(' · ')),
          for (final value in row.details) Text('${value.label} : ${value.value}'),
          if (row.id.isNotEmpty) Align(alignment: Alignment.centerRight, child: TextButton.icon(
            onPressed: busy ? null : () => execute(Operation.registerNative, {'token': r.token, 'row': row.id}),
            icon: const Icon(Icons.more_horiz), label: const Text('Actions'))),
        ]))),
    ]);
    return Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
      Align(alignment: Alignment.centerLeft, child: TextButton.icon(onPressed: busy ? null : () => leaveRegister(r),
        icon: const Icon(Icons.arrow_back), label: const Text('Retour à l’accueil'))),
      heading(r.title, r.subtitle),
      SegmentedButton<bool>(segments: [
        ButtonSegment(value: false, label: Text(staff ? 'Personnel' : pointage ? 'Mois en cours' : 'Cours actifs')),
        ButtonSegment(value: true, label: Text(staff ? 'Paie' : pointage ? 'Mois précédent' : 'Avec archives')),
      ], selected: {r.summary}, onSelectionChanged: busy ? null : (v) => loadRegister(r.kind, current: r, summary: v.first)),
      const SizedBox(height: 12),
      if (staff && r.summary && r.options.isNotEmpty)
        filter('Mois de paie', r.date, r.options, (v) => loadRegister(r.kind, current: r, date: v)),
      if (!staff && !pointage && r.promotions.isNotEmpty)
        filter('Promotion', r.promotion, r.promotions, (v) => loadRegister(r.kind, current: r, promotion: v)),
      const SizedBox(height: 12),
      action(staff ? 'Gestion du personnel et paie' : pointage ? 'Signatures et justifications' : 'Attributions et historique',
        Icons.more_horiz, Operation.registerNative, args: {'token': r.token}),
      const SizedBox(height: 12),
      if (r.rows.isEmpty) const Text('Aucun élément dans la sélection.'),
      if (pointage) Text('Pointage du jour', style: Theme.of(context).textTheme.titleMedium),
      rows(r.rows.where((x) => x.id.isNotEmpty)),
      if (!staff) ...[
        const SizedBox(height: 12),
        Text(pointage ? 'Bilan de la période' : 'Charges pédagogiques', style: Theme.of(context).textTheme.titleMedium),
        rows(r.rows.where((x) => x.id.isEmpty)),
      ],
    ]);
  }

  Widget codesRegister(SchoolRegister r) => Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
    Align(alignment: Alignment.centerLeft, child: TextButton.icon(onPressed: busy ? null : () => leaveRegister(r),
      icon: const Icon(Icons.arrow_back), label: const Text('Retour à l’accueil'))),
    heading(r.title, r.subtitle),
    if (r.promotions.isNotEmpty)
      filter('Promotion', r.promotion, r.promotions, (v) => loadRegister(r.kind, current: r, promotion: v)),
    const SizedBox(height: 12),
    action('Attribution, import et export des codes', Icons.more_horiz, Operation.registerNative, args: {'token': r.token}),
    const SizedBox(height: 12),
    if (r.rows.isEmpty) const Text('Aucun élément dans la sélection.'),
    for (final row in r.rows) Card.outlined(margin: const EdgeInsets.only(bottom: 8), child: Padding(
      padding: const EdgeInsets.all(12), child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        Text(row.name, style: Theme.of(context).textTheme.titleSmall),
        for (final value in row.details) Text('${value.label} : ${value.value}'),
        if (row.code.isNotEmpty) SelectableText(row.code, style: Theme.of(context).textTheme.titleLarge)
        else const Text('Code non attribué'),
        Text(row.status),
        Align(alignment: Alignment.centerRight, child: TextButton.icon(
          onPressed: busy ? null : () => execute(Operation.registerNative, {'token': r.token, 'row': row.id}),
          icon: const Icon(Icons.more_horiz), label: const Text('Actions'))),
      ]))),
  ]);

  Widget marksRegister(SchoolRegister r) {
    final mark = r.marks!;
    return Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
      Align(alignment: Alignment.centerLeft, child: TextButton.icon(onPressed: busy ? null : () => leaveRegister(r),
        icon: const Icon(Icons.arrow_back), label: const Text('Retour à l’accueil'))),
      heading(r.title, r.subtitle),
      if (r.promotions.isNotEmpty) filter('Promotion', r.promotion, r.promotions,
        (v) => loadRegister(r.kind, current: r, promotion: v, date: '', measure: '')),
      if (r.options.isNotEmpty) ...[
        const SizedBox(height: 12),
        filter('Cours', r.date, [const FilterChoice('', 'Choisir un cours'), ...r.options],
          (v) => loadRegister(r.kind, current: r, date: v, measure: '')),
      ],
      if (mark.kinds.isNotEmpty) ...[
        const SizedBox(height: 12),
        filter('Période de cotation', r.measure, mark.kinds,
          (v) => loadRegister(r.kind, current: r, measure: v)),
      ],
      const SizedBox(height: 12),
      if (mark.rules.isNotEmpty) Text(mark.rules),
      if (mark.announcement.isNotEmpty) Text(mark.announcement),
      const SizedBox(height: 12),
      if (r.date.isNotEmpty) ...[
        Text(mark.sheet, style: Theme.of(context).textTheme.titleMedium),
        Text(mark.hint),
        const SizedBox(height: 12),
        TextField(controller: marksCode, enabled: !busy, textCapitalization: TextCapitalization.characters,
          maxLength: 64, decoration: const InputDecoration(labelText: 'Code anonyme', prefixIcon: Icon(Icons.search))),
        ValueListenableBuilder<TextEditingValue>(valueListenable: marksCode, builder: (context, value, _) {
          final query = value.text.trim().toUpperCase();
          final validated = query.isNotEmpty && query == mark.validatedCode.toUpperCase();
          final suggestions = query.isEmpty || validated ? <RegisterRow>[] :
            r.rows.where((x) => x.code.toUpperCase().startsWith(query)).take(15).toList();
          return Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
            if (suggestions.isNotEmpty) Wrap(spacing: 8, children: [
              for (final row in suggestions) ActionChip(label: Text(row.code), onPressed: busy ? null : () {
                marksCode.text = row.code;
                execute(Operation.marksCheck, {'token': r.token, 'code': row.code});
              }),
            ]),
            OutlinedButton.icon(onPressed: busy || query.isEmpty ? null : () => execute(Operation.marksCheck,
              {'token': r.token, 'code': query}), icon: const Icon(Icons.verified_outlined), label: const Text('Vérifier le code')),
            if (mark.feedback.isNotEmpty) Padding(padding: const EdgeInsets.symmetric(vertical: 8), child: Text(mark.feedback)),
            if (validated && r.canWrite) ...[
              TextField(controller: marksScore, enabled: !busy,
                keyboardType: const TextInputType.numberWithOptions(decimal: true), maxLength: 32,
                decoration: InputDecoration(labelText: mark.maximum, prefixIcon: const Icon(Icons.edit_outlined))),
              ValueListenableBuilder<TextEditingValue>(valueListenable: marksScore, builder: (context, score, _) =>
                FilledButton.icon(onPressed: busy || score.text.trim().isEmpty ? null : () => execute(Operation.marksSave,
                  {'token': r.token, 'code': mark.validatedCode, 'score': score.text.trim()}),
                  icon: const Icon(Icons.save_outlined), label: const Text('Enregistrer la cote'))),
            ],
            if (!r.canWrite) const Text('Feuille non modifiable.'),
          ]);
        }),
      ],
      const SizedBox(height: 12),
      action('Feuille complète et autres actions', Icons.more_horiz, Operation.registerNative, args: {'token': r.token}),
    ]);
  }

  Widget academicRegister(SchoolRegister r) {
    final board = r.kind == 'deliberation';
    Widget document(String label, String id) => action(label, Icons.download_outlined,
      Operation.academicExport, args: {'token': r.token, 'document': id});
    return Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
      Align(alignment: Alignment.centerLeft, child: TextButton.icon(onPressed: busy ? null : () => leaveRegister(r),
        icon: const Icon(Icons.arrow_back), label: const Text('Retour à l’accueil'))),
      heading(r.title, r.subtitle),
      if (r.promotions.isNotEmpty)
        filter('Promotion', r.promotion, r.promotions, (v) => loadRegister(r.kind, current: r, promotion: v)),
      const SizedBox(height: 12),
      if (r.options.isNotEmpty) filter('Période', r.date, r.options,
        (v) => loadRegister(r.kind, current: r, date: v)),
      const SizedBox(height: 8),
      SwitchListTile(contentPadding: EdgeInsets.zero, title: Text(board ? 'Cas à examiner uniquement' : 'Feuilles à relancer uniquement'),
        value: r.summary, onChanged: busy ? null : (v) => loadRegister(r.kind, current: r, summary: v)),
      if (r.overview.isNotEmpty || r.context.isNotEmpty) Card.outlined(margin: EdgeInsets.zero,
        child: Padding(padding: const EdgeInsets.all(12), child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          Text('Synthèse', style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 8),
          if (r.overview.isNotEmpty) Text(r.overview),
          if (r.context.isNotEmpty) ...[const SizedBox(height: 8), Text(r.context)],
        ]))),
      const SizedBox(height: 12),
      if (!board || r.promotion.isNotEmpty) Wrap(spacing: 8, runSpacing: 8, children: [
        if (board) ...[
          document('Procès-verbal', 'pv'), document('Grille PDF', 'grille-pdf'),
          document('Grille Excel', 'grille-xlsx'), document('Palmarès', 'palmares'),
          document('Tous les bulletins', 'bulletins'),
        ] else ...[document('Suivi PDF', 'pdf'), document('Suivi Excel', 'xlsx')],
      ]),
      if (board) ...[
        const SizedBox(height: 12),
        action('Jury et autres actions', Icons.more_horiz, Operation.registerNative, args: {'token': r.token}),
      ],
      const SizedBox(height: 12),
      if (r.rows.isEmpty) const Text('Aucun élément dans la sélection.'),
      for (final row in r.rows) Card.outlined(margin: const EdgeInsets.only(bottom: 8), child: Padding(
        padding: const EdgeInsets.all(12), child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          Text(row.name, style: Theme.of(context).textTheme.titleSmall),
          if (row.code.isNotEmpty) Text(row.code),
          if (row.status.isNotEmpty) Text(row.status, style: TextStyle(fontWeight: FontWeight.w600,
            color: Theme.of(context).colorScheme.primary)),
          for (final detail in row.details)
            if (detail.value.isNotEmpty) Text('${detail.label} : ${detail.value}'),
          if (row.note.isNotEmpty) Text(row.note),
          if (board && row.id.isNotEmpty) Wrap(alignment: WrapAlignment.end, spacing: 8, children: [
            TextButton(onPressed: busy ? null : () => execute(Operation.academicExport,
              {'token': r.token, 'document': 'bulletin', 'row': row.id}), child: const Text('Bulletin PDF')),
            TextButton(onPressed: busy ? null : () => execute(Operation.registerNative,
              {'token': r.token, 'row': row.id}), child: const Text('Fiche et jury')),
          ]),
        ]))),
    ]);
  }

  Widget lmdRegister(SchoolRegister r) {
    const groups = [('Tranche', 'Échéancier des frais'), ('Structure', 'Structures académiques'),
      ('Semestre', 'Calendrier académique'), ('Programme', 'Maquette pédagogique UE / EC')];
    return Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
      Align(alignment: Alignment.centerLeft, child: TextButton.icon(onPressed: busy ? null : () => leaveRegister(r),
        icon: const Icon(Icons.arrow_back), label: const Text('Retour à l’accueil'))),
      heading(r.title, r.subtitle),
      if (r.promotions.isNotEmpty) filter('Promotion', r.promotion, r.promotions,
        (v) => loadRegister(r.kind, current: r, promotion: v)),
      const SizedBox(height: 12),
      Card.outlined(margin: EdgeInsets.zero, child: Padding(padding: const EdgeInsets.all(16),
        child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          Text('Frais académiques annuels', style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 8),
          Text(r.overview, style: Theme.of(context).textTheme.headlineSmall),
          if (r.canWrite) ...[const SizedBox(height: 12),
            action('Configurer les tranches', Icons.payments_outlined, Operation.lmdFeeForm,
              primary: true, args: {'token': r.token})],
        ]))),
      const SizedBox(height: 12),
      action('Structures, calendrier et programme', Icons.account_tree_outlined,
        Operation.registerNative, args: {'token': r.token}),
      for (final group in groups) ...[
        const SizedBox(height: 16),
        Text(group.$2, style: Theme.of(context).textTheme.titleMedium),
        const SizedBox(height: 8),
        if (!r.rows.any((x) => x.status == group.$1)) const Text('Aucun élément enregistré.'),
        for (final row in r.rows.where((x) => x.status == group.$1))
          Card.outlined(margin: const EdgeInsets.only(bottom: 8), child: Padding(
            padding: const EdgeInsets.all(12), child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
              Text(row.name, style: Theme.of(context).textTheme.titleSmall),
              if (row.code.isNotEmpty) Text(row.code),
              for (final item in row.details) Text('${item.label} : ${item.value}'),
            ]))),
      ],
    ]);
  }

  Widget administrationRegister(SchoolRegister r) {
    final settings = r.kind == 'settings';
    final directForm = settings && (r.date == 'promotions' || r.date == 'etablissement');
    final label = settings ? switch (r.date) {
      'etablissement' => 'Modifier les informations',
      'annees' => 'Gérer les années scolaires',
      'sections' => 'Créer ou gérer les sections',
      'promotions' => 'Nouvelle promotion',
      'frais' => 'Gérer les catégories de frais',
      _ => 'Ouvrir les paramètres',
    } : 'Nouvel utilisateur';
    return Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
      Align(alignment: Alignment.centerLeft, child: TextButton.icon(onPressed: busy ? null : () => leaveRegister(r),
        icon: const Icon(Icons.arrow_back), label: const Text('Retour à l’accueil'))),
      heading(r.title, r.subtitle),
      if (settings && r.options.isNotEmpty) ...[
        filter('Rubrique', r.date, r.options, (v) => loadRegister(r.kind, current: r, date: v)),
        const SizedBox(height: 16),
        Text(r.overview, style: Theme.of(context).textTheme.titleLarge),
        if (r.context.isNotEmpty) Text(r.context),
        const SizedBox(height: 12),
      ],
      if (r.canWrite) action(label, Icons.settings_outlined,
        !settings ? Operation.userForm : directForm ? Operation.settingsForm : Operation.registerNative,
        primary: true, args: {'token': r.token, if (!settings) 'mode': 'create'}),
      const SizedBox(height: 16),
      if (r.rows.isEmpty) const Text('Aucun élément enregistré.'),
      for (final row in r.rows) Card.outlined(margin: const EdgeInsets.only(bottom: 8), child: Padding(
        padding: const EdgeInsets.all(12), child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          Text(row.name, style: Theme.of(context).textTheme.titleSmall),
          if (row.code.isNotEmpty) Text(row.code),
          if (row.status.isNotEmpty) Text(row.status, style: TextStyle(color: Theme.of(context).colorScheme.primary)),
          for (final item in row.details) if (item.value.isNotEmpty) Padding(
            padding: const EdgeInsets.only(top: 4), child: Text(item.label == 'Valeur' ? item.value : '${item.label} : ${item.value}')),
          if (row.id.isNotEmpty && r.canWrite) Wrap(alignment: WrapAlignment.end, spacing: 8, children: [
            TextButton.icon(onPressed: busy ? null : () => execute(!settings ? Operation.userForm :
              directForm ? Operation.settingsForm : Operation.registerNative,
              {'token': r.token, 'row': row.id, if (!settings) 'mode': 'edit'}),
              icon: Icon(!settings || directForm ? Icons.edit_outlined : Icons.more_horiz),
              label: Text(!settings || directForm ? 'Modifier' : 'Actions')),
            if (!settings) TextButton.icon(onPressed: busy ? null : () => execute(Operation.userForm,
              {'token': r.token, 'row': row.id, 'mode': 'reset'}), icon: const Icon(Icons.key_outlined), label: const Text('Code provisoire')),
            if (!settings) TextButton(onPressed: busy ? null : () => execute(Operation.registerNative,
              {'token': r.token, 'row': row.id}), child: const Text('Autres actions')),
          ]),
        ]))),
    ]);
  }

  Widget dashboardRegister(SchoolRegister r) {
    final promoter = r.kind == 'promoter';
    final groups = r.rows.map((x) => x.status).toSet();
    Widget metric(RegisterRow x) => Card.outlined(margin: EdgeInsets.zero, child: Padding(
      padding: const EdgeInsets.all(12), child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        Text(x.name, style: Theme.of(context).textTheme.titleSmall),
        if (x.code.isNotEmpty) ...[const SizedBox(height: 6), Text(x.code,
          style: Theme.of(context).textTheme.headlineSmall?.copyWith(color: Theme.of(context).colorScheme.primary))],
        if (x.note.isNotEmpty) ...[const SizedBox(height: 4), Text(x.note)],
        if (x.share != null) ...[const SizedBox(height: 8),
          if (x.track != null) ...[
            LinearProgressIndicator(value: x.track!.clamp(0.0, 1.0).toDouble(),
              color: Theme.of(context).colorScheme.outline, minHeight: 5),
            const SizedBox(height: 4),
          ],
          LinearProgressIndicator(value: x.share!.clamp(0.0, 1.0).toDouble(), minHeight: 7),
        ],
        for (final item in x.details) if (item.value.isNotEmpty) Padding(
          padding: const EdgeInsets.only(top: 4), child: Text('${item.label} : ${item.value}')),
      ])));
    return Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
      Align(alignment: Alignment.centerLeft, child: TextButton.icon(onPressed: busy ? null : () => leaveRegister(r),
        icon: const Icon(Icons.arrow_back), label: const Text('Retour à l’accueil'))),
      heading(r.title, r.subtitle),
      if (promoter) const Text('Consultation uniquement · données agrégées'),
      if (r.overview.isNotEmpty) Text(r.overview),
      if (r.context.isNotEmpty) Padding(padding: const EdgeInsets.symmetric(vertical: 12),
        child: Text(r.context, style: TextStyle(color: Theme.of(context).colorScheme.onSurfaceVariant))),
      if (r.rows.isEmpty) const Text('Aucune donnée disponible.'),
      for (final group in groups) ...[
        const SizedBox(height: 20),
        Text(group, style: Theme.of(context).textTheme.titleMedium),
        if (group == 'Encaissements') const Text('Piste grise : attendu · barre colorée : encaissé'),
        const SizedBox(height: 8),
        LayoutBuilder(builder: (context, constraints) {
          final columns = (group == 'Indicateurs' || group == 'Aujourd’hui') && constraints.maxWidth >= 360
            ? (constraints.maxWidth >= 720 ? 3 : 2) : 1;
          final width = (constraints.maxWidth - (columns - 1) * 8) / columns;
          return Wrap(spacing: 8, runSpacing: 8, children: [
            for (final x in r.rows.where((x) => x.status == group)) SizedBox(width: width, child: metric(x)),
          ]);
        }),
      ],
      if (!promoter && (session?.modules.contains('cashier') ?? false)) ...[
        const SizedBox(height: 16),
        action('Encaisser', Icons.payments_outlined, Operation.openModule, args: {'module': 'cashier'}, primary: true),
      ],
    ]);
  }

  Widget schoolRegister(SchoolRegister r) {
    if (r.kind == 'dashboard' || r.kind == 'promoter') return dashboardRegister(r);
    if (r.kind == 'settings' || r.kind == 'users') return administrationRegister(r);
    if (r.kind == 'lmd') return lmdRegister(r);
    if (r.kind == 'deliberation' || r.kind == 'progress') return academicRegister(r);
    if (r.kind == 'marks') return marksRegister(r);
    if (r.kind == 'codes') return codesRegister(r);
    if (r.kind != 'attendance' && r.kind != 'discipline') return personnelRegister(r);
    final attendance = r.kind == 'attendance';
    final locked = busy || r.dirty;
    return Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
      Align(alignment: Alignment.centerLeft, child: TextButton.icon(onPressed: busy ? null : () => leaveRegister(r),
        icon: const Icon(Icons.arrow_back), label: const Text('Retour à l’accueil'))),
      heading(r.title, r.subtitle),
      if (r.promotions.isNotEmpty) IgnorePointer(ignoring: locked, child: filter('Promotion', r.promotion, r.promotions,
        (v) => loadRegister(r.kind, current: r, promotion: v))),
      const SizedBox(height: 12),
      if (attendance) ...[
        OutlinedButton.icon(onPressed: locked ? null : () async {
          final picked = await showDatePicker(context: context, initialDate: DateTime.tryParse(r.date) ?? DateTime.now(),
            firstDate: DateTime(1900), lastDate: DateTime(2100, 12, 31));
          if (!mounted || picked == null || session?.register?.token != r.token) return;
          await loadRegister(r.kind, current: r, date: picked.toIso8601String().substring(0, 10));
        }, icon: const Icon(Icons.calendar_today), label: Text(r.date)),
        const SizedBox(height: 8),
        SegmentedButton<bool>(segments: const [
          ButtonSegment(value: false, label: Text('Appel')),
          ButtonSegment(value: true, label: Text('Bilan 30 jours')),
        ], selected: {r.summary}, onSelectionChanged: locked ? null : (v) => loadRegister(r.kind, current: r, summary: v.first)),
        if (r.pending) const Padding(padding: EdgeInsets.symmetric(vertical: 8),
          child: Text('Appel pas encore fait. Présents par défaut : vérifiez avant d’enregistrer.')),
        if (r.dirty) const Text('Modifications non enregistrées'),
        if (r.canWrite && r.rows.isNotEmpty) ...[
          Wrap(spacing: 8, runSpacing: 8, children: [
            OutlinedButton(onPressed: busy ? null : () => execute(Operation.attendanceAll, {'token': r.token}),
              child: const Text('Tous présents')),
            FilledButton(onPressed: busy || (!r.dirty && !r.pending) ? null : () =>
              execute(Operation.saveAttendance, {'token': r.token}), child: const Text('Enregistrer l’appel')),
          ]),
          Wrap(spacing: 8, children: [
            TextButton(onPressed: locked ? null : () => execute(Operation.attendanceDay,
              {'token': r.token, 'confirm': 'true'}), child: const Text('Confirmer le jour de cours')),
            TextButton(onPressed: locked ? null : () => execute(Operation.attendanceDay,
              {'token': r.token, 'confirm': 'false'}), child: const Text('Retirer la confirmation')),
          ]),
        ],
      ] else ...[
        if (r.options.isNotEmpty) filter('Mesure', r.measure, r.options, (v) => loadRegister(r.kind, current: r, measure: v)),
        action('Exporter en CSV', Icons.download_outlined, Operation.disciplineExport, args: {'token': r.token}),
      ],
      OutlinedButton.icon(onPressed: locked ? null : () => execute(Operation.registerNative, {'token': r.token}),
        icon: const Icon(Icons.more_horiz), label: Text(attendance ? 'Alertes et autres actions' : 'Décisions et autres actions')),
      const SizedBox(height: 12),
      if (r.rows.isEmpty) const Text('Aucun élément dans la sélection.'),
      for (final row in r.rows) Card.outlined(margin: const EdgeInsets.only(bottom: 8),
        child: Padding(padding: const EdgeInsets.all(12), child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          Text(row.name, style: Theme.of(context).textTheme.titleSmall),
          Text(row.code),
          if (attendance && !r.summary) ...[
            const SizedBox(height: 8),
            if (r.canWrite) filter('Statut', row.status, r.options, (v) =>
              execute(Operation.editAttendance, {'token': r.token, 'row': row.id, 'status': v, 'note': row.note}))
            else Text(r.options.where((x) => x.id == row.status).map((x) => x.label).firstOrNull ?? row.status),
            if (r.canWrite) TextButton(onPressed: busy ? null : () => editAttendanceNote(r, row),
              child: Text(row.note.isEmpty ? 'Ajouter une remarque' : row.note))
            else if (row.note.isNotEmpty) Text(row.note),
          ] else ...[
            if (row.status.isNotEmpty) Text(row.status),
            if (row.note.isNotEmpty) Text(row.note),
            for (final field in row.details) Text('${field.label} : ${field.value}'),
          ],
        ]))),
    ]);
  }

  Future<void> listReceipts({ReceiptJournal? current, String? query, String? from,
    String? to, String? method, String? status, String? promotion, int page = 1}) =>
    execute(Operation.listReceipts, {'page': page.toString(), 'q': query ?? current?.search ?? '',
      'from': from ?? current?.from ?? '', 'to': to ?? current?.to ?? '',
      'method': method ?? current?.method ?? '', 'status': status ?? current?.status ?? '',
      'promotion': promotion ?? current?.promotion ?? ''});

  Future<void> chooseJournalDate(ReceiptJournal j, bool start) async {
    final value = start ? j.from : j.to;
    final initial = DateTime.tryParse(value) ?? DateTime.now();
    final picked = await showDatePicker(context: context, initialDate: initial,
      firstDate: DateTime(1900), lastDate: DateTime(2100, 12, 31));
    if (!mounted || picked == null || !identical(session?.journal, j)) return;
    final wire = picked.toIso8601String().substring(0, 10);
    if (start) { await listReceipts(current: j, from: wire); }
    else { await listReceipts(current: j, to: wire); }
  }

  Widget receipts(ReceiptJournal j) => Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
    Align(alignment: Alignment.centerLeft, child: TextButton.icon(
      onPressed: busy ? null : () => execute(Operation.snapshot),
      icon: const Icon(Icons.arrow_back), label: const Text('Retour à l’accueil'))),
    heading(j.title, j.subtitle),
    TextField(controller: journalSearch, enabled: !busy, maxLength: 100, textInputAction: TextInputAction.search,
      onChanged: (_) => setState(() {}), onSubmitted: (v) => listReceipts(current: j, query: v.trim()),
      decoration: InputDecoration(labelText: 'Nom, matricule ou numéro de reçu', counterText: '',
        prefixIcon: const Icon(Icons.search), suffixIcon: IconButton(tooltip: 'Appliquer la recherche',
          onPressed: busy ? null : () => listReceipts(current: j, query: journalSearch.text.trim()),
          icon: const Icon(Icons.arrow_forward)))),
    const SizedBox(height: 12),
    Wrap(spacing: 8, runSpacing: 8, children: [
      OutlinedButton.icon(onPressed: busy ? null : () => chooseJournalDate(j, true),
        icon: const Icon(Icons.calendar_today), label: Text(j.from.isEmpty ? 'Du' : j.from)),
      OutlinedButton.icon(onPressed: busy ? null : () => chooseJournalDate(j, false),
        icon: const Icon(Icons.calendar_today), label: Text(j.to.isEmpty ? 'Au' : j.to)),
      if (j.from.isNotEmpty || j.to.isNotEmpty)
        TextButton(onPressed: busy ? null : () => listReceipts(current: j, from: '', to: ''), child: const Text('Effacer les dates')),
    ]),
    const SizedBox(height: 12),
    if (j.methods.isNotEmpty) filter('Mode de paiement', j.method, j.methods, (v) => listReceipts(current: j, method: v)),
    const SizedBox(height: 8),
    if (j.statuses.isNotEmpty) filter('Statut', j.status, j.statuses, (v) => listReceipts(current: j, status: v)),
    const SizedBox(height: 8),
    if (j.promotions.isNotEmpty) filter('Promotion', j.promotion, j.promotions, (v) => listReceipts(current: j, promotion: v)),
    const SizedBox(height: 12),
    Text('Exporter le journal selon les filtres appliqués', style: Theme.of(context).textTheme.bodySmall),
    if (journalSearch.text.trim() != j.search) const Text('Appliquez la recherche avant d’exporter.'),
    Wrap(spacing: 8, children: [
      for (final (format, label) in [('xlsx', 'Excel'), ('pdf', 'PDF')])
        OutlinedButton(onPressed: busy || journalSearch.text.trim() != j.search ? null :
          () => execute(Operation.exportReceipts, {'format': format, 'page': j.page.toString()}), child: Text(label)),
    ]),
    const SizedBox(height: 12),
    if (j.rows.isEmpty) const Text('Aucun reçu dans la sélection.'),
    for (final row in j.rows) Card.outlined(margin: const EdgeInsets.only(bottom: 8),
      child: InkWell(onTap: busy ? null : () => execute(Operation.openReceipt, {'receipt': row.id}),
        child: Padding(padding: const EdgeInsets.all(12), child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Text(row.number, style: Theme.of(context).textTheme.titleSmall),
          Text(row.student), Text([row.code, row.promotion, row.date].where((v) => v.isNotEmpty).join(' · ')),
          const Divider(),
          Text(row.amount, style: Theme.of(context).textTheme.titleMedium),
          Text('${row.method} · ${row.status}'),
          Text('Visé par : ${row.cashier}'),
        ])))),
    Row(children: [
      IconButton(tooltip: 'Page précédente', onPressed: busy || !j.hasPrevious ? null :
        () => listReceipts(current: j, page: j.page - 1), icon: const Icon(Icons.chevron_left)),
      Expanded(child: Text('Page ${j.page}', textAlign: TextAlign.center)),
      IconButton(tooltip: 'Page suivante', onPressed: busy || !j.hasNext ? null :
        () => listReceipts(current: j, page: j.page + 1), icon: const Icon(Icons.chevron_right)),
    ]),
  ]);

  Widget cashier(CashierScreen c) => Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
    Align(alignment: Alignment.centerLeft, child: TextButton.icon(
      onPressed: busy ? null : () => execute(Operation.cashierLeave),
      icon: const Icon(Icons.arrow_back), label: const Text('Retour à l’accueil'))),
    heading('Encaisser', 'Échéances, montant, mode et reçu'),
    TextField(controller: cashierQuery, enabled: !busy, maxLength: 100,
      textInputAction: TextInputAction.search,
      onSubmitted: (v) { if (v.trim().length >= 2) unawaited(execute(Operation.cashierSearch, {'q': v.trim()})); },
      decoration: InputDecoration(labelText: 'Nom ou matricule', counterText: '',
        prefixIcon: const Icon(Icons.search), suffixIcon: IconButton(tooltip: 'Rechercher un payeur',
          onPressed: busy ? null : () {
            if (cashierQuery.text.trim().length >= 2) {
              unawaited(execute(Operation.cashierSearch, {'q': cashierQuery.text.trim()}));
            }
          }, icon: const Icon(Icons.arrow_forward)))),
    if (c.searchStatus.isNotEmpty) Text(c.searchStatus),
    if (!c.hasStudent) for (final payer in c.payers)
      ListTile(title: Text(payer.label), subtitle: Text(payer.detail), trailing: const Icon(Icons.chevron_right),
        onTap: busy ? null : () => execute(Operation.cashierChoose, {'student': payer.id})),
    if (c.hint.isNotEmpty) Padding(padding: const EdgeInsets.symmetric(vertical: 8), child: Text(c.hint)),
    if (c.hasStudent) ...[
      const SizedBox(height: 12),
      Card.outlined(child: Padding(padding: const EdgeInsets.all(12), child: Column(
        crossAxisAlignment: CrossAxisAlignment.start, children: [
          Text(c.student, style: Theme.of(context).textTheme.titleMedium), Text(c.info),
          const Divider(), Text('Solde : ${c.balance}'), Text('Exigible : ${c.overdue}'),
        ]))),
      if (c.hasReceipt) Text(c.receipt)
      else ...[
        const SizedBox(height: 12),
        Text('Échéances', style: Theme.of(context).textTheme.titleMedium),
        for (final fee in c.fees)
          CheckboxListTile(contentPadding: EdgeInsets.zero, title: Text(fee.label),
            subtitle: Text('${fee.amount} · ${fee.deadline}'), value: cashierFees.contains(fee.id),
            onChanged: busy ? null : (v) => setState(() {
              if (v == true) { cashierFees.add(fee.id); } else { cashierFees.remove(fee.id); }
              cashierDirty = true;
            })),
        TextField(controller: cashierAmount, enabled: !busy, keyboardType: const TextInputType.numberWithOptions(decimal: true),
          decoration: const InputDecoration(labelText: 'Montant reçu'),
          onChanged: (_) => setState(() => cashierDirty = true)),
        const SizedBox(height: 12),
        filter('Mode de paiement', cashierMethod, c.methods, (v) => setState(() {
          cashierMethod = v; cashierReference.clear(); cashierDirty = true;
        })),
        if (cashierMethod != 'Especes' && cashierMethod != 'Cash') ...[
          const SizedBox(height: 12),
          TextField(controller: cashierReference, enabled: !busy, maxLength: 120,
            decoration: const InputDecoration(labelText: 'Référence du paiement'),
            onChanged: (_) => setState(() => cashierDirty = true)),
        ],
        const SizedBox(height: 12),
        TextField(controller: cashierNote, enabled: !busy, maxLength: 250,
          decoration: const InputDecoration(labelText: 'Observation'),
          onChanged: (_) => setState(() => cashierDirty = true)),
        action('Vérifier la répartition', Icons.calculate_outlined, Operation.cashierPreview,
          args: {'fees': cashierFees.join(','), 'amount': cashierAmount.text, 'method': cashierMethod,
            'reference': cashierReference.text, 'note': cashierNote.text}),
        if (!cashierDirty) ...[
          const SizedBox(height: 12),
          for (final allocation in c.allocations)
            ListTile(contentPadding: EdgeInsets.zero, title: Text(allocation.label),
              subtitle: Text('Reste : ${allocation.remaining}'), trailing: Text(allocation.amount)),
          Text('Solde après versement : ${c.balanceAfter}'),
          const SizedBox(height: 8),
          FilledButton.icon(onPressed: busy || !c.canPay ? null : () => execute(Operation.cashierConfirm),
            icon: const Icon(Icons.check), label: Text('Confirmer ${c.total}')),
        ],
      ],
    ],
  ]);

  Widget enrollmentField(EnrollmentForm e, EnrollmentField f) {
    final current = enrollmentValues[f.key] ?? '';
    if (f.kind == 'Header') return Padding(padding: const EdgeInsets.only(top: 12, bottom: 4),
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Text(f.label, style: Theme.of(context).textTheme.titleMedium),
        if (f.hint.isNotEmpty) Text(f.hint, style: Theme.of(context).textTheme.bodySmall),
      ]));
    final decoration = InputDecoration(labelText: f.required ? '${f.label} *' : f.label,
      helperText: f.hint.isEmpty ? null : f.hint, helperMaxLines: 3,
      errorText: f.error.isEmpty ? null : f.error, errorMaxLines: 3);
    if (f.kind == 'Flag') return SwitchListTile(contentPadding: EdgeInsets.zero,
      title: Text(f.label), subtitle: f.hint.isEmpty ? null : Text(f.hint),
      value: current == 'true', onChanged: busy ? null : (v) => setState(() => enrollmentValues[f.key] = v.toString()));
    if (f.kind == 'Choice') return InputDecorator(decoration: decoration,
      child: DropdownButtonHideUnderline(child: DropdownButton<String>(
        value: f.options.any((x) => x.id == current) ? current : null,
        isExpanded: true, hint: const Text('Choisir'),
        items: f.options.map((x) => DropdownMenuItem(value: x.id,
          child: Text(x.label, overflow: TextOverflow.ellipsis))).toList(),
        onChanged: busy ? null : (v) => setState(() => enrollmentValues[f.key] = v ?? ''))));
    if (f.kind == 'Day') return InkWell(
      onTap: busy ? null : () async {
        final now = DateTime.now();
        final first = DateTime(1900);
        final last = DateTime(now.year + 5, 12, 31);
        final parsed = DateTime.tryParse(current);
        final initial = parsed != null && !parsed.isBefore(first) && !parsed.isAfter(last) ? parsed : now;
        final picked = await showDatePicker(context: context, initialDate: initial, firstDate: first, lastDate: last);
        if (!mounted || picked == null || session?.enrollment?.token != e.token) return;
        setState(() => enrollmentValues[f.key] = picked.toIso8601String().substring(0, 10));
      },
      child: InputDecorator(decoration: decoration.copyWith(suffixIcon: const Icon(Icons.calendar_today)),
        child: Text(current.isEmpty ? 'Choisir une date' : current)));
    return TextFormField(key: f.kind == 'Secret' ? ObjectKey(f) : ValueKey('${e.token}:${f.key}:${f.value}'), initialValue: current,
      enabled: !busy, obscureText: f.kind == 'Secret',
      autocorrect: f.kind != 'Secret', enableSuggestions: f.kind != 'Secret',
      maxLength: f.kind == 'Phone' ? 10 : 500,
      keyboardType: f.kind == 'Phone' ? TextInputType.phone : f.kind == 'Amount'
        ? const TextInputType.numberWithOptions(decimal: true) : TextInputType.text,
      inputFormatters: f.kind == 'Phone' ? [FilteringTextInputFormatter.digitsOnly] : null,
      textCapitalization: f.kind == 'Secret' ? TextCapitalization.none : TextCapitalization.words,
      maxLines: f.kind == 'Multiline' ? 3 : 1,
      decoration: decoration.copyWith(counterText: ''),
      onChanged: (v) => enrollmentValues[f.key] = v);
  }

  Widget enrollment(EnrollmentForm e) => Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
    heading(e.title, e.subtitle),
    if (e.error.isNotEmpty) Padding(padding: const EdgeInsets.only(bottom: 12),
      child: Text(e.error, style: TextStyle(color: Theme.of(context).colorScheme.error))),
    for (final field in e.fields)
      Padding(padding: const EdgeInsets.only(bottom: 12), child: enrollmentField(e, field)),
    if (e.purpose == 'lmdFee' || (e.purpose == 'promotion' && e.fields.any((f) => f.key == 'tranches'))) ...[
      OutlinedButton.icon(onPressed: busy ? null : () => execute(e.purpose == 'promotion' ? Operation.settingsPreview : Operation.lmdFeePreview,
        {'token': e.token, ...enrollmentValues}), icon: const Icon(Icons.calculate_outlined),
        label: const Text('Aperçu des tranches')),
      const SizedBox(height: 8),
    ],
    FilledButton.icon(onPressed: busy ? null : () async {
      if (e.purpose == 'lmdFee' || (e.purpose == 'promotion' && e.fields.any((f) => f.key == 'tranches'))) {
        final accepted = await showDialog<bool>(context: context, builder: (context) => AlertDialog(
          title: const Text('Confirmer les frais académiques'),
          content: Text('Total annuel : ${enrollmentValues['annuel']} · ${enrollmentValues['tranches']} tranche(s).'),
          actions: [TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Annuler')),
            FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Confirmer'))]));
        if (!mounted || accepted != true || session?.enrollment?.token != e.token) return;
      }
      if (e.purpose == 'user-reset') {
        final accepted = await showDialog<bool>(context: context, builder: (context) => AlertDialog(
          title: const Text('Réinitialiser le code ?'),
          content: const Text('Les sessions existantes seront invalidées. Le titulaire devra changer son code à la prochaine connexion.'),
          actions: [TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Annuler')),
            FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Confirmer'))]));
        if (!mounted || accepted != true || session?.enrollment?.token != e.token) return;
      }
      await execute(e.purpose == 'school' || e.purpose == 'promotion' || e.purpose.startsWith('user-')
        ? Operation.settingsSubmit : Operation.submitEnrollment, {'token': e.token, ...enrollmentValues});
    }, icon: Icon(e.purpose == 'enrollment' ? Icons.person_add_alt_1 : Icons.save_outlined),
      label: Text(e.purpose == 'lmdFee' ? 'Enregistrer les frais' : e.purpose == 'promotion' ? 'Enregistrer la promotion'
        : e.purpose == 'school' ? 'Enregistrer les informations' : e.purpose == 'user-create' ? 'Créer le compte'
        : e.purpose == 'user-edit' ? 'Enregistrer le compte' : e.purpose == 'user-reset' ? 'Réinitialiser le code' : 'Inscrire')),
    const SizedBox(height: 8),
    action('Annuler', Icons.close, Operation.snapshot),
  ]);

  Widget studentProfile(StudentProfile p) => Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
    Align(alignment: Alignment.centerLeft, child: TextButton.icon(
      onPressed: busy ? null : () => listStudents(studentPage, studentQuery),
      icon: const Icon(Icons.arrow_back), label: const Text('Retour au registre'))),
    heading(p.title, p.subtitle),
    Wrap(spacing: 8, runSpacing: 8, children: [
      if (session?.modules.contains('cashier') ?? false)
        FilledButton.icon(onPressed: busy ? null : () => execute(Operation.cashierStudent, {'student': p.id}),
          icon: const Icon(Icons.payments_outlined), label: const Text('Encaisser')),
      OutlinedButton.icon(onPressed: busy ? null : () => execute(Operation.studentActions, {'student': p.id}),
        icon: const Icon(Icons.more_horiz), label: const Text('Actions du dossier')),
    ]),
    const SizedBox(height: 16),
    for (final section in p.sections)
      Card.outlined(margin: const EdgeInsets.only(bottom: 12), child: Padding(
        padding: const EdgeInsets.all(12), child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          Text(section.title, style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 8),
          if (section.rows.isEmpty) const Text('Aucun élément'),
          for (var index = 0; index < section.rows.length; index++) ...[
            if (index > 0) const Divider(height: 20),
            for (final field in section.rows[index])
              Padding(padding: const EdgeInsets.symmetric(vertical: 3),
                child: LayoutBuilder(builder: (context, constraints) => constraints.maxWidth < 280
                  ? Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                    Text(field.label, style: Theme.of(context).textTheme.bodySmall),
                    Text(field.value)])
                  : Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
                    Expanded(flex: 2, child: Text(field.label, style: Theme.of(context).textTheme.bodySmall)),
                    const SizedBox(width: 8),
                    Expanded(flex: 3, child: Text(field.value)),
                  ]))),
          ],
        ]))),
  ]);

  Future<void> listMonthly({MonthlyListing? current, String? period,
    String? promotion, String? status, String? query, int page = 1}) =>
    execute(Operation.listMonthly, {
      'page': page.toString(), 'q': query ?? current?.search ?? '',
      if (period != null || current != null) 'period': period ?? current!.period,
      if (promotion != null || current != null) 'promotion': promotion ?? current!.promotion,
      if (status != null || current != null) 'status': status ?? current!.status,
    });

  Widget filter(String label, String value, List<FilterChoice> options, ValueChanged<String> changed) =>
    InputDecorator(decoration: InputDecoration(labelText: label), child: DropdownButtonHideUnderline(
      child: DropdownButton<String>(value: value, isExpanded: true,
        items: options.map((x) => DropdownMenuItem(value: x.id,
          child: Text(x.label, overflow: TextOverflow.ellipsis))).toList(),
        onChanged: busy ? null : (v) { if (v != null) changed(v); })));

  Widget monthly(MonthlyListing m) => Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
    Align(alignment: Alignment.centerLeft, child: TextButton.icon(
      onPressed: busy ? null : () => execute(Operation.snapshot),
      icon: const Icon(Icons.arrow_back), label: const Text('Retour à l’accueil'))),
    heading(m.title, m.subtitle),
    filter('Période', m.period, m.periods, (v) => listMonthly(current: m, period: v)),
    const SizedBox(height: 10),
    filter('Promotion', m.promotion, m.promotions, (v) => listMonthly(current: m, promotion: v)),
    const SizedBox(height: 10),
    filter('État du paiement', m.status, m.states, (v) => listMonthly(current: m, status: v)),
    const SizedBox(height: 10),
    TextField(controller: paymentSearch, enabled: !busy, maxLength: 100,
      onChanged: (_) => setState(() {}),
      textInputAction: TextInputAction.search, onSubmitted: (q) => listMonthly(current: m, query: q.trim()),
      decoration: InputDecoration(labelText: 'Rechercher un nom', counterText: '',
        prefixIcon: const Icon(Icons.search), suffixIcon: IconButton(tooltip: 'Appliquer la recherche',
          onPressed: busy ? null : () => listMonthly(current: m, query: paymentSearch.text.trim()),
          icon: const Icon(Icons.arrow_forward)))),
    const SizedBox(height: 16),
    Text(m.exportScope, style: Theme.of(context).textTheme.bodySmall),
    const SizedBox(height: 8),
    Text('Exporter toute la sélection filtrée (${m.count})'),
    if (paymentSearch.text.trim() != m.search)
      const Text('Appliquez la recherche avant d’exporter.'),
    Wrap(spacing: 8, runSpacing: 8, children: [
      for (final (format, label) in [('xlsx', 'Excel'), ('pdf', 'PDF'), ('docx', 'Word')])
        OutlinedButton(onPressed: busy || paymentSearch.text.trim() != m.search ? null : () => execute(Operation.exportMonthly,
          {'format': format, 'page': m.page.toString()}), child: Text(label)),
    ]),
    const SizedBox(height: 12),
    if (m.rows.isEmpty) const Text('Aucun résultat pour ces filtres.'),
    for (final row in m.rows)
      Card.outlined(margin: const EdgeInsets.only(bottom: 8), child: Padding(
        padding: const EdgeInsets.all(12), child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Text(row.name, style: Theme.of(context).textTheme.titleSmall),
          Text(row.promotion),
          const SizedBox(height: 8),
          Text(row.status, style: TextStyle(fontWeight: FontWeight.w600,
            color: Theme.of(context).colorScheme.primary)),
          const Divider(),
          Text('Attendu : ${row.expected}'),
          Text('Versé : ${row.paid}'),
          Text('Reste : ${row.remaining}'),
        ]))),
    Row(children: [
      IconButton(tooltip: 'Page précédente', onPressed: busy || !m.hasPrevious ? null :
        () => listMonthly(current: m, page: m.page - 1), icon: const Icon(Icons.chevron_left)),
      Expanded(child: Text('Page ${m.page}', textAlign: TextAlign.center)),
      IconButton(tooltip: 'Page suivante', onPressed: busy || !m.hasNext ? null :
        () => listMonthly(current: m, page: m.page + 1), icon: const Icon(Icons.chevron_right)),
    ]),
  ]);

  @override
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    final current = session;
    final listing = current?.students;
    final payments = current?.monthly;
    final journal = current?.journal;
    final register = current?.register;
    return Scaffold(
      appBar: AppBar(title: const Text('SCOLIA Pro'),
        actions: [IconButton(tooltip: 'Actualiser', onPressed: busy || (register?.dirty ?? false) ? null :
          () => listing != null ? listStudents(listing.page, listing.search)
            : payments != null ? listMonthly(current: payments, page: payments.page)
            : journal != null ? listReceipts(current: journal, page: journal.page)
            : register != null ? loadRegister(register.kind, current: register)
            : execute(Operation.snapshot), icon: const Icon(Icons.refresh))]),
      body: SafeArea(child: SingleChildScrollView(
        padding: const EdgeInsets.fromLTRB(20, 16, 20, 28),
        child: Center(child: ConstrainedBox(constraints: const BoxConstraints(maxWidth: 860),
          child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
            if (busy) const LinearProgressIndicator(),
            if (error != null) Padding(padding: const EdgeInsets.symmetric(vertical: 12),
              child: Text(error!, style: TextStyle(color: colors.error))),
            if (current == null) ...[
              heading('Initialisation', 'Vérification du moteur SCOLIA.'),
              if (!busy) action('Réessayer', Icons.refresh, Operation.snapshot),
            ] else ...[
              if (current.notice.isNotEmpty) Padding(padding: const EdgeInsets.only(bottom: 12),
                child: Text(current.notice)),
              switch (current.stage) {
                Stage.pairing => pairing(), Stage.login => login(current), Stage.home => home(current),
                Stage.loading => heading('Ouverture de SCOLIA', 'Préparation sécurisée de votre espace de travail…'),
                Stage.nativeScreen => heading('Interface SCOLIA', 'Cette opération utilise encore l’interface native existante.'),
                Stage.students => students(current.students!),
                Stage.monthly => monthly(current.monthly!),
                Stage.studentProfile => studentProfile(current.profile!),
                Stage.enrollment => enrollment(current.enrollment!),
                Stage.cashier => cashier(current.cashier!),
                Stage.receipts => receipts(current.journal!),
                Stage.schoolRegister => schoolRegister(current.register!),
              },
            ],
          ]),
        )),
      )),
    );
  }
}

class ModuleTile extends StatelessWidget {
  const ModuleTile({super.key, required this.label, required this.icon, required this.onTap});
  final String label;
  final IconData icon;
  final VoidCallback? onTap;
  @override
  Widget build(BuildContext context) {
    final c = Theme.of(context).colorScheme;
    final shape = RoundedRectangleBorder(borderRadius: BorderRadius.circular(8),
      side: BorderSide(color: c.outlineVariant));
    return Material(color: c.surface, shape: shape, clipBehavior: Clip.antiAlias,
      child: InkWell(onTap: onTap, child: ConstrainedBox(
        constraints: const BoxConstraints(minHeight: 116),
        child: Padding(padding: const EdgeInsets.all(12),
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Container(width: 36, height: 36, alignment: Alignment.center,
              decoration: BoxDecoration(color: c.primaryContainer, borderRadius: BorderRadius.circular(7)),
              child: Icon(icon, color: c.onPrimaryContainer, size: 24)),
            const SizedBox(height: 8),
            Text(label, style: const TextStyle(fontWeight: FontWeight.w600)),
          ]),
        ))));
  }
}
