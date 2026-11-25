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
