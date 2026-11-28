# SCOLIA Pro — audit général du 5 novembre 2026

**Périmètre :** interface native Avalonia Windows et Android, couche métier C#/.NET, impressions, rôles, recherche, hors ligne, diffusion. Version de travail `1.2.22`. Cet audit complète les revues des 31 octobre, 1er et 4 novembre.

## Méthodologie et limites

Revue des sources de `src/Scolia.Bureau`, `src/Scolia.Application`, `src/Scolia.Domain`, `src/Scolia.Infrastructure/Documents`, `src/Scolia.Mobile` et des tests associés. Les critères sont comparés aux pratiques des logiciels de gestion scolaire (familles de fonctions SIS : admissions, inscription, frais, présence, pédagogique, états et audit), aux principes de navigation des systèmes Android/Windows et à l'accessibilité des contrôles métier.

**Ne pas confondre** inspection de code et validation visuelle humaine sur vrais appareils ou impression réelle. Sans ces essais, ne pas certifier l'absence de débordement sur tous les modèles.

## Résultat et priorités

| P | Fonction | Constat vérifié | Correction dans 1.2.22 / suite |
|---|---|---|---|
| P0 | Cartes d'accès | Ancien format 92 × 63 mm, modèle à sceau, champs séparés et bande colorée | **Intégré** : nouveau modèle avec logo et nom de l'établissement, session lisible, QR à la place du cachet, matricule / noms / promotion, 85,60 × 53,98 mm ; PDF et Word, 8/A4 |
| P0 | Cartes / identité de l'école | Fallback sur des mentions d'un établissement précis dans le service et les paramètres | **Intégré** : identité configurée, aucune dénomination imposée ; logo obligatoire, absence du logo signalée |
| P0 | Cartes / contrôle métier | Doublon de matricule recherché après filtre de promotion ou de lot | **Intégré** : contrôle global de l'année avant les filtres, réutilisé à l'export |
| P1 | Cartes / lot d'impression | Filtrer une promotion reconstruisait les lignes et perdait les cases cochées | **Intégré** : lot indépendant des filtres, compteur et action globale de déselection |
| P1 | Cartes / recherche | Un appel réseau à chaque lettre et réponses potentiellement désordonnées | **Intégré** : pause de 300 ms et invalidation des réponses obsolètes |
| P1 | Cartes / statut | Une ligne non éligible pouvait encore être cochée visuellement | **Intégré** : refus de la sélection dès le modèle de vue |
| P1 | Formulaires métier | Liste requise avec plusieurs choix préremplie par le premier élément, même sans choix utilisateur | **Intégré** : choix explicite, préservation de la valeur des modifications et choix unique automatique |
| P1 | Pointage | 08:00:00 traité comme un retard alors que la règle métier stipule « après 8 h » | **Intégré** : strictement après 08:00:00, avec test de frontière |
| P2 | Retour Android | Condition du menu Paramètres répétée deux fois | **Intégré** : branche dupliquée supprimée |
| P1 | Disponibilité hors ligne | Le cache ne garantit pas toutes les fonctions ni les dossiers non encore téléchargés | **À concevoir** : préparation hors ligne complète, index et résolution explicite des conflits (voir audit du 4 novembre) |
| P1 | Impression | Taille d'un porte-badge réel et rendu depuis une imprimante non testés | **À vérifier** : imprimer à 100 %, comparer avec le support physique et scanner le QR |
| P1 | Sécurité / signature | Voie de récupération de la signature Android historique liée à la PR interne | **À stabiliser** : transférer la clé dans les secrets GitHub Actions avec rotation des droits et vérification d'empreinte, sans jamais publier ses octets |

## Standards UX retenus

1. **Hiérarchie métier :** accueil avec actions utiles au rôle, état réel de la synchronisation, tâches du jour, liens vers le détail (pas seulement des cartes décoratives).
2. **Moins de saisie :** données déjà présentes récupérées automatiquement ; champs obligatoires signalés ; aucune identité, classe ou enseignant choisi par défaut sans ambiguïté.
3. **Contrôle de l'utilisateur :** sélection conservée entre filtres ; suppression et opérations financières confirmées ; retours sans perte de saisie.
4. **Performance perçue :** chargement incrémental, recherche temporisée, réponse réseau obsolète ignorée, aucune animation empêchant l'action.
5. **Accessibilité :** boutons tactiles adaptés, contrastes et focus visibles, textes redimensionnables, états d'erreur textuels et pas seulement colorés.
6. **Administration fiable :** rôles et permissions contrôlés sur le serveur, audit traçable, fichiers exportés avec les informations du bon établissement et bons états scolaires.

## Scénarios de recette avant diffusion

- **Cartes :** établissement A avec logo A, établissement B avec logo B ; vérifier qu'aucune identité A n'apparaît sur les cartes B. Imprimer 8 cartes sur A4 à 100 % et mesurer 85,60 × 53,98 mm ; vérifier QR à 2 lecteurs.
- **Cartes :** cocher des élèves de deux promotions, changer recherche et filtres, revenir, imprimer le lot exact ; une suspension ou un matricule répété ne doit pas être accepté.
- **Formulaires :** création d'une attribution avec plusieurs enseignants et aucun choix initial, édition d'une attribution avec choix conservé.
- **Pointage :** 07:59:59 et 08:00:00 à l'heure, 08:00:01 en retard, 3 retards hebdomadaires selon la règle existante.
- **Écrans :** Android étroit 360 dp, écran standard, texte grand, thème sombre, clavier ouvert, navigation arrière ; Windows 1366×768 et fenêtrage compact.
- **Mises à jour :** installation de 1.2.22 par-dessus 1.2.21 sans effacement des données, validation du certificat Android et reprise des notifications après téléchargement.
- **Finance et réseau :** rejouer une opération hors ligne, vérifier idempotence, absence de reçu prématuré et résolution visible des conflits ; tester permissions secrétariat / caisse / promoteur.

## Livraison

La PR n° 66 contient les modifications proposées. La publication Android/Windows dépend des résultats des GitHub Actions et de la signature historique. Les tests automatiques ne remplacent pas une impression réelle ni un essai tactile sur appareil. Cette feuille de route doit être réévaluée après les captures d'écrans et les tests des utilisateurs métier.
