using System.Globalization;

namespace Scolia.Bureau.Infrastructure;

/// <summary>Mise en forme française des montants, dates et libellés.</summary>
public static class Format
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public static string Money(decimal value, string symbol) => $"{value.ToString("N0", Fr)} {symbol}";

    public static string Amount(decimal value) => value.ToString("N0", Fr);

    public static string Date(DateOnly date) => date.ToString("dd/MM/yyyy", Fr);
    /// <summary>Date en toutes lettres, avec la majuscule du jour : « Samedi 25 octobre 2026 ».</summary>
    public static string LongDate(DateOnly date) { var t = date.ToString("dddd d MMMM yyyy", Fr); return char.ToUpper(t[0], Fr) + t[1..]; }

    public static string DateTime(DateTime utc) => utc.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Fr);

    public static string Percent(decimal value) => $"{value.ToString("0.0", Fr)} %";

    public static string Count(int value, string singular, string plural) => $"{value.ToString("N0", Fr)} {(value > 1 ? plural : singular)}";

    public static string FullName(string last, string middle, string first) =>
        string.Join(' ', new[] { last, middle, first }.Where(s => !string.IsNullOrWhiteSpace(s)));

    /// <summary>Lit un montant saisi à la française : espaces et virgule décimale acceptés.</summary>
    public static bool TryAmount(string? text, out decimal value)
    {
        // Toutes les espaces, y compris insécables et fines, sont des séparateurs de milliers.
        var digits = new string((text ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray()).Replace(',', '.');
        return decimal.TryParse(digits, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Lit une date saisie au format jour/mois/année ou année-mois-jour.</summary>
    public static bool TryDay(string? text, out DateOnly day)
    {
        var formats = new[] { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd" };
        return DateOnly.TryParseExact((text ?? string.Empty).Trim(), formats, Fr, DateTimeStyles.None, out day);
    }
}

/// <summary>Libellés affichés pour les codes échangés avec le serveur.</summary>
public static class Labels
{
    /// <summary>Organisation d'un cours pour la délibération (ITMCD) : un semestre ou toute l'année.</summary>
    public static readonly IReadOnlyList<ChoiceItem> Semesters =
        [new("S1", "Premier semestre (S1)"), new("S2", "Second semestre (S2)"), new("Annuel", "Annuel (S1 et S2)"),
         new("JuryEpreuve", "Épreuve du jury (TOTAL 1)"), new("JuryBloc", "Bloc du jury (SOUS-TOTAL 2)")];

    /// <summary>Périodes délibérées : S1, S2, puis le jury annuel.</summary>
    public static readonly IReadOnlyList<ChoiceItem> DeliberationPeriods =
        [new("S1", "Premier semestre"), new("S2", "Second semestre"), new("Annuel", "Jury annuel")];

    // Cours : deux nombres distincts, à ne jamais confondre (fusion ITMCD / projet Délibération).
    public const string WeightLabel = "Pondération (coefficient)";
    public const string WeightHint = "Poids du cours dans la moyenne sur 20 des bulletins de « Cours et notes ». La délibération ne l'utilise pas.";
    public const string MaxScoreLabel = "Maxima du cours (points)";
    public const string MaxScoreHint = "Total de points du cours pour la délibération : cotes de 0 à ce maxima, échec sous la moitié. Vide : pondération × 10 (convention ITM).";

    /// <summary>Maxima saisi, ou à défaut la convention ITM : pondération × 10.</summary>
    public static decimal MaxScore(ViewModels.FormViewModel f) =>
        string.IsNullOrWhiteSpace(f.Text("cotation")) ? f.Amount("coefficient") * 10 : f.Amount("cotation");

    public static string Semester(string code) => code switch { "S1" => "S1", "S2" => "S2", "JuryEpreuve" => "Épreuve jury", "JuryBloc" => "Bloc jury", _ => "Annuel" };

    public static string Status(string code) => code switch
    {
        "Actif" => "Actif",
        "Suspendu" => "Suspendu",
        "Sorti" => "Sorti",
        "Diplome" => "Diplômé",
        "Transfere" => "Transféré",
        "Annule" => "Annulé",
        "Valide" => "Validé",
        "Brouillon" => "Brouillon",
        "Paye" => "Payé",
        "Impaye" => "Impayé",
        "Partiel" => "Partiel",
        "Couvert" => "Couvert",
        "Preparation" => "Préparation",
        "Open" => "Ouverte",
        "Closed" => "Clôturée",
        _ => code,
    };

    /// <summary>Action du journal d'audit, lisible : « connexion.reussie » devient « Connexion réussie ».</summary>
    public static string AuditAction(string code) => AuditActions.TryGetValue(code, out var label) ? label
        : code.Length == 0 ? code : char.ToUpperInvariant(code[0]) + code[1..].Replace('.', ' ').Replace('_', ' ');

    private static readonly Dictionary<string, string> AuditActions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["annee.cloturee"] = "Année clôturée", ["annee.courante"] = "Année courante choisie", ["annee.creee"] = "Année créée",
        ["apprenant.inscrit"] = "Apprenant inscrit", ["apprenant.modifie"] = "Apprenant modifié", ["apprenant.photo"] = "Photo de l'apprenant",
        ["apprenant.reinscrit"] = "Apprenant réinscrit", ["apprenant.renomme"] = "Apprenant renommé", ["apprenant.supprime"] = "Apprenant supprimé",
        ["audit.exporte"] = "Journal exporté", ["bulletin.pdf"] = "Bulletin imprimé", ["caisse.arretee"] = "Caisse arrêtée",
        ["cartes.export"] = "Cartes exportées", ["connexion.echec"] = "Connexion refusée", ["connexion.reussie"] = "Connexion réussie",
        ["connexion.verrouillage"] = "Compte bloqué", ["cours.archive"] = "Cours archivé", ["cours.attribue"] = "Cours attribué",
        ["cours.cree"] = "Cours créé", ["cours.modifie"] = "Cours modifié", ["cours.reactive"] = "Cours réactivé", ["cours.retrait"] = "Attribution retirée",
        ["depense.annulee"] = "Dépense annulée", ["depense.creee"] = "Dépense enregistrée",
        ["discipline.annulation"] = "Décision annulée", ["discipline.csv"] = "Discipline exportée", ["discipline.decision"] = "Décision disciplinaire",
        ["discipline.incident"] = "Incident signalé", ["discipline.reception"] = "Parent reçu",
        ["etablissement.logo"] = "Logo changé", ["etablissement.modifie"] = "Établissement modifié",
        ["evaluation.creee"] = "Évaluation créée", ["evaluation.modifiee"] = "Évaluation modifiée", ["import.scolia"] = "Classeur importé",
        ["inscription.promotion"] = "Changement de promotion", ["inscription.statut"] = "Statut changé", ["notes.saisies"] = "Notes saisies",
        ["paie.modifiee"] = "Paie modifiée", ["paie.payee"] = "Salaire payé", ["paie.preparee"] = "Paie préparée",
        ["paiement.annule"] = "Reçu annulé", ["paiement.encaisse"] = "Encaissement", ["paiement.lot"] = "Encaissement en lot",
        ["personnel.cree"] = "Membre ajouté", ["personnel.modifie"] = "Membre modifié",
        ["pointage.annulation"] = "Pointage annulé", ["pointage.justification"] = "Absence justifiée", ["pointage.signature"] = "Pointage signé",
        ["presences.jours"] = "Jours de cours", ["presences.saisies"] = "Appel enregistré",
        ["priseencharge.creee"] = "Prise en charge créée", ["priseencharge.modifiee"] = "Prise en charge modifiée", ["priseencharge.terminee"] = "Prise en charge terminée",
        ["promotion.creee"] = "Promotion créée", ["promotion.modifiee"] = "Promotion modifiée", ["promotion.supprimee"] = "Promotion supprimée",
        ["recu.pdf"] = "Reçu imprimé", ["reinscription.lot"] = "Réinscriptions", ["releve.pdf"] = "Relevé imprimé",
        ["sauvegarde.creee"] = "Sauvegarde faite", ["sauvegarde.nettoyage"] = "Anciennes sauvegardes retirées", ["sauvegarde.restauree"] = "Sauvegarde restaurée",
        ["section.creee"] = "Section créée", ["section.modifiee"] = "Section modifiée",
        ["utilisateur.cree"] = "Compte créé", ["utilisateur.modifie"] = "Compte modifié", ["utilisateur.motdepasse"] = "Mot de passe changé",
        ["utilisateur.jury.accorde"] = "Privilège de jury", ["utilisateur.recuperation"] = "Mot de passe réinitialisé",
        ["annee.calendrier.modifie"] = "Calendrier modifié", ["installation"] = "Installation de SCOLIA",
        ["deliberation.autorites"] = "Autorités enregistrées", ["deliberation.cloturee"] = "Délibération clôturée",
        ["deliberation.code.attribue"] = "Code anonyme attribué", ["deliberation.code.retire"] = "Code anonyme retiré",
        ["deliberation.code.remplace"] = "Code anonyme remplacé", ["deliberation.codes.attribues"] = "Codes anonymes attribués",
        ["deliberation.codes.importes"] = "Codes anonymes importés", ["deliberation.cote.anonyme"] = "Cote saisie par code",
        ["deliberation.cotes.importees"] = "Cotes reprises", ["deliberation.cotes.remises"] = "Cotes remises",
        ["deliberation.cotes.renvoyees"] = "Cotes renvoyées", ["deliberation.cotes.saisies"] = "Cotes enregistrées",
        ["deliberation.decision"] = "Décision du jury", ["deliberation.decision.retiree"] = "Décision du jury retirée",
        ["deliberation.epreuves.creees"] = "Épreuves créées", ["deliberation.ouverte"] = "Délibération ouverte",
        ["deliberation.reglages"] = "Paramètres de délibération", ["deliberation.rouverte"] = "Délibération rouverte",
        ["donnees.apprenants.effaces"] = "Dossiers effacés", ["donnees.effacement.refuse"] = "Effacement refusé",
        ["donnees.paiements.effaces"] = "Paiements effacés", ["enseignant.compte.reactive"] = "Compte enseignant réactivé",
        ["frais.categorie.creee"] = "Catégorie de frais créée", ["frais.categorie.supprimee"] = "Catégorie de frais supprimée",
        ["frais.categories.reprise"] = "Catégories de frais reprises", ["nuage.active"] = "Sauvegarde en ligne",
        ["nuage.code.consulte"] = "Code de secours consulté", ["nuage.recuperee"] = "Copie en ligne récupérée",
        ["presences.jours.retrait"] = "Jour de cours retiré", ["promotion.creee.reinscription"] = "Promotion créée (réinscription)",
        ["rapport.journalier.reglages"] = "Rapport du jour réglé", ["section.supprimee"] = "Section supprimée",
    };

    /// <summary>Objet du journal d'audit, lisible : « CodeAnonyme » devient « Code anonyme ».</summary>
    public static string AuditEntity(string code) => code switch
    {
        "AnneeScolaire" => "Année scolaire", "ArreteCaisse" => "Arrêté de caisse", "CarteAcces" => "Carte d'accès",
        "CategorieFrais" => "Catégorie de frais", "CodeAnonyme" => "Code anonyme", "Depense" => "Dépense",
        "Etablissement" => "Établissement", "Evaluation" => "Évaluation", "Presence" => "Présence",
        "PriseEnCharge" => "Prise en charge", "RapportJournalier" => "Rapport du jour", "Recu" => "Reçu",
        "RegistreDiscipline" => "Discipline", "ReglagesDeliberation" => "Délibération", "RemiseCotes" => "Remise des cotes",
        "SessionDeliberation" => "Délibération", "Nuage" => "Sauvegarde en ligne",
        _ => code,
    };

    public static string Method(string code) => code switch
    {
        "Especes" => "Espèces",
        "MobileMoney" => "Mobile Money",
        "Banque" => "Banque",
        "Cheque" => "Chèque",
        "Autre" => "Autre",
        _ => code,
    };

    public static string Gender(string code) => code == "F" ? "Féminin" : "Masculin";

    public static string Severity(string code) => code switch
    {
        "Mineur" => "Mineure",
        "Moyen" => "Moyenne",
        _ => code,
    };

    public static string Kind(string code) => code switch
    {
        "Interrogation" => "Interrogation",
        "Devoir" => "Devoir",
        "TravauxPratiques" => "Travaux pratiques",
        "Examen" => "Examen",
        _ => code,
    };

    /// <summary>Tonalité d'un statut : vert pour le sain, orange pour l'attention, rouge pour l'échec.</summary>
    public static string Tone(string code) => code switch
    {
        // Codes du serveur, et libellés déjà traduits (« Payé », « Pris en charge ») qu'envoient les comptes.
        "Valide" or "Actif" or "Paye" or "Couvert" or "Open" or "Payé" or "Pris en charge" => "green",
        "Annule" or "Sorti" or "Grave" or "Impaye" or "Impayé" or "Non payé" => "red",
        "Suspendu" or "Partiel" or "Moyen" or "Retard" or "Brouillon" => "orange",
        _ => "neutral",
    };

    public static readonly IReadOnlyList<ChoiceItem> Methods =
    [
        new("Especes", "Espèces"), new("MobileMoney", "Mobile Money"), new("Banque", "Banque"), new("Cheque", "Chèque"),
    ];

    public static readonly IReadOnlyList<ChoiceItem> Genders = [new("M", "Masculin"), new("F", "Féminin")];

    public static readonly IReadOnlyList<ChoiceItem> Statuses =
    [
        new("Actif", "Actif"), new("Suspendu", "Suspendu"), new("Sorti", "Sorti"), new("Diplome", "Diplômé"), new("Transfere", "Transféré"),
    ];

    public static readonly IReadOnlyList<ChoiceItem> Attendance =
    [
        new("Présent", "Présent"), new("Retard", "Retard"), new("Absent", "Absent"), new("Absence justifiée", "Justifié"),
    ];

    public static readonly IReadOnlyList<ChoiceItem> Severities =
    [
        new("Mineur", "Mineure"), new("Moyen", "Moyenne"), new("Grave", "Grave"),
    ];

    public static readonly IReadOnlyList<ChoiceItem> AssessmentKinds =
    [
        new("Interrogation", "Interrogation"), new("Devoir", "Devoir"), new("TravauxPratiques", "Travaux pratiques"), new("Examen", "Examen"),
    ];

    public static readonly IReadOnlyList<ChoiceItem> Roles =
    [
        // Rôle de direction : consultation des statistiques, sans droit de modifier les données.
        new("Secretariat", "Secrétariat"), new("Comptabilite", "Comptabilité"),
        new("Promoteur", "Promoteur / Responsable"),
        new("Enseignant", "Enseignant (encodage de ses cours)"),
    ];

    public static readonly IReadOnlyList<ChoiceItem> PayrollModes =
    [
        new("Especes", "Espèces"), new("Banque", "Banque"), new("MobileMoney", "Mobile Money"), new("Cheque", "Chèque"),
    ];

    public static readonly IReadOnlyList<ChoiceItem> Buckets =
    [
        new("", "Toutes"), new("0-30 jours", "0-30 jours"), new("31-60 jours", "31-60 jours"), new("61-90 jours", "61-90 jours"), new("Plus de 90 jours", "Plus de 90 jours"),
    ];
}
