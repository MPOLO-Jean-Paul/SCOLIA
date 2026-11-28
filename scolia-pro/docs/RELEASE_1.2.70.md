# SCOLIA Pro 1.2.70 — Encodage enseignant, continuité hors ligne et écrans d'accès bleus

## Encodage des cotes par les enseignants

- Parcours guidé **Promotion → Cours attribué → Code anonyme → Note** : recherche et suggestions instantanées des codes officiels déjà présents dans la promotion choisie, dès le premier caractère.
- L'enseignant doit sélectionner ou vérifier un code avant de saisir une note ; un code d'une autre promotion renvoie « Apprenant hors promotion ».
- Les maxima de notation proviennent du cours, avec échelle spécifique pour l'appui ; validation côté client et revalidation métier côté serveur avant toute écriture.
- Raccourci d'encodage dans le menu Paramètres du profil enseignant ; aucune identité nominative ajoutée aux feuilles anonymisées.

## Retirer un compte ou une liaison au serveur

- Page de connexion : **retirer un compte mémorisé sur l'appareil** après confirmation. Son compte serveur et les autres utilisateurs restent intacts ; l'authentification locale de ce compte est retirée.
- Les écritures encore en attente ou en conflit empêchent le retrait local pour éviter la perte de leur traçabilité.
- Appareil client : possibilité de retirer la liaison locale à un serveur, sans effacer la base scolaire de l'ordinateur principal.
- Machine principale : les comptes peuvent être désactivés depuis Utilisateurs, avec confirmation et conservation des notes et du journal d'audit. Les protections contre l'auto-désactivation et la perte du dernier compte d'administration restent actives.

## Travail enseignant hors ligne

- Les cours réellement attribués, les feuilles anonymes, les paramètres de délibération et les annonces se copient dans le stockage local chiffré propre à chaque compte.
- Reprise automatique des téléchargements manquants et contrôle de révision au retour du réseau ; séparation entre les données pédagogiques d'un enseignant et le registre nominatif du secrétariat.
- Les cotes saisies hors ligne sont placées en file d'attente durable, clairement distinguées des notes définitivement validées ; le serveur revérifie la promotion, l'autorisation et le maximum à la synchronisation.

## Interface native avant l'accueil

- Nouveau modèle inspiré de la deuxième référence : **fond bleu ciel, grand panneau cobalt, logo SCOLIA**, formulaire centré et boutons aux contrastes élevés.
- Connexion et écran de session verrouillée harmonisés sur Windows et Android, adaptation compacte pour petits écrans.
- Actions secondaires déplacées vers des icônes accompagnées d'infobulles et de noms accessibles, sans supprimer la récupération vérifiée du mot de passe ni les erreurs utiles.
- Aucun transfert de secrets dans les paramètres visuels et aucune migration destructive de la base scolaire.

## Distribution

- Android / Windows **1.2.70** ; Android `versionCode` **190**.
- Déploiement uniquement après réussite des tests, vérification de la signature Android officielle (même certificat que les versions précédentes) et contrôle SHA-256 des fichiers d'installation.
- Ne pas désinstaller la version existante avant la mise à jour ; réaliser une sauvegarde vérifiée sur le serveur de l'établissement.
