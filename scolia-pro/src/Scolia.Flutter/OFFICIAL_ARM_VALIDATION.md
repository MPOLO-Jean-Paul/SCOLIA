# Contrôle du candidat Android officiel sur téléphone ARM

Cette procédure est complémentaire au test de migration SQLite sur émulateur isolé. Elle ne modifie ni la clé, ni l'identifiant Android, ni le canal de publication. La branche de validation ne doit pas être fusionnée avant la fin des compilations du candidat en cours si l'on souhaite conserver son SHA.

## Préparer le contrôle

- Télécharger l'APK de la dernière release stable et l'APK **officiel signé** de l'artefact de la compilation du candidat. Ne pas prendre l'APK fluttervalidation ni un APK signé pour test.
- Vérifier sur GitHub Actions que le candidat provient bien du commit de publication attendu ; relever le SHA complet et comparer les sommes SHA-256 des artefacts.
- Employer un téléphone Android ARM dédié, sans compte ni installation SCOLIA réelle, sans connexion à un établissement réel, débogage USB activé. Activer le mode avion et désactiver Wi-Fi et données mobiles ; retirer toute connexion réseau additionnelle.
- Utiliser Python 3.11 ou plus récent, adb et les Android SDK Build Tools (aapt, apksigner, dexdump dans le même dossier).

Depuis le répertoire Scolia.Flutter :

```sh
python3 android_bridge/official_arm_upgrade.py \
  --serial SERIAL_TEST --previous /chemin/ancien.apk \
  --candidate /chemin/candidat.apk --commit SHA_COMPLET_DU_CANDIDAT \
  --aapt /chemin/build-tools/aapt --apksigner /chemin/build-tools/apksigner \
  --report preuve-arm.json --isolated-clean-arm-device
```

Le script contrôle le certificat historique, le package, la progression des versions, les bibliothèques Flutter et les assets, l'installation avec adb install -r, le démarrage du vrai canal .NET et la reprise hors connexion. Il n'efface aucune donnée, ne désinstalle aucun paquet et ne publie rien. Il refuse une installation SCOLIA préexistante. Le rapport ne constitue pas une approbation du déploiement et le SHA indiqué par l'opérateur doit être rapproché de l'artefact du workflow.

## Avant d'autoriser la diffusion 1.3.0

- Vérifier la réussite de la migration chiffrée sur émulateur, puis répéter un parcours de migration avec un établissement et des comptes exclusivement fictifs sur un téléphone dédié, avec une sauvegarde vérifiée. Vérifier le cache SQLite, le compte hors ligne, la file des opérations et la reprise.
- Vérifier sur téléphone : connexion, inscription, paiement partiel, absence de paiement en double après reconnexion, journal et exports, cartes d'accès (modèle inchangé), présences, droits et verrouillage, retour depuis les écrans natifs.
- Contrôler la provenance et le SHA-256 exact du candidat signé ARM, ainsi que les résultats de compilation et les tests Windows.
- Seul le responsable du déploiement peut renseigner SCOLIA_ROLLOUT_APPROVED_SHA pour le commit exact après ces validations. Si un nouveau commit modifie le candidat, répéter les vérifications. Ne jamais utiliser une approbation fictive ou supprimer le verrou de diffusion.

Le script ne démontre pas à lui seul la conservation des données métier ni la parité graphique complète. Aucune opération de production ne doit servir de donnée de test.
