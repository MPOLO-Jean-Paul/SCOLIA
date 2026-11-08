namespace Scolia.Bureau.Infrastructure;

/// <summary>Une fonction de SCOLIA décrite en une phrase : ce qu'elle fait et où la trouver.</summary>
public sealed record CatalogEntry(string Title, string Text);

/// <summary>Un module du catalogue : son nom, son icône, sa couleur, un résumé et ses fonctions.</summary>
public sealed record CatalogModule(string Title, string Icon, string Tone, string Summary, IReadOnlyList<CatalogEntry> Entries);

/// <summary>
/// Catalogue du fonctionnement général de SCOLIA Pro, affiché dans « Info sur l'application ». Il décrit ce que
/// l'application fait réellement, module par module, dans l'ordre des menus.
/// </summary>
public static class AppCatalog
{
    public static readonly IReadOnlyList<CatalogModule> Modules =
    [
        new("Principe général", "InformationOutline", "ToneBlue",
            "Un ordinateur principal garde les données de l'établissement ; les autres ordinateurs et les téléphones travaillent dessus, en direct.",
            [
                new("Ordinateur principal", "Il héberge la base de données, les sauvegardes et le rapport journalier. Il peut être un ordinateur Windows ou, sans ordinateur, un téléphone Android."),
                new("Postes et téléphones", "Les autres ordinateurs, les téléphones Android et les iPhone se connectent à l'ordinateur principal, sur le Wi-Fi de l'établissement ou à distance avec Tailscale."),
                new("Travail en direct", "Ce qui est enregistré sur un poste apparaît aussitôt sur les autres. Deux personnes ne peuvent pas encaisser le même apprenant en même temps."),
                new("Hors ligne", "Si l'ordinateur principal est injoignable, le dernier compte connecté continue sur les données gardées de l'appareil ; les opérations partent au retour du réseau."),
                new("Comptes", "Secrétariat (administration complète), Comptabilité (finances), Promoteur (consultation des indicateurs seulement) et Enseignant (encodage et remise des cotes de ses cours). Chaque compte ne voit que ce que son rôle permet."),
            ]),
        new("Installation et connexion", "LoginVariant", "ToneIndigo",
            "La première ouverture guide la configuration, puis chaque personne se connecte avec son propre code.",
            [
                new("Assistant d'installation", "Nom, logo et coordonnées de l'établissement, année scolaire, mois de début et de fin, promotions et frais : tout se règle en quelques écrans."),
                new("Code PIN ou mot de passe", "Chacun choisit un code PIN (6 chiffres ou plus) ou un mot de passe (8 caractères ou plus). Un code provisoire donné par le secrétariat doit être remplacé à la première connexion."),
                new("Empreinte et Face ID", "Sur le téléphone, l'empreinte ou Face ID ouvre SCOLIA sans saisir le code. Le téléphone garde l'accès dans sa puce de sécurité ; une nouvelle empreinte enregistrée le désactive."),
                new("Protection", "Après cinq erreurs de code, la connexion se bloque deux minutes puis revient seule. Personne, pas même le secrétariat, ne voit les codes."),
                new("Mise en veille", "Sans activité pendant le délai choisi (1 à 30 minutes, réglé dans Sécurité du compte), ou au retour après ce temps en arrière-plan, SCOLIA se verrouille : le code ou l'empreinte est redemandé, et la session reprend là où elle était. « Changer de compte » ouvre la connexion sans fermer la session verrouillée : « Revenir à la session de… », le geste retour ou Échap y ramènent."),
            ]),
        new("Accueil", "HomeOutline", "ToneBlue",
            "La journée en un coup d'œil, avec les actions les plus fréquentes.",
            [
                new("Chiffres du jour", "Encaissé aujourd'hui et ce mois, apprenants actifs, taux de recouvrement et apprenants en retard."),
                new("Actions rapides", "Encaisser, inscrire, voir les retards et les rapports en un toucher."),
                new("Aujourd'hui", "Caisse à arrêter, appel à faire, renvois en cours, convocations de parents et anniversaires des apprenants."),
                new("Réseau", "Qui travaille sur quel poste, et ce qui attend d'être envoyé."),
            ]),
        new("Pilotage", "ChartBox", "ToneBlue",
            "Indicateurs et documents pour suivre l'établissement.",
            [
                new("Tableau de bord", "Encaissé, attendu à ce jour, reste à payer, taux de recouvrement, évolution mensuelle, ancienneté des retards et situation de chaque promotion."),
                new("Rapports et exports", "Situation par promotion, journal des encaissements, suivi mensuel, retards de paiement, dépenses, listes d'apprenants et de classe, discipline et audit, en PDF ou Excel."),
                new("Rapport journalier", "Chaque jour : encaissements, dépenses, inscriptions et absences, détaillés par promotion, en PDF. Il peut partir tout seul à l'heure choisie, par WhatsApp aux numéros et par e-mail aux adresses indiqués (« Planifier »)."),
                new("Espace promoteur", "Le promoteur voit les indicateurs agrégés, jamais les dossiers nominatifs."),
            ]),
        new("Secrétariat", "AccountGroup", "ToneIndigo",
            "Les dossiers des apprenants, de l'inscription à la carte d'accès.",
            [
                new("Apprenants", "Inscription avec les champs obligatoires, matricule automatique, photo, contacts du parent, fiche détaillée et relevé de compte. Renommer ou supprimer d'un clic droit (ou d'un appui long)."),
                new("Réinscriptions", "Passage d'une année à la suivante, promotion par promotion."),
                new("Cartes d'accès", "Cartes d'examen en PDF ou Word, huit par page, avec QR code, couleurs et session, et historique des lots imprimés."),
            ]),
        new("Finances", "CashMultiple", "ToneGreen",
            "Tout l'argent qui entre et qui sort, avec des reçus vérifiables.",
            [
                new("Encaisser", "Choisir l'apprenant, cocher les mois à payer jusqu'à la date limite, puis valider : le reçu numéroté avec QR code est prêt à imprimer ou à partager."),
                new("Encaissement par lot", "Plusieurs apprenants payés en une seule opération, avec le détail ligne par ligne."),
                new("Journal des reçus", "Tous les reçus, leur recherche, et l'annulation motivée d'un reçu (jamais effacé)."),
                new("Suivi mensuel", "Pour chaque mois, qui a payé et qui reste à payer, par promotion."),
                new("Recouvrement", "Les apprenants en retard, l'ancienneté de la dette et la relance du parent par WhatsApp ou SMS, avec un message déjà rédigé."),
                new("Caisse", "Arrêté de caisse journalier : ce qui est encaissé, dépensé et compté."),
                new("Dépenses", "Sorties de caisse et de banque, numérotées et justifiées."),
                new("Prises en charge", "Bourses, réductions et prises en charge par un tiers, déduites des frais de l'apprenant."),
            ]),
        new("Scolarité", "School", "TonePurple",
            "La vie de classe : cours, notes, présences et discipline.",
            [
                new("Cours et notes", "Cours par promotion, évaluations et saisie des notes."),
                new("Résultats", "Moyennes, rangs et bulletins imprimables."),
                new("Présences", "Appel de la journée ; un jour de cours sans appel devient une absence non signée, les retards sont convertis par semaine, les absences peuvent être justifiées."),
                new("Discipline", "Incidents, renvois temporaires et définitifs (règle des deux renvois), convocations de parents imprimables et suivi de leur venue."),
            ]),
        new("Gestion des délibérations", "Gavel", "ToneRed",
            "Tout ce qui mène au jury, dans l'ordre du travail : enseignants et cours, préparation, encodage, délibération.",
            [
                new("Attribution des cours", "Cours de chaque promotion avec maxima, pondération et semestre, et l'enseignant qui en a la charge ; historique et charges horaires."),
                new("Comptes enseignants", "« Nouvel enseignant » crée la fiche et le compte en une fois : identifiant « prénom.nom » proposé et code provisoire généré, montré une seule fois. L'enseignant le remplace à sa première connexion ; avant cela, il ne peut ouvrir aucune feuille. Nouveau code en cas d'oubli, privilège jury et désactivation sur la même page."),
                new("Paramètres de délibération", "Rangés par blocs : encodage par les enseignants, règles, autorités, jury et épreuves du jury."),
                new("Codes anonymes", "Les apprenants déjà inscrits reçoivent un code officiel ; les copies et les feuilles des enseignants ne portent que ce code."),
                new("Encodage et suivi", "Cotes du S1, du S2, moyennes d'appui et épreuves du jury ; avancement par cours et par enseignant, feuilles remises et à relancer."),
                new("Délibération", "Résultats, décisions motivées, clôture, procès-verbal, grille, palmarès et bulletins avec QR de vérification."),
            ]),
        new("Espace enseignant", "AccountSchool", "TonePurple",
            "Ce que voit un compte enseignant : son métier, rien d'autre.",
            [
                new("Mon espace", "Ses cours et leur avancement, les feuilles remises, les annonces de la délibération et les règles de l'encodage en vigueur."),
                new("Mes cours et cotes", "Feuilles anonymes de ses seuls cours (codes, jamais les noms) : saisie, saisie par code, reprise d'un fichier, remise au jury."),
                new("Cloisonnement", "Aucun accès aux dossiers des apprenants, aux finances, aux autres cours ni aux résultats ; le serveur le vérifie à chaque demande."),
            ]),
        new("Personnel", "BadgeAccountHorizontal", "ToneTeal",
            "Les membres du personnel, leur pointage et leur paie.",
            [
                new("Personnel et paie", "Dossiers du personnel, salaires de base et bulletins de paie du mois."),
                new("Pointage", "Entrée et sortie de chaque membre, retard compté après 08:00, signatures figées et bilan par période."),
            ]),
        new("Paramètres", "CogOutline", "ToneNeutral",
            "Les règles de l'établissement et les préférences de chaque poste.",
            [
                new("Configuration", "Établissement et logo (toute image acceptée), années scolaires, sections, promotions et frais, calendrier et date limite de paiement."),
                new("Saisie prédictive", "Le code INF1 propose « Première Infirmière » puis les autres niveaux ; ACC propose les accoucheuses. Un choix remplit le code, le niveau, la promotion suivante et la section. Pour une année, « 2026 » donne « 2026-2027 » et le dernier mois suit le premier."),
                new("Utilisateurs", "Création des comptes, rôles et remise d'un code provisoire."),
                new("Préférences du poste", "Thème clair ou sombre, couleur, police et taille du texte, animations, sons et notifications : propres à chaque appareil."),
                new("Mon compte", "Sécurité du compte (code et empreinte), poste et réseau, déconnexion."),
            ]),
        new("Données et sécurité", "DatabaseLock", "ToneRed",
            "Rien ne se perd : copies vérifiées, traces de chaque opération.",
            [
                new("Sauvegardes", "Copie automatique régulière et copie manuelle à la demande, chacune vérifiée par son empreinte ; restauration en un clic, la base actuelle étant d'abord conservée."),
                new("Journal d'audit", "Chaque opération : qui, quand, quoi, depuis quel poste, dans une chaîne d'empreintes vérifiable."),
                new("Effacement des données", "Effacer les paiements ou les apprenants d'essai, toujours après une sauvegarde qui permet de revenir en arrière."),
                new("Import et clôture", "Import du classeur Excel de SCOLIA, modèle d'import, et clôture de l'année scolaire."),
            ]),
        new("Notifications", "BellRingOutline", "ToneOrange",
            "Être prévenu de ce qui se passe sur les autres postes.",
            [
                new("Bannières", "Un encaissement, un reçu annulé, une sauvegarde ou le rapport du jour fait ailleurs s'annonce par une bannière en haut de l'écran ; la toucher ouvre ce dont elle parle."),
                new("Notifications du téléphone", "Quand SCOLIA est en arrière-plan, la même alerte arrive dans les notifications d'Android ou de l'iPhone."),
                new("Réglages", "Chaque sorte d'alerte se coupe dans Paramètres, Sons, rubrique Notifications ; le bouton « Essayer » montre un exemple."),
            ]),
        new("Recherche et navigation", "Magnify", "ToneBlue",
            "Trouver vite, comme sur un téléphone moderne.",
            [
                new("Recherche générale", "Façon Google : apprenants, matricules, reçus, fonctions et réglages en un seul champ, avec les recherches récentes et des accès rapides."),
                new("Recherche à la voix", "Sur le téléphone, le micro de chaque recherche écoute en français : dites un nom, un matricule ou une fonction. Sans Internet, l'écoute se fait dans le téléphone quand il le permet (Android 12 et plus, iPhone). Sur Windows, la dictée Windows+H écrit dans le champ."),
                new("Filtres", "Sur chaque liste, la loupe ouvre la même barre que la recherche de l'accueil et filtre uniquement les données de l'écran."),
                new("Raccourcis", "Ctrl+K pour chercher, raccourcis clavier par onglet, menus contextuels et double-clic sur les lignes."),
                new("Téléphone", "Barre d'onglets en bas, menu complet, retour par le geste d'Android, champs qui remontent au-dessus du clavier."),
            ]),
    ];

    /// <summary>Filtre sans accents ni majuscules : « recu » trouve « Reçu ».</summary>
    public static bool Matches(string query, CatalogModule module, CatalogEntry entry)
    {
        var terms = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var text = Normalize($"{module.Title} {entry.Title} {entry.Text}");
        return terms.All(t => text.Contains(t, StringComparison.Ordinal));
    }

    private static string Normalize(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        return new string(decomposed.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
    }
}
