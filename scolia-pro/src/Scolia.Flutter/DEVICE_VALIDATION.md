# Validation Android sur appareil dédié

Le contrôle statique du paquet ne suffit pas à prouver le démarrage Flutter. Ce test vérifie l'installation, l'apparition d'une première image Flutter, la réponse du canal C# à la commande initiale et la reprise après passage à l'arrière-plan.

## Prérequis

- Téléphone ARM de test, sans installation SCOLIA officielle ni expérimentale, isolé de tout serveur d'établissement réel.
- Débogage USB activé, Android SDK (adb, aapt et apksigner), Python 3.
- APK expérimental construit depuis cette branche avec les marqueurs de validation, signé pour test et identifié com.jpservices.scolia.fluttervalidation.
- Aucun compte ou fichier de données scolaire réel. Le script ne désinstalle ni n'efface aucune application.

Exécution :

```bash
python android_bridge/device_smoke.py --serial SERIAL_TEST --apk SCOLIA_Flutter_VALIDATION.apk --aapt /chemin/aapt --apksigner /chemin/apksigner --isolated-device
```

Le code de sortie non nul interdit de considérer le test comme réussi. Si une boîte de dialogue Android empêche l'affichage, le test peut échouer et doit être diagnostiqué sur le téléphone. Les seuls marqueurs ajoutés ne contiennent aucune donnée métier, identité ou secret. Ce test n'est pas un test d'encaissement, de connexion, de synchronisation de données ou de migration de signature. Ces parcours nécessitent encore une base et des comptes de test conformément à la PR 188.

## Émulateur CI x86_64

Le workflow séparé construit un APK de validation x86_64, distinct de l'APK ARM destiné au téléphone. Il installe l'application sur un émulateur jetable, vérifie la réponse initiale du moteur et la reprise avec Wi-Fi et données mobiles désactivés. Ce test ne prouve pas que les encaissements sont correctement synchronisés : il vérifie le démarrage et le canal natif sans réseau. Les paramètres de production restent inchangés.

## Contrôle de mise à jour officielle

`verify_release_upgrade.py` compare le package, la version et les empreintes des certificats des deux APK signés et exige la présence de Flutter pour ARM. Il n'utilise aucune clé privée. La réussite ne remplace pas un test de migration de données sur une installation de test.

## Continuité des mises à jour

Le mode candidat `ScoliaFlutterOfficial=true` utilise le package com.jpservices.scolia et le manifeste de production, sans les comportements réservés au paquet de validation. Il nécessite une compilation Release avec clé de distribution. Le script officiel ne l'active pas par défaut ; l'activation ne pourra intervenir qu'après validation de l'émulateur, d'un téléphone ARM et de la migration des données. Les versions produit/Android restent alignées et le versionCode reste géré par publier-android.sh. Le workflow existant compare désormais, pour une nouvelle version officielle, le véritable APK signé à la dernière release stable du client. Aucun changement de canal GitHub Releases ni de secret de signature.
