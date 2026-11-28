# Correction de compilation des champs mobiles

Le compilateur Avalonia a rejeté le sélecteur composé AcceptsReturn et la propriété VerticalContentAlignment commune à des types différents. Utiliser un style de classe explicite pour les zones multilignes et un alignement local dans le formulaire générique. Conserver la hauteur tactile, les marges, le contraste et les contrôles de saisie. Aucune suppression de test ou modification de signature. Validation CI à relancer.
