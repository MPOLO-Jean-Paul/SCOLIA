# SCOLIA Pro 1.3.2 — fluidité native

- Interface Avalonia et design de la 1.2.99 conservés. Flutter reste suspendu en production.
- Défilement à la molette : interruption immédiate par une nouvelle direction, le clavier, le pointeur ou le repositionnement d'une liste ; aucun mouvement résiduel sur une page masquée.
- Effets tactiles : une seule onde par cible, synchronisée avec les images du moteur de rendu, plus courte et sans allocation de tableau à chaque image.
- Apparition des cartes : plus courte et discrète, sans animations empilées sur les cartes imbriquées ou les tableaux ; annulation lorsque la vue est retirée.
- Chargement et actualisation : les notifications du relais sont regroupées, les vues masquées ne sont plus rafraîchies par ce mécanisme et les indicateurs respectent le réglage Animations.
- Aucun changement du moteur de paiement, de SQLite, de la signature historique ni des modèles d'export ou de cartes d'accès.
- Le gain perceptible doit être confirmé sur des appareils physiques ; aucune mesure de FPS n'est annoncée.
