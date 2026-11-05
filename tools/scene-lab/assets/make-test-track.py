"""Builds assets/test-track.wav: a 12 s drum-and-bass test loop at 120 BPM with a real silent tail.

It is not a mock envelope: the WAV goes through the dock's own FFT/band/onset analysis in WavTrack, so a
scene rendered with --wav proves it reacts to actual sound samples, not to a hand-drawn curve.
"""
import math
import random
import struct
import wave

RATE = 48000
SECONDS = 12.0
BEAT = 0.5
SILENT_FROM = 10.6

random.seed(7)
n = int(RATE * SECONDS)
left = [0.0] * n
right = [0.0] * n


def add(start, length, func, gain=1.0, pan=0.0):
    first = int(start * RATE)
    count = int(length * RATE)
    gl = gain * (1.0 - max(0.0, pan))
    gr = gain * (1.0 + min(0.0, pan))
    for i in range(count):
        at = first + i
        if at >= n or at < 0:
            break
        value = func(i / RATE)
        left[at] += value * gl
        right[at] += value * gr


def kick(duration):
    def env(t):
        if t > duration:
            return 0.0
        body = math.exp(-t * 26.0) * math.sin(2 * math.pi * (48.0 + 70.0 * math.exp(-t * 40.0)) * t)
        click = math.exp(-t * 260.0) * random.uniform(-0.4, 0.4)
        return body + click
    return env


def snare(duration):
    def env(t):
        if t > duration:
            return 0.0
        noise = random.uniform(-1.0, 1.0)
        tone = math.sin(2 * math.pi * 185.0 * t) * 0.5
        return (noise * 0.7 + tone) * math.exp(-t * 22.0)
    return env


def hat(duration):
    def env(t):
        if t > duration:
            return 0.0
        return random.uniform(-1.0, 1.0) * math.exp(-t * 90.0)
    return env


def bass(duration, hz):
    def env(t):
        if t > duration:
            return 0.0
        attack = min(1.0, t * 60.0)
        saw = sum(math.sin(2 * math.pi * hz * k * t) / k for k in range(1, 4))
        return saw * 0.45 * attack * math.exp(-t * 1.1)
    return env


# intro 0-2 s: sparse kick + bass; 2-6: beat builds; 6-10.5: full drop; 10.6+: silence
for beat_index in range(int(SILENT_FROM / BEAT) + 2):
    t = beat_index * BEAT
    if t >= SILENT_FROM:
        break
    bar_beat = beat_index % 4
    loudness = 0.4 if t < 2 else (0.75 if t < 6 else 1.0)

    add(t, 0.28, kick(0.28), 0.95 * loudness)
    if bar_beat in (1, 3):
        add(t, 0.22, snare(0.22), 0.55 * loudness, pan=0.15)
    if t >= 2:
        for step in range(2):
            add(t + step * (BEAT / 2), 0.09, hat(0.09), 0.22 * loudness, pan=-0.2)

# bass line: two-bar pattern, follows the kick
pattern = [0, 0, 7, 0, 3, 0, 5, 3]
for index in range(int(SILENT_FROM / BEAT)):
    t = index * BEAT
    if t >= SILENT_FROM:
        break
    semitone = pattern[index % len(pattern)]
    hz = 55.0 * (2 ** (semitone / 12.0))
    add(t, BEAT * 0.99, bass(BEAT * 0.99, hz), 0.7 if t >= 2 else 0.45)

# a quiet noise bed under everything, so the analysis window is never digitally silent between hits
for i in range(n):
    bed = random.uniform(-1.0, 1.0) * 0.0022
    left[i] += bed
    right[i] += bed

# a pad that opens up in the drop, so the mids have something real to show
for index in range(int(SILENT_FROM / BEAT)):
    t = index * BEAT
    if t < 2 or t >= SILENT_FROM:
        continue
    for hz, gain in ((220.0, 0.10), (277.2, 0.08), (329.6, 0.06)):
        add(t, BEAT * 1.02, bass(BEAT * 1.02, hz), gain * (1.0 if t < 6 else 1.4))

peak = max(max(abs(v) for v in left), max(abs(v) for v in right), 1e-6)
scale = 0.89 / peak
frames = bytearray()
for i in range(n):
    frames += struct.pack("<hh", int(max(-1.0, min(1.0, left[i] * scale)) * 32767),
                          int(max(-1.0, min(1.0, right[i] * scale)) * 32767))

with wave.open("test-track.wav", "wb") as out:
    out.setnchannels(2)
    out.setsampwidth(2)
    out.setframerate(RATE)
    out.writeframes(bytes(frames))

print(f"test-track.wav: {SECONDS:.1f}s stereo {RATE} Hz, silence from {SILENT_FROM:.1f}s")
