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
             "homecoming_bell.wav": 7.0, "device_awake.wav": 2.5, "gnat_buzz.wav": 1.0,
             "frog_chorus.wav": 5.0}
# 无缝循环：不做首尾淡出，校验「接缝处的跳变不大于文件内部最大的相邻采样跳变」。
# 云蚋的嗡声全场只有一个共享发声体循环播放（SkyIslandGnats），接缝处一顿就是每秒一次的咔哒。
LOOPS = {"gnat_buzz.wav"}


def gnat_buzz(t):
    # 整数赫兹 + 整秒时长：每个分量在循环边界上回到同一相位，t=0 处全部分量为 0。
    # 410 Hz 的锯齿味谐波叠一条 423 Hz 的副声（13 Hz 拍频，听起来是一群而不是一只），再按 23 Hz 振翅起伏。
    wing = .78 + .22 * math.sin(math.tau * 23 * t)
    tone = sum(a * math.sin(math.tau * f * t) for f, a in ((410, 1.0), (820, .5), (1230, .3), (1640, .16), (2050, .09)))
    swarm = .45 * math.sin(math.tau * 423 * t) + .2 * math.sin(math.tau * 846 * t)
    return (tone + swarm) * wing


def bell(t, frequency, decay):
    if t < 0:
        return 0.0
    attack = min(1.0, t * 350)
    return attack * sum(math.sin(math.tau * frequency * partial * t) *
                        math.exp(-t * decay * (1 + i * .32)) / (1 + i * 1.4)
                        for i, partial in enumerate((1, 2.013, 2.71, 4.08, 5.43)))


def frog_chorus(t):
    # 两只成蛙错落应答：短促气囊脉冲、下降音高与谐波共鸣，中间留白，避免连续高音盖住脚步。
    value = 0.0
    for start, frequency, length in ((.15, 340, .65), (.95, 290, .8), (2.25, 340, .6), (3.35, 290, .85)):
        local = t - start
        if not 0 < local < length:
            continue
        phase = math.tau * (frequency * local - 35 * local * local / length)
        envelope = math.sin(math.pi * local / length) ** 2
        pulse = (.5 + .5 * math.sin(math.tau * 31 * local)) ** 2
        value += envelope * pulse * (math.sin(phase) + .55 * math.sin(phase * 2) + .3 * math.sin(phase * 3))
    return value


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
        elif name == "gnat_buzz.wav":
            value = gnat_buzz(t)
        elif name == "frog_chorus.wav":
            value = frog_chorus(t)
        else:
            value = sum(bell(t - delay, freq, 2.0) * .12 for delay, freq in
                        ((0, 523.25), (.22, 659.25), (.44, 783.99), (.66, 1046.5)))
        values.append(value if name in LOOPS else value * min(1, max(0, seconds - t) / .12))
    peak = max(abs(v) for v in values)
    # 环境风比提示音轻，峰值有明确上限，循环边界归零。嗡声介于风与提示音之间：听得见、盖不过枪声。
    target = .09 if name == "island_wind.wav" else .12 if name in ("gnat_buzz.wav", "frog_chorus.wav") else .32
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
        if name in LOOPS:
            largest_step = max(abs(samples[i + 1] - samples[i]) for i in range(len(samples) - 1))
            assert abs(samples[0]) < 50 and abs(samples[-1] - samples[0]) <= largest_step, name + " 循环接缝不连续"
        else:
            assert abs(samples[0]) < 50 and abs(samples[-1]) < 50, name
        print("PASS", name, len(samples), "samples")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--only", choices=tuple(DURATIONS), help="只重建指定音效，随后仍校验整套文件")
    args = parser.parse_args()
    if not args.check:
        OUT.mkdir(parents=True, exist_ok=True)
        for filename, duration in DURATIONS.items():
            if args.only is None or filename == args.only:
                generate(filename, duration)
    check()
