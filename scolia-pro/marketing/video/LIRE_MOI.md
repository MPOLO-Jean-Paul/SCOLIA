# Vidéo publicitaire SCOLIA Pro

Sources de la vidéo de 51 secondes, à 60 images par seconde, en deux formats : 16:9 et 9:16.

## Contenu

- `index.html` : l'animation. Une fonction `render(t)` dessine l'image de l'instant t.
- Chaque écran est dans un appareil : téléphone, tablette ou ordinateur portable.
- Une loupe agrandit la zone utile de l'écran, à partir de la capture à 3x.
- `render.js` : rend chaque image avec Playwright et l'encode avec ffmpeg.
- `preview.js` : rend quelques instants en PNG, pour vérifier un cadrage.
- `music.py` : compose et synthétise la musique avec numpy.
- `assets/` : captures de la vraie application, sur l'école de démonstration.

## Prérequis

- Node avec Playwright et Chromium, Python 3 avec numpy, ffmpeg.
- La police Inter, copiée dans `assets/` :

```
for w in Regular Medium SemiBold Bold ExtraBold; do cp /usr/share/fonts/opentype/inter/Inter-$w.otf assets/; done
```

## Fabrication

```
node render.js h silencieux-16x9.mp4
node render.js v silencieux-9x16.mp4
python3 music.py musique-brute.wav
ffmpeg -i musique-brute.wav -af loudnorm=I=-16:TP=-1.5:LRA=11 -ar 48000 musique.wav
ffmpeg -i silencieux-16x9.mp4 -i musique.wav -map 0:v -map 1:a -c:v copy -af highpass=f=30 -c:a aac -b:a 192k -shortest -movflags +faststart SCOLIA_Pro_publicite_16x9.mp4
```

- Même dernière commande pour le format 9:16.
- Contrôle d'un cadrage : `node preview.js v prev 9.5 21 40`.

## Captures

- Elles viennent de `tests/Scolia.Bureau.Captures`, lancé sur un serveur de démonstration.
- Téléphone : `SCOLIA_MOBILE=1 SCOLIA_TAILLE=412x915 SCOLIA_ECHELLE=3 SCOLIA_ASTUCES=0`.
- Tablette : `SCOLIA_MOBILE=1 SCOLIA_TAILLE=1280x800 SCOLIA_ECHELLE=2 SCOLIA_ASTUCES=0`.
- Ordinateur : `SCOLIA_TAILLE=1440x900 SCOLIA_ECHELLE=2 SCOLIA_ASTUCES=0`.
- `p3-` est la capture du téléphone à 3x, pour la loupe. `p2-` est la même réduite à 2x, pour l'écran de l'appareil.
