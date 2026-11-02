# ITMCD — analyse complète du fonctionnement actuel

Analyse du dépôt `MPOLO-Jean-Paul/ITMCD` (branche `main`, commit `56bbafe`), réalisée par lecture
intégrale du code PHP. Aucune base de données n'était disponible : la structure des tables est
reconstituée à partir des requêtes SQL, et aucun comportement n'a été exécuté ni testé.

Objectif : servir de référence pour réécrire ITMCD sur le modèle de SCOLIA Pro (serveur .NET local,
base SQLite, clients natifs Windows, Android et iOS, accès à distance par Tailscale), **en conservant
toutes les règles de délibération actuelles**.

---

## 1. Vue d'ensemble

| Élément | Valeur actuelle |
|---|---|
| Nature | Application web PHP 100 % serveur, pages rendues côté serveur, sans framework |
| Base | MySQL (PDO `pdo_mysql`, utf8mb4) |
| Hébergement | InfinityFree (détection automatique du domaine dans `config/env.php`) ; mode local `127.0.0.1` |
| Mobile | PWA : `manifest.json`, `service-worker.js`, page `offline.html`, notifications Web Push (VAPID) |
| Établissement | Institut Technique Médical Cœur de Dieu (ITMCD), Lubumbashi, RDC, Ministère de la Santé Publique |
| Taille | 40 fichiers PHP, environ 12 700 lignes avec l'interface |
| Rôles | `admin` et `enseignant` uniquement |

Arborescence :

```
login.php, logout.php, index.php (redirige vers login)
config/db.php                 connexion, sécurité, TOUTES les règles de calcul, notifications, push
config/env.php                paramètres de connexion MySQL (local / InfinityFree)
admin/                        20 fichiers de l'administration (secrétariat académique, jury)
enseignant/                   10 fichiers de l'espace enseignant
assets/                       app-shell.js / .css, app-modern.css (thème, menu, PWA)
service-worker.js, manifest.json, offline.html
enseignant.zip                ancienne copie du dossier enseignant (à ne pas reprendre)
```

---

## 2. Modèle de données reconstitué

Les tables principales ne sont créées nulle part dans le code : elles viennent d'un export SQL
absent du dépôt (`*.sql` est ignoré par Git). Colonnes déduites des requêtes :

| Table | Colonnes utilisées | Remarques |
|---|---|---|
| `utilisateurs` | `id`, `nom`, `email` (unique), `mot_de_passe` (hash bcrypt), `role` (`admin` / `enseignant`), `remember_token` (SHA-256) | Un seul jeton « se souvenir de moi » par compte |
| `annees_scolaires` | `id`, `libelle` (ex. `2025-2026`), `est_active` (0/1) | Une seule année active à la fois |
| `promotions` | `id`, `nom` (en majuscules, unique applicatif), `id_promotion_suivante` | Sert au passage de classe |
| `apprenants` | `id`, `nom`, `post_nom`, `prenom`, `code_unique`, `promotion_id`, `annee_scolaire_id`, `decision_s1`, `decision_s2`, `decision_jury` | **Une ligne par apprenant et par année** (nouvelle ligne à chaque reconduction) |
| `cours` (appelés « éléments » à l'écran) | `id`, `nom`, `coefficient`, `promotion_id`, `enseignant_id`, `semestre` (`S1`, `S2`, `ANNUEL`) | `coefficient` est en réalité **le maxima** du cours (1 à 2000) |
| `notes` | `id`, `apprenant_id`, `cours_id`, `enseignant_id`, `type_note`, `valeur`, `date_encodage` | Clé unique probable `(apprenant_id, cours_id, type_note)` (utilisation de `ON DUPLICATE KEY`) |
| `attributions` | `id`, `enseignant_id`, `cours_id` | Table héritée : n'est utilisée par aucun calcul, l'enseignant d'un cours est `cours.enseignant_id` |
| `parametres_app` | `cle`, `valeur`, `updated_at` | Créée automatiquement ; interrupteurs fonctionnels |
| `notifications` | `id`, `message`, `est_lu`, `target_role`, `target_user_id`, `type`, `cree_le` | Créée et complétée automatiquement |
| `push_subscriptions` | `id`, `user_id`, `endpoint`, `endpoint_hash`, `p256dh`, `auth`, dates | Abonnements Web Push |

Types de note (`notes.type_note`) :

| Type | Échelle | Rôle |
|---|---|---|
| `cote_s1` | 0 à maxima du cours | Cote du semestre 1 (type actuel) |
| `cote_s2` | 0 à maxima du cours | Cote du semestre 2 (type actuel) |
| `cote` | 0 à maxima | Ancienne cote unique (héritage, encore lue) |
| `examen` | 0 à 10 | Ancien examen sur 10 (héritage, converti en points) |
| `moyenne` | 0 à 10 | « Moyenne de secours » qui peut relever une cote faible |

---

## 3. Règles de calcul (cœur de la délibération)

Toutes ces règles sont dans `config/db.php`. Elles doivent être reprises **à l'identique** dans le
domaine métier de la nouvelle application, avec des tests unitaires.

### 3.1 Cote d'un cours pour une période

- Un cours `S1` ne reçoit qu'une cote S1 ; un cours `S2` qu'une cote S2 ; un cours `ANNUEL` reçoit les deux.
- Cote retenue pour **S1** : `cote_s1`, à défaut l'ancienne `cote`.
- Cote retenue pour **S2** : `cote_s2`, à défaut l'ancienne `cote`.
- Cote retenue pour le **résultat final (jury)** :
  - cours `ANNUEL` : moyenne `(S1 + S2) / 2`, où S1 = `cote_s1` ou `cote` ou 0, et S2 = `cote_s2` ou 0 ;
  - cours `S1` ou `S2` : la cote de son semestre.
- Si aucune cote directe n'existe mais qu'un ancien `examen` existe : points = `examen / 10 × maxima`.

### 3.2 Moyenne de secours

Pour une cote de base `B` sur un maxima `M`, avec une moyenne de secours `m` sur 10 :

- si `B > 75 % de M` : aucune modification ;
- sinon : `B + (m / 10) × (25 % de M)`, plafonné à `M`.

Exemple : maxima 40, cote 20, moyenne 8 → 20 + 0,8 × 10 = 28.

### 3.3 Réussite d'un cours

Un cours est en échec si la cote est présente et que les points sont **inférieurs à la moitié du maxima**.

### 3.4 Décision d'un apprenant

Pour une période (S1, S2 ou final) :

- cours pris en compte : S1 → cours `S1` et `ANNUEL` ; S2 → cours `S2` et `ANNUEL` ; final → tous ;
- pourcentage = total des points / total des maxima × 100, arrondi à 2 décimales ;
- **ADMIS** si le pourcentage est **≥ 60 %** et qu'**aucune cote ne manque** ; sinon **ECHEC** ;
- la décision est enregistrée dans `decision_s1`, `decision_s2` ou `decision_jury`.

### 3.5 Mention (bulletin)

| Pourcentage | Mention |
|---|---|
| ≥ 90 | A |
| ≥ 80 | B |
| ≥ 70 | C |
| ≥ 60 | D |
| sinon | E |

### 3.6 Recalcul automatique

Chaque encodage (cote ou moyenne), par l'enseignant comme par l'administration, recalcule
immédiatement les trois décisions (S1, S2, final) de l'apprenant concerné.

---

## 4. Parcours et écrans

### 4.1 Connexion (`login.php`)

- Identifiant : adresse e-mail et mot de passe (bcrypt), protection CSRF.
- Option « se souvenir de moi » : jeton aléatoire de 30 jours, stocké haché.
- Redirection selon le rôle vers `admin/index.php` ou `enseignant/index.php`.
- Déconnexion : effacement du jeton et de la session.

### 4.2 Espace administration

| Écran | Fonction |
|---|---|
| Tableau de bord (`index.php`) | Compteurs par année scolaire : apprenants, cours, enseignants, promotions ; cloche de notifications |
| Inscription (`ajouter_apprenant.php`) | Nom et post-nom en majuscules, prénom capitalisé, promotion ; refus d'un doublon exact dans l'année active ; code unique aléatoire de 6 caractères (lettre, chiffre, chiffre, lettre, chiffre, lettre) |
| Liste globale (`liste_globale.php`) | Filtre par année et promotion, modification (`edit_apprenant.php`), suppression |
| Promotions (`gestion_promotions.php`) | Création (sans doublon), suppression |
| Éléments / cours (`gestion_cours.php`) | Nom, maxima de 1 à 2000, promotion, enseignant, semestre S1/S2/ANNUEL ; pas de doublon de nom dans une promotion ; modification et suppression |
| Enseignants (`gestion_enseignants.php`) | Création de comptes enseignants (nom, e-mail, mot de passe), modification, suppression |
| Attributions (`gestion_attributions.php`) | Ancienne page, absente du menu, sans effet sur les calculs |
| Gestion des notes (`gestion_notes.php`) | Choix promotion puis apprenant ; correction des cotes S1/S2 de chaque cours ; affichage des échecs en tête ; recalcul des décisions |
| Modifier une note (`modifier_note.php`) | Correction d'une note isolée ; page reliée à aucun menu |
| Délibération (`deliberation.php`) | Voir § 4.3 |
| Bulletins (`bulletin.php`) | Par année, promotion (ou toutes) et période S1/S2/final : tableau par cours, « INC » si cote manquante, total, pourcentage, mention, décision, signatures ; impression navigateur |
| Bulletins PDF (`generate_bulletins_pdf.php`) | Capture des bulletins en PDF dans le navigateur (html2canvas + jsPDF) |
| Grille des cotes (`grille_inf1.php`) | Tableau promotion × cours : points finals, total, pourcentage, décision ; export **PDF A4 paysage** généré par le serveur et export **Excel** |
| Suivi (`monitoring.php`) | Taux de réussite par promotion, progression d'encodage par enseignant, flux des 15 dernières cotes, modification du profil admin |
| Paramètres (`parametres.php`) | Interrupteurs : encodage S1, encodage S2, moyennes de secours, mise à jour des cotes déjà saisies (désactivée par défaut), téléchargements ; chaque changement d'encodage notifie tous les enseignants |
| Années et passage (`cloture_annee.php`) | Création d'une nouvelle année active ; reconduction d'une année vers une autre (voir § 4.4) |

### 4.3 Délibération

1. Statistiques par promotion pour S1, S2 ou jury : effectif, admis, échecs, cotes manquantes, taux.
2. Statistiques par cours : réussites, échecs, cotes manquantes, seuil (moitié du maxima).
3. **Lancer la délibération** : l'administrateur ressaisit son mot de passe ; toutes les décisions
   de la période choisie sont recalculées dans une transaction.
4. **Jury annuel** : bilan global et par promotion, liste filtrable des admis ou des échecs,
   fiche d'un apprenant avec ses cours en échec ; le jury peut **corriger directement les cotes S1/S2**
   de l'apprenant, ce qui recalcule son bulletin annuel.

### 4.4 Passage à l'année suivante

- Pour chaque apprenant de l'année source, une **nouvelle ligne** est créée dans l'année cible.
- Décision jury `ADMIS` → promotion suivante (`id_promotion_suivante`) ; sinon même promotion (redoublement).
- Nouveau code au format `ITM-AA-XXXX` (AA = 2 chiffres de l'année cible).
- Les notes restent attachées à l'ancienne ligne.

### 4.5 Espace enseignant

| Écran | Fonction |
|---|---|
| Accueil (`index.php`) | Cours attribués, effectifs, cotes saisies, progression, taux de réussite |
| Encodage des cotes (`encodage_examen.php`) | Choix du cours et de la période ; saisie **par code apprenant** avec suggestion automatique ; contrôle 0 à maxima et appartenance à la promotion ; refus de remplacer une cote si la mise à jour est désactivée ; historique ; notification à l'administration |
| Moyennes de secours (`saisie_moyennes.php`) | Saisie en lot des moyennes sur 10 pour tout un cours ; aperçu base, bonus et cote ajustée ; recalcul des décisions ; notification |
| Exports (`export_notes.php`) | Relevé d'un cours par période, complet ou échecs seulement ; aperçu et fichier Excel |
| Grille des cotes (`grille_inf1.php`) | Consultation et export Excel de la grille (toutes promotions) |
| Statistiques (`stats_enseignant.php`) | Taux de réussite, moyenne générale, performance par cours, trois meilleurs apprenants, appréciation |
| Réglages (`reglages.php`) | Changement de mot de passe (ancien obligatoire, 8 caractères minimum) |
| Installation (`install.php`) | Aide à l'installation de la PWA sur le téléphone |

### 4.6 Notifications

- Un encodage d'enseignant crée une notification pour l'administration.
- Un changement d'interrupteur d'encodage crée une notification par enseignant.
- Les pages interrogent régulièrement `check_notifications.php` ; le service worker affiche une
  notification système ; un « réveil » Web Push (VAPID) est envoyé si les clés sont configurées.

---

## 5. Anomalies et risques relevés à la lecture

Ces points sont issus de la lecture du code, sans exécution. Ils seront corrigés dans la nouvelle
version, sauf décision contraire.

### Critique

1. **Mot de passe MySQL en clair dans un dépôt public.** `config/env.php` contient l'hôte,
   l'utilisateur et le mot de passe réels de la base InfinityFree. Le dépôt étant public, il faut
   **changer ce mot de passe immédiatement** dans le panneau InfinityFree, puis retirer ce fichier du
   dépôt et de son historique.

### Résultats incohérents entre écrans

2. **Années mélangées.** La délibération, la grille des cotes, la gestion des notes et les moyennes de
   secours prennent tous les apprenants d'une promotion, **sans filtrer l'année scolaire**. Après une
   reconduction, les anciens et les nouveaux apprenants d'une même promotion sont délibérés ensemble.
3. **Grille des cotes** : la décision n'exige que 60 %, sans tenir compte des cotes manquantes.
   Elle peut afficher ADMIS là où la délibération donne ECHEC.
4. **Suivi et statistiques enseignant** : formule SQL différente de la délibération (fusion des cotes
   S1/S2 par maximum, pas de moyenne annuelle, pas de règle des cotes manquantes).
5. **Progression d'encodage du suivi** : seules les anciennes cotes `cote` sont comptées ; les cotes
   `cote_s1` et `cote_s2` actuelles ne le sont pas, la progression reste donc proche de zéro.
6. **Cours annuel encodé seulement en S1** : la cote finale vaut S1 / 2, et le cours est considéré
   comme coté. L'apprenant peut paraître en échec avant l'encodage du S2.

### Données et intégrité

7. **Code apprenant** : tirage aléatoire sans vérification d'unicité ; deux formats coexistent
   (6 caractères à l'inscription, `ITM-AA-XXXX` à la reconduction).
8. **Modifier une note** : pour `cote_s1` et `cote_s2`, le maxima autorisé devient 10 au lieu du maxima
   du cours, le message parle de 20, et les décisions ne sont pas recalculées.
9. **Modifier un apprenant** : pas de normalisation des noms, pas de recalcul, changement de promotion
   sans traitement des notes des anciens cours.
10. **Suppressions** d'une promotion, d'un cours, d'un enseignant ou d'un apprenant sans contrôle des
    notes liées (le résultat dépend des clés étrangères de la base, inconnues).
11. **Délibération non figée** : toute cote modifiée après le jury change la décision ; aucun
    procès-verbal ni historique de délibération.
12. **Aucune trace d'audit** : on ne sait pas qui a modifié une cote, ni l'ancienne valeur.
13. **Aucune sauvegarde** intégrée ; les données dépendent entièrement de l'hébergeur.

### Exploitation

14. Une seule personne « admin », pas de rôle en lecture seule (préfet, promoteur).
15. Noms des signataires et de l'établissement écrits en dur dans le code.
16. Jeton « se souvenir de moi » unique par compte : une connexion sur un second appareil invalide le premier.
17. `enseignant.zip`, `gestion_attributions.php` et `modifier_note.php` sont des restes d'anciennes
    versions, absents des menus.

---

## 6. Correspondance avec l'architecture SCOLIA Pro

| ITMCD actuel | Nouvelle version ITMCD Pro |
|---|---|
| Hébergeur InfinityFree + MySQL | Serveur local `ITMCD-Serveur` (.NET, ASP.NET Core) + **SQLite** sur un PC ou un téléphone Android de l'établissement |
| Accès par Internet public | Réseau local, et **Tailscale** pour l'accès à distance (aucun port ouvert sur le routeur) |
| Pages PHP + PWA | Client **natif Avalonia** unique : Windows (EXE), Android (APK), iOS (IPA) |
| Sessions PHP, cookie 30 jours | Sessions serveur, mots de passe hachés, verrouillage, biométrie optionnelle sur mobile |
| Règles dans `config/db.php` | Projet `Itmcd.Domain` : règles pures, testées une par une |
| Rôles admin / enseignant | Rôles **Administration (jury)**, **Enseignant**, et **Lecture seule** (préfet, promoteur) |
| Notifications par sondage + Web Push | Notifications du serveur local vers les clients connectés |
| Exports Excel en HTML, PDF fait main | PDF (QuestPDF) et Excel (ClosedXML) réels : bulletins, grille, relevés, PV de délibération |
| Aucune sauvegarde | Sauvegarde quotidienne vérifiée, restauration, journal d'audit chaîné |
| Base chez l'hébergeur | **Import unique** de l'export MySQL existant (utilisateurs, années, promotions, cours, apprenants, notes) |

### Ce qui est conservé tel quel

- Les cinq types de note et leur lecture, y compris les anciennes `cote` et `examen`.
- Le calcul de la cote par période, la moyenne de secours, le seuil de 50 % par cours,
  le seuil de 60 % et la règle des cotes manquantes pour la décision, les mentions.
- Le recalcul automatique à chaque encodage.
- La saisie des cotes par code apprenant, les interrupteurs d'encodage, la saisie en lot des moyennes.
- La délibération avec confirmation par mot de passe, le jury annuel et ses corrections.
- Le passage de classe par promotion suivante.
- Les documents officiels : bulletins, grille des cotes, relevés et listes d'échecs.

### Ce qui sera corrigé (proposition)

- Filtrer **partout** par année scolaire.
- Un **seul moteur de calcul** pour délibération, grille, bulletins, suivi et statistiques.
- Codes apprenants uniques garantis, un seul format.
- Journal d'audit de toute cote créée, modifiée ou corrigée par le jury.
- **Clôture de délibération** : décisions figées et PV signé ; toute correction ultérieure passe par une réouverture tracée.
- Établissement, signataires et logo modifiables dans les paramètres.

---

## 7. Éléments à fournir avant la migration des données

1. **L'export SQL complet** de la base InfinityFree (structure et données), exporté depuis phpMyAdmin.
   Il ne faut pas le déposer dans un dépôt public.
2. La confirmation des règles du § 3, en particulier le cas du cours annuel encodé seulement en S1
   (§ 5, point 6).
3. Le choix de l'appareil serveur : PC toujours allumé (recommandé) ou téléphone Android.
