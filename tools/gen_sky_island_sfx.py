#!/usr/bin/env python3
"""原创程序化群岛环境音；标准库生成 32 kHz / mono / PCM16 WAV，不依赖外部录音。

python tools/gen_sky_island_sfx.py [--check]
产物属于 Assets/Sounds/SkyIsland，由正式编译脚本随 Mod 部署。
"""
import argparse
import math
import random
import struct
import wave
from pathlib import Path

RATE = 32000
OUT = Path(__file__).resolve().parents[1] / "Assets/Sounds/SkyIsland"
DURATIONS = {"island_wind.wav": 8.0, "wind_chimes.wav": 4.0,
             "homecoming_bell.wav": 7.0, "device_awake.wav": 2.5}


def bell(t, frequency, decay):
    if t < 0:
        return 0.0
    attack = min(1.0, t * 350)
    return attack * sum(math.sin(math.tau * frequency * partial * t) *
                        math.exp(-t * decay * (1 + i * .32)) / (1 + i * 1.4)
                        for i, partial in enumerate((1, 2.013, 2.71, 4.08, 5.43)))


def generate(name, seconds):
    rng = random.Random(20260908)
    low = 0.0
    values = []
    for index in range(int(seconds * RATE)):
        t = index / RATE
        if name == "island_wind.wav":
            low = low * .985 + rng.uniform(-1, 1) * .015
            value = low * (.6 + .24 * math.sin(math.tau * t / seconds))
            value *= min(1, t / .5, (seconds - t) / .5)
        elif name == "wind_chimes.wav":
            value = sum(bell(t - delay, freq, 1.4) * .10 for delay, freq in
                        ((0, 1046.5), (.35, 1318.5), (.82, 1568), (1.8, 1174.7)))
        elif name == "homecoming_bell.wav":
            value = bell(t, 220, .53) * .22 + bell(t - 1.8, 329.63, .65) * .12
        else:
            value = sum(bell(t - delay, freq, 2.0) * .12 for delay, freq in
                        ((0, 523.25), (.22, 659.25), (.44, 783.99), (.66, 1046.5)))
        values.append(value * min(1, max(0, seconds - t) / .12))
    peak = max(abs(v) for v in values)
    # 环境风比提示音轻，峰值有明确上限，循环边界归零。
    target = .09 if name == "island_wind.wav" else .32
    scale = target / max(peak, .001)
    with wave.open(str(OUT / name), "wb") as wav:
        wav.setparams((1, 2, RATE, 0, "NONE", "not compressed"))
        wav.writeframes(b"".join(struct.pack("<h", round(v * scale * 32767)) for v in values))


def check():
    for name, seconds in DURATIONS.items():
        with wave.open(str(OUT / name), "rb") as wav:
            assert (wav.getnchannels(), wav.getsampwidth(), wav.getframerate()) == (1, 2, RATE), name
            assert wav.getnframes() == int(seconds * RATE), name
            raw = wav.readframes(wav.getnframes())
        samples = struct.unpack("<" + "h" * (len(raw) // 2), raw)
        assert 500 < max(abs(v) for v in samples) < 15000, name
        assert abs(samples[0]) < 50 and abs(samples[-1]) < 50, name
        print("PASS", name, len(samples), "samples")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    if not args.check:
        OUT.mkdir(parents=True, exist_ok=True)
        for filename, duration in DURATIONS.items():
            generate(filename, duration)
    check()
