#!/usr/bin/env python3
"""Synthesizes the game's sound effects into ../Sounds as 16-bit mono WAV files.

Nothing is sampled from the original machine: every effect is built here from oscillators and noise,
so the sounds can be tweaked and regenerated. Run it from anywhere: python3 tools/make_sounds.py
(The spoken lines come from tools/make_voice.sh.)
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Sounds")
random.seed(1977)


def write(name, samples, volume=0.6):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def n(seconds):
    return int(RATE * seconds)


def env(i, total, attack=0.005, release=1.0):
    """Linear attack, then a decay shaped by `release` (higher = faster fade)."""
    t = i / RATE
    a = min(1.0, t / attack) if attack > 0 else 1.0
    return a * (1 - i / total) ** release


def sweep(seconds, f0, f1, wave_fn, release=1.0, attack=0.005):
    total, phase, out = n(seconds), 0.0, []
    for i in range(total):
        f = f0 + (f1 - f0) * i / total
        phase += f / RATE
        out.append(wave_fn(phase) * env(i, total, attack=attack, release=release))
    return out


def square(p):
    return 1.0 if (p % 1) < 0.5 else -1.0


def tri(p):
    return 4 * abs((p % 1) - 0.5) - 1


def saw(p):
    return 2 * (p % 1) - 1


def sine(p):
    return math.sin(2 * math.pi * p)


def noise(seconds, release=1.0, smooth=0.0, attack=0.002):
    total, last, out = n(seconds), 0.0, []
    for i in range(total):
        last = last * smooth + random.uniform(-1, 1) * (1 - smooth)
        out.append(last * env(i, total, attack=attack, release=release))
    return out


def mix(*tracks):
    length = max(len(t) for t in tracks)
    return [sum(t[i] for t in tracks if i < len(t)) for i in range(length)]


def scale(track, k):
    return [s * k for s in track]


def silence(seconds):
    return [0.0] * n(seconds)


os.makedirs(OUT, exist_ok=True)

# The X-wing's lasers: a sharp, falling "pew" with a buzzy edge.
total = n(0.2)
pew, phase = [], 0.0
for i in range(total):
    f = 2400 * (0.22 ** (i / total))
    phase += f / RATE
    pew.append((0.7 * square(phase) + 0.3 * saw(phase * 0.5)) * env(i, total, attack=0.002, release=1.2))
write("Laser.wav", pew, 0.3)

# A TIE fighter screaming past: a howl with a wide, fast wobble over rushing noise, its pitch falling
# as it goes by (the Doppler drop), swelling and dying away.
def scream(seconds, top, bottom, volume, name):
    total = n(seconds)
    out, p1, p2, last = [], 0.0, 0.0, 0.0
    for i in range(total):
        t = i / total
        f = top - (top - bottom) * (0.5 + 0.5 * math.tanh((t - 0.55) * 7))
        wob = 1 + 0.06 * math.sin(2 * math.pi * 31 * i / RATE)
        p1 += f * wob / RATE
        p2 += f * 1.5 * wob / RATE
        last = last * 0.6 + random.uniform(-1, 1) * 0.4
        swell = math.sin(math.pi * min(1, t * 1.15)) ** 1.5
        out.append((0.55 * saw(p1) + 0.3 * tri(p2) + 0.5 * last) * swell)
    write(name, out, volume)

scream(1.1, 900, 380, 0.4, "TieScream.wav")
scream(1.6, 520, 180, 0.5, "VaderScream.wav")

# Explosions, small to enormous.
write("TieBoom.wav", mix(noise(1.0, release=1.8, smooth=0.65), scale(sweep(1.0, 140, 40, tri, release=1.5), 0.8)), 0.7)
write("FireballBoom.wav", mix(noise(0.3, release=2.5, smooth=0.3), sweep(0.3, 700, 150, square, release=2)), 0.4)
write("GroundBoom.wav", mix(noise(0.8, release=2, smooth=0.75), scale(sweep(0.8, 100, 35, tri, release=1.6), 0.9)), 0.7)
write("TowerBoom.wav", mix(noise(1.1, release=1.8, smooth=0.6), scale(sweep(1.1, 300, 60, square, release=1.8), 0.4),
                           scale(sweep(1.1, 90, 30, tri, release=1.4), 0.8)), 0.75)

# A fireball leaving its gun: a short rising whoosh.
total = n(0.35)
whoosh, last = [], 0.0
for i in range(total):
    k = 0.3 + 0.6 * i / total
    last = last * (1 - k) + random.uniform(-1, 1) * k
    whoosh.append(last * math.sin(math.pi * i / total))
write("Fireball.wav", mix(whoosh, scale(sweep(0.35, 150, 420, tri, release=1.2), 0.5)), 0.35)

# The shields taking a hit: a crackling electrical burst over a deep thump.
total = n(0.7)
crackle = []
for i in range(total):
    t = i / RATE
    spark = random.uniform(-1, 1) if random.random() < 0.25 else 0.0
    crackle.append(spark * math.exp(-t * 4) + 0.9 * math.sin(2 * math.pi * (70 - 30 * t) * t) * math.exp(-t * 6))
write("ShieldHit.wav", crackle, 0.75)

# A hit on Vader's TIE: a hard metallic clang.
total = n(0.45)
clang = []
for i in range(total):
    t = i / RATE
    clang.append((math.sin(2 * math.pi * 523 * t) + 0.8 * math.sin(2 * math.pi * 811 * t)
                  + 0.6 * math.sin(2 * math.pi * 1307 * t)) * math.exp(-t * 11))
write("VaderHit.wav", mix(clang, scale(noise(0.1, release=3, smooth=0.2), 0.8)), 0.55)

# A proton torpedo: a rising whistle with a hiss.
write("Torpedo.wav", mix(sweep(0.7, 500, 2200, sine, release=0.8, attack=0.02), scale(noise(0.7, release=1, smooth=0.4), 0.3)), 0.4)

# The Death Star going up: a huge, long, crackling roar.
total = n(4.5)
roar, last, deep = [], 0.0, 0.0
for i in range(total):
    t = i / RATE
    last = last * 0.8 + random.uniform(-1, 1) * 0.2
    deep = deep * 0.97 + random.uniform(-1, 1) * 0.03
    spark = random.uniform(-1, 1) * 0.6 if random.random() < 0.02 * math.exp(-t) else 0.0
    shape = min(1, t / 0.05) * math.exp(-t * 0.75)
    roar.append((0.6 * last + 5 * deep + spark + 0.6 * math.sin(2 * math.pi * 38 * t)) * shape)
write("DeathStarBoom.wav", roar, 0.9)

# Bonus: a bright rising arpeggio.
notes = []
for f in (523, 659, 784, 1047, 1319):
    notes += sweep(0.08, f, f, square, release=0.3)
notes += sweep(0.3, 1568, 1568, square, release=1)
write("Bonus.wav", notes, 0.3)

# A missed torpedo: two falling tones.
write("Miss.wav", sweep(0.3, 440, 330, tri, release=0.6) + sweep(0.5, 330, 220, tri, release=1), 0.45)

# Shields low: three urgent beeps.
beep = sweep(0.12, 1250, 1250, square, release=0.3)
write("Alarm.wav", beep + silence(0.08) + beep + silence(0.08) + beep, 0.25)

# A new phase begins: a quick rising swoosh.
write("PhaseStart.wav", mix(sweep(0.6, 200, 1200, tri, release=1.2, attack=0.05), scale(noise(0.6, release=1.5, smooth=0.5), 0.4)), 0.35)

# A short heroic fanfare for the start of a game: an original brass-like figure (not the film's music),
# square waves with a fifth above, the last note held.
fanfare = []
for f, d in ((392, 0.18), (392, 0.09), (523, 0.45), (784, 0.3), (698, 0.12), (659, 0.12), (587, 0.12), (1047, 0.7)):
    body = mix(sweep(d, f, f, square, release=0.25, attack=0.01), scale(sweep(d, f * 1.5, f * 1.5, saw, release=0.25, attack=0.01), 0.35))
    fanfare += body
write("Fanfare.wav", fanfare, 0.3)

# The X-wing's engines: a steady low roar with a faint whine, made of whole-cycle tones so it loops.
total = n(1.0)
engine, last = [], 0.0
for i in range(total):
    t = i / RATE
    last = last * 0.95 + random.uniform(-1, 1) * 0.05
    engine.append(3 * last + 0.3 * math.sin(2 * math.pi * 64 * t) + 0.12 * math.sin(2 * math.pi * 520 * t)
                  * (0.8 + 0.2 * math.sin(2 * math.pi * 4 * t)))
write("Engine.wav", engine, 0.22)

print("Wrote sounds to", os.path.normpath(OUT))
