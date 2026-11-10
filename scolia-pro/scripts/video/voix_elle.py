"""Choisit une meilleure voix féminine (Kyutai TTS) puis relit les répliques « elle ».

Chaque voix féminine candidate lit la réplique d'ouverture ; Whisper la retranscrit. On retient la voix la plus nette
(moins d'erreurs de transcription), au débit naturel et à l'intonation vivante. Les trois meilleures sont gardées en échantillons.
"""
import json
import re
import sys
import time
from pathlib import Path

import numpy as np
import sphn
import torch
import whisper
from huggingface_hub import hf_hub_download, list_repo_files
from moshi.models.loaders import CheckpointInfo
from moshi.models.tts import DEFAULT_DSM_TTS_REPO, DEFAULT_DSM_TTS_VOICE_REPO, TTSModel

out = Path(sys.argv[1])
(out / "echantillons").mkdir(parents=True, exist_ok=True)
narration = json.loads(Path(__file__).with_name("narration.json").read_text(encoding="utf-8"))


def pitch(x, sr):
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
    return np.array(vals)


def words(t):
    return re.sub(r"[^a-zàâçéèêëîïôûùüÿœ' ]", " ", t.lower().replace("-", " ")).split()


def wer(ref, hyp):
    r, h = words(ref), words(hyp)
    d = np.arange(len(h) + 1)
    for i in range(1, len(r) + 1):
        prev, d[0] = d.copy(), i
        for j in range(1, len(h) + 1):
            d[j] = min(prev[j] + 1, d[j - 1] + 1, prev[j - 1] + (r[i - 1] != h[j - 1]))
    return d[-1] / max(1, len(r))


files = list_repo_files(DEFAULT_DSM_TTS_VOICE_REPO)
candidats = []
for f in files:
    if f.startswith("cml-tts/fr/") and f.endswith("_enhanced.wav"):
        pcm, sr = sphn.read(hf_hub_download(DEFAULT_DSM_TTS_VOICE_REPO, f))
        p = pitch(pcm[0], sr)
        if len(p) > 30 and 180 <= np.median(p) <= 250:
            candidats.append(f)
print(len(candidats), "voix féminines :", candidats, flush=True)

model = TTSModel.from_checkpoint_info(CheckpointInfo.from_hf_repo(DEFAULT_DSM_TTS_REPO), n_q=32, temp=0.6, device="cpu")
asr = whisper.load_model("small")
sr = model.mimi.sample_rate


def speak(text, voice):
    result = model.generate([model.prepare_script([text], padding_between=1)], [voice])
    with model.mimi.streaming(1), torch.no_grad():
        pcm = np.concatenate([np.clip(model.mimi.decode(fr[:, 1:, :]).cpu().numpy()[0, 0], -1, 1)
                              for fr in result.frames[model.delay_steps:]], axis=-1)
    loud = np.where(np.abs(pcm) > 0.02)[0]
    return pcm if len(loud) == 0 else pcm[max(0, loud[0] - int(0.05 * sr)):loud[-1] + int(0.12 * sr)]


test = "Bonjour et bienvenue. Je vous présente SCOLIA Pro, le logiciel de gestion scolaire conçu pour les écoles."
notes = []
for f in candidats[:14]:
    start = time.time()
    voice = model.make_condition_attributes([model.get_voice_path(f)], cfg_coef=2.0)
    pcm = speak(test, voice)
    name = f.split("/")[-1].replace("_enhanced.wav", "")
    path = out / "echantillons" / f"{name}.wav"
    sphn.write_wav(str(path), pcm, sr)
    text = asr.transcribe(str(path), language="fr", fp16=False)["text"]
    p = pitch(pcm, sr)
    dur = len(pcm) / sr
    note = {"voix": f, "erreurs": round(wer(test.replace("SCOLIA Pro", "scolia pro"), text), 3),
            "debit": round(len(test) / dur, 1), "f0": round(float(np.median(p)), 1) if len(p) else 0,
            "intonation": round(float(np.std(p) / np.median(p)), 3) if len(p) else 0, "transcription": text}
    # Netteté d'abord, puis débit proche de 14 caractères par seconde, puis une intonation vivante.
    note["score"] = round(note["erreurs"] * 10 + abs(note["debit"] - 14.5) * 0.15 + max(0, 0.14 - note["intonation"]) * 5, 3)
    notes.append(note)
    print(json.dumps(note, ensure_ascii=False), f"{time.time() - start:.0f} s", flush=True)

notes.sort(key=lambda n: n["score"])
(out / "classement.json").write_text(json.dumps(notes, indent=1, ensure_ascii=False))
for n in notes[3:]:
    (out / "echantillons" / (n["voix"].split("/")[-1].replace("_enhanced.wav", "") + ".wav")).unlink(missing_ok=True)
best = notes[0]["voix"]
print("Voix retenue :", best, flush=True)
voice = model.make_condition_attributes([model.get_voice_path(best)], cfg_coef=2.0)
gap = np.zeros(int(0.32 * sr), dtype=np.float32)
for line in narration:
    if line["voix"] != "elle":
        continue
    phrases = [p for p in re.split(r"(?<=[.!?:])\s+", line.get("tts", line["texte"])) if p.strip()]
    parts = []
    for phrase in phrases:
        parts += [speak(phrase, voice), gap]
    target = out / f"{line['id']}-elle.wav"
    sphn.write_wav(str(target), np.concatenate(parts[:-1]), sr)
    print(target, asr.transcribe(str(target), language="fr", fp16=False)["text"], flush=True)
