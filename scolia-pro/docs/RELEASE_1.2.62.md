# SCOLIA Pro 1.2.62 — Accès Avalonia cohérent sur Windows et Android

## Périmètre

Cette version ne modifie pas le métier scolaire : connexion, jumelage, récupération et biométrie ont les mêmes contrats et droits que la 1.2.61. Les travaux pédagogiques des PR #129 à #135 restent indépendants et non activés.

## Présentation de l'accès

- Écran d'identification : hiérarchie plus nette, carte et champs de tailles cohérentes, contraste du texte conservé, bouton de connexion explicitement accessible.
- Responsive : marges calibrées selon l'espace disponible ; écran Android plus étroit et clavier virtuel pris en compte par les classes `compact`, `tiny`, `short` et `micro` existantes.
- Écran de jumelage sur ordinateur : boutons, champs, espacements et carte alignés sur la connexion SCOLIA.
- Jumelage Android : même langage graphique, navigation compacte sans défilement, QR immédiatement disponible.

## Finitions d'interface inspirées de Flutter (sans moteur Flutter)

La version utilise toujours **Avalonia/C#**, pas Dart ou le moteur Flutter. Les techniques transposables sont :

- **Material 3 / Cupertino :** composants et palette SCOLIA homogènes, avec les contrôles Avalonia existants (pas de deuxième bibliothèque visuelle).
- **AnimatedContainer / InkWell :** transitions de couleurs et opacité (120–160 ms, `CubicEaseOut`) sur les boutons, les champs et la navigation mobile ; retour immédiat au toucher.
- **Widgets réutilisables :** styles `Controls.axaml` pour les formulaires métiers et `Phone.axaml` pour les onglets Android ; aucun clonage du code d'authentification.
- **MediaQuery / écrans adaptatifs :** classes `compact`, `tiny`, `short` et `micro` déjà pilotées par `AuthLayout`. Le jumelage Android reste organisé en pages courtes sans défilement.
- **Accessibilité :** état de focus visible, boutons tactiles préservés et étiquettes d'automatisation. Aucune animation permanente ni transition de largeur/hauteur qui provoquerait un débordement.
- **Performances :** aucune dépendance Dart, WebView ou Flutter ajoutée. Avalonia conserve son moteur de rendu. Impeller ne peut pas être activé comme s'il s'agissait d'un simple thème Avalonia.

Tests supplémentaires : `PremiumMotionViewsTests`, vérifiant transitions globales, onglets Android et focus des trois écrans d'accès. Le rendu réel (écrans compacts, sombre/clair, souris et clavier) doit toujours être vérifié lors de la capture CI avant publication.

## Compatibilité

- Moteur d'interface : **Avalonia** sur Windows et Android ; aucun moteur Flutter ajouté.
- API, mot de passe, empreinte, QR et droits : inchangés.
- Version : 1.2.62, `AndroidVersionCode = 182`. Pour une mise à jour installable sur les téléphones existants, le certificat de signature Android doit correspondre à la version 1.2.61. Le pipeline doit vérifier l'empreinte de signature avant diffusion.
- Fusion sur la branche `release/direct-apk-scolia-pro-20261103` : déclenche la chaîne officielle uniquement après validation des workflows et de la disponibilité de la clé de signature.

## Cible de cette livraison

**Android et Windows exclusivement.** Les workflows de vérification iOS sont désactivés pour les changements automatiques et ne peuvent être démarrés que manuellement, à la demande. Aucun IPA ni paquet iOS n'est requis pour la version 1.2.62.

## Contrôles avant publication

1. `SCOLIA Natif` : construction Avalonia, tests métier, captures préconnexion, APK Android et paquet Windows uniquement.
2. `SCOLIA Pro` : tests globaux, captures préconnexion responsive, vérification de la confidentialité.
3. Empreinte Android : identique à la distribution courante, jamais clé éphémère pour une mise à jour officielle.
4. Vérification des fichiers APK, installateur EXE et sommes SHA-256 publiés.

La présence de sources ou d'artefacts de validation dans une PR ne constitue **pas** une publication officielle.
