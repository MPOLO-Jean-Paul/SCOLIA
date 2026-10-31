# SCOLIA V54

La fenêtre de connexion métier utilise une colonne : identité SCOLIA, établissement, instruction, compte, mot de passe et action principale. Le contenu défile verticalement si la fenêtre est trop petite. Le mot de passe conserve son bouton afficher/masquer et son masquage initial.

Les erreurs sont séparées du nom de l’établissement, effacées à la correction et le mot de passe est vidé après un refus. La sélection du compte sans protection masque le champ et efface le secret précédent. Les contrôles sont désactivés pendant la connexion.

Un seul indicateur global suit la vérification d’accès puis la préparation de l’accueil. Il fonctionne sur son propre thread UI et reste animé pendant les traitements synchrones. Les anneaux, lueurs, trait vertical coloré, cadre transparent magenta et curseur d’attente de connexion ont été supprimés. La progression est une barre horizontale indéterminée ; aucun pourcentage n’est inventé. Le temps écoulé apparaît après 12 secondes. Les indicateurs partagés de configuration utilisent également une animation linéaire.

L’authentification, les permissions et la configuration partagée conservent leurs traitements existants. Les corrections de syntaxe et le formulaire d’inscription V53 sont inclus.

Tests fournis : compilation et analyse PowerShell natives, transitions sur thread indépendant, contrôle des champs et thèmes du nouveau formulaire. Les contrôles statiques et l’archive ont été vérifiés sous Linux ; l’exécution WinForms et l’affichage Windows restent à confirmer sur Windows avec VERIFIER_V54.cmd.
