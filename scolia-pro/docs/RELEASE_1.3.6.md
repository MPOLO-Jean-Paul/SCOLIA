# SCOLIA Pro 1.3.6 — fluidité des interfaces natives

- Collections de commandes stabilisées : éviter les recréations inutiles lors des changements de taille.
- Filtrage contextuel : notifications uniquement lorsqu'une valeur change.
- Défilement desktop : éviter les callbacks et mises à jour graphiques sans mouvement utile.
- Actualisations : écarter les callbacks d'un ancien cycle de visibilité.
- Synchronisation des listes : index des valeurs au premier écart, conserver les lignes inchangées et les comparateurs métier.
- Sept nouveaux tests, dont 200 photographies successives avec doublons et valeurs nulles.

Windows et Android natifs. Les modèles des cartes, les données, les règles métier et le circuit de signature restent inchangés. VersionCode minimal 3630481. Les tests automatisés doivent valider la révision finale ; les gains de performance ne sont pas mesurés sur appareil physique.
