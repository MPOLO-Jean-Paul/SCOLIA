# SCOLIA Pro — audit métier et interfaces du 7 novembre 2026

**Branche auditée :** `feature/scolia-cloudflare-relay-20261107` (commit `e1cf262`, la plus récente du jour).
**Périmètre :** nouveau relais Cloudflare (`deploy/cloudflare`), règles métier (`Scolia.Domain`, `Scolia.Application`),
autorisations de l'API (`Scolia.Api/Endpoints`), écrans natifs Avalonia (`Scolia.Bureau`).
Cet audit complète celui du 5 novembre et ne répète pas ses points.

**Méthode et limites.** Lecture du code source. Les 9 tests du relais Cloudflare ont été lancés (`node --test`) : ils passent.
Les tests .NET n'ont pas pu être lancés (pas de SDK .NET dans l'environnement d'audit), et aucun écran n'a été rendu.
Les constats d'interface viennent donc du XAML et des modèles de vue, pas d'un essai sur appareil.

Priorités : **P0** risque d'argent, de données ou d'accès ; **P1** erreur métier probable ou gêne réelle ; **P2** amélioration.

---

## 1. Relais Cloudflare (nouveau dans la branche)

Le protocole est bien repris du relais .NET et le contenu reste chiffré de bout en bout (AES-GCM côté SCOLIA).
Plusieurs garde-fous du relais .NET n'ont toutefois pas été reportés.

| P | Constat | Fichier | Proposition |
|---|---|---|---|
| P0 | **Reconnexion de l'hôte bloquée.** Un second hôte reçoit `409` tant que l'ancien socket paraît ouvert. Le serveur SCOLIA renouvelle sa session toutes les 55 min par `socket.Abort()` (sans trame de fermeture) : si Cloudflare n'a pas encore vu la coupure, il refuse la reconnexion et l'attente passe à 4, 8… 60 s. Une coupure réseau à moitié ouverte produit le même effet. | `src/core.js` (`connectRoom`), `Scolia.Transport/RemoteHost.cs` | L'hôte est authentifié par son jeton (son empreinte est le nom de la salle) : remplacer l'ancien hôte au lieu de refuser (fermer l'ancien socket et ses clients). Côté .NET, fermer proprement (`CloseOutputAsync`) avant le renouvellement. |
| P1 | **Aucune durée maximale.** Le relais .NET coupe un client après 15 s sans requête et 15 min au total, et l'hôte après 1 h. Le Worker n'a aucune limite : un client peut garder indéfiniment l'une des 16 places de la salle. | `src/core.js` | Ajouter une alarme de Durable Object (`ctx.storage.setAlarm`) qui ferme les clients sans requête après 15 s et les sockets trop anciens. Garder l'heure d'ouverture dans l'attachement. |
| P1 | **Pas de limite de débit.** Le .NET limite à 120 requêtes/min par IP. Le Worker transmet au Durable Object toute requête `/v1/client/…` au format valide, même avec un faux jeton : chaque tentative consomme le quota gratuit. Le commentaire « checks capabilities before it creates/bills a DO request » est faux pour le rôle client. | `src/index.ts` | Règle Cloudflare Rate Limiting (gratuite) sur `/v1/*`, ou compteur par IP dans le Worker. Corriger le commentaire. |
| P1 | **Contrôle de saturation incertain.** `bufferedAmount` n'est peut-être pas exposé par le WebSocket des Workers. Si la valeur est `undefined`, le test `> 2 Mio` est toujours faux et la protection ne fait rien. | `src/core.js` (`safelySend`) | Vérifier dans la documentation Workers et tester avec `wrangler dev`. Sinon, borner le nombre de trames par requête. |
| P2 | **Installation non reproductible.** Pas de `package-lock.json` ; `wrangler ^4` et `typescript ^5.9` flottants. Le CI fait `npm install`. | `package.json`, workflow | Versionner le fichier de verrouillage et utiliser `npm ci`. |
| P2 | **README périmé.** Il dit que le bouton « peut encore afficher Render », or le commit `eeea660` l'a renommé « Activer ou vérifier Internet ». La phrase « Ne pas transmettre … dans ChatGPT » doit dire « à un assistant ou un tiers ». La branche de production doit devenir `main` après la fusion. | `deploy/cloudflare/README.md` | Mettre à jour le texte. |
| P2 | Les tests simulent `getWebSockets`/l'hibernation, mais aucun test ne met deux vrais sockets en relation. | `tests/` | Ajouter un essai `wrangler dev` + client .NET (ou `unstable_dev`) dans le CI. |

## 2. Cadre métier

### 2.1 Contrôle interne et rôles — P0

| Constat | Fichier | Proposition |
|---|---|---|
| **Le rôle Secrétariat a *toutes* les permissions**, comme Administrateur et Direction : encaisser, annuler un reçu, arrêter la caisse, gérer les utilisateurs, restaurer une sauvegarde, effacer des données. Une même personne peut encaisser, annuler puis arrêter la caisse, sans contrôle d'une autre. | `Scolia.Domain/Security/Users.cs` (`Permissions.For`) | Séparer **Administrateur/Direction** (tout) et **Secrétariat** (apprenants, inscriptions, cartes, scolarité, lecture des paiements). L'annulation de reçu et l'arrêté de caisse vont à Comptabilité/Direction. Prévoir une migration qui conserve un administrateur. |
| **Les prises en charge ne demandent que `apprenants.ecrire`.** Une prise en charge complète efface la dette d'un apprenant : c'est une décision financière, mais le secrétariat peut l'accorder seul et Comptabilité ne le peut pas. | `ScoliaEndpoints.cs` l. 298-306 | Créer une permission `prises-en-charge.accorder` (Direction) ; montrer les prises en charge du jour dans le journal journalier et au promoteur. |
| **Annulation de dépense** contrôlée par `depenses.ecrire` (le même droit que la création), alors que l'annulation d'un reçu a un droit séparé. | `ScoliaEndpoints.cs` l. 341 | Exiger `paiements.annuler` ou un droit `depenses.annuler`. |
| L'auteur d'un reçu peut l'annuler lui-même. | `PaymentService.CancelAsync` | Option « annulation par une autre personne » ; au minimum, signaler ces cas dans le rapport journalier. |
| Le rôle Enseignant n'a plus aucune permission : les notes et les présences sont saisies par le secrétariat. | `Users.cs` | Si les enseignants doivent saisir eux-mêmes : rôle limité à **leurs** cours (attributions déjà présentes dans `TeachingService`). |

### 2.2 Encaissement et caisse

| P | Constat | Fichier | Proposition |
|---|---|---|---|
| P0 | **Double reçu possible après une coupure.** La clé d'idempotence est créée à chaque clic. Si le serveur enregistre le reçu mais que la réponse se perd (Wi-Fi, relais), le caissier clique encore et une nouvelle clé crée un second reçu. La clé ne protège que le rejeu hors ligne. | `Scolia.Bureau/ViewModels/Cashier.cs` (`RecordPaymentAsync`) | Créer la clé une seule fois par versement (à la première tentative) et la réutiliser jusqu'au succès ou jusqu'au changement de mois, de montant ou d'apprenant. |
| P0 | **Un arrêté de caisse bloque le reste de la journée.** Après un arrêté à la date du jour, tout reçu daté du jour est refusé (`date <= dernier arrêté`), et une date future l'est aussi. Le guichet ne peut plus encaisser avant le lendemain. Un encaissement hors ligne fait avant l'arrêté passe en conflit pour toujours. | `PaymentService.CreateAsync`, `CashService.CloseAsync` | Arrêter par **horodatage** (`ClosedAtUtc`) plutôt que par jour, ou n'autoriser que l'arrêté de la veille. Prévoir une procédure de « régularisation » pour les envois hors ligne antérieurs à l'arrêté. |
| P1 | **Frais d'inscription, d'examen et autres impossibles à encaisser.** Le modèle a `FeeKind.Inscription/Examen/Autre`. La démo les crée et le passage d'année les copie. Mais `AccountService` ne garde que les mensualités, et `PaymentAllocator` refuse tout autre type. Ces frais n'apparaissent ni au compte, ni au recouvrement, ni sur un reçu. | `AccountService.cs` l. 56, `StudentAccount.cs` (`PaymentAllocator.Months`) | Soit gérer ces rubriques (encaissement à part, ligne dans le compte), soit les retirer du modèle, de la démo et du passage d'année. L'état actuel est trompeur. |
| P1 | **Une réimpression de reçu change le document.** Le reçu montre le solde *actuel* de l'élève, pas le solde au moment du paiement. Un reçu sans payeur saisi prend le tuteur *actuel* (le commentaire le dit). Deux impressions du même numéro peuvent donc différer. | `PaymentService.ReceiptPdfAsync`, `GetAsync` | Figer à l'encaissement le solde restant et le nom du payeur dans le `Receipt` ; la réimpression porte la mention « Duplicata ». |
| P1 | **Antidatage libre.** Le sélecteur de date permet n'importe quelle date passée non arrêtée, sans motif. | `CashierView.axaml`, `PaymentService` | Limiter à J-1 sans droit particulier ; au-delà, motif obligatoire et ligne d'audit marquée. |
| P1 | **Arrêté de caisse : montant attendu figé à aujourd'hui.** Le formulaire affiche les espèces attendues du jour, mais permet de changer la date. L'écart est calculé par le serveur sur une autre date que celle affichée. La situation est lue *avant* la prise du verrou multiposte. | `Bureau/ViewModels/Finance.cs` (`ClosingAsync`) | Recalculer l'attendu quand la date change et après la prise du verrou ; afficher l'écart en direct. |
| P1 | **Pas de rapprochement Mobile Money / banque.** L'arrêté ne couvre que les espèces ; les autres modes ne sont jamais contrôlés. Pas de billetage (détail des coupures). | `CashService` | Ajouter un pointage des références Mobile Money/banque et un billetage optionnel à l'arrêté. |
| P1 | **Une seule monnaie.** En RDC, les frais se paient souvent en USD et en CDF. | `SchoolProfile.CurrencyCode` | Monnaie par rubrique, ou deuxième monnaie avec taux du jour figé sur le reçu. |
| P2 | La numérotation des dépenses suit l'année **courante**, pas celle de la date de la dépense (les numéros `DP-` peuvent sauter d'année en début de rentrée). | `CashService.CreateExpenseAsync` | Numéroter selon l'année de la date. |
| P2 | Recherche des dépenses sensible à la casse et aux accents (`Contains` brut), à la différence des reçus (`TextKey.Normalize`). | `CashService.ExpensesAsync` | Ajouter une colonne `SearchText` normalisée. |
| P2 | Le contrôle « espèces suffisantes » ne regarde que les opérations jusqu'à la date choisie : une dépense antidatée peut rendre la caisse négative à une date ultérieure. | `CashService.CreateExpenseAsync` | Contrôler aussi le solde à la date du jour. |
| P2 | Totaux des listes calculés en chargeant tous les montants en mémoire (`ToListAsync().Sum()`). C'est acceptable aujourd'hui, mais lent sur plusieurs années. | `PaymentService.ListAsync`, `CashService` | Stocker les montants en entier (unités) ou en `double` agrégable côté SQLite. |

### 2.3 Fonctions métier à envisager

- **Pénalités de retard** et **remises** (fratrie, personnel) distinctes des prises en charge.
- **Avances et trop-perçus** : un versement supérieur aux mois choisis est refusé. Il n'y a ni crédit, ni remboursement tracé.
- **Échéancier personnalisé** (plan de paiement négocié avec une famille).
- **Relances groupées** (WhatsApp/SMS) depuis la liste des débiteurs, avec historique des relances.
- **Téléphone** : la règle n'accepte que les numéros congolais à 10 chiffres. Un tuteur à l'étranger ne peut pas être enregistré : accepter le format international E.164.

## 3. Interfaces et fenêtres

| P | Constat | Fichier | Proposition |
|---|---|---|---|
| P0 | **Aucune confirmation avant un encaissement simple.** Le lot de paiements, la restauration et la clôture d'année demandent une confirmation ; un reçu unique part en un clic sur la barre du bas, alors qu'il ne peut ensuite être qu'annulé avec motif. | `Cashier.cs` (`PayAsync`) | Fenêtre récapitulative : apprenant, mois, montant en chiffres **et en lettres**, mode, référence. Raccourci Entrée pour confirmer. |
| P1 | **Accessibilité absente.** Aucun `AutomationProperties.Name` dans les 51 écrans : les boutons-icônes (flèches, menus, cadenas) n'ont pas de nom pour un lecteur d'écran (Narrateur, TalkBack). Les retards sont signalés seulement en rouge. | `Views/*.axaml` | Nommer les boutons-icônes ; ajouter un texte ou une icône « En retard » à côté de la couleur. |
| P1 | **Fenêtre de bureau au format téléphone.** `MainWindow` s'ouvre en 460 × 640 avec `MinWidth/MinHeight = 0`. Elle peut être réduite à rien, et sur PC l'écran Encaisser (2 colonnes, empilées sous 1000 px) s'ouvre empilé. | `MainWindow.axaml`, `MainWindow.axaml.cs` | Taille de départ selon l'écran (par ex. 1280 × 800 ou maximisée), taille minimale ≈ 360 × 560, dernière taille mémorisée. |
| P1 | **Libellé trompeur à l'encaissement** : « Le reste sera conservé sur le compte ». Aucun crédit n'est conservé : le montant ne peut pas dépasser les mois choisis, et « le reste » est la dette restante. | `CashierView.axaml` | « Le montant non versé reste dû sur ces mois. » |
| P1 | **Payeur non modifiable.** Le téléphone du payeur est repris du dossier sans possibilité de modification, et le nom du payeur n'est pas demandé. Quand un oncle ou un parrain paie, le reçu porte le tuteur. | `CashierView.axaml` | Champ « Payé par » et téléphone modifiables, préremplis par le tuteur. |
| P2 | L'observation vide est imprimée « OK » sur le reçu. | `Cashier.cs` (`DefaultNote`) | Laisser vide. |
| P2 | Alignement de la 2e ligne du formulaire Encaisser : la colonne 1 utilise `Spacing="4"`, les colonnes 2 et 3 `Margin="0,10,…"`, d'où un décalage vertical des trois champs. | `CashierView.axaml` | Même `Spacing`, sans `Margin` ad hoc. |
| P2 | Tailles en dur : ~90 `FontSize` numériques (11 à 24) à côté des ressources `FsSmall`/`FsHeading`. Le réglage « taille du texte » ne les agrandit pas. | `Views/*.axaml` | Remplacer par les ressources dynamiques. |
| P2 | Modèles de vue très gros : `Shell.cs` (1 970 lignes), `Operations.cs` (1 306), `Administration.cs` (1 256). Les régressions deviennent plus probables à chaque version. | `ViewModels/` | Découper par écran ; sortir la navigation, la synchronisation et les notifications du Shell. |
| P2 | L'arrêté de caisse est un formulaire générique : ni liste des mouvements, ni écart en direct, ni impression du procès-verbal. | `Finance.cs` | Écran dédié : mouvements, billetage, écart, PDF signé par le caissier et le contrôleur. |

## 4. Plan conseillé

1. **Tout de suite (avant de couper Render)** : remplacement de l'hôte dans le relais Cloudflare (§1, P0), plus durées maximales et limite de débit.
2. **Version suivante** : clé d'idempotence par versement, confirmation d'encaissement, arrêté par horodatage, séparation des rôles et droit dédié aux prises en charge.
3. **Ensuite** : décision sur les frais non mensuels, reçus figés et duplicata, rapprochement Mobile Money, accessibilité et taille de fenêtre.
4. **Feuille de route** : multi-devise, pénalités et remises, échéanciers, relances groupées.

**Recette à prévoir :** coupure Wi-Fi juste après « Encaisser » (aucun second reçu), arrêté à midi puis encaissement l'après-midi, renouvellement de l'hôte à 55 min sur Cloudflare avec un client actif, compte Secrétariat qui tente d'annuler un reçu (doit être refusé après correction), lecture des écrans avec le Narrateur Windows et TalkBack.
