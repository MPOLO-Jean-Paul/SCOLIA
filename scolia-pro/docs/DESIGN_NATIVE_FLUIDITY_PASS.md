# Fluidité native — passe après 1.3.5

Réutiliser les listes immuables des commandes de page, ne reconstruire les boutons que si leur nombre affiché ou leur contenu change. Éviter les notifications redondantes de filtre et de défilement, ignorer les callbacks du relais appartenant à un ancien cycle d'affichage, mesurer les titres avec la graisse réellement utilisée. Deux tests ciblent la stabilité et le tri des commandes. Aucun changement de résultat de recherche, de sécurité, de signature ou de cartes. Gains attendus, non mesurés sur matériel physique ; validation CI à effectuer.
