# SCOLIA Pro — Continuité quand l'ordinateur principal est hors ligne

## Décision d'architecture

L'ordinateur principal reste **l'unique autorité métier** pour les droits, paiements,
recettes, conflits et numéros de reçus. Les postes Android/Windows **déjà jumelés
et authentifiés auparavant** peuvent travailler depuis leur réplique locale chiffrée.
Lorsqu'une écriture quotidienne autorisée est effectuée, l'outbox SQLite chiffrée
conserve l'opération **avant** de répondre à l'interface.

Si la fonction « Sauvegarde et relève en ligne » est activée par l'établissement
et que sa console JP-Services est opérationnelle (Cloudflare Worker + D1 + R2),
le poste peut **déposer une copie chiffrée de l'opération** même quand
l'ordinateur principal est hors ligne. Le serveur relève ensuite cette boîte et
valide chaque écriture avec les droits de son auteur, une délégation signée,
une clé d'idempotence et les contrôles de licence.

**Le dépôt cloud n'est pas un paiement validé.** Un reçu provisoire LOCAL ne
devient officiel qu'après application par le serveur. La réception cloud n'efface
pas l'outbox locale. Seul le résultat serveur `applied` ou une réponse serveur
réussie peut en retirer une opération ; un refus reste visible comme conflit.

## Correctifs de ce changement

- Android : le travail différé WorkManager essaie maintenant de déposer
  les opérations chiffrées des comptes possédant un accès cloud préalablement
  remis, même quand la sonde du serveur principal échoue. Il conserve toujours
  les entrées localement.
- Android : au retour du serveur, les résultats cloud déjà appliqués sont
  récupérés avant la reprise de l'envoi direct.
- Windows : la sonde de disponibilité, quand elle échoue, réessaie le dépôt
  cloud à cadence limitée (deux minutes), sans bloquer les écrans.
- Multi-compte : la sélection porte sur les comptes ayant réellement des
  opérations non déposées et un accès de dépôt chiffré existant sur cet appareil.
  Aucun partage de mot de passe ni création de droits depuis un relais.
- Test automatisé : la file résiste au redémarrage, reste cloisonnée par
  utilisateur, et un accusé de dépôt ne retire pas l'opération.

## Prérequis avant activation réelle

1. Déployer et contrôler la console propriétaire JP-Services, son Worker,
   la base D1, le stockage privé R2 et les routes de boîte de dépôt.
   `wrangler.jsonc` contient une base D1 à provisionner : une documentation
   seule ne constitue pas une infrastructure active.
2. Enregistrer légalement et volontairement l'établissement, puis
   activer la continuité depuis l'ordinateur principal avec l'accord du
   responsable. Conserver le code de secours de l'école hors du PC.
3. Donner aux comptes concernés une première authentification sur chaque
   appareil, récupérer une délégation signée avant toute coupure, et effectuer
   une synchronisation initiale des données nécessaires. Les délégations ont
   une durée limitée (actuellement 30 jours).
4. Vérifier les journaux et les délais de conservation, les quotas, les
   protections contre les abus et les mécanismes de révocation. Un appareil
   révoqué n'obtient pas de nouvelle délégation, et le serveur revérifie les
   droits lors de l'application.
5. Tester sur appareils réels les coupures prolongées, les changements de
   réseau, l'expiration de session, la réinstallation, les paiements concurrents,
   les réponses perdues, l'interruption de l'application, les sauvegardes et
   la reprise après sinistre. Les restrictions Android ne permettent pas de
   garantir une synchronisation à la seconde près.

## Statuts à distinguer dans les interfaces

- **Local** : enregistré durablement sur l'appareil, non encore déposé.
- **Déposé dans le cloud** : copie chiffrée reçue, toujours non validée par le PC.
- **Appliqué** : accepté par le PC de l'établissement (et retiré de la file).
- **Conflit / refus** : à résoudre selon les règles métier, jamais effacé en silence.

Si le téléphone n'a plus de connexion Internet, la copie locale reste
opérationnelle, et le dépôt est retenté lorsqu'une liaison est disponible.
Les autres appareils ne peuvent pas lire instantanément les modifications
d'un appareil **tant que l'ordinateur principal n'a pas repris et confirmé**
la synchronisation : le cloud n'est pas une nouvelle base métier partagée.

## Fournisseurs cloud

- **Render / Cloudflare relais WSS** : transport d'une liaison vers le PC
  principal, mais pas de stockage autonome de comptes ou de paiements.
- **Console JP-Services (Cloudflare D1/R2)** : boîte de dépôt chiffrée et
  sauvegarde côté établissement, si activée et testée.
- **Netlify** `scolia-pro-netlify-relay-jp.netlify.app` : point de santé HTTPS,
  pilote désactivé (`enabled: false`, `websocket: false`, `turn: false`,
  `production_ready: false`). Ne pas y transférer d'opérations réelles ni
  l'ajouter à la rotation WSS avant nouvelle validation du protocole.

La mise en place du code sur une branche et le succès d'un test unitaire ne
prouvent **pas** le déploiement cloud, la publication de nouveaux APK/EXE ou la
synchronisation réelle de bout en bout. Ces étapes doivent être validées
séparément avant fusion et diffusion à des établissements.
