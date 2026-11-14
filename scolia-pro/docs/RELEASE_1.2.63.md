# SCOLIA Pro 1.2.63 — Interfaces d'accès natives et espace enseignant unifié

## Android et Windows exclusivement

Cette version conserve le moteur graphique Avalonia et toute la logique C#/.NET existante. Aucune compilation ni publication iOS n'est requise.

### Avant l'accueil
- **Connexion**, récupération du compte et saisie du code : présentation directement intégrée au fond de la fenêtre ou du téléphone, sans carte blanche, ombre ou panneau visuel de type site web.
- **Choix du serveur et jumelage** : commandes QR, invitation, Wi-Fi et progression intégrées à la page native, sans panneaux superposés.
- **Reprise de session** : code, empreinte, déverrouillage via appareil autorisé et changement de compte préservés.
- **Apparence** : respecte le thème clair/sombre de l'application, au lieu de forcer un thème clair uniquement sur les écrans d'accès.
- **Petits téléphones** : conserve les réductions adaptatives `compact`, `tiny`, `short` et `micro`, sans ajouter de défilement horizontal.

### Enseignants
- L'accueil enseignant réutilise **la bande d'accent, la tuile d'icône et les boutons natifs du même kit** que le secrétariat et l'administration.
- Les cours, les indicateurs et les règles métiers gardent les mêmes modèles de données et composants déjà partagés.
- La recherche des comptes enseignants adopte les mêmes champs `w11` que les formulaires SCOLIA.

### Conformité et sécurité

- Ni les API, ni les mots de passe, ni les autorisations, ni la biométrie, ni SQLite ni la synchronisation ne sont modifiés.
- Les tests `NativeEntryAndTeacherVisualTests` empêchent le retour des cartes d'accès et des écrans imposés en clair.
- L'APK de production doit être signé avec **le certificat Android historique de la PR n° 18**. Tout APK signé avec une clé de validation provisoire doit rester un artefact de test non installable en mise à jour.
- Les APK/EXE ne seront publiés qu'après validation des tests de navigation native, du rendu pré-connexion et des contrôles de confidentialité.
