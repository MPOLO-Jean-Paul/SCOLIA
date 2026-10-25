# Musique d'ambiance originale, synthétisée : nappe, basse, arpège de cloches, rythme doux, réverbération.
import numpy as np, wave, sys
SR = 48000; DUR = 48.0; N = int(SR * DUR)
t = np.arange(N) / SR
rng = np.random.default_rng(7)
BPM = 100; beat = 60 / BPM; bar = beat * 4
L = np.zeros(N); R = np.zeros(N)
def hz(m): return 440 * 2 ** ((m - 69) / 12)
def add(sig, start, pan=0.0, gain=1.0):
    i = int(start * SR); j = min(N, i + len(sig))
    if j <= i: return
    s = sig[: j - i] * gain
    L[i:j] += s * np.sqrt((1 - pan) / 2); R[i:j] += s * np.sqrt((1 + pan) / 2)
def lowpass(x, cut):
    X = np.fft.rfft(x); f = np.fft.rfftfreq(len(x), 1 / SR)
    return np.fft.irfft(X / np.sqrt(1 + (f / cut) ** 4), len(x))

# Accords : Do maj9, La m9, Fa maj7(9), Sol 6 ; un accord par mesure.
chords = [[48, 55, 59, 62, 64], [45, 52, 55, 59, 60], [41, 48, 52, 55, 57], [43, 50, 55, 59, 64]]
roots = [36, 33, 29, 31]
nbars = int(np.ceil(DUR / bar))
for b in range(nbars):
    c = chords[b % 4]; start = b * bar; length = bar + 1.2
    n = int(length * SR); tt = np.arange(n) / SR
    env = np.minimum(1, tt / 0.6) * np.minimum(1, np.maximum(0, (length - tt) / 1.2))
    pad = np.zeros(n)
    for m in c:
        for det in (-0.06, 0.06):
            f = hz(m + 12 + det)
            for k, a in ((1, 1), (2, .35), (3, .15), (4, .07)):
                pad += a * np.sin(2 * np.pi * f * k * tt + rng.uniform(0, 6.28))
    pad = lowpass(pad * env, 2600) / 26
    add(pad, start, -.25, .9); add(pad, start + .011, .25, .9)
    # basse : deux notes par mesure, après l'ouverture
    if start >= bar * 1.7:
        for k in (0, 2):
            n2 = int(beat * 2 * SR); t2 = np.arange(n2) / SR
            f = hz(roots[b % 4])
            e2 = (1 - np.exp(-t2 / .01)) * np.exp(-t2 / .7)
            bass = (np.sin(2 * np.pi * f * t2) + .3 * np.sin(4 * np.pi * f * t2)) * e2
            add(bass, start + k * beat, 0, .16)

# Arpège de cloches en croches, de 4 s à 44 s
pattern = [0, 2, 4, 3, 1, 3, 4, 2]
step = beat / 2; s = 4.1; k = 0
while s < 44:
    b = int(s // bar); c = chords[b % 4]
    m = c[1:][pattern[k % 8] % 4] + 24
    n = int(1.6 * SR); tt = np.arange(n) / SR
    f = hz(m)
    bell = (np.sin(2 * np.pi * f * tt) * np.exp(-tt / .55) + .25 * np.sin(2 * np.pi * f * 3.01 * tt) * np.exp(-tt / .18)
            + .12 * np.sin(2 * np.pi * f * 4.2 * tt) * np.exp(-tt / .08)) * (1 - np.exp(-tt / .003))
    vel = .7 + .3 * (k % 2 == 0)
    add(bell, s, .45 if k % 2 else -.45, .16 * vel)
    s += step; k += 1

# Rythme doux : grosse caisse feutrée et charleston léger
def kick():
    n = int(.4 * SR); tt = np.arange(n) / SR
    f = 50 + 70 * np.exp(-tt / .03)
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-tt / .14)
def hat():
    n = int(.08 * SR); tt = np.arange(n) / SR
    x = rng.standard_normal(n); x = x - lowpass(x, 7000)
    return x * np.exp(-tt / .018)
s = 4.1 + 0.0
while s < 43.5:
    pos = round((s - 4.1) / beat) % 4
    if pos in (0, 2): add(kick(), s, 0, .22)
    if s > 16.2: add(hat(), s + beat / 2, .2, .05)
    s += beat

# Souffles discrets aux changements de scène
for at in (4.1, 10.3, 16.3, 22.8, 29.3, 35.8, 41.8):
    n = int(1.2 * SR); tt = np.arange(n) / SR
    x = lowpass(rng.standard_normal(n), 2500); x -= lowpass(x, 300)
    env = np.sin(np.pi * np.clip(tt / 1.2, 0, 1)) ** 2
    add(x * env, at - .8, rng.uniform(-.4, .4), .05)

# Réverbération : réponse impulsionnelle synthétique de 2,2 s
def reverb(x, seed):
    r = np.random.default_rng(seed); n = int(2.2 * SR); tt = np.arange(n) / SR
    ir = r.standard_normal(n) * np.exp(-tt / .55); ir = lowpass(ir, 5000); ir /= np.sqrt(np.sum(ir ** 2))
    m = len(x) + n; size = 1 << (m - 1).bit_length()
    return np.fft.irfft(np.fft.rfft(x, size) * np.fft.rfft(ir, size), size)[: len(x)]
L = L + .32 * reverb(L, 1); R = R + .32 * reverb(R, 2)

# Fondus d'entrée et de sortie, crête à -1 dB
fade = np.minimum(1, t / .4) * np.minimum(1, np.maximum(0, (DUR - t) / 3.5))
L *= fade; R *= fade
peak = max(np.abs(L).max(), np.abs(R).max()); g = 10 ** (-1 / 20) / peak
pcm = (np.stack([L, R], 1) * g * 32767).astype(np.int16)
with wave.open(sys.argv[1], 'wb') as w:
    w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR); w.writeframes(pcm.tobytes())
print('ok', DUR)
