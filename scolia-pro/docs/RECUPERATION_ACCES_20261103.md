# SCOLIA Pro — récupération d'accès et ergonomie de connexion
Date : 3 novembre 2026

## Périmètre
Ces modifications sont réalisées exclusivement dans le dépôt **SCOLIA**, depuis la branche de Claude `claude/powershell-forms-optimization-dfy9vr`. Aucune base de données ni mécanisme de synchronisation n'est réinitialisé. Aucun changement dans le dépôt `Gestion-scolaire`.

## Récupérer l'accès
- Sur **Connexion**, choisir **Mot de passe oublié ?** ; sur l'écran verrouillé, choisir **Code ou mot de passe oublié ?**. Le formulaire explique la procédure sans prétendre que l'application peut connaître l'ancien mot de passe.
- Un membre du personnel demande à une personne habilitée du secrétariat de suivre **Administration → Utilisateurs → Réinitialiser le mot de passe**. Ce formulaire exige désormais le nouveau code provisoire et sa confirmation.
- La réinitialisation utilise l'API existante protégée par la permission `utilisateurs.gerer`. La politique de code est vérifiée côté client et côté serveur. Le serveur enregistre le nouveau hachage, impose un changement au prochain accès, renouvelle le `SecurityStamp` afin de révoquer les sessions existantes et journalise l'opération. Le mot de passe antérieur n'est ni renvoyé ni affiché.
- Si le **seul administrateur** a oublié son mot de passe, il n'existe pas encore de parcours autonome pré-enrôlé et vérifiable pour le restaurer. Ne pas créer de compte d'urgence universel ni supprimer les données pour le débloquer : la récupération autonome nécessitera un dispositif de secours préconfiguré, à usage unique, avec contrôle anti-bruteforce et audit.

## Expérience de saisie
- Champs d'identifiant (40 caractères) et de mot de passe (128 caractères) avec taille tactile adaptée, métadonnées de saisie sensible et bouton de soumission inactif tant que les champs sont vides.
- Les écrans de connexion et de verrouillage permettent d'atteindre la procédure de récupération. Une session verrouillée reste verrouillée en arrière-plan ; l'utilisateur peut y revenir.
- Les champs secrets des formulaires ont un contrôle « afficher/masquer » utilisable au clavier et au toucher, masqué par défaut.
- La réinitialisation demande une double saisie ; les erreurs sont indiquées dans le champ sans effacer prématurément la saisie.

## Validation
Les tests unitaires `AuthRecoveryExperienceTests` couvrent l'état des champs, le guide de récupération et l'œil des champs secrets. Les workflows existants `SCOLIA Pro` et `SCOLIA Natif` compilent les projets et exécutent les tests à l'ouverture d'une pull request. **Ne pas considérer une compilation ou un test sur appareil comme réussi avant que GitHub Actions l'ait confirmé.**
