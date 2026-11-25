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
    final scheme = ColorScheme.fromSeed(seedColor: const Color(0xff345ab7),
      brightness: brightness);
    final base = ThemeData(useMaterial3: true, colorScheme: scheme);
    final shape = RoundedRectangleBorder(borderRadius: BorderRadius.circular(10));
    return base.copyWith(
      scaffoldBackgroundColor: scheme.surface,
      appBarTheme: AppBarTheme(backgroundColor: scheme.surface, elevation: 0,
        centerTitle: false, scrolledUnderElevation: 0),
      inputDecorationTheme: InputDecorationTheme(filled: true,
        fillColor: scheme.surfaceContainerLow,
        border: OutlineInputBorder(borderRadius: BorderRadius.circular(10)),
        contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 17)),
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

  @override
  void initState() { super.initState(); execute(Operation.snapshot); }

  @override
  void dispose() {
    openingPoll?.cancel();
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
        if (result.stage != Stage.students) studentSearch.clear();
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

  Widget home(SchoolSession s) => Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
    heading('Bienvenue, ${s.user}', [s.school, s.year].where((v) => v.isNotEmpty).join(' · ')),
    Text('Espace de travail', style: Theme.of(context).textTheme.titleMedium),
    const SizedBox(height: 12),
    LayoutBuilder(builder: (context, constraints) {
      final width = constraints.maxWidth;
      final columns = width >= 680 ? 3 : width >= 330 ? 2 : 1;
      final allowed = moduleLabels.entries.where((e) => s.modules.contains(e.key)).toList();
      if (allowed.isEmpty) return const Text('Aucune fonctionnalité autorisée pour ce compte.');
      return Wrap(spacing: 12, runSpacing: 12, children: [
        for (final entry in allowed)
          SizedBox(width: (width - 12 * (columns - 1)) / columns,
            child: ModuleTile(label: entry.value.$1, icon: entry.value.$2,
              onTap: busy ? null : () => entry.key == 'students'
                ? listStudents(1, '') : entry.key == 'monthly' ? listMonthly()
                : execute(Operation.openModule, {'module': entry.key}))),
      ]);
    }),
    const SizedBox(height: 24),
    action('Se déconnecter', Icons.logout, Operation.signOut),
  ]);

  Future<void> listStudents(int page, String query) =>
    execute(Operation.listStudents, {'page': page.toString(), 'q': query.trim()});

  Widget students(StudentListing listing) => Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
    Align(alignment: Alignment.centerLeft, child: TextButton.icon(
      onPressed: busy ? null : () => execute(Operation.snapshot),
      icon: const Icon(Icons.arrow_back), label: const Text('Retour à l’accueil'))),
    heading(listing.title, listing.subtitle),
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
    return Scaffold(
      appBar: AppBar(title: const Text('SCOLIA Pro'),
        actions: [IconButton(tooltip: 'Actualiser', onPressed: busy ? null :
          () => listing != null ? listStudents(listing.page, listing.search)
            : payments != null ? listMonthly(current: payments, page: payments.page)
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
    final shape = RoundedRectangleBorder(borderRadius: BorderRadius.circular(10),
      side: BorderSide(color: c.outlineVariant));
    return Material(color: c.surfaceContainerLow, shape: shape, clipBehavior: Clip.antiAlias,
      child: InkWell(onTap: onTap, child: Padding(padding: const EdgeInsets.all(12),
        child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          Container(height: 62, alignment: Alignment.center,
            decoration: BoxDecoration(color: c.secondaryContainer, borderRadius: BorderRadius.circular(6)),
            child: Icon(icon, color: c.onSecondaryContainer, size: 30)),
          const SizedBox(height: 10),
          Text(label, style: const TextStyle(fontWeight: FontWeight.w600),
            maxLines: 2, overflow: TextOverflow.ellipsis),
        ]),
      )));
  }
}
