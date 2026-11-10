"""Narration avec Kyutai TTS (modèle et voix sous licence CC BY 4.0) : voix naturelles, une femme et un homme francophones."""
import inspect
import json
import sys
import time
from pathlib import Path

import numpy as np
import sphn
import torch
from huggingface_hub import hf_hub_download, list_repo_files
from moshi.models.loaders import CheckpointInfo
from moshi.models.tts import DEFAULT_DSM_TTS_REPO, DEFAULT_DSM_TTS_VOICE_REPO, TTSModel

out = Path(sys.argv[1])
out.mkdir(parents=True, exist_ok=True)
torch.set_num_threads(max(1, torch.get_num_threads()))


def f0(path):
    """Hauteur médiane de la voix (Hz), par autocorrélation sur les trames voisées."""
    pcm, sr = sphn.read(path)
    x = pcm[0] if pcm.ndim > 1 else pcm
    win, hop = int(0.04 * sr), int(0.01 * sr)
    vals = []
    for i in range(0, len(x) - win, hop):
        fr = x[i:i + win] - x[i:i + win].mean()
        if np.sqrt((fr ** 2).mean()) < 0.02:
            continue
        ac = np.correlate(fr, fr, "full")[win - 1:]
        lo, hi = int(sr / 320), int(sr / 70)
        k = lo + int(np.argmax(ac[lo:hi]))
        if ac[k] > 0.35 * ac[0]:
            vals.append(sr / k)
    return float(np.median(vals)) if len(vals) > 30 else 0.0, len(x) / sr


files = list_repo_files(DEFAULT_DSM_TTS_VOICE_REPO)
dossiers = sorted({f.split("/")[0] for f in files})
print("Dossiers de voix :", dossiers)
fr = [f for f in files if f.endswith(".wav") and ("/fr/" in f or "/fr_" in f or "french" in f.lower())]
print(len(fr), "voix françaises :", fr[:80])
mesures = []
for f in fr[:60]:
    try:
        hz, dur = f0(hf_hub_download(DEFAULT_DSM_TTS_VOICE_REPO, f))
        mesures.append({"voix": f, "f0": round(hz, 1), "duree": round(dur, 1)})
    except Exception as e:  # noqa: BLE001
        print("ignorée", f, e)
print(json.dumps(mesures, indent=1))
femmes = sorted([m for m in mesures if 175 <= m["f0"] <= 250], key=lambda m: -m["duree"])
hommes = sorted([m for m in mesures if 90 <= m["f0"] <= 140], key=lambda m: -m["duree"])
choix = {"elle": femmes[0]["voix"], "lui": hommes[0]["voix"]}
# Même voix, version débruitée quand elle existe : un son plus propre.
choix = {k: (v[:-4] + "_enhanced.wav" if v[:-4] + "_enhanced.wav" in files else v) for k, v in choix.items()}
print("Choix :", choix)
(out / "voix-choisies.json").write_text(json.dumps({"choix": choix, "mesures": mesures}, indent=1, ensure_ascii=False))

kwargs = {"n_q": 32, "temp": 0.6, "device": "cpu"}
if "dtype" in inspect.signature(TTSModel.from_checkpoint_info).parameters:
    kwargs["dtype"] = torch.float32
model = TTSModel.from_checkpoint_info(CheckpointInfo.from_hf_repo(DEFAULT_DSM_TTS_REPO), **kwargs)
voices = {k: model.make_condition_attributes([model.get_voice_path(v)], cfg_coef=2.0) for k, v in choix.items()}

narration = json.loads(Path(__file__).with_name("narration.json").read_text(encoding="utf-8"))
def speak(text, voice):
    entries = model.prepare_script([text], padding_between=1)
    result = model.generate([entries], [voice])
    with model.mimi.streaming(1), torch.no_grad():
        return np.concatenate([np.clip(model.mimi.decode(fr[:, 1:, :]).cpu().numpy()[0, 0], -1, 1)
                               for fr in result.frames[model.delay_steps:]], axis=-1)


def trim(pcm, sr):
    """Retire le silence de début et de fin, en gardant un léger souffle."""
    loud = np.where(np.abs(pcm) > 0.02)[0]
    if len(loud) == 0:
        return pcm
    return pcm[max(0, loud[0] - int(0.05 * sr)):loud[-1] + int(0.12 * sr)]


import re

for line in narration:
    start = time.time()
    sr = model.mimi.sample_rate
    # Une phrase à la fois : le modèle ne saute ni ne coupe la fin d'une réplique longue.
    phrases = [p for p in re.split(r"(?<=[.!?:])\s+", line.get("tts", line["texte"])) if p.strip()]
    gap = np.zeros(int(0.32 * sr), dtype=np.float32)
    parts = []
    for phrase in phrases:
        parts += [trim(speak(phrase, voices[line["voix"]]), sr), gap]
    pcm = np.concatenate(parts[:-1])
    target = out / f"{line['id']}-{line['voix']}.wav"
    sphn.write_wav(str(target), pcm, model.mimi.sample_rate)
    print(target, f"{len(pcm) / model.mimi.sample_rate:.1f} s en {time.time() - start:.0f} s", flush=True)
