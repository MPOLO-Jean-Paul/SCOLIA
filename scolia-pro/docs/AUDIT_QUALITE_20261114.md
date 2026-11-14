# SCOLIA Pro — audit de qualité du 14 novembre 2026

Revue de l'ensemble du projet : compilation, tests de toutes les parties, appel de toutes les routes de
lecture de l'API pour chaque type d'établissement, et parcours de tous les écrans de chaque rôle, sur
ordinateur et en format téléphone (ITM/IM avec données de démonstration, école primaire, université LMD).

## 1. Ce qui a été vérifié

| Contrôle | Portée | Résultat |
| --- | --- | --- |
| Compilation de la solution | serveur, poste Windows, Android partagé, relais | sans erreur ; 4 avertissements Avalonia corrigés |
| Interface web | typage, tests, construction | sans erreur |
| Relais Cloudflare, console propriétaire, sauvegarde Netlify | tests et vérifications | sans erreur |
| Tests de bout en bout (navigateur) | 33 parcours | réussis |
| Routes de lecture de l'API | 6 profils : ITM/IM, maternelle, primaire, secondaire, complexe scolaire, université | 3 pannes corrigées, test ajouté |
| Écrans | secrétariat, comptabilité, enseignant ; ordinateur et téléphone | défauts ci-dessous corrigés ; aucun texte qui déborde sur téléphone |

## 2. Défauts relevés et corrigés

**Pannes du serveur**
1. *Convocation disciplinaire introuvable* : l'API répondait par une erreur interne (500). Elle répond
   maintenant « Décision disciplinaire introuvable » (404).
2. *Sauvegarde en ligne sans inscription auprès de JP-Services* : la liste des copies en ligne et les
   autres opérations du nuage répondaient par une erreur interne. Elles expliquent désormais qu'il faut
   activer la licence de l'établissement.
3. *Internet ou console JP-Services injoignable* : toute coupure réseau vers la console donnait une
   erreur interne. Le serveur répond maintenant 503 avec un message clair, et le journal technique note un
   avertissement au lieu d'une erreur.

**Incompatibilités entre profils**
4. *Comptes enseignants* : dans une école ou une université, « Attribution des cours » ouvrait l'écran des
   cours ITM. Le bouton ouvre maintenant « Attribution des branches » (école) ou « Attribution des EC »
   (université).
5. *Privilège jury* : propre à la délibération ITM/IM, il apparaissait aussi dans les écoles et universités
   (bouton, compteur, case des formulaires, colonne). Il n'y apparaît plus.
6. *Nombre de cours d'un enseignant* : dans une école ou une université, l'enseignant apparaissait
   « 0 cours » malgré ses branches ou EC attribués. Le compte inclut maintenant les attributions du programme.
7. *Vocabulaire* : de nombreux écrans communs écrivaient « apprenant » dans une école ou une université
   (effacement des données, encaissement, cartes, présences, discipline, reçus, prises en charge, tableau
   de bord, recouvrement, raccourcis du téléphone, formulaires et messages). Ils disent maintenant « élève »
   ou « étudiant » selon le profil ; un ITM/IM garde « apprenant ».
8. *Interface web* : elle ignorait le profil. Le ruban adapte maintenant son vocabulaire, et « Cours et
   notes » (moteur ITM) n'est plus proposé à une école ou une université : un message les renvoie vers
   l'application SCOLIA. Un enseignant ou un étudiant connecté au navigateur arrive sur une page qui
   explique où se trouve son espace, au lieu d'un tableau de bord qu'il n'a pas le droit de lire.

**Fonctionnement**
9. *Réactiver le compte d'un enseignant* : l'action échouait, car le formulaire lisait un champ
   « Identifiant » qu'il ne contient pas pour un compte existant.
10. *Délibération de fin d'année (EPST) et délibération LMD* : les boutons ne suivaient pas l'étape. Par
    exemple, « Publier les résultats du S1 » restait le bouton principal après la publication. Désormais,
    l'étape suivante est toujours le bouton principal, et une étape franchie n'est plus proposée.

**Affichage**
11. *Bannière « Nouvelle sauvegarde » / « Nouvel encaissement »* : sur ordinateur, elle couvrait le titre de
    la page et son bouton principal pendant plusieurs secondes. Elle s'affiche maintenant en bas à droite ;
    sur téléphone, elle reste en haut comme les notifications du système.
12. *Textes indicatifs des champs* : Avalonia 12 a renommé l'élément interne du texte indicatif. Les styles
    de la connexion et des champs sur fond de couleur ne s'appliquaient plus. Ils sont rétablis, et
    l'attribut obsolète `Watermark` est remplacé par `PlaceholderText`.
13. *Cartes du téléphone* : un rang (« 1 », « 2 »…) s'affichait seul sous le nom de l'élève, sans libellé.
    Il s'affiche maintenant « Place : 1 » et le matricule reprend sa place de sous-titre.
14. *Organisation LMD* : les natures s'affichaient en anglais (« Faculty », « Department »,
    « TeachingUnit »). Elles s'affichent en français : Faculté, Département, Filière, UE, EC.

## 3. Tests ajoutés

- `ProfileRouteSweepTests` : pour chacun des six profils, le secrétariat appelle toutes les routes de lecture
  de l'API ; aucune ne doit répondre par une erreur du serveur.
- `VocabularyTests` (poste) et `terms.test.ts` (web) : vocabulaire de chaque profil.
- `access.test.ts` : un enseignant ou un étudiant arrive sur son espace dans le navigateur.

## 4. Points relevés, laissés en l'état

- *Données de démonstration ITM* : l'écran « Résultats » montre des moyennes (évaluations de démonstration),
  tandis que « Délibération » est vide (aucune cote de jury de démonstration). C'est le comportement attendu
  du moteur ITM, qui n'a pas été modifié.
- *Espaces enseignant et étudiant dans le navigateur* : la saisie des notes et la consultation des résultats
  restent dans l'application SCOLIA (Windows et Android) ; le navigateur l'explique.
- *APK Android* : les écrans sont partagés avec Android et vérifiés en format téléphone, mais l'APK se
  construit sur GitHub Actions et n'a pas été compilé ici.
