# SCOLIA Pro 1.2.66 — Profils d'établissements et cycles pédagogiques

## Application native Windows et Android (Avalonia / C#)

Cette version introduit des configurations pédagogiques selon le type d'établissement, tout en conservant la compatibilité des bases et des comptes ITM/IM existants. Les écrans restent intégrés aux thèmes et composants natifs SCOLIA.

### Types d'établissements et statut juridique

- **ITM / IM** : maintien du fonctionnement historique, des promotions, mensualités, pièces et archives.
- **École maternelle** : cycle dédié et observations pédagogiques, sans conversion automatique en notes.
- **École primaire** : classes rattachées au cycle, programmes et périodes configurables.
- **École secondaire / technique** : classes et matières propres au cycle, évaluations par période et matière.
- **Complexe scolaire** : même établissement et base locale pour deux ou trois cycles (maternelle, primaire, secondaire), avec rattachement explicite des classes, frais et périodes distincts.
- **Université / supérieur LMD** : profil d'installation disponible, écran et services de préparation pédagogique LMD (unités, éléments constitutifs, semestres) ; la validation académique des relevés officiels reste du ressort de l'établissement.
- **Régime privé ou public** : choix explicite lors de l'installation. La tarification publique fait l'objet de restrictions distinctes pour empêcher l'activation des mensualités privées sans validation réglementaire. **La gratuité n'est pas imposée aux écoles privées.**

### Scolarité et finance

- Calendrier pédagogique séparé du calendrier des mensualités, avec périmètre par cycle et année scolaire.
- Création et modification de promotions avec affectation à leur cycle ; montants de mensualité autorisés à zéro lorsque les règles du profil l'exigent.
- La définition d'un tarif de référence de cycle ne constitue pas un encaissement : les rubriques effectives restent rattachées aux classes.
- Protection des paiements passés : les données ITM et les notes historiques ne sont pas supprimées ni réécrites par les migrations scolaires.

### Évaluations et documents

- Plusieurs matières peuvent utiliser un code courant (par exemple `INTERRO1`) pour un élève et une période ; l'unicité porte sur l'élève, la période, la matière et le code.
- Maternelle : observations narratives, non assimilées à des cotes chiffrées.
- Primaire et secondaire : aperçus pédagogiques PDF A4 signalés **NON OFFICIELS** ; l'utilisation comme bulletins réglementaires exige des modèles et règles ministérielles vérifiés.
- Contrôles d'accès, de versions concurrentes et de compatibilité SQLite maintenus.

### Validation et distribution

- Versions application : **1.2.66** sur Windows et Android ; pas de version iOS dans ce lot.
- Vérifications du domaine, des API, de l'interface native et des migrations exécutées dans GitHub Actions.
- APK Android issu du certificat de signature historique contrôlé par la chaîne de publication ; installateur Windows produit sans revendiquer une signature Authenticode.
- La publication doit conserver les contrôles d'intégrité SHA-256 et ne jamais remplacer un tag GitHub Release existant.

### Limites explicites

Les documents d'évaluation et les résultats LMD ne doivent pas être présentés comme officiellement approuvés sans validation des contenus pédagogiques, des barèmes et des modèles réglementaires applicables. Les règles de perception des frais des établissements publics exigent un cadre distinct de celui des établissements privés.
