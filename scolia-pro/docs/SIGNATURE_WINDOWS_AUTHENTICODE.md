# Signature Windows Authenticode — SCOLIA Pro

## Objectif

Les builds de validation GitHub Actions peuvent rester **non signés**, mais **la distribution officielle Windows doit être bloquée sans certificat de signature de code valide**. Les deux exécutables natifs `SCOLIA.exe`, `SCOLIA-Serveur.exe`, puis le programme Inno Setup `SCOLIA_Pro_Installation_<version>_Windows_x64.exe`, sont signés et vérifiés dans le workflow natif sur la branche officielle.

Les empreintes SHA-256 des ZIP et EXE sont recalculées **après** signature.

## Préparation des secrets (propriétaire du dépôt)

1. Obtenir un certificat de signature de code Windows auprès d'une autorité approuvée, au nom de l'éditeur légal ; un certificat Android n'est pas compatible.
2. Si le certificat est exportable, produire un PFX protégé par un mot de passe fort. Conserver les fichiers privés hors de GitHub et des artefacts de CI.
3. Dans GitHub > Settings > Secrets and variables > Actions, enregistrer les secrets:
   - `SCOLIA_WINDOWS_SIGNING_PFX` : **Base64 du PFX** (contenu du fichier, non nom ni chemin).
   - `SCOLIA_WINDOWS_SIGNING_PASSWORD` : mot de passe du PFX.
4. Ajouter facultativement la variable Actions `SCOLIA_WINDOWS_CERT_THUMBPRINT` avec l'empreinte SHA-1 publique du certificat attendu. Elle bloque les rotations inattendues.
5. Protéger les branches et la gestion des secrets et, si disponible, utiliser un environnement de publication avec approbation. **Ne pas transmettre les valeurs secrètes dans ce chat, un commit, une issue ou un log.**

**Remarque :** une clé EV non exportable ou un fournisseur HSM/cloud nécessite une adaptation vers son service de signature. Ne jamais affaiblir la politique matérielle pour obtenir un PFX.

## Vérifications effectuées par le script

- Refuse un mot de passe ou un PFX absent.
- Exige un certificat avec clé privée, usage de signature de code et période de validité correcte.
- Vérifie l'empreinte attendue si elle est définie.
- Importe temporairement le certificat dans le magasin Windows de l'utilisateur de build.
- Lance `signtool sign` avec SHA-256 et horodatage RFC 3161 puis `signtool verify /pa /all /v`.
- Vérifie `Get-AuthenticodeSignature` et retire le certificat et le fichier PFX temporaire.
- Interrompt la publication officielle en cas d'échec.

## Compatibilité et limites

- Tant que les deux secrets ne sont pas configurés, un `push` sur la branche officielle déclenchant la publication Windows **échouera volontairement**, plutôt que de publier un installateur non signé.
- Les PR et branches de test n'utilisent pas ces secrets et ne doivent jamais publier des releases officielles.
- Une signature valide ne prouve pas que le logiciel est sans défaut ; l'audit, les sauvegardes, l'anti-malware et les tests de mise à jour restent nécessaires.
- Le certificat est une identité de publication. Son renouvellement exige une procédure contrôlée et des tests Windows avant distribution.
- Le workflow qui publie les binaires Android doit également conserver **la même signature Android historique**. Voir la PR #51.
