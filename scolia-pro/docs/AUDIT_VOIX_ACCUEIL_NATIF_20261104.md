# SCOLIA Pro — vérification ciblée des interfaces natives et de la recherche vocale
**Date : 4 novembre 2026** · Base : branche `release/direct-apk-scolia-pro-20261103`.

## Constat
- La recherche globale du bandeau Windows et les recherches contextuelles disposent déjà du bouton micro en Avalonia, mais sa visibilité dépend de `VoiceInput.Available`. Le client Windows n'enregistrait aucun fournisseur `Platform.RecognizeSpeechAsync` : le bouton restait masqué.
- L'accueil Windows réutilisait une bannière bleue en dégradé, des textes blancs et des boutons très marqués visuellement, davantage assimilables à un site web qu'à une application native.
- La grille de quatre raccourcis Android/iOS était toujours composée sur quatre colonnes, même lorsque l'écran était étroit.

## Corrections proposées dans cette branche
1. **Dictée Windows** : service vocal du système via `System.Speech` en français, à la demande du micro uniquement. Entrée limitée à une phrase, pas de commande système tirée de la reconnaissance, aucune donnée scolaire envoyée au moteur vocal par SCOLIA.
2. **Réutilisation des recherches métier** : le texte dicté est confié aux ViewModels actuels ; Accueil reste global, pages Apprenants / Reçus / Paiements restent filtrées par leurs propres recherches. Aucun changement d'autorisations ni d'API.
3. **Accueil Windows natif** : fond système clair/sombre, bord discret, initiales, salutation, informations du compte, statuts et deux actions, sans dégradé et avec une largeur adaptative.
4. **Mobile étroit** : grille de raccourcis capable de passer automatiquement de 4 à moins de colonnes selon la largeur disponible.

## Qualité / sécurité
- L'outil vocal sous Windows nécessite le composant Windows de reconnaissance vocale française (`fr-FR`) et un microphone autorisé. Si le composant manque, le champ textuel continue à fonctionner.
- L'écoute n'est jamais active au démarrage ou en arrière-plan : uniquement après un clic.
- Un seul micro Windows à la fois ; interruption d'un moteur bloqué après délai ; aucun jeton, mot de passe ou donnée de compte passé au processus de reconnaissance.
- Le stockage, la synchronisation, les contrôles d'accès, les paiements et les migrations restent inchangés dans cette intervention.

## Vérifications à effectuer avant une nouvelle publication
| Parcours | Résultat attendu |
| --- | --- |
| Windows, accueil clair et sombre | Contraste lisible, aucune bande web, salutation et état cohérents |
| Windows, 720 / 1024 / 1440 px | Carte sans chevauchement, actions toujours accessibles |
| Windows, recherche globale | Micro affiché ; phrase dictée → recherche globale, boutons et clavier inchangés |
| Windows, Apprenants et Encaisser | Micro affiché ; phrase dictée → filtre local du module seulement |
| Windows sans reconnaissance vocale française | Aucune perte de saisie ni blocage ; possibilité de saisir au clavier |
| Android/iOS 320 / 360 / 420 px | Les raccourcis se réorganisent sans coupe des icônes |
| Tous supports | Aucun changement aux paiements, permissions et données existantes |

**Limite explicite :** revue du code et modifications réalisées ; ni compilation .NET ni essai au microphone sur un poste Windows réel n'ont été exécutés dans cet environnement. La CI et les essais Windows/Android doivent valider l'intégration avant diffusion.
