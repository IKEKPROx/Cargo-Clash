#!/usr/bin/env python3
"""Synthesizes placeholder sound effects and music into Assets/Game/Resources/Audio.

Everything here is generated, so there are no licensing questions. Replace any WAV with a real
recording of the same name and the game picks it up. Run from the repository root:

    python3 Tools/generate_audio.py
"""
import math
import random
import struct
import wave
from pathlib import Path

RATE = 22050
OUT = Path("Assets/Game/Resources/Audio")


def save(name, samples):
    peak = max(0.001, max(abs(s) for s in samples))
    scale = 0.9 / peak
    with wave.open(str(OUT / f"{name}.wav"), "wb") as out:
        out.setnchannels(1)
        out.setsampwidth(2)
        out.setframerate(RATE)
        out.writeframes(b"".join(struct.pack("<h", int(s * scale * 32767)) for s in samples))


def count(seconds):
    return int(seconds * RATE)


def sweep(start, end, seconds, curve=1.0):
    """A sine that glides from start to end Hz."""
    phase = 0.0
    n = count(seconds)
    result = []
    for i in range(n):
        t = i / n
        phase += 2 * math.pi * (start + (end - start) * t**curve) / RATE
        result.append(math.sin(phase))
    return result


def noise(seconds, seed=1):
    rng = random.Random(seed)
    return [rng.uniform(-1, 1) for _ in range(count(seconds))]


def decay(samples, speed):
    n = len(samples)
    return [s * math.exp(-speed * i / n) for i, s in enumerate(samples)]


def mix(*tracks):
    length = max(len(t) for t in tracks)
    result = [0.0] * length
    for track in tracks:
        for i, s in enumerate(track):
            result[i] += s
    return result


def lowpass(samples, amount):
    value = 0.0
    result = []
    for s in samples:
        value += amount * (s - value)
        result.append(value)
    return result


def note(frequency, seconds, shape="square", volume=1.0):
    n = count(seconds)
    result = []
    for i in range(n):
        phase = (i * frequency / RATE) % 1.0
        if shape == "square":
            wave_value = 1.0 if phase < 0.5 else -1.0
        elif shape == "triangle":
            wave_value = 4 * abs(phase - 0.5) - 1
        else:
            wave_value = math.sin(2 * math.pi * phase)
        envelope = min(1.0, i / (0.004 * RATE)) * math.exp(-3.2 * i / n)
        result.append(wave_value * envelope * volume)
    return result


def midi(number):
    return 440.0 * 2 ** ((number - 69) / 12)


def make_rotor_loop():
    """Heavy blade thumps over rushing air. The ends match, so it loops without a click."""
    seconds = 2.0
    blade_passes = 16
    length = count(seconds)
    overlap = count(0.12)
    air = lowpass(noise(seconds + 0.12, 7), 0.2)
    rumble = lowpass(noise(seconds + 0.12, 8), 0.03)
    for i in range(overlap):
        blend = i / overlap
        air[i] = air[i] * blend + air[length + i] * (1 - blend)
        rumble[i] = rumble[i] * blend + rumble[length + i] * (1 - blend)

    result = []
    for i in range(length):
        beat = (i / length * blade_passes) % 1.0
        thump = math.exp(-5.5 * beat)
        low = math.sin(2 * math.pi * 62 * beat / (blade_passes / seconds)) * math.exp(-8 * beat)
        result.append(air[i] * (0.18 + 0.82 * thump) * 0.8 + rumble[i] * 2.0 + low * 0.55)
    return result


def make_suck_loop():
    """Rushing air with a whine on top. The ends match, so it loops without a click."""
    seconds = 1.2
    length = count(seconds)
    overlap = count(0.15)
    air = lowpass(noise(seconds + 0.15, 41), 0.42)
    for i in range(overlap):
        blend = i / overlap
        air[i] = air[i] * blend + air[length + i] * (1 - blend)
    result = []
    for i in range(length):
        # Whole numbers of cycles, so the whine joins up too.
        whine = math.sin(2 * math.pi * 620 * i / length) * 0.12 + math.sin(2 * math.pi * 930 * i / length) * 0.07
        result.append(air[i] * 0.9 + whine)
    return result


def make_effects():
    save("bonk", mix(decay(sweep(260, 70, 0.28, 0.5), 7), [s * 0.5 for s in decay(lowpass(noise(0.14, 2), 0.45), 9)]))
    save("whoosh", [s * math.sin(math.pi * i / count(0.26)) for i, s in enumerate(lowpass(noise(0.26, 3), 0.18))])
    save("pickup", mix(decay(sweep(420, 760, 0.12), 3), [0.0] * count(0.12)))
    save("drop", decay(sweep(240, 90, 0.2, 0.6), 6))
    save("smash", mix(decay(lowpass(noise(0.1, 5), 0.6), 10), [s * 0.5 for s in decay(sweep(180, 90, 0.1), 8)]))
    save("break", mix(decay(lowpass(noise(0.45, 6), 0.7), 6), [s * 0.8 for s in decay(sweep(150, 45, 0.4, 0.5), 5)]))
    rescue = []
    for number in (72, 76, 79):
        rescue += note(midi(number), 0.09, "triangle")
    rescue += note(midi(84), 0.22, "triangle")
    save("rescue", rescue)
    whistle_length = count(0.55)
    whistle = []
    phase = 0.0
    for i in range(whistle_length):
        flutter = 2600 + 120 * math.sin(2 * math.pi * 34 * i / RATE)
        phase += 2 * math.pi * flutter / RATE
        envelope = min(1.0, i / 400) * min(1.0, (whistle_length - i) / 800)
        whistle.append(math.sin(phase) * envelope)
    save("whistle", whistle)
    save("rotor", make_rotor_loop())
    save("clunk", mix(decay(sweep(620, 260, 0.09), 7), [s * 0.7 for s in decay(lowpass(noise(0.09, 11), 0.8), 8)],
                      [0.0] * count(0.06) + [s * 0.6 for s in decay(sweep(180, 90, 0.16), 6)]))
    fanfare = []
    for number, length in ((67, 0.13), (72, 0.13), (76, 0.13), (79, 0.26), (76, 0.13), (79, 0.5)):
        fanfare += mix(note(midi(number), length, "square", 0.6), note(midi(number - 12), length, "triangle", 0.8))
    save("fanfare", fanfare)
    save("click", mix(decay(sweep(880, 540, 0.05), 5), [s * 0.25 for s in decay(lowpass(noise(0.03, 13), 0.7), 8)]))
    save("locked", mix(decay(sweep(190, 120, 0.13), 5), [0.0] * count(0.09) + decay(sweep(150, 95, 0.14), 5)))
    # First person: the hammer going through the air, things breaking apart, and footsteps.
    swing_noise = lowpass(noise(0.2, 17), 0.3)
    save("swing", [s * math.sin(math.pi * i / len(swing_noise)) ** 2 for i, s in enumerate(swing_noise)])
    save("crack", mix(decay(lowpass(noise(0.16, 19), 0.85), 14), [s * 0.5 for s in decay(sweep(320, 120, 0.12), 9)],
                      [0.0] * count(0.04) + decay(lowpass(noise(0.08, 23), 0.9), 12)))
    coins = [0.0] * count(0.5)
    rng = random.Random(29)
    for i in range(14):
        start = count(0.01 + i * 0.026 + rng.uniform(0.0, 0.015))
        ping = decay(sweep(rng.uniform(2600, 4200), rng.uniform(2400, 3800), 0.09), 8)
        for j, sample in enumerate(ping):
            if start + j < len(coins):
                coins[start + j] += sample * 0.5 * (1.0 - i / 18)
    save("coins", coins)
    save("step", mix(decay(lowpass(noise(0.07, 31), 0.32), 9), [s * 0.7 for s in decay(sweep(120, 70, 0.07), 7)]))
    save("punch", mix(decay(sweep(190, 80, 0.11, 0.6), 7), [s * 0.6 for s in decay(lowpass(noise(0.05, 37), 0.55), 10)]))
    # The vacuum and the charged hammer.
    save("blast", mix(decay(lowpass(noise(0.24, 43), 0.55), 7), [s * 0.5 for s in decay(sweep(420, 110, 0.16, 0.6), 7)]))
    save("suck", make_suck_loop())
    save("slam", mix(decay(sweep(150, 34, 0.42, 0.5), 5), [s * 0.8 for s in decay(lowpass(noise(0.26, 47), 0.5), 8)],
                     [0.0] * count(0.03) + [s * 0.4 for s in decay(lowpass(noise(0.1, 53), 0.9), 9)]))
    # Knocking someone out: the bell from a boxing ring, twice.
    bell = mix(decay(sweep(1760, 1750, 0.5), 5), [s * 0.5 for s in decay(sweep(2640, 2630, 0.32), 7)])
    save("knockout", mix(bell, [0.0] * count(0.16) + bell))
    respawn = []
    for number in (67, 74, 79):
        respawn += note(midi(number), 0.07, "triangle", 0.8)
    save("respawn", mix(respawn, [s * 0.3 for s in decay(lowpass(noise(0.2, 59), 0.3), 5)]))
    save("tick", decay(sweep(1500, 1100, 0.045), 6))


def make_music():
    bpm = 150
    beat = 60 / bpm
    bars = 8
    total = count(beat * 4 * bars)
    track = [0.0] * total

    def place(start_beat, samples, volume=1.0):
        start = int(start_beat * beat * RATE)
        for i, s in enumerate(samples):
            if start + i >= total:
                break
            track[start + i] += s * volume

    roots = [48, 53, 55, 48, 45, 53, 55, 48]
    for bar, root in enumerate(roots):
        for step in range(4):
            place(bar * 4 + step, note(midi(root + (12 if step % 2 else 0)), beat * 0.9, "triangle"), 0.55)
        for step in (1, 3):
            place(bar * 4 + step, note(midi(root + 19), beat * 0.4, "square"), 0.09)
            place(bar * 4 + step, note(midi(root + 24), beat * 0.4, "square"), 0.09)

    melody = [
        (0, 72, 1), (1, 76, 0.5), (1.5, 79, 0.5), (2, 81, 1), (3, 79, 1),
        (4, 77, 1), (5, 76, 0.5), (5.5, 74, 0.5), (6, 72, 1), (7, 69, 1),
        (8, 71, 1), (9, 74, 0.5), (9.5, 77, 0.5), (10, 79, 1), (11, 77, 1),
        (12, 76, 0.5), (12.5, 74, 0.5), (13, 72, 1), (14, 67, 2),
        (16, 69, 1), (17, 72, 0.5), (17.5, 76, 0.5), (18, 81, 1), (19, 79, 1),
        (20, 77, 1), (21, 81, 0.5), (21.5, 84, 0.5), (22, 81, 1), (23, 77, 1),
        (24, 79, 0.5), (24.5, 77, 0.5), (25, 76, 0.5), (25.5, 74, 0.5), (26, 72, 1), (27, 74, 1),
        (28, 76, 0.5), (28.5, 74, 0.5), (29, 71, 0.5), (29.5, 67, 0.5), (30, 72, 2),
    ]
    for start, number, length in melody:
        place(start, note(midi(number), beat * length * 0.95, "square"), 0.2)

    hat = decay(lowpass(noise(0.05, 9), 0.9), 12)
    for step in range(bars * 8):
        place(step * 0.5, hat, 0.12 if step % 2 else 0.07)
    save("music", track)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    make_effects()
    make_music()
    print("Wrote", len(list(OUT.glob("*.wav"))), "clips to", OUT)


if __name__ == "__main__":
    main()
