#!/usr/bin/env python3
"""水墨主题界面与钟罩的原创程序化音效；标准库生成 32 kHz / mono / PCM16 WAV，不依赖外部录音（形态照 tools/gen_lastshift_sfx.py）。

python tools/gen_ink_ui_sfx.py [--check] [--only 文件名]
产物属于 Assets/Sounds/InkUI（local-only，走私有仓库同步），由正式编译脚本随 Assets\\Sounds 整树部署；
运行时由 Common/UI/BossRushInkSkin.cs 的 PlaySfx / PlaySfxAt 经官方 AudioManager.PostCustomSFX 播放。
口径（2026-11-24 owner「高级、大气、史诗感、无塑料感」）：只用低频、有质地的声音——钟、磬、纸、笔、火、战鼓。
"""
import argparse
import math
import random
import struct
import wave
from pathlib import Path

RATE = 32000
OUT = Path(__file__).resolve().parents[1] / "Assets/Sounds/InkUI"
DURATIONS = {
    # 落印：印石压上纸的一声闷响，带一点印泥被揭开的黏声。
    "seal_stamp.wav": 0.5,
    # 翻页：宣纸被手指带起、落下的沙沙声。
    "paper_turn.wav": 0.65,
    # 笔扫：一笔扫过纸面，起笔重、收笔轻。
    "brush_sweep.wav": 0.55,
    # 古钟：大钟一撞，长尾带拍频（立誓、噩梦、压轴）。
    "temple_bell.wav": 4.0,
    # 磬：石磬清音（升级、心意簿添笔）。
    "chime_stone.wav": 2.2,
    # 战鼓：一记大鼓，带厅堂回响（压轴登场、烽烟显形）。
    "war_drum.wav": 1.8,
    # 钟罩受击：铜钟被敲了一下的短促嗡鸣。
    "bell_hum.wav": 0.7,
    # 钟罩碎裂：铜壁开裂的脆响 + 低沉的一声钟。
    "bell_shatter.wav": 2.4,
    # 钟罩回满：一声清亮的小钟。
    "bell_ding.wav": 1.3,
    # 余烬：噩梦卡燃起的一阵火星噼啪。
    "ember_flare.wav": 0.9,
    # 魂火：献魂时魂火升起的气声与微光。
    "soul_rise.wav": 1.4,
}
TARGETS = {
    "seal_stamp.wav": .34, "paper_turn.wav": .22, "brush_sweep.wav": .2, "temple_bell.wav": .3,
    "chime_stone.wav": .26, "war_drum.wav": .38, "bell_hum.wav": .26, "bell_shatter.wav": .34,
    "bell_ding.wav": .22, "ember_flare.wav": .22, "soul_rise.wav": .2,
}


def partials(t, base, ratios, decays, weights):
    """一组非谐泛音（钟、磬）：每支各自衰减。"""
    if t < 0:
        return 0.0
    attack = min(1.0, t * 300)
    return attack * sum(w * math.sin(math.tau * base * r * t) * math.exp(-t * d)
                        for r, d, w in zip(ratios, decays, weights))


class OnePole:
    def __init__(self, a):
        self.a, self.y = a, 0.0

    def __call__(self, x):
        self.y = self.a * self.y + (1 - self.a) * x
        return self.y


class Band:
    """两级一阶低通相减，近似一个宽带通。"""

    def __init__(self, low_a, high_a):
        self.low, self.high = OnePole(low_a), OnePole(high_a)

    def __call__(self, x):
        return self.high(x) - self.low(x)


def generate(name, seconds):
    rng = random.Random(20261124)
    n = int(seconds * RATE)
    values = []
    lp = OnePole(.82)
    lp2 = OnePole(.94)
    band = Band(.995, .7)
    phase = 0.0
    pops = [(rng.uniform(.02, seconds - .1), rng.uniform(.3, 1.0)) for _ in range(26)]
    for index in range(n):
        t = index / RATE
        if name == "seal_stamp.wav":
            # 印石落下：90 Hz 闷击 + 纸面拍击的噪声瞬态；0.16 秒后揭印，一点黏声。
            thud = math.sin(math.tau * 92 * t) * math.exp(-t * 28) + math.sin(math.tau * 61 * t) * math.exp(-t * 18) * .6
            slap = lp(rng.uniform(-1, 1)) * math.exp(-t * 70) * 1.4
            u = t - .16
            peel = band(rng.uniform(-1, 1)) * math.exp(-u * 30) * .5 if u > 0 else 0.0
            value = thud * 1.1 + slap + peel
        elif name == "paper_turn.wav":
            # 纸被带起（0–0.3 秒渐强的沙沙）再落下（0.4 秒处一声轻拍）。
            env = math.sin(math.pi * min(1.0, t / .45)) ** 1.5 if t < .45 else 0.0
            flutter = .6 + .4 * math.sin(math.tau * 23 * t + 2 * math.sin(math.tau * 5 * t))
            rustle = band(rng.uniform(-1, 1)) * env * flutter
            u = t - .42
            land = lp(rng.uniform(-1, 1)) * math.exp(-u * 45) * .8 if u > 0 else 0.0
            value = rustle + land
        elif name == "brush_sweep.wav":
            # 一笔扫过：低通噪声，起笔快、收笔拖长，笔毛摩擦带一点颗粒。
            env = min(1.0, t / .05) * math.exp(-max(0.0, t - .08) * 6.5)
            grain = 1 + .5 * (rng.random() < .03)
            value = lp2(rng.uniform(-1, 1)) * env * grain * 1.6 + band(rng.uniform(-1, 1)) * env * .3
        elif name == "temple_bell.wav":
            # 大钟：110 Hz 附近的非谐泛音（钟的 hum / prime / tierce / quint / nominal），
            # 两支相近的主音拍出慢颤，撞木的一声木头瞬态。
            value = partials(t, 110.0, (.5, 1.0, 1.004, 1.19, 1.5, 2.0, 2.66, 3.01),
                             (.55, .8, .82, 1.4, 1.9, 2.4, 3.6, 4.5), (.55, .9, .7, .45, .35, .4, .2, .15))
            value += lp(rng.uniform(-1, 1)) * math.exp(-t * 60) * .5
        elif name == "chime_stone.wav":
            # 石磬：清亮、泛音稀，衰减比钟快。
            value = partials(t, 784.0, (1.0, 2.76, 5.4, 8.93), (2.2, 4.0, 7.0, 11.0), (1.0, .45, .2, .08))
            value += partials(t, 392.0, (1.0,), (3.0,), (.25,))
            value += band(rng.uniform(-1, 1)) * math.exp(-t * 90) * .3
        elif name == "war_drum.wav":
            # 大鼓：58 Hz 起、落到 46 Hz 的鼓腔，鼓皮噪声瞬态，0.12 秒后一层厅堂回响。
            freq = 46 + 12 * math.exp(-t * 9)
            phase += math.tau * freq / RATE
            body = math.sin(phase) * math.exp(-t * 3.2)
            skin = lp(rng.uniform(-1, 1)) * math.exp(-t * 35)
            room = lp2(rng.uniform(-1, 1)) * math.exp(-max(0.0, t - .12) * 3.0) * (.18 if t > .12 else 0)
            value = body * 1.3 + skin * .9 + room
        elif name == "bell_hum.wav":
            # 钟罩受击：小铜钟 220 Hz 的短嗡鸣，挨打那一下带金属颤。
            value = partials(t, 220.0, (1.0, 1.006, 2.0, 2.73, 3.9), (5.0, 5.2, 8.0, 11.0, 15.0), (.8, .6, .4, .25, .15))
            value += band(rng.uniform(-1, 1)) * math.exp(-t * 80) * .6
        elif name == "bell_shatter.wav":
            # 碎裂：先是一串高频碎裂声（铜片四散），叠一声 98 Hz 的低钟长尾。
            crack = 0.0
            for k in range(6):
                u = t - k * .045
                if u > 0:
                    crack += partials(u, 2200 + 410 * k, (1.0, 1.53, 2.31), (28.0, 34.0, 40.0), (.4, .25, .15))
            crack += band(rng.uniform(-1, 1)) * math.exp(-t * 22) * .9
            toll = partials(t - .03, 98.0, (.5, 1.0, 1.005, 1.2, 2.0, 2.7), (.9, 1.2, 1.25, 2.0, 3.2, 4.4), (.45, .9, .6, .4, .3, .15))
            value = crack + toll
        elif name == "bell_ding.wav":
            # 回满：1318 Hz 的清亮小钟，尾音干净。
            value = partials(t, 1318.5, (1.0, 2.0, 2.76, 4.07), (3.0, 5.0, 7.0, 10.0), (1.0, .3, .25, .1))
        elif name == "ember_flare.wav":
            # 余烬：低沉的一阵火息 + 稀疏的噼啪。
            breath = lp2(rng.uniform(-1, 1)) * math.sin(math.pi * min(1.0, t / seconds)) * .9
            crackle = 0.0
            for start, weight in pops:
                u = t - start
                if 0 <= u < .02:
                    crackle += rng.uniform(-1, 1) * weight * math.exp(-u * 300)
            value = breath + crackle * .9
        elif name == "soul_rise.wav":
            # 魂火：气声上扬（带通噪声中心慢慢升高），叠两支轻微失谐的高音闪烁。
            env = math.sin(math.pi * min(1.0, t / seconds)) ** 1.2
            air = band(rng.uniform(-1, 1)) * env * .8
            glide = 660 + 520 * (t / seconds)
            phase += math.tau * glide / RATE
            shimmer = (math.sin(phase) + .6 * math.sin(phase * 1.503)) * env * (.5 + .5 * math.sin(math.tau * 6 * t))
            value = air + shimmer * .35
        else:
            value = 0.0
        values.append(value)
    values = [v * min(1, max(0, seconds - i / RATE) / .08) for i, v in enumerate(values)]
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
    print("INK_UI_SFX_OK", len(DURATIONS))


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
