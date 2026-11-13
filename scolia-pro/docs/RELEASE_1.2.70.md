# SCOLIA Pro 1.2.70 — Encodage anonyme et retrait sécurisé des comptes

## Espace enseignant

- Parcours : **Promotion → Cours attribué → Code anonyme → Validation → Note obtenue → Enregistrer**.
- Les suggestions de codes sont filtrées instantanément à partir du premier caractère, dans la promotion du cours.
- La sélection d'un code présent sur la feuille confirme le code sans afficher l'identité. Pour un code saisi manuellement qui n'appartient pas à cette feuille, le serveur vérifie son affectation et signale notamment « Apprenant hors promotion ».
- Le champ Note reste désactivé avant la confirmation du code. Le maxima provient du cours choisi ; les moyennes d'appui sont sur 10. Valeurs négatives, non numériques ou au-dessus du maxima rejetées par l'interface **et** par le serveur.
- Accès aux cours et à la vérification des codes contrôlé côté API, avec nouveau contrôle lors de l'enregistrement.
- Option « Encodage » ajoutée au menu Paramètres et au bloc d'information de l'espace enseignant ; styles Avalonia natifs conservés. Relevé anonyme en lecture seule pour l'enseignant.

## Retrait des comptes / serveurs

- Sur l'écran de connexion Android et Windows, retirer un compte mémorisé nécessite confirmation. Cela efface son empreinte de connexion hors ligne locale sans supprimer les données pédagogiques ni son compte serveur. Un compte avec biométrie doit d'abord désactiver cette fonction avec vérification d'identité.
- En présence de modifications locales non synchronisées, le retrait est refusé pour empêcher de bloquer leur envoi.
- Sur un poste client, « Retirer ce serveur » retire la configuration de liaison, l'invitation Internet et les autorisations mémorisées sans toucher à la base du serveur principal.
- Sur la machine principale, un administrateur peut utiliser « Retirer l'accès au serveur » dans Utilisateurs : désactivation réversible et auditée, refus de retirer son propre compte et protection du dernier administrateur.
- Les comptes et données scolaires du serveur principal ne sont jamais supprimés par ces commandes.

## Qualité et publication

- Tests de régression : mémorisation et oubli des comptes, opérations en attente, XAML natif de saisie et de retrait.
- Publication Android / Windows **1.2.70** ; Android versionCode **190**.
- Conserver la signature Android historique de PR #18. Aucun effacement SQLite ni migration destructive.
- Publication uniquement après CI verte, vérification des exécutables et de leur signature.
