# SCOLIA Pro — Cahier métier : pointage des enseignants et Promoteur

## Décision technique
Un téléphone Android partagé ne permet pas d'identifier lequel des enseignants a posé son doigt. BiometricPrompt fournit une preuve de déverrouillage de l'appareil, pas une identité par empreinte. Il est interdit d'attribuer automatiquement un StaffMemberId selon cette seule preuve.

## Modalités réalistes
**Phase téléphone unique :** borne officielle autorisée par l'école, enseignant identifié par badge individuel et secret personnel, confirmation visuelle par l'opérateur, entrée et sortie. Un code partagé ne prouve pas la présence physique : prévoir une surveillance humaine, un journal des opérations et une procédure de contestation.
**Phase téléphones individuels :** clé cryptographique liée à chaque appareil et compte enseignant, authentification Android sur son propre appareil, défi signé à usage unique, contrôle indépendant de la présence sur place.
**Phase capteur externe :** uniquement un lecteur USB/Bluetooth multi-utilisateurs avec SDK Android du fabricant explicitement capable d'identification 1:N ou vérification 1:1 ; audit de conservation des modèles biométriques, confidentialité et procédure de suppression avant toute utilisation.

## Processus journalier
1. Avant la journée : vérifier l'activité, calendrier de travail et horaires propres à chaque enseignant. Administrateur habilité à créer les identités et à inscrire la borne.
2. Entrée : identifier personnellement l'enseignant selon la méthode approuvée, confirmer, transmettre une opération unique. Le poste principal attribue date, heure et retard ; il refuse une seconde entrée.
3. Sortie : nouvelle vérification, entrée préalable obligatoire ; une seconde sortie est refusée.
4. Journée validée : entrée ET sortie confirmées. Une entrée sans sortie reste Incomplet, à régulariser ; elle ne vaut pas présence définitivement validée.
5. Absences et retards : prise en compte du calendrier, des horaires, du fuseau Africa/Lubumbashi, des justificatifs, de la règle des trois retards par semaine et des dates non ouvrables.
6. Correction exceptionnelle : motif et autorisation obligatoires, historisation de l'ancienne et nouvelle valeur ; aucune suppression silencieuse.

## Sécurité
- Droit de borne distinct de personnel.gerer ; pas de comptes administrateurs, frais ou étudiants accessibles sur le terminal.
- Secret personnel vérifié côté serveur, hachage adapté aux mots de passe, limitations d'essais et révocation.
- Terminal appairé avec clé non exportable et canal authentifié/chiffré. Interdire les requêtes anonymes.
- L'heure et le jour sont établis côté poste principal, jamais depuis l'horloge du téléphone.
- Chaque opération possède une identité idempotente, un contrôle d'unicité transactionnel (enseignant, jour, type), et une piste d'audit.
- En cas de panne réseau, montrer En attente de synchronisation, jamais Présent validé avant confirmation centrale.
- L'accès biométrique Android sur téléphone partagé n'est jamais employé comme reconnaissance d'un enseignant.
- Protéger les secrets, supprimer les traces sensibles, prévoir révocation du terminal perdu, informer les enseignants et définir la conservation des données.

## Promoteur / Responsable
Rôle séparé de Secrétariat et Comptabilité. Permission limitée à pilotage.lire pour récupérer uniquement un résumé agrégé. Thème, police et sons restent des préférences locales libres.
Indicateurs : effectif actif et débiteurs par promotion, encaissé aujourd'hui, ce mois et sur l'année, dette à venir et dette exigible, ventilation par promotion et période scolaire.
La date d'encaissement et le mois de dette sont deux dimensions distinctes : payer en octobre une dette de septembre augmente les recettes d'octobre mais diminue le solde de septembre.
Aucun accès aux dossiers nominatifs, paiements individuels, reçus, annulations, administrateurs, utilisateurs, sauvegardes ou écritures.

## Recette minimale avant activation du pointage par téléphone
- T01 : aucune confusion entre plusieurs doigts enregistrés sur un smartphone partagé.
- T02 : entrée et sortie nécessaires pour statut Présent ; sortie sans entrée, doubles enregistrements refusés.
- T03 : rejouage réseau, décalage de l'horloge client et mode hors ligne ne créent pas de fausse présence.
- T04 : révocation du terminal et des secrets individuels immédiatement appliquée.
- T05 : Promoteur obtient 403 pour toute écriture et API de données nominatives.
- T06 : chaque rubrique trouvée via la recherche du menu ouvre la rubrique précise, sans retour à Accueil.

## Avancement réel
Le rôle Promoteur, les contrôles d'accès serveur, le résumé financier agrégé et la page de pilotage sont codés et testés sur cette branche. La navigation depuis les résultats de recherche a été corrigée.
**Le pointage multi-enseignants authentifié via un lecteur biométrique de téléphone partagé n'est pas réalisable avec les API Android standard et n'a pas été développé.** La borne avec identification personnelle exige ses propres travaux de sécurité, de migration et de tests avant mise en service.
Ne jamais promettre un logiciel impossible à pirater : le but est une sécurité défendable, testée et révisable.