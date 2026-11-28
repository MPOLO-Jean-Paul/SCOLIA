# SCOLIA Console administrateur — état au 28 novembre 2026

## Fonctions livrées dans la version 1.2.2

La console native Avalonia est commune à Android et Windows. Elle administre les établissements, invitations,
licences, serveurs, relais WSS, sauvegardes cloud et journal d’audit directement par l’API propriétaire.
La version 1.2.2 termine les trois écrans qui renvoyaient encore vers le navigateur :

- **Stockage cloud** : état R2, vérification par écriture/relecture/suppression d’un fichier témoin, enregistrement
  des identifiants chiffrés côté service, retrait des identifiants ajoutés et proposition de sauvegarde aux écoles.
- **Découverte TURN** : catalogue Cloudflare en lecture seule, présenté séparément des relais WSS.
- **Secours HTTPS** : enregistrement, vérification de disponibilité et retrait d’un site Netlify.
  Le transport expérimental reste désactivé tant que le protocole et les clients ne sont pas validés.

Les modifications sensibles demandent une confirmation dans l’interface. Les formulaires restent remplis après
un refus du service ; les clés R2 sont masquées et effacées après un enregistrement réussi. Une nouvelle saisie
faite pendant une requête est préservée. Le retour ramène à la rubrique d’origine. Les pages sont adaptées aux
petits téléphones, au paysage et au bureau, en thèmes clair et sombre.

Les paramètres de fréquence des sauvegardes et les nouveaux filtres/exports du tableau de bord demandés pour
**SCOLIA des écoles** sont un chantier distinct ; ils ne sont pas ajoutés à cette console propriétaire.

## Validation effectuée

- **48 tests du Worker** : sécurité, autorisations, établissements, licences, stockage et répertoire des serveurs.
- **37 tests natifs** : API, coffre chiffré, rendu des pages et interactions. Les parcours R2 et Netlify utilisent
  le vrai Worker avec SQLite en mémoire ; seuls les fournisseurs externes sont simulés.
- **Windows** : exécutable autonome et installateur compilés.
- **Android** : APK ARM/ARM64 compilé ; lancement réel de la configuration Release sur émulateur x86_64,
  application encore ouverte après 25 secondes.
- **Console web** : cinq formats et thèmes vérifiés sans débordement ni erreur JavaScript, paiement confirmé
  envoyé une seule fois et verrouillage conservé malgré une réponse tardive.
- Aucun secret réel, aucune licence et aucune donnée d’école de production n’ont été modifiés par ces tests.

Exécution de référence : [GitHub Actions 46131684267](https://github.com/MPOLO-Jean-Paul/SCOLIA/actions/runs/46131684267),
source applicative `ab204be804c659e4d7f73ccf450a130c76b30b32`.

## Distribution disponible et blocage de la publication officielle

La [préversion 1.2.2](https://github.com/MPOLO-Jean-Paul/SCOLIA/releases/tag/scolia-console-avalonia-preview-v1.2.2-ab204be8)
contient l’APK de test et l’installateur Windows. Elle est explicitement distincte du canal officiel
`scolia-owner-v*` : le paquet Android de test `com.jpservices.scolia.owner.preview` ne reçoit pas les mises à jour
signées du paquet officiel `com.jpservices.scolia.owner`. Le journal Windows confirme l’absence du certificat
Authenticode pour cette compilation. Il ne s’agit donc pas d’une version officielle signée.

La publication officielle attend ces éléments dans les réglages GitHub Actions du dépôt :

| Plateforme | Secrets | Variables publiques |
| --- | --- | --- |
| Android Admin | `OWNER_ADMIN_KEYSTORE_B64`, `OWNER_ADMIN_KEYSTORE_PASSWORD`, `OWNER_ADMIN_KEY_ALIAS`, `OWNER_ADMIN_KEY_PASSWORD` | `OWNER_ADMIN_CERT_SHA256` |
| Windows Authenticode | `SCOLIA_WINDOWS_SIGNING_PFX`, `SCOLIA_WINDOWS_SIGNING_PASSWORD` | `SCOLIA_WINDOWS_CERT_THUMBPRINT` |

Le contrôle préalable du workflow liste les noms des éléments absents, sans afficher leur contenu, avant de
compiler les installateurs officiels. Les étapes de signature vérifient ensuite réellement les certificats et
les fichiers. Aucun APK de test ne peut devenir officiel ; aucune clé scolaire ne remplace la clé Admin.
La consultation des secrets par l’intégration GitHub de cet environnement renvoie HTTP 403 : leur configuration
ne peut pas être faite depuis cette session. Ce refus ne prouve pas à lui seul l’absence de chaque secret.

La [procédure de signature](CONSOLE_SIGNATURES_PRODUCTION.md) et le script
`scripts/owner/new-permanent-android-key.ps1` permettent de préparer la clé Android sur un poste de confiance.
Conserver cette clé et ses sauvegardes chiffrées : les mises à jour Android doivent garder le même certificat.

## Service de production et accès

Console web : <https://scolia-pro-owner.jeanpaulmpolo242.workers.dev/console>.
Le Worker et les installateurs ont deux workflows distincts, déclenchés depuis `main` avec une source épinglée.
Le déploiement Worker ne nécessite pas les certificats des installateurs.

L’accès propriétaire reste protégé par Cloudflare Access **et** le jeton propriétaire. L’application native
utilise un jeton de service Access ; les trois valeurs sont chiffrées par DPAPI sous Windows ou Android Keystore.
Aucun secret n’est intégré aux binaires.

Pour un nouvel appareil :

1. Créer un jeton dans **Cloudflare Zero Trust → Access → Service Auth → Service Tokens**.
2. Ajouter une règle **Service Auth** incluant ce jeton à l’application Access qui protège `/console` et
   `/api/v1/owner/*`, sans retirer la protection des accès humains.
3. Dans **Compte → Accès sécurisé de cet appareil**, saisir le Client ID, le Client Secret et le jeton
   propriétaire, puis **Enregistrer et vérifier**. Les données sont conservées seulement si le service accepte.
4. Pour révoquer un appareil, révoquer son jeton Access et renouveler le jeton propriétaire si nécessaire.

Les routes publiques utilisées par les écoles doivent rester accessibles conformément à la configuration
existante ; ne jamais exclure les routes propriétaires de la protection Access pour résoudre un problème client.

## Repères de version

Version native **1.2.2**, Android **ApplicationVersion 6**. Les versions de l’API native, du Core, du projet
Desktop, de l’installateur, du projet Android et du workflow sont synchronisées. Une release officielle publiée
reste immuable. La coque WebView historique n’est plus le canal de distribution courant.
