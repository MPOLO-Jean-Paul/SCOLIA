# Candidat SCOLIA Pro 1.3.0 — validation avant diffusion

Migration progressive Android vers Flutter Material 3, moteur métier .NET, SQLite et cartes d'accès conservés. Windows conserve l'interface native.

Le test CI de mise à niveau installe un paquet natif de référence compilé à partir du moteur partagé actuel, sans Flutter, avec un compte fictif, cache et une opération en attente chiffrés dans SQLite/Android Keystore. Il le met à jour sans désinstallation vers le paquet Flutter de test avec le même identifiant isolé et le même certificat de test, réseau Wi-Fi/mobile désactivé. Il exige le déchiffrement et la conservation de ces données, le démarrage Flutter et la reprise. Ce test ne prétend pas utiliser le binaire officiel 1.2.99 ni la clé historique.

Avant diffusion : valider le test de mise à niveau, construire le candidat officiel avec la clé historique et vérifier sa signature, son identifiant et les versions par comparaison avec l'APK officiel publié. Tester sur appareil Android ARM avec une installation de test et une sauvegarde vérifiée. Vérifier une inscription, un encaissement, la synchronisation et les exports de bout en bout. Ne pas remplacer la release 1.2.99 ni distribuer le paquet expérimental. Le numéro de version produit reste inchangé jusqu'à la validation du candidat.
