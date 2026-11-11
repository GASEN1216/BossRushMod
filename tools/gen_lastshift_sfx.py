#!/usr/bin/env python3
"""末班编组站原创程序化音效；标准库生成 32 kHz / mono / PCM16 WAV，不依赖外部录音（形态照 tools/gen_tidegate_sfx.py）。

python tools/gen_lastshift_sfx.py [--check] [--only 文件名]
产物属于 Assets/Sounds/LastShift（local-only，走私有仓库同步），由正式编译脚本随 Assets\\Sounds 整树部署；
运行时由 LastShiftYard/LastShiftSounds.cs 经官方 AudioManager.PostCustomSFX 播放。
"""
import argparse
import math
import random
import struct
import wave
from pathlib import Path

RATE = 32000
OUT = Path(__file__).resolve().parents[1] / "Assets/Sounds/LastShift"
DURATIONS = {
    # 剪票：打孔钳咬下去的一声「咔哒」，带一点钢件回响。
    "ticket_punch.wav": 0.55,
    # 站场广播前的两声提示音（叮——咚）。
    "tannoy_chime.wav": 1.5,
    # 进站汽笛：三音和弦的蒸汽笛，起音带气声。
    "train_whistle.wav": 2.4,
}
TARGETS = {"ticket_punch.wav": .34, "tannoy_chime.wav": .22, "train_whistle.wav": .26}


def bell(t, frequency, decay):
    if t < 0:
        return 0.0
    attack = min(1.0, t * 400)
    return attack * sum(math.sin(math.tau * frequency * partial * t) *
                        math.exp(-t * decay * (1 + i * .35)) / (1 + i * 1.5)
                        for i, partial in enumerate((1, 2.01, 2.76, 4.1)))


class OnePole:
    def __init__(self, a):
        self.a, self.y = a, 0.0

    def __call__(self, x):
        self.y = self.a * self.y + (1 - self.a) * x
        return self.y


def generate(name, seconds):
    rng = random.Random(20261111)
    n = int(seconds * RATE)
    values = []
    hiss_lp = OnePole(.6)
    phases = [0.0, 0.0, 0.0]
    for index in range(n):
        t = index / RATE
        if name == "ticket_punch.wav":
            # 钳口合上：极短的金属撞击（带噪的瞬态）+ 两支高频钢响；0.18 秒后弹簧把钳柄顶回去，轻一点的第二下。
            def clack(u, weight):
                if u < 0:
                    return 0.0
                hit = rng.uniform(-1, 1) * math.exp(-u * 260)
                thud = math.sin(math.tau * 140 * u) * math.exp(-u * 45)
                return weight * (hit * .9 + thud * .6 + bell(u, 2650.0, 38.0) * .5 + bell(u, 4180.0, 55.0) * .3)
            value = clack(t, 1.0) + clack(t - .18, .38)
        elif name == "tannoy_chime.wav":
            # 老站场喇叭：叮（659 Hz）——咚（523 Hz），带一点喇叭的箱体共鸣（低通）。
            value = bell(t, 659.3, 2.2) * .8 + bell(t - .55, 523.3, 2.0) * .8
            value = hiss_lp(value)
        elif name == "train_whistle.wav":
            # 蒸汽笛：440 / 554 / 659 Hz 三音，起音 0.15 秒从略低滑上来，末尾一段放气声。
            bend = 1 - .04 * math.exp(-t * 9)
            tone = 0.0
            for k, hz in enumerate((440.0, 554.4, 659.3)):
                phases[k] += math.tau * hz * bend / RATE
                tone += math.sin(phases[k]) + .35 * math.sin(2 * phases[k]) + .12 * math.sin(3 * phases[k])
            breath = hiss_lp(rng.uniform(-1, 1)) * .9
            body = min(1.0, t / .15) * min(1.0, max(0.0, (seconds - .45 - t) / .25)) if t < seconds - .45 else 0.0
            tail = max(0.0, min(1.0, (t - (seconds - .7)) / .1)) * max(0.0, (seconds - t) / .6)
            value = tone * body * .32 + breath * (.25 * body + .9 * tail)
        else:
            value = 0.0
        values.append(value)
    values = [v * min(1, max(0, seconds - i / RATE) / .06) for i, v in enumerate(values)]
    peak = max(abs(v) for v in values)
    scale = TARGETS.get(name, .3) / max(peak, .001)
    OUT.mkdir(parents=True, exist_ok=True)
    with wave.open(str(OUT / name), "wb") as wav:
        wav.setparams((1, 2, RATE, 0, "NONE", "not compressed"))
        wav.writeframes(b"".join(struct.pack("<h", round(v * scale * 32767)) for v in values))


def check():
    for name, seconds in DURATIONS.items():
        with wave.open(str(OUT / name), "rb") as wav:
            assert (wav.getnchannels(), wav.getsampwidth(), wav.getframerate()) == (1, 2, RATE), name
            assert wav.getnframes() == int(seconds * RATE), name
            raw = wav.readframes(wav.getnframes())
        samples = struct.unpack("<%dh" % (len(raw) // 2), raw)
        peak = max(abs(s) for s in samples) / 32767
        assert .05 < peak <= .4, (name, peak)
    print("LASTSHIFT_SFX_OK", len(DURATIONS))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--only", nargs="*")
    args = parser.parse_args()
    if not args.check:
        for name, seconds in DURATIONS.items():
            if args.only and name not in args.only:
                continue
            generate(name, seconds)
    check()


if __name__ == "__main__":
    main()
