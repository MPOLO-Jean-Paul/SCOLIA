# SCOLIA Console 1.2.2 — Réglages natifs complets

- Configuration Cloudflare R2 directement dans l'application : écriture, relecture et suppression d'un fichier témoin avant enregistrement chiffré côté service.
- Champs de clés masqués, effacés après réussite et conservés en cas de refus ; une saisie plus récente faite pendant l'envoi est préservée.
- Retrait des identifiants R2 et proposition de sauvegarde à tous les établissements, avec confirmation en deux temps.
- Catalogue Cloudflare TURN en lecture seule, distinct des relais WSS utilisés par les écoles.
- Configuration, test de disponibilité et retrait du site Netlify ; le canal reste expérimental et désactivé conformément au service.
- Retour vers la rubrique d'origine, tableaux, cartes et formulaires adaptés aux petits écrans.

Vérification locale : 37 tests natifs réussis, 48 tests du Worker réussis. Les parcours R2 et Netlify utilisent le vrai Worker avec SQLite en mémoire et des fournisseurs simulés ; aucun secret ni service de production n'est utilisé par les tests.

Les builds Android et Windows passent sur GitHub Actions ; l'application reste ouverte sur un émulateur Android après son lancement. La console web est vérifiée dans cinq formats et thèmes, avec paiement unique et verrouillage pendant une lecture. L'APK et l'installateur de préversion ont été téléchargés et leurs empreintes contrôlées.

La publication officielle détecte les paramètres de signature absents avant de compiler. Les étapes suivantes contrôlent toujours les certificats et les fichiers signés ; la préversion reste sur son canal distinct.

Version Android : 6, affichage 1.2.2. La publication officielle reste soumise à la clé Admin permanente, à son empreinte et à la signature Windows Authenticode requises par le workflow. La clé du client SCOLIA Pro n'est jamais utilisée pour la console.
