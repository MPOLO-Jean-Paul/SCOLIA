# Audit SCOLIA V79 — performances générales

## Objectif

Réduire la latence perceptible dans les actions ordinaires de SCOLIA sans supprimer
les contrôles de cohérence, les journaux ni les protections de récupération. L'audit
a porté en priorité sur la navigation, les recherches, l'affichage des grilles,
l'encaissement, la modification d'un dossier et les accès Excel/VBA.

## Goulots d'étranglement corrigés

### 1. Encaissement

L'ancien chemin pouvait reconstruire une grande partie du modèle de données avant
un paiement, puis reparcourir l'historique. V79 recherche d'abord le matricule dans
la seule colonne nécessaire, lit uniquement les champs de son dossier et effectue
une passe légère sur les identifiants/matricules des paiements. Les colonnes de détail
ne sont lues que pour les lignes appartenant à l'apprenant concerné.

### 2. Modification d'un apprenant ayant déjà payé

Le verrou historique « paiement présent => promotion/année figées » a été supprimé.
Le dossier complet reste modifiable. Pour préserver l'audit comptable, la promotion
des anciens reçus n'est pas réécrite ; les nouveaux paiements utilisent la promotion
courante du dossier. L'identité affichée sur les paiements liés est actualisée de
manière ciblée et bénéficie d'un rollback ciblé en cas d'échec.

La règle de prise en charge reste volontairement indépendante : elle peut conserver
le verrou année/promotion prévu par ce processus métier.

### 3. Transactions et recalculs Excel

Lorsqu'une transaction externe de stockage possède déjà une copie de récupération,
le module de fiabilité ne crée plus une seconde copie identique. Le recalcul final
porte sur le classeur concerné au lieu de demander un recalcul global de toutes les
instances/formules Excel. Les formules de situation ne sont étendues que lorsque la
table des apprenants grandit.

### 4. Index et actualisation différentielle en mémoire

Des index par matricule et par identité normalisée sont maintenus pour les recherches
ponctuelles. Après un encaissement ordinaire, SCOLIA ne reconstruit plus toutes les
collections d'apprenants, de paiements et de soldes : le dossier concerné est modifié
en place, les anciens reçus correspondants sont actualisés par identifiant et seuls
les nouveaux reçus/soldes sont ajoutés. Les index restent ainsi immédiatement chauds.

Une correction structurelle rare (suppression d'un reçu, réaffectation de mois,
changement des clés d'identité/promotion) conserve un repli complet et sûr. Les
fonctions gardent également un repli par parcours lorsque l'index n'est pas disponible,
ce qui protège les appels isolés et les anciens chemins.

### 5. Grilles et mises en page

`AutoSizeRowsMode` n'est plus laissé actif en permanence sur les grilles génériques.
La hauteur visible est recalculée quand les données changent ; le redimensionnement,
le défilement et les rafraîchissements de présentation n'imposent plus une mesure
complète à répétition.

Les recalculs de disposition des pages modernes et des panneaux responsifs sont
regroupés sur une trame de 16 ms. Plusieurs événements successifs deviennent donc une
seule passe de layout.

### 6. Recherche et filtrage

La recherche principale, la Scolarité et l'interface Cours/Attributions regroupent
les frappes sur une fenêtre courte de 45 ms. En Scolarité, le tri, les promotions et
les matricules en double sont mis en cache. Une frappe de recherche n'appelle plus la
synchronisation disque de la discipline ; cette synchronisation reste effectuée lors
des vrais rafraîchissements de données.

## Opérations volontairement plus lourdes

Les opérations explicitement globales restent globales : connexion/chargement initial,
actualisation complète demandée par l'utilisateur, consolidation, export complexe,
correction/migration et certaines synchronisations réseau. Les déplacer hors du fil
Excel demanderait une refonte d'architecture plus risquée et n'est pas confondu avec
les clics ordinaires optimisés dans V79.

## Diagnostic

Les mesures de l'application sont conservées dans :

`%LOCALAPPDATA%\ITMCD\diagnostics\performance-v79.csv`

Elles permettent d'identifier une éventuelle latence restante liée au volume réel du
classeur, au stockage réseau, à l'antivirus ou au matériel du poste.
