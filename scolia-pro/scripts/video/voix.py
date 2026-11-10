"""Synthèse vocale de la narration (voix françaises Piper, hors ligne) : un fichier WAV par réplique."""
import json
import sys
import wave
from pathlib import Path

from piper import PiperVoice

modeles = {"elle": sys.argv[1], "lui": sys.argv[2]}
sortie = Path(sys.argv[3])
sortie.mkdir(parents=True, exist_ok=True)
voix = {k: PiperVoice.load(v) for k, v in modeles.items()}
for replique in json.loads(Path(__file__).with_name("narration.json").read_text(encoding="utf-8")):
    v = voix[replique["voix"]]
    cible = sortie / f"{replique['id']}-{replique['voix']}.wav"
    with wave.open(str(cible), "wb") as wav:
        try:
            from piper import SynthesisConfig  # piper-tts 1.3
            v.synthesize_wav(replique["texte"], wav, syn_config=SynthesisConfig(length_scale=1.06))
        except ImportError:
            v.synthesize(replique["texte"], wav, length_scale=1.06, sentence_silence=0.25)  # piper-tts 1.2
    print(cible)
