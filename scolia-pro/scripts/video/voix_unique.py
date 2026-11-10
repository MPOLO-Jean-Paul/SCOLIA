"""Narration entière avec une seule voix féminine nette (Kyutai TTS, CC BY 4.0) : relit les répliques « lui » avec la voix retenue."""
import json
import re
import sys
from pathlib import Path

import numpy as np
import sphn
import torch
import whisper
from moshi.models.loaders import CheckpointInfo
from moshi.models.tts import DEFAULT_DSM_TTS_REPO, TTSModel

out = Path(sys.argv[1])
VOIX = "cml-tts/fr/2154_2576_000020-0003_enhanced.wav"
narration = json.loads(Path(__file__).with_name("narration.json").read_text(encoding="utf-8"))
model = TTSModel.from_checkpoint_info(CheckpointInfo.from_hf_repo(DEFAULT_DSM_TTS_REPO), n_q=32, temp=0.6, device="cpu")
asr = whisper.load_model("small")
sr = model.mimi.sample_rate
voice = model.make_condition_attributes([model.get_voice_path(VOIX)], cfg_coef=2.0)


def speak(text):
    result = model.generate([model.prepare_script([text], padding_between=1)], [voice])
    with model.mimi.streaming(1), torch.no_grad():
        pcm = np.concatenate([np.clip(model.mimi.decode(fr[:, 1:, :]).cpu().numpy()[0, 0], -1, 1)
                              for fr in result.frames[model.delay_steps:]], axis=-1)
    loud = np.where(np.abs(pcm) > 0.02)[0]
    return pcm if len(loud) == 0 else pcm[max(0, loud[0] - int(0.05 * sr)):loud[-1] + int(0.12 * sr)]


def words(t):
    return set(re.sub(r"[^a-zàâçéèêëîïôûùüÿœ ]", " ", t.lower().replace("-", " ").replace("'", " ")).split())


def checked(phrase):
    """Une phrase, refaite jusqu'à quatre fois tant que la transcription n'en retrouve pas presque tous les mots."""
    best, best_score = None, -1.0
    for attempt in range(4):
        pcm = speak(phrase)
        sphn.write_wav("/tmp/essai.wav", pcm, sr)
        heard = words(asr.transcribe("/tmp/essai.wav", language="fr", fp16=False)["text"])
        expected = words(phrase.replace("SCOLIA", "scolia"))
        score = len(expected & heard) / max(1, len(expected))
        print(f"   essai {attempt + 1} : {score:.2f} · {phrase[:60]}", flush=True)
        if score > best_score:
            best, best_score = pcm, score
        if score >= 0.9:
            break
    return best


only = set(sys.argv[2].split(",")) if len(sys.argv) > 2 else None
gap = np.zeros(int(0.32 * sr), dtype=np.float32)
for line in narration:
    if only is not None and line["id"] not in only:
        continue
    if only is None and line["voix"] == "elle":
        continue
    parts = []
    for phrase in [p for p in re.split(r"(?<=[.!?:])\s+", line.get("tts", line["texte"])) if p.strip()]:
        parts += [checked(phrase), gap]
    (out / f"{line['id']}-lui.wav").unlink(missing_ok=True)
    target = out / f"{line['id']}-elle.wav"
    sphn.write_wav(str(target), np.concatenate(parts[:-1]), sr)
    print(target, asr.transcribe(str(target), language="fr", fp16=False)["text"], flush=True)
