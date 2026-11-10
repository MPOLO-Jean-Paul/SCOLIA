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


gap = np.zeros(int(0.32 * sr), dtype=np.float32)
for line in narration:
    if line["voix"] == "elle":
        continue
    parts = []
    for phrase in [p for p in re.split(r"(?<=[.!?:])\s+", line.get("tts", line["texte"])) if p.strip()]:
        parts += [speak(phrase), gap]
    (out / f"{line['id']}-lui.wav").unlink(missing_ok=True)
    target = out / f"{line['id']}-elle.wav"
    sphn.write_wav(str(target), np.concatenate(parts[:-1]), sr)
    print(target, asr.transcribe(str(target), language="fr", fp16=False)["text"], flush=True)
