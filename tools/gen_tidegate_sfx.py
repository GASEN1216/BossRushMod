#!/usr/bin/env python3
"""潮闸镇原创程序化音效；标准库生成 32 kHz / mono / PCM16 WAV，不依赖外部录音（形态照 tools/gen_sky_island_sfx.py）。

python tools/gen_tidegate_sfx.py [--check] [--only 文件名]
产物属于 Assets/Sounds/Tidegate（local-only，走私有仓库同步），由正式编译脚本随 Assets\\Sounds 整树部署；
运行时由 Tidegate/TidegateSounds.cs 经官方 AudioManager.PostCustomSFX 播放。
"""
import argparse
import math
import random
import struct
import wave
from pathlib import Path

RATE = 32000
OUT = Path(__file__).resolve().parents[1] / "Assets/Sounds/Tidegate"
DURATIONS = {
    # 环境：沿岸海浪循环（只在离岸 38 m 内响）、栈桥外的浮标钟。
    "sea_waves.wav": 8.0, "buoy_bell.wav": 3.0,
    # 提示：涨潮警报、来船汽笛、阀门拧开、大闸提起、灯塔点亮。
    "tide_siren.wav": 3.4, "boat_horn.wav": 2.8, "valve_turn.wav": 1.4, "gate_lift.wav": 4.0,
    "lighthouse_ignite.wav": 2.0,
}
# 无缝循环：不做首尾淡出；校验「接缝处的跳变不大于文件内部最大的相邻采样跳变」，起点在过零处。
LOOPS = {"sea_waves.wav"}
# 峰值：海浪最轻（环境底噪，盖不过脚步）、浮标钟次之；警报跟着玩家放，比其它提示音轻一点。
TARGETS = {"sea_waves.wav": .09, "buoy_bell.wav": .12, "tide_siren.wav": .20, "gate_lift.wav": .24}
DEFAULT_TARGET = .30


def bell(t, frequency, decay):
    if t < 0:
        return 0.0
    attack = min(1.0, t * 350)
    return attack * sum(math.sin(math.tau * frequency * partial * t) *
                        math.exp(-t * decay * (1 + i * .32)) / (1 + i * 1.4)
                        for i, partial in enumerate((1, 2.013, 2.71, 4.08, 5.43)))


class OnePole:
    """一阶低通：a 越接近 1 越闷（a = 0.97 约 155 Hz，0.6 约 2.6 kHz @ 32 kHz）。"""

    def __init__(self, a):
        self.a = a
        self.y = 0.0

    def __call__(self, x):
        self.y = self.y * self.a + x * (1 - self.a)
        return self.y


def sea_waves(seconds, rng):
    # 两层：闷的冲刷（低通噪声）+ 浪头碎开时的「唰」（两级低通再减去更闷的一路，落在几百赫兹到一千多赫兹，不发嘶）。
    # 涌浪包络的两个周期 4 s / 8/3 s 都整除 8 s，包络本身首尾相接；噪声靠末尾续写的 1 s 交叉淡入开头接缝。
    n = int(seconds * RATE)
    fade = RATE
    wash_lp, hiss_a, hiss_b, hiss_lo = OnePole(.97), OnePole(.8), OnePole(.8), OnePole(.95)
    raw = []
    for i in range(n + fade):
        t = (i % n) / RATE
        x = rng.uniform(-1, 1)
        wash = wash_lp(x) * 4.0
        hiss = (hiss_b(hiss_a(x)) - hiss_lo(x)) * 1.6
        swell = .55 + .45 * (.6 * math.sin(math.pi * t / 4.0) ** 2 + .4 * math.sin(math.pi * t / (8.0 / 3.0)) ** 2)
        crest = max(0.0, math.sin(math.tau * t / 4.0 - .6)) ** 4 + .6 * max(0.0, math.sin(math.tau * t / (8.0 / 3.0) + 1.1)) ** 4
        raw.append(wash * swell + hiss * (.15 + .85 * crest))
    out = [raw[i] * (i / fade) + raw[n + i] * (1 - i / fade) if i < fade else raw[i] for i in range(n)]
    # 起点挪到离零最近的那个上升过零点（循环整体转一圈，接缝处就是原来相邻的两个采样，仍然连续）。
    start = min((i for i in range(1, n) if out[i - 1] <= 0 < out[i]), key=lambda i: abs(out[i]))
    return out[start:] + out[:start]


def generate(name, seconds):
    rng = random.Random(20261105)
    n = int(seconds * RATE)
    if name == "sea_waves.wav":
        values = sea_waves(seconds, rng)
    else:
        values = []
        phase = phase2 = 0.0
        tone_lp = OnePole(.35)
        rumble_lp = OnePole(.985)
        grit_a, grit_b = OnePole(.85), OnePole(.85)
        rush_a, rush_b, rush_lo = OnePole(.85), OnePole(.85), OnePole(.97)
        spark_lp = OnePole(.5)
        for index in range(n):
            t = index / RATE
            if name == "buoy_bell.wav":
                # 浮标上的钟：一记实的、隔 0.9 秒随浪轻轻一碰。
                value = bell(t, 392.0, 1.1) * .75 + bell(t - .9, 392.0, 1.6) * .28
            elif name == "tide_siren.wav":
                # 老式手摇警报：音高 440 → 760 → 440 Hz 一起一落，带 6 Hz 颤音；首尾各一段淡入淡出。
                frequency = 440 + 320 * math.sin(math.pi * t / seconds) ** 2
                frequency *= 1 + .015 * math.sin(math.tau * 6 * t)
                phase += math.tau * frequency / RATE
                tone = math.sin(phase) + .5 * math.sin(2 * phase) + .22 * math.sin(3 * phase)
                envelope = min(1.0, t / .25) * min(1.0, (seconds - t) / .45)
                value = tone_lp(tone) * envelope
            elif name == "boat_horn.wav":
                # 汽笛：98 Hz 与 99.5 Hz 两支叠出拍频，起音时音高从略低滑上来；闷一点，像隔着海面传过来。
                bend = 1 - .03 * math.exp(-t * 6)
                phase += math.tau * 98.0 * bend / RATE
                phase2 += math.tau * 99.5 * bend / RATE
                tone = sum(math.sin(k * phase) / k ** 1.1 + .7 * math.sin(k * phase2) / k ** 1.1 for k in range(1, 7))
                envelope = min(1.0, t / .12) * min(1.0, (seconds - t) / .5)
                value = tone_lp(tone) * envelope
            elif name == "valve_turn.wav":
                # 锈阀门：一段带音高的吱嘎（700 → 900 Hz，按 20 Hz 一顿一顿地咬齿，摩擦声用低通过的噪声调制音高），最后咔一声落位。
                creak = 0.0
                grit = grit_b(grit_a(rng.uniform(-1, 1))) * 4.0
                if t < 1.0:
                    sweep = 700 + 200 * t
                    phase += math.tau * sweep / RATE
                    grain = (.5 + .5 * math.sin(math.tau * 20 * t)) ** 3
                    creak = (math.sin(phase) * (.55 + .45 * grit) + math.sin(2 * phase) * .2) * grain * math.sin(math.pi * t) ** .6
                local = t - 1.05
                clunk = 0.0
                if local >= 0:
                    clunk = math.sin(math.tau * 80 * local) * math.exp(-local * 14) * 1.4 + bell(local, 1240.0, 9.0) * .5
                value = creak + clunk
            elif name == "gate_lift.wav":
                # 大闸提起：远处的机械闷响 + 一节一节的铁链声，后半段泻湖的水开始往外涌。
                x = rng.uniform(-1, 1)
                rumble = rumble_lp(x) * 9.0 * min(1.0, t / .5) * min(1.0, (seconds - t) / .8)
                chain = bell(t % .21, 1800.0, 22.0) * .25 * max(0.0, 1 - t / 3.2) if t < 3.2 else 0.0
                rush = (rush_b(rush_a(x)) - rush_lo(x)) * max(0.0, min(1.0, (t - 1.8) / 1.2)) * min(1.0, (seconds - t) / .8) * 2.4
                value = rumble + chain + rush
            elif name == "lighthouse_ignite.wav":
                # 灯塔点亮：100 Hz 电流嗡声起势、前半秒几下火花噼啪，接一声灯罩玻璃的叮。
                phase += math.tau * 100.0 / RATE
                hum = sum(math.sin(k * phase) / k for k in (1, 3, 5, 7)) * min(1.0, t / .6) * max(0.0, 1 - max(0.0, t - 1.2) / .8)
                crackle = spark_lp((rng.uniform(-1, 1) if rng.random() < .04 else 0.0) * (1 - t / .5) if t < .5 else 0.0)
                value = hum * .55 + crackle * 1.4 + bell(t - .65, 1318.5, 3.5) * .5 + bell(t - .72, 1975.5, 4.5) * .3
            else:
                value = 0.0
            values.append(value)
    if name not in LOOPS:
        values = [v * min(1, max(0, seconds - i / RATE) / .12) for i, v in enumerate(values)]
    peak = max(abs(v) for v in values)
    scale = TARGETS.get(name, DEFAULT_TARGET) / max(peak, .001)
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
    print("TIDEGATE_SFX_OK", len(DURATIONS), "files")


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
