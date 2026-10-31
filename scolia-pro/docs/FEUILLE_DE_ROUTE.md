# Feuille de route et propositions

## Ce qui est livré dans SCOLIA Pro 1.0

- Connexion nominative, rôles, verrouillage, journal d'audit chaîné.
- Élèves, matricules, doublons, réinscriptions, prises en charge.
- Encaissement réparti, reçus numérotés avec QR et page de vérification publique, annulation motivée.
- Dépenses numérotées, arrêté de caisse, recouvrement par ancienneté.
- Rappels WhatsApp et SMS, sans envoi automatique.
- Scolarité : cours, évaluations, notes pondérées, présences, discipline, bulletins PDF.
- Personnel, bulletins de paie, paiement des salaires.
- Sauvegardes quotidiennes vérifiées, restauration, import du classeur V79.
- Icône de notification, service Windows, accès réseau local, paquet autonome.

## Priorités proposées pour la version 1.1

| Proposition | Valeur pour l'établissement | Effort estimé |
|---|---|---|
| Envoi réel de SMS par passerelle agréée | Les rappels partent sans action manuelle | Moyen |
| Portail parent en lecture seule (solde, reçus, présences) | Réduit les appels au secrétariat | Élevé |
| Mise à jour automatique signée | Corrections installées sans passer par chaque poste | Moyen |
| Signature numérique de l'exécutable | Pas d'alerte de sécurité de Windows | Faible (coût du certificat) |
| Sauvegarde externe chiffrée (cloud ou disque réseau) | Protection contre le vol ou l'incendie | Moyen |
| Export comptable standard (plan comptable OHADA) | Transmission directe au cabinet | Moyen |

## Pistes à plus long terme

- **Monnaies multiples** : USD et CDF avec taux de change daté.
- **Paiement mobile intégré** : réconciliation automatique avec les opérateurs de mobile money, par leur API.
- **Option PostgreSQL** pour un groupe scolaire avec de nombreux postes simultanés.
- **Photo et carte d'élève** imprimable avec QR.
- **Emploi du temps** et gestion des salles.
- **Tableaux de bord de direction** : indicateurs comparés d'une année sur l'autre.
- **Application tablette** pour les appels de présence hors réseau, synchronisée à la reconnexion.
- **Tests de charge** publiés pour fixer la taille maximale recommandée par poste.

## Questions pour l'établissement

1. Combien de postes utiliseront SCOLIA en même temps, et lesquels sont en réseau ?
2. Les parents doivent-ils consulter leur solde en ligne, et avec quel outil (SMS, WhatsApp, portail) ?
3. Quel opérateur de mobile money et quelle passerelle SMS utilisez-vous ?
4. Faut-il gérer une seconde monnaie, et quel taux de change appliquer ?
5. Le plan comptable à transmettre au cabinet est-il le plan OHADA ?
6. Préférez-vous une sauvegarde externe chiffrée vers un cloud, ou vers un disque sur place ?
7. Acceptez-vous un coût de certificat de signature pour éviter les alertes Windows à chaque installation ?

## Décisions à prendre avant la version 1.1

- Le paquet est-il distribué par téléchargement, ou par clé USB dans chaque établissement ?
- Le dépôt GitHub reste-t-il public ? La présence de données réelles dans l'historique impose de le rendre privé ou de purger l'archive `SCOLIA OK.zip`.
