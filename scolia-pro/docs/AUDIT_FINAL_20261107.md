# SCOLIA Pro — audit de contrôle du 7 novembre 2026 (version 1.2.36)

**Branche :** `ccr-0ebe2fd6-veic7n`, issue de `feature/scolia-cloudflare-relay-20261107` et contenant la tête de
`release/direct-apk-scolia-pro-20261103` (1.2.35). Cet audit refait le contrôle après toutes les corrections du jour ;
il remplace, pour l'état courant, les tableaux de `AUDIT_METIER_INTERFACES_20261107.md`.

## 1. Méthode

- Relecture du code modifié (serveur, client Avalonia, relais Cloudflare) et des points restés ouverts.
- Exécution de toutes les suites disponibles, et des parcours d'interface sans affichage contre un serveur réel, sur une
  base de démonstration créée par la version 1.2.35 (pour éprouver la migration).
- Contrôle visuel de captures : tableau de bord, Encaisser, catégories de frais, Réseau et postes, fenêtres de partage,
  menus et barres d'outils, ordinateur (1280 × 820) et téléphone (360 × 760).

**Limites.** Pas d'appareil réel ni d'imprimante ; pas de relais Cloudflare déployé (aucun compte) ; dans le conteneur,
aucune carte réseau n'est vue comme active : le code QR prêt et son compte à rebours n'ont pas pu être affichés.

## 2. Résultats des contrôles

| Contrôle | Résultat |
|---|---|
| Compilation `Scolia.sln` (avertissements bloquants en CI) | 0 erreur |
| Tests serveur `Scolia.Tests` | **266 / 266** |
| Tests client `Scolia.Bureau.Tests` | **430 / 430** |
| Tests mobile `Scolia.Mobile.Tests` | **42 / 42** |
| Relais Cloudflare (`npm test`, types, `wrangler deploy --dry-run`) | **13 / 13**, types et paquet valides |
| Archive des sources (`test_source_archive.py`) | OK |
| Parcours `smoke` (métier complet) | 40 contrôles, 0 échec |
| Parcours `bureau` (tailles, texte agrandi, dialogues) | **176 contrôles, 0 échec** (les 2 échecs historiques venaient du test lui-même, corrigé) |
| Parcours `fluidite` (mobile, petits écrans, clavier) | 189 contrôles, 0 échec |
| Captures `partage` mobile | aucun texte coupé |
| Migration d'une base 1.2.35 | sauvegarde « avant mise à jour », catégories créées pour les anciens frais, journal d'audit intègre |

## 3. État des constats

### Corrigés

| Domaine | Constat initial | État |
|---|---|---|
| Relais Cloudflare | Hôte refusé à la reconnexion, aucune durée maximale, pas de limite de débit, versions flottantes | Corrigé : remplacement de session, alarme 15 s / 15 min / 65 min, 120 tentatives/min/IP, `package-lock.json`, fermeture propre côté .NET |
| Encaissement | Double reçu après réponse perdue ; aucune confirmation | Corrigé : clé anti-doublon par versement, récapitulatif avant envoi |
| Caisse | Arrêté du jour bloquant la journée ; attendu lu avant le verrou | Corrigé : verrou à l'heure de l'arrêté, situation lue sous verrou, attendu de la date choisie |
| Dépenses | Annulation avec le droit de saisie ; numérotation et recherche | Corrigé |
| Frais | Inscription / examen impossibles à encaisser | Corrigé : catalogue des catégories, montant par catégorie et par promotion, frais encaissables, mensualité obligatoire |
| Prises en charge | Comptées parmi les payants et « en ordre » | Corrigé : comptées à part partout (tableaux, pilotage, suivi mensuel, listes par promotion, exports), signalées |
| Partage | Réglages dispersés, aucune gestion des appareils, messages faux sur le serveur, « Render » nommé | Corrigé : page Réseau et postes regroupée, appareils autorisés avec retrait, QR avec compte à rebours, libellés neutres |
| Icônes | Styles mélangés (plein / trait), boutons d'action sans icône, flèche en texte | Corrigé : tuiles pleines (navigation, réglages) ; icônes au trait dans barres, menus et 45 boutons d'action ; tailles 16 / 18 |
| Accessibilité | Boutons-icônes sans nom pour les lecteurs d'écran | Corrigé : nom tiré de l'info-bulle |

### Nouveaux constats de cet audit

| P | Constat | Action |
|---|---|---|
| P1 | Supprimer une catégorie retirait aussi ses frais non payés des **années clôturées** (soldes modifiés après coup) ; un renommage changeait aussi leurs libellés | **Corrigé** : suppression refusée si une année clôturée l'utilise ; renommage limité aux années ouvertes ; test ajouté |
| P2 | Le test « Connexion : carte et champs dans la fenêtre » cherchait l'identifiant, masqué quand le compte est reconnu | **Corrigé** dans le parcours (l'écran était juste) |
| P2 | Suivi mensuel : « Aucun pris en charge » répété à côté de la nouvelle tuile | **Corrigé** |
| P2 | Le relais Cloudflare compte les tentatives par instance (pas de quota global) | Accepté : borne de premier niveau ; ajouter une règle Cloudflare Rate Limiting au déploiement si besoin |
| P2 | Le code QR prêt et son compte à rebours n'ont pas été vus à l'écran | À vérifier sur un vrai PC avec le partage ouvert |

### Restent ouverts (décision du responsable)

1. **Rôles** : le compte créé à l'installation a le rôle Secrétariat, qui a tous les droits. Séparer suppose un vrai rôle
   Administrateur et une reprise des comptes.
2. **Droit dédié aux prises en charge** (aujourd'hui : droit d'écriture des dossiers).
3. **Reçu figé** (solde et payeur au jour du paiement, mention « Duplicata ») : migration à prévoir.
4. **Antidatage** des reçus : règle à définir sans bloquer les encaissements hors ligne synchronisés tard.
5. Feuille de route : plusieurs monnaies, rapprochement Mobile Money, billetage, pénalités, remises, échéanciers.
6. **Signature Android** : la publication par la branche `release/…` exige les secrets `SCOLIA_ANDROID_KEYSTORE` et
   `SCOLIA_ANDROID_KEYSTORE_PASS` (empreinte attendue `E5:B5:…:89:80`). Sans eux, le CI bloque la publication sans rien
   diffuser.

## 4. Avis de mise en production

La version **1.2.36** peut être publiée, avec deux précautions :

- **Prévenir les caisses** : les frais d'inscription ou d'examen déjà saisis dans des promotions comptent désormais dans
  le solde et le retard des apprenants (ils étaient ignorés). Les soldes affichés vont donc augmenter là où ces frais existent.
- **Ne pas installer pendant des encaissements**, synchroniser les appareils avant, et laisser la sauvegarde automatique
  « avant mise à jour » se faire (elle est vérifiée au démarrage).
